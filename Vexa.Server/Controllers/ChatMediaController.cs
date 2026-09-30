using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.DTOs;
using NovaChat.Server.Services;
using System.Globalization;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatMediaController : ControllerBase
{
    private const long MaxImageBytes = 10 * 1024 * 1024;
    private const long MaxFileBytes = 25 * 1024 * 1024;
    private const long MaxVoiceBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImages = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    private static readonly HashSet<string> AllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".rar", ".7z", ".csv", ".json", ".mp4", ".mov", ".mkv", ".webm", ".exe"
    };

    private readonly ChatService _chatService;
    private readonly IWebHostEnvironment _environment;
    private readonly IHubContext<NovaChat.Server.Hubs.ChatHub> _hub;
    private readonly ExecutableFileSecurityService _executableSecurity;

    public ChatMediaController(
        ChatService chatService,
        IWebHostEnvironment environment,
        IHubContext<NovaChat.Server.Hubs.ChatHub> hub,
        ExecutableFileSecurityService executableSecurity)
    {
        _chatService = chatService;
        _environment = environment;
        _hub = hub;
        _executableSecurity = executableSecurity;
    }

    [HttpPost("{chatId}")]
    [RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> Upload(
        int chatId,
        IFormFile file,
        [FromQuery] string type = "file",
        [FromQuery] string? durationSeconds = null)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null || !await _chatService.CanAccessChatAsync(chatId, userId.Value))
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Please select a file." });

        type = type.Trim().ToLowerInvariant();
        if (type is not ("image" or "file" or "voice"))
            return BadRequest(new { message = "Invalid media type." });

        var extension = Path.GetExtension(file.FileName);
        if (type == "image" && !AllowedImages.Contains(extension))
            return BadRequest(new { message = "Unsupported image type." });
        if (type == "file" && !AllowedFiles.Contains(extension))
            return BadRequest(new { message = "Unsupported file type." });
        if (type == "voice" && !string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Voice messages must be WAV audio." });

        var maxBytes = type switch
        {
            "image" => MaxImageBytes,
            "voice" => MaxVoiceBytes,
            _ => MaxFileBytes
        };

        if (file.Length > maxBytes)
            return BadRequest(new { message = $"This {type} is too large." });

        double? parsedDuration = null;
        if (type == "voice" && !string.IsNullOrWhiteSpace(durationSeconds))
        {
            if (!double.TryParse(durationSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                !double.TryParse(durationSeconds, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return BadRequest(new { message = "Invalid voice duration." });
            }

            parsedDuration = Math.Max(0, value);
        }

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var isExecutable = type == "file" &&
                           string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase);

        var storageRoot = isExecutable
            ? Path.Combine(_environment.ContentRootPath, "secure-uploads", "chat")
            : Path.Combine(root, "uploads", "chat");

        var folder = Path.Combine(storageRoot, type);
        Directory.CreateDirectory(folder);

        var storageName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var finalPath = Path.Combine(folder, storageName);

        string? quarantinePath = null;
        ExecutableValidationResult? executableValidation = null;

        try
        {
            if (isExecutable)
            {
                var quarantineFolder = Path.Combine(
                    _environment.ContentRootPath,
                    "secure-uploads",
                    "quarantine");

                Directory.CreateDirectory(quarantineFolder);

                quarantinePath = Path.Combine(
                    quarantineFolder,
                    $"{Guid.NewGuid():N}.upload");

                await using (var stream = System.IO.File.Create(quarantinePath))
                    await file.CopyToAsync(stream, HttpContext.RequestAborted);

                executableValidation = await _executableSecurity.ValidateAsync(
                    quarantinePath,
                    HttpContext.RequestAborted);

                if (!executableValidation.IsAccepted)
                {
                    TryDeleteFile(quarantinePath);
                    quarantinePath = null;
                    return BadRequest(new { message = executableValidation.Message });
                }

                System.IO.File.Move(quarantinePath, finalPath);
                quarantinePath = null;
            }
            else
            {
                await using var stream = System.IO.File.Create(finalPath);
                await file.CopyToAsync(stream);
            }

            var contentType = type switch
            {
                "image" => string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                "voice" => "audio/wav",
                _ => string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType
            };

            var envelope = new MediaMessageEnvelope
            {
                Type = type,
                StorageName = $"{type}/{storageName}",
                FileName = Path.GetFileName(file.FileName),
                ContentType = contentType,
                Size = file.Length,
                DurationSeconds = parsedDuration,
                Sha256 = executableValidation?.Sha256
            };

            // Media deliberately stays outside E2EE. Text messages remain E2EE,
            // while media is stored separately and only safe metadata is saved in the message.
            var message = await _chatService.SendMessageAsync(chatId, userId.Value, envelope.Serialize());
            if (message == null)
            {
                System.IO.File.Delete(finalPath);
                return BadRequest(new { message = "Unable to create media message." });
            }

            var dto = MessageDtoMapper.Map(message);
            var recipients = chat.ChatMembers.Select(m => m.UserId.ToString()).Distinct(StringComparer.Ordinal);
            await _hub.Clients.Users(recipients).SendAsync("ReceiveMessage", dto);

            return Ok(new
            {
                message = isExecutable
                    ? $"Verified executable sent successfully using {executableValidation!.Scanner}."
                    : "Media sent successfully.",
                data = dto
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            TryDeleteFile(quarantinePath);
            TryDeleteFile(finalPath);
            throw;
        }
    }

    [HttpGet("{messageId}")]
    public async Task<IActionResult> Get(int messageId)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null || !MediaMessageEnvelope.TryParse(message.Content, out var media) || media == null)
            return NotFound();

        if (!await _chatService.CanAccessChatAsync(message.ChatId, userId.Value))
            return Forbid();

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var isExecutable = media.Type == "file" &&
                           string.Equals(
                               Path.GetExtension(media.FileName),
                               ".exe",
                               StringComparison.OrdinalIgnoreCase);

        var uploadRoot = isExecutable
            ? Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "secure-uploads", "chat"))
            : Path.GetFullPath(Path.Combine(root, "uploads", "chat"));
        var path = Path.GetFullPath(Path.Combine(uploadRoot, media.StorageName.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = uploadRoot.EndsWith(Path.DirectorySeparatorChar)
            ? uploadRoot
            : uploadRoot + Path.DirectorySeparatorChar;

        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path))
            return NotFound();

        Response.Headers.ContentDisposition = $"inline; filename=\"{Uri.EscapeDataString(media.FileName)}\"";
        Response.Headers.CacheControl = "private, max-age=3600";
        return PhysicalFile(path, media.ContentType, enableRangeProcessing: true);
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch
        {
        }
    }

    private long? CurrentUserId() =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) && userId > 0
            ? userId
            : null;
}

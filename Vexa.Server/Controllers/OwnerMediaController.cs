using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.Services;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "OwnerOnly")]
public sealed class OwnerMediaController(AppDbContext db, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("{chatId:int}/{messageId:int}")]
    public async Task<IActionResult> Get(int chatId, int messageId)
    {
        var message = await db.Messages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ChatId == chatId && !m.DeletedForEveryone);

        if (message == null)
            return NotFound(new { message = "Media message not found." });

        if (!MediaMessageEnvelope.TryParse(message.Content, out var media) || media == null)
            return NotFound(new { message = "The selected message does not contain supported legacy media." });

        var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var uploadRoot = Path.GetFullPath(Path.Combine(root, "uploads", "chat"));
        var path = Path.GetFullPath(Path.Combine(uploadRoot, media.StorageName.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = uploadRoot.EndsWith(Path.DirectorySeparatorChar)
            ? uploadRoot
            : uploadRoot + Path.DirectorySeparatorChar;

        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path))
            return NotFound(new { message = "Media file not found on the server." });

        Response.Headers.ContentDisposition = $"inline; filename=\"{Uri.EscapeDataString(Path.GetFileName(media.FileName))}\"";
        Response.Headers.CacheControl = "private, max-age=3600";
        return PhysicalFile(path,
            string.IsNullOrWhiteSpace(media.ContentType) ? "application/octet-stream" : media.ContentType,
            enableRangeProcessing: true);
    }
}

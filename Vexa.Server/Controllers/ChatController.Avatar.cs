using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

public partial class ChatController
{
    [HttpGet("{chatId}/avatar")]
    public async Task<IActionResult> GetGroupAvatar(int chatId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized();

        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null || chat.Type != NovaChat.Server.Entities.ChatType.Group)
            return NotFound();

        if (!await _chatService.CanAccessChatAsync(chatId, userId))
            return Forbid();

        if (string.IsNullOrWhiteSpace(chat.AvatarUrl))
            return NotFound();

        var fileName = Path.GetFileName(chat.AvatarUrl);
        if (string.IsNullOrWhiteSpace(fileName))
            return NotFound();

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var path = Path.Combine(root, "uploads", "groups", fileName);
        if (!System.IO.File.Exists(path))
            return NotFound();

        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers.ContentDisposition = "inline";
        await Task.CompletedTask;
        return PhysicalFile(path, "image/jpeg", enableRangeProcessing: true);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/message-read")]
[Authorize]
public sealed class MessageReadController(ChatService chatService, MessageReadService messageReadService) : ControllerBase
{
    [HttpGet("unread")]
    public async Task<IActionResult> GetUnreadCounts(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await messageReadService.GetUnreadCountsAsync(userId, cancellationToken));
    }

    [HttpGet("{chatId}/sent")]
    public async Task<IActionResult> GetSentMessageReadStates(int chatId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!await chatService.CanAccessChatAsync(chatId, userId)) return Forbid();
        var ids = await messageReadService.GetReadMessageIdsForSenderAsync(chatId, userId, cancellationToken);
        return Ok(new { chatId, messageIds = ids });
    }

    [HttpPost("{chatId}/read")]
    public async Task<IActionResult> MarkRead(int chatId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!await chatService.CanAccessChatAsync(chatId, userId)) return Forbid();
        var ids = await messageReadService.MarkChatAsReadAsync(chatId, userId, cancellationToken);
        return Ok(new { chatId, messageIds = ids });
    }

    private bool TryGetUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}

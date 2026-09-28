using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.Entities;
using NovaChat.Server.Hubs;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class GroupManagementController : ControllerBase
{
    private readonly ChatService _chatService;
    private readonly IHubContext<ChatHub> _hub;

    public GroupManagementController(ChatService chatService, IHubContext<ChatHub> hub)
    {
        _chatService = chatService;
        _hub = hub;
    }

    [HttpPost("{chatId:int}/leave")]
    public async Task<IActionResult> LeaveGroup(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!await _chatService.LeaveGroupAsync(chatId, userId))
            return BadRequest(new { message = "You cannot leave this group." });

        await _hub.Clients.User(userId.ToString()).SendAsync("ChatMemberRemoved", new { chatId, userId = userId.ToString() });
        return Ok(new { message = "You left the group." });
    }

    [HttpDelete("{chatId:int}")]
    public async Task<IActionResult> DeleteGroup(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null || chat.Type != ChatType.Group) return NotFound(new { message = "Group not found." });
        if (chat.CreatedByUserId != userId) return Forbid();

        var recipients = chat.ChatMembers.Select(m => m.UserId.ToString()).Distinct().ToList();
        if (!await _chatService.DeleteChatAsync(chatId)) return NotFound();
        await _hub.Clients.Users(recipients).SendAsync("ChatDeleted", new { chatId, deletedBy = userId.ToString() });
        return Ok(new { message = "Group deleted successfully." });
    }

    private bool TryGetCurrentUserId(out long userId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}

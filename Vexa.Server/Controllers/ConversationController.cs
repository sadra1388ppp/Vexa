using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.Entities;
using NovaChat.Server.Hubs;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ConversationController : ControllerBase
{
    private readonly AppDbContext _db; private readonly IHubContext<ChatHub> _hub;
    public ConversationController(AppDbContext db, IHubContext<ChatHub> hub) { _db = db; _hub = hub; }
    [HttpDelete("{chatId:int}")]
    public async Task<IActionResult> DeletePrivateConversation(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        var chat = await _db.Chats.Include(c => c.ChatMembers).AsTracking().FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat == null) return NotFound(new { message = "Conversation not found." });
        if (chat.Type != ChatType.Private) return BadRequest(new { message = "Group chats must be left or deleted from Group Info." });
        if (!chat.ChatMembers.Any(m => m.UserId == userId)) return Forbid();
        var recipients = chat.ChatMembers.Select(m => m.UserId.ToString()).Distinct(StringComparer.Ordinal).ToList();
        _db.Chats.Remove(chat); await _db.SaveChangesAsync();
        await _hub.Clients.Users(recipients).SendAsync("ChatDeleted", new { chatId = chat.Id, deletedBy = userId.ToString() });
        return Ok(new { message = "Private conversation permanently deleted." });
    }
    private bool TryGetCurrentUserId(out long userId) { var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"); return long.TryParse(claim, out userId); }
}

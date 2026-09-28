using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.DTOs;
using NovaChat.Server.Hubs;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/ChatRequests")]
public sealed class ChatRequestController(ChatRequestService requests, ChatService chats, IHubContext<ChatHub> hub) : ControllerBase
{
    private readonly ChatRequestService _requests = requests;
    private readonly ChatService _chats = chats;
    private readonly IHubContext<ChatHub> _hub = hub;

    [HttpGet("incoming")]
    public async Task<IActionResult> Incoming(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _requests.GetIncomingAsync(userId, cancellationToken));
    }

    [HttpGet("outgoing")]
    public async Task<IActionResult> Outgoing(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _requests.GetOutgoingAsync(userId, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateChatRequestDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await _requests.CreateAsync(userId, dto.Username, cancellationToken);
        if (!result.Success) return BadRequest(new { message = result.Message });
        if (result.Request == null) return Ok(new { message = result.Message, chat = (object?)null, requestPending = false });
        var recipient = await _chats.GetUserByUsernameAsync(result.Request.TargetUsername);
        if (recipient != null)
            await _hub.Clients.User(recipient.Id.ToString()).SendAsync("ChatRequestReceived", result.Request, cancellationToken);
        return Ok(new { message = result.Message, requestPending = true, requestId = result.Request.Id, chat = (object?)null });
    }

    [HttpPost("{requestId:long}/accept")]
    public async Task<IActionResult> Accept(long requestId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await _requests.AcceptAsync(requestId, userId, cancellationToken);
        if (!result.Success) return BadRequest(new { message = result.Message });
        if (result.Chat == null) return Problem(statusCode: 500, title: "Chat request accepted but the conversation was not returned.");

        var chat = result.Chat;
        await _hub.Clients.Users(new[]
        {
            chat.User1Id?.ToString() ?? string.Empty,
            chat.User2Id?.ToString() ?? string.Empty
        }.Where(x => !string.IsNullOrWhiteSpace(x))).SendAsync("ChatCreated", new
        {
            Id = chat.Id,
            Type = chat.Type,
            Name = "",
            CreatedByUserId = chat.CreatedByUserId?.ToString() ?? "",
            User1Id = chat.User1Id?.ToString() ?? "",
            User2Id = chat.User2Id?.ToString() ?? "",
            User1Name = chat.User1?.DisplayName ?? "",
            User2Name = chat.User2?.DisplayName ?? "",
            CreatedAt = chat.CreatedAt
        }, cancellationToken);
        return Ok(new { message = result.Message, request = result.Request, chat = new { chatId = chat.Id } });
    }

    [HttpPost("{requestId:long}/reject")]
    public async Task<IActionResult> Reject(long requestId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await _requests.RejectAsync(requestId, userId, cancellationToken);
        if (!result.Success) return BadRequest(new { message = result.Message });
        if (result.Request != null)
            await _hub.Clients.User(result.Request.RequesterUserId).SendAsync("ChatRequestRejected", result.Request, cancellationToken);
        return Ok(new { message = result.Message, request = result.Request });
    }

    private bool TryGetUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}
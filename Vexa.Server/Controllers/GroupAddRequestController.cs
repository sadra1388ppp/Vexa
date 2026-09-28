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
[Route("api/GroupAddRequests")]
public sealed class GroupAddRequestController(GroupAddRequestService requests, IHubContext<ChatHub> hub) : ControllerBase
{
    private readonly GroupAddRequestService _requests = requests;
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

    [HttpPost("{groupId:int}")]
    public async Task<IActionResult> Create(int groupId, CreateGroupAddRequestDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Username)) return BadRequest(new { message = "Username is required." });
        var result = await _requests.CreateAsync(groupId, userId, dto.Username, cancellationToken);
        if (!result.Success) return BadRequest(new { message = result.Message });
        if (result.Request != null)
            await _hub.Clients.User(result.Request.TargetUserId).SendAsync("GroupAddRequestReceived", result.Request, cancellationToken);
        return Ok(new { message = result.Message, requestPending = !result.AddedImmediately, request = result.Request });
    }

    [HttpPost("{requestId:long}/accept")]
    public async Task<IActionResult> Accept(long requestId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await _requests.AcceptAsync(requestId, userId, cancellationToken);
        if (!result.Success) return BadRequest(new { message = result.Message });
        if (result.Request != null)
            await _hub.Clients.User(result.Request.RequesterUserId).SendAsync("GroupAddRequestAccepted", result.Request, cancellationToken);
        return Ok(new { message = result.Message, request = result.Request, groupId = result.GroupId });
    }

    [HttpPost("{requestId:long}/reject")]
    public async Task<IActionResult> Reject(long requestId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await _requests.RejectAsync(requestId, userId, cancellationToken);
        if (!result.Success) return BadRequest(new { message = result.Message });
        if (result.Request != null)
            await _hub.Clients.User(result.Request.RequesterUserId).SendAsync("GroupAddRequestRejected", result.Request, cancellationToken);
        return Ok(new { message = result.Message, request = result.Request });
    }

    private bool TryGetUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}
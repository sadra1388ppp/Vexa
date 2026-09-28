using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/e2ee")]
public sealed class E2eeController(E2eeDeviceService devices) : ControllerBase
{
    private readonly E2eeDeviceService _devices = devices;

    [HttpPost("devices")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterE2eeDeviceRequest request, CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized();
        if (request == null || string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 64 || string.IsNullOrWhiteSpace(request.PublicKeyPem))
            return BadRequest(new { message = "A valid device id and public key are required." });
        if (request.PublicKeyPem.Length > 12000)
            return BadRequest(new { message = "Public key is too large." });

        await _devices.UpsertAsync(userId, request.DeviceId.Trim(), request.PublicKeyPem.Trim(), cancellationToken);
        return Ok(new { deviceId = request.DeviceId.Trim() });
    }

    [HttpGet("chats/{chatId:int}/devices")]
    public async Task<IActionResult> GetChatDevices(int chatId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized();
        var devices = await _devices.GetChatDevicesAsync(chatId, userId, cancellationToken);
        if (devices.Count == 0) return Ok(Array.Empty<E2eeDeviceResponse>());
        return Ok(devices.Select(x => new E2eeDeviceResponse
        {
            DeviceId = x.DeviceId,
            UserId = x.UserId.ToString(),
            PublicKeyPem = x.PublicKeyPem
        }).ToList());
    }

    [HttpDelete("devices/{deviceId}")]
    public async Task<IActionResult> RevokeDevice(string deviceId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(deviceId)) return BadRequest();
        await _devices.RevokeAsync(userId, deviceId.Trim(), cancellationToken);
        return NoContent();
    }

    public sealed class RegisterE2eeDeviceRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
    }

    public sealed class E2eeDeviceResponse
    {
        public string DeviceId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
    }
}

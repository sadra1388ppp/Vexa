using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.DTOs;
using NovaChat.Server.Hubs;
using NovaChat.Server.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private const long MaxAvatarBytes = 5 * 1024 * 1024;
    private readonly UserService _userService; private readonly JwtService _jwtService; private readonly IWebHostEnvironment _environment; private readonly IHubContext<ChatHub> _hub;
    public UserController(UserService userService, JwtService jwtService, IWebHostEnvironment environment, IHubContext<ChatHub> hub) { _userService = userService; _jwtService = jwtService; _environment = environment; _hub = hub; }
    [AllowAnonymous, HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto) { var result = await _userService.RegisterAsync(dto); if (!result.Success) return Conflict(new { message = result.Message }); return Ok(new { message = result.Message, user = result.User }); }
    [AllowAnonymous, HttpGet("registration-availability")]
    public async Task<IActionResult> RegistrationAvailability([FromQuery] string? username, [FromQuery] string? email, [FromQuery] string? phoneNumber) { var result = await _userService.CheckRegistrationAvailabilityAsync(username, email, phoneNumber); return Ok(new { usernameTaken = result.UsernameTaken, emailTaken = result.EmailTaken, phoneTaken = result.PhoneTaken }); }
    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto) { var user = await _userService.LoginAsync(dto); if (user == null) return Unauthorized(new { message = "Invalid username/phone number or password." }); var token = _jwtService.GenerateToken(user); var id = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture); return Ok(new { message = "Login successful.", token, user = await _userService.GetUserByIdAsync(id) }); }
    [Authorize, HttpGet("profile/me")]
    public async Task<IActionResult> GetMyProfile() { var id = CurrentUserId(); if (id == null) return Unauthorized(); var user = await _userService.GetUserByIdAsync(id, true); return user == null ? NotFound(new { message = "User not found." }) : Ok(user); }
    [Authorize, HttpGet("profile/{id}")]
    public async Task<IActionResult> GetPublicProfile(string id) { var user = await _userService.GetUserByIdAsync(id); return user == null ? NotFound(new { message = "User not found." }) : Ok(user); }
    [Authorize, HttpGet("profile/{id}/avatar")]
    public async Task<IActionResult> GetAvatar(string id) { var user = await _userService.GetUserByIdAsync(id); if (user == null || string.IsNullOrWhiteSpace(user.AvatarUrl)) return NotFound(); var fileName = Path.GetFileName(user.AvatarUrl); if (string.IsNullOrWhiteSpace(fileName)) return NotFound(); var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"); var path = Path.Combine(root, "uploads", "avatars", fileName); if (!System.IO.File.Exists(path)) return NotFound(); Response.Headers.CacheControl = "no-cache, no-store, must-revalidate"; Response.Headers.Pragma = "no-cache"; Response.Headers.Expires = "0"; Response.Headers.ContentDisposition = "inline"; return PhysicalFile(path, "image/jpeg", enableRangeProcessing: true); }
    [Authorize, HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q) { var id = CurrentUserId(); if (id == null) return Unauthorized(); return Ok(string.IsNullOrWhiteSpace(q) ? Array.Empty<UserResponseDto>() : await _userService.SearchUsersAsync(q, id)); }
    [Authorize, HttpGet("{id}")]
    public async Task<IActionResult> GetUser(string id) { if (!IsOwner() && !IsCurrentUser(id)) return Forbid(); var user = await _userService.GetUserByIdAsync(id, IsCurrentUser(id) || IsOwner()); return user == null ? NotFound(new { message = "User not found." }) : Ok(user); }
    [Authorize, HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(string id, UpdateUserDto dto) { if (!IsOwner() && !IsCurrentUser(id)) return Forbid(); var result = await _userService.UpdateUserAsync(id, dto); if (!result.Success) return result.Message == "User not found." ? NotFound(new { message = result.Message }) : Conflict(new { message = result.Message }); await _hub.Clients.All.SendAsync("ProfileUpdated", new { userId = id, username = result.User?.Username ?? string.Empty, displayName = result.User?.DisplayName ?? string.Empty, bio = result.User?.Bio ?? string.Empty, avatarUrl = result.User?.AvatarUrl }); return Ok(new { message = result.Message, user = result.User }); }
    [Authorize, HttpPost("{id}/avatar"), RequestSizeLimit(MaxAvatarBytes)]
    public async Task<IActionResult> UploadAvatar(string id, IFormFile file) { if (!IsCurrentUser(id)) return Forbid(); if (file == null || file.Length == 0) return BadRequest(new { message = "Please select an image." }); if (file.Length > MaxAvatarBytes) return BadRequest(new { message = "Profile picture must be 5 MB or smaller." }); var allowed = new[] { "image/jpeg", "image/png", "image/webp" }; if (!allowed.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase)) return BadRequest(new { message = "Only JPG, PNG and WebP images are supported." }); try { await using var input = file.OpenReadStream(); using var image = await Image.LoadAsync(input); if (image.Width < 64 || image.Height < 64) return BadRequest(new { message = "Image must be at least 64x64 pixels." }); var dir = Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), "uploads", "avatars"); Directory.CreateDirectory(dir); var fileName = $"{Guid.NewGuid():N}.jpg"; var fullPath = Path.Combine(dir, fileName); image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(512, 512), Mode = ResizeMode.Crop })); await image.SaveAsJpegAsync(fullPath, new JpegEncoder { Quality = 88 }); var old = await _userService.GetUserByIdAsync(id); var result = await _userService.SetAvatarAsync(id, $"/uploads/avatars/{fileName}"); if (!result.Success) return NotFound(new { message = result.Message }); DeleteStoredAvatar(old?.AvatarUrl); var refreshed = await _userService.GetUserByIdAsync(id, true); await _hub.Clients.All.SendAsync("ProfileUpdated", new { userId = id, username = refreshed?.Username ?? string.Empty, displayName = refreshed?.DisplayName ?? string.Empty, bio = refreshed?.Bio ?? string.Empty, avatarUrl = refreshed?.AvatarUrl }); return Ok(new { message = result.Message, user = refreshed }); } catch (UnknownImageFormatException) { return BadRequest(new { message = "The uploaded file is not a valid image." }); } }
    [Authorize, HttpDelete("{id}/avatar")]
    public async Task<IActionResult> DeleteAvatar(string id) { if (!IsCurrentUser(id)) return Forbid(); var result = await _userService.ClearAvatarAsync(id); if (!result.Success) return NotFound(new { message = result.Message }); DeleteStoredAvatar(result.OldAvatarUrl); var user = await _userService.GetUserByIdAsync(id, true); await _hub.Clients.All.SendAsync("ProfileUpdated", new { userId = id, username = user?.Username ?? string.Empty, displayName = user?.DisplayName ?? string.Empty, bio = user?.Bio ?? string.Empty, avatarUrl = (string?)null }); return Ok(new { message = result.Message, user }); }
    [Authorize, HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id) { if (!IsOwner() && !IsCurrentUser(id)) return Forbid(); var deleted = await _userService.DeleteUserAsync(id); return deleted ? Ok(new { message = "User deleted successfully." }) : NotFound(new { message = "User not found." }); }
    [Authorize, HttpPut("{id}/password")]
    public async Task<IActionResult> ChangePassword(string id, ChangePasswordDto dto) { if (!IsOwner() && !IsCurrentUser(id)) return Forbid(); var result = await _userService.ChangePasswordAsync(id, dto); if (!result.Success) return result.Message == "User not found." ? NotFound(new { message = result.Message }) : BadRequest(new { message = result.Message }); return Ok(new { message = result.Message }); }
    private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsCurrentUser(string id) => string.Equals(CurrentUserId(), id, StringComparison.Ordinal);
    private bool IsOwner() { var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>(); var owner = config["Owner:Username"] ?? config["Owner:UserId"]; var username = User.FindFirst("username")?.Value; if (!string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(username) && string.Equals(username, owner, StringComparison.OrdinalIgnoreCase)) return true; return !string.IsNullOrWhiteSpace(owner) && string.Equals(CurrentUserId(), owner, StringComparison.Ordinal); }
    private void DeleteStoredAvatar(string? avatarUrl) { if (string.IsNullOrWhiteSpace(avatarUrl)) return; var fileName = Path.GetFileName(avatarUrl); if (string.IsNullOrWhiteSpace(fileName)) return; var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"); var path = Path.Combine(root, "uploads", "avatars", fileName); if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.Services;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/avatar")]
public class AvatarController : ControllerBase
{
    private readonly UserService _userService;
    private readonly IWebHostEnvironment _environment;

    public AvatarController(UserService userService, IWebHostEnvironment environment)
    {
        _userService = userService;
        _environment = environment;
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null || string.IsNullOrWhiteSpace(user.AvatarUrl)) return NotFound();

        var fileName = Path.GetFileName(user.AvatarUrl);
        if (string.IsNullOrWhiteSpace(fileName)) return NotFound();

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var path = Path.Combine(root, "uploads", "avatars", fileName);
        if (!System.IO.File.Exists(path)) return NotFound();

        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        if (bytes.Length == 0) return NotFound();

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers.ContentDisposition = "inline";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return File(bytes, "image/jpeg");
    }
}

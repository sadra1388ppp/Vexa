using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/Privacy")]
public sealed class PrivacyController(AppDbContext db) : ControllerBase
{
    private readonly AppDbContext _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user == null) return NotFound(new { message = "User not found." });

        return Ok(new
        {
            messagePrivacy = NormalizeMessagePrivacy(user.MessagePrivacy),
            allowGroupAdds = user.AllowGroupAdds
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdatePrivacyRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user == null) return NotFound(new { message = "User not found." });

        user.MessagePrivacy = NormalizeMessagePrivacy(request.MessagePrivacy);
        user.AllowGroupAdds = request.AllowGroupAdds;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Privacy settings updated.",
            messagePrivacy = user.MessagePrivacy,
            allowGroupAdds = user.AllowGroupAdds
        });
    }

    private static string NormalizeMessagePrivacy(string? value) =>
        string.Equals(value?.Trim(), "Requests", StringComparison.OrdinalIgnoreCase) ? "Requests" : "Everybody";

    private bool TryGetUserId(out long userId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;

    public sealed class UpdatePrivacyRequest
    {
        public string MessagePrivacy { get; set; } = "Everybody";
        public bool AllowGroupAdds { get; set; } = true;
    }
}

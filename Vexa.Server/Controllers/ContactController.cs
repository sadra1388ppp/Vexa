using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.DTOs;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ContactController : ControllerBase
{
    private readonly ContactService _contactService;

    public ContactController(ContactService contactService)
    {
        _contactService = contactService;
    }

    [HttpGet]
    public async Task<IActionResult> GetContacts()
    {
        var userId = CurrentUserId();
        if (userId == null)
            return Unauthorized();

        return Ok(await _contactService.GetAllAsync(userId));
    }

    [HttpGet("group-candidates")]
    public async Task<IActionResult> GetGroupCandidates()
    {
        var userId = CurrentUserId();
        if (userId == null)
            return Unauthorized();

        return Ok(await _contactService.GetGroupCandidatesAsync(userId));
    }

    [HttpPost]
    public async Task<IActionResult> AddContact(AddContactDto dto)
    {
        var userId = CurrentUserId();
        if (userId == null)
            return Unauthorized();

        var result = await _contactService.AddAsync(userId, dto.UserId);

        if (!result.Success)
            return BadRequest(new { message = result.Message });

        return Ok(new { message = result.Message });
    }

    [HttpDelete("{contactUserId}")]
    public async Task<IActionResult> RemoveContact(string contactUserId)
    {
        var userId = CurrentUserId();
        if (userId == null)
            return Unauthorized();

        var removed = await _contactService.RemoveAsync(userId, contactUserId);

        if (!removed)
            return NotFound(new { message = "Contact not found." });

        return Ok(new { message = "Contact removed successfully." });
    }

    private string? CurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}

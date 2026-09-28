using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace NovaChat.Server.Authorization;

public class OwnerAuthorizationHandler : AuthorizationHandler<OwnerRequirement>
{
    private readonly IConfiguration _configuration;

    public OwnerAuthorizationHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerRequirement requirement)
    {
        var currentUsername = context.User.FindFirst("username")?.Value;
        var configuredOwnerUsername = _configuration["Owner:Username"];

        if (!string.IsNullOrWhiteSpace(currentUsername) &&
            !string.IsNullOrWhiteSpace(configuredOwnerUsername) &&
            string.Equals(currentUsername, configuredOwnerUsername, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Backward compatibility for installations that still configure Owner:UserId.
        var currentUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var configuredOwnerId = _configuration["Owner:UserId"];

        if (!string.IsNullOrWhiteSpace(currentUserId) &&
            !string.IsNullOrWhiteSpace(configuredOwnerId) &&
            string.Equals(currentUserId, configuredOwnerId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
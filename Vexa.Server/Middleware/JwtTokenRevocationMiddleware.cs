using Microsoft.AspNetCore.Http;
using NovaChat.Server.Services;

namespace NovaChat.Server.Middleware;

public sealed class JwtTokenRevocationMiddleware
{
    private readonly RequestDelegate _next;

    public JwtTokenRevocationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        JwtTokenRevocationService revocationService)
    {
        var token = GetAccessToken(context);

        if (!string.IsNullOrWhiteSpace(token) && revocationService.IsRevoked(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "This session has been logged out."
            });
            return;
        }

        await _next(context);
    }

    private static string? GetAccessToken(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();

        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization["Bearer ".Length..].Trim();

        if (context.Request.Path.StartsWithSegments("/hubs/chat"))
        {
            var accessToken = context.Request.Query["access_token"].ToString();

            if (!string.IsNullOrWhiteSpace(accessToken))
                return accessToken;
        }

        return null;
    }
}

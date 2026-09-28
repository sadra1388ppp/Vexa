using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace NovaChat.Server.Services;

public sealed class ChatPrivacyMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context, ChatService chats, ChatRequestService requests)
    {
        if (!HttpMethods.IsPost(context.Request.Method) ||
            !string.Equals(context.Request.Path.Value, "/api/Chat", StringComparison.OrdinalIgnoreCase) ||
            context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var claim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(claim, out var requesterId) || requesterId <= 0)
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;
        if (string.IsNullOrWhiteSpace(body))
        {
            await _next(context);
            return;
        }

        string? username;
        try
        {
            using var document = JsonDocument.Parse(body);
            username = document.RootElement.TryGetProperty("username", out var property)
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            await _next(context);
            return;
        }

        var target = await chats.GetUserByUsernameAsync(username);
        if (target == null || target.Id == requesterId ||
            !string.Equals(target.MessagePrivacy, "Requests", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var existing = await chats.FindPrivateChatAsync(requesterId, target.Id);
        if (existing != null)
        {
            await _next(context);
            return;
        }

        var result = await requests.CreateAsync(requesterId, target.Username, context.RequestAborted);
        if (!result.Success)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = result.Message }, context.RequestAborted);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(new
        {
            message = result.Message,
            chat = (object?)null,
            requestPending = true,
            requestId = result.Request?.Id
        }, context.RequestAborted);
    }
}

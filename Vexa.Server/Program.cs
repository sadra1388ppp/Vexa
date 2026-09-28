using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NovaChat.Server.Authorization;
using NovaChat.Server.Data;
using NovaChat.Server.Hubs;
using NovaChat.Server.Middleware;
using NovaChat.Server.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(options => options.UseVexaDatabase(builder.Configuration));
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddSingleton<PasswordHashService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddSingleton<JwtTokenRevocationService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<ChatRequestService>();
builder.Services.AddScoped<GroupAddRequestService>();
builder.Services.AddScoped<MessageReadService>();
builder.Services.AddScoped<E2eeDeviceService>();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<IAuthorizationHandler, OwnerAuthorizationHandler>();
builder.Services.AddAuthorization(options => options.AddPolicy("OwnerOnly", policy => { policy.RequireAuthenticatedUser(); policy.AddRequirements(new OwnerRequirement()); }));
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey)) throw new InvalidOperationException("JWT Key is not configured.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"], ValidateLifetime = true, ClockSkew = TimeSpan.Zero };
    options.Events = new JwtBearerEvents { OnMessageReceived = context => { var accessToken = context.Request.Query["access_token"]; var path = context.HttpContext.Request.Path; if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat")) context.Token = accessToken; return Task.CompletedTask; } };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Description = "Enter your JWT token." });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
});

var app = builder.Build();
try
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().ValidateSchemaAsync();
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    app.Logger.LogCritical(exception, "Vexa server startup database validation failed. The HTTP listener was not started.");
    throw;
}

var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "avatars"));
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseMiddleware<JwtTokenRevocationMiddleware>();
app.UseAuthorization();
app.UseMiddleware<ChatPrivacyMiddleware>();
app.MapControllers();

app.MapPost("/api/User/logout", (
    HttpContext context,
    JwtTokenRevocationService revocationService) =>
{
    var authorization = context.Request.Headers.Authorization.ToString();

    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();

    var token = authorization["Bearer ".Length..].Trim();

    if (string.IsNullOrWhiteSpace(token))
        return Results.Unauthorized();

    revocationService.Revoke(token);

    return Results.Ok(new
    {
        message = "Logged out successfully."
    });
}).RequireAuthorization();

app.MapHub<ChatHub>("/hubs/chat");
app.Run();

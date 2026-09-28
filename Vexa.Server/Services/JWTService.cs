using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class JwtService
{
    private readonly IConfiguration _configuration;
    public JwtService(IConfiguration configuration) => _configuration = configuration;

    public string GenerateToken(User user)
    {
        var jwtKey = _configuration["Jwt:Key"]; var issuer = _configuration["Jwt:Issuer"]; var audience = _configuration["Jwt:Audience"];
        if (string.IsNullOrWhiteSpace(jwtKey)) throw new InvalidOperationException("JWT Key is not configured.");
        if (string.IsNullOrWhiteSpace(issuer)) throw new InvalidOperationException("JWT Issuer is not configured.");
        if (string.IsNullOrWhiteSpace(audience)) throw new InvalidOperationException("JWT Audience is not configured.");
        var id = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var expirationMinutes = _configuration.GetValue<int>("Jwt:ExpirationMinutes");
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, id), new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, user.DisplayName), new("username", user.Username), new(JwtRegisteredClaimNames.Email, user.Email) };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer: issuer, audience: audience, claims: claims, expires: DateTime.UtcNow.AddMinutes(expirationMinutes), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

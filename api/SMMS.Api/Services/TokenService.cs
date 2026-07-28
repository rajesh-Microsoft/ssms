using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SMMS.Api.Models;

namespace SMMS.Api.Services;

public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SMMS.Api";
    public string Audience { get; set; } = "SMMS.Client";
    public int ExpiryMinutes { get; set; } = 120;
}

public class TokenService(JwtSettings settings)
{
    public string CreateToken(User user, string tenantKey, string? impersonatedBy = null, int? expiryMinutesOverride = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("tenant", tenantKey),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // When a super-admin is impersonating a society admin, record who is behind the session.
        if (!string.IsNullOrEmpty(impersonatedBy))
            claims.Add(new Claim("imp", impersonatedBy));

        foreach (var (module, level) in PermissionHelper.Parse(user.Permissions))
            claims.Add(new Claim($"perm:{module}", level));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutesOverride ?? settings.ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

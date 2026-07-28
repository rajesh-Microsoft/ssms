using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SMMS.Api.Models.Control;

namespace SMMS.Api.Services;

/// <summary>Issues JWTs for super-admins (platform operators). These tokens carry a
/// "platform_role=SuperAdmin" claim and deliberately have NO "tenant" claim, so the
/// cross-tenant guard rejects them against any society subdomain — they are only valid
/// on the control-plane (admin) surface.</summary>
public class PlatformTokenService(JwtSettings settings)
{
    public string CreateToken(PlatformUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("platform_role", "SuperAdmin"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(settings.ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

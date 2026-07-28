using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data.Control;
using SMMS.Api.Models.Control;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/platform/auth")]
public class PlatformAuthController(ControlDbContext db, PlatformTokenService tokenService) : ControllerBase
{
    private static readonly PasswordHasher<PlatformUser> Hasher = new();

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(PlatformLoginRequest request)
    {
        var user = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
        if (user is null || !user.IsActive)
            return Unauthorized(new { message = "Invalid username or password." });

        var verify = Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid username or password." });

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var token = tokenService.CreateToken(user);
        return Ok(new PlatformLoginResponse(token, user.Username, user.DisplayName));
    }

    [HttpGet("me")]
    [Authorize(Policy = "SuperAdmin")]
    public IActionResult Me() => Ok(new
    {
        username = User.Identity?.Name,
        role = "SuperAdmin"
    });
}

public record PlatformLoginRequest(string Username, string Password);

public record PlatformLoginResponse(string Token, string Username, string? DisplayName);

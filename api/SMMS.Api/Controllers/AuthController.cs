using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(SmmsDbContext db, TokenService tokenService, AuditService audit, ITenantContext tenantContext,
    EffectivePermissionService permissions) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
        if (user is null)
            return Unauthorized(new { message = "Invalid username or password." });

        var status = user.Status.ToLowerInvariant();
        if (status == "pending")
            return Unauthorized(new { message = "Your account is awaiting Admin approval." });
        if (status != "active")
            return Unauthorized(new { message = "This account is inactive. Contact your admin." });

        var verify = Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid username or password." });

        var access = await permissions.ResolveAsync(user.Id);
        if (access is null) return Unauthorized(new { message = "This account is inactive. Contact your admin." });

        var token = tokenService.CreateToken(user, tenantContext.Current!.Key, access.Permissions);
        return Ok(new LoginResponse(token, user.Id, user.Username, user.Role,
            access.Permissions, user.MustChangePassword));
    }

    /// <summary>The society's flats, for the sign-up dropdown. Anonymous like sign-up itself, and
    /// scoped by the tenant resolved from the Host header; returns flat numbers only, no residents.</summary>
    [HttpGet("flats")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<FlatOptionDto>>> GetFlats()
    {
        var roster = await db.Members
            .Where(m => m.Status == "Active")
            .Select(m => new { m.Flat, m.Floor })
            .ToListAsync();

        var claimed = await db.Users
            .Where(u => u.Flat != null && u.Status.ToLower() != "inactive")
            .Select(u => u.Flat!)
            .ToListAsync();
        var claimedSet = claimed.Select(f => f.Trim().ToLowerInvariant()).ToHashSet();

        var flats = roster
            .Where(m => !string.IsNullOrWhiteSpace(m.Flat))
            .GroupBy(m => m.Flat.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new FlatOptionDto(g.Key, g.First().Floor, claimedSet.Contains(g.Key.ToLowerInvariant())))
            .OrderBy(f => int.TryParse(f.Flat, out var n) ? n : int.MaxValue)
            .ThenBy(f => f.Flat, StringComparer.OrdinalIgnoreCase);

        return Ok(flats);
    }

    [HttpPost("signup")]
    [AllowAnonymous]
    public async Task<IActionResult> Signup(SignupRequest request)
    {
        var exists = await db.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower());
        if (exists)
            return Conflict(new { message = "That username is already taken." });

        var flat = request.Flat.Trim();
        var flatKey = flat.ToLowerInvariant();

        var onRoster = await db.Members.AnyAsync(m => m.Status == "Active" && m.Flat.Trim().ToLower() == flatKey);
        if (!onRoster)
            return BadRequest(new { message = $"Flat {flat} is not on this society's flat list. Please contact your Admin." });

        if (await FlatAllocation.IsClaimedAsync(db, flat))
            return Conflict(new { message = $"An account is already registered for flat {flat}. Please contact your Admin if this isn't you." });

        var user = new User
        {
            Username = request.Username,
            Role = "Member",
            Status = "Pending",
            Name = request.Name,
            Email = request.Email,
            Mobile = request.Mobile,
            Flat = flat,
            Floor = request.Floor,
            SecurityQuestion = request.SecurityQuestion
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        user.SecurityAnswerHash = Hasher.HashPassword(user, request.SecurityAnswer.Trim().ToLowerInvariant());

        db.Users.Add(user);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Signup", $"New member sign-up request: {user.Username} ({request.Name}, Flat {request.Flat})");

        return Created(string.Empty, new { message = "Account request submitted! An Admin will review and activate it." });
    }

    [HttpGet("security-question")]
    [AllowAnonymous]
    public async Task<ActionResult<SecurityQuestionResponse>> GetSecurityQuestion([FromQuery] string username)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user is null || string.IsNullOrEmpty(user.SecurityQuestion))
            return NotFound(new { message = "No recovery option is set up for this account. Please contact your Admin." });

        return Ok(new SecurityQuestionResponse(user.SecurityQuestion));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
        if (user is null || string.IsNullOrEmpty(user.SecurityAnswerHash))
            return NotFound(new { message = "No recovery option is set up for this account. Please contact your Admin." });

        var verify = Hasher.VerifyHashedPassword(user, user.SecurityAnswerHash, request.SecurityAnswer.Trim().ToLowerInvariant());
        if (verify == PasswordVerificationResult.Failed)
            return BadRequest(new { message = "Incorrect answer." });

        user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Password Reset", $"Password self-reset via security question for user: {user.Username}");

        return Ok(new { message = "Password reset! You can now log in." });
    }
}

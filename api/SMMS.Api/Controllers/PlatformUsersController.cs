using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data.Control;
using SMMS.Api.Dtos.Control;
using SMMS.Api.Models.Control;
using SMMS.Api.Services.Control;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/platform/users")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformUsersController(ControlDbContext db, PlatformAuditService audit) : ControllerBase
{
    private static readonly PasswordHasher<PlatformUser> Hasher = new();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlatformUserDto>>> List()
    {
        var users = await db.PlatformUsers.AsNoTracking()
            .OrderBy(u => u.Username)
            .ToListAsync();
        return Ok(users.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<PlatformUserDto>> Create(CreatePlatformUserRequest req)
    {
        var username = (req.Username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest(new { message = "Username is required." });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });
        if (await db.PlatformUsers.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
            return BadRequest(new { message = $"User '{username}' already exists." });

        var user = new PlatformUser
        {
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? username : req.DisplayName.Trim(),
            IsActive = true
        };
        user.PasswordHash = Hasher.HashPassword(user, req.Password);
        db.PlatformUsers.Add(user);
        await db.SaveChangesAsync();

        await audit.LogAsync("PlatformUserCreate", "PlatformUser", user.Username,
            $"Created super-admin '{user.Username}'.");
        return Created($"/api/platform/users/{user.Id}", ToDto(user));
    }

    /// <summary>Activates/deactivates an operator. Guards against locking out the platform:
    /// you can't deactivate yourself or the last remaining active super-admin.</summary>
    [HttpPost("{id:int}/toggle")]
    public async Task<ActionResult<PlatformUserDto>> Toggle(int id)
    {
        var user = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        if (user.IsActive)
        {
            if (string.Equals(user.Username, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "You cannot deactivate your own account." });
            if (await db.PlatformUsers.CountAsync(u => u.IsActive) <= 1)
                return BadRequest(new { message = "At least one active super-admin is required." });
        }

        user.IsActive = !user.IsActive;
        await db.SaveChangesAsync();

        await audit.LogAsync(user.IsActive ? "PlatformUserActivate" : "PlatformUserDeactivate",
            "PlatformUser", user.Username,
            $"Super-admin '{user.Username}' set {(user.IsActive ? "active" : "inactive")}.");
        return Ok(ToDto(user));
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPlatformPasswordRequest req)
    {
        var user = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });

        user.PasswordHash = Hasher.HashPassword(user, req.NewPassword);
        await db.SaveChangesAsync();

        await audit.LogAsync("PlatformUserResetPassword", "PlatformUser", user.Username,
            $"Password reset for '{user.Username}'.");
        return Ok(new { message = "Password updated." });
    }

    private static PlatformUserDto ToDto(PlatformUser u) => new(
        u.Id, u.Username, u.DisplayName, u.IsActive, u.CreatedAt, u.LastLoginAt);
}

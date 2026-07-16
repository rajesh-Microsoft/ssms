using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    private static UserDto ToDto(User u) => new(u.Id, u.Username, u.Role, u.Email, u.Status);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAll()
    {
        var users = await db.Users.OrderBy(u => u.Username).ToListAsync();
        return Ok(users.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(UserCreateRequest request)
    {
        var exists = await db.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower());
        if (exists) return Conflict(new { message = "Username already exists." });

        var user = new User
        {
            Username = request.Username,
            Role = request.Role,
            Status = request.Status,
            Email = request.Email
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Add", $"Added user: {user.Username}");
        return CreatedAtAction(nameof(GetAll), new { }, ToDto(user));
    }

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.Status = "Active";
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Approve", $"Approved user account: {user.Username}");
        return NoContent();
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetUserPasswordRequest request)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Reset Password", $"Password reset for user: {user.Username}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await db.Users.CountAsync() <= 1)
            return BadRequest(new { message = "Cannot delete last user." });

        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Delete", $"Deleted user id: {id}");
        return NoContent();
    }
}

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

    private static UserDto ToDto(User u) => new(
        u.Id, u.Username, u.Role, u.Email, u.Mobile, u.Flat, u.Floor, u.Status, PermissionHelper.Parse(u.Permissions));

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
            Email = request.Email,
            Mobile = request.Mobile,
            Flat = request.Flat,
            Floor = request.Floor,
            Permissions = request.Permissions is null ? null : PermissionHelper.Serialize(request.Permissions)
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Add", $"Added user: {user.Username}");
        return CreatedAtAction(nameof(GetAll), new { }, ToDto(user));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UserUpdateRequest request)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        var usernameTaken = await db.Users.AnyAsync(u => u.Id != id && u.Username.ToLower() == request.Username.ToLower());
        if (usernameTaken) return Conflict(new { message = "Username already exists." });

        if (user.Role == "Admin" && request.Role != "Admin" && await db.Users.CountAsync(u => u.Role == "Admin") <= 1)
            return BadRequest(new { message = "Cannot demote the last remaining Admin." });

        user.Username = request.Username;
        user.Role = request.Role;
        user.Status = request.Status;
        user.Email = request.Email;
        user.Mobile = request.Mobile;
        user.Flat = request.Flat;
        user.Floor = request.Floor;
        if (request.Permissions is not null)
            user.Permissions = PermissionHelper.Serialize(request.Permissions);

        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Update", $"Updated user: {user.Username}");
        return Ok(ToDto(user));
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

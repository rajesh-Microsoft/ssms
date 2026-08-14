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

    private static readonly HashSet<string> AllowedRoles =
        new(StringComparer.Ordinal) { "Admin", "Member", "Treasurer", "Secretary", "Committee Member", "Chairman", Roles.Caretaker };

    // The resident's name lives on Member, linked by flat: an account has no name of its own.
    private static UserDto ToDto(User u, string? name = null) => new(
        u.Id, u.Username, name, u.Role, u.Email, u.Mobile, u.Flat, u.Floor, u.Status, PermissionHelper.Parse(u.Permissions));

    private async Task<string?> ResolveMemberNameAsync(string? flat)
    {
        if (string.IsNullOrWhiteSpace(flat)) return null;
        var wanted = flat.Trim().ToLower();
        return await db.Members.Where(m => m.Flat.ToLower() == wanted)
            .Select(m => m.Name)
            .FirstOrDefaultAsync();
    }

    /// <summary>An account being saved as Inactive isn't occupying its flat, so it is never blocked.</summary>
    private Task<bool> FlatTakenAsync(string? flat, string status, int? excludingUserId = null) =>
        string.IsNullOrWhiteSpace(flat) || status.Equals("Inactive", StringComparison.OrdinalIgnoreCase)
            ? Task.FromResult(false)
            : FlatAllocation.IsClaimedAsync(db, flat, excludingUserId);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAll()
    {
        var users = await db.Users.OrderBy(u => u.Username).ToListAsync();

        // One lookup for the whole list rather than a query per user. Grouped rather than keyed
        // directly: two member rows can share a flat, and that must not break the user list.
        var namesByFlat = (await db.Members.Select(m => new { m.Flat, m.Name }).ToListAsync())
            .GroupBy(m => m.Flat.Trim().ToLower())
            .ToDictionary(g => g.Key, g => g.First().Name);

        return Ok(users.Select(u => ToDto(u,
            string.IsNullOrWhiteSpace(u.Flat) ? null : namesByFlat.GetValueOrDefault(u.Flat.Trim().ToLower()))));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(UserCreateRequest request)
    {
        if (!AllowedRoles.Contains(request.Role))
            return BadRequest(new { message = "Invalid role." });

        var exists = await db.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower());
        if (exists) return Conflict(new { message = "Username already exists." });

        if (await FlatTakenAsync(request.Flat, request.Status))
            return Conflict(new { message = $"Flat {request.Flat!.Trim()} already has a registered account." });

        var user = new User
        {
            Username = request.Username,
            Role = request.Role,
            Status = request.Status,
            Email = request.Email,
            Mobile = request.Mobile,
            Flat = request.Flat,
            Floor = request.Floor,
            Permissions = request.Permissions is null ? null : PermissionHelper.Serialize(request.Permissions),
            MustChangePassword = request.MustChangePassword
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Add", $"Added user: {user.Username}");
        return CreatedAtAction(nameof(GetAll), new { }, ToDto(user, await ResolveMemberNameAsync(user.Flat)));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UserUpdateRequest request)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        if (!AllowedRoles.Contains(request.Role))
            return BadRequest(new { message = "Invalid role." });

        var usernameTaken = await db.Users.AnyAsync(u => u.Id != id && u.Username.ToLower() == request.Username.ToLower());
        if (usernameTaken) return Conflict(new { message = "Username already exists." });

        if (user.Role == "Admin" && request.Role != "Admin" && await db.Users.CountAsync(u => u.Role == "Admin") <= 1)
            return BadRequest(new { message = "Cannot demote the last remaining Admin." });

        if (await FlatTakenAsync(request.Flat, request.Status, id))
            return Conflict(new { message = $"Flat {request.Flat!.Trim()} already has a registered account." });

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
        return Ok(ToDto(user, await ResolveMemberNameAsync(user.Flat)));
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

        // Complaints are soft-deleted, so a "deleted" one still holds its foreign key.
        // IgnoreQueryFilters is the point here: without it this check misses exactly the
        // rows that make the delete fail, and the caller gets a raw 500 from SQL Server.
        var blockers = new List<string>();
        await Count(db.Complaints.IgnoreQueryFilters().Where(c => c.RaisedByUserId == id), "complaint");
        await Count(db.Visitors.Where(v => v.RecordedByUserId == id), "visitor entry", "visitor entries");
        await Count(db.Deliveries.Where(d => d.RecordedByUserId == id), "parcel");
        await Count(db.DailyChecklists.Where(c => c.SubmittedByUserId == id), "daily checklist");
        await Count(db.PaymentProofs.Where(p => p.SubmittedByUserId == id || p.ReviewedByUserId == id), "payment proof");

        if (blockers.Count > 0)
        {
            return Conflict(new
            {
                message = $"{user.Username} cannot be deleted because their name is on {string.Join(", ", blockers)}. " +
                          "Set the account to Inactive instead, which keeps that history intact."
            });
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Delete", $"Deleted user id: {id}");
        return NoContent();

        async Task Count<T>(IQueryable<T> query, string singular, string? plural = null)
        {
            var n = await query.CountAsync();
            if (n > 0) blockers.Add($"{n} {(n == 1 ? singular : plural ?? singular + "s")}");
        }
    }
}

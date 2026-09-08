using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

/// <summary>
/// Positions in the society — Treasurer, Chairman, a Festival Committee — each carrying a set of
/// module permissions that its members inherit. Configuring a position once here replaces editing
/// the same permissions on every committee member's account.
/// </summary>
[ApiController]
[Route("api/user-groups")]
[Authorize(Roles = Roles.Admin)]
public class UserGroupsController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    private const string SystemGroupLocked =
        "This is a built-in group. Its permissions can be changed, but it cannot be renamed or deleted.";

    private static UserGroupDto ToDto(UserGroup g, int memberCount) => new(
        g.Id, g.Name, g.Description, g.IsSystem, g.IsActive, memberCount,
        PermissionHelper.Parse(g.Permissions));

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserGroupDto>>> GetAll()
    {
        var groups = await db.UserGroups.AsNoTracking()
            .Select(g => new { Group = g, Count = g.Members.Count })
            .OrderByDescending(x => x.Group.IsSystem).ThenBy(x => x.Group.Name)
            .ToListAsync();
        return Ok(groups.Select(x => ToDto(x.Group, x.Count)));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserGroupDetailDto>> GetById(int id)
    {
        var group = await db.UserGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();

        var members = await db.UserGroupMembers.AsNoTracking()
            .Where(m => m.UserGroupId == id)
            .Select(m => new UserGroupMemberDto(
                m.UserId, m.User!.Username, m.User.Name, m.User.Flat, m.User.Role))
            .OrderBy(m => m.Name ?? m.Username)
            .ToListAsync();

        return Ok(new UserGroupDetailDto(ToDto(group, members.Count), members));
    }

    [HttpPost]
    public async Task<ActionResult<UserGroupDto>> Create(UserGroupUpsertRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest("Group name is required.");
        if (await db.UserGroups.AnyAsync(g => g.Name == name))
            return Conflict($"A group named '{name}' already exists.");

        var group = new UserGroup
        {
            Name = name,
            Description = request.Description?.Trim(),
            IsActive = request.IsActive,
            IsSystem = false,
            Permissions = request.Permissions is null ? null : PermissionHelper.Serialize(request.Permissions)
        };
        db.UserGroups.Add(group);
        await db.SaveChangesAsync();
        await audit.LogAsync("UserGroups", "Add", $"Created group '{group.Name}'");
        return CreatedAtAction(nameof(GetById), new { id = group.Id }, ToDto(group, 0));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UserGroupUpsertRequest request)
    {
        var group = await db.UserGroups.FindAsync(id);
        if (group is null) return NotFound();

        var name = request.Name.Trim();
        if (group.IsSystem && !string.Equals(group.Name, name, StringComparison.Ordinal))
            return BadRequest(SystemGroupLocked);
        if (await db.UserGroups.AnyAsync(g => g.Name == name && g.Id != id))
            return Conflict($"A group named '{name}' already exists.");

        group.Name = name;
        group.Description = request.Description?.Trim();
        group.IsActive = group.IsSystem || request.IsActive;
        if (request.Permissions is not null)
            group.Permissions = PermissionHelper.Serialize(request.Permissions);

        await db.SaveChangesAsync();
        await audit.LogAsync("UserGroups", "Update", $"Updated group '{group.Name}' (id {id})");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var group = await db.UserGroups.FindAsync(id);
        if (group is null) return NotFound();
        if (group.IsSystem) return BadRequest(SystemGroupLocked);

        var members = await db.UserGroupMembers.CountAsync(m => m.UserGroupId == id);
        if (members > 0)
            return Conflict($"'{group.Name}' still has {members} member(s). Remove them before deleting it.");

        db.UserGroups.Remove(group);
        await db.SaveChangesAsync();
        await audit.LogAsync("UserGroups", "Delete", $"Deleted group '{group.Name}' (id {id})");
        return NoContent();
    }

    /// <summary>Replaces the group's membership wholesale, which is how the screen edits it.</summary>
    [HttpPut("{id:int}/members")]
    public async Task<IActionResult> SetMembers(int id, UserGroupMembersRequest request)
    {
        var group = await db.UserGroups.FindAsync(id);
        if (group is null) return NotFound();

        var wanted = request.UserIds.Distinct().ToList();
        var real = await db.Users.Where(u => wanted.Contains(u.Id)).Select(u => u.Id).ToListAsync();
        var missing = wanted.Except(real).ToList();
        if (missing.Count > 0) return BadRequest($"Unknown user id(s): {string.Join(", ", missing)}.");

        var existing = await db.UserGroupMembers.Where(m => m.UserGroupId == id).ToListAsync();
        db.UserGroupMembers.RemoveRange(existing.Where(m => !real.Contains(m.UserId)));
        foreach (var userId in real.Except(existing.Select(m => m.UserId)))
            db.UserGroupMembers.Add(new UserGroupMember { UserGroupId = id, UserId = userId, AddedOn = DateTime.UtcNow, AddedBy = User.Identity?.Name });

        await db.SaveChangesAsync();
        await audit.LogAsync("UserGroups", "SetMembers", $"'{group.Name}' now has {real.Count} member(s)");
        return NoContent();
    }

    /// <summary>Shows what a user can actually do and whether it still comes from an individual
    /// override, so an admin can tell at a glance who has not been moved onto groups yet.</summary>
    [HttpGet("effective/{userId:int}")]
    public async Task<ActionResult<EffectiveAccessDto>> Effective(int userId, [FromServices] EffectivePermissionService resolver)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId)) return NotFound();

        var groups = await db.UserGroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.UserGroup!.Name)
            .OrderBy(n => n)
            .ToListAsync();

        var access = await resolver.ResolveAsync(userId);
        if (access is null)
        {
            // Inactive accounts resolve to nothing, but the screen still needs to show their setup.
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
            return Ok(new EffectiveAccessDto(userId, user.Role, !string.IsNullOrWhiteSpace(user.Permissions),
                groups, PermissionHelper.Parse(user.Permissions)));
        }

        return Ok(new EffectiveAccessDto(userId, access.Role, access.UsesIndividualOverride, groups, access.Permissions));
    }
}

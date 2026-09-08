using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;

namespace SMMS.Api.Services;

/// <summary>The live authorization state of a user, read from the database rather than the token.</summary>
public record EffectiveAccess(
    int UserId,
    string Role,
    string Status,
    Dictionary<string, string> Permissions,
    string OccupancyGroup,
    IReadOnlyList<string> Groups);

/// <summary>
/// Works out what a user may do right now.
///
/// Permissions are deliberately NOT taken from the JWT. A token lives for two hours, so trusting it
/// would leave a treasurer removed from their group still editing expenses until it expired, and a
/// deactivated account still working. Resolving per request costs one indexed lookup on a table
/// with a few dozen rows per society.
///
/// Access is the union of two things: the baseline for how the person occupies their flat
/// (Owner or Tenant, from <c>User.OccupancyType</c>), and any committee positions they hold.
/// </summary>
public class EffectivePermissionService(SmmsDbContext db)
{
    /// <summary>Returns null when the account no longer exists or is no longer active, which the
    /// caller treats as "reject the request".</summary>
    public async Task<EffectiveAccess?> ResolveAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Role, u.Status, u.OccupancyType })
            .FirstOrDefaultAsync(ct);

        if (user is null) return null;
        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase)) return null;

        var occupancyGroup = UserGroups.ForOccupancy(user.OccupancyType);

        var joined = await db.UserGroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.UserGroup!.IsActive)
            .Select(m => m.UserGroup!.Name)
            .ToListAsync(ct);

        // The occupancy baseline is resolved by NAME, not by a membership row, so changing someone
        // from Tenant to Owner takes effect without any join table to keep in step.
        var wanted = joined.Append(occupancyGroup).ToList();
        var permissions = await db.UserGroups.AsNoTracking()
            .Where(g => g.IsActive && wanted.Contains(g.Name))
            .Select(g => g.Permissions)
            .ToListAsync(ct);

        return new EffectiveAccess(
            user.Id, user.Role, user.Status,
            PermissionHelper.Combine(permissions),
            occupancyGroup,
            joined);
    }
}

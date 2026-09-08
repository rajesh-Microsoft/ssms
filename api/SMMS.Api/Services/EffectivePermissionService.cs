using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;

namespace SMMS.Api.Services;

/// <summary>The live authorization state of a user, read from the database rather than the token.</summary>
public record EffectiveAccess(int UserId, string Role, string Status, Dictionary<string, string> Permissions, bool UsesIndividualOverride);

/// <summary>
/// Works out what a user may do right now.
///
/// Permissions are deliberately NOT taken from the JWT. A token lives for two hours, so trusting it
/// would leave a treasurer removed from their group still editing expenses until it expired, and a
/// deactivated account still working. Resolving per request costs one indexed lookup on a table
/// with a few dozen rows per society.
/// </summary>
public class EffectivePermissionService(SmmsDbContext db)
{
    /// <summary>Returns null when the account no longer exists or is no longer active, which the
    /// caller treats as "reject the request".</summary>
    public async Task<EffectiveAccess?> ResolveAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Role, u.Status, u.Permissions })
            .FirstOrDefaultAsync(ct);

        if (user is null) return null;
        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase)) return null;

        // An individual override means this account has not been moved onto groups yet. It wins
        // outright rather than merging, so what an admin sees on the user's own screen is exactly
        // what applies — no silent widening from a group they also happen to be in.
        if (!string.IsNullOrWhiteSpace(user.Permissions))
            return new EffectiveAccess(user.Id, user.Role, user.Status, PermissionHelper.Parse(user.Permissions), true);

        var groupPermissions = await db.UserGroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.UserGroup!.IsActive)
            .Select(m => m.UserGroup!.Permissions)
            .ToListAsync(ct);

        return new EffectiveAccess(user.Id, user.Role, user.Status, PermissionHelper.Combine(groupPermissions), false);
    }
}

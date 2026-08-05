using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;

namespace SMMS.Api.Services;

/// <summary>One flat, one login. A Pending or Active account holds its flat; deactivating or
/// deleting that account frees the flat for the next resident.</summary>
public static class FlatAllocation
{
    public static Task<bool> IsClaimedAsync(SmmsDbContext db, string flat, int? excludingUserId = null)
    {
        var key = flat.Trim().ToLowerInvariant();
        return db.Users.AnyAsync(u =>
            u.Flat != null
            && u.Flat.Trim().ToLower() == key
            && u.Status.ToLower() != "inactive"
            && (excludingUserId == null || u.Id != excludingUserId));
    }
}

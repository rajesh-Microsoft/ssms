using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Data.Interceptors;

/// <summary>Auto-fills IAuditable stamps and converts hard deletes of ISoftDelete rows into soft deletes.</summary>
public sealed class AuditSaveChangesInterceptor(IHttpContextAccessor http) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext? db)
    {
        if (db is null) return;
        var now = DateTime.UtcNow;
        var user = http.HttpContext?.User?.FindFirstValue(ClaimTypes.Name) ?? "System";

        foreach (var e in db.ChangeTracker.Entries<IAuditable>())
        {
            if (e.State == EntityState.Added)
            {
                e.Entity.CreatedOn = now;
                e.Entity.CreatedBy = user;
                e.Entity.ModifiedOn = now;
                e.Entity.ModifiedBy = user;
            }
            else if (e.State == EntityState.Modified)
            {
                e.Entity.ModifiedOn = now;
                e.Entity.ModifiedBy = user;
            }
        }

        // Convert hard deletes to soft deletes.
        foreach (var e in db.ChangeTracker.Entries<ISoftDelete>())
        {
            if (e.State == EntityState.Deleted)
            {
                e.State = EntityState.Modified;
                e.Entity.IsDeleted = true;
                if (e.Entity is IAuditable a) { a.ModifiedOn = now; a.ModifiedBy = user; }
            }
        }
    }
}

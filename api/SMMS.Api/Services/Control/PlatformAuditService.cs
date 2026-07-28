using System.Security.Claims;
using SMMS.Api.Data.Control;
using SMMS.Api.Models.Control;

namespace SMMS.Api.Services.Control;

/// <summary>Writes platform-level (super-admin) audit entries to SmmsControlDb. The actor is always
/// derived from the authenticated super-admin, never accepted from the client.</summary>
public class PlatformAuditService(ControlDbContext db, IHttpContextAccessor httpContextAccessor)
{
    public async Task LogAsync(
        string action,
        string? targetType = null,
        string? targetKey = null,
        string? details = null,
        string? impersonatedTenant = null)
    {
        var actor = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name) ?? "System";

        db.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            ActorUsername = actor,
            Action = action,
            TargetType = targetType,
            TargetKey = targetKey,
            Details = details,
            ImpersonatedTenant = impersonatedTenant
        });

        await db.SaveChangesAsync();
    }
}

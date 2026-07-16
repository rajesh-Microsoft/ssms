using System.Security.Claims;
using SMMS.Api.Data;
using SMMS.Api.Models;

namespace SMMS.Api.Services;

/// <summary>
/// Writes audit trail entries server-side. Audit entries are never accepted from the client —
/// they are always derived from the authenticated user and the action the server actually performed.
/// </summary>
public class AuditService(SmmsDbContext db, IHttpContextAccessor httpContextAccessor)
{
    public async Task LogAsync(string module, string action, string details)
    {
        var username = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name) ?? "System";
        db.AuditLog.Add(new AuditLogEntry
        {
            User = username,
            Module = module,
            Action = action,
            Details = details
        });
        await db.SaveChangesAsync();
    }
}

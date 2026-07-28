using SMMS.Api.Data;
using SMMS.Api.Data.Tenancy;

namespace SMMS.Api.Services.Control;

/// <summary>Reads live metrics from an individual tenant database. Runs in a fresh scope with the
/// tenant context set so the scoped <see cref="SmmsDbContext"/> connects to the correct DB.</summary>
public static class TenantMetrics
{
    public static int CountMembers(IServiceScopeFactory scopeFactory, TenantInfo tenant)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Current = tenant;
            var db = scope.ServiceProvider.GetRequiredService<SmmsDbContext>();
            return db.Members.Count();
        }
        catch
        {
            // A society DB that isn't reachable yet shouldn't break the platform dashboard.
            return 0;
        }
    }
}

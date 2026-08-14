using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Models.Control;

namespace SMMS.Api.Services.Utilities;

public class UtilityBillBackgroundService(
    IServiceScopeFactory scopeFactory,
    ITenantStore tenantStore,
    ILogger<UtilityBillBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DelayUntilNextRun(), stoppingToken);
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Utility bill background sweep failed."); }
        }
    }

    internal static TimeSpan DelayUntilNextRun()
    {
        TimeZoneInfo timeZone;
        try { timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch (TimeZoneNotFoundException) { timeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var next = localNow.Date.AddHours(8);
        if (next <= localNow) next = next.AddDays(1);
        return next - localNow;
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in tenantStore.GetAll().Where(t => t.Status is SocietyStatus.Active or SocietyStatus.Trial))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Current = tenant;
                var db = scope.ServiceProvider.GetRequiredService<SmmsDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<UtilityBillService>();
                var connectionIds = await db.UtilityConnections
                    .Where(c => c.Status == "Active" && c.AutoFetchEnabled)
                    .Select(c => c.Id)
                    .ToListAsync(cancellationToken);
                foreach (var connectionId in connectionIds)
                {
                    try { await service.FetchAsync(connectionId, cancellationToken); }
                    catch (Exception ex) { logger.LogWarning(ex, "Utility fetch skipped after failure Tenant={Tenant} ConnectionId={ConnectionId}", tenant.Key, connectionId); }
                }
            }
            catch (Exception ex) { logger.LogError(ex, "Utility sweep failed for Tenant={Tenant}", tenant.Key); }
        }
    }
}
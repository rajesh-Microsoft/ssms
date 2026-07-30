using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data.Control;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Models.Control;

namespace SMMS.Api.Services.Control;

/// <summary>Background sweep that enforces society subscriptions: any Active/Trial society whose
/// <see cref="Society.ExpiryDate"/> has passed is flipped to <see cref="SocietyStatus.Expired"/>,
/// audit-logged, and the tenant registry is refreshed so <c>TenantResolutionMiddleware</c> starts
/// blocking it. Runs once at startup and then on a fixed interval. The middleware also checks the
/// expiry date in real time, so access is cut off immediately even between sweeps — this job just
/// makes the "Expired" state durable and visible in the console.</summary>
public class SubscriptionEnforcementService(
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionEnforcementService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay so the app finishes starting (DB migrations/seeding) before the first sweep.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Subscription enforcement sweep failed.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<DbTenantStore>();
        var audit = scope.ServiceProvider.GetRequiredService<PlatformAuditService>();

        var today = DateTime.UtcNow.Date;

        var lapsed = await db.Societies
            .Where(s => (s.Status == SocietyStatus.Active || s.Status == SocietyStatus.Trial)
                        && s.ExpiryDate != null && s.ExpiryDate < today)
            .ToListAsync(ct);

        if (lapsed.Count == 0) return;

        foreach (var society in lapsed)
        {
            society.Status = SocietyStatus.Expired;
            await audit.LogAsync("SubscriptionExpired", "Society", society.Key,
                $"Subscription expired on {society.ExpiryDate:yyyy-MM-dd}; access auto-suspended.");
        }

        await db.SaveChangesAsync(ct);
        tenantStore.Reload();

        logger.LogInformation("Subscription enforcement: {Count} society(ies) marked Expired.", lapsed.Count);
    }
}

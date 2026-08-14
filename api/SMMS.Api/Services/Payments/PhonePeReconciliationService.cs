using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Models.Control;

namespace SMMS.Api.Services.Payments;

/// <summary>
/// Polls PhonePe for checkout attempts that never reached a terminal state. A resident who
/// closes the tab mid-payment leaves nothing to confirm the order, and the webhook can be lost
/// (retries expire, or this host is unreachable), so PhonePe requires merchants to reconcile
/// PENDING orders on a schedule until they resolve.
///
/// The cadence below follows PhonePe's mandated schedule: first check at ~20s, then every 3s,
/// 6s, 10s, 30s and finally every minute until the order reaches a terminal state or expires.
/// </summary>
public class PhonePeReconciliationService(
    IServiceScopeFactory scopeFactory,
    ITenantStore tenantStore,
    ILogger<PhonePeReconciliationService> logger) : BackgroundService
{
    private static readonly TimeSpan BusyInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);

    /// <summary>Longest a checkout can stay open (PhonePe caps expireAfter at 3600s) plus slack.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(65);

    private readonly ConcurrentDictionary<string, DateTimeOffset> lastChecked = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var pendingSeen = 0;
            try
            {
                pendingSeen = await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PhonePe reconciliation sweep failed.");
            }

            try { await Task.Delay(pendingSeen > 0 ? BusyInterval : IdleInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Returns how many attempts are still awaiting a terminal state.</summary>
    private async Task<int> SweepAsync(CancellationToken ct)
    {
        var stillPending = 0;
        var cutoff = DateTime.UtcNow - MaxAge;

        foreach (var tenant in tenantStore.GetAll()
                     .Where(t => t.Status is SocietyStatus.Active or SocietyStatus.Trial))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Current = tenant;
                var gateway = scope.ServiceProvider.GetRequiredService<PhonePePaymentGateway>();
                if (!gateway.IsConfigured) continue;

                var db = scope.ServiceProvider.GetRequiredService<SmmsDbContext>();
                var attempts = await db.PaymentProofs
                    .Where(p => p.GatewayName == "PhonePe"
                        && p.Status == "Initiated"
                        && p.SubmittedAt >= cutoff)
                    .ToListAsync(ct);
                if (attempts.Count == 0) continue;

                var payments = scope.ServiceProvider.GetRequiredService<PaymentService>();
                var now = DateTimeOffset.UtcNow;

                foreach (var attempt in attempts)
                {
                    stillPending++;
                    var key = $"{tenant.Key}:{attempt.GatewayReference}";
                    var age = now - new DateTimeOffset(attempt.SubmittedAt, TimeSpan.Zero);
                    if (!IsDue(age, lastChecked.GetValueOrDefault(key), now)) continue;

                    lastChecked[key] = now;
                    if (await TryResolveAsync(gateway, payments, db, attempt, ct)) stillPending--;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PhonePe reconciliation failed for tenant {Tenant}.", tenant.Key);
            }
        }

        // Drop tracking for anything past the window so the dictionary cannot grow without bound.
        var expired = DateTimeOffset.UtcNow - MaxAge;
        foreach (var (key, seen) in lastChecked)
            if (seen < expired) lastChecked.TryRemove(key, out _);

        return stillPending;
    }

    /// <summary>True once the attempt has reached a terminal state.</summary>
    private async Task<bool> TryResolveAsync(
        PhonePePaymentGateway gateway, PaymentService payments, SmmsDbContext db,
        Models.PaymentProof attempt, CancellationToken ct)
    {
        PhonePeOrderStatus status;
        try
        {
            status = await gateway.GetOrderStatusAsync(attempt.GatewayReference!, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PhonePe status check failed for {OrderId}.", attempt.GatewayReference);
            return false;
        }

        if (status.State == "COMPLETED")
        {
            if (status.Amount != PhonePePaymentGateway.ToPaise(attempt.Amount))
            {
                logger.LogWarning(
                    "PhonePe reconciliation amount mismatch for {OrderId}: gateway {Amount} vs invoice {Expected}.",
                    attempt.GatewayReference, status.Amount, PhonePePaymentGateway.ToPaise(attempt.Amount));
                return true;
            }

            await payments.ApproveGatewayPaymentAsync(attempt, status.TransactionId ?? status.OrderId, ct);
            logger.LogInformation("PhonePe reconciliation settled {OrderId}.", attempt.GatewayReference);
            return true;
        }

        if (status.State == "FAILED")
        {
            // Set directly rather than via RejectAsync: there is no reviewing user in a sweep.
            attempt.Status = "Rejected";
            attempt.ReviewedAt = DateTime.UtcNow;
            attempt.ReviewRemarks = "PhonePe reported the payment as failed.";
            await db.SaveChangesAsync(ct);
            return true;
        }

        return false;
    }

    /// <summary>PhonePe's mandated cadence: nothing before ~20s, then 3s, 6s, 10s, 30s, 60s.</summary>
    public static bool IsDue(TimeSpan age, DateTimeOffset lastCheck, DateTimeOffset now)
    {
        if (age < TimeSpan.FromSeconds(20)) return false;

        var interval = age.TotalSeconds switch
        {
            < 55 => 3,
            < 115 => 6,
            < 175 => 10,
            < 235 => 30,
            _ => 60
        };

        return lastCheck == default || (now - lastCheck).TotalSeconds >= interval;
    }
}

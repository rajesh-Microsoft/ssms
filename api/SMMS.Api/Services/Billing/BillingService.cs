using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;

namespace SMMS.Api.Services.Billing;

/// <summary>
/// Generates monthly maintenance invoices (Collection rows with Status="Unpaid") — one per active
/// member per billing month. Idempotent: a member who already has any Collection for the target
/// Year+Month is skipped, so re-running (manually or via the scheduler) never creates duplicates.
/// Each invoice's amount comes from the data-driven maintenance rule engine
/// (<see cref="MaintenanceCalculationService"/>), with the per-component breakdown persisted as lines.
/// </summary>
public class BillingService(SmmsDbContext db, AuditService audit, MaintenanceCalculationService calc, AdvanceService advance)
{
    public async Task<BillingRunResult> GenerateForMonthAsync(int month, int year, decimal? amountOverride = null)
    {
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1-12.");
        if (year is < 2000 or > 2100) throw new ArgumentOutOfRangeException(nameof(year), "Year is out of range.");

        var settings = await db.Settings.FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Society settings not found.");

        var dueDay = Math.Clamp(settings.DueDay, 1, DateTime.DaysInMonth(year, month));
        var dueDate = new DateTime(year, month, dueDay);

        var components = await calc.LoadComponentsAsync();

        var activeMembers = await db.Members
            .Where(m => m.Status == "Active")
            .ToListAsync();

        // Idempotency / dup-prevention: any existing charge for this member+month+year → skip.
        var billedSet = (await db.Collections
                .Where(c => c.Year == year && c.Month == month)
                .Select(c => c.MemberId)
                .ToListAsync())
            .ToHashSet();

        var newInvoices = new List<Collection>();
        foreach (var m in activeMembers.Where(m => !billedSet.Contains(m.Id)))
        {
            var invoice = calc.CalculateForFlat(components, MaintenanceCalculationService.ToContext(m));

            // amountOverride still wins for one-off manual runs; otherwise use the engine total.
            var total = amountOverride ?? invoice.Total;

            newInvoices.Add(new Collection
            {
                MemberId = m.Id,
                Amount = total,
                Status = "Unpaid",
                Month = month,
                Year = year,
                DueDate = dueDate,
                // InvoiceNumber left null → PaymentNumbering derives "INV{Id:D6}" after save,
                // consistent with every other invoice in the system.
                Remarks = "Auto-generated monthly maintenance",
                Lines = amountOverride is null
                    ? invoice.Lines.Select(l => new CollectionLine
                    {
                        ComponentId = l.ComponentId,
                        ComponentName = l.ComponentName,
                        Method = l.Method,
                        Amount = l.Amount
                    }).ToList()
                    : new List<CollectionLine>()
            });
        }

        if (newInvoices.Count > 0)
        {
            db.Collections.AddRange(newInvoices);
            await db.SaveChangesAsync();

            // Auto-settle each new invoice from the member's advance wallet (Auto mode only).
            var membersById = activeMembers.ToDictionary(m => m.Id);
            var settledFromAdvance = 0;
            foreach (var inv in newInvoices)
            {
                if (membersById.TryGetValue(inv.MemberId, out var mem) && advance.ApplyToCharge(mem, inv) > 0)
                    settledFromAdvance++;
            }
            if (settledFromAdvance > 0) await db.SaveChangesAsync();

            var avg = newInvoices.Average(i => i.Amount);
            await audit.LogAsync("Collections", "GenerateBilling",
                $"Generated {newInvoices.Count} maintenance invoice(s) for {month:D2}/{year} via rule engine " +
                $"(skipped {activeMembers.Count - newInvoices.Count} already billed; " +
                $"{settledFromAdvance} settled from advance).");

            return new BillingRunResult(month, year, avg, newInvoices.Count,
                activeMembers.Count - newInvoices.Count, activeMembers.Count);
        }

        await audit.LogAsync("Collections", "GenerateBilling",
            $"Generated 0 maintenance invoice(s) for {month:D2}/{year} via rule engine " +
            $"(skipped {activeMembers.Count} already billed).");

        return new BillingRunResult(month, year, 0m, 0,
            activeMembers.Count, activeMembers.Count);
    }
}

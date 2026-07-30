using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services.Payments;

namespace SMMS.Api.Services.Billing;

/// <summary>
/// Raises a one-time (special) charge for a OneTime collection category against active members —
/// e.g. a festival, painting or corpus fund. Each targeted member gets an Unpaid Collection row
/// (CollectionType="OneTime") payable through the normal UPI-proof flow. Idempotent per
/// (member, category): a member already charged for this category is skipped, so re-running is safe.
/// The per-flat amount reuses the maintenance rule engine, so FixedAmount, PerSquareFoot and Manual
/// categories all work; an explicit amount override wins when supplied.
/// </summary>
public class OneTimeChargeService(SmmsDbContext db, AuditService audit, ChargeStrategyResolver resolver)
{
    public async Task<OneTimeChargeResult> GenerateAsync(
        int categoryId, decimal? amountOverride, DateTime? dueDate, IReadOnlyCollection<int>? memberIds)
    {
        var category = await db.MaintenanceComponents.FindAsync(categoryId)
            ?? throw new InvalidOperationException("Category not found.");
        if (category.CategoryType != CollectionCategoryType.OneTime)
            throw new InvalidOperationException("Only OneTime categories can be raised as a one-time charge.");

        var due = (dueDate ?? DateTime.UtcNow.Date.AddDays(15)).Date;

        var membersQuery = db.Members.Where(m => m.Status == "Active");
        if (memberIds is { Count: > 0 })
            membersQuery = membersQuery.Where(m => memberIds.Contains(m.Id));
        var members = await membersQuery.ToListAsync();

        // Idempotency: skip members already charged for this one-time category.
        var alreadyCharged = (await db.Collections
                .Where(c => c.CollectionType == "OneTime" && c.CategoryId == categoryId)
                .Select(c => c.MemberId)
                .ToListAsync())
            .ToHashSet();

        var newCharges = new List<Collection>();
        foreach (var m in members.Where(m => !alreadyCharged.Contains(m.Id)))
        {
            var amount = amountOverride ?? decimal.Round(
                resolver.For(category.Method).Calculate(
                    category, MaintenanceCalculationService.ToContext(m), new Dictionary<int, decimal>()),
                2, MidpointRounding.AwayFromZero);
            if (amount <= 0m) continue;   // nothing to bill this flat (e.g. optional/zero)

            newCharges.Add(new Collection
            {
                MemberId = m.Id,
                Amount = amount,
                Status = "Unpaid",
                Month = due.Month,
                Year = due.Year,
                DueDate = due,
                CollectionType = "OneTime",
                CategoryId = category.Id,
                Title = category.Name,
                Remarks = $"One-time charge: {category.Name}",
                Lines = new List<CollectionLine>
                {
                    new()
                    {
                        ComponentId = category.Id,
                        ComponentName = category.Name,
                        Method = category.Method.ToString(),
                        Amount = amount
                    }
                }
            });
        }

        if (newCharges.Count > 0)
        {
            db.Collections.AddRange(newCharges);
            await db.SaveChangesAsync();
        }

        await audit.LogAsync("Collections", "OneTimeCharge",
            $"Raised one-time charge \"{category.Name}\" for {newCharges.Count} member(s) " +
            $"(skipped {members.Count - newCharges.Count} already charged), due {due:dd MMM yyyy}.");

        return new OneTimeChargeResult(
            category.Id, category.Name, newCharges.Sum(c => c.Amount),
            newCharges.Count, members.Count - newCharges.Count, members.Count,
            PaymentNumbering.BillingLabel(due.Month, due.Year));
    }
}

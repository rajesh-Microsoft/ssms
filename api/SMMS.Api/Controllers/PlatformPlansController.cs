using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data.Control;
using SMMS.Api.Dtos.Control;
using SMMS.Api.Models.Control;
using SMMS.Api.Services.Control;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/platform/plans")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformPlansController(ControlDbContext db, PlatformAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SubscriptionPlanDto>>> List()
    {
        var plans = await db.SubscriptionPlans.AsNoTracking()
            .OrderBy(p => p.Price)
            .ToListAsync();
        return Ok(plans.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<SubscriptionPlanDto>> Create(UpsertPlanRequest req)
    {
        var code = (req.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (req.Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Code and name are required." });
        if (req.Price < 0)
            return BadRequest(new { message = "Price cannot be negative." });
        if (req.BillingPeriodMonths <= 0)
            return BadRequest(new { message = "Billing period must be at least 1 month." });
        if (await db.SubscriptionPlans.AnyAsync(p => p.Code == code))
            return BadRequest(new { message = $"A plan with code '{code}' already exists." });

        var plan = new SubscriptionPlan
        {
            Code = code,
            Name = name,
            Price = req.Price,
            BillingPeriodMonths = req.BillingPeriodMonths,
            Currency = NormalizeCurrency(req.Currency),
            IsActive = req.IsActive
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();

        await audit.LogAsync("PlanCreate", "Plan", plan.Code,
            $"Created plan '{plan.Name}' @ {plan.Price} {plan.Currency}/{plan.BillingPeriodMonths}mo.");
        return Created($"/api/platform/plans/{plan.Id}", ToDto(plan));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SubscriptionPlanDto>> Update(int id, UpsertPlanRequest req)
    {
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
        if (plan is null) return NotFound();

        var name = (req.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Name is required." });
        if (req.Price < 0)
            return BadRequest(new { message = "Price cannot be negative." });
        if (req.BillingPeriodMonths <= 0)
            return BadRequest(new { message = "Billing period must be at least 1 month." });

        // Code is immutable once created (issued invoices snapshot it); only mutable fields change.
        plan.Name = name;
        plan.Price = req.Price;
        plan.BillingPeriodMonths = req.BillingPeriodMonths;
        plan.Currency = NormalizeCurrency(req.Currency);
        plan.IsActive = req.IsActive;
        await db.SaveChangesAsync();

        await audit.LogAsync("PlanUpdate", "Plan", plan.Code, $"Updated plan '{plan.Name}'.");
        return Ok(ToDto(plan));
    }

    /// <summary>Toggles a plan's active flag. Inactive plans stay on record (and on past invoices)
    /// but are hidden from new invoice generation.</summary>
    [HttpPost("{id:int}/toggle")]
    public async Task<ActionResult<SubscriptionPlanDto>> Toggle(int id)
    {
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
        if (plan is null) return NotFound();

        plan.IsActive = !plan.IsActive;
        await db.SaveChangesAsync();

        await audit.LogAsync(plan.IsActive ? "PlanActivate" : "PlanDeactivate", "Plan", plan.Code,
            $"Plan '{plan.Name}' set {(plan.IsActive ? "active" : "inactive")}.");
        return Ok(ToDto(plan));
    }

    private static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "INR" : currency.Trim().ToUpperInvariant();

    private static SubscriptionPlanDto ToDto(SubscriptionPlan p) => new(
        p.Id, p.Code, p.Name, p.Price, p.BillingPeriodMonths, p.Currency, p.IsActive, p.CreatedAt);
}

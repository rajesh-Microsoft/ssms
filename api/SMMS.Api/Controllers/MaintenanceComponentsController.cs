using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;
using SMMS.Api.Services.Billing;

namespace SMMS.Api.Controllers;

/// <summary>Admin CRUD for the data-driven maintenance components, plus a per-flat invoice preview.
/// Adding/removing components here changes how invoices are calculated with no code changes.</summary>
[ApiController]
[Route("api/admin/maintenance-components")]
[Authorize(Roles = "Admin")]
public class MaintenanceComponentsController(
    SmmsDbContext db, AuditService audit, MaintenanceCalculationService calc) : ControllerBase
{
    private static MaintenanceComponentDto ToDto(MaintenanceComponent c) => new(
        c.Id, c.Name, c.Description, c.Method.ToString(), c.Amount, c.PercentageValue,
        c.PercentageBaseComponentId, c.ApplyToAllFlats, c.IsActive, c.SortOrder,
        c.CategoryType.ToString(), c.Frequency.ToString(), c.TaxApplicable, c.LateFeeApplicable,
        c.Rates.Select(r => new ComponentRateDto(r.Key, r.Amount)).ToArray(),
        c.FlatOverrides.Select(o => new ComponentFlatOverrideDto(o.MemberId, o.IsApplicable, o.Amount)).ToArray());

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MaintenanceComponentDto>>> List()
    {
        if (!User.CanView(PermissionModules.Settings)) return Forbid();
        var items = await db.MaintenanceComponents
            .Include(c => c.Rates)
            .Include(c => c.FlatOverrides)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync();
        return Ok(items.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<MaintenanceComponentDto>> Create(MaintenanceComponentUpsertRequest req)
    {
        if (!User.CanEdit(PermissionModules.Settings)) return Forbid();
        if (!TryValidate(req, out var method, out var error)) return BadRequest(new { message = error });

        var c = new MaintenanceComponent();
        Apply(c, req, method);
        db.MaintenanceComponents.Add(c);
        await db.SaveChangesAsync();
        await audit.LogAsync("MaintenanceComponents", "Create", $"Created component '{c.Name}'.");
        return CreatedAtAction(nameof(List), new { id = c.Id }, ToDto(c));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, MaintenanceComponentUpsertRequest req)
    {
        if (!User.CanEdit(PermissionModules.Settings)) return Forbid();
        if (!TryValidate(req, out var method, out var error)) return BadRequest(new { message = error });

        var c = await db.MaintenanceComponents
            .Include(x => x.Rates)
            .Include(x => x.FlatOverrides)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();

        // Simplest correct sync for the child rows: clear and re-add from the request.
        db.MaintenanceComponentRates.RemoveRange(c.Rates);
        db.MaintenanceComponentFlatOverrides.RemoveRange(c.FlatOverrides);
        Apply(c, req, method);
        await db.SaveChangesAsync();
        await audit.LogAsync("MaintenanceComponents", "Update", $"Updated component '{c.Name}'.");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!User.CanEdit(PermissionModules.Settings)) return Forbid();
        var c = await db.MaintenanceComponents.FindAsync(id);
        if (c is null) return NotFound();
        db.MaintenanceComponents.Remove(c);
        await db.SaveChangesAsync();
        await audit.LogAsync("MaintenanceComponents", "Delete", $"Deleted component '{c.Name}'.");
        return NoContent();
    }

    /// <summary>Preview the full per-flat breakdown without generating any invoices.</summary>
    [HttpGet("preview")]
    public async Task<ActionResult<IEnumerable<FlatInvoiceDto>>> Preview()
    {
        if (!User.CanView(PermissionModules.Settings)) return Forbid();
        var invoices = await calc.CalculateAllAsync();
        return Ok(invoices.Select(i => new FlatInvoiceDto(i.MemberId, i.FlatNumber,
            i.Lines.Select(l => new InvoiceLineDto(l.ComponentId, l.ComponentName, l.Method, l.Amount)).ToArray(),
            i.Total)));
    }

    private static bool TryValidate(MaintenanceComponentUpsertRequest req,
        out CalculationMethod method, out string? error)
    {
        error = null;
        if (!Enum.TryParse(req.Method, out method))
        {
            error = "Unknown calculation method.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(req.Name))
        {
            error = "Name is required.";
            return false;
        }
        if (method == CalculationMethod.Percentage && req.PercentageValue is null or < 0 or > 100)
        {
            error = "Percentage must be between 0 and 100.";
            return false;
        }
        if (req.CategoryType is not null && !Enum.TryParse<CollectionCategoryType>(req.CategoryType, out _))
        {
            error = "Unknown category type.";
            return false;
        }
        if (req.Frequency is not null && !Enum.TryParse<BillingFrequency>(req.Frequency, out _))
        {
            error = "Unknown billing frequency.";
            return false;
        }
        return true;
    }

    private static void Apply(MaintenanceComponent c, MaintenanceComponentUpsertRequest req, CalculationMethod method)
    {
        c.Name = req.Name.Trim();
        c.Description = req.Description;
        c.Method = method;
        c.Amount = req.Amount;
        c.PercentageValue = req.PercentageValue;
        c.PercentageBaseComponentId = req.PercentageBaseComponentId;
        c.ApplyToAllFlats = req.ApplyToAllFlats;
        c.IsActive = req.IsActive;
        c.SortOrder = req.SortOrder;
        c.CategoryType = Enum.TryParse<CollectionCategoryType>(req.CategoryType, out var ct) ? ct : CollectionCategoryType.Recurring;
        c.Frequency = Enum.TryParse<BillingFrequency>(req.Frequency, out var fr) ? fr : BillingFrequency.Monthly;
        c.TaxApplicable = req.TaxApplicable;
        c.LateFeeApplicable = req.LateFeeApplicable;
        c.Rates = req.Rates
            .Where(r => !string.IsNullOrWhiteSpace(r.Key))
            .Select(r => new MaintenanceComponentRate { Key = r.Key.Trim(), Amount = r.Amount })
            .ToList();
        c.FlatOverrides = req.FlatOverrides
            .Select(o => new MaintenanceComponentFlatOverride
            {
                MemberId = o.MemberId,
                IsApplicable = o.IsApplicable,
                Amount = o.Amount
            })
            .ToList();
    }
}

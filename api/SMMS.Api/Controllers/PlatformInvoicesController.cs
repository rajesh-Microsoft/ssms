using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data.Control;
using SMMS.Api.Dtos.Control;
using SMMS.Api.Models.Control;
using SMMS.Api.Services.Control;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/platform/invoices")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformInvoicesController(ControlDbContext db, PlatformAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlatformInvoiceDto>>> List(
        [FromQuery] string? societyKey, [FromQuery] string? status)
    {
        var query = db.PlatformInvoices.AsNoTracking().Include(i => i.Society).AsQueryable();

        if (!string.IsNullOrWhiteSpace(societyKey))
            query = query.Where(i => i.Society.Key == societyKey);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(i => i.Status == status);

        var list = await query
            .OrderByDescending(i => i.IssuedAt)
            .ThenByDescending(i => i.Id)
            .ToListAsync();

        return Ok(list.Select(ToDto));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<InvoiceSummaryDto>> Summary()
    {
        var all = await db.PlatformInvoices.AsNoTracking().ToListAsync();

        var outstanding = all
            .Where(i => i.Status is PlatformInvoiceStatus.Issued or PlatformInvoiceStatus.Overdue)
            .Sum(i => i.Amount);
        var collected = all
            .Where(i => i.Status == PlatformInvoiceStatus.Paid)
            .Sum(i => i.Amount);
        var currency = all.FirstOrDefault()?.Currency ?? "INR";

        return Ok(new InvoiceSummaryDto(
            Total: all.Count,
            Paid: all.Count(i => i.Status == PlatformInvoiceStatus.Paid),
            Issued: all.Count(i => i.Status == PlatformInvoiceStatus.Issued && !IsOverdue(i)),
            Overdue: all.Count(i => i.Status == PlatformInvoiceStatus.Overdue || IsOverdue(i)),
            Draft: all.Count(i => i.Status == PlatformInvoiceStatus.Draft),
            Void: all.Count(i => i.Status == PlatformInvoiceStatus.Void),
            OutstandingAmount: outstanding,
            CollectedAmount: collected,
            Currency: currency));
    }

    [HttpPost]
    public async Task<ActionResult<PlatformInvoiceDto>> Generate(GenerateInvoiceRequest req)
    {
        var society = await db.Societies.FirstOrDefaultAsync(s => s.Key == req.SocietyKey);
        if (society is null) return BadRequest(new { message = "Unknown society." });

        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == req.PlanId);
        if (plan is null) return BadRequest(new { message = "Unknown plan." });

        var periodStart = (req.PeriodStart ?? DateTime.UtcNow).Date;
        var issuedAt = DateTime.UtcNow;
        var dueDate = issuedAt.Date.AddDays(req.DueInDays is > 0 ? req.DueInDays.Value : 14);

        var invoice = new PlatformInvoice
        {
            InvoiceNumber = await NextInvoiceNumberAsync(),
            SocietyId = society.Id,
            SubscriptionPlanId = plan.Id,
            PlanCode = plan.Code,
            PlanName = plan.Name,
            BillingPeriodMonths = plan.BillingPeriodMonths,
            Amount = plan.Price,
            Currency = plan.Currency,
            PeriodStart = periodStart,
            PeriodEnd = periodStart.AddMonths(plan.BillingPeriodMonths),
            IssuedAt = issuedAt,
            DueDate = dueDate,
            Status = PlatformInvoiceStatus.Issued,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim()
        };
        db.PlatformInvoices.Add(invoice);

        // Keep the society's displayed plan aligned with what it's being billed for.
        society.Plan = plan.Name;
        await db.SaveChangesAsync();

        await audit.LogAsync("InvoiceGenerate", "Invoice", invoice.InvoiceNumber,
            $"Issued {invoice.Amount} {invoice.Currency} to '{society.DisplayName}' for plan '{plan.Name}'.");

        invoice.Society = society;
        return Created($"/api/platform/invoices/{invoice.Id}", ToDto(invoice));
    }

    [HttpPost("{id:int}/pay")]
    public async Task<ActionResult<PlatformInvoiceDto>> Pay(int id, PayInvoiceRequest req)
    {
        var invoice = await db.PlatformInvoices.Include(i => i.Society).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        if (invoice.Status == PlatformInvoiceStatus.Paid)
            return BadRequest(new { message = "Invoice is already paid." });
        if (invoice.Status == PlatformInvoiceStatus.Void)
            return BadRequest(new { message = "A voided invoice cannot be paid." });

        invoice.Status = PlatformInvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        invoice.PaymentReference = string.IsNullOrWhiteSpace(req.PaymentReference)
            ? null : req.PaymentReference.Trim();
        await db.SaveChangesAsync();

        await audit.LogAsync("InvoicePay", "Invoice", invoice.InvoiceNumber,
            $"Marked paid ({invoice.Amount} {invoice.Currency})"
            + (invoice.PaymentReference is null ? "." : $", ref {invoice.PaymentReference}."));
        return Ok(ToDto(invoice));
    }

    [HttpPost("{id:int}/void")]
    public async Task<ActionResult<PlatformInvoiceDto>> Void(int id)
    {
        var invoice = await db.PlatformInvoices.Include(i => i.Society).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        if (invoice.Status == PlatformInvoiceStatus.Paid)
            return BadRequest(new { message = "A paid invoice cannot be voided." });

        invoice.Status = PlatformInvoiceStatus.Void;
        await db.SaveChangesAsync();

        await audit.LogAsync("InvoiceVoid", "Invoice", invoice.InvoiceNumber, "Invoice voided.");
        return Ok(ToDto(invoice));
    }

    /// <summary>Sequential per-month invoice number, e.g. INV-202608-0001.</summary>
    private async Task<string> NextInvoiceNumberAsync()
    {
        var prefix = $"INV-{DateTime.UtcNow:yyyyMM}-";
        var last = await db.PlatformInvoices
            .Where(i => i.InvoiceNumber.StartsWith(prefix))
            .Select(i => i.InvoiceNumber)
            .OrderByDescending(n => n)
            .FirstOrDefaultAsync();

        var seq = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var n)) seq = n + 1;
        return prefix + seq.ToString("D4");
    }

    private static bool IsOverdue(PlatformInvoice i) =>
        i.Status == PlatformInvoiceStatus.Issued && i.DueDate.Date < DateTime.UtcNow.Date;

    private static PlatformInvoiceDto ToDto(PlatformInvoice i)
    {
        // Overdue is derived on read (an Issued invoice past its due date) rather than persisted,
        // so no background job is needed to flip the status.
        var status = IsOverdue(i) ? PlatformInvoiceStatus.Overdue : i.Status;
        return new PlatformInvoiceDto(
            i.Id, i.InvoiceNumber, i.Society?.Key ?? string.Empty, i.Society?.DisplayName ?? string.Empty,
            i.PlanCode, i.PlanName, i.BillingPeriodMonths, i.Amount, i.Currency,
            i.PeriodStart, i.PeriodEnd, i.IssuedAt, i.DueDate,
            status, i.PaidAt, i.PaymentReference, i.Notes);
    }
}

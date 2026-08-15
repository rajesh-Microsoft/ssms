using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;
using SMMS.Api.Services.Utilities;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/utilities")]
[Authorize]
public class UtilitiesController(
    SmmsDbContext db,
    UtilityBillService billService,
    AuditService audit,
    IOptions<UtilityIntegrationOptions> utilityOptions) : ControllerBase
{
    /// <summary>The biller's own payment page with the consumer number pre-filled. Null when the
    /// provider has no payment page configured, which hides the Pay action rather than guessing a URL.</summary>
    private string? PayUrl(string providerCode, string consumerNumber)
    {
        if (!utilityOptions.Value.Providers.TryGetValue(providerCode, out var provider)) return null;
        return string.IsNullOrWhiteSpace(provider.PaymentUrlTemplate)
            ? null
            : provider.PaymentUrlTemplate.Replace("{ConsumerNumber}", Uri.EscapeDataString(consumerNumber));
    }

    private UtilityBillDto ToDto(UtilityBill bill) => new(
        bill.Id,
        bill.UtilityConnectionId,
        bill.UtilityConnection!.Provider!.ProviderName,
        bill.UtilityConnection.Provider.Category,
        bill.UtilityConnection.ConsumerNumber,
        bill.BillingMonth,
        bill.BillNumber,
        bill.BillDate,
        bill.DueDate,
        bill.BillAmount,
        bill.UnitsConsumed,
        bill.Arrears,
        bill.ConsumerName,
        bill.Status,
        bill.FetchedOn,
        PayUrl(bill.UtilityConnection.Provider.Code, bill.UtilityConnection.ConsumerNumber),
        bill.PaidOn,
        bill.PaymentReference,
        bill.ExpenseId);

    [HttpGet("providers")]
    public async Task<ActionResult<IReadOnlyList<UtilityProviderDto>>> Providers(CancellationToken cancellationToken) =>
        Ok(await db.UtilityProviders.AsNoTracking().OrderBy(p => p.ProviderName)
            .Select(p => new UtilityProviderDto(p.Id, p.Code, p.ProviderName, p.Category, p.SupportsAutoFetch, p.Status))
            .ToListAsync(cancellationToken));

    [HttpGet("connections")]
    public async Task<ActionResult<IReadOnlyList<UtilityConnectionDto>>> Connections(CancellationToken cancellationToken)
    {
        var connections = await db.UtilityConnections.AsNoTracking()
            .Include(c => c.Provider).Include(c => c.Bills)
            .OrderBy(c => c.Provider!.Category).ThenBy(c => c.ConsumerNumber)
            .ToListAsync(cancellationToken);
        return Ok(connections.Select(c => new UtilityConnectionDto(
            c.Id, c.ProviderId, c.Provider!.Code, c.Provider.ProviderName, c.Provider.Category,
            c.ConsumerNumber, c.ServiceNumber, c.AutoFetchEnabled, c.Status, c.CreatedOn,
            c.LastFetchedOn, c.LastFetchError,
            c.Bills.OrderByDescending(b => b.BillingMonth).ThenByDescending(b => b.FetchedOn)
                .Select(ToDto).FirstOrDefault(),
            PayUrl(c.Provider.Code, c.ConsumerNumber))).ToList());
    }

    [HttpPost("connections")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<UtilityConnectionDto>> Create(UtilityConnectionRequest request, CancellationToken cancellationToken)
    {
        var provider = await db.UtilityProviders.FirstOrDefaultAsync(p => p.Id == request.ProviderId && p.Status == "Active", cancellationToken);
        if (provider is null) return BadRequest(new { message = "Select an active utility provider." });
        var consumerNumber = request.ConsumerNumber.Trim();
        if (await db.UtilityConnections.AnyAsync(c => c.ProviderId == request.ProviderId && c.ConsumerNumber == consumerNumber, cancellationToken))
            return Conflict(new { message = "This utility connection is already configured." });

        var connection = new UtilityConnection
        {
            ProviderId = provider.Id,
            Provider = provider,
            ConsumerNumber = consumerNumber,
            ServiceNumber = Clean(request.ServiceNumber),
            AutoFetchEnabled = request.AutoFetchEnabled && provider.SupportsAutoFetch,
            Status = request.Active ? "Active" : "Inactive"
        };
        db.UtilityConnections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);
        await audit.LogAsync("Settings", "AddUtilityConnection", $"Added {provider.Code} connection {consumerNumber}");
        return CreatedAtAction(nameof(Connections), ToConnectionDto(connection));
    }

    [HttpPut("connections/{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(int id, UtilityConnectionRequest request, CancellationToken cancellationToken)
    {
        var connection = await db.UtilityConnections.Include(c => c.Provider)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null) return NotFound();
        var provider = await db.UtilityProviders.FirstOrDefaultAsync(p => p.Id == request.ProviderId && p.Status == "Active", cancellationToken);
        if (provider is null) return BadRequest(new { message = "Select an active utility provider." });
        var consumerNumber = request.ConsumerNumber.Trim();
        if (await db.UtilityConnections.AnyAsync(c => c.Id != id && c.ProviderId == provider.Id && c.ConsumerNumber == consumerNumber, cancellationToken))
            return Conflict(new { message = "This utility connection is already configured." });

        connection.ProviderId = provider.Id;
        connection.ConsumerNumber = consumerNumber;
        connection.ServiceNumber = Clean(request.ServiceNumber);
        connection.AutoFetchEnabled = request.AutoFetchEnabled && provider.SupportsAutoFetch;
        connection.Status = request.Active ? "Active" : "Inactive";
        await db.SaveChangesAsync(cancellationToken);
        await audit.LogAsync("Settings", "UpdateUtilityConnection", $"Updated utility connection {id}");
        return NoContent();
    }

    [HttpDelete("connections/{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var connection = await db.UtilityConnections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null) return NotFound();
        if (await db.UtilityBills.AnyAsync(b => b.UtilityConnectionId == id, cancellationToken))
        {
            connection.Status = "Inactive";
            connection.AutoFetchEnabled = false;
        }
        else db.UtilityConnections.Remove(connection);
        await db.SaveChangesAsync(cancellationToken);
        await audit.LogAsync("Settings", "DeleteUtilityConnection", $"Removed utility connection {id}");
        return NoContent();
    }

    [HttpPost("connections/{id:int}/fetch")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<UtilityFetchDto>> Fetch(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await billService.FetchAsync(id, cancellationToken);
            var bill = result.Bill is null ? null : await db.UtilityBills.AsNoTracking()
                .Include(b => b.UtilityConnection)!.ThenInclude(c => c!.Provider)
                .FirstAsync(b => b.Id == result.Bill.Id, cancellationToken);
            await audit.LogAsync("Settings", "FetchUtilityBill", $"Connection {id}: {result.Message}");
            return Ok(new UtilityFetchDto(result.BillAvailable, result.Created, result.Message, bill is null ? null : ToDto(bill)));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (HttpRequestException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "The utility provider is currently unavailable. Please try again later." }); }
    }

    [HttpGet("bills")]
    public async Task<ActionResult<IReadOnlyList<UtilityBillDto>>> Bills([FromQuery] int? connectionId, CancellationToken cancellationToken)
    {
        var query = db.UtilityBills.AsNoTracking().Include(b => b.UtilityConnection)!.ThenInclude(c => c!.Provider).AsQueryable();
        if (connectionId.HasValue) query = query.Where(b => b.UtilityConnectionId == connectionId.Value);
        return Ok((await query.OrderByDescending(b => b.BillingMonth).ThenByDescending(b => b.FetchedOn)
            .ToListAsync(cancellationToken)).Select(ToDto).ToList());
    }

    [HttpGet("bills/{id:int}")]
    public async Task<ActionResult<UtilityBillDto>> Bill(int id, CancellationToken cancellationToken)
    {
        var bill = await db.UtilityBills.AsNoTracking().Include(b => b.UtilityConnection)!.ThenInclude(c => c!.Provider)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        return bill is null ? NotFound() : Ok(ToDto(bill));
    }

    /// <summary>Records a bill settled on the biller's own site and books the matching expense. The
    /// expense is dated by when the money left the bank, not by the billing month, so the cash
    /// position moves in the month it actually moved.</summary>
    [HttpPost("bills/{id:int}/mark-paid")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<UtilityBillDto>> MarkPaid(int id, UtilityBillPaymentRequest request, CancellationToken cancellationToken)
    {
        var bill = await db.UtilityBills.Include(b => b.UtilityConnection)!.ThenInclude(c => c!.Provider)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bill is null) return NotFound();
        if (bill.ExpenseId.HasValue)
            return Conflict(new { message = "This bill is already marked paid and booked as an expense." });

        var paidOn = request.PaidOn.Date;
        var expense = new Expense
        {
            ExpenseDate = paidOn,
            Month = paidOn.Month,
            Year = paidOn.Year,
            Category = request.Category.Trim(),
            Description = $"{bill.UtilityConnection!.Provider!.ProviderName} bill {bill.BillingMonth:MMM yyyy}",
            Vendor = bill.UtilityConnection.Provider.ProviderName,
            Amount = request.Amount,
            PaymentMode = Clean(request.PaymentMode) ?? "UPI",
            Remarks = $"Consumer {bill.UtilityConnection.ConsumerNumber} · UTR {request.PaymentReference.Trim()}"
        };
        db.Expenses.Add(expense);

        bill.Status = "Paid";
        bill.PaidOn = paidOn;
        bill.PaymentReference = request.PaymentReference.Trim();
        bill.Expense = expense;   // EF stamps ExpenseId on commit

        await db.SaveChangesAsync(cancellationToken);
        await audit.LogAsync("Expenses", "MarkUtilityBillPaid",
            $"Bill {bill.Id} ({bill.UtilityConnection.ConsumerNumber}) marked paid, {request.Amount:0.00} booked as expense {expense.Id}");
        return Ok(ToDto(bill));
    }

    [HttpGet("bills/{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        var bill = await db.UtilityBills.AsNoTracking().Include(b => b.UtilityConnection)!.ThenInclude(c => c!.Provider)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bill is null) return NotFound();
        var name = $"{bill.UtilityConnection!.Provider!.Code}-{bill.UtilityConnection.ConsumerNumber}-{bill.BillingMonth:yyyy-MM}.html";
        return File(Encoding.UTF8.GetBytes(bill.RawHtml), "text/html; charset=utf-8", name);
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<IReadOnlyList<UtilityNotificationDto>>> Notifications(CancellationToken cancellationToken) =>
        Ok(await db.UtilityNotifications.AsNoTracking().OrderByDescending(n => n.CreatedOn).Take(50)
            .Select(n => new UtilityNotificationDto(n.Id, n.UtilityBillId, n.Title, n.Message, n.CreatedOn))
            .ToListAsync(cancellationToken));

    private UtilityConnectionDto ToConnectionDto(UtilityConnection c) => new(
        c.Id, c.ProviderId, c.Provider!.Code, c.Provider.ProviderName, c.Provider.Category,
        c.ConsumerNumber, c.ServiceNumber, c.AutoFetchEnabled, c.Status, c.CreatedOn,
        c.LastFetchedOn, c.LastFetchError, null,
        PayUrl(c.Provider.Code, c.ConsumerNumber));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
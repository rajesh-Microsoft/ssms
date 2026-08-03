using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Services;
using SMMS.Api.Services.Payments;
using SMMS.Api.Services.Storage;

namespace SMMS.Api.Controllers;

/// <summary>
/// Admin-only payment verification &amp; configuration: review uploaded proofs, approve/reject
/// (approval marks the charge Paid), view screenshots, and configure the society UPI/bank details
/// used to build member QR codes.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminPaymentsController(
    SmmsDbContext db,
    PaymentService payments,
    IFileStorage storage,
    AuditService audit) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static AdminPaymentProofDto ToDto(Models.PaymentProof p) => new(
        p.Id, p.CollectionId,
        p.Collection is not null ? PaymentNumbering.InvoiceNumber(p.Collection) : $"INV{p.CollectionId:D6}",
        p.MemberId, p.Member?.Name, p.Member?.Flat, p.Amount,
        p.Collection?.Month ?? 0, p.Collection?.Year ?? 0,
        p.Collection is not null ? PaymentNumbering.BillingLabel(p.Collection.Month, p.Collection.Year) : "",
        p.Status, p.UpiReference, p.StoredPath is not null,
        p.SubmittedAt, p.ReviewedAt, p.ReviewRemarks);

    [HttpGet("payment-proofs")]
    public async Task<ActionResult<IEnumerable<AdminPaymentProofDto>>> List(
        [FromQuery] string? status, [FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? flat)
    {
        var q = db.PaymentProofs.Include(p => p.Collection).Include(p => p.Member)
            .Where(p => p.Status != "Initiated");
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == status);
        if (month.HasValue) q = q.Where(p => p.Collection!.Month == month.Value);
        if (year.HasValue) q = q.Where(p => p.Collection!.Year == year.Value);
        if (!string.IsNullOrWhiteSpace(flat)) q = q.Where(p => p.Member!.Flat == flat);

        var rows = await q.OrderByDescending(p => p.SubmittedAt).ToListAsync();
        return Ok(rows.Select(ToDto));
    }

    [HttpGet("payment-dashboard")]
    public async Task<ActionResult<PaymentDashboardDto>> Dashboard()
    {
        var pending = await db.PaymentProofs
            .Include(p => p.Collection).Include(p => p.Member)
            .Where(p => p.Status == "Pending")
            .OrderBy(p => p.SubmittedAt)
            .ToListAsync();

        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var todayCollections = await db.Collections
            .Where(c => c.Status == "Paid" && c.PaymentDate != null && c.PaymentDate!.Value >= today)
            .SumAsync(c => (decimal?)c.Amount) ?? 0m;
        var monthCollections = await db.Collections
            .Where(c => c.Status == "Paid" && c.PaymentDate != null && c.PaymentDate!.Value >= monthStart)
            .SumAsync(c => (decimal?)c.Amount) ?? 0m;

        return Ok(new PaymentDashboardDto(
            pending.Count, pending.Sum(p => p.Amount), todayCollections, monthCollections,
            pending.Select(ToDto)));
    }

    [HttpPost("payment-proofs/{id:int}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var proof = await db.PaymentProofs.FirstOrDefaultAsync(p => p.Id == id);
        if (proof is null) return NotFound();
        if (proof.Status != "Pending") return BadRequest(new { message = $"Proof already {proof.Status}." });

        await payments.ApproveAsync(proof, CurrentUserId);
        return NoContent();
    }

    [HttpPost("payment-proofs/{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, RejectPaymentRequest request)
    {
        var proof = await db.PaymentProofs.FirstOrDefaultAsync(p => p.Id == id);
        if (proof is null) return NotFound();
        if (proof.Status != "Pending") return BadRequest(new { message = $"Proof already {proof.Status}." });

        await payments.RejectAsync(proof, CurrentUserId, request.Remarks);
        return NoContent();
    }

    [HttpGet("payment-proofs/{id:int}/screenshot")]
    public async Task<IActionResult> Screenshot(int id)
    {
        var proof = await db.PaymentProofs.FirstOrDefaultAsync(p => p.Id == id);
        if (proof is null || proof.StoredPath is null) return NotFound();
        var bytes = await storage.ReadAsync(proof.StoredPath);
        if (bytes is null) return NotFound();
        return File(bytes, proof.ContentType ?? "application/octet-stream");
    }

    [HttpGet("upi-settings")]
    public async Task<ActionResult<UpiSettingsDto>> GetUpiSettings()
    {
        var s = await db.Settings.FirstOrDefaultAsync();
        if (s is null) return Ok(new UpiSettingsDto(null, null, null, null, null, null));
        return Ok(new UpiSettingsDto(s.UpiId, s.UpiPayeeName, s.BankName, s.BankAccountName, s.BankAccountNumber, s.BankIfsc));
    }

    [HttpPut("upi-settings")]
    public async Task<IActionResult> SaveUpiSettings(UpiSettingsDto request)
    {
        var s = await db.Settings.FirstOrDefaultAsync();
        if (s is null) return NotFound(new { message = "Society settings not found." });

        s.UpiId = request.UpiId?.Trim();
        s.UpiPayeeName = request.UpiPayeeName?.Trim();
        s.BankName = request.BankName?.Trim();
        s.BankAccountName = request.BankAccountName?.Trim();
        s.BankAccountNumber = request.BankAccountNumber?.Trim();
        s.BankIfsc = request.BankIfsc?.Trim();
        await db.SaveChangesAsync();
        await audit.LogAsync("Settings", "UpdateUpi", "Updated society UPI/bank collection settings");
        return NoContent();
    }
}

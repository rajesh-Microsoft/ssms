using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;
using SMMS.Api.Services.Payments;
using SMMS.Api.Services.Storage;

namespace SMMS.Api.Controllers;

/// <summary>
/// Resident self-service payment endpoints. A member sees only their OWN charges (matched by
/// flat, like MeController) — invoice ownership is validated on every call so one resident can
/// never fetch another's QR, screenshot or history.
/// </summary>
[ApiController]
[Route("api/member")]
[Authorize]
public class MemberPaymentsController(
    SmmsDbContext db,
    PaymentService payments,
    IPaymentGateway gateway,
    QrService qr,
    IFileStorage storage) : ControllerBase
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Resolves the logged-in user's linked Member record via flat (case-insensitive).</summary>
    private async Task<Member?> ResolveMemberAsync()
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null || string.IsNullOrWhiteSpace(user.Flat)) return null;
        var flat = user.Flat!.ToLower();
        return await db.Members.FirstOrDefaultAsync(m => m.Flat.ToLower() == flat);
    }

    [HttpGet("pending-invoices")]
    public async Task<ActionResult<IEnumerable<PendingInvoiceDto>>> PendingInvoices()
    {
        var member = await ResolveMemberAsync();
        if (member is null) return Ok(Array.Empty<PendingInvoiceDto>());

        var settings = await db.Settings.FirstOrDefaultAsync();
        var dueDay = settings?.DueDay ?? 5;

        var charges = await db.Collections
            .Where(c => c.MemberId == member.Id && c.Status != "Paid")
            .OrderBy(c => c.Year).ThenBy(c => c.Month)
            .ToListAsync();

        var pendingProofCollectionIds = await db.PaymentProofs
            .Where(p => p.MemberId == member.Id && p.Status == "Pending")
            .Select(p => p.CollectionId)
            .ToListAsync();

        var today = DateTime.UtcNow.Date;
        var dtos = charges.Select(c =>
        {
            var due = PaymentNumbering.DueDate(c, dueDay);
            return new PendingInvoiceDto(
                c.Id, PaymentNumbering.InvoiceNumber(c), c.Amount, c.Status, c.Month, c.Year,
                PaymentNumbering.BillingLabel(c.Month, c.Year), due, due.Date < today,
                pendingProofCollectionIds.Contains(c.Id));
        });
        return Ok(dtos);
    }

    /// <summary>UPI URI + note for a charge (text form), for display alongside the QR image.</summary>
    [HttpGet("qrcode/{collectionId:int}/payload")]
    public async Task<ActionResult<QrPayloadDto>> QrPayload(int collectionId)
    {
        var (charge, member, settings, error) = await LoadPayableAsync(collectionId);
        if (error is not null) return error;

        var instruction = gateway.CreateInstruction(settings!, charge!, member!);
        var payeeName = string.IsNullOrWhiteSpace(settings!.UpiPayeeName) ? settings.SocietyName : settings.UpiPayeeName!;
        return Ok(new QrPayloadDto(
            charge!.Id, instruction.InvoiceNumber, instruction.Amount,
            instruction.Payload, instruction.Note, payeeName, settings.UpiId!));
    }

    /// <summary>The dynamic UPI QR as a PNG. Amount is baked in server-side (never client-editable).</summary>
    [HttpGet("qrcode/{collectionId:int}")]
    public async Task<IActionResult> QrImage(int collectionId)
    {
        var (charge, member, settings, error) = await LoadPayableAsync(collectionId);
        if (error is not null) return error;

        var instruction = gateway.CreateInstruction(settings!, charge!, member!);
        var png = qr.PngFromText(instruction.Payload);
        return File(png, "image/png");
    }

    [HttpPost("upload-payment-proof")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<PaymentProofDto>> UploadProof(
        [FromForm] int collectionId, [FromForm] string? upiReference, IFormFile? screenshot)
    {
        var member = await ResolveMemberAsync();
        if (member is null) return Forbid();

        var charge = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
        if (charge is null) return NotFound(new { message = "Invoice not found." });
        if (charge.MemberId != member.Id) return Forbid();                       // ownership
        if (charge.Status == "Paid") return BadRequest(new { message = "This invoice is already paid." });

        var alreadyPending = await db.PaymentProofs
            .AnyAsync(p => p.CollectionId == collectionId && p.Status == "Pending");
        if (alreadyPending) return BadRequest(new { message = "A payment for this invoice is already awaiting verification." });

        Stream? stream = null;
        string? fileName = null, contentType = null;
        if (screenshot is { Length: > 0 })
        {
            if (screenshot.Length > MaxUploadBytes) return BadRequest(new { message = "Screenshot must be under 5 MB." });
            if (!screenshot.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Only image files are allowed." });
            stream = screenshot.OpenReadStream();
            fileName = screenshot.FileName;
            contentType = screenshot.ContentType;
        }

        var proof = await payments.SubmitProofAsync(
            charge, member, CurrentUserId, stream, fileName, contentType, upiReference);

        return Ok(new PaymentProofDto(
            proof.Id, proof.CollectionId, PaymentNumbering.InvoiceNumber(charge), proof.Amount,
            proof.Status, proof.UpiReference, proof.StoredPath is not null,
            proof.SubmittedAt, proof.ReviewedAt, proof.ReviewRemarks));
    }

    [HttpGet("payment-history")]
    public async Task<ActionResult<IEnumerable<PaymentProofDto>>> History()
    {
        var member = await ResolveMemberAsync();
        if (member is null) return Ok(Array.Empty<PaymentProofDto>());

        var rows = await db.PaymentProofs
            .Include(p => p.Collection)
            .Where(p => p.MemberId == member.Id)
            .OrderByDescending(p => p.SubmittedAt)
            .ToListAsync();

        return Ok(rows.Select(p => new PaymentProofDto(
            p.Id, p.CollectionId,
            p.Collection is not null ? PaymentNumbering.InvoiceNumber(p.Collection) : $"INV{p.CollectionId:D6}",
            p.Amount, p.Status, p.UpiReference, p.StoredPath is not null,
            p.SubmittedAt, p.ReviewedAt, p.ReviewRemarks)));
    }

    [HttpGet("payment-proof/{id:int}/screenshot")]
    public async Task<IActionResult> Screenshot(int id)
    {
        var member = await ResolveMemberAsync();
        if (member is null) return Forbid();
        var proof = await db.PaymentProofs.FirstOrDefaultAsync(p => p.Id == id);
        if (proof is null || proof.StoredPath is null) return NotFound();
        if (proof.MemberId != member.Id) return Forbid();                        // ownership

        var bytes = await storage.ReadAsync(proof.StoredPath);
        if (bytes is null) return NotFound();
        return File(bytes, proof.ContentType ?? "application/octet-stream");
    }

    /// <summary>Loads a charge for the current member and validates it can be paid via UPI.</summary>
    private async Task<(Collection? charge, Member? member, SocietySettings? settings, ObjectResult? error)> LoadPayableAsync(int collectionId)
    {
        var member = await ResolveMemberAsync();
        if (member is null) return (null, null, null, StatusCode(StatusCodes.Status403Forbidden, new { message = "No linked flat." }));

        var charge = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
        if (charge is null) return (null, null, null, NotFound(new { message = "Invoice not found." }));
        if (charge.MemberId != member.Id) return (null, null, null, StatusCode(StatusCodes.Status403Forbidden, new { message = "Not your invoice." }));
        if (charge.Status == "Paid") return (null, null, null, BadRequest(new { message = "This invoice is already paid." }));

        var settings = await db.Settings.FirstOrDefaultAsync();
        if (settings is null || string.IsNullOrWhiteSpace(settings.UpiId))
            return (null, null, null, BadRequest(new { message = "Online payment is not configured for this society yet." }));

        return (charge, member, settings, null);
    }
}

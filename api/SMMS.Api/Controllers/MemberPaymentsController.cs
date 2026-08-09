using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Data.Tenancy;
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
    RazorpayPaymentGateway razorpay,
    QrService qr,
    IFileStorage storage,
    ITenantContext tenantContext) : ControllerBase
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

    [HttpGet("payment-options")]
    public async Task<ActionResult<PaymentOptionsDto>> PaymentOptions()
    {
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync();
        var online = razorpay.IsConfigured && settings?.OnlinePaymentsEnabled == true;
        return Ok(new PaymentOptionsDto(
            online,
            !string.IsNullOrWhiteSpace(settings?.UpiId),
            online && razorpay.IsTestMode));
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
            .Where(p => p.MemberId == member.Id && p.Status == "Pending" && p.GatewayName == "ManualUPI")
            .Select(p => p.CollectionId)
            .ToListAsync();

        var today = DateTime.UtcNow.Date;
        var dtos = charges.Select(c =>
        {
            var due = PaymentNumbering.DueDate(c, dueDay);
            // One-time charges show their friendly title; monthly invoices show the month/year label.
            var label = c.CollectionType == "OneTime" && !string.IsNullOrWhiteSpace(c.Title)
                ? c.Title!
                : PaymentNumbering.BillingLabel(c.Month, c.Year);
            return new PendingInvoiceDto(
                c.Id, PaymentNumbering.InvoiceNumber(c), c.Amount, c.Status, c.Month, c.Year,
                label, due, due.Date < today,
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

    [HttpPost("razorpay/order/{collectionId:int}")]
    public async Task<ActionResult<RazorpayOrderDto>> CreateRazorpayOrder(
        int collectionId, CancellationToken ct)
    {
        if (!razorpay.IsConfigured)
            return BadRequest(new { message = "Razorpay test mode is not configured." });

        var (charge, member, settings, error) = await LoadPayableAsync(collectionId, requireUpi: false);
        if (error is not null) return error;

        if (!settings!.OnlinePaymentsEnabled)
            return BadRequest(new { message = "This society has not enabled online card payments." });

        var activeAttempt = await db.PaymentProofs
            .Where(p => p.CollectionId == collectionId
                && p.MemberId == member!.Id
                && p.GatewayName == "Razorpay"
                && p.Status == "Initiated"
                && p.SubmittedAt >= DateTime.UtcNow.AddMinutes(-15))
            .OrderByDescending(p => p.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        RazorpayOrder order;
        if (activeAttempt is not null)
        {
            order = new RazorpayOrder(
                activeAttempt.GatewayReference!,
                RazorpayPaymentGateway.ToPaise(activeAttempt.Amount),
                "INR");
        }
        else
        {
            var invoiceNumber = PaymentNumbering.InvoiceNumber(charge!);
            try
            {
                order = await razorpay.CreateOrderAsync(
                    charge!.Amount,
                    $"smms-{charge.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    invoiceNumber,
                    member!.Flat,
                    tenantContext.Current!.Key,
                    ct);
            }
            catch (InvalidOperationException ex)
            {
                // Gateway outage or bad credentials: let the client fall back to manual UPI.
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }

            db.PaymentProofs.Add(new PaymentProof
            {
                CollectionId = charge.Id,
                MemberId = member.Id,
                SubmittedByUserId = CurrentUserId,
                Amount = charge.Amount,
                Status = "Initiated",
                SubmittedAt = DateTime.UtcNow,
                GatewayName = "Razorpay",
                GatewayReference = order.Id
            });
            await db.SaveChangesAsync(ct);
        }

        return Ok(new RazorpayOrderDto(
            razorpay.KeyId,
            order.Id,
            order.Amount,
            order.Currency,
            settings!.SocietyName,
            $"Maintenance {PaymentNumbering.InvoiceNumber(charge!)}",
            member!.Name,
            member.Email,
            member.Mobile));
    }

    // Deliberately not gated on OnlinePaymentsEnabled: the money has already left the resident's
    // account by the time we get here, so an admin toggling the setting mid-checkout must not
    // strand a real payment. New orders are blocked instead.
    [HttpPost("razorpay/verify")]
    public async Task<ActionResult<RazorpayVerifyResponse>> VerifyRazorpayPayment(
        RazorpayVerifyRequest request, CancellationToken ct)
    {
        if (!razorpay.IsConfigured)
            return BadRequest(new { message = "Razorpay test mode is not configured." });
        if (string.IsNullOrWhiteSpace(request.OrderId)
            || string.IsNullOrWhiteSpace(request.PaymentId)
            || string.IsNullOrWhiteSpace(request.Signature))
            return BadRequest(new { message = "Razorpay returned an incomplete payment response." });

        var member = await ResolveMemberAsync();
        if (member is null) return Forbid();

        var attempt = await db.PaymentProofs
            .FirstOrDefaultAsync(p => p.GatewayName == "Razorpay"
                && p.GatewayReference == request.OrderId
                && p.MemberId == member.Id, ct);
        if (attempt is null) return NotFound(new { message = "Payment order not found." });

        if (!razorpay.VerifyCheckoutSignature(attempt.GatewayReference!, request.PaymentId, request.Signature))
            return BadRequest(new { message = "Razorpay payment signature is invalid." });
        if (attempt.Status == "Approved")
        {
            if (attempt.UpiReference != request.PaymentId)
                return BadRequest(new { message = "Payment order was already completed by a different payment." });
            return Ok(new RazorpayVerifyResponse("Approved", request.PaymentId, attempt.CollectionId));
        }

        RazorpayPayment payment;
        try
        {
            payment = await razorpay.FetchPaymentAsync(request.PaymentId, ct);
        }
        catch (InvalidOperationException ex)
        {
            // The webhook is the safety net: it settles this attempt once Razorpay reaches us.
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }

        if (payment.OrderId != attempt.GatewayReference
            || payment.Amount != RazorpayPaymentGateway.ToPaise(attempt.Amount)
            || payment.Currency != "INR")
            return BadRequest(new { message = "Razorpay payment does not match this invoice." });
        if (payment.Status != "captured")
            return BadRequest(new { message = $"Payment is {payment.Status}; it must be captured before the invoice is marked paid." });

        await payments.ApproveGatewayPaymentAsync(attempt, payment.Id, ct);
        return Ok(new RazorpayVerifyResponse("Approved", payment.Id, attempt.CollectionId));
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
            var problem = UploadRules.Validate(screenshot, UploadRules.Images, MaxUploadBytes);
            if (problem is not null) return BadRequest(new { message = problem });
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
            .Where(p => p.MemberId == member.Id && p.Status != "Initiated")
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
    private async Task<(Collection? charge, Member? member, SocietySettings? settings, ObjectResult? error)> LoadPayableAsync(
        int collectionId, bool requireUpi = true)
    {
        var member = await ResolveMemberAsync();
        if (member is null) return (null, null, null, StatusCode(StatusCodes.Status403Forbidden, new { message = "No linked flat." }));

        var charge = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
        if (charge is null) return (null, null, null, NotFound(new { message = "Invoice not found." }));
        if (charge.MemberId != member.Id) return (null, null, null, StatusCode(StatusCodes.Status403Forbidden, new { message = "Not your invoice." }));
        if (charge.Status == "Paid") return (null, null, null, BadRequest(new { message = "This invoice is already paid." }));

        var settings = await db.Settings.FirstOrDefaultAsync();
        if (settings is null)
            return (null, null, null, BadRequest(new { message = "Society payment settings are unavailable." }));
        if (requireUpi && string.IsNullOrWhiteSpace(settings.UpiId))
            return (null, null, null, BadRequest(new { message = "Online payment is not configured for this society yet." }));

        return (charge, member, settings, null);
    }
}

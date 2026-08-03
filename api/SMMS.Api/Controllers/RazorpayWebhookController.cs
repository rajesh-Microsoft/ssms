using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Services.Payments;

namespace SMMS.Api.Controllers;

/// <summary>
/// Server-to-server settlement path for Razorpay. The browser-driven /api/member/razorpay/verify
/// call is best-effort — if the resident closes the tab between capture and verification the
/// invoice would stay unpaid. Razorpay retries this webhook until it gets a 2xx, so it is the
/// authoritative source of truth.
///
/// The endpoint is anonymous but is protected by an HMAC-SHA256 signature over the raw body using
/// the webhook secret; nothing in the payload is trusted until that check passes. It is also
/// tenant-less (Razorpay posts to a single platform host), so the society is taken from the order
/// notes that were stamped at order-creation time and used to open that tenant's database.
/// </summary>
[ApiController]
[Route("api/webhooks/razorpay")]
[AllowAnonymous]
public class RazorpayWebhookController(
    RazorpayPaymentGateway razorpay,
    ITenantStore tenantStore,
    IServiceScopeFactory scopeFactory,
    ILogger<RazorpayWebhookController> logger) : ControllerBase
{
    private const int MaxBodyBytes = 128 * 1024;

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (!razorpay.IsWebhookConfigured)
        {
            logger.LogWarning("Razorpay webhook received but no webhook secret is configured.");
            return NotFound();
        }

        var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(signature)) return Unauthorized();

        string rawBody;
        using (var reader = new StreamReader(Request.Body))
        {
            var buffer = new char[MaxBodyBytes + 1];
            var read = await reader.ReadBlockAsync(buffer, ct);
            if (read > MaxBodyBytes) return BadRequest();
            rawBody = new string(buffer, 0, read);
        }

        if (!razorpay.VerifyWebhookSignature(rawBody, signature))
        {
            logger.LogWarning("Razorpay webhook signature verification failed.");
            return Unauthorized();
        }

        RazorpayWebhookEvent? evt;
        try
        {
            evt = ParseEvent(rawBody);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        // Acknowledge anything we don't act on, otherwise Razorpay keeps retrying it forever.
        if (evt is null) return Ok(new { status = "ignored" });

        var tenant = tenantStore.GetByKey(evt.Society);
        if (tenant is null)
        {
            logger.LogWarning("Razorpay webhook referenced unknown society {Society}.", evt.Society);
            return Ok(new { status = "ignored" });
        }

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Current = tenant;
        var db = scope.ServiceProvider.GetRequiredService<SmmsDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentService>();

        var attempt = await db.PaymentProofs.FirstOrDefaultAsync(
            p => p.GatewayName == "Razorpay" && p.GatewayReference == evt.OrderId, ct);
        if (attempt is null)
        {
            logger.LogWarning("Razorpay webhook referenced unknown order {OrderId}.", evt.OrderId);
            return Ok(new { status = "ignored" });
        }

        if (attempt.Status == "Approved")
            return Ok(new { status = "already-settled" });

        if (evt.Currency != "INR" || evt.Amount != RazorpayPaymentGateway.ToPaise(attempt.Amount))
        {
            logger.LogWarning(
                "Razorpay webhook amount mismatch for order {OrderId}: got {Amount} {Currency}.",
                evt.OrderId, evt.Amount, evt.Currency);
            return Ok(new { status = "mismatch" });
        }

        await payments.ApproveGatewayPaymentAsync(attempt, evt.PaymentId, ct);
        logger.LogInformation(
            "Razorpay webhook settled payment {PaymentId} for society {Society}.", evt.PaymentId, evt.Society);
        return Ok(new { status = "settled" });
    }

    /// <summary>Returns the captured-payment details, or null for events we do not act on.</summary>
    private static RazorpayWebhookEvent? ParseEvent(string rawBody)
    {
        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;

        if (!root.TryGetProperty("event", out var eventName)
            || eventName.GetString() is not ("payment.captured" or "order.paid"))
            return null;

        if (!root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("payment", out var paymentWrapper)
            || !paymentWrapper.TryGetProperty("entity", out var payment))
            return null;

        if (payment.TryGetProperty("status", out var status) && status.GetString() != "captured")
            return null;

        var paymentId = payment.TryGetProperty("id", out var id) ? id.GetString() : null;
        var orderId = payment.TryGetProperty("order_id", out var order) ? order.GetString() : null;
        var society = payment.TryGetProperty("notes", out var notes)
            && notes.ValueKind == JsonValueKind.Object
            && notes.TryGetProperty("society", out var societyNote)
                ? societyNote.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(paymentId)
            || string.IsNullOrWhiteSpace(orderId)
            || string.IsNullOrWhiteSpace(society))
            return null;

        var amount = payment.TryGetProperty("amount", out var amt) ? amt.GetInt64() : 0;
        var currency = payment.TryGetProperty("currency", out var cur) ? cur.GetString() : null;

        return new RazorpayWebhookEvent(society, orderId, paymentId, amount, currency ?? "");
    }

    private sealed record RazorpayWebhookEvent(
        string Society, string OrderId, string PaymentId, long Amount, string Currency);
}

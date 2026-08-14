using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Services.Payments;

namespace SMMS.Api.Controllers;

/// <summary>
/// Server-to-server settlement path for PhonePe. The browser redirect after checkout is
/// best-effort — a resident who closes the tab would otherwise leave the invoice unpaid — so
/// PhonePe also posts here and retries until it gets a 2xx.
///
/// The request is tenant-less (PhonePe posts to one platform host), so the society is taken from
/// metaInfo, falling back to the merchant order id. The shared-secret header only proves the
/// caller knows the secret; the payment is always re-confirmed against the Order Status API
/// before any invoice is marked paid.
/// </summary>
[ApiController]
[Route("api/webhooks/phonepe")]
[AllowAnonymous]
public class PhonePeWebhookController(
    IServiceScopeFactory scopeFactory,
    ITenantStore tenantStore,
    ILogger<PhonePeWebhookController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);

        PhonePeWebhookEvent? evt;
        try
        {
            evt = ParseEvent(rawBody);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        // Acknowledge anything we do not act on, otherwise PhonePe retries it forever.
        if (evt is null) return Ok(new { status = "ignored" });

        var tenant = tenantStore.GetByKey(evt.Society);
        if (tenant is null)
        {
            logger.LogWarning("PhonePe webhook referenced unknown society {Society}.", evt.Society);
            return Ok(new { status = "ignored" });
        }

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Current = tenant;
        var gateway = scope.ServiceProvider.GetRequiredService<PhonePePaymentGateway>();

        if (!gateway.IsWebhookConfigured)
        {
            logger.LogWarning("PhonePe webhook received but no webhook credentials are configured.");
            return NotFound();
        }

        if (!gateway.VerifyWebhookAuthorization(Request.Headers.Authorization.FirstOrDefault()))
        {
            logger.LogWarning("PhonePe webhook authorization failed for society {Society}.", evt.Society);
            return Unauthorized();
        }

        var db = scope.ServiceProvider.GetRequiredService<SmmsDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentService>();

        var attempt = await db.PaymentProofs.FirstOrDefaultAsync(
            p => p.GatewayName == "PhonePe" && p.GatewayReference == evt.MerchantOrderId, ct);
        if (attempt is null)
        {
            logger.LogWarning("PhonePe webhook referenced unknown order {OrderId}.", evt.MerchantOrderId);
            return Ok(new { status = "ignored" });
        }

        if (attempt.Status == "Approved") return Ok(new { status = "already-settled" });

        PhonePeOrderStatus status;
        try
        {
            status = await gateway.GetOrderStatusAsync(evt.MerchantOrderId, ct);
        }
        catch (Exception ex)
        {
            // A 5xx makes PhonePe retry, which is what we want when we could not confirm.
            logger.LogError(ex, "PhonePe status confirmation failed for {OrderId}.", evt.MerchantOrderId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (status.State != "COMPLETED")
            return Ok(new { status = "not-completed" });

        if (status.Amount != PhonePePaymentGateway.ToPaise(attempt.Amount))
        {
            logger.LogWarning(
                "PhonePe amount mismatch for {OrderId}: gateway {Amount} vs invoice {Expected}.",
                evt.MerchantOrderId, status.Amount, PhonePePaymentGateway.ToPaise(attempt.Amount));
            return Ok(new { status = "mismatch" });
        }

        await payments.ApproveGatewayPaymentAsync(attempt, status.TransactionId ?? status.OrderId, ct);
        logger.LogInformation(
            "PhonePe settled order {OrderId} for society {Society}.", evt.MerchantOrderId, evt.Society);
        return Ok(new { status = "settled" });
    }

    /// <summary>Returns the completed-order details, or null for events we do not act on.</summary>
    private static PhonePeWebhookEvent? ParseEvent(string rawBody)
    {
        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;

        // PhonePe documents "event" as authoritative and "type" as ignorable.
        if (!root.TryGetProperty("event", out var eventName)
            || eventName.GetString() != "checkout.order.completed")
            return null;

        if (!root.TryGetProperty("payload", out var payload)) return null;

        var merchantOrderId = payload.TryGetProperty("merchantOrderId", out var moid) ? moid.GetString() : null;
        if (string.IsNullOrWhiteSpace(merchantOrderId)) return null;

        var society = payload.TryGetProperty("metaInfo", out var meta)
            && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("udf1", out var udf1)
                ? udf1.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(society))
            society = PhonePePaymentGateway.ParseMerchantOrderId(merchantOrderId)?.Society;

        return string.IsNullOrWhiteSpace(society)
            ? null
            : new PhonePeWebhookEvent(society, merchantOrderId);
    }

    private sealed record PhonePeWebhookEvent(string Society, string MerchantOrderId);
}

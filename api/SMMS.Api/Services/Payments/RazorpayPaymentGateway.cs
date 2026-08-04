using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SMMS.Api.Data.Tenancy;

namespace SMMS.Api.Services.Payments;

public sealed class RazorpayOptions
{
    public bool Enabled { get; set; }
    public string? KeyId { get; set; }
    public string? KeySecret { get; set; }
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Societies allowed to take card payments, comma separated, or "*" for every
    /// society. Opt in is deliberate: a test key that reaches a real society lets a
    /// resident clear a real due without any money moving.
    /// </summary>
    public string? AllowedSocieties { get; set; }
}

public sealed record RazorpayOrder(string Id, long Amount, string Currency);
public sealed record RazorpayPayment(string Id, string OrderId, long Amount, string Currency, string Status);

public sealed class RazorpayPaymentGateway(
    HttpClient httpClient,
    IOptions<RazorpayOptions> configuredOptions,
    ITenantContext tenantContext,
    ILogger<RazorpayPaymentGateway> logger)
{
    private readonly RazorpayOptions options = configuredOptions.Value;

    private bool IsAllowedForCurrentSociety
    {
        get
        {
            var allowed = options.AllowedSocieties;
            if (string.IsNullOrWhiteSpace(allowed)) return false;
            if (allowed.Trim() == "*") return true;

            var society = tenantContext.Current?.Key;
            if (string.IsNullOrWhiteSpace(society)) return false;

            return allowed
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(entry => string.Equals(entry, society, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool IsConfigured => options.Enabled
        && !string.IsNullOrWhiteSpace(options.KeyId)
        && !string.IsNullOrWhiteSpace(options.KeySecret)
        && IsAllowedForCurrentSociety;

    public bool IsTestMode => IsConfigured
        && options.KeyId!.StartsWith("rzp_test_", StringComparison.OrdinalIgnoreCase);

    public bool IsWebhookConfigured => !string.IsNullOrWhiteSpace(options.WebhookSecret);

    public string KeyId => IsConfigured
        ? options.KeyId!
        : throw new InvalidOperationException("Razorpay test mode is not configured.");

    public async Task<RazorpayOrder> CreateOrderAsync(
        decimal amount, string receipt, string invoiceNumber, string flat, string society,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        var amountInPaise = ToPaise(amount);
        using var request = CreateRequest(HttpMethod.Post, "orders");
        request.Content = JsonContent.Create(new
        {
            amount = amountInPaise,
            currency = "INR",
            receipt,
            // "society" is echoed back on webhooks, which arrive with no tenant subdomain.
            notes = new { invoice = invoiceNumber, flat, society }
        });

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Razorpay order creation failed with HTTP {StatusCode}.", (int)response.StatusCode);
            throw new InvalidOperationException("Razorpay could not create the payment order. Check the test credentials and try again.");
        }

        using var json = JsonDocument.Parse(body);
        return new RazorpayOrder(
            RequiredString(json.RootElement, "id"),
            json.RootElement.GetProperty("amount").GetInt64(),
            RequiredString(json.RootElement, "currency"));
    }

    public async Task<RazorpayPayment> FetchPaymentAsync(string paymentId, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = CreateRequest(HttpMethod.Get, $"payments/{Uri.EscapeDataString(paymentId)}");
        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Razorpay payment lookup failed with HTTP {StatusCode}.", (int)response.StatusCode);
            throw new InvalidOperationException("Razorpay could not verify the payment status.");
        }

        using var json = JsonDocument.Parse(body);
        return new RazorpayPayment(
            RequiredString(json.RootElement, "id"),
            RequiredString(json.RootElement, "order_id"),
            json.RootElement.GetProperty("amount").GetInt64(),
            RequiredString(json.RootElement, "currency"),
            RequiredString(json.RootElement, "status"));
    }

    public bool VerifyCheckoutSignature(string orderId, string paymentId, string signature)
    {
        EnsureConfigured();
        var message = Encoding.UTF8.GetBytes($"{orderId}|{paymentId}");
        var secret = Encoding.UTF8.GetBytes(options.KeySecret!);
        var expected = HMACSHA256.HashData(secret, message);

        try
        {
            var supplied = Convert.FromHexString(signature);
            return supplied.Length == expected.Length
                && CryptographicOperations.FixedTimeEquals(supplied, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Verifies the X-Razorpay-Signature header over the exact raw webhook body.</summary>
    public bool VerifyWebhookSignature(string rawBody, string signature)
    {
        if (!IsWebhookConfigured) return false;
        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(options.WebhookSecret!),
            Encoding.UTF8.GetBytes(rawBody));

        try
        {
            var supplied = Convert.FromHexString(signature);
            return supplied.Length == expected.Length
                && CryptographicOperations.FixedTimeEquals(supplied, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static long ToPaise(decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("Payment amount must be positive and have at most two decimal places.");
        return checked((long)(amount * 100m));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.KeyId}:{options.KeySecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        return request;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Razorpay test mode is not configured.");
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || string.IsNullOrWhiteSpace(property.GetString()))
            throw new InvalidOperationException($"Razorpay returned an invalid {propertyName} value.");
        return property.GetString()!;
    }
}
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SMMS.Api.Data.Tenancy;

namespace SMMS.Api.Services.Payments;

public sealed class PhonePeOptions
{
    public bool Enabled { get; set; }

    /// <summary>"Sandbox" or "Production". Sandbox money is not real.</summary>
    public string Environment { get; set; } = "Sandbox";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string ClientVersion { get; set; } = "1";

    /// <summary>Credentials configured on the PhonePe dashboard webhook (SHA auth).</summary>
    public string? WebhookUsername { get; set; }
    public string? WebhookPassword { get; set; }

    /// <summary>Seconds before the checkout session expires. PhonePe allows 300-3600.</summary>
    public int ExpireAfterSeconds { get; set; } = 1200;

    /// <summary>
    /// Societies allowed to take PhonePe payments, comma separated, or "*" for every society.
    /// Opt in is deliberate: sandbox credentials reaching a real society would let a resident
    /// clear a real due without any money moving.
    /// </summary>
    public string? AllowedSocieties { get; set; }
}

public sealed record PhonePeCheckout(string MerchantOrderId, string OrderId, string RedirectUrl);

/// <summary>State is PENDING, COMPLETED or FAILED; <paramref name="Amount"/> is in paisa.</summary>
public sealed record PhonePeOrderStatus(string OrderId, string State, long Amount, string? TransactionId);

/// <summary>
/// PhonePe Standard Checkout (v2). Authentication is OAuth client-credentials: a short-lived
/// O-Bearer token is fetched and cached, then sent on every call. The resident is redirected to
/// a PhonePe-hosted page, so no card or UPI credentials ever reach SMMS.
/// </summary>
public sealed class PhonePePaymentGateway(
    HttpClient httpClient,
    IOptions<PhonePeOptions> configuredOptions,
    ITenantContext tenantContext,
    ILogger<PhonePePaymentGateway> logger)
{
    private readonly PhonePeOptions options = configuredOptions.Value;
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? cachedToken;
    private DateTimeOffset tokenExpiresAt = DateTimeOffset.MinValue;

    public bool IsSandbox => !string.Equals(options.Environment, "Production", StringComparison.OrdinalIgnoreCase);

    private string AuthUrl => IsSandbox
        ? "https://api-preprod.phonepe.com/apis/pg-sandbox/v1/oauth/token"
        : "https://api.phonepe.com/apis/identity-manager/v1/oauth/token";

    private string ApiBase => IsSandbox
        ? "https://api-preprod.phonepe.com/apis/pg-sandbox/"
        : "https://api.phonepe.com/apis/pg/";

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
        && !string.IsNullOrWhiteSpace(options.ClientId)
        && !string.IsNullOrWhiteSpace(options.ClientSecret)
        && IsAllowedForCurrentSociety;

    public bool IsWebhookConfigured => !string.IsNullOrWhiteSpace(options.WebhookUsername)
        && !string.IsNullOrWhiteSpace(options.WebhookPassword);

    /// <summary>
    /// Encodes the society and invoice into the merchant order id. Webhooks arrive on a
    /// tenant-less host, so this (plus metaInfo) is how the right database is found again.
    /// Underscore separates the parts because a society key may contain hyphens.
    /// </summary>
    public static string BuildMerchantOrderId(string society, int collectionId)
    {
        var id = $"SMMS_{society}_{collectionId}_{Guid.NewGuid():N}";
        return id.Length <= 63 ? id : id[..63];
    }

    public static (string Society, int CollectionId)? ParseMerchantOrderId(string? merchantOrderId)
    {
        if (string.IsNullOrWhiteSpace(merchantOrderId)) return null;
        var parts = merchantOrderId.Split('_');
        if (parts.Length < 4 || parts[0] != "SMMS") return null;
        return int.TryParse(parts[^2], out var collectionId)
            ? (string.Join('_', parts[1..^2]), collectionId)
            : null;
    }

    public async Task<PhonePeCheckout> CreateCheckoutAsync(
        string merchantOrderId, decimal amount, string redirectUrl,
        string society, int collectionId, string invoiceNumber, string flat,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}checkout/v2/pay");
        request.Headers.Authorization = new AuthenticationHeaderValue("O-Bearer", await GetTokenAsync(ct));
        request.Content = JsonContent.Create(new
        {
            merchantOrderId,
            amount = ToPaise(amount),
            expireAfter = Math.Clamp(options.ExpireAfterSeconds, 300, 3600),
            paymentFlow = new
            {
                type = "PG_CHECKOUT",
                message = $"Maintenance {invoiceNumber}",
                merchantUrls = new { redirectUrl }
            },
            // Echoed back on status and webhook payloads; udf names are fixed by PhonePe.
            metaInfo = new { udf1 = society, udf2 = collectionId.ToString(), udf3 = invoiceNumber, udf4 = flat }
        });

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("PhonePe checkout creation failed with HTTP {StatusCode}.", (int)response.StatusCode);
            throw new InvalidOperationException("PhonePe could not start the payment. Please try again.");
        }

        using var json = JsonDocument.Parse(body);
        return new PhonePeCheckout(
            merchantOrderId,
            RequiredString(json.RootElement, "orderId"),
            RequiredString(json.RootElement, "redirectUrl"));
    }

    /// <summary>Authoritative server-to-server confirmation. Never settle on a callback alone.</summary>
    public async Task<PhonePeOrderStatus> GetOrderStatusAsync(string merchantOrderId, CancellationToken ct = default)
    {
        EnsureConfigured();
        var path = $"{ApiBase}checkout/v2/order/{Uri.EscapeDataString(merchantOrderId)}/status?details=false";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("O-Bearer", await GetTokenAsync(ct));

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("PhonePe order status failed with HTTP {StatusCode}.", (int)response.StatusCode);
            throw new InvalidOperationException("PhonePe could not confirm the payment status.");
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        string? transactionId = null;
        if (root.TryGetProperty("paymentDetails", out var details) && details.ValueKind == JsonValueKind.Array)
        {
            foreach (var attempt in details.EnumerateArray())
            {
                if (attempt.TryGetProperty("state", out var attemptState)
                    && attemptState.GetString() == "COMPLETED"
                    && attempt.TryGetProperty("transactionId", out var txn))
                {
                    transactionId = txn.GetString();
                    break;
                }
            }
        }

        return new PhonePeOrderStatus(
            RequiredString(root, "orderId"),
            RequiredString(root, "state"),
            root.TryGetProperty("amount", out var amt) ? amt.GetInt64() : 0,
            transactionId);
    }

    /// <summary>
    /// PhonePe signs webhooks with SHA256(username:password) in the Authorization header.
    /// This proves the caller knows the shared secret; the payment itself is still re-confirmed
    /// against the Order Status API before any invoice is settled.
    /// </summary>
    public bool VerifyWebhookAuthorization(string? authorizationHeader)
    {
        if (!IsWebhookConfigured || string.IsNullOrWhiteSpace(authorizationHeader)) return false;

        var supplied = authorizationHeader.Trim();
        // Tolerate a scheme prefix; PhonePe sends the bare hash but dashboards vary.
        var spaceIndex = supplied.IndexOf(' ');
        if (spaceIndex > 0) supplied = supplied[(spaceIndex + 1)..].Trim();

        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{options.WebhookUsername}:{options.WebhookPassword}")));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(expected));
    }

    public static long ToPaise(decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("Payment amount must be positive and have at most two decimal places.");
        return checked((long)(amount * 100m));
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (cachedToken is not null && DateTimeOffset.UtcNow < tokenExpiresAt) return cachedToken;

        await tokenLock.WaitAsync(ct);
        try
        {
            if (cachedToken is not null && DateTimeOffset.UtcNow < tokenExpiresAt) return cachedToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, AuthUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = options.ClientId!,
                    ["client_version"] = options.ClientVersion,
                    ["client_secret"] = options.ClientSecret!,
                    ["grant_type"] = "client_credentials"
                })
            };

            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("PhonePe token request failed with HTTP {StatusCode}.", (int)response.StatusCode);
                throw new InvalidOperationException("PhonePe authentication failed. Check the client credentials.");
            }

            using var json = JsonDocument.Parse(body);
            var token = RequiredString(json.RootElement, "access_token");
            var expiresAt = json.RootElement.TryGetProperty("expires_at", out var exp) && exp.TryGetInt64(out var epoch)
                ? DateTimeOffset.FromUnixTimeSeconds(epoch)
                : DateTimeOffset.UtcNow.AddMinutes(10);

            cachedToken = token;
            // Refresh a minute early so a call cannot start with a token that expires mid-flight.
            tokenExpiresAt = expiresAt.AddMinutes(-1);
            return token;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("PhonePe is not configured for this society.");
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || string.IsNullOrWhiteSpace(property.GetString()))
            throw new InvalidOperationException($"PhonePe returned an invalid {propertyName} value.");
        return property.GetString()!;
    }
}

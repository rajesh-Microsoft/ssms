using System.Diagnostics;
using Microsoft.Extensions.Options;
using SMMS.Api.Models;

namespace SMMS.Api.Services.Utilities;

public class UtilityIntegrationOptions
{
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public int RetryCount { get; set; } = 3;
    public Dictionary<string, UtilityProviderOptions> Providers { get; set; } = [];
}

public class UtilityProviderOptions
{
    public string BillUrlTemplate { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    /// <summary>Where an admin is sent to settle the bill. The biller's own checkout, not ours - paying
    /// a utility runs over BBPS, which our collection gateway cannot do.</summary>
    public string? PaymentUrlTemplate { get; set; }
}

public class TGSPDCLProvider(
    HttpClient httpClient,
    TGSPDCLJsonParser parser,
    IOptions<UtilityIntegrationOptions> options,
    ILogger<TGSPDCLProvider> logger) : IUtilityProvider
{
    public string Code => "TGSPDCL";

    public async Task<UtilityBillResult?> FetchBillAsync(UtilityConnection connection, CancellationToken cancellationToken)
    {
        if (!options.Value.Providers.TryGetValue(Code, out var providerOptions) ||
            string.IsNullOrWhiteSpace(providerOptions.BillUrlTemplate))
            throw new InvalidOperationException("Utilities:Providers:TGSPDCL:BillUrlTemplate is not configured.");
        if (string.IsNullOrWhiteSpace(providerOptions.ApiKey))
            throw new InvalidOperationException("Utilities:Providers:TGSPDCL:ApiKey is not configured.");

        Exception? lastError = null;
        for (var attempt = 1; attempt <= Math.Max(1, options.Value.RetryCount); attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var url = providerOptions.BillUrlTemplate.Replace("{ConsumerNumber}", Uri.EscapeDataString(connection.ConsumerNumber));
                logger.LogInformation("Utility request Provider={Provider} Consumer={ConsumerNumber} Attempt={Attempt}",
                    Code, connection.ConsumerNumber, attempt);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-API-Key", providerOptions.ApiKey);
                using var response = await httpClient.SendAsync(request, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                response.EnsureSuccessStatusCode();
                var result = parser.Parse(json);
                logger.LogInformation("Utility response Provider={Provider} Consumer={ConsumerNumber} Success={Success} DurationMs={DurationMs} StatusCode={StatusCode}",
                    Code, connection.ConsumerNumber, result is not null, stopwatch.ElapsedMilliseconds, (int)response.StatusCode);
                if (result is not null) return result;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                logger.LogWarning(ex, "Utility request failed Provider={Provider} Consumer={ConsumerNumber} Attempt={Attempt} DurationMs={DurationMs}",
                    Code, connection.ConsumerNumber, attempt, stopwatch.ElapsedMilliseconds);
                if (attempt < options.Value.RetryCount)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }

        if (lastError is not null) throw new HttpRequestException("TGSPDCL bill lookup failed after retries.", lastError);
        return null;
    }
}
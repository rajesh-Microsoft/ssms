using System.Diagnostics;
using Microsoft.Extensions.Options;
using SMMS.Api.Models;

namespace SMMS.Api.Services.Utilities;

/// <summary>
/// Hyderabad Metropolitan Water Supply and Sewerage Board, via its BillDesk portal. The portal's own
/// form POSTs the CAN, but the target page reads it from the query string just as happily, so a plain
/// GET is enough and no browser automation is needed.
/// </summary>
public class HmwssbProvider(
    HttpClient httpClient,
    HmwssbHtmlParser parser,
    IOptions<UtilityIntegrationOptions> options,
    ILogger<HmwssbProvider> logger) : IUtilityProvider
{
    public string Code => "HMWSSB";

    public async Task<UtilityBillResult?> FetchBillAsync(UtilityConnection connection, CancellationToken cancellationToken)
    {
        if (!options.Value.Providers.TryGetValue(Code, out var providerOptions) ||
            string.IsNullOrWhiteSpace(providerOptions.BillUrlTemplate))
            throw new InvalidOperationException("Utilities:Providers:HMWSSB:BillUrlTemplate is not configured.");

        Exception? lastError = null;
        for (var attempt = 1; attempt <= Math.Max(1, options.Value.RetryCount); attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var url = providerOptions.BillUrlTemplate.Replace("{ConsumerNumber}", Uri.EscapeDataString(connection.ConsumerNumber));
                logger.LogInformation("Utility request Provider={Provider} Consumer={ConsumerNumber} Attempt={Attempt}",
                    Code, connection.ConsumerNumber, attempt);
                using var response = await httpClient.GetAsync(url, cancellationToken);
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                response.EnsureSuccessStatusCode();
                var result = parser.Parse(html);
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

        if (lastError is not null) throw new HttpRequestException("HMWSSB bill lookup failed after retries.", lastError);
        return null;
    }
}

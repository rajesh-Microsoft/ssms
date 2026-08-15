using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using SMMS.Api.Models;

namespace SMMS.Api.Services.Utilities;

public class UtilityIntegrationOptions
{
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public int RetryCount { get; set; } = 3;
    public bool PlaywrightFallbackEnabled { get; set; } = true;
    public Dictionary<string, UtilityProviderOptions> Providers { get; set; } = [];
}

public class UtilityProviderOptions
{
    public string BillUrlTemplate { get; set; } = string.Empty;
    /// <summary>Where an admin is sent to settle the bill. The biller's own checkout, not ours - paying
    /// a utility runs over BBPS, which our collection gateway cannot do.</summary>
    public string? PaymentUrlTemplate { get; set; }
    public string? FormUrl { get; set; }
    public string ConsumerInputSelector { get; set; } = "#ukscno";
    public string SubmitSelector { get; set; } = "#submitbtnclicked";
    public string ResultSelector { get; set; } = "text=Total Amount to be Paid";
}

public class TGSPDCLProvider(
    HttpClient httpClient,
    TGSPDCLHtmlParser parser,
    IOptions<UtilityIntegrationOptions> options,
    ILogger<TGSPDCLProvider> logger) : IUtilityProvider
{
    public string Code => "TGSPDCL";

    public async Task<UtilityBillResult?> FetchBillAsync(UtilityConnection connection, CancellationToken cancellationToken)
    {
        if (!options.Value.Providers.TryGetValue(Code, out var providerOptions) ||
            string.IsNullOrWhiteSpace(providerOptions.BillUrlTemplate))
            throw new InvalidOperationException("Utilities:Providers:TGSPDCL:BillUrlTemplate is not configured.");

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

        if (options.Value.PlaywrightFallbackEnabled)
        {
            var browserResult = await FetchWithBrowserAsync(connection.ConsumerNumber, providerOptions, cancellationToken);
            if (browserResult is not null) return browserResult;
        }

        if (lastError is not null) throw new HttpRequestException("TGSPDCL bill lookup failed after retries.", lastError);
        return null;
    }

    private async Task<UtilityBillResult?> FetchWithBrowserAsync(
        string consumerNumber,
        UtilityProviderOptions providerOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync(providerOptions.FormUrl ?? providerOptions.BillUrlTemplate.Replace("?ukscno={ConsumerNumber}", string.Empty));
            await page.Locator(providerOptions.ConsumerInputSelector).FillAsync(consumerNumber);
            await page.Locator(providerOptions.SubmitSelector).First.ClickAsync();
            await page.Locator(providerOptions.ResultSelector).WaitForAsync(new() { State = WaitForSelectorState.Visible });
            var html = await page.ContentAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return parser.Parse(html);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Playwright fallback failed Provider={Provider} Consumer={ConsumerNumber}", Code, consumerNumber);
            return null;
        }
    }
}
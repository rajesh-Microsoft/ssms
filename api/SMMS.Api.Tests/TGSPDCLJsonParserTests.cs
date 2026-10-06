using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SMMS.Api.Models;
using SMMS.Api.Services.Utilities;
using Xunit;

namespace SMMS.Api.Tests;

public class TGSPDCLJsonParserTests
{
    private readonly TGSPDCLJsonParser _parser = new();

    [Fact]
    public void Parse_ExtractsApiBill()
    {
        const string json = """
            {
              "BILLDATE": "02-Oct-26",
              "AMOUNTPAYABLE": "8529.00",
              "SERVICENO": "0505 07958",
              "ARRAMT": 0,
              "CUSTOMERNAME": "MS NEW LAND CONSTRUCTIONS",
              "DUEDATE": "16-Oct-26",
              "UNITS": 922,
              "BILLAMT": 8529
            }
            """;

        var result = _parser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal("MS NEW LAND CONSTRUCTIONS", result.ConsumerName);
        Assert.Equal("0505 07958", result.ServiceNumber);
        Assert.Equal(new DateTime(2026, 10, 1), result.BillingMonth);
        Assert.Equal(new DateTime(2026, 10, 2), result.BillDate);
        Assert.Equal(new DateTime(2026, 10, 16), result.DueDate);
        Assert.Equal(8529m, result.BillAmount);
        Assert.Equal(922m, result.UnitsConsumed);
        Assert.Equal(0m, result.Arrears);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenBillIsNotAvailable()
    {
        const string json = """{"message":"Consumer not found."}""";

        Assert.Null(_parser.Parse(json));
    }

    [Fact]
    public async Task Provider_UsesJsonEndpointAndApiKey()
    {
        var handler = new RecordingHandler("""
            {
              "BILLDATE": "02-Oct-26",
              "AMOUNTPAYABLE": "8529.00",
              "SERVICENO": "0505 07958",
              "DUEDATE": "16-Oct-26",
              "UNITS": 922
            }
            """);
        using var client = new HttpClient(handler);
        var options = Options.Create(new UtilityIntegrationOptions
        {
            RetryCount = 1,
            Providers = new()
            {
                ["TGSPDCL"] = new()
                {
                    BillUrlTemplate = "https://tgsouthernpower.org/api/public/bill?uscno={ConsumerNumber}",
                    ApiKey = "test-api-key"
                }
            }
        });
        var provider = new TGSPDCLProvider(client, _parser, options, NullLogger<TGSPDCLProvider>.Instance);

        var result = await provider.FetchBillAsync(
            new UtilityConnection { ConsumerNumber = "115362356" },
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(8529m, result.BillAmount);
        Assert.Equal(
            "https://tgsouthernpower.org/api/public/bill?uscno=115362356",
            handler.RequestUri?.AbsoluteUri);
        Assert.Equal("test-api-key", handler.ApiKey);
    }

    [Fact]
    public async Task Provider_RejectsMissingApiKey()
    {
        using var client = new HttpClient(new RecordingHandler("{}"));
        var options = Options.Create(new UtilityIntegrationOptions
        {
            Providers = new()
            {
                ["TGSPDCL"] = new()
                {
                    BillUrlTemplate = "https://tgsouthernpower.org/api/public/bill?uscno={ConsumerNumber}"
                }
            }
        });
        var provider = new TGSPDCLProvider(client, _parser, options, NullLogger<TGSPDCLProvider>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.FetchBillAsync(new UtilityConnection { ConsumerNumber = "115362356" }, CancellationToken.None));

        Assert.Contains("ApiKey", error.Message);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("X-API-Key").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            });
        }
    }
}
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Services.Payments;
using Xunit;

namespace SMMS.Api.Tests;

public class PhonePePaymentGatewayTests
{
    private static PhonePePaymentGateway Gateway(PhonePeOptions options) => new(
        new HttpClient(),
        Options.Create(options),
        new TenantContext(),
        NullLogger<PhonePePaymentGateway>.Instance);

    [Theory]
    [InlineData("aadya", 4321)]
    [InlineData("brij-a", 7)]        // society keys contain hyphens, so '_' must separate parts
    [InlineData("lake-view", 100200)]
    public void MerchantOrderId_RoundTrips(string society, int collectionId)
    {
        var id = PhonePePaymentGateway.BuildMerchantOrderId(society, collectionId);

        var parsed = PhonePePaymentGateway.ParseMerchantOrderId(id);

        Assert.NotNull(parsed);
        Assert.Equal(society, parsed!.Value.Society);
        Assert.Equal(collectionId, parsed.Value.CollectionId);
        Assert.True(id.Length <= 63, "PhonePe rejects merchant order ids longer than 63 characters.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-ours")]
    [InlineData("SMMS_aadya")]
    [InlineData("SMMS_aadya_notanumber_abc")]
    public void ParseMerchantOrderId_RejectsForeignIds(string? id) =>
        Assert.Null(PhonePePaymentGateway.ParseMerchantOrderId(id));

    [Fact]
    public void ToPaise_ConvertsRupees() => Assert.Equal(957800L, PhonePePaymentGateway.ToPaise(9578.00m));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.001)]
    public void ToPaise_RejectsInvalidAmounts(decimal amount) =>
        Assert.Throws<InvalidOperationException>(() => PhonePePaymentGateway.ToPaise(amount));

    [Fact]
    public void VerifyWebhookAuthorization_AcceptsMatchingHash()
    {
        var gateway = Gateway(new PhonePeOptions { WebhookUsername = "smms", WebhookPassword = "s3cret" });
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("smms:s3cret")));

        Assert.True(gateway.VerifyWebhookAuthorization(expected));
        Assert.True(gateway.VerifyWebhookAuthorization($"SHA256 {expected}"));
    }

    [Fact]
    public void VerifyWebhookAuthorization_RejectsWrongOrMissingHash()
    {
        var gateway = Gateway(new PhonePeOptions { WebhookUsername = "smms", WebhookPassword = "s3cret" });
        var wrong = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("smms:wrong")));

        Assert.False(gateway.VerifyWebhookAuthorization(wrong));
        Assert.False(gateway.VerifyWebhookAuthorization(null));
        Assert.False(gateway.VerifyWebhookAuthorization("   "));
    }

    [Fact]
    public void VerifyWebhookAuthorization_FailsClosedWhenUnconfigured()
    {
        var gateway = Gateway(new PhonePeOptions());
        var anything = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(":")));

        Assert.False(gateway.VerifyWebhookAuthorization(anything));
    }

    [Fact]
    public void IsConfigured_RequiresSocietyOptIn()
    {
        var baseOptions = new PhonePeOptions
        {
            Enabled = true,
            ClientId = "id",
            ClientSecret = "secret",
            AllowedSocieties = ""
        };

        Assert.False(Gateway(baseOptions).IsConfigured);

        baseOptions.AllowedSocieties = "*";
        Assert.True(Gateway(baseOptions).IsConfigured);
    }
}

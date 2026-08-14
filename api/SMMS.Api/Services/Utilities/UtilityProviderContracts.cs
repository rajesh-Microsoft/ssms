using SMMS.Api.Models;

namespace SMMS.Api.Services.Utilities;

public record UtilityBillResult(
    DateTime BillingMonth,
    string? BillNumber,
    DateTime? BillDate,
    DateTime? DueDate,
    decimal BillAmount,
    decimal? UnitsConsumed,
    decimal Arrears,
    string? ConsumerName,
    string? ServiceNumber,
    string Status,
    string RawHtml);

public interface IUtilityProvider
{
    string Code { get; }
    Task<UtilityBillResult?> FetchBillAsync(UtilityConnection connection, CancellationToken cancellationToken);
}

public interface IUtilityProviderResolver
{
    IUtilityProvider Resolve(string providerCode);
}

public class UtilityProviderResolver(IEnumerable<IUtilityProvider> providers) : IUtilityProviderResolver
{
    private readonly IReadOnlyDictionary<string, IUtilityProvider> _providers = providers
        .ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    public IUtilityProvider Resolve(string providerCode) =>
        _providers.TryGetValue(providerCode, out var provider)
            ? provider
            : throw new InvalidOperationException($"Utility provider '{providerCode}' is not registered.");
}
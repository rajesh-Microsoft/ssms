using SMMS.Api.Models;

namespace SMMS.Api.Services.Billing;

/// <summary>Maps a CalculationMethod to its strategy. New methods are picked up automatically
/// because every IChargeStrategy implementation is injected by DI (Open/Closed).</summary>
public class ChargeStrategyResolver
{
    private readonly IReadOnlyDictionary<CalculationMethod, IChargeStrategy> _map;

    public ChargeStrategyResolver(IEnumerable<IChargeStrategy> strategies)
        => _map = strategies.ToDictionary(s => s.Method);

    public IChargeStrategy For(CalculationMethod method)
        => _map.TryGetValue(method, out var s)
            ? s
            : throw new NotSupportedException($"No calculation strategy registered for {method}.");
}

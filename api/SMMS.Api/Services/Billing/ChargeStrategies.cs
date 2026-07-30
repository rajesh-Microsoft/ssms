using SMMS.Api.Models;

namespace SMMS.Api.Services.Billing;

public sealed class FixedAmountStrategy : IChargeStrategy
{
    public CalculationMethod Method => CalculationMethod.FixedAmount;
    public decimal Calculate(MaintenanceComponent c, FlatContext f, IReadOnlyDictionary<int, decimal> _)
        => c.Amount;
}

public sealed class PerSquareFootStrategy : IChargeStrategy
{
    public CalculationMethod Method => CalculationMethod.PerSquareFoot;
    public decimal Calculate(MaintenanceComponent c, FlatContext f, IReadOnlyDictionary<int, decimal> _)
        => c.Amount * f.AreaSqFt;
}

public sealed class PercentageStrategy : IChargeStrategy
{
    public CalculationMethod Method => CalculationMethod.Percentage;
    public decimal Calculate(MaintenanceComponent c, FlatContext f, IReadOnlyDictionary<int, decimal> prior)
    {
        var pct = c.PercentageValue ?? 0m;
        var basis = c.PercentageBaseComponentId is int id
            ? (prior.TryGetValue(id, out var v) ? v : 0m)
            : prior.Values.Sum();
        return basis * pct / 100m;
    }
}

public sealed class PerFlatTypeStrategy : IChargeStrategy
{
    public CalculationMethod Method => CalculationMethod.PerFlatType;
    public decimal Calculate(MaintenanceComponent c, FlatContext f, IReadOnlyDictionary<int, decimal> _)
        => RateLookup.ByKey(c, f.FlatType);
}

public sealed class PerTowerStrategy : IChargeStrategy
{
    public CalculationMethod Method => CalculationMethod.PerTower;
    public decimal Calculate(MaintenanceComponent c, FlatContext f, IReadOnlyDictionary<int, decimal> _)
        => RateLookup.ByKey(c, f.Tower);
}

public sealed class PerFloorStrategy : IChargeStrategy
{
    public CalculationMethod Method => CalculationMethod.PerFloor;
    public decimal Calculate(MaintenanceComponent c, FlatContext f, IReadOnlyDictionary<int, decimal> _)
        => RateLookup.ByKey(c, f.Floor);
}

public sealed class CustomPerFlatStrategy : IChargeStrategy
{
    public CalculationMethod Method => CalculationMethod.CustomPerFlat;
    public decimal Calculate(MaintenanceComponent c, FlatContext f, IReadOnlyDictionary<int, decimal> _)
    {
        var ovr = c.FlatOverrides.FirstOrDefault(o => o.MemberId == f.MemberId);
        return ovr?.Amount ?? c.Amount; // fall back to the component's default amount
    }
}

public sealed class ManualStrategy : IChargeStrategy
{
    public CalculationMethod Method => CalculationMethod.Manual;
    // No formula: auto-generation uses the admin-set default amount as a starting value.
    public decimal Calculate(MaintenanceComponent c, FlatContext f, IReadOnlyDictionary<int, decimal> _)
        => c.Amount;
}

internal static class RateLookup
{
    public static decimal ByKey(MaintenanceComponent c, string? key)
        => string.IsNullOrWhiteSpace(key)
            ? 0m
            : c.Rates.FirstOrDefault(r =>
                string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase))?.Amount ?? 0m;
}

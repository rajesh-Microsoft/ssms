using SMMS.Api.Models;

namespace SMMS.Api.Services.Billing;

/// <summary>Computes one calculation method. priorAmounts maps componentId → the amount already
/// computed for this same flat, letting Percentage components base off earlier components.</summary>
public interface IChargeStrategy
{
    CalculationMethod Method { get; }

    decimal Calculate(MaintenanceComponent component, FlatContext flat,
        IReadOnlyDictionary<int, decimal> priorAmounts);
}

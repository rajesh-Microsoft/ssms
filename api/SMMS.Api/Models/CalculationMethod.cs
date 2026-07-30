namespace SMMS.Api.Models;

/// <summary>How a maintenance component's amount is derived for a given flat.</summary>
public enum CalculationMethod
{
    FixedAmount = 0,
    PerSquareFoot = 1,
    Percentage = 2,
    PerFlatType = 3,
    PerTower = 4,
    PerFloor = 5,
    CustomPerFlat = 6,
    Manual = 7
}

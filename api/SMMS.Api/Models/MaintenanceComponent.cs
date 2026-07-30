using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMMS.Api.Models;

/// <summary>A single billable head (e.g. "Sinking Fund"). Data-driven: admins add/edit/remove
/// these without code changes. The Method decides which calculation strategy runs.</summary>
public class MaintenanceComponent
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    public CalculationMethod Method { get; set; } = CalculationMethod.FixedAmount;

    /// <summary>FixedAmount → the amount; PerSquareFoot → rate/sq ft; CustomPerFlat → fallback amount.</summary>
    [Column(TypeName = "decimal(12,4)")]
    public decimal Amount { get; set; }

    /// <summary>Percentage method: the percent value (10 = 10%).</summary>
    [Column(TypeName = "decimal(6,3)")]
    public decimal? PercentageValue { get; set; }

    /// <summary>Percentage of this component's amount; null = percentage of all prior components.</summary>
    public int? PercentageBaseComponentId { get; set; }

    /// <summary>false = optional: applies only to flats listed in FlatOverrides with IsApplicable=true.</summary>
    public bool ApplyToAllFlats { get; set; } = true;

    public bool IsActive { get; set; } = true;

    /// <summary>Evaluation order; a Percentage component must sort after its base component.</summary>
    public int SortOrder { get; set; }

    public ICollection<MaintenanceComponentRate> Rates { get; set; } = new List<MaintenanceComponentRate>();
    public ICollection<MaintenanceComponentFlatOverride> FlatOverrides { get; set; } = new List<MaintenanceComponentFlatOverride>();
}

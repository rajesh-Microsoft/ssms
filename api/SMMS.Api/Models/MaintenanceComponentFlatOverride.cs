using System.ComponentModel.DataAnnotations.Schema;

namespace SMMS.Api.Models;

/// <summary>Per-flat data: applicability for optional components and the custom amount for CustomPerFlat.</summary>
public class MaintenanceComponentFlatOverride
{
    public int Id { get; set; }

    public int ComponentId { get; set; }
    public MaintenanceComponent? Component { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public bool IsApplicable { get; set; } = true;

    [Column(TypeName = "decimal(12,4)")]
    public decimal? Amount { get; set; }
}

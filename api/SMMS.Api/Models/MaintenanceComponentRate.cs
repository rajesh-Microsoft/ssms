using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>Keyed amount for PerFlatType / PerTower / PerFloor. Key is the flat type / tower / floor label.</summary>
public class MaintenanceComponentRate : IAuditable
{
    public int Id { get; set; }

    public int ComponentId { get; set; }
    public MaintenanceComponent? Component { get; set; }

    [Required, MaxLength(50)]
    public string Key { get; set; } = string.Empty;

    [Column(TypeName = "decimal(12,4)")]
    public decimal Amount { get; set; }

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}

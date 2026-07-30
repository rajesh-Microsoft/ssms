using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMMS.Api.Models;

/// <summary>Keyed amount for PerFlatType / PerTower / PerFloor. Key is the flat type / tower / floor label.</summary>
public class MaintenanceComponentRate
{
    public int Id { get; set; }

    public int ComponentId { get; set; }
    public MaintenanceComponent? Component { get; set; }

    [Required, MaxLength(50)]
    public string Key { get; set; } = string.Empty;

    [Column(TypeName = "decimal(12,4)")]
    public decimal Amount { get; set; }
}

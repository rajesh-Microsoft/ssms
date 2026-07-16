using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMMS.Api.Models;

/// <summary>Single-row table holding society-wide configuration.</summary>
public class SocietySettings
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string SocietyName { get; set; } = "Our Society";

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal MaintenanceAmt { get; set; } = 2000;

    /// <summary>Comma-separated floor labels, e.g. "1,2,3,4,5".</summary>
    [MaxLength(500)]
    public string Floors { get; set; } = "1,2,3,4,5";

    /// <summary>Comma-separated expense category names.</summary>
    [MaxLength(1000)]
    public string Categories { get; set; } = "Security,Housekeeping,Electricity,Water,Repairs,Lift Maintenance,Gardening,Festival,CCTV,Miscellaneous";

    [MaxLength(20)]
    public string Theme { get; set; } = "light";
}

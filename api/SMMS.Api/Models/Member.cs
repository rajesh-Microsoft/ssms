using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

public class Member : IAuditable
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Flat { get; set; } = string.Empty;

    [MaxLength(10)]
    public string? Floor { get; set; }

    /// <summary>Carpet/built-up area in square feet; drives PerSquareFoot components.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal AreaSqFt { get; set; }

    [MaxLength(50)]
    public string? FlatType { get; set; }

    [MaxLength(50)]
    public string? Tower { get; set; }

    [MaxLength(20)]
    public string? Mobile { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    /// <summary>"Active" or "Inactive".</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Active";

    /// <summary>Cached advance/wallet balance (money received but not yet applied to a month).
    /// Reconcilable from <see cref="AdvanceLedgerEntry"/>; kept here for fast dashboard/report reads.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal AdvanceBalance { get; set; }

    /// <summary>"Auto" — new monthly invoices are auto-settled from the wallet; "Manual" — the wallet
    /// is held as credit until an admin applies it.</summary>
    [Required, MaxLength(10)]
    public string AdvanceMode { get; set; } = "Auto";

    public ICollection<Collection> Collections { get; set; } = new List<Collection>();

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}

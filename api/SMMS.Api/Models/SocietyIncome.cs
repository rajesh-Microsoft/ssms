using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>Society-level income that is NOT tied to a member/flat — e.g. advertising/hoarding
/// income, shop or tower rent, community-hall rental, bank interest, scrap sale, donations.
/// The counterpart to <see cref="Expense"/> on the receipts side.</summary>
public class SocietyIncome : IAuditable, ISoftDelete
{
    public int Id { get; set; }

    public DateTime IncomeDate { get; set; }

    [Required, MaxLength(60)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Who the money came from (advertiser, shop tenant, bank, donor).</summary>
    [MaxLength(150)]
    public string? Source { get; set; }

    [Required, MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    [MaxLength(30)]
    public string? PaymentMode { get; set; }

    /// <summary>Cheque no / UTR / receipt reference.</summary>
    [MaxLength(60)]
    public string? Reference { get; set; }

    [Range(1, 12)]
    public int Month { get; set; }

    public int Year { get; set; }

    [MaxLength(500)]
    public string? Remarks { get; set; }

    // ── Audit (IAuditable) + soft delete (ISoftDelete) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
}

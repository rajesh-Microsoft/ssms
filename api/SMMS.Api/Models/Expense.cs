using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

public class Expense : IAuditable, ISoftDelete
{
    public int Id { get; set; }

    public DateTime ExpenseDate { get; set; }

    [Required, MaxLength(60)]
    public string Category { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Vendor { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    [MaxLength(30)]
    public string? PaymentMode { get; set; }

    [Range(1, 12)]
    public int Month { get; set; }

    public int Year { get; set; }

    [MaxLength(500)]
    public string? Remarks { get; set; }

    /// <summary>Set when a contributor funded this cost out of pocket. The cost is the society's from
    /// day one, but no society cash left the bank, so this row is excluded from cash-in-hand until
    /// the matching liability is repaid.</summary>
    public int? FundedByLiabilityId { get; set; }

    [ForeignKey(nameof(FundedByLiabilityId))]
    public SocietyLiability? FundedByLiability { get; set; }

    // ── Audit (IAuditable) + soft delete (ISoftDelete) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
}

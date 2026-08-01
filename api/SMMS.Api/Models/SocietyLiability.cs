using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>Where the money the society owes came from. Extensible; MVP implements the first two.</summary>
public enum LiabilitySource
{
    MemberContribution,
    TreasurerAdvance
}

/// <summary>Settlement lifecycle of a liability, derived from SettledAmount vs Amount.</summary>
public enum LiabilityStatus
{
    Open,
    PartiallySettled,
    Settled
}

/// <summary>
/// A sum the society owes back to a contributor — the mirror of a member's advance wallet.
/// Recorded when someone (usually a member or treasurer) funds a society expense out of pocket;
/// settled later by repaying real cash (an <see cref="Expense"/>) or by converting the balance into
/// the contributor's advance wallet. Append-only <see cref="SocietyLiabilitySettlement"/> rows track
/// each repayment so partial settlements and a full audit trail are preserved.
/// </summary>
public class SocietyLiability : IAuditable, ISoftDelete
{
    public int Id { get; set; }

    public LiabilitySource Source { get; set; } = LiabilitySource.MemberContribution;

    /// <summary>The contributing member, when known. Null for external/free-text contributors.</summary>
    public int? MemberId { get; set; }

    [ForeignKey(nameof(MemberId))]
    public Member? Member { get; set; }

    /// <summary>Free-text contributor label, used when <see cref="MemberId"/> is null.</summary>
    [MaxLength(120)]
    public string? ContributorName { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    /// <summary>Original amount the society owes for this contribution.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    /// <summary>Running total repaid or converted so far. Outstanding = Amount − SettledAmount.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal SettledAmount { get; set; }

    public LiabilityStatus Status { get; set; } = LiabilityStatus.Open;

    [MaxLength(300)]
    public string? Purpose { get; set; }

    public List<SocietyLiabilitySettlement> Settlements { get; set; } = [];

    // ── Audit (IAuditable) + soft delete (ISoftDelete) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
}

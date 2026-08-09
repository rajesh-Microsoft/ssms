using System.ComponentModel.DataAnnotations;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>Where a reimbursement claim sits in the approval flow. Settlement state is deliberately
/// absent: once approved, the money lives on the linked <see cref="SocietyLiability"/> and its
/// settlement status is the single source of truth. Two places tracking the same money would drift.</summary>
public enum ReimbursementStatus
{
    /// <summary>Submitted by the member, waiting for a reviewer.</summary>
    Pending,

    /// <summary>Reviewer asked for more detail. The member can edit and resubmit.</summary>
    NeedsInfo,

    /// <summary>Accepted. A liability now exists and the cost has been booked as an expense.</summary>
    Approved,

    /// <summary>Declined. No money moves and no liability is created.</summary>
    Rejected
}

/// <summary>
/// A member's claim for money they spent on the society's behalf — the water motor that failed on a
/// Sunday, the tanker nobody else could pay for.
/// </summary>
/// <remarks>
/// This is the intake and approval document, NOT the money. Approving one creates a
/// <see cref="SocietyLiability"/>, which books the cost as an expense in the month it was incurred
/// and tracks repayment. The claim keeps only the paperwork: who asked, what for, who decided.
/// </remarks>
public class ReimbursementRequest : IAuditable, ISoftDelete
{
    public int Id { get; set; }

    /// <summary>The member out of pocket. Resolved from the signed-in user's flat, never supplied by the client.</summary>
    public int MemberId { get; set; }
    public Member? Member { get; set; }

    /// <summary>The user account that submitted it — kept separate from MemberId because several
    /// residents can share a flat, and "who typed this" is the accountable party.</summary>
    public int SubmittedByUserId { get; set; }

    /// <summary>Expense category, matching the free-text categories used by <see cref="Expense"/>.</summary>
    [Required, MaxLength(60)]
    public string Category { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Vendor { get; set; }

    public decimal Amount { get; set; }

    /// <summary>When the money was actually spent. Drives which month the cost lands in on approval.</summary>
    public DateTime ExpenseDate { get; set; }

    [MaxLength(30)]
    public string? PaymentMode { get; set; }

    /// <summary>UPI reference, cheque number or similar, as evidence of the outgoing payment.</summary>
    [MaxLength(60)]
    public string? TransactionReference { get; set; }

    public ReimbursementStatus Status { get; set; } = ReimbursementStatus.Pending;

    /// <summary>Set once approved. Null for everything else — this is what links paperwork to money.</summary>
    public int? LiabilityId { get; set; }
    public SocietyLiability? Liability { get; set; }

    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedOn { get; set; }

    public List<ReimbursementAttachment> Attachments { get; set; } = [];

    /// <summary>Why it was rejected, or what the reviewer needs to see. Shown to the member.</summary>
    [MaxLength(500)]
    public string? ReviewNote { get; set; }

    // ── Audit (IAuditable) + soft delete (ISoftDelete) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
}

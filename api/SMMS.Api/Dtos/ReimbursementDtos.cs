using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

/// <summary>What a member submits when claiming money back. The member is resolved from the token,
/// never sent by the client.</summary>
public record ReimbursementCreateRequest(
    [Required, MaxLength(60)] string Category,
    [Required, MaxLength(300)] string Description,
    [MaxLength(150)] string? Vendor,
    [Range(0.01, 100_000_000)] decimal Amount,
    DateTime ExpenseDate,
    [MaxLength(30)] string? PaymentMode,
    [MaxLength(60)] string? TransactionReference);

/// <summary>Reviewer's note when rejecting or asking for more detail. Required, because "rejected"
/// with no reason is what makes committees look arbitrary.</summary>
public record ReimbursementReviewRequest(
    [Required, MaxLength(500)] string Note);

/// <summary>A claim as shown to members and reviewers. Settlement figures are read from the linked
/// liability rather than stored here, so they cannot disagree with the ledger.</summary>
public record ReimbursementDto(
    int Id,
    int MemberId,
    string MemberName,
    string Flat,
    string Category,
    string Description,
    string? Vendor,
    decimal Amount,
    DateTime ExpenseDate,
    string? PaymentMode,
    string? TransactionReference,
    string Status,
    DateTime SubmittedOn,
    string? ReviewNote,
    DateTime? ReviewedOn,
    int? LiabilityId,
    decimal SettledAmount,
    decimal Outstanding,
    /// <summary>Pending | NeedsInfo | Rejected | Approved | PartiallySettled | Settled — the
    /// approval state until approved, the money's state afterwards.</summary>
    string DisplayStatus);

/// <summary>Counts for the admin queue badge and the dashboard widget.</summary>
public record ReimbursementSummaryDto(
    int Pending,
    int NeedsInfo,
    int AwaitingSettlement,
    decimal AwaitingSettlementValue);

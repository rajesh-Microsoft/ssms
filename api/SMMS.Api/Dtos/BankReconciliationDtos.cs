namespace SMMS.Api.Dtos;

/// <summary>A reconciliation candidate (a pending payment proof that could match a bank credit).</summary>
public record MatchCandidateDto(
    int PaymentProofId,
    int CollectionId,
    string InvoiceNumber,
    string BillingLabel,
    string? Flat,
    string? MemberName,
    decimal Amount,
    string? UpiReference,
    string Confidence,   // "Exact" | "Likely"
    string Reason);

/// <summary>An imported bank credit row plus any suggested matches.</summary>
public record BankTransactionDto(
    int Id,
    DateTime TxnDate,
    string Narration,
    string? Reference,
    decimal Amount,
    string Status,
    int? MatchedPaymentProofId,
    int? MatchedCollectionId,
    IEnumerable<MatchCandidateDto> Candidates);

/// <summary>Summary returned after a statement is imported.</summary>
public record ImportResultDto(
    string Batch,
    int TotalRows,
    int Imported,
    int SkippedDuplicates,
    int SkippedDebits,
    int AutoMatched,
    IEnumerable<BankTransactionDto> Transactions);

/// <summary>Reconciliation KPIs for the admin dashboard card.</summary>
public record ReconciliationSummaryDto(
    int UnmatchedCount,
    decimal UnmatchedAmount,
    int MatchedCount,
    int PendingProofCount);

public record ConfirmMatchRequest(int BankTransactionId, int PaymentProofId);

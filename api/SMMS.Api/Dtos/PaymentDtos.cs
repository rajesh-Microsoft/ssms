namespace SMMS.Api.Dtos;

// ── Member-facing ──

/// <summary>An unpaid maintenance charge with everything the pay card/QR popup needs.</summary>
public record PendingInvoiceDto(
    int CollectionId,
    string InvoiceNumber,
    decimal Amount,
    string Status,
    int Month,
    int Year,
    string BillingLabel,
    DateTime DueDate,
    bool IsOverdue,
    bool HasPendingProof);

/// <summary>The UPI payload for a charge (URI + note); the PNG itself is a separate image endpoint.</summary>
public record QrPayloadDto(
    int CollectionId,
    string InvoiceNumber,
    decimal Amount,
    string UpiUri,
    string Note,
    string PayeeName,
    string UpiId);

/// <summary>One row of a resident's payment-proof history.</summary>
public record PaymentProofDto(
    int Id,
    int CollectionId,
    string InvoiceNumber,
    decimal Amount,
    string Status,
    string? UpiReference,
    bool HasScreenshot,
    DateTime SubmittedAt,
    DateTime? ReviewedAt,
    string? ReviewRemarks);

// ── Admin-facing ──

/// <summary>A payment proof enriched with member/flat context for the approval screen.</summary>
public record AdminPaymentProofDto(
    int Id,
    int CollectionId,
    string InvoiceNumber,
    int MemberId,
    string? MemberName,
    string? Flat,
    decimal Amount,
    int Month,
    int Year,
    string BillingLabel,
    string Status,
    string? UpiReference,
    bool HasScreenshot,
    DateTime SubmittedAt,
    DateTime? ReviewedAt,
    string? ReviewRemarks);

/// <summary>Top-line counters for the admin payment dashboard.</summary>
public record PaymentDashboardDto(
    int PendingCount,
    decimal PendingAmount,
    decimal TodayCollections,
    decimal MonthCollections,
    IEnumerable<AdminPaymentProofDto> PendingProofs);

public record RejectPaymentRequest(string? Remarks);

// ── UPI / bank settings ──

public record UpiSettingsDto(
    string? UpiId,
    string? UpiPayeeName,
    string? BankName,
    string? BankAccountName,
    string? BankAccountNumber,
    string? BankIfsc);

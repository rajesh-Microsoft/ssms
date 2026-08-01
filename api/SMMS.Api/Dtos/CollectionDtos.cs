using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record CollectionDto(int Id, int MemberId, string? MemberName, string? Flat, decimal Amount, string Status,
    int Month, int Year, DateTime? PaymentDate, string? PaymentMode, string? Remarks, decimal AmountPaid);

public record CollectionUpsertRequest(
    [Required] int MemberId,
    [Required] decimal Amount,
    string Status,
    [Range(1, 12)] int Month,
    [Required] int Year,
    DateTime? PaymentDate,
    string? PaymentMode,
    string? Remarks);

/// <summary>Admin records a real payment received from a member. The amount is allocated to the
/// member's dues (per <see cref="PaymentType"/>) with any surplus credited to the advance wallet.</summary>
public record RecordPaymentRequest(
    [Required] int MemberId,
    [Required] decimal Amount,
    /// "CurrentMonthOnly", "CurrentPlusArrears" or "AdvancePayment".
    string PaymentType,
    string? PaymentMode,
    DateTime? PaymentDate,
    string? Remarks);

/// <summary>Outcome of allocating a received payment: how much settled invoices vs. went to the wallet.</summary>
public record PaymentAllocationResult(
    decimal TotalReceived,
    decimal AppliedToInvoices,
    decimal CreditedToAdvance,
    int InvoicesSettled,
    int InvoicesPartial,
    decimal NewAdvanceBalance);

using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

/// <summary>A single row of a member's advance (wallet) ledger.</summary>
public record AdvanceLedgerEntryDto(int Id, DateTime Date, string Type, decimal Amount,
    decimal BalanceAfter, string Source, int? CollectionId, string? Note);

/// <summary>A member's wallet: current balance, auto/manual mode and full ledger history (newest first).</summary>
public record AdvanceLedgerDto(int MemberId, string MemberName, string Flat, decimal Balance,
    string Mode, IEnumerable<AdvanceLedgerEntryDto> Entries);

/// <summary>Admin manual wallet adjustment. <see cref="Type"/> is "Credit" or "Debit".</summary>
public record AdvanceAdjustRequest([Required] string Type, [Required] decimal Amount, string? Note);

/// <summary>Toggle a member's advance mode: "Auto" (auto-settle new invoices) or "Manual" (hold credit).</summary>
public record AdvanceModeRequest([Required] string Mode);

/// <summary>Refund a member's wallet balance (e.g. on move-out). Debits the full balance.</summary>
public record AdvanceRefundRequest(string? Note);

/// <summary>One member's outstanding wallet credit — used by the Advance Balance report.</summary>
public record AdvanceBalanceRowDto(int MemberId, string Name, string Flat, decimal Balance, string Mode);

/// <summary>A wallet deduction (Debit) within a period — used by the Advance Deduction History report.</summary>
public record AdvanceDeductionRowDto(int Id, DateTime Date, int MemberId, string MemberName, string Flat,
    decimal Amount, decimal BalanceAfter, string Source, int? CollectionId, string? Note);

using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record BudgetItemDto(
    int Id,
    string Category,
    string? Description,
    decimal EstimatedAmount,
    decimal? ActualAmount,
    decimal? Variance,
    DateTime? DueDate,
    string Status,
    int? ExpenseId,
    decimal EffectiveAmount,
    string Source,
    DateTime? SourceFetchedOn,
    int? UtilityBillId);

public record BudgetDto(
    int Id,
    int Month,
    int Year,
    decimal OpeningBalance,
    decimal ExpectedCollection,
    decimal ExpectedExpense,
    decimal ExpectedClosingBalance,
    decimal Deficit,
    string Status,
    string? Notes,
    IReadOnlyList<BudgetItemDto> Items);

public record BudgetUpsertRequest(
    [Range(1, 12)] int Month,
    [Required] int Year,
    decimal OpeningBalance,
    decimal ExpectedCollection,
    string? Status,
    string? Notes);

public record BudgetItemUpsertRequest(
    [Required] string Category,
    string? Description,
    [Required] decimal EstimatedAmount,
    DateTime? DueDate);

public record ConvertToActualRequest(
    [Required] decimal ActualAmount,
    DateTime? ExpenseDate,
    string? Vendor,
    string? PaymentMode,
    string? Remarks);

public record CategorySuggestionDto(
    string Category,
    decimal SuggestedAmount,
    int MonthsOfHistory,
    decimal LastAmount,
    string Source,
    DateTime? FetchedOn,
    int? UtilityBillId);

public record BudgetSuggestionDto(
    decimal SuggestedOpeningBalance,
    decimal SuggestedExpectedCollection,
    IReadOnlyList<CategorySuggestionDto> Categories);

/// <summary>Budgeted is null for spending that was never planned, which is the row committees
/// most want to see.</summary>
public record BudgetVarianceRowDto(
    string Category,
    decimal Budgeted,
    decimal Actual,
    decimal Difference,
    bool Unbudgeted);

public record BudgetVarianceDto(
    int Month,
    int Year,
    decimal TotalBudgeted,
    decimal TotalActual,
    decimal TotalDifference,
    IReadOnlyList<BudgetVarianceRowDto> Rows);

/// <summary>Where the society actually stands today, as opposed to what the month was planned to do.
/// <paramref name="BalanceAnchored"/> is false when no budget exists for the month, in which case the
/// balance is derived purely from the ledger and is only as complete as the ledger is - a society that
/// never recorded its opening corpus will show far too little.
/// <paramref name="MonthsOfCover"/> and <paramref name="Accuracy"/> are null when there is not enough
/// history to answer honestly.</summary>
public record BudgetHealthDto(
    decimal BankBalance,
    bool BalanceAnchored,
    decimal Receivables,
    decimal Payables,
    decimal NetCashPosition,
    decimal AvgMonthlyExpense,
    decimal? MonthsOfCover,
    string Band,
    decimal? Accuracy,
    int AccuracyMonths);

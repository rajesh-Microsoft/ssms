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
    int? ExpenseId);

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
    decimal LastAmount);

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

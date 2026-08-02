using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record IncomeDto(int Id, DateTime IncomeDate, string Category, string? Source, string Description,
    decimal Amount, string? PaymentMode, string? Reference, int Month, int Year, string? Remarks);

public record IncomeUpsertRequest(
    [Required] DateTime IncomeDate,
    [Required] string Category,
    string? Source,
    [Required] string Description,
    [Required] decimal Amount,
    string? PaymentMode,
    string? Reference,
    [Range(1, 12)] int Month,
    [Required] int Year,
    string? Remarks);

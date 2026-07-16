using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record ExpenseDto(int Id, DateTime ExpenseDate, string Category, string Description, string? Vendor,
    decimal Amount, string? PaymentMode, int Month, int Year, string? Remarks);

public record ExpenseUpsertRequest(
    [Required] DateTime ExpenseDate,
    [Required] string Category,
    [Required] string Description,
    string? Vendor,
    [Required] decimal Amount,
    string? PaymentMode,
    [Range(1, 12)] int Month,
    [Required] int Year,
    string? Remarks);

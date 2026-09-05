using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record ExpenseDto(int Id, DateTime ExpenseDate, string Category, string Description, string? Vendor,
    decimal Amount, string? PaymentMode, int Month, int Year, string? Remarks, int? FundedByLiabilityId,
    int AttachmentCount);

/// <summary>An uploaded bill or payment screenshot. The stored path is deliberately not exposed —
/// files are fetched through the download endpoint, which checks who is asking.</summary>
public record ExpenseAttachmentDto(
    int Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime UploadedOn);

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

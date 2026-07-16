using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record CollectionDto(int Id, int MemberId, string? MemberName, string? Flat, decimal Amount, string Status,
    int Month, int Year, DateTime? PaymentDate, string? PaymentMode, string? Remarks);

public record CollectionUpsertRequest(
    [Required] int MemberId,
    [Required] decimal Amount,
    string Status,
    [Range(1, 12)] int Month,
    [Required] int Year,
    DateTime? PaymentDate,
    string? PaymentMode,
    string? Remarks);

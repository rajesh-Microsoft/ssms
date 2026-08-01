using System.ComponentModel.DataAnnotations;
using SMMS.Api.Models;

namespace SMMS.Api.Dtos;

public record SocietyLiabilitySettlementDto(
    int Id, DateTime Date, decimal Amount, string Method, int? ExpenseId, string? Note);

public record SocietyLiabilityDto(
    int Id,
    string Source,
    int? MemberId,
    string ContributorLabel,
    DateTime Date,
    decimal Amount,
    decimal SettledAmount,
    decimal Outstanding,
    string Status,
    string? Purpose,
    IEnumerable<SocietyLiabilitySettlementDto> Settlements);

public record SocietyLiabilitySummaryDto(decimal TotalOutstanding, int OpenCount, int ContributorCount);

public record SocietyLiabilityUpsertRequest(
    [Required] string Source,
    int? MemberId,
    string? ContributorName,
    [Required] DateTime Date,
    [Range(0.01, 100_000_000)] decimal Amount,
    string? Purpose);

public record SocietyLiabilitySettleRequest(
    [Range(0.01, 100_000_000)] decimal Amount,
    [Required] string Method,
    string? PaymentMode,
    string? Note);

using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record UtilityProviderDto(int Id, string Code, string ProviderName, string Category, bool SupportsAutoFetch, string Status);

public record UtilityConnectionDto(
    int Id,
    int ProviderId,
    string ProviderCode,
    string ProviderName,
    string Category,
    string ConsumerNumber,
    string? ServiceNumber,
    bool AutoFetchEnabled,
    string Status,
    DateTime CreatedOn,
    DateTime? LastFetchedOn,
    string? LastFetchError,
    UtilityBillDto? LastBill);

public record UtilityConnectionRequest(
    [Range(1, int.MaxValue)] int ProviderId,
    [Required, StringLength(80, MinimumLength = 4), RegularExpression("^[A-Za-z0-9 -]+$")] string ConsumerNumber,
    [StringLength(80)] string? ServiceNumber,
    bool AutoFetchEnabled,
    bool Active);

public record UtilityBillDto(
    int Id,
    int UtilityConnectionId,
    string ProviderName,
    string Category,
    string ConsumerNumber,
    DateTime BillingMonth,
    string? BillNumber,
    DateTime? BillDate,
    DateTime? DueDate,
    decimal BillAmount,
    decimal? UnitsConsumed,
    decimal Arrears,
    string? ConsumerName,
    string Status,
    DateTime FetchedOn);

public record UtilityFetchDto(bool BillAvailable, bool Created, string Message, UtilityBillDto? Bill);

public record UtilityNotificationDto(int Id, int UtilityBillId, string Title, string Message, DateTime CreatedOn);
using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record MePaymentDto(int Id, decimal Amount, string Status, int Month, int Year,
    DateTime? PaymentDate, string? PaymentMode, string? Remarks);

public record MeAdvanceEntryDto(int Id, DateTime Date, string Type, decimal Amount,
    decimal BalanceAfter, string Source, string? Note);

public record MeAdvanceDto(decimal Balance, string Mode, IEnumerable<MeAdvanceEntryDto> Entries);

public record MeComplaintSummaryDto(int Open, int Closed, int Total);

public record MeMaintenanceSummaryDto(decimal TotalPaid, int TotalReceipts, decimal PendingAmount,
    int PendingMonths, DateTime? LastPaymentDate, DateTime? NextDueDate, decimal MaintenanceAmt, int DueDay);

public record MeProfileDto(
    int Id, string Username, string Role, string? Name, string? Email, string? Mobile,
    string? Flat, string? Floor, string? OccupancyType, string? EmergencyContact,
    string? ProfilePhoto, string? FamilyJson, string? VehiclesJson, string? NotifyPrefsJson,
    string SocietyName,
    MeMaintenanceSummaryDto Maintenance, MeComplaintSummaryDto Complaints,
    IEnumerable<MePaymentDto> Payments);

public record MeUpdateRequest(
    string? Name, string? Email, string? Mobile, string? OccupancyType, string? EmergencyContact,
    string? ProfilePhoto, string? FamilyJson, string? VehiclesJson, string? NotifyPrefsJson);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(4)] string NewPassword);

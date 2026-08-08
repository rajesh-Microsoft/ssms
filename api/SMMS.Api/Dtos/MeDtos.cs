using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record MePaymentDto(int Id, decimal Amount, string Status, int Month, int Year,
    DateTime? PaymentDate, string? PaymentMode, string? Remarks);

public record MeAdvanceEntryDto(int Id, DateTime Date, string Type, decimal Amount,
    decimal BalanceAfter, string Source, string? Note);

public record MeAdvanceDto(decimal Balance, string Mode, IEnumerable<MeAdvanceEntryDto> Entries);

public record MeComplaintSummaryDto(int Open, int Closed, int Total);

// ── My Gate (what the caretaker logged for THIS resident's flat) ──
// The visitor's mobile is deliberately left out: the resident does not need it.

public record MeGateVisitorDto(int Id, string Name, string Purpose, string? VehicleNumber,
    DateTime InAt, DateTime? OutAt);

public record MeGateParcelDto(int Id, string Courier, string Status,
    DateTime ReceivedAt, DateTime? CollectedAt, string? CollectedBy);

public record MeGateDto(
    string? Flat,
    int ParcelsWaiting,
    int VisitorsInside,
    IEnumerable<MeGateParcelDto> Parcels,
    IEnumerable<MeGateVisitorDto> Visitors);

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

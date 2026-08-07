using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

// ── Dashboard ──

public record CaretakerSummaryDto(
    DateOnly Date,
    string CaretakerName,
    int VisitorsToday,
    int VisitorsInside,
    int DeliveriesWaiting,
    int OpenComplaints,
    bool ChecklistDoneToday);

// ── Visitors ──

public record VisitorDto(
    int Id,
    string Name,
    string? Mobile,
    string Flat,
    string Purpose,
    string? VehicleNumber,
    DateTime InAt,
    DateTime? OutAt,
    string? Notes);

public record VisitorCreateRequest(
    [Required, MaxLength(150)] string Name,
    [MaxLength(20)] string? Mobile,
    [Required, MaxLength(20)] string Flat,
    [Required, MaxLength(40)] string Purpose,
    [MaxLength(20)] string? VehicleNumber,
    [MaxLength(200)] string? Notes);

// ── Deliveries ──

public record DeliveryDto(
    int Id,
    string Courier,
    string Flat,
    string Status,
    DateTime ReceivedAt,
    DateTime? CollectedAt,
    string? CollectedBy,
    string? Notes);

public record DeliveryCreateRequest(
    [Required, MaxLength(60)] string Courier,
    [Required, MaxLength(20)] string Flat,
    [MaxLength(200)] string? Notes);

public record DeliveryCollectRequest(
    [MaxLength(150)] string? CollectedBy);

// ── Daily checklist ──

public record ChecklistItemDto(
    [Required, MaxLength(100)] string Label,
    bool Done,
    [MaxLength(200)] string? Remark);

public record ChecklistDto(
    DateOnly Date,
    bool Submitted,
    DateTime? SubmittedAt,
    string? SubmittedBy,
    string? Notes,
    IEnumerable<ChecklistItemDto> Items);

public record ChecklistSubmitRequest(
    [MaxLength(500)] string? Notes,
    List<ChecklistItemDto> Items);

// ── Issues raised from the gate ──

public record CaretakerComplaintDto(
    int Id,
    string Subject,
    string Category,
    string Priority,
    string Status,
    DateTime CreatedAt,
    bool HasPhoto);

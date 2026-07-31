namespace SMMS.Api.Dtos.Control;

/// <summary>A subscription plan as shown in the platform console.</summary>
public record SubscriptionPlanDto(
    int Id,
    string Code,
    string Name,
    decimal Price,
    int BillingPeriodMonths,
    string Currency,
    bool IsActive,
    DateTime CreatedAt);

/// <summary>Create/update payload for a plan. On update the <see cref="Code"/> is ignored (immutable).</summary>
public record UpsertPlanRequest(
    string Code,
    string Name,
    decimal Price,
    int BillingPeriodMonths,
    string? Currency,
    bool IsActive);

/// <summary>A platform invoice (a society being billed for its subscription) as shown in the console.
/// <see cref="Status"/> is the effective status — "Issued" past its due date is surfaced as "Overdue".</summary>
public record PlatformInvoiceDto(
    int Id,
    string InvoiceNumber,
    string SocietyKey,
    string SocietyName,
    string PlanCode,
    string PlanName,
    int BillingPeriodMonths,
    decimal Amount,
    string Currency,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime IssuedAt,
    DateTime DueDate,
    string Status,
    DateTime? PaidAt,
    string? PaymentReference,
    string? Notes);

/// <summary>Payload to generate an invoice for a society against a plan.</summary>
public record GenerateInvoiceRequest(
    string SocietyKey,
    int PlanId,
    DateTime? PeriodStart,
    int? DueInDays,
    string? Notes);

/// <summary>Payload to mark an invoice paid (optional external reference).</summary>
public record PayInvoiceRequest(string? PaymentReference);

/// <summary>Aggregate figures for the Payments dashboard.</summary>
public record InvoiceSummaryDto(
    int Total,
    int Paid,
    int Issued,
    int Overdue,
    int Draft,
    int Void,
    decimal OutstandingAmount,
    decimal CollectedAmount,
    string Currency);

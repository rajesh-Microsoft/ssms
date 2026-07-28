namespace SMMS.Api.Dtos;

/// <summary>Admin request to generate monthly maintenance invoices. Amount is optional —
/// when null the society's configured MaintenanceAmt is used.</summary>
public record GenerateBillingRequest(int Month, int Year, decimal? Amount);

/// <summary>Result of a billing run.</summary>
public record BillingRunResult(int Month, int Year, decimal Amount, int Generated, int Skipped, int ActiveMembers);

namespace SMMS.Api.Dtos;

/// <summary>Admin request to generate monthly maintenance invoices. Amount is optional —
/// when null the society's configured MaintenanceAmt is used.</summary>
public record GenerateBillingRequest(int Month, int Year, decimal? Amount);

/// <summary>Result of a billing run.</summary>
public record BillingRunResult(int Month, int Year, decimal Amount, int Generated, int Skipped, int ActiveMembers);

/// <summary>Admin request to raise a one-time charge from a OneTime collection category.
/// Amount null → use the category's own method/amount per flat. MemberIds null/empty → all active flats.</summary>
public record OneTimeChargeRequest(int CategoryId, decimal? Amount, DateTime? DueDate, IReadOnlyList<int>? MemberIds);

/// <summary>Result of a one-time charge run.</summary>
public record OneTimeChargeResult(int CategoryId, string CategoryName, decimal TotalAmount,
    int Generated, int Skipped, int Targeted, string BillingLabel);

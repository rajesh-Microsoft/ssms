namespace SMMS.Api.Dtos;

public record ComponentRateDto(string Key, decimal Amount);
public record ComponentFlatOverrideDto(int MemberId, bool IsApplicable, decimal? Amount);

public record MaintenanceComponentDto(int Id, string Name, string? Description, string Method,
    decimal Amount, decimal? PercentageValue, int? PercentageBaseComponentId,
    bool ApplyToAllFlats, bool IsActive, int SortOrder,
    ComponentRateDto[] Rates, ComponentFlatOverrideDto[] FlatOverrides);

public record MaintenanceComponentUpsertRequest(string Name, string? Description, string Method,
    decimal Amount, decimal? PercentageValue, int? PercentageBaseComponentId,
    bool ApplyToAllFlats, bool IsActive, int SortOrder,
    ComponentRateDto[] Rates, ComponentFlatOverrideDto[] FlatOverrides);

/// <summary>One line of a previewed/persisted invoice breakdown.</summary>
public record InvoiceLineDto(int? ComponentId, string ComponentName, string Method, decimal Amount);

/// <summary>Full per-flat invoice preview produced by the rule engine (no rows saved).</summary>
public record FlatInvoiceDto(int MemberId, string FlatNumber, InvoiceLineDto[] Lines, decimal Total);

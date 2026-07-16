namespace SMMS.Api.Dtos;

public record SettingsDto(string SocietyName, string? Address, string? Email, string? Phone,
    decimal MaintenanceAmt, string[] Floors, string[] Categories, string Theme);

public record SettingsUpsertRequest(string SocietyName, string? Address, string? Email, string? Phone,
    decimal MaintenanceAmt, string[] Floors, string[] Categories, string Theme);

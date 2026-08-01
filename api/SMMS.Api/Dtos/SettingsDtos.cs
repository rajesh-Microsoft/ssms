namespace SMMS.Api.Dtos;

public record SettingsDto(string SocietyName, string? Address, string? Email, string? Phone,
    string? RegistrationNumber, string? Gst, string? Pan, string? LogoBase64,
    decimal MaintenanceAmt, int DueDay, decimal LateFee, int GraceDays, string? FinancialYear,
    string[] Floors, string[] Categories, string Theme, string PrimaryColor, string? SecondaryColor,
    string? ApplicationTitle, int BillingDay, bool AutoGenerateInvoices, string MaintenanceCalcMethod,
    string[] Towers, string[] FlatTypes);

public record SettingsUpsertRequest(string SocietyName, string? Address, string? Email, string? Phone,
    string? RegistrationNumber, string? Gst, string? Pan, string? LogoBase64,
    decimal MaintenanceAmt, int DueDay, decimal LateFee, int GraceDays, string? FinancialYear,
    string[] Floors, string[] Categories, string Theme, string PrimaryColor, string? SecondaryColor,
    string? ApplicationTitle, int BillingDay, bool AutoGenerateInvoices, string? MaintenanceCalcMethod = null,
    string[]? Towers = null, string[]? FlatTypes = null);

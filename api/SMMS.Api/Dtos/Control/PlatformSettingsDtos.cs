namespace SMMS.Api.Dtos.Control;

/// <summary>The platform configuration surface. The SMTP password is write-only: it is never
/// returned — only <see cref="SmtpPasswordSet"/> indicates whether one is stored.</summary>
public record PlatformSettingsDto(
    string? BrandName,
    string? SupportEmail,
    string? DefaultPlanCode,
    int ExpiryWarningDays,
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpUsername,
    string? SmtpFromEmail,
    bool SmtpEnableSsl,
    bool SmtpPasswordSet);

/// <summary>Update payload for platform settings. Null fields are left unchanged; the SMTP password
/// is only overwritten when a non-empty value is supplied.</summary>
public record UpdatePlatformSettingsRequest(
    string? BrandName,
    string? SupportEmail,
    string? DefaultPlanCode,
    int? ExpiryWarningDays,
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpUsername,
    string? SmtpFromEmail,
    bool? SmtpEnableSsl,
    string? SmtpPassword);

using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models.Control;

/// <summary>A single platform configuration value, stored as a string key/value pair in SmmsControlDb.
/// Typed access is provided by the settings controller via <see cref="PlatformSettingKeys"/>.</summary>
public class PlatformSetting
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class PlatformSettingKeys
{
    public const string BrandName = "brand.name";
    public const string SupportEmail = "brand.supportEmail";
    public const string DefaultPlanCode = "billing.defaultPlanCode";
    public const string ExpiryWarningDays = "billing.expiryWarningDays";
    public const string SmtpHost = "smtp.host";
    public const string SmtpPort = "smtp.port";
    public const string SmtpUsername = "smtp.username";
    public const string SmtpPassword = "smtp.password";
    public const string SmtpFromEmail = "smtp.fromEmail";
    public const string SmtpEnableSsl = "smtp.enableSsl";
}

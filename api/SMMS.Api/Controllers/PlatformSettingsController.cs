using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data.Control;
using SMMS.Api.Dtos.Control;
using SMMS.Api.Models.Control;
using SMMS.Api.Services.Control;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/platform/settings")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformSettingsController(ControlDbContext db, PlatformAuditService audit) : ControllerBase
{
    private const int DefaultExpiryWarningDays = 15;

    [HttpGet]
    public async Task<ActionResult<PlatformSettingsDto>> Get()
    {
        var map = await db.PlatformSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        return Ok(ToDto(map));
    }

    [HttpPut]
    public async Task<ActionResult<PlatformSettingsDto>> Update(UpdatePlatformSettingsRequest req)
    {
        if (req.ExpiryWarningDays is < 0 or > 365)
            return BadRequest(new { message = "Expiry warning window must be between 0 and 365 days." });
        if (req.SmtpPort is < 1 or > 65535)
            return BadRequest(new { message = "SMTP port must be between 1 and 65535." });

        await SetAsync(PlatformSettingKeys.BrandName, req.BrandName);
        await SetAsync(PlatformSettingKeys.SupportEmail, req.SupportEmail);
        await SetAsync(PlatformSettingKeys.DefaultPlanCode, req.DefaultPlanCode?.Trim().ToUpperInvariant());
        if (req.ExpiryWarningDays is int days) await SetAsync(PlatformSettingKeys.ExpiryWarningDays, days.ToString());
        await SetAsync(PlatformSettingKeys.SmtpHost, req.SmtpHost);
        if (req.SmtpPort is int port) await SetAsync(PlatformSettingKeys.SmtpPort, port.ToString());
        await SetAsync(PlatformSettingKeys.SmtpUsername, req.SmtpUsername);
        await SetAsync(PlatformSettingKeys.SmtpFromEmail, req.SmtpFromEmail);
        if (req.SmtpEnableSsl is bool ssl) await SetAsync(PlatformSettingKeys.SmtpEnableSsl, ssl ? "true" : "false");
        // Password is write-only: only overwrite when a new non-empty value is supplied.
        if (!string.IsNullOrEmpty(req.SmtpPassword))
            await SetAsync(PlatformSettingKeys.SmtpPassword, req.SmtpPassword);

        await db.SaveChangesAsync();
        await audit.LogAsync("PlatformSettingsUpdate", "Settings", null, "Platform settings updated.");

        var map = await db.PlatformSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        return Ok(ToDto(map));
    }

    private async Task SetAsync(string key, string? value)
    {
        var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var row = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
            db.PlatformSettings.Add(new PlatformSetting { Key = key, Value = v, UpdatedAt = DateTime.UtcNow });
        else
        {
            row.Value = v;
            row.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static PlatformSettingsDto ToDto(IReadOnlyDictionary<string, string?> m)
    {
        string? V(string k) => m.TryGetValue(k, out var v) ? v : null;

        return new PlatformSettingsDto(
            BrandName: V(PlatformSettingKeys.BrandName),
            SupportEmail: V(PlatformSettingKeys.SupportEmail),
            DefaultPlanCode: V(PlatformSettingKeys.DefaultPlanCode),
            ExpiryWarningDays: int.TryParse(V(PlatformSettingKeys.ExpiryWarningDays), out var d) ? d : DefaultExpiryWarningDays,
            SmtpHost: V(PlatformSettingKeys.SmtpHost),
            SmtpPort: int.TryParse(V(PlatformSettingKeys.SmtpPort), out var p) ? p : null,
            SmtpUsername: V(PlatformSettingKeys.SmtpUsername),
            SmtpFromEmail: V(PlatformSettingKeys.SmtpFromEmail),
            SmtpEnableSsl: bool.TryParse(V(PlatformSettingKeys.SmtpEnableSsl), out var b) && b,
            SmtpPasswordSet: !string.IsNullOrEmpty(V(PlatformSettingKeys.SmtpPassword)));
    }
}

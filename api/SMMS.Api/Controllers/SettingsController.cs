using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    private static SettingsDto ToDto(Models.SocietySettings s) => new(
        s.SocietyName, s.Address, s.Email, s.Phone,
        s.RegistrationNumber, s.Gst, s.Pan, s.LogoBase64,
        s.MaintenanceAmt, s.DueDay, s.LateFee, s.GraceDays, s.FinancialYear,
        s.Floors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        s.Categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        s.Theme, s.PrimaryColor, s.SecondaryColor, s.ApplicationTitle,
        s.BillingDay, s.AutoGenerateInvoices, s.MaintenanceCalcMethod,
        s.Towers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // Public society branding (name/address/contact/floors) — needed by the pre-login
    // landing page (home.html) so it can render the correct society for the current
    // tenant subdomain before the visitor has any credentials. Not sensitive data, and
    // already scoped to the correct tenant DB by TenantResolutionMiddleware.
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<SettingsDto>> Get()
    {
        var settings = await db.Settings.FirstOrDefaultAsync();
        if (settings is null) return NotFound();
        return Ok(ToDto(settings));
    }

    [HttpPut]
    public async Task<IActionResult> Update(SettingsUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Settings)) return Forbid();
        var settings = await db.Settings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new Models.SocietySettings();
            db.Settings.Add(settings);
        }

        settings.SocietyName = request.SocietyName;
        settings.Address = request.Address;
        settings.Email = request.Email;
        settings.Phone = request.Phone;
        settings.RegistrationNumber = request.RegistrationNumber;
        settings.Gst = request.Gst;
        settings.Pan = request.Pan;
        settings.LogoBase64 = request.LogoBase64;
        settings.MaintenanceAmt = request.MaintenanceAmt;
        settings.DueDay = request.DueDay;
        settings.LateFee = request.LateFee;
        settings.GraceDays = request.GraceDays;
        settings.FinancialYear = request.FinancialYear;
        settings.Floors = string.Join(',', request.Floors);
        settings.Categories = string.Join(',', request.Categories);
        settings.Theme = request.Theme;
        settings.PrimaryColor = request.PrimaryColor;
        settings.SecondaryColor = request.SecondaryColor;
        settings.ApplicationTitle = request.ApplicationTitle;
        settings.BillingDay = request.BillingDay;
        settings.AutoGenerateInvoices = request.AutoGenerateInvoices;
        settings.MaintenanceCalcMethod = request.MaintenanceCalcMethod ?? settings.MaintenanceCalcMethod;
        settings.Towers = request.Towers is not null ? string.Join(',', request.Towers) : settings.Towers;

        await db.SaveChangesAsync();
        await audit.LogAsync("Settings", "Update", "Updated society settings");
        return NoContent();
    }
}

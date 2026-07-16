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
        s.SocietyName, s.Address, s.Email, s.Phone, s.MaintenanceAmt,
        s.Floors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        s.Categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        s.Theme);

    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get()
    {
        var settings = await db.Settings.FirstOrDefaultAsync();
        if (settings is null) return NotFound();
        return Ok(ToDto(settings));
    }

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(SettingsUpsertRequest request)
    {
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
        settings.MaintenanceAmt = request.MaintenanceAmt;
        settings.Floors = string.Join(',', request.Floors);
        settings.Categories = string.Join(',', request.Categories);
        settings.Theme = request.Theme;

        await db.SaveChangesAsync();
        await audit.LogAsync("Settings", "Update", "Updated society settings");
        return NoContent();
    }
}

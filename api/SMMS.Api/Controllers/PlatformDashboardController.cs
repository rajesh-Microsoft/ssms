using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data.Control;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Dtos.Control;
using SMMS.Api.Models.Control;
using SMMS.Api.Services.Control;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/platform/dashboard")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformDashboardController(
    ControlDbContext controlDb,
    DbTenantStore tenantStore,
    IServiceScopeFactory scopeFactory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PlatformDashboardDto>> Get()
    {
        var societies = await controlDb.Societies.AsNoTracking()
            .OrderBy(s => s.DisplayName)
            .ToListAsync();

        var dtos = societies.Select(s =>
        {
            var tenant = tenantStore.GetByKey(s.Key);
            var memberCount = tenant is not null ? TenantMetrics.CountMembers(scopeFactory, tenant) : 0;
            return SocietyMapping.ToDto(s, memberCount);
        }).ToList();

        var dto = new PlatformDashboardDto(
            TotalSocieties: dtos.Count,
            ActiveSocieties: dtos.Count(d => d.Status == SocietyStatus.Active),
            SuspendedSocieties: dtos.Count(d => d.Status == SocietyStatus.Suspended),
            TrialSocieties: dtos.Count(d => d.Status == SocietyStatus.Trial),
            ExpiredSocieties: dtos.Count(d => d.IsExpired),
            ExpiringSoonSocieties: dtos.Count(d => !d.IsExpired
                && d.DaysUntilExpiry is >= 0 and <= SocietyMapping.ExpiringSoonDays),
            TotalMembers: dtos.Sum(d => d.MemberCount),
            Societies: dtos);

        return Ok(dto);
    }
}

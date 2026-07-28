using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data.Control;
using SMMS.Api.Dtos.Control;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/platform/audit")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformAuditController(ControlDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlatformAuditDto>>> List([FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 500);

        var logs = await db.PlatformAuditLogs.AsNoTracking()
            .OrderByDescending(l => l.Timestamp)
            .Take(take)
            .ToListAsync();

        return Ok(logs.Select(l => new PlatformAuditDto(
            l.Id, l.ActorUsername, l.Action, l.TargetType,
            l.TargetKey, l.Details, l.ImpersonatedTenant, l.Timestamp)));
    }
}

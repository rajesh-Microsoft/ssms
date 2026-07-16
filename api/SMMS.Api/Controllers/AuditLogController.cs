using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/auditlog")]
[Authorize(Roles = "Admin")]
public class AuditLogController(SmmsDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> GetAll()
    {
        var entries = await db.AuditLog.OrderByDescending(a => a.Timestamp).Take(500).ToListAsync();
        return Ok(entries.Select(a => new AuditLogDto(a.Id, a.Timestamp, a.User, a.Module, a.Action, a.Details)));
    }
}

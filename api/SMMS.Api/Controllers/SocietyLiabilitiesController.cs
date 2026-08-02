using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/society-liabilities")]
[Authorize]
public class SocietyLiabilitiesController(SmmsDbContext db, SocietyLiabilityService service, AuditService audit)
    : ControllerBase
{
    private static SocietyLiabilityDto ToDto(SocietyLiability l) => new(
        l.Id,
        l.Source.ToString(),
        l.MemberId,
        l.Member?.Name ?? l.ContributorName ?? "—",
        l.Date,
        l.Amount,
        l.SettledAmount,
        l.Amount - l.SettledAmount,
        l.Status.ToString(),
        l.Purpose,
        l.Settlements
            .OrderByDescending(s => s.Date)
            .Select(s => new SocietyLiabilitySettlementDto(s.Id, s.Date, s.Amount, s.Method.ToString(), s.ExpenseId, s.Note, s.Reference)));

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SocietyLiabilityDto>>> GetAll([FromQuery] string? status)
    {
        if (!User.CanView(PermissionModules.Liabilities)) return Forbid();
        var query = db.SocietyLiabilities.Include(l => l.Member).Include(l => l.Settlements).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LiabilityStatus>(status, true, out var st))
            query = query.Where(l => l.Status == st);
        var results = await query.OrderByDescending(l => l.Date).ToListAsync();
        return Ok(results.Select(ToDto));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<SocietyLiabilitySummaryDto>> GetSummary()
    {
        if (!User.CanView(PermissionModules.Liabilities)) return Forbid();
        var open = await db.SocietyLiabilities
            .Where(l => l.Status != LiabilityStatus.Settled)
            .ToListAsync();
        var total = open.Sum(l => l.Amount - l.SettledAmount);
        var contributors = open.Where(l => l.MemberId != null).Select(l => l.MemberId).Distinct().Count()
            + open.Count(l => l.MemberId == null);
        return Ok(new SocietyLiabilitySummaryDto(total, open.Count, contributors));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SocietyLiabilityDto>> GetById(int id)
    {
        if (!User.CanView(PermissionModules.Liabilities)) return Forbid();
        var l = await db.SocietyLiabilities.Include(x => x.Member).Include(x => x.Settlements)
            .FirstOrDefaultAsync(x => x.Id == id);
        return l is null ? NotFound() : Ok(ToDto(l));
    }

    [HttpPost]
    public async Task<ActionResult<SocietyLiabilityDto>> Create(SocietyLiabilityUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Liabilities)) return Forbid();
        if (!Enum.TryParse<LiabilitySource>(request.Source, true, out var source))
            return BadRequest("Invalid source.");
        if (request.MemberId is null && string.IsNullOrWhiteSpace(request.ContributorName))
            return BadRequest("Provide either a member or a contributor name.");
        if (request.MemberId is int mid && !await db.Members.AnyAsync(m => m.Id == mid))
            return BadRequest("Member not found.");

        var liability = service.Create(source, request.MemberId, request.ContributorName?.Trim(),
            request.Date, request.Amount, request.Purpose?.Trim());
        await db.SaveChangesAsync();
        await audit.LogAsync("Liabilities", "Add",
            $"Recorded liability of {liability.Amount} ({liability.Source})");

        await db.Entry(liability).Reference(l => l.Member).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = liability.Id }, ToDto(liability));
    }

    [HttpPost("{id:int}/settle")]
    public async Task<ActionResult<SocietyLiabilityDto>> Settle(int id, SocietyLiabilitySettleRequest request)
    {
        if (!User.CanEdit(PermissionModules.Liabilities)) return Forbid();
        var liability = await db.SocietyLiabilities.Include(l => l.Member).Include(l => l.Settlements)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (liability is null) return NotFound();
        if (liability.Status == LiabilityStatus.Settled) return BadRequest("Liability is already fully settled.");
        if (!Enum.TryParse<LiabilitySettlementMethod>(request.Method, true, out var method))
            return BadRequest("Invalid settlement method.");
        if (method == LiabilitySettlementMethod.ConvertedToAdvance && liability.Member is null)
            return BadRequest("Cannot convert to advance without a linked member.");

        var applied = service.Settle(liability, request.Amount, method, liability.Member,
            request.PaymentMode, request.Reference, request.Note);
        if (applied <= 0) return BadRequest("Nothing outstanding to settle.");

        await db.SaveChangesAsync();
        await audit.LogAsync("Liabilities", "Settle",
            $"Settled {applied} on liability #{id} via {method}");
        return Ok(ToDto(liability));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!User.CanEdit(PermissionModules.Liabilities)) return Forbid();
        var liability = await db.SocietyLiabilities.FindAsync(id);
        if (liability is null) return NotFound();
        if (liability.SettledAmount > 0) return BadRequest("Cannot delete a liability that has settlements.");

        db.SocietyLiabilities.Remove(liability);
        await db.SaveChangesAsync();
        await audit.LogAsync("Liabilities", "Delete", $"Deleted liability #{id}");
        return NoContent();
    }
}

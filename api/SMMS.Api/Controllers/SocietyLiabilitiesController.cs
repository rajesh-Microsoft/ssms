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
    private static SocietyLiabilityDto ToDto(SocietyLiability l, int? expenseId) => new(
        l.Id,
        l.Source.ToString(),
        l.MemberId,
        l.Member?.Name ?? l.ContributorName ?? "—",
        l.Date,
        l.Amount,
        l.SettledAmount,
        l.Amount - l.SettledAmount,
        l.Status.ToString(),
        l.Category,
        l.Purpose,
        expenseId,
        l.Settlements
            .OrderByDescending(s => s.Date)
            .Select(s => new SocietyLiabilitySettlementDto(s.Id, s.Date, s.Amount, s.Method.ToString(), s.ExpenseId, s.Note, s.Reference)));

    /// <summary>Liability id -> the cost row booked when it was raised, for the whole result set.</summary>
    private async Task<Dictionary<int, int>> FundedExpenseIdsAsync(IEnumerable<int> liabilityIds)
    {
        var ids = liabilityIds.ToList();
        return await db.Expenses
            .Where(e => e.FundedByLiabilityId != null && ids.Contains(e.FundedByLiabilityId!.Value))
            .ToDictionaryAsync(e => e.FundedByLiabilityId!.Value, e => e.Id);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SocietyLiabilityDto>>> GetAll([FromQuery] string? status)
    {
        if (!User.CanView(PermissionModules.Liabilities)) return Forbid();
        var query = db.SocietyLiabilities.Include(l => l.Member).Include(l => l.Settlements).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LiabilityStatus>(status, true, out var st))
            query = query.Where(l => l.Status == st);
        var results = await query.OrderByDescending(l => l.Date).ToListAsync();
        var expenseIds = await FundedExpenseIdsAsync(results.Select(l => l.Id));
        return Ok(results.Select(l => ToDto(l, expenseIds.TryGetValue(l.Id, out var eid) ? eid : null)));
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
        if (l is null) return NotFound();
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.FundedByLiabilityId == id);
        return Ok(ToDto(l, expense?.Id));
    }

    [HttpPost]
    public async Task<ActionResult<SocietyLiabilityDto>> Create(SocietyLiabilityUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Liabilities)) return Forbid();
        if (!Enum.TryParse<LiabilitySource>(request.Source, true, out var source))
            return BadRequest("Invalid source.");
        if (request.MemberId is null && string.IsNullOrWhiteSpace(request.ContributorName))
            return BadRequest("Provide either a member or a contributor name.");
        if (string.IsNullOrWhiteSpace(request.Category))
            return BadRequest("Pick the expense category this money paid for.");

        Member? member = null;
        if (request.MemberId is int mid)
        {
            member = await db.Members.FirstOrDefaultAsync(m => m.Id == mid);
            if (member is null) return BadRequest("Member not found.");
        }

        var liability = service.Create(source, member, request.ContributorName?.Trim(),
            request.Date, request.Amount, request.Category.Trim(), request.Purpose?.Trim());
        await db.SaveChangesAsync();
        await audit.LogAsync("Liabilities", "Add",
            $"Recorded liability of {liability.Amount} ({liability.Source}) and booked the {liability.Category} cost");

        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.FundedByLiabilityId == liability.Id);
        return CreatedAtAction(nameof(GetById), new { id = liability.Id }, ToDto(liability, expense?.Id));
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

        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.FundedByLiabilityId == id);
        var applied = service.Settle(liability, request.Amount, method, liability.Member,
            costAlreadyBooked: expense is not null, request.PaymentMode, request.Reference, request.Note);
        if (applied <= 0) return BadRequest("Nothing outstanding to settle.");

        await db.SaveChangesAsync();
        await audit.LogAsync("Liabilities", "Settle",
            $"Settled {applied} on liability #{id} via {method}");
        return Ok(ToDto(liability, expense?.Id));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!User.CanEdit(PermissionModules.Liabilities)) return Forbid();
        var liability = await db.SocietyLiabilities.FindAsync(id);
        if (liability is null) return NotFound();
        if (liability.SettledAmount > 0) return BadRequest("Cannot delete a liability that has settlements.");

        // The cost row exists only because this liability does, so it goes with it.
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.FundedByLiabilityId == id);
        if (expense is not null) db.Expenses.Remove(expense);

        db.SocietyLiabilities.Remove(liability);
        await db.SaveChangesAsync();
        await audit.LogAsync("Liabilities", "Delete",
            $"Deleted liability #{id}" + (expense is null ? "" : $" and its {expense.Category} cost of {expense.Amount}"));
        return NoContent();
    }
}

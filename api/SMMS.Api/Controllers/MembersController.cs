using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;
using SMMS.Api.Services.Billing;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/members")]
[Authorize]
public class MembersController(SmmsDbContext db, AuditService audit, AdvanceService advance) : ControllerBase
{
    private static MemberDto ToDto(Member m) => new(m.Id, m.Name, m.Flat, m.Floor, m.Mobile, m.Email, m.Status,
        m.AreaSqFt, m.FlatType, m.Tower, m.AdvanceBalance, m.AdvanceMode);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MemberDto>>> GetAll()
    {
        if (!User.CanView(PermissionModules.Members)) return Forbid();
        var members = await db.Members.OrderBy(m => m.Name).ToListAsync();
        return Ok(members.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MemberDto>> GetById(int id)
    {
        if (!User.CanView(PermissionModules.Members)) return Forbid();
        var member = await db.Members.FindAsync(id);
        return member is null ? NotFound() : Ok(ToDto(member));
    }

    [HttpPost]
    public async Task<ActionResult<MemberDto>> Create(MemberUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Members)) return Forbid();
        var member = new Member
        {
            Name = request.Name,
            Flat = request.Flat,
            Floor = request.Floor,
            Mobile = request.Mobile,
            Email = request.Email,
            Status = request.Status,
            AreaSqFt = request.AreaSqFt,
            FlatType = request.FlatType,
            Tower = request.Tower
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        await audit.LogAsync("Members", "Add", $"Added: {member.Name} ({member.Flat})");
        return CreatedAtAction(nameof(GetById), new { id = member.Id }, ToDto(member));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, MemberUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Members)) return Forbid();
        var member = await db.Members.FindAsync(id);
        if (member is null) return NotFound();

        member.Name = request.Name;
        member.Flat = request.Flat;
        member.Floor = request.Floor;
        member.Mobile = request.Mobile;
        member.Email = request.Email;
        member.Status = request.Status;
        member.AreaSqFt = request.AreaSqFt;
        member.FlatType = request.FlatType;
        member.Tower = request.Tower;
        await db.SaveChangesAsync();
        await audit.LogAsync("Members", "Update", $"Updated: {member.Name} ({member.Flat})");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!User.CanEdit(PermissionModules.Members)) return Forbid();
        var member = await db.Members.FindAsync(id);
        if (member is null) return NotFound();

        // Member is referenced (Restrict) by proofs, collections, overrides and the advance
        // ledger — clear the owned rows first so the hard delete can proceed. CollectionLines
        // cascade off Collections at the DB level.
        var collectionIds = await db.Collections.Where(c => c.MemberId == id).Select(c => c.Id).ToListAsync();
        var proofs = await db.PaymentProofs
            .Where(p => p.MemberId == id || collectionIds.Contains(p.CollectionId)).ToListAsync();
        if (proofs.Count > 0) db.PaymentProofs.RemoveRange(proofs);
        var collections = await db.Collections.Where(c => c.MemberId == id).ToListAsync();
        if (collections.Count > 0) db.Collections.RemoveRange(collections);
        var overrides = await db.MaintenanceComponentFlatOverrides.Where(o => o.MemberId == id).ToListAsync();
        if (overrides.Count > 0) db.MaintenanceComponentFlatOverrides.RemoveRange(overrides);
        var ledger = await db.AdvanceLedger.Where(e => e.MemberId == id).ToListAsync();
        if (ledger.Count > 0) db.AdvanceLedger.RemoveRange(ledger);
        db.Members.Remove(member);
        await db.SaveChangesAsync();
        await audit.LogAsync("Members", "Delete", $"Deleted member id: {id}");
        return NoContent();
    }

    // ── Advance wallet (money handling → gated on the Collections permission) ──

    /// <summary>A member's wallet balance, mode and full ledger history (newest first).</summary>
    [HttpGet("{id:int}/advance-ledger")]
    public async Task<ActionResult<AdvanceLedgerDto>> GetAdvanceLedger(int id)
    {
        if (!User.CanView(PermissionModules.Collections)) return Forbid();
        var member = await db.Members.FindAsync(id);
        if (member is null) return NotFound();

        var entries = await db.AdvanceLedger
            .Where(e => e.MemberId == id)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Select(e => new AdvanceLedgerEntryDto(e.Id, e.Date, e.Type, e.Amount, e.BalanceAfter,
                e.Source, e.CollectionId, e.Note))
            .ToListAsync();

        return Ok(new AdvanceLedgerDto(member.Id, member.Name, member.Flat,
            member.AdvanceBalance, member.AdvanceMode, entries));
    }

    /// <summary>Manually credit or debit a member's wallet (admin adjustment). Debits are capped at the balance.</summary>
    [HttpPost("{id:int}/advance-adjust")]
    public async Task<ActionResult<AdvanceLedgerDto>> AdjustAdvance(int id, AdvanceAdjustRequest request)
    {
        if (!User.CanEdit(PermissionModules.Collections)) return Forbid();
        if (request.Amount <= 0) return BadRequest(new { message = "Amount must be greater than zero." });

        var member = await db.Members.FindAsync(id);
        if (member is null) return NotFound();

        var note = string.IsNullOrWhiteSpace(request.Note) ? "Manual admin adjustment" : request.Note!.Trim();
        if (request.Type == "Credit")
        {
            advance.Credit(member, request.Amount, "ManualAdmin", note);
        }
        else if (request.Type == "Debit")
        {
            if (request.Amount > member.AdvanceBalance)
                return BadRequest(new { message = "Debit exceeds the wallet balance." });
            advance.Debit(member, request.Amount, "ManualAdmin", note);
        }
        else
        {
            return BadRequest(new { message = "Type must be 'Credit' or 'Debit'." });
        }

        await db.SaveChangesAsync();
        await audit.LogAsync("Collections", "AdvanceAdjust",
            $"{request.Type} {request.Amount:0.00} to {member.Flat} wallet (bal {member.AdvanceBalance:0.00}): {note}");
        return await GetAdvanceLedger(id);
    }

    /// <summary>Refunds (zeroes) a member's wallet balance — e.g. on move-out. Records a Refund debit.</summary>
    [HttpPost("{id:int}/advance-refund")]
    public async Task<ActionResult<AdvanceLedgerDto>> RefundAdvance(int id, AdvanceRefundRequest request)
    {
        if (!User.CanEdit(PermissionModules.Collections)) return Forbid();
        var member = await db.Members.FindAsync(id);
        if (member is null) return NotFound();
        if (member.AdvanceBalance <= 0) return BadRequest(new { message = "Wallet balance is already zero." });

        var refunded = member.AdvanceBalance;
        var note = string.IsNullOrWhiteSpace(request.Note) ? "Refund on move-out" : request.Note!.Trim();
        advance.Debit(member, refunded, "Refund", note);
        await db.SaveChangesAsync();
        await audit.LogAsync("Collections", "AdvanceRefund",
            $"Refunded {refunded:0.00} from {member.Flat} wallet: {note}");
        return await GetAdvanceLedger(id);
    }

    /// <summary>Switches a member's advance mode between "Auto" (auto-settle new invoices) and "Manual".</summary>
    [HttpPut("{id:int}/advance-mode")]
    public async Task<IActionResult> SetAdvanceMode(int id, AdvanceModeRequest request)
    {
        if (!User.CanEdit(PermissionModules.Collections)) return Forbid();
        if (request.Mode != "Auto" && request.Mode != "Manual")
            return BadRequest(new { message = "Mode must be 'Auto' or 'Manual'." });

        var member = await db.Members.FindAsync(id);
        if (member is null) return NotFound();

        member.AdvanceMode = request.Mode;
        await db.SaveChangesAsync();
        await audit.LogAsync("Collections", "AdvanceMode", $"Set {member.Flat} advance mode to {request.Mode}");
        return NoContent();
    }

    /// <summary>Advance Balance report: every member currently holding wallet credit (largest first).</summary>
    [HttpGet("advance/balances")]
    public async Task<ActionResult<IEnumerable<AdvanceBalanceRowDto>>> AdvanceBalances()
    {
        if (!User.CanView(PermissionModules.Collections)) return Forbid();
        var rows = await db.Members
            .Where(m => m.AdvanceBalance > 0)
            .OrderByDescending(m => m.AdvanceBalance)
            .Select(m => new AdvanceBalanceRowDto(m.Id, m.Name, m.Flat, m.AdvanceBalance, m.AdvanceMode))
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>Advance Deduction History report: wallet debits within an optional date range (newest first).</summary>
    [HttpGet("advance/deductions")]
    public async Task<ActionResult<IEnumerable<AdvanceDeductionRowDto>>> AdvanceDeductions(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (!User.CanView(PermissionModules.Collections)) return Forbid();
        var query = db.AdvanceLedger.Include(e => e.Member).Where(e => e.Type == "Debit");
        if (from.HasValue) query = query.Where(e => e.Date >= from.Value);
        if (to.HasValue) query = query.Where(e => e.Date < to.Value.AddDays(1));

        var rows = await query
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Select(e => new AdvanceDeductionRowDto(e.Id, e.Date, e.MemberId,
                e.Member!.Name, e.Member.Flat, e.Amount, e.BalanceAfter, e.Source, e.CollectionId, e.Note))
            .ToListAsync();
        return Ok(rows);
    }
}

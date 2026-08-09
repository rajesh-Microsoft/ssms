using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;
using SMMS.Api.Services.Storage;

namespace SMMS.Api.Controllers;

/// <summary>
/// Expense reimbursement: a member claims money they spent on the society's behalf, a reviewer
/// accepts or declines it, and acceptance turns it into a real liability.
/// </summary>
/// <remarks>
/// Members reach their own claims without any granted permission, the same way they can always see
/// their own payments. Reviewing needs Liabilities:Edit, because approving one commits the society
/// to paying it.
/// </remarks>
[ApiController]
[Route("api/reimbursements")]
[Authorize]
public class ReimbursementsController(
    SmmsDbContext db,
    SocietyLiabilityService liabilities,
    IFileStorage storage,
    AuditService audit) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Residents are linked to their Member row by flat, not a foreign key, so a user with
    /// no flat (or a flat that matches nothing) has no member to claim on behalf of.</summary>
    private async Task<Member?> ResolveMemberAsync()
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == CurrentUserId);
        if (user is null || string.IsNullOrWhiteSpace(user.Flat)) return null;
        var flat = user.Flat!.Trim().ToLower();
        return await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Flat.ToLower() == flat);
    }

    // ── Member's own claims ──────────────────────────────────────────────────

    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<ReimbursementDto>>> Mine()
    {
        var member = await ResolveMemberAsync();
        if (member is null) return Ok(Array.Empty<ReimbursementDto>());

        var rows = await Query().Where(r => r.MemberId == member.Id)
            .OrderByDescending(r => r.Id)
            .ToListAsync();

        return Ok(rows.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<ReimbursementDto>> Create(ReimbursementCreateRequest req)
    {
        var member = await ResolveMemberAsync();
        if (member is null)
            return BadRequest(new { message = "Your account is not linked to a flat, so a claim cannot be raised. Ask an admin to set your flat." });

        if (req.ExpenseDate.Date > DateTime.UtcNow.Date)
            return BadRequest(new { message = "The expense date cannot be in the future." });

        var request = new ReimbursementRequest
        {
            MemberId = member.Id,
            SubmittedByUserId = CurrentUserId,
            Category = req.Category.Trim(),
            Description = req.Description.Trim(),
            Vendor = string.IsNullOrWhiteSpace(req.Vendor) ? null : req.Vendor.Trim(),
            Amount = req.Amount,
            ExpenseDate = req.ExpenseDate.Date,
            PaymentMode = string.IsNullOrWhiteSpace(req.PaymentMode) ? null : req.PaymentMode.Trim(),
            TransactionReference = string.IsNullOrWhiteSpace(req.TransactionReference) ? null : req.TransactionReference.Trim(),
            Status = ReimbursementStatus.Pending
        };

        db.ReimbursementRequests.Add(request);
        await db.SaveChangesAsync();

        await audit.LogAsync("Reimbursements", "Create",
            $"{member.Flat} claimed {req.Amount:0.00} for {request.Category}.");

        var saved = await Query().FirstAsync(r => r.Id == request.Id);
        return CreatedAtAction(nameof(Mine), new { }, ToDto(saved));
    }

    /// <summary>Lets a member correct a claim while it is still theirs to change. An approved claim
    /// is immutable: money has already moved in the ledger.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ReimbursementDto>> Update(int id, ReimbursementCreateRequest req)
    {
        var member = await ResolveMemberAsync();
        if (member is null) return Forbid();

        var request = await db.ReimbursementRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (request.MemberId != member.Id) return Forbid();

        if (request.Status is not (ReimbursementStatus.Pending or ReimbursementStatus.NeedsInfo))
            return BadRequest(new { message = $"A {request.Status} claim can no longer be edited." });

        request.Category = req.Category.Trim();
        request.Description = req.Description.Trim();
        request.Vendor = string.IsNullOrWhiteSpace(req.Vendor) ? null : req.Vendor.Trim();
        request.Amount = req.Amount;
        request.ExpenseDate = req.ExpenseDate.Date;
        request.PaymentMode = string.IsNullOrWhiteSpace(req.PaymentMode) ? null : req.PaymentMode.Trim();
        request.TransactionReference = string.IsNullOrWhiteSpace(req.TransactionReference) ? null : req.TransactionReference.Trim();

        // Resubmitting answers the reviewer's question, so it goes back into the queue.
        if (request.Status == ReimbursementStatus.NeedsInfo)
        {
            request.Status = ReimbursementStatus.Pending;
            request.ReviewNote = null;
        }

        await db.SaveChangesAsync();
        await audit.LogAsync("Reimbursements", "Update", $"Claim #{id} updated by {member.Flat}.");

        return Ok(ToDto(await Query().FirstAsync(r => r.Id == id)));
    }

    /// <summary>Withdraws a claim the member no longer wants reviewed.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Withdraw(int id)
    {
        var member = await ResolveMemberAsync();
        if (member is null) return Forbid();

        var request = await db.ReimbursementRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (request.MemberId != member.Id) return Forbid();

        if (request.Status is not (ReimbursementStatus.Pending or ReimbursementStatus.NeedsInfo))
            return BadRequest(new { message = $"A {request.Status} claim cannot be withdrawn." });

        request.IsDeleted = true;
        await db.SaveChangesAsync();
        await audit.LogAsync("Reimbursements", "Withdraw", $"Claim #{id} withdrawn by {member.Flat}.");
        return NoContent();
    }

    // ── Reviewer ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReimbursementDto>>> List([FromQuery] string? status)
    {
        // Edit, not View: permissions default to View for anyone without an explicit grant, and a
        // claim names a neighbour, what they bought and their payment reference. Only people who
        // can actually act on the queue should read it; members use /mine for their own.
        if (!User.CanEdit(PermissionModules.Liabilities)) return Forbid();

        var q = Query();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ReimbursementStatus>(status, true, out var parsed))
            q = q.Where(r => r.Status == parsed);

        // Pending first: this list is a work queue, not an archive.
        var rows = await q.ToListAsync();
        return Ok(rows
            .OrderBy(r => r.Status == ReimbursementStatus.Pending ? 0 : 1)
            .ThenByDescending(r => r.Id)
            .Select(ToDto));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ReimbursementSummaryDto>> Summary()
    {
        if (!User.CanEdit(PermissionModules.Liabilities)) return Forbid();

        var rows = await Query().ToListAsync();
        var awaiting = rows
            .Where(r => r.Status == ReimbursementStatus.Approved && r.Liability is not null)
            .Select(r => r.Liability!.Amount - r.Liability!.SettledAmount)
            .Where(outstanding => outstanding > 0)
            .ToList();

        return Ok(new ReimbursementSummaryDto(
            rows.Count(r => r.Status == ReimbursementStatus.Pending),
            rows.Count(r => r.Status == ReimbursementStatus.NeedsInfo),
            awaiting.Count,
            awaiting.Sum()));
    }

    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<ReimbursementDto>> Approve(int id)
    {
        if (!User.CanEdit(PermissionModules.Liabilities)) return Forbid();

        var request = await db.ReimbursementRequests.Include(r => r.Member).FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (request.Status is ReimbursementStatus.Approved)
            return BadRequest(new { message = "This claim has already been approved." });
        if (request.Status is ReimbursementStatus.Rejected)
            return BadRequest(new { message = "A rejected claim cannot be approved. Ask the member to resubmit." });

        // Nobody signs off their own money, whatever permissions they hold.
        var reviewer = await ResolveMemberAsync();
        if (reviewer is not null && reviewer.Id == request.MemberId)
            return BadRequest(new { message = "You cannot approve your own reimbursement. Another committee member must review it." });

        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == request.MemberId);
        if (member is null) return BadRequest(new { message = "The member on this claim no longer exists." });

        // Creating the liability books the cost as an expense in the month it was incurred, and
        // records that the society still owes the money. Settlement stays a separate decision.
        var liability = liabilities.Create(
            LiabilitySource.MemberContribution, member, null,
            request.ExpenseDate, request.Amount, request.Category,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description);

        request.Status = ReimbursementStatus.Approved;
        request.Liability = liability;
        request.ReviewedByUserId = CurrentUserId;
        request.ReviewedOn = DateTime.UtcNow;
        request.ReviewNote = null;

        await db.SaveChangesAsync();

        await audit.LogAsync("Reimbursements", "Approve",
            $"Approved claim #{id} for {member.Flat}: {request.Amount:0.00} ({request.Category}). " +
            $"Liability #{liability.Id} raised and the cost booked to {request.ExpenseDate:MMM yyyy}.");

        return Ok(ToDto(await Query().FirstAsync(r => r.Id == id)));
    }

    [HttpPost("{id:int}/reject")]
    public Task<ActionResult<ReimbursementDto>> Reject(int id, ReimbursementReviewRequest req) =>
        ReviewAsync(id, ReimbursementStatus.Rejected, req.Note, "Reject");

    [HttpPost("{id:int}/request-info")]
    public Task<ActionResult<ReimbursementDto>> RequestInfo(int id, ReimbursementReviewRequest req) =>
        ReviewAsync(id, ReimbursementStatus.NeedsInfo, req.Note, "RequestInfo");

    private async Task<ActionResult<ReimbursementDto>> ReviewAsync(
        int id, ReimbursementStatus target, string note, string action)
    {
        if (!User.CanEdit(PermissionModules.Liabilities)) return Forbid();

        var request = await db.ReimbursementRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (request.Status == ReimbursementStatus.Approved)
            return BadRequest(new { message = "An approved claim cannot be changed. Settle or delete the liability instead." });

        var reviewer = await ResolveMemberAsync();
        if (reviewer is not null && reviewer.Id == request.MemberId)
            return BadRequest(new { message = "You cannot review your own reimbursement." });

        request.Status = target;
        request.ReviewNote = note.Trim();
        request.ReviewedByUserId = CurrentUserId;
        request.ReviewedOn = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await audit.LogAsync("Reimbursements", action, $"Claim #{id} -> {target}: {note.Trim()}");
        return Ok(ToDto(await Query().FirstAsync(r => r.Id == id)));
    }

    /// <summary>Repays the member without leaving the claim screen. Delegates to the same service
    /// the liabilities ledger uses, so partial settlement, advance conversion and the audit trail
    /// behave identically however the treasurer got here.</summary>
    [HttpPost("{id:int}/settle")]
    public async Task<ActionResult<ReimbursementDto>> Settle(int id, ReimbursementSettleRequest req)
    {
        if (!User.CanEdit(PermissionModules.Liabilities)) return Forbid();

        var request = await db.ReimbursementRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (request.Status != ReimbursementStatus.Approved || request.LiabilityId is null)
            return BadRequest(new { message = "Only an approved claim can be settled." });

        var liability = await db.SocietyLiabilities
            .Include(l => l.Member).Include(l => l.Settlements)
            .FirstOrDefaultAsync(l => l.Id == request.LiabilityId);
        if (liability is null) return BadRequest(new { message = "The liability behind this claim is missing." });
        if (liability.Status == LiabilityStatus.Settled)
            return BadRequest(new { message = "This claim has already been repaid in full." });

        if (!Enum.TryParse<LiabilitySettlementMethod>(req.Method, true, out var method))
            return BadRequest(new { message = "Settle either by Repaid or ConvertedToAdvance." });
        if (method == LiabilitySettlementMethod.ConvertedToAdvance && liability.Member is null)
            return BadRequest(new { message = "Cannot convert to an advance without a linked member." });

        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.FundedByLiabilityId == liability.Id);
        var applied = liabilities.Settle(liability, req.Amount, method, liability.Member,
            costAlreadyBooked: expense is not null, req.PaymentMode, req.Reference, req.Note);
        if (applied <= 0) return BadRequest(new { message = "There is nothing outstanding to settle." });

        await db.SaveChangesAsync();
        await audit.LogAsync("Reimbursements", "Settle",
            $"Repaid {applied:0.00} on claim #{id} (liability #{liability.Id}) via {method}" +
            (string.IsNullOrWhiteSpace(req.Reference) ? "." : $", ref {req.Reference.Trim()}."));

        return Ok(ToDto(await Query().FirstAsync(r => r.Id == id)));
    }

    // ── Attachments: the bill and the proof of payment ───────────────────────
    /// <summary>Whether the caller may see this claim's evidence: the member who raised it, or
    /// somebody who can act on the queue.</summary>
    private async Task<bool> CanSeeAsync(ReimbursementRequest request)
    {
        if (User.CanEdit(PermissionModules.Liabilities)) return true;
        var member = await ResolveMemberAsync();
        return member is not null && member.Id == request.MemberId;
    }

    [HttpGet("{id:int}/attachments")]
    public async Task<ActionResult<IEnumerable<ReimbursementAttachmentDto>>> Attachments(int id)
    {
        var request = await db.ReimbursementRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (!await CanSeeAsync(request)) return Forbid();

        var rows = await db.ReimbursementAttachments.AsNoTracking()
            .Where(a => a.RequestId == id).OrderBy(a => a.Id).ToListAsync();

        return Ok(rows.Select(a => new ReimbursementAttachmentDto(
            a.Id, a.FileName ?? "attachment", a.ContentType ?? "application/octet-stream",
            a.SizeBytes, a.UploadedOn)));
    }

    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(UploadRules.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<ReimbursementAttachmentDto>> Attach(int id, IFormFile file)
    {
        var member = await ResolveMemberAsync();
        if (member is null) return Forbid();

        var request = await db.ReimbursementRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (request.MemberId != member.Id) return Forbid();

        // Once approved the claim is evidence for money already committed; it must not change.
        if (request.Status is not (ReimbursementStatus.Pending or ReimbursementStatus.NeedsInfo))
            return BadRequest(new { message = $"A {request.Status} claim cannot take new attachments." });

        var problem = UploadRules.Validate(file, UploadRules.ImagesAndPdf);
        if (problem is not null) return BadRequest(new { message = problem });

        await using var stream = file.OpenReadStream();
        var stored = await storage.SaveAsync(stream, "reimbursements", file.FileName);

        var attachment = new ReimbursementAttachment
        {
            RequestId = id,
            StoredPath = stored,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            UploadedByUserId = CurrentUserId
        };
        db.ReimbursementAttachments.Add(attachment);
        await db.SaveChangesAsync();

        await audit.LogAsync("Reimbursements", "Attach", $"Attached '{file.FileName}' to claim #{id}.");

        return Ok(new ReimbursementAttachmentDto(
            attachment.Id, attachment.FileName!, attachment.ContentType!, attachment.SizeBytes, attachment.UploadedOn));
    }

    [HttpGet("{id:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> Download(int id, int attachmentId)
    {
        var request = await db.ReimbursementRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (!await CanSeeAsync(request)) return Forbid();

        var attachment = await db.ReimbursementAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.RequestId == id);
        if (attachment is null) return NotFound();

        var bytes = await storage.ReadAsync(attachment.StoredPath);
        if (bytes is null) return NotFound(new { message = "The stored file is missing." });

        // inline so the reviewer can eyeball a bill without downloading it first.
        Response.Headers.ContentDisposition = $"inline; filename=\"{Path.GetFileName(attachment.FileName ?? "attachment")}\"";
        return File(bytes, attachment.ContentType ?? "application/octet-stream");
    }

    [HttpDelete("{id:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> RemoveAttachment(int id, int attachmentId)
    {
        var member = await ResolveMemberAsync();
        if (member is null) return Forbid();

        var request = await db.ReimbursementRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (request.MemberId != member.Id) return Forbid();
        if (request.Status is not (ReimbursementStatus.Pending or ReimbursementStatus.NeedsInfo))
            return BadRequest(new { message = "Evidence on a reviewed claim cannot be removed." });

        var attachment = await db.ReimbursementAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.RequestId == id);
        if (attachment is null) return NotFound();

        db.ReimbursementAttachments.Remove(attachment);
        await db.SaveChangesAsync();
        storage.Delete(attachment.StoredPath);

        await audit.LogAsync("Reimbursements", "RemoveAttachment", $"Removed '{attachment.FileName}' from claim #{id}.");
        return NoContent();
    }

    private IQueryable<ReimbursementRequest> Query() =>
        db.ReimbursementRequests.AsNoTracking()
            .Include(r => r.Member).Include(r => r.Attachments)
            .Include(r => r.Liability).ThenInclude(l => l!.Settlements);
    private static ReimbursementDto ToDto(ReimbursementRequest r)
    {
        var settled = r.Liability?.SettledAmount ?? 0m;
        var outstanding = r.Liability is null ? 0m : r.Liability.Amount - settled;
        var last = r.Liability?.Settlements.OrderByDescending(s => s.Id).FirstOrDefault();

        // Before approval the approval state is the interesting one; after it, what the member
        // wants to know is whether they have actually been paid.
        var display = r.Status switch
        {
            ReimbursementStatus.Approved when r.Liability is null => "Approved",
            ReimbursementStatus.Approved => r.Liability!.Status switch
            {
                LiabilityStatus.Settled => "Settled",
                LiabilityStatus.PartiallySettled => "PartiallySettled",
                _ => "AwaitingSettlement"
            },
            _ => r.Status.ToString()
        };

        return new ReimbursementDto(
            r.Id, r.MemberId, r.Member?.Name ?? "(unknown)", r.Member?.Flat ?? "",
            r.Category, r.Description, r.Vendor, r.Amount, r.ExpenseDate,
            r.PaymentMode, r.TransactionReference, r.Status.ToString(),
            r.CreatedOn, r.ReviewNote, r.ReviewedOn,
            r.LiabilityId, settled, outstanding, r.Attachments.Count,
            last?.Date, last?.Method.ToString(), last?.Reference, display);
    }
}

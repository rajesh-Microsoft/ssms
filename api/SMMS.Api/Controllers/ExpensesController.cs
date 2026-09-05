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

[ApiController]
[Route("api/expenses")]
[Authorize]
public class ExpensesController(SmmsDbContext db, IFileStorage storage, AuditService audit) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static ExpenseDto ToDto(Expense e) => new(
        e.Id, e.ExpenseDate, e.Category, e.Description, e.Vendor, e.Amount, e.PaymentMode, e.Month, e.Year,
        e.Remarks, e.FundedByLiabilityId, e.Attachments.Count);

    private const string LiabilityOwned =
        "This cost was funded by a contributor and belongs to its liability. Edit or remove it from the Liabilities screen.";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExpenseDto>>> GetAll([FromQuery] int? year, [FromQuery] int? month)
    {
        if (!User.CanView(PermissionModules.Expenses)) return Forbid();
        var query = db.Expenses.Include(e => e.Attachments).AsQueryable();
        if (year.HasValue) query = query.Where(e => e.Year == year.Value);
        if (month.HasValue) query = query.Where(e => e.Month == month.Value);
        var results = await query.OrderByDescending(e => e.ExpenseDate).ToListAsync();
        return Ok(results.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ExpenseDto>> GetById(int id)
    {
        if (!User.CanView(PermissionModules.Expenses)) return Forbid();
        var e = await db.Expenses.Include(x => x.Attachments).FirstOrDefaultAsync(x => x.Id == id);
        return e is null ? NotFound() : Ok(ToDto(e));
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Create(ExpenseUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Expenses)) return Forbid();
        var expense = new Expense
        {
            ExpenseDate = request.ExpenseDate,
            Category = request.Category,
            Description = request.Description,
            Vendor = request.Vendor,
            Amount = request.Amount,
            PaymentMode = request.PaymentMode,
            Month = request.Month,
            Year = request.Year,
            Remarks = request.Remarks
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        await audit.LogAsync("Expenses", "Add", $"Added expense: {expense.Description} ({expense.Amount})");
        return CreatedAtAction(nameof(GetById), new { id = expense.Id }, ToDto(expense));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ExpenseUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Expenses)) return Forbid();
        var expense = await db.Expenses.FindAsync(id);
        if (expense is null) return NotFound();
        if (expense.FundedByLiabilityId is not null) return BadRequest(LiabilityOwned);

        expense.ExpenseDate = request.ExpenseDate;
        expense.Category = request.Category;
        expense.Description = request.Description;
        expense.Vendor = request.Vendor;
        expense.Amount = request.Amount;
        expense.PaymentMode = request.PaymentMode;
        expense.Month = request.Month;
        expense.Year = request.Year;
        expense.Remarks = request.Remarks;
        await db.SaveChangesAsync();
        await audit.LogAsync("Expenses", "Update", $"Updated expense id {id}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!User.CanEdit(PermissionModules.Expenses)) return Forbid();
        var expense = await db.Expenses.FindAsync(id);
        if (expense is null) return NotFound();
        if (expense.FundedByLiabilityId is not null) return BadRequest(LiabilityOwned);

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync();
        await audit.LogAsync("Expenses", "Delete", $"Deleted expense id: {id}");
        return NoContent();
    }

    // ── Attachments: the bill, receipt or payment screenshot behind the cost ───────────────
    [HttpGet("{id:int}/attachments")]
    public async Task<ActionResult<IEnumerable<ExpenseAttachmentDto>>> Attachments(int id)
    {
        if (!User.CanView(PermissionModules.Expenses)) return Forbid();
        if (!await db.Expenses.AnyAsync(e => e.Id == id)) return NotFound();

        var rows = await db.ExpenseAttachments.AsNoTracking()
            .Where(a => a.ExpenseId == id).OrderBy(a => a.Id).ToListAsync();

        return Ok(rows.Select(a => new ExpenseAttachmentDto(
            a.Id, a.FileName ?? "attachment", a.ContentType ?? "application/octet-stream",
            a.SizeBytes, a.UploadedOn)));
    }

    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(UploadRules.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<ExpenseAttachmentDto>> Attach(int id, IFormFile file)
    {
        if (!User.CanEdit(PermissionModules.Expenses)) return Forbid();

        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id);
        if (expense is null) return NotFound();

        var problem = UploadRules.Validate(file, UploadRules.ImagesAndPdf);
        if (problem is not null) return BadRequest(new { message = problem });

        await using var stream = file.OpenReadStream();
        var stored = await storage.SaveAsync(stream, "expenses", file.FileName);

        var attachment = new ExpenseAttachment
        {
            ExpenseId = id,
            StoredPath = stored,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            UploadedByUserId = CurrentUserId
        };
        db.ExpenseAttachments.Add(attachment);
        await db.SaveChangesAsync();

        await audit.LogAsync("Expenses", "Attach", $"Attached '{file.FileName}' to expense #{id}.");

        return Ok(new ExpenseAttachmentDto(
            attachment.Id, attachment.FileName!, attachment.ContentType!, attachment.SizeBytes, attachment.UploadedOn));
    }

    [HttpGet("{id:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> Download(int id, int attachmentId)
    {
        if (!User.CanView(PermissionModules.Expenses)) return Forbid();
        if (!await db.Expenses.AnyAsync(e => e.Id == id)) return NotFound();

        var attachment = await db.ExpenseAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.ExpenseId == id);
        if (attachment is null) return NotFound();

        var bytes = await storage.ReadAsync(attachment.StoredPath);
        if (bytes is null) return NotFound(new { message = "The stored file is missing." });

        // inline so a receipt can be eyeballed without downloading it first.
        Response.Headers.ContentDisposition = $"inline; filename=\"{Path.GetFileName(attachment.FileName ?? "attachment")}\"";
        return File(bytes, attachment.ContentType ?? "application/octet-stream");
    }

    [HttpDelete("{id:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> RemoveAttachment(int id, int attachmentId)
    {
        if (!User.CanEdit(PermissionModules.Expenses)) return Forbid();
        if (!await db.Expenses.AnyAsync(e => e.Id == id)) return NotFound();

        var attachment = await db.ExpenseAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.ExpenseId == id);
        if (attachment is null) return NotFound();

        db.ExpenseAttachments.Remove(attachment);
        await db.SaveChangesAsync();
        storage.Delete(attachment.StoredPath);

        await audit.LogAsync("Expenses", "RemoveAttachment", $"Removed '{attachment.FileName}' from expense #{id}.");
        return NoContent();
    }
}

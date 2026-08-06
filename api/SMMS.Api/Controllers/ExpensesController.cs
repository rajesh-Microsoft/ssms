using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/expenses")]
[Authorize]
public class ExpensesController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    private static ExpenseDto ToDto(Expense e) => new(
        e.Id, e.ExpenseDate, e.Category, e.Description, e.Vendor, e.Amount, e.PaymentMode, e.Month, e.Year,
        e.Remarks, e.FundedByLiabilityId);

    private const string LiabilityOwned =
        "This cost was funded by a contributor and belongs to its liability. Edit or remove it from the Liabilities screen.";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExpenseDto>>> GetAll([FromQuery] int? year, [FromQuery] int? month)
    {
        if (!User.CanView(PermissionModules.Expenses)) return Forbid();
        var query = db.Expenses.AsQueryable();
        if (year.HasValue) query = query.Where(e => e.Year == year.Value);
        if (month.HasValue) query = query.Where(e => e.Month == month.Value);
        var results = await query.OrderByDescending(e => e.ExpenseDate).ToListAsync();
        return Ok(results.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ExpenseDto>> GetById(int id)
    {
        if (!User.CanView(PermissionModules.Expenses)) return Forbid();
        var e = await db.Expenses.FindAsync(id);
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
}

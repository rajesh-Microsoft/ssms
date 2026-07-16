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
        e.Id, e.ExpenseDate, e.Category, e.Description, e.Vendor, e.Amount, e.PaymentMode, e.Month, e.Year, e.Remarks);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExpenseDto>>> GetAll([FromQuery] int? year, [FromQuery] int? month)
    {
        var query = db.Expenses.AsQueryable();
        if (year.HasValue) query = query.Where(e => e.Year == year.Value);
        if (month.HasValue) query = query.Where(e => e.Month == month.Value);
        var results = await query.OrderByDescending(e => e.ExpenseDate).ToListAsync();
        return Ok(results.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ExpenseDto>> GetById(int id)
    {
        var e = await db.Expenses.FindAsync(id);
        return e is null ? NotFound() : Ok(ToDto(e));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ExpenseDto>> Create(ExpenseUpsertRequest request)
    {
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
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, ExpenseUpsertRequest request)
    {
        var expense = await db.Expenses.FindAsync(id);
        if (expense is null) return NotFound();

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
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var expense = await db.Expenses.FindAsync(id);
        if (expense is null) return NotFound();

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync();
        await audit.LogAsync("Expenses", "Delete", $"Deleted expense id: {id}");
        return NoContent();
    }
}

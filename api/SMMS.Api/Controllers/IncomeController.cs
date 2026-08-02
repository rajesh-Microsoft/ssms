using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/income")]
[Authorize]
public class IncomeController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    private static IncomeDto ToDto(SocietyIncome i) => new(
        i.Id, i.IncomeDate, i.Category, i.Source, i.Description, i.Amount, i.PaymentMode, i.Reference, i.Month, i.Year, i.Remarks);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<IncomeDto>>> GetAll([FromQuery] int? year, [FromQuery] int? month)
    {
        if (!User.CanView(PermissionModules.Income)) return Forbid();
        var query = db.SocietyIncomes.AsQueryable();
        if (year.HasValue) query = query.Where(i => i.Year == year.Value);
        if (month.HasValue) query = query.Where(i => i.Month == month.Value);
        var results = await query.OrderByDescending(i => i.IncomeDate).ToListAsync();
        return Ok(results.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<IncomeDto>> GetById(int id)
    {
        if (!User.CanView(PermissionModules.Income)) return Forbid();
        var i = await db.SocietyIncomes.FindAsync(id);
        return i is null ? NotFound() : Ok(ToDto(i));
    }

    [HttpPost]
    public async Task<ActionResult<IncomeDto>> Create(IncomeUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Income)) return Forbid();
        var income = new SocietyIncome
        {
            IncomeDate = request.IncomeDate,
            Category = request.Category,
            Source = request.Source,
            Description = request.Description,
            Amount = request.Amount,
            PaymentMode = request.PaymentMode,
            Reference = request.Reference,
            Month = request.Month,
            Year = request.Year,
            Remarks = request.Remarks
        };
        db.SocietyIncomes.Add(income);
        await db.SaveChangesAsync();
        await audit.LogAsync("Income", "Add", $"Added income: {income.Description} ({income.Amount})");
        return CreatedAtAction(nameof(GetById), new { id = income.Id }, ToDto(income));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, IncomeUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Income)) return Forbid();
        var income = await db.SocietyIncomes.FindAsync(id);
        if (income is null) return NotFound();

        income.IncomeDate = request.IncomeDate;
        income.Category = request.Category;
        income.Source = request.Source;
        income.Description = request.Description;
        income.Amount = request.Amount;
        income.PaymentMode = request.PaymentMode;
        income.Reference = request.Reference;
        income.Month = request.Month;
        income.Year = request.Year;
        income.Remarks = request.Remarks;
        await db.SaveChangesAsync();
        await audit.LogAsync("Income", "Update", $"Updated income id {id}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!User.CanEdit(PermissionModules.Income)) return Forbid();
        var income = await db.SocietyIncomes.FindAsync(id);
        if (income is null) return NotFound();

        db.SocietyIncomes.Remove(income);
        await db.SaveChangesAsync();
        await audit.LogAsync("Income", "Delete", $"Deleted income id: {id}");
        return NoContent();
    }
}

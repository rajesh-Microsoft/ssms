using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/budgets")]
[Authorize]
public class BudgetsController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>How far back the auto-forecast looks. Six months smooths out one-off bills without
    /// dragging in last year's tariffs.</summary>
    private const int LookbackMonths = 6;

    /// <summary>Months as a single comparable number, so period ranges translate to plain SQL
    /// instead of being evaluated client-side.</summary>
    private static int Ordinal(int year, int month) => year * 12 + month;

    private static BudgetItemDto ToDto(BudgetItem i) => new(
        i.Id, i.Category, i.Description, i.EstimatedAmount, i.ActualAmount,
        i.ActualAmount.HasValue ? i.ActualAmount.Value - i.EstimatedAmount : null,
        i.DueDate, i.Status, i.ExpenseId);

    private static BudgetDto ToDto(Budget b)
    {
        var items = b.Items.OrderBy(i => i.DueDate ?? DateTime.MaxValue).ThenBy(i => i.Category).ToList();
        // Once a bill has landed the actual figure is the better forecast, so it supersedes the estimate.
        var expectedExpense = items.Sum(i => i.ActualAmount ?? i.EstimatedAmount);
        var closing = b.OpeningBalance + b.ExpectedCollection - expectedExpense;
        // Deficit compares the month against itself: it is a warning that this month's income does not
        // cover this month's costs, even when a healthy opening balance hides that.
        var deficit = Math.Max(0m, expectedExpense - b.ExpectedCollection);

        return new BudgetDto(b.Id, b.Month, b.Year, b.OpeningBalance, b.ExpectedCollection,
            expectedExpense, closing, deficit, b.Status, b.Notes, items.Select(ToDto).ToList());
    }

    [HttpGet]
    public async Task<ActionResult<BudgetDto>> Get([FromQuery] int year, [FromQuery] int month)
    {
        if (!User.CanView(PermissionModules.Budgets)) return Forbid();
        var budget = await db.Budgets.Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Year == year && b.Month == month);
        return budget is null ? NotFound() : Ok(ToDto(budget));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BudgetDto>> GetById(int id)
    {
        if (!User.CanView(PermissionModules.Budgets)) return Forbid();
        var budget = await db.Budgets.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id);
        return budget is null ? NotFound() : Ok(ToDto(budget));
    }

    [HttpGet("months")]
    public async Task<ActionResult<IEnumerable<object>>> GetMonths([FromQuery] int year)
    {
        if (!User.CanView(PermissionModules.Budgets)) return Forbid();
        var months = await db.Budgets.Where(b => b.Year == year)
            .OrderBy(b => b.Month)
            .Select(b => new { b.Id, b.Month, b.Year, b.Status })
            .ToListAsync();
        return Ok(months);
    }

    [HttpPost]
    public async Task<ActionResult<BudgetDto>> Create(BudgetUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Budgets)) return Forbid();
        if (await db.Budgets.AnyAsync(b => b.Year == request.Year && b.Month == request.Month))
            return Conflict(new { message = $"A budget already exists for {request.Month}/{request.Year}." });

        var budget = new Budget
        {
            Month = request.Month,
            Year = request.Year,
            OpeningBalance = request.OpeningBalance,
            ExpectedCollection = request.ExpectedCollection,
            Status = request.Status ?? "Draft",
            Notes = request.Notes
        };
        db.Budgets.Add(budget);
        await db.SaveChangesAsync();
        await audit.LogAsync("Budgets", "Add", $"Created budget for {request.Month}/{request.Year}");
        return CreatedAtAction(nameof(GetById), new { id = budget.Id }, ToDto(budget));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, BudgetUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Budgets)) return Forbid();
        var budget = await db.Budgets.FindAsync(id);
        if (budget is null) return NotFound();

        budget.OpeningBalance = request.OpeningBalance;
        budget.ExpectedCollection = request.ExpectedCollection;
        budget.Status = request.Status ?? budget.Status;
        budget.Notes = request.Notes;
        await db.SaveChangesAsync();
        await audit.LogAsync("Budgets", "Update", $"Updated budget for {budget.Month}/{budget.Year}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!User.CanEdit(PermissionModules.Budgets)) return Forbid();
        var budget = await db.Budgets.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id);
        if (budget is null) return NotFound();

        // Deleting would cascade the items away and sever the trail from real expenses back to the plan.
        var converted = budget.Items.Count(i => i.ExpenseId.HasValue);
        if (converted > 0)
            return BadRequest(new { message = $"This budget has {converted} line(s) already booked as expenses. Delete those expenses first if you really need to remove the plan." });

        db.Budgets.Remove(budget);
        await db.SaveChangesAsync();
        await audit.LogAsync("Budgets", "Delete", $"Deleted budget for {budget.Month}/{budget.Year}");
        return NoContent();
    }

    [HttpPost("{id:int}/items")]
    public async Task<ActionResult<BudgetItemDto>> AddItem(int id, BudgetItemUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Budgets)) return Forbid();
        if (!await db.Budgets.AnyAsync(b => b.Id == id)) return NotFound();

        var item = new BudgetItem
        {
            BudgetId = id,
            Category = request.Category,
            Description = request.Description,
            EstimatedAmount = request.EstimatedAmount,
            DueDate = request.DueDate,
            Status = "Expected"
        };
        db.BudgetItems.Add(item);
        await db.SaveChangesAsync();
        await audit.LogAsync("Budgets", "AddItem", $"Added expected {item.Category} of {item.EstimatedAmount} to budget {id}");
        return Ok(ToDto(item));
    }

    [HttpPut("items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(int itemId, BudgetItemUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Budgets)) return Forbid();
        var item = await db.BudgetItems.FindAsync(itemId);
        if (item is null) return NotFound();
        if (item.ExpenseId.HasValue)
            return BadRequest(new { message = "This line is already booked as an expense. Edit the expense instead." });

        item.Category = request.Category;
        item.Description = request.Description;
        item.EstimatedAmount = request.EstimatedAmount;
        item.DueDate = request.DueDate;
        await db.SaveChangesAsync();
        await audit.LogAsync("Budgets", "UpdateItem", $"Updated budget line {itemId}");
        return NoContent();
    }

    [HttpDelete("items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int itemId)
    {
        if (!User.CanEdit(PermissionModules.Budgets)) return Forbid();
        var item = await db.BudgetItems.FindAsync(itemId);
        if (item is null) return NotFound();
        if (item.ExpenseId.HasValue)
            return BadRequest(new { message = "This line is already booked as an expense and cannot be removed from the plan." });

        db.BudgetItems.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync("Budgets", "DeleteItem", $"Removed budget line {itemId}");
        return NoContent();
    }

    /// <summary>Books the real bill against a planned line, creating the expense and stamping the link
    /// in one save so a line can never be booked twice.</summary>
    [HttpPost("items/{itemId:int}/convert")]
    public async Task<ActionResult<BudgetItemDto>> ConvertToActual(int itemId, ConvertToActualRequest request)
    {
        if (!User.CanEdit(PermissionModules.Budgets)) return Forbid();
        if (!User.CanEdit(PermissionModules.Expenses))
            return Forbid();

        var item = await db.BudgetItems.Include(i => i.Budget).FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is null) return NotFound();
        if (item.ExpenseId.HasValue)
            return BadRequest(new { message = "This line has already been booked as an expense." });
        if (request.ActualAmount <= 0)
            return BadRequest(new { message = "Actual amount must be greater than zero." });

        var budget = item.Budget!;
        var expense = new Expense
        {
            ExpenseDate = request.ExpenseDate ?? DateTime.UtcNow.Date,
            Category = item.Category,
            Description = string.IsNullOrWhiteSpace(item.Description) ? $"{item.Category} (budgeted)" : item.Description!,
            Vendor = request.Vendor,
            Amount = request.ActualAmount,
            PaymentMode = request.PaymentMode,
            Month = budget.Month,
            Year = budget.Year,
            Remarks = request.Remarks
        };

        // Assigning the navigation lets EF resolve the new key itself, keeping this to one round trip.
        db.Expenses.Add(expense);
        item.Expense = expense;
        item.ActualAmount = request.ActualAmount;
        item.Status = "Actual";
        await db.SaveChangesAsync();

        var variance = request.ActualAmount - item.EstimatedAmount;
        await audit.LogAsync("Budgets", "ConvertToActual",
            $"Booked {item.Category} for {budget.Month}/{budget.Year}: estimated {item.EstimatedAmount}, actual {request.ActualAmount} (variance {variance:+0.00;-0.00;0})");
        return Ok(ToDto(item));
    }

    /// <summary>Suggests an opening balance, an expected collection and per-category amounts drawn from
    /// what the society has actually spent.</summary>
    [HttpGet("suggest")]
    public async Task<ActionResult<BudgetSuggestionDto>> Suggest([FromQuery] int year, [FromQuery] int month)
    {
        if (!User.CanView(PermissionModules.Budgets)) return Forbid();

        var cutoff = Ordinal(year, month);

        // Cash position implied by everything recorded before this month. Partly-paid invoices only
        // count what actually came in.
        var paid = await db.Collections
            .Where(c => c.Year * 12 + c.Month < cutoff && c.Status == "Paid")
            .SumAsync(c => (decimal?)c.Amount) ?? 0m;
        var partlyPaid = await db.Collections
            .Where(c => c.Year * 12 + c.Month < cutoff && c.Status == "Partial")
            .SumAsync(c => (decimal?)c.AmountPaid) ?? 0m;
        var otherIncome = await db.SocietyIncomes
            .Where(i => i.Year * 12 + i.Month < cutoff)
            .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        var spent = await db.Expenses
            .Where(e => e.Year * 12 + e.Month < cutoff)
            .SumAsync(e => (decimal?)e.Amount) ?? 0m;
        var openingBalance = paid + partlyPaid + otherIncome - spent;

        // Prefer this month's raised invoices; before they are generated, last month is the best guide.
        var invoiced = await db.Collections
            .Where(c => c.Year == year && c.Month == month)
            .SumAsync(c => (decimal?)c.Amount) ?? 0m;
        if (invoiced == 0m)
        {
            invoiced = await db.Collections
                .Where(c => c.Year * 12 + c.Month == cutoff - 1)
                .SumAsync(c => (decimal?)c.Amount) ?? 0m;
        }

        var history = await db.Expenses
            .Where(e => e.Year * 12 + e.Month < cutoff && e.Year * 12 + e.Month >= cutoff - LookbackMonths)
            .GroupBy(e => new { e.Category, e.Year, e.Month })
            .Select(g => new { g.Key.Category, g.Key.Year, g.Key.Month, Total = g.Sum(x => x.Amount) })
            .ToListAsync();

        var categories = history
            .GroupBy(h => h.Category)
            .Select(g =>
            {
                var months = g.OrderBy(h => h.Year * 12 + h.Month).ToList();
                // Linearly weighted towards recent months: responsive to a rising tariff, but without the
                // wild extrapolation a trend line produces on noisy or sparse data.
                decimal weightedTotal = 0m;
                int weightSum = 0;
                for (var i = 0; i < months.Count; i++)
                {
                    var weight = i + 1;
                    weightedTotal += months[i].Total * weight;
                    weightSum += weight;
                }
                var suggested = weightSum == 0 ? 0m : Math.Round(weightedTotal / weightSum, 0, MidpointRounding.AwayFromZero);
                return new CategorySuggestionDto(g.Key, suggested, months.Count, months[^1].Total);
            })
            .OrderByDescending(c => c.SuggestedAmount)
            .ToList();

        return Ok(new BudgetSuggestionDto(
            Math.Round(openingBalance, 2),
            Math.Round(invoiced, 2),
            categories));
    }

    /// <summary>Budget against actual for a month. Actuals come from the expense ledger rather than only
    /// from converted lines, so spending nobody planned for still shows up.</summary>
    [HttpGet("variance")]
    public async Task<ActionResult<BudgetVarianceDto>> Variance([FromQuery] int year, [FromQuery] int month)
    {
        if (!User.CanView(PermissionModules.Budgets)) return Forbid();

        var budgeted = await db.BudgetItems
            .Where(i => i.Budget!.Year == year && i.Budget.Month == month)
            .GroupBy(i => i.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.EstimatedAmount) })
            .ToListAsync();

        var actual = await db.Expenses
            .Where(e => e.Year == year && e.Month == month)
            .GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync();

        var rows = budgeted.Select(b => b.Category)
            .Union(actual.Select(a => a.Category))
            .OrderBy(c => c)
            .Select(category =>
            {
                var plan = budgeted.FirstOrDefault(b => b.Category == category)?.Total ?? 0m;
                var real = actual.FirstOrDefault(a => a.Category == category)?.Total ?? 0m;
                return new BudgetVarianceRowDto(category, plan, real, real - plan, plan == 0m && real > 0m);
            })
            .ToList();

        return Ok(new BudgetVarianceDto(month, year,
            rows.Sum(r => r.Budgeted), rows.Sum(r => r.Actual), rows.Sum(r => r.Difference), rows));
    }
}

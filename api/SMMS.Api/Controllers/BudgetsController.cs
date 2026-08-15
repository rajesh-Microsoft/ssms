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

    /// <summary>A society can hold several meters of the same kind (one per block), so every
    /// matching connection counts towards the category, not just the most recently fetched one.</summary>
    private static List<UtilityBill> MatchingBills(string category, IEnumerable<UtilityBill> bills) => bills
        .Where(b => string.Equals(category, b.UtilityConnection!.Provider!.Category, StringComparison.OrdinalIgnoreCase)
            || category.Contains(b.UtilityConnection.Provider.Category, StringComparison.OrdinalIgnoreCase)
            || b.UtilityConnection.Provider.Category.Contains(category, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(b => b.FetchedOn)
        .ToList();

    private static BudgetItemDto ToDto(BudgetItem item, IEnumerable<UtilityBill> bills)
    {
        var liveBills = item.ActualAmount.HasValue ? [] : MatchingBills(item.Category, bills);
        var effectiveAmount = item.ActualAmount
            ?? (liveBills.Count > 0 ? liveBills.Sum(b => b.BillAmount) : item.EstimatedAmount);
        var source = item.ActualAmount.HasValue ? "Expense Ledger"
            : liveBills.Count > 0 ? "Live Utility Bill" : "Estimate";
        // Attributable to a single bill only when exactly one meter fed the figure.
        var singleBill = liveBills.Count == 1 ? liveBills[0] : null;
        return new BudgetItemDto(
            item.Id, item.Category, item.Description, item.EstimatedAmount, item.ActualAmount,
            effectiveAmount - item.EstimatedAmount,
            liveBills.Where(b => b.DueDate.HasValue).Min(b => b.DueDate) ?? item.DueDate,
            item.Status, item.ExpenseId,
            effectiveAmount, source,
            liveBills.Count > 0 ? liveBills.Max(b => b.FetchedOn) : null,
            singleBill?.Id);
    }

    private static BudgetDto ToDto(Budget b, IReadOnlyList<UtilityBill>? utilityBills = null)
    {
        utilityBills ??= [];
        var items = b.Items.OrderBy(i => i.DueDate ?? DateTime.MaxValue).ThenBy(i => i.Category).ToList();
        // Once a bill has landed the actual figure is the better forecast, so it supersedes the estimate.
        var expectedExpense = items.Sum(i =>
        {
            if (i.ActualAmount.HasValue) return i.ActualAmount.Value;
            var liveBills = MatchingBills(i.Category, utilityBills);
            return liveBills.Count > 0 ? liveBills.Sum(b => b.BillAmount) : i.EstimatedAmount;
        });
        var closing = b.OpeningBalance + b.ExpectedCollection - expectedExpense;
        // Deficit compares the month against itself: it is a warning that this month's income does not
        // cover this month's costs, even when a healthy opening balance hides that.
        var deficit = Math.Max(0m, expectedExpense - b.ExpectedCollection);

        return new BudgetDto(b.Id, b.Month, b.Year, b.OpeningBalance, b.ExpectedCollection,
            expectedExpense, closing, deficit, b.Status, b.Notes, items.Select(i => ToDto(i, utilityBills)).ToList());
    }

    private async Task<List<UtilityBill>> UtilityBillsFor(int year, int month) => await db.UtilityBills
        .AsNoTracking()
        .Include(b => b.UtilityConnection)!.ThenInclude(c => c!.Provider)
        .Where(b => b.BillingMonth.Year == year && b.BillingMonth.Month == month)
        .ToListAsync();

    [HttpGet]
    public async Task<ActionResult<BudgetDto>> Get([FromQuery] int year, [FromQuery] int month)
    {
        if (!User.CanView(PermissionModules.Budgets)) return Forbid();
        var budget = await db.Budgets.Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Year == year && b.Month == month);
        return budget is null ? NotFound() : Ok(ToDto(budget, await UtilityBillsFor(year, month)));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BudgetDto>> GetById(int id)
    {
        if (!User.CanView(PermissionModules.Budgets)) return Forbid();
        var budget = await db.Budgets.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id);
        return budget is null ? NotFound() : Ok(ToDto(budget, await UtilityBillsFor(budget.Year, budget.Month)));
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
        return Ok(ToDto(item, []));
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
        return Ok(ToDto(item, []));
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
                return new CategorySuggestionDto(g.Key, suggested, months.Count, months[^1].Total,
                    "Expense History", null, null);
            })
            .OrderByDescending(c => c.SuggestedAmount)
            .ToList();

        var utilityBills = await UtilityBillsFor(year, month);
        foreach (var group in utilityBills
                     .GroupBy(b => b.UtilityConnection!.Provider!.Category, StringComparer.OrdinalIgnoreCase))
        {
            var categoryName = group.Key;
            var total = group.Sum(b => b.BillAmount);
            var index = categories.FindIndex(c =>
                c.Category.Contains(categoryName, StringComparison.OrdinalIgnoreCase) ||
                categoryName.Contains(c.Category, StringComparison.OrdinalIgnoreCase));
            var live = new CategorySuggestionDto(
                index >= 0 ? categories[index].Category : categoryName,
                total,
                index >= 0 ? categories[index].MonthsOfHistory : 0,
                total,
                "Live Utility Bill",
                group.Max(b => b.FetchedOn),
                group.Count() == 1 ? group.First().Id : null);
            if (index >= 0) categories[index] = live;
            else categories.Add(live);
        }
        categories = categories.OrderByDescending(c => c.SuggestedAmount).ToList();

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

    /// <summary>Cash actually paid back to contributors in the given window. Settlements that booked
    /// their own expense are excluded - that cost is already in <c>Expenses</c> - as are wallet
    /// conversions, which move no money. Settlements of a deleted liability do not count.</summary>
    private async Task<decimal> RepaidCashAsync(
        System.Linq.Expressions.Expression<Func<SocietyLiabilitySettlement, bool>> window)
    {
        return await db.SocietyLiabilitySettlements
            .Where(s => s.Method == LiabilitySettlementMethod.Repaid
                     && s.ExpenseId == null
                     && db.SocietyLiabilities.Any(l => l.Id == s.LiabilityId))
            .Where(window)
            .SumAsync(s => (decimal?)s.Amount) ?? 0m;
    }

    /// <summary>The society's standing position: cash it holds, cash it is owed, cash it owes, and how
    /// long that cash would last at its recent burn rate.</summary>
    [HttpGet("health")]
    public async Task<ActionResult<BudgetHealthDto>> Health([FromQuery] int year, [FromQuery] int month)
    {
        if (!User.CanView(PermissionModules.Budgets)) return Forbid();

        var cutoff = Ordinal(year, month);
        var budget = await db.Budgets.FirstOrDefaultAsync(b => b.Year == year && b.Month == month);

        // An entered opening balance is a human asserting the real bank figure, so it beats anything
        // derived. Without one the ledger is all there is, and it under-reports by whatever was never
        // recorded - typically the society's founding corpus.
        decimal opening;
        if (budget is not null)
        {
            opening = budget.OpeningBalance;
        }
        else
        {
            var paidBefore = await db.Collections
                .Where(c => c.Year * 12 + c.Month < cutoff && c.Status == "Paid")
                .SumAsync(c => (decimal?)c.Amount) ?? 0m;
            var partialBefore = await db.Collections
                .Where(c => c.Year * 12 + c.Month < cutoff && c.Status == "Partial")
                .SumAsync(c => (decimal?)c.AmountPaid) ?? 0m;
            var incomeBefore = await db.SocietyIncomes
                .Where(i => i.Year * 12 + i.Month < cutoff)
                .SumAsync(i => (decimal?)i.Amount) ?? 0m;
            var spentBefore = await db.Expenses
                .Where(e => e.Year * 12 + e.Month < cutoff)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;
            var memberFundedBefore = await db.Expenses
                .Where(e => e.Year * 12 + e.Month < cutoff && e.FundedByLiabilityId != null)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;
            var repaidBefore = await RepaidCashAsync(s => s.Date.Year * 12 + s.Date.Month < cutoff);
            opening = paidBefore + partialBefore + incomeBefore - (spentBefore - memberFundedBefore) - repaidBefore;
        }

        var paidThis = await db.Collections
            .Where(c => c.Year == year && c.Month == month && c.Status == "Paid")
            .SumAsync(c => (decimal?)c.Amount) ?? 0m;
        var partialThis = await db.Collections
            .Where(c => c.Year == year && c.Month == month && c.Status == "Partial")
            .SumAsync(c => (decimal?)c.AmountPaid) ?? 0m;
        var incomeThis = await db.SocietyIncomes
            .Where(i => i.Year == year && i.Month == month)
            .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        var spentThis = await db.Expenses
            .Where(e => e.Year == year && e.Month == month)
            .SumAsync(e => (decimal?)e.Amount) ?? 0m;

        // A contributor-funded cost is booked in the month it is incurred, but no society money moved
        // then - the cash leaves when the contributor is repaid, often in a later month. Counting both
        // would charge the society twice, so the booked cost is added back and the repayment is what
        // actually reduces the bank. This mirrors the dashboard's cash-in-hand tile.
        var memberFundedThis = await db.Expenses
            .Where(e => e.Year == year && e.Month == month && e.FundedByLiabilityId != null)
            .SumAsync(e => (decimal?)e.Amount) ?? 0m;
        var repaidThis = await RepaidCashAsync(s => s.Date.Year == year && s.Date.Month == month);

        var bank = opening + paidThis + partialThis + incomeThis - (spentThis - memberFundedThis) - repaidThis;

        // Everything still owed up to and including this month. Invoices dated later are not yet due.
        var receivables = await db.Collections
            .Where(c => c.Year * 12 + c.Month <= cutoff && c.Status != "Paid")
            .SumAsync(c => (decimal?)(c.Amount - c.AmountPaid)) ?? 0m;

        // Planned lines that have not yet been booked as an expense: the bills still to be paid.
        var payables = budget is null ? 0m : await db.BudgetItems
            .Where(i => i.BudgetId == budget.Id && i.ExpenseId == null)
            .SumAsync(i => (decimal?)i.EstimatedAmount) ?? 0m;

        var monthlySpend = await db.Expenses
            .Where(e => e.Year * 12 + e.Month < cutoff && e.Year * 12 + e.Month >= cutoff - LookbackMonths)
            .GroupBy(e => new { e.Year, e.Month })
            .Select(g => g.Sum(x => x.Amount))
            .ToListAsync();
        var avgSpend = monthlySpend.Count == 0 ? 0m : monthlySpend.Sum() / monthlySpend.Count;

        decimal? cover = avgSpend <= 0m ? null : Math.Round(bank / avgSpend, 1);
        var band = cover switch
        {
            null => "unknown",
            <= 0m => "critical",
            < 1m => "critical",
            < 2m => "warning",
            < 3m => "healthy",
            _ => "excellent"
        };
        // A society cannot really hold less than nothing. A derived balance below zero means the ledger
        // is missing history - almost always the opening corpus - so claim ignorance rather than crisis.
        if (budget is null && bank <= 0m) band = "unknown";

        // Accuracy is only meaningful once bills have actually landed against a plan, so it is measured
        // over booked lines in months that have finished.
        var booked = await db.BudgetItems
            .Where(i => i.ActualAmount != null && i.Budget!.Year * 12 + i.Budget.Month < cutoff)
            .Select(i => new { i.Budget!.Year, i.Budget.Month, i.EstimatedAmount, Actual = i.ActualAmount!.Value })
            .ToListAsync();
        var accuracyMonths = booked.Select(b => b.Year * 12 + b.Month).Distinct().Count();
        var estimated = booked.Sum(b => b.EstimatedAmount);
        decimal? accuracy = accuracyMonths == 0 || estimated <= 0m
            ? null
            : Math.Max(0m, Math.Round(100m - booked.Sum(b => Math.Abs(b.Actual - b.EstimatedAmount)) / estimated * 100m, 0));

        return Ok(new BudgetHealthDto(
            Math.Round(bank, 2), budget is not null,
            Math.Round(receivables, 2), Math.Round(payables, 2),
            Math.Round(bank + receivables - payables, 2),
            Math.Round(avgSpend, 2), cover, band, accuracy, accuracyMonths));
    }
}

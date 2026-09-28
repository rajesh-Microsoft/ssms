using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

/// <summary>
/// Staff salary: setup, attendance, payments during the month and month-end settlement.
/// Access follows the Expenses module, since every rupee here is a society expense:
/// View reads, Edit records attendance and payments. Changing the salary itself, paying it and
/// reversing it are Admin only.
/// </summary>
[ApiController]
[Route("api/staff")]
[Authorize]
public class StaffController(SmmsDbContext db, StaffSalaryService salary, AuditService audit) : ControllerBase
{
    private const string Module = "Staff";

    public static readonly string[] PaymentCategories = ["Salary Advance", "Mobile Recharge", "Emergency Advance", "Other"];

    private const string LockedPayment =
        "This payment was already deducted from a paid salary. Reverse that salary first to change it.";

    private static string ClaimOwned(int claimId) =>
        $"This came from reimbursement claim #{claimId}, so it is managed there. To undo it, delete the claim's liability on the Liabilities screen.";

    private bool CanView => User.CanView(PermissionModules.Expenses);
    private bool CanEdit => User.CanEdit(PermissionModules.Expenses);
    private bool IsAdmin => User.IsInRole(Roles.Admin);
    private string? UserName => User.FindFirstValue(ClaimTypes.Name);

    private static StaffDto ToDto(Staff s) => new(
        s.Id, s.Name, s.Role, s.Mobile, s.JoiningDate, s.LeavingDate, s.MonthlySalary,
        s.PaidLeavePerMonth, s.PayDay, s.ExpenseCategory, s.IsActive, s.Notes);

    private static StaffPaymentDto ToDto(StaffPayment p) => new(
        p.Id, p.StaffId, p.Date, p.Amount, p.Category, p.Notes, p.PaymentMode, p.AdjustAgainstSalary,
        p.ExpenseId, p.SettlementId,
        p.Settlement is { } s ? StaffSalaryService.PeriodLabel(s.Year, s.Month) : null,
        p.CreatedBy, p.CreatedOn, p.ReimbursementId);

    private static string? ValidateStaff(StaffUpsertRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Name is required.";
        if (string.IsNullOrWhiteSpace(r.Role)) return "Role is required.";
        if (r.MonthlySalary < 0) return "Salary cannot be negative.";
        if (r.PaidLeavePerMonth is < 0 or > 31) return "Paid leave must be between 0 and 31 days.";
        if (r.PayDay is < 0 or > 28) return "Pay day must be 0 (last day of the month) or between 1 and 28.";
        if (r.JoiningDate == default) return "Joining date is required.";
        if (r.LeavingDate is { } l && l < r.JoiningDate) return "Leaving date cannot be before the joining date.";
        return null;
    }

    private static string? ValidatePayment(StaffPaymentUpsertRequest r)
    {
        if (r.Amount <= 0) return "Amount must be greater than zero.";
        if (r.Date == default) return "Date is required.";
        if (!PaymentCategories.Contains(r.Category)) return $"Pick a payment type: {string.Join(", ", PaymentCategories)}.";
        return null;
    }

    private static string? ValidatePeriod(int year, int month) =>
        month is < 1 or > 12 ? "Invalid month." : year is < 2000 or > 2100 ? "Invalid year." : null;

    // ── Staff setup ──────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StaffDto>>> GetAll([FromQuery] bool includeInactive = true)
    {
        if (!CanView) return Forbid();
        var query = db.StaffMembers.AsNoTracking();
        if (!includeInactive) query = query.Where(s => s.IsActive);
        var rows = await query.OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ToListAsync();
        return Ok(rows.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StaffDto>> GetById(int id)
    {
        if (!CanView) return Forbid();
        var s = await db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return s is null ? NotFound() : Ok(ToDto(s));
    }

    [HttpPost]
    public async Task<ActionResult<StaffDto>> Create(StaffUpsertRequest request)
    {
        if (!IsAdmin) return Forbid();
        if (ValidateStaff(request) is { } problem) return BadRequest(problem);

        var staff = new Staff();
        Apply(staff, request);
        db.StaffMembers.Add(staff);
        await db.SaveChangesAsync();
        await audit.LogAsync(Module, "Add",
            $"Added {staff.Role} {staff.Name}: salary {staff.MonthlySalary:0.##}, {staff.PaidLeavePerMonth} paid leave/month");
        return CreatedAtAction(nameof(GetById), new { id = staff.Id }, ToDto(staff));
    }

    /// <summary>Only future drafts pick up a change. Paid months keep the salary they were paid at.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, StaffUpsertRequest request)
    {
        if (!IsAdmin) return Forbid();
        if (ValidateStaff(request) is { } problem) return BadRequest(problem);
        var staff = await db.StaffMembers.FindAsync(id);
        if (staff is null) return NotFound();

        var before = $"salary {staff.MonthlySalary:0.##}, {staff.PaidLeavePerMonth} paid leave, pay day {staff.PayDay}, active {staff.IsActive}";
        Apply(staff, request);
        await db.SaveChangesAsync();
        var after = $"salary {staff.MonthlySalary:0.##}, {staff.PaidLeavePerMonth} paid leave, pay day {staff.PayDay}, active {staff.IsActive}";
        await audit.LogAsync(Module, "Update", $"Updated {staff.Role} {staff.Name} (#{id}): {before} → {after}");
        return NoContent();
    }

    private static void Apply(Staff staff, StaffUpsertRequest r)
    {
        staff.Name = r.Name.Trim();
        staff.Role = r.Role.Trim();
        staff.Mobile = string.IsNullOrWhiteSpace(r.Mobile) ? null : r.Mobile.Trim();
        staff.JoiningDate = r.JoiningDate;
        staff.LeavingDate = r.LeavingDate;
        staff.MonthlySalary = StaffSalaryService.Round(r.MonthlySalary);
        staff.PaidLeavePerMonth = r.PaidLeavePerMonth;
        staff.PayDay = r.PayDay;
        staff.ExpenseCategory = string.IsNullOrWhiteSpace(r.ExpenseCategory) ? "Security" : r.ExpenseCategory.Trim();
        staff.IsActive = r.IsActive;
        staff.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
    }

    // ── Attendance ───────────────────────────────────────────────────────────────────────

    [HttpGet("{id:int}/attendance")]
    public async Task<ActionResult<StaffAttendanceMonthDto>> Attendance(int id, [FromQuery] int year, [FromQuery] int month)
    {
        if (!CanView) return Forbid();
        if (ValidatePeriod(year, month) is { } problem) return BadRequest(problem);
        var staff = await db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (staff is null) return NotFound();

        var first = new DateOnly(year, month, 1);
        var next = first.AddMonths(1);
        var marks = await db.StaffAttendance.AsNoTracking()
            .Where(a => a.StaffId == id && a.Date >= first && a.Date < next)
            .OrderBy(a => a.Date)
            .ToListAsync();

        var range = StaffSalaryService.EmployedRange(staff.JoiningDate, staff.LeavingDate, year, month);
        var calc = StaffSalaryService.Calculate(staff.MonthlySalary, staff.PaidLeavePerMonth, year, month,
            staff.JoiningDate, staff.LeavingDate, marks.Select(m => (m.Date, m.Status)), 0m);

        return Ok(new StaffAttendanceMonthDto(
            year, month, calc.DaysInMonth, range?.From, range?.To,
            await salary.IsMonthPaidAsync(id, year, month),
            calc.PresentDays, calc.LeaveDays, calc.PaidLeaveDays, calc.UnpaidLeaveDays,
            marks.Select(m => new StaffAttendanceDayDto(m.Date, m.Status.ToString()))));
    }

    /// <summary>Marks one day. Present removes the mark, since present is the default.</summary>
    [HttpPut("{id:int}/attendance")]
    public async Task<IActionResult> SetAttendance(int id, StaffAttendanceSetRequest request)
    {
        if (!CanEdit) return Forbid();
        var staff = await db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (staff is null) return NotFound();

        StaffAttendanceStatus? status;
        switch (request.Status?.Trim())
        {
            case "Present": status = null; break;
            case "Leave": status = StaffAttendanceStatus.Leave; break;
            case "Unpaid": status = StaffAttendanceStatus.Unpaid; break;
            default: return BadRequest("Status must be Present, Leave or Unpaid.");
        }

        var date = request.Date;
        if (StaffSalaryService.EmployedRange(staff.JoiningDate, staff.LeavingDate, date.Year, date.Month) is not { } range
            || date < range.From || date > range.To)
            return BadRequest($"{staff.Name} was not employed on {date:dd-MMM-yyyy}.");
        if (date > StaffSalaryService.Today)
            return BadRequest("Attendance cannot be marked for a future date.");
        if (await salary.IsMonthPaidAsync(id, date.Year, date.Month))
            return BadRequest($"{StaffSalaryService.PeriodLabel(date.Year, date.Month)} salary is already paid. Reverse it first to correct attendance.");

        var existing = await db.StaffAttendance.FirstOrDefaultAsync(a => a.StaffId == id && a.Date == date);
        var was = existing?.Status.ToString() ?? "Present";
        var now = status?.ToString() ?? "Present";
        if (was == now) return NoContent();

        if (status is null) db.StaffAttendance.Remove(existing!);
        else if (existing is null) db.StaffAttendance.Add(new StaffAttendance { StaffId = id, Date = date, Status = status.Value });
        else existing.Status = status.Value;

        await db.SaveChangesAsync();
        await audit.LogAsync(Module, "Attendance", $"{staff.Name} {date:dd-MMM-yyyy}: {was} → {now}");
        return NoContent();
    }

    // ── Payments during the month ────────────────────────────────────────────────────────

    [HttpGet("{id:int}/payments")]
    public async Task<ActionResult<IEnumerable<StaffPaymentDto>>> Payments(int id, [FromQuery] int? year, [FromQuery] int? month)
    {
        if (!CanView) return Forbid();
        if (!await db.StaffMembers.AnyAsync(s => s.Id == id)) return NotFound();

        var query = db.StaffPayments.AsNoTracking().Include(p => p.Settlement).Where(p => p.StaffId == id);
        if (year is int y && month is int m)
        {
            if (ValidatePeriod(y, m) is { } problem) return BadRequest(problem);
            var from = new DateTime(y, m, 1);
            var to = from.AddMonths(1);
            query = query.Where(p => p.Date >= from && p.Date < to);
        }
        else if (year is int yy)
        {
            query = query.Where(p => p.Date.Year == yy);
        }

        var rows = await query.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToListAsync();
        return Ok(rows.Select(ToDto));
    }

    /// <summary>Books the cash as a society expense straight away, so the expense list and cash in
    /// hand are right on the day the money leaves, whether or not it is later deducted.</summary>
    [HttpPost("{id:int}/payments")]
    public async Task<ActionResult<StaffPaymentDto>> AddPayment(int id, StaffPaymentUpsertRequest request)
    {
        if (!CanEdit) return Forbid();
        if (ValidatePayment(request) is { } problem) return BadRequest(problem);
        var staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id);
        if (staff is null) return NotFound();

        var payment = new StaffPayment { StaffId = id, Expense = new Expense() };
        ApplyPayment(payment, staff, request);
        db.StaffPayments.Add(payment);
        await db.SaveChangesAsync();
        await audit.LogAsync(Module, "PaymentAdd",
            $"{request.Category} {payment.Amount:0.##} to {staff.Name} on {payment.Date:dd-MMM-yyyy}" +
            (payment.AdjustAgainstSalary ? ", to be deducted from salary" : ", not deducted from salary") +
            $" (expense #{payment.ExpenseId})");
        return CreatedAtAction(nameof(Payments), new { id }, ToDto(payment));
    }

    [HttpPut("payments/{paymentId:int}")]
    public async Task<IActionResult> UpdatePayment(int paymentId, StaffPaymentUpsertRequest request)
    {
        if (!CanEdit) return Forbid();
        if (ValidatePayment(request) is { } problem) return BadRequest(problem);
        var payment = await db.StaffPayments.Include(p => p.Staff).Include(p => p.Expense)
            .FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment is null) return NotFound();
        if (payment.SettlementId is not null) return BadRequest(LockedPayment);
        if (payment.ReimbursementId is int claimId) return BadRequest(ClaimOwned(claimId));

        var before = $"{payment.Category} {payment.Amount:0.##} on {payment.Date:dd-MMM-yyyy}, deduct {payment.AdjustAgainstSalary}";
        payment.Expense ??= new Expense();
        ApplyPayment(payment, payment.Staff!, request);
        await db.SaveChangesAsync();
        await audit.LogAsync(Module, "PaymentUpdate",
            $"Payment #{paymentId} to {payment.Staff!.Name}: {before} → {payment.Category} {payment.Amount:0.##} on {payment.Date:dd-MMM-yyyy}, deduct {payment.AdjustAgainstSalary}");
        return NoContent();
    }

    [HttpDelete("payments/{paymentId:int}")]
    public async Task<IActionResult> DeletePayment(int paymentId)
    {
        if (!CanEdit) return Forbid();
        var payment = await db.StaffPayments.Include(p => p.Staff).Include(p => p.Expense)
            .FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment is null) return NotFound();
        if (payment.SettlementId is not null) return BadRequest(LockedPayment);
        if (payment.ReimbursementId is int claimId) return BadRequest(ClaimOwned(claimId));

        // Both soft-deleted together, so the expense list never shows cash with no reason behind it.
        payment.IsDeleted = true;
        if (payment.Expense is not null) payment.Expense.IsDeleted = true;
        await db.SaveChangesAsync();
        await audit.LogAsync(Module, "PaymentDelete",
            $"Deleted {payment.Category} {payment.Amount:0.##} to {payment.Staff!.Name} (payment #{paymentId}, expense #{payment.ExpenseId})");
        return NoContent();
    }

    private static void ApplyPayment(StaffPayment payment, Staff staff, StaffPaymentUpsertRequest r)
    {
        payment.Date = r.Date.Date;
        payment.Amount = StaffSalaryService.Round(r.Amount);
        payment.Category = r.Category;
        payment.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        payment.PaymentMode = string.IsNullOrWhiteSpace(r.PaymentMode) ? "Cash" : r.PaymentMode.Trim();
        payment.AdjustAgainstSalary = r.AdjustAgainstSalary;

        var e = payment.Expense!;
        e.ExpenseDate = payment.Date;
        e.Category = staff.ExpenseCategory;
        e.Description = $"{payment.Category} — {staff.Name} ({staff.Role})";
        e.Vendor = staff.Name;
        e.Amount = payment.Amount;
        e.PaymentMode = payment.PaymentMode;
        e.Month = payment.Date.Month;
        e.Year = payment.Date.Year;
        e.Remarks = (payment.AdjustAgainstSalary ? "Deducted from salary" : "Not deducted from salary")
                    + (payment.Notes is { } n ? $" · {n}" : "");
    }

    // ── Salary ───────────────────────────────────────────────────────────────────────────

    /// <summary>The paid snapshot if the month is paid, otherwise the live draft.</summary>
    [HttpGet("{id:int}/salary")]
    public async Task<ActionResult<SalaryBreakdownDto>> Salary(int id, [FromQuery] int year, [FromQuery] int month)
    {
        if (!CanView) return Forbid();
        if (ValidatePeriod(year, month) is { } problem) return BadRequest(problem);
        var staff = await db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (staff is null) return NotFound();
        return Ok(await salary.GetBreakdownAsync(staff, year, month));
    }

    [HttpGet("salary/{settlementId:int}")]
    public async Task<ActionResult<SalaryBreakdownDto>> Settlement(int settlementId)
    {
        if (!CanView) return Forbid();
        var s = await db.SalarySettlements.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == settlementId);
        return s is null ? NotFound() : Ok(StaffSalaryService.FromSettlement(s));
    }

    [HttpGet("{id:int}/salary/history")]
    public async Task<ActionResult<IEnumerable<SalaryHistoryRowDto>>> History(int id, [FromQuery] int? year)
    {
        if (!CanView) return Forbid();
        if (!await db.StaffMembers.AnyAsync(s => s.Id == id)) return NotFound();

        var query = db.SalarySettlements.AsNoTracking().Where(s => s.StaffId == id);
        if (year is int y) query = query.Where(s => s.Year == y);
        var rows = await query.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).ThenByDescending(s => s.Id).ToListAsync();

        return Ok(rows.Select(s => new SalaryHistoryRowDto(
            s.Id, s.Year, s.Month, s.Status.ToString(), s.GrossSalary, s.PresentDays, s.PaidLeaveDays,
            s.UnpaidLeaveDays, s.LeaveDeduction, s.AdjustmentsApplied, s.NetPaid, s.PaidOn, s.PaymentMode,
            s.ReversedOn, s.ReversalReason)));
    }

    [HttpPost("{id:int}/salary/pay")]
    public async Task<ActionResult<SalaryBreakdownDto>> Pay(int id, SalaryPayRequest request)
    {
        if (!IsAdmin) return Forbid();
        if (ValidatePeriod(request.Year, request.Month) is { } problem) return BadRequest(problem);
        var staff = await db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (staff is null) return NotFound();

        var result = await salary.PayAsync(staff, request);
        if (result.Settlement is not { } s)
            return result.IsConflict ? Conflict(result.Error) : BadRequest(result.Error);

        await audit.LogAsync(Module, "SalaryPaid",
            $"Paid {staff.Name} {s.NetPaid:0.##} for {StaffSalaryService.PeriodLabel(s.Year, s.Month)}: " +
            $"gross {s.GrossSalary:0.##}, {s.UnpaidLeaveDays} unpaid day(s) −{s.LeaveDeduction:0.##}, " +
            $"adjustments −{s.AdjustmentsApplied:0.##}" +
            (s.CarryForward > 0 ? $", {s.CarryForward:0.##} carried forward" : "") +
            (s.ExpenseId is int eid ? $" (expense #{eid})" : " (no cash paid)"));
        return Ok(StaffSalaryService.FromSettlement(s));
    }

    [HttpPost("salary/{settlementId:int}/reverse")]
    public async Task<ActionResult<SalaryBreakdownDto>> Reverse(int settlementId, SalaryReverseRequest request)
    {
        if (!IsAdmin) return Forbid();
        var s = await db.SalarySettlements.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == settlementId);
        if (s is null) return NotFound();

        var result = await salary.ReverseAsync(s, request.Reason, UserName);
        if (result.Settlement is null) return BadRequest(result.Error);

        await audit.LogAsync(Module, "SalaryReversed",
            $"Reversed {s.StaffName}'s {StaffSalaryService.PeriodLabel(s.Year, s.Month)} salary of {s.NetPaid:0.##}: {s.ReversalReason}");
        return Ok(StaffSalaryService.FromSettlement(s));
    }

    // ── Dashboard ────────────────────────────────────────────────────────────────────────

    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<StaffSalarySummaryDto>>> Summary()
    {
        if (!CanView) return Forbid();
        var staff = await db.StaffMembers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        var rows = new List<StaffSalarySummaryDto>();
        foreach (var s in staff)
            if (await salary.SummaryAsync(s) is { } row) rows.Add(row);
        return Ok(rows);
    }
}

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;

namespace SMMS.Api.Services;

/// <summary>The month's numbers, before anything is saved.</summary>
public sealed record SalaryCalculation(
    int DaysInMonth,
    int EmployedDays,
    int PresentDays,
    int LeaveDays,
    int PaidLeaveDays,
    int UnpaidLeaveDays,
    decimal DailyRate,
    decimal GrossSalary,
    decimal LeaveDeduction,
    decimal AdjustmentsTotal,
    decimal AdjustmentsApplied,
    decimal CarryForward,
    decimal NetPayable);

public sealed record SalaryOpResult(SalarySettlement? Settlement, string? Error, bool IsConflict = false)
{
    public static SalaryOpResult Fail(string error) => new(null, error);
    public static SalaryOpResult Conflict(string error) => new(null, error, true);
}

/// <summary>
/// Monthly salary for a fixed-salary staff member:
/// <c>gross − unpaid-leave deduction − salary adjustments = net payable</c>.
/// The draft is computed live from attendance and advances; paying it freezes a snapshot in
/// <see cref="SalarySettlement"/>. Salaries are settled in month order so an advance larger than
/// one month's salary can carry forward to the next without ambiguity.
/// </summary>
public class StaffSalaryService(SmmsDbContext db)
{
    /// <summary>Rupees to paise, half away from zero, applied only to final amounts so that a
    /// rounded daily rate is never multiplied back up into a larger error.</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static string PeriodLabel(int year, int month) =>
        $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)} {year}";

    private static int PeriodKey(int year, int month) => year * 12 + month;

    /// <summary>The days of the month the person was on the rolls, or null if none.</summary>
    public static (DateOnly From, DateOnly To)? EmployedRange(DateOnly joining, DateOnly? leaving, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var from = joining > first ? joining : first;
        var to = leaving is { } l && l < last ? l : last;
        return to < from ? null : (from, to);
    }

    /// <summary>
    /// Calendar-day basis. Leave marked <see cref="StaffAttendanceStatus.Leave"/> is paid up to the
    /// allowance and only the excess is deducted; <see cref="StaffAttendanceStatus.Unpaid"/> days are
    /// always deducted. Adjustments never take the net below zero — the rest is carried forward.
    /// </summary>
    public static SalaryCalculation Calculate(
        decimal monthlySalary, int paidLeaveAllowed, int year, int month,
        DateOnly joining, DateOnly? leaving,
        IEnumerable<(DateOnly Date, StaffAttendanceStatus Status)> nonWorkingDays,
        decimal adjustmentsTotal)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var range = EmployedRange(joining, leaving, year, month);
        var employed = range is { } r ? r.To.DayNumber - r.From.DayNumber + 1 : 0;

        var marks = range is { } rr
            ? nonWorkingDays.Where(d => d.Date >= rr.From && d.Date <= rr.To).DistinctBy(d => d.Date).ToList()
            : [];
        var leave = marks.Count(d => d.Status == StaffAttendanceStatus.Leave);
        var explicitUnpaid = marks.Count(d => d.Status == StaffAttendanceStatus.Unpaid);
        var paidLeave = Math.Min(leave, Math.Max(0, paidLeaveAllowed));
        var unpaid = leave - paidLeave + explicitUnpaid;

        var gross = employed == daysInMonth ? monthlySalary : Round(monthlySalary * employed / daysInMonth);
        var deduction = Math.Min(gross, Round(monthlySalary * unpaid / daysInMonth));
        var earned = gross - deduction;

        var adjustments = Math.Max(0m, adjustmentsTotal);
        var applied = Math.Min(adjustments, earned);

        return new SalaryCalculation(
            daysInMonth, employed, employed - leave - explicitUnpaid, leave, paidLeave, unpaid,
            Round(monthlySalary / daysInMonth), gross, deduction,
            adjustments, applied, adjustments - applied, earned - applied);
    }

    /// <summary>PayDay 0 means the last day of the salary month; otherwise that day of the next month.</summary>
    public static DateOnly DueDate(int payDay, int year, int month)
    {
        if (payDay <= 0) return new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var next = new DateOnly(year, month, 1).AddMonths(1);
        return new DateOnly(next.Year, next.Month, Math.Min(payDay, DateTime.DaysInMonth(next.Year, next.Month)));
    }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ── Reads ────────────────────────────────────────────────────────────────────────────

    public Task<SalarySettlement?> FindPaidAsync(int staffId, int year, int month) =>
        db.SalarySettlements.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.StaffId == staffId && s.Year == year && s.Month == month
                                      && s.Status == SalarySettlementStatus.Paid);

    public Task<bool> IsMonthPaidAsync(int staffId, int year, int month) =>
        db.SalarySettlements.AnyAsync(s => s.StaffId == staffId && s.Year == year && s.Month == month
                                           && s.Status == SalarySettlementStatus.Paid);

    private sealed record Draft(
        SalaryCalculation Calc,
        List<StaffPayment> Payments,
        decimal CarryIn,
        DateTime CarryInDate,
        string? BlockedReason);

    private async Task<Draft> BuildDraftStateAsync(Staff staff, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var nextStart = first.AddMonths(1);
        var nextStartDt = nextStart.ToDateTime(TimeOnly.MinValue);
        var key = PeriodKey(year, month);

        var marks = await db.StaffAttendance.AsNoTracking()
            .Where(a => a.StaffId == staff.Id && a.Date >= first && a.Date < nextStart)
            .Select(a => new { a.Date, a.Status })
            .ToListAsync();

        // Every adjustable payment not yet deducted, up to the end of this month. An advance from
        // a month that was never settled is still owed, so it is picked up here.
        var payments = await db.StaffPayments
            .Where(p => p.StaffId == staff.Id && p.AdjustAgainstSalary && p.SettlementId == null && p.Date < nextStartDt)
            .OrderBy(p => p.Date).ThenBy(p => p.Id)
            .ToListAsync();

        var previous = await db.SalarySettlements.AsNoTracking()
            .Where(s => s.StaffId == staff.Id && s.Status == SalarySettlementStatus.Paid
                        && s.Year * 12 + s.Month < key)
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .FirstOrDefaultAsync();
        var carryIn = previous?.CarryForward ?? 0m;
        var carryInDate = previous is null
            ? first.ToDateTime(TimeOnly.MinValue)
            : new DateTime(previous.Year, previous.Month, DateTime.DaysInMonth(previous.Year, previous.Month));

        var calc = Calculate(staff.MonthlySalary, staff.PaidLeavePerMonth, year, month,
            staff.JoiningDate, staff.LeavingDate,
            marks.Select(m => (m.Date, m.Status)),
            payments.Sum(p => p.Amount) + carryIn);

        string? blocked = null;
        if (calc.EmployedDays == 0)
            blocked = $"{staff.Name} was not employed in {PeriodLabel(year, month)}.";
        else if (first > Today)
            blocked = $"{PeriodLabel(year, month)} has not started yet.";
        else
        {
            var later = await db.SalarySettlements.AsNoTracking()
                .Where(s => s.StaffId == staff.Id && s.Status == SalarySettlementStatus.Paid
                            && s.Year * 12 + s.Month > key)
                .OrderBy(s => s.Year).ThenBy(s => s.Month)
                .FirstOrDefaultAsync();
            if (later is not null)
                blocked = $"{PeriodLabel(later.Year, later.Month)} is already paid. Salaries are settled in month order — " +
                          $"reverse {PeriodLabel(later.Year, later.Month)} first to pay {PeriodLabel(year, month)}.";
        }

        return new Draft(calc, payments, carryIn, carryInDate, blocked);
    }

    public async Task<SalaryBreakdownDto> GetBreakdownAsync(Staff staff, int year, int month)
    {
        var paid = await FindPaidAsync(staff.Id, year, month);
        if (paid is not null) return FromSettlement(paid);

        var d = await BuildDraftStateAsync(staff, year, month);
        var lines = d.Payments
            .Select(p => new SalaryAdjustmentLineDto(p.Id, p.Date, p.Category, p.Notes, p.Amount))
            .ToList();
        if (d.CarryIn > 0)
            lines.Insert(0, new SalaryAdjustmentLineDto(null, d.CarryInDate, "Carried forward", "Not covered by last month's salary", d.CarryIn));

        var c = d.Calc;
        return new SalaryBreakdownDto(
            staff.Id, staff.Name, staff.Role, year, month, "Draft", null,
            staff.MonthlySalary, c.DaysInMonth, c.EmployedDays, c.PresentDays, staff.PaidLeavePerMonth,
            c.LeaveDays, c.PaidLeaveDays, c.UnpaidLeaveDays, c.DailyRate, c.GrossSalary, c.LeaveDeduction,
            lines, d.CarryIn, c.AdjustmentsTotal, c.AdjustmentsApplied, c.CarryForward, c.NetPayable,
            null, null, null, null, null, null, null, null,
            d.BlockedReason is null, d.BlockedReason);
    }

    public static SalaryBreakdownDto FromSettlement(SalarySettlement s)
    {
        var lines = s.Lines.OrderBy(l => l.Date).ThenBy(l => l.Id)
            .Select(l => new SalaryAdjustmentLineDto(l.StaffPaymentId, l.Date, l.Category, l.Notes, l.Amount))
            .ToList();
        if (s.CarryIn > 0)
            lines.Insert(0, new SalaryAdjustmentLineDto(null, new DateTime(s.Year, s.Month, 1), "Carried forward",
                "Not covered by last month's salary", s.CarryIn));

        return new SalaryBreakdownDto(
            s.StaffId, s.StaffName, s.Role, s.Year, s.Month, s.Status.ToString(), s.Id,
            s.MonthlySalary, s.DaysInMonth, s.EmployedDays, s.PresentDays, s.PaidLeaveAllowed,
            s.LeaveDays, s.PaidLeaveDays, s.UnpaidLeaveDays, s.DailyRate, s.GrossSalary, s.LeaveDeduction,
            lines, s.CarryIn, s.AdjustmentsTotal, s.AdjustmentsApplied, s.CarryForward, s.NetPaid,
            s.PaidOn, s.PaymentMode, s.CreatedBy, s.ExpenseId, s.Notes,
            s.ReversedOn, s.ReversedBy, s.ReversalReason,
            false, s.Status == SalarySettlementStatus.Paid ? "This month is already paid." : "This settlement was reversed.");
    }

    // ── Writes ───────────────────────────────────────────────────────────────────────────

    public const string ReimbursementCategory = "Member Reimbursement";

    /// <summary>
    /// Why this claim cannot be deducted from <paramref name="staff"/>'s salary, or null if it can.
    /// A staff member who has left and whose last month is paid has no salary left to take it from.
    /// </summary>
    public async Task<string?> CannotDeductReimbursementAsync(Staff staff)
    {
        if (staff.IsActive) return null;
        if (staff.LeavingDate is not { } left)
            return $"{staff.Name} is inactive, so there is no salary left to deduct this from.";
        if (await IsMonthPaidAsync(staff.Id, left.Year, left.Month))
            return $"{staff.Name}'s final salary ({PeriodLabel(left.Year, left.Month)}) is already paid, so there is no salary left to deduct this from.";
        return null;
    }

    /// <summary>
    /// Queues a salary adjustment for an approved claim. The member's cash is already the claim's
    /// expense (<paramref name="claimExpense"/>), so this books nothing new: it only tells the
    /// month-end settlement that the staff member has already received this money. Dated when the
    /// member actually paid, so it lands in the first unpaid salary on or after that day. Does not
    /// call SaveChanges — the approval commits it together with the liability.
    /// </summary>
    public StaffPayment QueueReimbursementDeduction(Staff staff, ReimbursementRequest claim, Member member, Expense claimExpense)
    {
        var notes = $"Paid by {member.Name} (flat {member.Flat}), claim #{claim.Id}: {claim.Description}";
        var payment = new StaffPayment
        {
            StaffId = staff.Id,
            Date = claim.ExpenseDate.Date,
            Amount = Round(claim.Amount),
            Category = ReimbursementCategory,
            Notes = notes.Length > 300 ? notes[..300] : notes,
            PaymentMode = string.IsNullOrWhiteSpace(claim.PaymentMode) ? "Paid by member" : claim.PaymentMode,
            AdjustAgainstSalary = true,
            Expense = claimExpense,
            Reimbursement = claim
        };
        db.StaffPayments.Add(payment);
        return payment;
    }
    public async Task<SalaryOpResult> PayAsync(Staff staff, SalaryPayRequest request)
    {
        if (request.Month is < 1 or > 12) return SalaryOpResult.Fail("Invalid month.");
        var period = PeriodLabel(request.Year, request.Month);

        if (await IsMonthPaidAsync(staff.Id, request.Year, request.Month))
            return SalaryOpResult.Conflict($"{staff.Name}'s salary for {period} is already paid.");

        var d = await BuildDraftStateAsync(staff, request.Year, request.Month);
        if (d.BlockedReason is not null) return SalaryOpResult.Fail(d.BlockedReason);

        var c = d.Calc;
        // The admin confirmed a specific figure. If an advance or attendance changed since, paying
        // a different amount without showing it would defeat the confirmation.
        if (c.NetPayable != Round(request.ExpectedNetPayable))
            return SalaryOpResult.Conflict(
                $"The amount changed from ₹{request.ExpectedNetPayable:0.##} to ₹{c.NetPayable:0.##} since the breakdown was shown. Review it and pay again.");

        var paidOn = request.PaidOn ?? DateTime.UtcNow;
        var paymentMode = string.IsNullOrWhiteSpace(request.PaymentMode) ? "Cash" : request.PaymentMode.Trim();

        var settlement = new SalarySettlement
        {
            StaffId = staff.Id,
            Year = request.Year,
            Month = request.Month,
            Status = SalarySettlementStatus.Paid,
            StaffName = staff.Name,
            Role = staff.Role,
            MonthlySalary = staff.MonthlySalary,
            DaysInMonth = c.DaysInMonth,
            EmployedDays = c.EmployedDays,
            PresentDays = c.PresentDays,
            PaidLeaveAllowed = staff.PaidLeavePerMonth,
            LeaveDays = c.LeaveDays,
            PaidLeaveDays = c.PaidLeaveDays,
            UnpaidLeaveDays = c.UnpaidLeaveDays,
            DailyRate = c.DailyRate,
            GrossSalary = c.GrossSalary,
            LeaveDeduction = c.LeaveDeduction,
            CarryIn = d.CarryIn,
            AdjustmentsTotal = c.AdjustmentsTotal,
            AdjustmentsApplied = c.AdjustmentsApplied,
            CarryForward = c.CarryForward,
            NetPaid = c.NetPayable,
            PaidOn = paidOn,
            PaymentMode = paymentMode,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        foreach (var p in d.Payments)
        {
            settlement.Lines.Add(new SalarySettlementLine
            {
                StaffPaymentId = p.Id,
                Date = p.Date,
                Category = p.Category,
                Notes = p.Notes,
                Amount = p.Amount
            });
            p.Settlement = settlement;
        }

        // Advances were booked as expenses when they were handed over, so only the cash paid now
        // is a new expense. When advances cover the whole salary there is nothing to book.
        if (c.NetPayable > 0)
        {
            settlement.Expense = new Expense
            {
                ExpenseDate = paidOn,
                Category = staff.ExpenseCategory,
                Description = $"{staff.Role} salary — {period} ({staff.Name})",
                Vendor = staff.Name,
                Amount = c.NetPayable,
                PaymentMode = paymentMode,
                Month = request.Month,
                Year = request.Year,
                Remarks = $"Gross ₹{c.GrossSalary:0.##} − leave ₹{c.LeaveDeduction:0.##} − adjustments ₹{c.AdjustmentsApplied:0.##}"
            };
        }

        db.SalarySettlements.Add(settlement);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The filtered unique index: someone else paid this month a moment ago.
            db.ChangeTracker.Clear();
            return SalaryOpResult.Conflict($"{staff.Name}'s salary for {period} is already paid.");
        }
        return new SalaryOpResult(settlement, null);
    }

    /// <summary>Undoes a paid month without deleting it: the snapshot stays for the record, the
    /// salary expense is withdrawn, and the deducted advances are released for the corrected payment.</summary>
    public async Task<SalaryOpResult> ReverseAsync(SalarySettlement settlement, string? reason, string? user)
    {
        if (settlement.Status != SalarySettlementStatus.Paid)
            return SalaryOpResult.Fail("Only a paid salary can be reversed.");
        if (string.IsNullOrWhiteSpace(reason))
            return SalaryOpResult.Fail("A reason is required to reverse a paid salary.");

        var key = PeriodKey(settlement.Year, settlement.Month);
        var later = await db.SalarySettlements.AsNoTracking()
            .Where(s => s.StaffId == settlement.StaffId && s.Status == SalarySettlementStatus.Paid
                        && s.Year * 12 + s.Month > key)
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .FirstOrDefaultAsync();
        if (later is not null)
            return SalaryOpResult.Fail(
                $"{PeriodLabel(later.Year, later.Month)} is already paid. Reverse the latest month first.");

        settlement.Status = SalarySettlementStatus.Reversed;
        settlement.ReversedOn = DateTime.UtcNow;
        settlement.ReversedBy = user;
        settlement.ReversalReason = reason.Trim();

        if (settlement.ExpenseId is int eid)
        {
            // Soft-deleted, not removed: the settlement keeps pointing at the cash record it booked.
            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == eid);
            if (expense is not null) expense.IsDeleted = true;
        }

        var released = await db.StaffPayments.Where(p => p.SettlementId == settlement.Id).ToListAsync();
        foreach (var p in released) p.SettlementId = null;

        await db.SaveChangesAsync();
        return new SalaryOpResult(settlement, null);
    }

    // ── Dashboard ────────────────────────────────────────────────────────────────────────

    /// <summary>Last month while it is still unpaid, otherwise this month.</summary>
    public async Task<StaffSalarySummaryDto?> SummaryAsync(Staff staff)
    {
        var today = Today;
        var current = new DateOnly(today.Year, today.Month, 1);
        var previous = current.AddMonths(-1);

        async Task<StaffSalarySummaryDto?> For(DateOnly period)
        {
            var paid = await FindPaidAsync(staff.Id, period.Year, period.Month);
            var due = DueDate(staff.PayDay, period.Year, period.Month);
            if (paid is not null)
                return new StaffSalarySummaryDto(staff.Id, staff.Name, staff.Role, period.Year, period.Month,
                    "Paid", paid.NetPaid, due, false, paid.PaidOn);

            var b = await GetBreakdownAsync(staff, period.Year, period.Month);
            if (b.EmployedDays == 0 || !b.CanPay) return null;
            return new StaffSalarySummaryDto(staff.Id, staff.Name, staff.Role, period.Year, period.Month,
                "Due", b.NetPayable, due, today > due, null);
        }

        if (EmployedRange(staff.JoiningDate, staff.LeavingDate, previous.Year, previous.Month) is not null
            && !await IsMonthPaidAsync(staff.Id, previous.Year, previous.Month))
        {
            var prev = await For(previous);
            if (prev is not null) return prev;
        }
        return await For(current);
    }
}

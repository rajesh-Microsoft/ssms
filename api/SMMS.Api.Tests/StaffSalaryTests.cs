using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Controllers;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;
using Xunit;

namespace SMMS.Api.Tests;

/// <summary>
/// Watchman salary: the month-end arithmetic on its own, then the whole workflow through the
/// controller — setup, attendance, advances, pay, duplicate protection, reversal and history.
/// Months used are in the past so "not started yet" never interferes.
/// </summary>
public class StaffSalaryTests : IDisposable
{
    private readonly List<SqliteConnection> _connections = [];

    private SmmsDbContext NewSociety()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        _connections.Add(connection);
        var options = new DbContextOptionsBuilder<SmmsDbContext>().UseSqlite(connection).Options;
        var db = new SmmsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    public void Dispose()
    {
        foreach (var c in _connections) c.Dispose();
        GC.SuppressFinalize(this);
    }

    private static StaffController ControllerFor(SmmsDbContext db, ClaimsPrincipal user)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } };
        return new StaffController(db, new StaffSalaryService(db), new AuditService(db, accessor))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private static ClaimsPrincipal WithExpenses(string level) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "treasurer"),
            new Claim(ClaimTypes.Role, Roles.Member),
            new Claim($"perm:{PermissionModules.Expenses}", level)], "test"));

    private static ClaimsPrincipal Caretaker() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "gate"), new Claim(ClaimTypes.Role, Roles.Caretaker)], "test"));

    private static StaffUpsertRequest Watchman(decimal salary = 10000, int leave = 2, DateOnly? joined = null) =>
        new("Ramesh", "Watchman", "9876543210", joined ?? new DateOnly(2025, 1, 1), null, salary, leave, 0, "Security", true, null);

    private static async Task<int> AddWatchmanAsync(StaffController c, decimal salary = 10000, int leave = 2, DateOnly? joined = null)
    {
        var created = await c.Create(Watchman(salary, leave, joined));
        return ((StaffDto)((CreatedAtActionResult)created.Result!).Value!).Id;
    }

    private static async Task<int> AddPaymentAsync(StaffController c, int staffId, DateTime date, decimal amount,
        string category = "Salary Advance", bool deduct = true)
    {
        var r = await c.AddPayment(staffId, new StaffPaymentUpsertRequest(date, amount, category, null, "Cash", deduct));
        return ((StaffPaymentDto)((CreatedAtActionResult)r.Result!).Value!).Id;
    }

    private static async Task MarkAsync(StaffController c, int staffId, string status, params DateOnly[] days)
    {
        foreach (var d in days)
            Assert.IsType<NoContentResult>(await c.SetAttendance(staffId, new StaffAttendanceSetRequest(d, status)));
    }

    private static async Task<SalaryBreakdownDto> BreakdownAsync(StaffController c, int staffId, int year, int month) =>
        (SalaryBreakdownDto)((OkObjectResult)(await c.Salary(staffId, year, month)).Result!).Value!;

    private static DateOnly Sep(int day) => new(2025, 9, day);

    // ── Calculation ──────────────────────────────────────────────────────────────────────

    private static SalaryCalculation Calc(decimal salary, int allowance, int year, int month,
        IEnumerable<(DateOnly, StaffAttendanceStatus)>? marks = null, decimal adjustments = 0,
        DateOnly? joined = null, DateOnly? left = null) =>
        StaffSalaryService.Calculate(salary, allowance, year, month, joined ?? new DateOnly(2020, 1, 1), left,
            marks ?? [], adjustments);

    private static IEnumerable<(DateOnly, StaffAttendanceStatus)> Leave(int year, int month, int days, int startDay = 1) =>
        Enumerable.Range(startDay, days).Select(d => (new DateOnly(year, month, d), StaffAttendanceStatus.Leave));

    [Fact]
    public void NoLeave_NoAdvance_PaysFullSalary()
    {
        var c = Calc(10000, 2, 2025, 9);
        Assert.Equal(30, c.PresentDays);
        Assert.Equal(0m, c.LeaveDeduction);
        Assert.Equal(10000m, c.NetPayable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LeaveWithinAllowance_IsNotDeducted(int days)
    {
        var c = Calc(10000, 2, 2025, 9, Leave(2025, 9, days));
        Assert.Equal(days, c.PaidLeaveDays);
        Assert.Equal(0, c.UnpaidLeaveDays);
        Assert.Equal(0m, c.LeaveDeduction);
        Assert.Equal(10000m, c.NetPayable);
    }

    /// <summary>The requirement's own example: ₹10,000, 5 days off with 2 allowed, ₹1,300 advanced.</summary>
    [Fact]
    public void ExcessLeave_DeductsOnlyTheExcess()
    {
        var c = Calc(10000, 2, 2025, 9, Leave(2025, 9, 5), adjustments: 1300);
        Assert.Equal(5, c.LeaveDays);
        Assert.Equal(2, c.PaidLeaveDays);
        Assert.Equal(3, c.UnpaidLeaveDays);
        Assert.Equal(333.33m, c.DailyRate);
        Assert.Equal(1000m, c.LeaveDeduction);
        Assert.Equal(7700m, c.NetPayable);
    }

    [Fact]
    public void UnpaidDays_AreDeductedEvenWithAllowanceLeft()
    {
        var c = Calc(10000, 2, 2025, 9, [(Sep(4), StaffAttendanceStatus.Unpaid)]);
        Assert.Equal(0, c.PaidLeaveDays);
        Assert.Equal(1, c.UnpaidLeaveDays);
        Assert.Equal(333.33m, c.LeaveDeduction);
    }

    /// <summary>Rounded once at the end: 2 × ₹333.33 would be ₹666.66, the true figure is ₹666.67.</summary>
    [Fact]
    public void Deduction_IsRoundedOnceNotPerDay()
    {
        var c = Calc(10000, 0, 2025, 9, Leave(2025, 9, 2));
        Assert.Equal(666.67m, c.LeaveDeduction);
        Assert.Equal(9333.33m, c.NetPayable);
    }

    [Theory]
    [InlineData(2024, 29)]   // leap year
    [InlineData(2025, 28)]
    public void February_UsesItsActualLength(int year, int days)
    {
        var c = Calc(days * 1000m, 0, year, 2, Leave(year, 2, 1));
        Assert.Equal(days, c.DaysInMonth);
        Assert.Equal(1000m, c.LeaveDeduction);
    }

    [Fact]
    public void MultipleAdvances_AreAllDeducted()
    {
        var c = Calc(10000, 2, 2025, 9, adjustments: 500 + 1000 + 300);
        Assert.Equal(1800m, c.AdjustmentsApplied);
        Assert.Equal(8200m, c.NetPayable);
    }

    [Fact]
    public void AdjustmentsLargerThanSalary_StopAtZeroAndCarryForward()
    {
        var c = Calc(10000, 2, 2025, 9, adjustments: 12000);
        Assert.Equal(0m, c.NetPayable);
        Assert.Equal(10000m, c.AdjustmentsApplied);
        Assert.Equal(2000m, c.CarryForward);
    }

    [Fact]
    public void JoiningMidMonth_ProratesByCalendarDays_AndIgnoresEarlierMarks()
    {
        var marks = new[] { (Sep(10), StaffAttendanceStatus.Unpaid) };   // before joining
        var c = Calc(10000, 2, 2025, 9, marks, joined: Sep(16));
        Assert.Equal(15, c.EmployedDays);
        Assert.Equal(5000m, c.GrossSalary);
        Assert.Equal(0, c.UnpaidLeaveDays);
        Assert.Equal(5000m, c.NetPayable);
    }

    [Fact]
    public void LeavingMidMonth_PaysOnlyUpToLastDay()
    {
        var c = Calc(10000, 2, 2025, 9, left: Sep(10));
        Assert.Equal(10, c.EmployedDays);
        Assert.Equal(3333.33m, c.GrossSalary);
    }

    [Theory]
    [InlineData(0, 2025, 9, "2025-09-30")]
    [InlineData(7, 2025, 9, "2025-10-07")]
    [InlineData(5, 2025, 12, "2026-01-05")]
    [InlineData(0, 2024, 2, "2024-02-29")]
    public void DueDate_FollowsPayDay(int payDay, int year, int month, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), StaffSalaryService.DueDate(payDay, year, month));

    // ── Workflow ─────────────────────────────────────────────────────────────────────────

    /// <summary>Configuration → attendance → advances → draft → pay → history/audit.</summary>
    [Fact]
    public async Task FullMonth_PaysNetAndBooksEachRupeeOnce()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c);

        await MarkAsync(c, id, "Leave", Sep(3), Sep(4), Sep(11), Sep(18), Sep(25));
        await AddPaymentAsync(c, id, new DateTime(2025, 9, 12), 1000);
        await AddPaymentAsync(c, id, new DateTime(2025, 9, 5), 300, "Mobile Recharge");
        await AddPaymentAsync(c, id, new DateTime(2025, 9, 20), 200, "Other", deduct: false);

        var draft = await BreakdownAsync(c, id, 2025, 9);
        Assert.Equal("Draft", draft.Status);
        Assert.True(draft.CanPay);
        Assert.Equal(3, draft.UnpaidLeaveDays);
        Assert.Equal(1000m, draft.LeaveDeduction);
        Assert.Equal(2, draft.Adjustments.Count());
        Assert.Equal(1300m, draft.AdjustmentsApplied);
        Assert.Equal(7700m, draft.NetPayable);

        var pay = await c.Pay(id, new SalaryPayRequest(2025, 9, new DateTime(2025, 9, 30), "UPI", null, 7700m));
        var paid = Assert.IsType<SalaryBreakdownDto>(Assert.IsType<OkObjectResult>(pay.Result).Value);
        Assert.Equal("Paid", paid.Status);
        Assert.Equal(7700m, paid.NetPayable);

        // Advances were booked when handed over; only the net is new. 1000 + 300 + 200 + 7700.
        var expenses = await db.Expenses.ToListAsync();
        Assert.Equal(4, expenses.Count);
        Assert.Equal(9200m, expenses.Sum(e => e.Amount));
        Assert.Single(expenses, e => e.Amount == 7700m && e.Month == 9 && e.Year == 2025 && e.Category == "Security");

        Assert.Equal(2, await db.StaffPayments.CountAsync(p => p.SettlementId == paid.SettlementId));
        Assert.Null((await db.StaffPayments.SingleAsync(p => !p.AdjustAgainstSalary)).SettlementId);

        var history = (IEnumerable<SalaryHistoryRowDto>)((OkObjectResult)(await c.History(id, 2025)).Result!).Value!;
        var row = Assert.Single(history);
        Assert.Equal("Paid", row.Status);
        Assert.Equal(7700m, row.NetPaid);

        var actions = await db.AuditLog.Where(a => a.Module == "Staff").Select(a => a.Action).ToListAsync();
        Assert.Contains("Add", actions);
        Assert.Contains("Attendance", actions);
        Assert.Contains("PaymentAdd", actions);
        Assert.Contains("SalaryPaid", actions);
    }

    [Fact]
    public async Task PayingTheSameMonthTwice_IsRefused()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c);

        Assert.IsType<OkObjectResult>((await c.Pay(id, new SalaryPayRequest(2025, 9, null, "Cash", null, 10000m))).Result);
        var again = await c.Pay(id, new SalaryPayRequest(2025, 9, null, "Cash", null, 10000m));

        Assert.IsType<ConflictObjectResult>(again.Result);
        Assert.Equal(1, await db.SalarySettlements.CountAsync());
        Assert.Equal(1, await db.Expenses.CountAsync());
    }

    /// <summary>The database itself refuses a second Paid row, even if the service check is bypassed.</summary>
    [Fact]
    public async Task UniqueIndex_BlocksSecondPaidRowForSameMonth()
    {
        using var db = NewSociety();
        var id = await AddWatchmanAsync(ControllerFor(db, Admin()));
        SalarySettlement Row() => new() { StaffId = id, Year = 2025, Month = 9, StaffName = "Ramesh", Role = "Watchman", PaidOn = DateTime.UtcNow };

        db.SalarySettlements.Add(Row());
        await db.SaveChangesAsync();
        db.SalarySettlements.Add(Row());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task StaleConfirmedAmount_IsRefused()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c);
        await AddPaymentAsync(c, id, new DateTime(2025, 9, 12), 1000);   // added after the admin looked

        var pay = await c.Pay(id, new SalaryPayRequest(2025, 9, null, "Cash", null, 10000m));

        Assert.IsType<ConflictObjectResult>(pay.Result);
        Assert.Equal(0, await db.SalarySettlements.CountAsync());
    }

    [Fact]
    public async Task PaidMonth_LocksAttendanceAndDeductedPayments()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c);
        var advance = await AddPaymentAsync(c, id, new DateTime(2025, 9, 12), 1000);
        await c.Pay(id, new SalaryPayRequest(2025, 9, null, "Cash", null, 9000m));

        Assert.IsType<BadRequestObjectResult>(await c.SetAttendance(id, new StaffAttendanceSetRequest(Sep(3), "Leave")));
        Assert.IsType<BadRequestObjectResult>(await c.UpdatePayment(advance,
            new StaffPaymentUpsertRequest(new DateTime(2025, 9, 12), 500, "Salary Advance", null, "Cash", true)));
        Assert.IsType<BadRequestObjectResult>(await c.DeletePayment(advance));

        // Nor can it be changed behind the salary's back from the Expenses screen.
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Admin() } };
        var expenses = new ExpensesController(db, null!, new AuditService(db, accessor))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Admin() } }
        };
        foreach (var e in await db.Expenses.ToListAsync())
            Assert.IsType<BadRequestObjectResult>(await expenses.Delete(e.Id));
    }

    [Fact]
    public async Task Reversal_KeepsHistory_WithdrawsExpense_AndAllowsCorrectedPayment()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c);
        await AddPaymentAsync(c, id, new DateTime(2025, 9, 12), 1000);
        var first = (SalaryBreakdownDto)((OkObjectResult)(await c.Pay(id,
            new SalaryPayRequest(2025, 9, null, "Cash", null, 9000m))).Result!).Value!;

        Assert.IsType<BadRequestObjectResult>((await c.Reverse(first.SettlementId!.Value, new SalaryReverseRequest(" "))).Result);

        var reversed = await c.Reverse(first.SettlementId!.Value, new SalaryReverseRequest("Forgot 3 days of leave"));
        Assert.Equal("Reversed", Assert.IsType<SalaryBreakdownDto>(Assert.IsType<OkObjectResult>(reversed.Result).Value).Status);
        Assert.DoesNotContain(await db.Expenses.ToListAsync(), e => e.Amount == 9000m);
        Assert.Null((await db.StaffPayments.SingleAsync()).SettlementId);

        await MarkAsync(c, id, "Unpaid", Sep(1), Sep(2), Sep(3));
        var corrected = await BreakdownAsync(c, id, 2025, 9);
        Assert.Equal(8000m, corrected.NetPayable);
        Assert.IsType<OkObjectResult>((await c.Pay(id, new SalaryPayRequest(2025, 9, null, "Cash", null, 8000m))).Result);

        var history = ((IEnumerable<SalaryHistoryRowDto>)((OkObjectResult)(await c.History(id, null)).Result!).Value!).ToList();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.Status == "Reversed" && h.NetPaid == 9000m && h.ReversalReason == "Forgot 3 days of leave");
        Assert.Contains(history, h => h.Status == "Paid" && h.NetPaid == 8000m);
        Assert.Contains("SalaryReversed", await db.AuditLog.Select(a => a.Action).ToListAsync());
    }

    [Fact]
    public async Task AdvanceLargerThanSalary_CarriesIntoNextMonth()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c);
        await AddPaymentAsync(c, id, new DateTime(2025, 9, 2), 12000, "Emergency Advance");

        var sep = await BreakdownAsync(c, id, 2025, 9);
        Assert.Equal(0m, sep.NetPayable);
        Assert.Equal(2000m, sep.CarryForward);
        await c.Pay(id, new SalaryPayRequest(2025, 9, null, "Cash", null, 0m));
        Assert.Equal(1, await db.Expenses.CountAsync());   // only the advance: no cash left at month-end

        var oct = await BreakdownAsync(c, id, 2025, 10);
        Assert.Equal(2000m, oct.CarryIn);
        Assert.Equal(8000m, oct.NetPayable);
    }

    [Fact]
    public async Task Months_AreSettledInOrder()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c);
        var sep = (SalaryBreakdownDto)((OkObjectResult)(await c.Pay(id,
            new SalaryPayRequest(2025, 9, null, "Cash", null, 10000m))).Result!).Value!;
        await c.Pay(id, new SalaryPayRequest(2025, 10, null, "Cash", null, 10000m));

        Assert.IsType<BadRequestObjectResult>((await c.Reverse(sep.SettlementId!.Value, new SalaryReverseRequest("wrong"))).Result);

        var aug = await BreakdownAsync(c, id, 2025, 8);
        Assert.False(aug.CanPay);
        Assert.IsType<BadRequestObjectResult>((await c.Pay(id, new SalaryPayRequest(2025, 8, null, "Cash", null, aug.NetPayable))).Result);
    }

    [Fact]
    public async Task HistoricalMonth_KeepsTheSalaryItWasPaidAt()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c);
        await c.Pay(id, new SalaryPayRequest(2025, 9, null, "Cash", null, 10000m));

        await c.Update(id, Watchman(salary: 12000));

        Assert.Equal(10000m, (await BreakdownAsync(c, id, 2025, 9)).NetPayable);
        Assert.Equal(12000m, (await BreakdownAsync(c, id, 2025, 10)).NetPayable);
    }

    [Fact]
    public async Task NotYetEmployed_CannotBePaid()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c, joined: new DateOnly(2025, 10, 1));

        var sep = await BreakdownAsync(c, id, 2025, 9);
        Assert.False(sep.CanPay);
        Assert.IsType<BadRequestObjectResult>(await c.SetAttendance(id, new StaffAttendanceSetRequest(Sep(5), "Leave")));
    }

    [Fact]
    public async Task Validation_RejectsBadInput()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c);

        Assert.IsType<BadRequestObjectResult>((await c.Create(Watchman(salary: -1))).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(Watchman(leave: -1))).Result);
        Assert.IsType<BadRequestObjectResult>((await c.AddPayment(id,
            new StaffPaymentUpsertRequest(new DateTime(2025, 9, 1), 0, "Salary Advance", null, null, true))).Result);
        Assert.IsType<BadRequestObjectResult>(await c.SetAttendance(id, new StaffAttendanceSetRequest(Sep(1), "Holiday")));
        Assert.IsType<BadRequestObjectResult>(await c.SetAttendance(id,
            new StaffAttendanceSetRequest(StaffSalaryService.Today.AddDays(1), "Leave")));
    }

    [Fact]
    public async Task MarkingPresent_ClearsTheDay()
    {
        using var db = NewSociety();
        var c = ControllerFor(db, Admin());
        var id = await AddWatchmanAsync(c);
        await MarkAsync(c, id, "Unpaid", Sep(1));
        await MarkAsync(c, id, "Present", Sep(1));

        Assert.Equal(0, await db.StaffAttendance.CountAsync());
        Assert.Equal(10000m, (await BreakdownAsync(c, id, 2025, 9)).NetPayable);
    }

    // ── Authorization ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExpensesEditor_RecordsPaymentsAndAttendance_ButCannotPayOrConfigure()
    {
        using var db = NewSociety();
        var id = await AddWatchmanAsync(ControllerFor(db, Admin()));
        var c = ControllerFor(db, WithExpenses("Edit"));

        Assert.IsType<CreatedAtActionResult>((await c.AddPayment(id,
            new StaffPaymentUpsertRequest(new DateTime(2025, 9, 1), 500, "Salary Advance", null, null, true))).Result);
        Assert.IsType<NoContentResult>(await c.SetAttendance(id, new StaffAttendanceSetRequest(Sep(2), "Leave")));

        Assert.IsType<ForbidResult>((await c.Pay(id, new SalaryPayRequest(2025, 9, null, "Cash", null, 9500m))).Result);
        Assert.IsType<ForbidResult>(await c.Update(id, Watchman(salary: 20000)));
        Assert.IsType<ForbidResult>((await c.Create(Watchman())).Result);
    }

    [Fact]
    public async Task ExpensesViewer_CanOnlyRead()
    {
        using var db = NewSociety();
        var id = await AddWatchmanAsync(ControllerFor(db, Admin()));
        var c = ControllerFor(db, WithExpenses("View"));

        Assert.IsType<OkObjectResult>((await c.Salary(id, 2025, 9)).Result);
        Assert.IsType<ForbidResult>((await c.AddPayment(id,
            new StaffPaymentUpsertRequest(new DateTime(2025, 9, 1), 500, "Salary Advance", null, null, true))).Result);
        Assert.IsType<ForbidResult>(await c.SetAttendance(id, new StaffAttendanceSetRequest(Sep(2), "Leave")));
    }

    [Fact]
    public async Task NoExpensesAccess_OrCaretaker_SeesNothing()
    {
        using var db = NewSociety();
        var id = await AddWatchmanAsync(ControllerFor(db, Admin()));

        Assert.IsType<ForbidResult>((await ControllerFor(db, WithExpenses("None")).Salary(id, 2025, 9)).Result);
        Assert.IsType<ForbidResult>((await ControllerFor(db, Caretaker()).GetAll()).Result);
    }
}

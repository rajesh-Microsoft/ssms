using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Controllers;
using SMMS.Api.Data;
using SMMS.Api.Data.Interceptors;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;
using SMMS.Api.Services.Billing;
using Xunit;

namespace SMMS.Api.Tests;

/// <summary>
/// A member pays the watchman out of pocket and claims it back. The claim keeps its usual path
/// (approve → expense + liability → repay), and only when the reviewer explicitly chooses to at
/// approval is the amount also deducted from the watchman's salary, without booking it twice.
/// </summary>
public class ReimbursementSalaryTests : IDisposable
{
    private readonly List<SqliteConnection> _connections = [];

    private sealed record Society(
        SmmsDbContext Db, ReimbursementsController Claims, StaffController Staff,
        SocietyLiabilitiesController Liabilities, int StaffId, int MemberId);

    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "admin"),
            new Claim(ClaimTypes.NameIdentifier, "999"),
            new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    /// <summary>Runs with the real save interceptor, because deleting a liability is a soft delete
    /// in production and a hard delete would trip the claim's foreign key instead.</summary>
    private Society NewSociety(ClaimsPrincipal? user = null)
    {
        user ??= Admin();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        _connections.Add(connection);

        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } };
        var options = new DbContextOptionsBuilder<SmmsDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(accessor))
            .Options;
        var db = new SmmsDbContext(options);
        db.Database.EnsureCreated();

        var audit = new AuditService(db, accessor);
        var salary = new StaffSalaryService(db);
        var liabilityService = new SocietyLiabilityService(db, new AdvanceService(db));
        var ctx = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };

        var member = new Member { Name = "Anita", Flat = "302" };
        db.Members.Add(member);
        var staff = new Staff
        {
            Name = "Ramesh", Role = "Watchman", JoiningDate = new DateOnly(2025, 1, 1),
            MonthlySalary = 10000, PaidLeavePerMonth = 2, ExpenseCategory = "Security"
        };
        db.StaffMembers.Add(staff);
        db.SaveChanges();

        return new Society(db,
            new ReimbursementsController(db, liabilityService, salary, null!, audit) { ControllerContext = ctx },
            new StaffController(db, salary, audit) { ControllerContext = ctx },
            new SocietyLiabilitiesController(db, liabilityService, audit) { ControllerContext = ctx },
            staff.Id, member.Id);
    }

    public void Dispose()
    {
        foreach (var c in _connections) c.Dispose();
        GC.SuppressFinalize(this);
    }

    private static int Claim(Society s, decimal amount, DateTime spentOn, string category = "Repairs")
    {
        var claim = new ReimbursementRequest
        {
            MemberId = s.MemberId, SubmittedByUserId = 1, Category = category,
            Description = "Gave the watchman an advance", Amount = amount, ExpenseDate = spentOn,
            PaymentMode = "UPI", Status = ReimbursementStatus.Pending
        };
        s.Db.ReimbursementRequests.Add(claim);
        s.Db.SaveChanges();
        return claim.Id;
    }

    private static Task<ActionResult<ReimbursementDto>> Approve(Society s, int claimId, string? category = null, bool deduct = true) =>
        s.Claims.Approve(claimId, new ReimbursementApproveRequest(category, deduct ? s.StaffId : null));

    private static async Task<SalaryBreakdownDto> Salary(Society s, int year, int month) =>
        (SalaryBreakdownDto)((OkObjectResult)(await s.Staff.Salary(s.StaffId, year, month)).Result!).Value!;

    private static async Task<SalaryBreakdownDto> Pay(Society s, int year, int month, decimal expected) =>
        (SalaryBreakdownDto)((OkObjectResult)(await s.Staff.Pay(s.StaffId,
            new SalaryPayRequest(year, month, null, "Cash", null, expected))).Result!).Value!;

    private static async Task MarkLeave(Society s, params int[] septemberDays)
    {
        foreach (var d in septemberDays)
            await s.Staff.SetAttendance(s.StaffId, new StaffAttendanceSetRequest(new DateOnly(2025, 9, d), "Leave"));
    }

    // ── The requirement's example ──────────────────────────────────────────────────────

    /// <summary>₹10,000 − ₹1,000 advance − ₹500 approved claim − ₹1,000 leave = ₹7,500. The member
    /// picked the wrong category and the reviewer corrected it while approving.</summary>
    [Fact]
    public async Task ApprovedClaim_IsDeducted_AndEveryRupeeIsBookedOnce()
    {
        var s = NewSociety();
        await MarkLeave(s, 3, 4, 11, 18, 25);
        await s.Staff.AddPayment(s.StaffId,
            new StaffPaymentUpsertRequest(new DateTime(2025, 9, 12), 1000, "Salary Advance", null, "Cash", true));
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10), category: "Repairs");

        var approved = await Approve(s, claimId, category: "Watchman Salary");
        var dto = Assert.IsType<ReimbursementDto>(Assert.IsType<OkObjectResult>(approved.Result).Value);
        Assert.Equal("Ramesh (Watchman)", dto.SalaryDeduction);

        var sep = await Salary(s, 2025, 9);
        Assert.Equal(1000m, sep.LeaveDeduction);
        Assert.Equal(1500m, sep.AdjustmentsApplied);
        Assert.Equal(7500m, sep.NetPayable);
        Assert.Contains(sep.Adjustments, a => a.Category == StaffSalaryService.ReimbursementCategory && a.Amount == 500m);

        // The claim keeps its normal path: one expense under the reviewer's category and a liability.
        var claimExpense = await s.Db.Expenses.SingleAsync(e => e.FundedByLiabilityId != null);
        Assert.Equal(500m, claimExpense.Amount);
        Assert.Equal("Watchman Salary", claimExpense.Category);
        var deduction = await s.Db.StaffPayments.SingleAsync(p => p.ReimbursementId == claimId);
        Assert.Equal(claimExpense.Id, deduction.ExpenseId);
        Assert.Equal(new DateTime(2025, 9, 10), deduction.Date);
        Assert.Equal(1, await s.Db.SocietyLiabilities.CountAsync());

        await Pay(s, 2025, 9, 7500m);
        // 1,000 advance + 500 claim + 7,500 salary = salary less the leave deduction.
        Assert.Equal(9000m, (await s.Db.Expenses.ToListAsync()).Sum(e => e.Amount));

        var audit = await s.Db.AuditLog.ToListAsync();
        Assert.Contains(audit, a => a.Module == "Reimbursements" && a.Action == "Approve" && a.Details.Contains("Deducted from Ramesh"));
        Assert.Contains(audit, a => a.Module == "Staff" && a.Action == "PaymentAdd" && a.Details.Contains($"claim #{claimId}"));
    }

    // ── Only the reviewer's decision counts ────────────────────────────────────────────

    [Fact]
    public async Task SalaryCategoryAlone_DeductsNothing()
    {
        var s = NewSociety();
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10), category: "Watchman Salary");

        await Approve(s, claimId, deduct: false);

        Assert.Equal(0, await s.Db.StaffPayments.CountAsync());
        Assert.Equal(10000m, (await Salary(s, 2025, 9)).NetPayable);
        Assert.Equal(1, await s.Db.Expenses.CountAsync());   // the claim itself is still booked as usual
    }

    [Fact]
    public async Task UnapprovedClaims_NeverTouchTheSalary()
    {
        var s = NewSociety();
        var info = Claim(s, 300, new DateTime(2025, 9, 5), "Watchman Salary");
        var rejected = Claim(s, 400, new DateTime(2025, 9, 6), "Watchman Salary");
        Claim(s, 500, new DateTime(2025, 9, 7), "Watchman Salary");   // left pending

        await s.Claims.RequestInfo(info, new ReimbursementReviewRequest("Attach the receipt"));
        await s.Claims.Reject(rejected, new ReimbursementReviewRequest("Not a society cost"));

        Assert.Equal(0, await s.Db.StaffPayments.CountAsync());
        Assert.Equal(10000m, (await Salary(s, 2025, 9)).NetPayable);
    }

    [Fact]
    public async Task ApprovingTwice_DeductsOnce()
    {
        var s = NewSociety();
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10));
        await Approve(s, claimId);

        var again = await Approve(s, claimId);

        Assert.IsType<BadRequestObjectResult>(again.Result);
        Assert.Equal(1, await s.Db.StaffPayments.CountAsync());
        Assert.Equal(9500m, (await Salary(s, 2025, 9)).NetPayable);
    }

    [Fact]
    public async Task Database_RefusesASecondDeductionForTheSameClaim()
    {
        var s = NewSociety();
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10));
        await Approve(s, claimId);

        s.Db.StaffPayments.Add(new StaffPayment
        {
            StaffId = s.StaffId, Date = new DateTime(2025, 9, 10), Amount = 500, Category = "Other",
            AdjustAgainstSalary = true, ReimbursementId = claimId
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => s.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task UnknownStaff_IsRefused_AndTheClaimStaysPending()
    {
        var s = NewSociety();
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10));

        var result = await s.Claims.Approve(claimId, new ReimbursementApproveRequest(null, 12345));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(ReimbursementStatus.Pending, (await s.Db.ReimbursementRequests.SingleAsync()).Status);
        Assert.Equal(0, await s.Db.SocietyLiabilities.CountAsync());
    }

    [Fact]
    public async Task StaffWhoseFinalSalaryIsPaid_CannotTakeADeduction()
    {
        var s = NewSociety();
        await Pay(s, 2025, 9, 10000m);
        var staff = await s.Db.StaffMembers.SingleAsync();
        staff.IsActive = false;
        staff.LeavingDate = new DateOnly(2025, 9, 30);
        await s.Db.SaveChangesAsync();
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10));

        Assert.IsType<BadRequestObjectResult>((await Approve(s, claimId)).Result);
        Assert.Equal(0, await s.Db.SocietyLiabilities.CountAsync());
    }

    // ── Which month ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClaimApprovedAfterTheMonthWasPaid_ComesOffTheNextSalary()
    {
        var s = NewSociety();
        await Pay(s, 2025, 9, 10000m);
        var claimId = Claim(s, 500, new DateTime(2025, 9, 25));

        await Approve(s, claimId);

        Assert.Equal(10000m, (await Salary(s, 2025, 9)).NetPayable);   // paid month untouched
        Assert.Equal(9500m, (await Salary(s, 2025, 10)).NetPayable);
    }

    [Fact]
    public async Task ClaimCoveringTheWholeSalary_LeavesNothingToPay()
    {
        var s = NewSociety();
        var claimId = Claim(s, 10000, new DateTime(2025, 9, 30));
        await Approve(s, claimId);

        var sep = await Salary(s, 2025, 9);
        Assert.Equal(0m, sep.NetPayable);
        await Pay(s, 2025, 9, 0m);

        Assert.Equal(1, await s.Db.Expenses.CountAsync());   // only the claim; no salary cash
    }

    // ── Managed from the claim, not the salary screen ──────────────────────────────────

    [Fact]
    public async Task ClaimDeduction_IsReadOnlyOnTheSalaryScreen()
    {
        var s = NewSociety();
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10));
        await Approve(s, claimId);
        var paymentId = (await s.Db.StaffPayments.SingleAsync()).Id;

        Assert.IsType<BadRequestObjectResult>(await s.Staff.UpdatePayment(paymentId,
            new StaffPaymentUpsertRequest(new DateTime(2025, 9, 10), 50, "Other", null, "Cash", false)));
        Assert.IsType<BadRequestObjectResult>(await s.Staff.DeletePayment(paymentId));
        Assert.Equal(500m, (await s.Db.StaffPayments.SingleAsync()).Amount);
    }

    [Fact]
    public async Task DeletingTheLiability_RemovesTheDeduction()
    {
        var s = NewSociety();
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10));
        await Approve(s, claimId);
        var liabilityId = (await s.Db.SocietyLiabilities.SingleAsync()).Id;

        Assert.IsType<NoContentResult>(await s.Liabilities.Delete(liabilityId));

        Assert.Equal(0, await s.Db.StaffPayments.CountAsync());
        Assert.Equal(0, await s.Db.Expenses.CountAsync());
        Assert.Equal(10000m, (await Salary(s, 2025, 9)).NetPayable);
    }

    [Fact]
    public async Task DeletingTheLiability_IsBlockedOnceAPaidSalaryUsedIt_UntilThatSalaryIsReversed()
    {
        var s = NewSociety();
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10));
        await Approve(s, claimId);
        var paid = await Pay(s, 2025, 9, 9500m);
        var liabilityId = (await s.Db.SocietyLiabilities.SingleAsync()).Id;

        Assert.IsType<BadRequestObjectResult>(await s.Liabilities.Delete(liabilityId));
        Assert.Equal(1, await s.Db.StaffPayments.CountAsync());

        await s.Staff.Reverse(paid.SettlementId!.Value, new SalaryReverseRequest("Claim was a duplicate"));
        Assert.IsType<NoContentResult>(await s.Liabilities.Delete(liabilityId));
        Assert.Equal(10000m, (await Salary(s, 2025, 9)).NetPayable);
    }

    [Fact]
    public async Task RepayingTheMember_LeavesTheDeductionAndBooksNoNewExpense()
    {
        var s = NewSociety();
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10));
        await Approve(s, claimId);

        var settled = await s.Claims.Settle(claimId, new ReimbursementSettleRequest(500, "Repaid", "UPI", "UTR123", null));

        Assert.IsType<OkObjectResult>(settled.Result);
        Assert.Equal(LiabilityStatus.Settled, (await s.Db.SocietyLiabilities.SingleAsync()).Status);
        Assert.Equal(1, await s.Db.Expenses.CountAsync());
        Assert.Equal(9500m, (await Salary(s, 2025, 9)).NetPayable);
    }

    // ── Who may do it ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExpensesEditorWithoutLiabilitiesEdit_CannotApprove()
    {
        var treasurer = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "treasurer"),
            new Claim(ClaimTypes.NameIdentifier, "998"),
            new Claim(ClaimTypes.Role, Roles.Member),
            new Claim($"perm:{PermissionModules.Expenses}", "Edit"),
            new Claim($"perm:{PermissionModules.Liabilities}", "View")], "test"));
        var s = NewSociety(treasurer);
        var claimId = Claim(s, 500, new DateTime(2025, 9, 10));

        Assert.IsType<ForbidResult>((await Approve(s, claimId)).Result);
        Assert.Equal(0, await s.Db.StaffPayments.CountAsync());
    }
}

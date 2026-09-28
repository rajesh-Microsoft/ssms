using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record StaffDto(
    int Id,
    string Name,
    string Role,
    string? Mobile,
    DateOnly JoiningDate,
    DateOnly? LeavingDate,
    decimal MonthlySalary,
    int PaidLeavePerMonth,
    int PayDay,
    string ExpenseCategory,
    bool IsActive,
    string? Notes);

/// <summary><paramref name="PayDay"/>: 0 = last day of the month, 1–28 = that day of the next month.</summary>
public record StaffUpsertRequest(
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(40)] string Role,
    [MaxLength(20)] string? Mobile,
    DateOnly JoiningDate,
    DateOnly? LeavingDate,
    [Range(0, 9999999)] decimal MonthlySalary,
    [Range(0, 31)] int PaidLeavePerMonth,
    [Range(0, 28)] int PayDay,
    [MaxLength(60)] string? ExpenseCategory,
    bool IsActive,
    [MaxLength(300)] string? Notes);

public record StaffAttendanceDayDto(DateOnly Date, string Status);

public record StaffAttendanceMonthDto(
    int Year,
    int Month,
    int DaysInMonth,
    DateOnly? EmployedFrom,
    DateOnly? EmployedTo,
    bool Locked,
    int PresentDays,
    int LeaveDays,
    int PaidLeaveDays,
    int UnpaidLeaveDays,
    IEnumerable<StaffAttendanceDayDto> Days);

/// <summary><paramref name="Status"/> is Present, Leave or Unpaid.</summary>
public record StaffAttendanceSetRequest(DateOnly Date, [Required] string Status);

public record StaffPaymentDto(
    int Id,
    int StaffId,
    DateTime Date,
    decimal Amount,
    string Category,
    string? Notes,
    string? PaymentMode,
    bool AdjustAgainstSalary,
    int? ExpenseId,
    int? SettlementId,
    string? SettledPeriod,
    string? CreatedBy,
    DateTime CreatedOn,
    int? ReimbursementId = null);

public record StaffPaymentUpsertRequest(
    DateTime Date,
    [Range(0.01, 9999999)] decimal Amount,
    [Required, MaxLength(40)] string Category,
    [MaxLength(300)] string? Notes,
    [MaxLength(30)] string? PaymentMode,
    bool AdjustAgainstSalary);

/// <summary>One deduction in a salary breakdown. <paramref name="PaymentId"/> is null for the
/// balance carried in from the previous month.</summary>
public record SalaryAdjustmentLineDto(int? PaymentId, DateTime Date, string Category, string? Notes, decimal Amount);

/// <summary>The same shape for a live draft and a paid month, so the screen renders both alike.
/// For a draft, <paramref name="CanPay"/>/<paramref name="BlockedReason"/> say whether Pay is allowed.</summary>
public record SalaryBreakdownDto(
    int StaffId,
    string StaffName,
    string Role,
    int Year,
    int Month,
    string Status,
    int? SettlementId,
    decimal MonthlySalary,
    int DaysInMonth,
    int EmployedDays,
    int PresentDays,
    int PaidLeaveAllowed,
    int LeaveDays,
    int PaidLeaveDays,
    int UnpaidLeaveDays,
    decimal DailyRate,
    decimal GrossSalary,
    decimal LeaveDeduction,
    IEnumerable<SalaryAdjustmentLineDto> Adjustments,
    decimal CarryIn,
    decimal AdjustmentsTotal,
    decimal AdjustmentsApplied,
    decimal CarryForward,
    decimal NetPayable,
    DateTime? PaidOn,
    string? PaymentMode,
    string? PaidBy,
    int? ExpenseId,
    string? Notes,
    DateTime? ReversedOn,
    string? ReversedBy,
    string? ReversalReason,
    bool CanPay,
    string? BlockedReason);

/// <summary><paramref name="ExpectedNetPayable"/> is the figure the admin confirmed. If anything
/// changed since the breakdown was shown, the payment is refused rather than paying a different sum.</summary>
public record SalaryPayRequest(
    int Year,
    [Range(1, 12)] int Month,
    DateTime? PaidOn,
    [MaxLength(30)] string? PaymentMode,
    [MaxLength(300)] string? Notes,
    decimal ExpectedNetPayable);

public record SalaryReverseRequest([Required, MaxLength(300)] string Reason);

public record SalaryHistoryRowDto(
    int SettlementId,
    int Year,
    int Month,
    string Status,
    decimal GrossSalary,
    int PresentDays,
    int PaidLeaveDays,
    int UnpaidLeaveDays,
    decimal LeaveDeduction,
    decimal AdjustmentsApplied,
    decimal NetPaid,
    DateTime PaidOn,
    string? PaymentMode,
    DateTime? ReversedOn,
    string? ReversalReason);

/// <summary>The dashboard line for one staff member: the month that needs attention and its amount.</summary>
public record StaffSalarySummaryDto(
    int StaffId,
    string Name,
    string Role,
    int Year,
    int Month,
    string Status,
    decimal NetPayable,
    DateOnly DueDate,
    bool Overdue,
    DateTime? PaidOn);

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>A day that was not a normal working day. Present is the default and is not stored,
/// so a month with no rows means the staff member worked every day.</summary>
public enum StaffAttendanceStatus
{
    /// <summary>Leave that counts against the monthly paid-leave allowance; only the excess is unpaid.</summary>
    Leave,

    /// <summary>Always unpaid, whatever allowance is left (e.g. absent without informing).</summary>
    Unpaid
}

/// <summary>A settlement row only exists once money is paid. The draft is always computed live,
/// so it can never go stale against attendance or advances.</summary>
public enum SalarySettlementStatus
{
    Paid,
    Reversed
}

/// <summary>
/// Someone the society pays a fixed monthly salary to. Small societies have just a watchman, but
/// storing staff as rows means a replacement watchman or an added cleaner needs no schema change,
/// and a paid month always keeps the name of the person who was actually paid.
/// </summary>
public class Staff : IAuditable
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Role { get; set; } = "Watchman";

    [MaxLength(20)]
    public string? Mobile { get; set; }

    /// <summary>Days before this in the joining month are not paid (calendar-day proration).</summary>
    public DateOnly JoiningDate { get; set; }

    /// <summary>Days after this in the leaving month are not paid.</summary>
    public DateOnly? LeavingDate { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal MonthlySalary { get; set; }

    public int PaidLeavePerMonth { get; set; }

    /// <summary>0 = last day of the same month; 1–28 = that day of the following month.</summary>
    public int PayDay { get; set; }

    /// <summary>Expense category every payment to this person is booked under.</summary>
    [Required, MaxLength(60)]
    public string ExpenseCategory { get; set; } = "Security";

    public bool IsActive { get; set; } = true;

    [MaxLength(300)]
    public string? Notes { get; set; }

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>One non-working day. Unique per staff member and date.</summary>
public class StaffAttendance : IAuditable
{
    public int Id { get; set; }

    public int StaffId { get; set; }

    [ForeignKey(nameof(StaffId))]
    public Staff? Staff { get; set; }

    public DateOnly Date { get; set; }

    public StaffAttendanceStatus Status { get; set; }

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>
/// Money handed to a staff member during the month: an advance, a mobile recharge. The cash is
/// booked once, as the linked <see cref="Expense"/>, when it is paid. When
/// <see cref="AdjustAgainstSalary"/> is set the month-end settlement subtracts it from the salary
/// and references this row rather than booking the money a second time.
/// </summary>
public class StaffPayment : IAuditable, ISoftDelete
{
    public int Id { get; set; }

    public int StaffId { get; set; }

    [ForeignKey(nameof(StaffId))]
    public Staff? Staff { get; set; }

    public DateTime Date { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    [Required, MaxLength(40)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Notes { get; set; }

    [MaxLength(30)]
    public string? PaymentMode { get; set; }

    public bool AdjustAgainstSalary { get; set; }

    public int? ExpenseId { get; set; }

    [ForeignKey(nameof(ExpenseId))]
    public Expense? Expense { get; set; }

    /// <summary>The paid salary this was deducted from. Set means locked: editing it would change
    /// a salary that has already been handed over.</summary>
    public int? SettlementId { get; set; }

    [ForeignKey(nameof(SettlementId))]
    public SalarySettlement? Settlement { get; set; }

    /// <summary>Set when a member paid this out of pocket and the admin chose, on approving the
    /// claim, to deduct it from salary. The cash record is then the claim's own expense, and the
    /// row is managed from the claim rather than the salary screen.</summary>
    public int? ReimbursementId { get; set; }

    [ForeignKey(nameof(ReimbursementId))]
    public ReimbursementRequest? Reimbursement { get; set; }

    // ── Audit (IAuditable) + soft delete (ISoftDelete) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>
/// A paid month's salary, with every input copied in. Later changes to the salary, attendance or
/// advances never rewrite it; a mistake is undone by reversing it, which keeps this row.
/// Only one Paid row may exist per staff member and month, enforced by a filtered unique index.
/// </summary>
public class SalarySettlement : IAuditable
{
    public int Id { get; set; }

    public int StaffId { get; set; }

    [ForeignKey(nameof(StaffId))]
    public Staff? Staff { get; set; }

    public int Year { get; set; }

    [Range(1, 12)]
    public int Month { get; set; }

    public SalarySettlementStatus Status { get; set; } = SalarySettlementStatus.Paid;

    [Required, MaxLength(100)]
    public string StaffName { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Role { get; set; } = string.Empty;

    [Column(TypeName = "decimal(12,2)")]
    public decimal MonthlySalary { get; set; }

    public int DaysInMonth { get; set; }
    public int EmployedDays { get; set; }
    public int PresentDays { get; set; }
    public int PaidLeaveAllowed { get; set; }
    public int LeaveDays { get; set; }
    public int PaidLeaveDays { get; set; }
    public int UnpaidLeaveDays { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal DailyRate { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal GrossSalary { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal LeaveDeduction { get; set; }

    /// <summary>Advances the previous paid month could not absorb.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal CarryIn { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal AdjustmentsTotal { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal AdjustmentsApplied { get; set; }

    /// <summary>Adjustments larger than the salary; deducted from the next paid month.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal CarryForward { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal NetPaid { get; set; }

    public DateTime PaidOn { get; set; }

    [MaxLength(30)]
    public string? PaymentMode { get; set; }

    [MaxLength(300)]
    public string? Notes { get; set; }

    /// <summary>The expense that booked the net salary. Null when advances covered it all.</summary>
    public int? ExpenseId { get; set; }

    [ForeignKey(nameof(ExpenseId))]
    public Expense? Expense { get; set; }

    public DateTime? ReversedOn { get; set; }

    [MaxLength(50)]
    public string? ReversedBy { get; set; }

    [MaxLength(300)]
    public string? ReversalReason { get; set; }

    public List<SalarySettlementLine> Lines { get; set; } = [];

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>An adjustment as it stood when the salary was paid. Kept after a reversal, when the
/// underlying <see cref="StaffPayment"/> is released for the corrected settlement.</summary>
public class SalarySettlementLine
{
    public int Id { get; set; }

    public int SettlementId { get; set; }

    [ForeignKey(nameof(SettlementId))]
    public SalarySettlement? Settlement { get; set; }

    public int StaffPaymentId { get; set; }

    public DateTime Date { get; set; }

    [Required, MaxLength(40)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Notes { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }
}

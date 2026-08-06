using SMMS.Api.Data;
using SMMS.Api.Models;
using SMMS.Api.Services.Billing;

namespace SMMS.Api.Services;

/// <summary>
/// Core logic for the society-liability feature — the mirror of the advance wallet. Records money
/// the society owes a contributor and settles it in one of two ways. Like <see cref="AdvanceService"/>,
/// methods mutate tracked entities and queue rows but do NOT call SaveChanges — the caller commits.
/// </summary>
public class SocietyLiabilityService(SmmsDbContext db, AdvanceService advance)
{
    /// <summary>
    /// Records a new open liability and books the cost it paid for as an <see cref="Expense"/> in the
    /// month it was incurred, under its real category. The society consumed the goods the moment the
    /// contributor paid for them, so the cost belongs to that month whoever fronted the cash; the
    /// liability records only that the money is still owed. Repaying it later moves cash, not cost.
    /// </summary>
    public SocietyLiability Create(LiabilitySource source, Member? member, string? contributorName,
        DateTime date, decimal amount, string category, string? purpose)
    {
        var liability = new SocietyLiability
        {
            Source = source,
            MemberId = member?.Id,
            Member = member,
            ContributorName = contributorName,
            Date = date,
            Amount = amount,
            SettledAmount = 0m,
            Status = LiabilityStatus.Open,
            Category = category,
            Purpose = purpose
        };
        db.SocietyLiabilities.Add(liability);

        var label = ContributorLabel(liability, member);
        db.Expenses.Add(new Expense
        {
            ExpenseDate = date,
            Category = category,
            Description = string.IsNullOrWhiteSpace(purpose) ? $"Funded by {label}" : purpose,
            Amount = amount,
            Month = date.Month,
            Year = date.Year,
            Remarks = $"Paid out of pocket by {label} \u2014 society owes this back",
            FundedByLiability = liability // EF sets FundedByLiabilityId on commit
        });
        return liability;
    }

    private static string ContributorLabel(SocietyLiability liability, Member? member) =>
        member?.Name ?? liability.ContributorName ?? $"contributor #{liability.MemberId}";

    /// <summary>
    /// Settles part or all of a liability. Caps the settled amount at the outstanding balance.
    /// <list type="bullet">
    /// <item><b>Repaid</b> — hands real cash back. Books no expense, because the cost was already
    /// recorded when the liability was raised; this only clears the debt.</item>
    /// <item><b>ConvertToAdvance</b> — credits the member's advance wallet via
    /// <see cref="AdvanceService.Credit"/>; existing auto-adjustment then offsets future bills.</item>
    /// </list>
    /// <paramref name="costAlreadyBooked"/> is false only for liabilities raised before costs were
    /// booked up front; those still book their expense here, or their cost would never appear at all.
    /// Returns the amount actually settled (0 when nothing outstanding).
    /// </summary>
    public decimal Settle(SocietyLiability liability, decimal amount, LiabilitySettlementMethod method,
        Member? member, bool costAlreadyBooked, string? paymentMode, string? reference, string? note)
    {
        var outstanding = liability.Amount - liability.SettledAmount;
        if (amount <= 0 || outstanding <= 0) return 0m;
        var applied = Math.Min(amount, outstanding);

        Expense? repayExpense = null;
        if (method == LiabilitySettlementMethod.Repaid)
        {
            if (!costAlreadyBooked)
            {
                var label = ContributorLabel(liability, member);
                repayExpense = new Expense
                {
                    ExpenseDate = DateTime.UtcNow,
                    Category = "Contribution Repayment",
                    Description = $"Repayment to {label}" + (liability.Purpose is { Length: > 0 } p ? $" \u2014 {p}" : ""),
                    Amount = applied,
                    PaymentMode = paymentMode,
                    Month = DateTime.UtcNow.Month,
                    Year = DateTime.UtcNow.Year,
                    Remarks = string.IsNullOrWhiteSpace(reference)
                        ? note
                        : $"UTR {reference.Trim()}" + (string.IsNullOrWhiteSpace(note) ? "" : $" \u00b7 {note}")
                };
                db.Expenses.Add(repayExpense);
            }
        }
        else // ConvertedToAdvance
        {
            if (member is null)
                throw new InvalidOperationException("Cannot convert to advance without a linked member.");
            advance.Credit(member, applied, source: "LiabilitySettlement",
                note: $"Society liability #{liability.Id} converted to advance");
        }

        liability.SettledAmount += applied;
        liability.Status = liability.SettledAmount >= liability.Amount
            ? LiabilityStatus.Settled
            : LiabilityStatus.PartiallySettled;

        db.SocietyLiabilitySettlements.Add(new SocietyLiabilitySettlement
        {
            Liability = liability,
            Date = DateTime.UtcNow,
            Amount = applied,
            Method = method,
            Expense = repayExpense, // EF sets ExpenseId on commit
            Reference = reference?.Trim(),
            Note = note
        });
        return applied;
    }
}

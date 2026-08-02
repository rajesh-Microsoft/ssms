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
    /// <summary>Creates a new open liability. Amount must be positive.</summary>
    public SocietyLiability Create(LiabilitySource source, int? memberId, string? contributorName,
        DateTime date, decimal amount, string? purpose)
    {
        var liability = new SocietyLiability
        {
            Source = source,
            MemberId = memberId,
            ContributorName = contributorName,
            Date = date,
            Amount = amount,
            SettledAmount = 0m,
            Status = LiabilityStatus.Open,
            Purpose = purpose
        };
        db.SocietyLiabilities.Add(liability);
        return liability;
    }

    /// <summary>
    /// Settles part or all of a liability. Caps the settled amount at the outstanding balance.
    /// <list type="bullet">
    /// <item><b>Repaid</b> — books a real <see cref="Expense"/> (category "Contribution Repayment") so
    /// the cash actually leaves the society books and the Balance/Expense KPIs stay correct.</item>
    /// <item><b>ConvertToAdvance</b> — credits the member's advance wallet via
    /// <see cref="AdvanceService.Credit"/>; existing auto-adjustment then offsets future bills.</item>
    /// </list>
    /// Returns the amount actually settled (0 when nothing outstanding).
    /// </summary>
    public decimal Settle(SocietyLiability liability, decimal amount, LiabilitySettlementMethod method,
        Member? member, string? paymentMode, string? reference, string? note)
    {
        var outstanding = liability.Amount - liability.SettledAmount;
        if (amount <= 0 || outstanding <= 0) return 0m;
        var applied = Math.Min(amount, outstanding);

        Expense? repayExpense = null;
        if (method == LiabilitySettlementMethod.Repaid)
        {
            var label = member?.Name ?? liability.ContributorName ?? $"contributor #{liability.MemberId}";
            repayExpense = new Expense
            {
                ExpenseDate = DateTime.UtcNow,
                Category = "Contribution Repayment",
                Description = $"Repayment to {label}" + (liability.Purpose is { Length: > 0 } p ? $" — {p}" : ""),
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

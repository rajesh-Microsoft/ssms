using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;

namespace SMMS.Api.Services.Billing;

/// <summary>
/// Core logic for the advance (wallet) feature. Keeps "money received" separate from "maintenance
/// earned": overpayments are credited to a member's wallet as a liability, and only recognised as
/// income when applied to a specific month's invoice. Methods mutate tracked entities and queue
/// <see cref="AdvanceLedgerEntry"/> rows but do NOT call SaveChanges — the caller commits, so a
/// billing run can batch many members into one transaction.
/// </summary>
public class AdvanceService(SmmsDbContext db)
{
    /// <summary>Credits money into a member's wallet and appends a ledger entry. No-op for amounts ≤ 0.</summary>
    public void Credit(Member member, decimal amount, string source, string note,
        int? collectionId = null, int? paymentProofId = null)
    {
        if (amount <= 0) return;
        member.AdvanceBalance += amount;
        db.AdvanceLedger.Add(new AdvanceLedgerEntry
        {
            MemberId = member.Id,
            Date = DateTime.UtcNow,
            Type = "Credit",
            Amount = amount,
            BalanceAfter = member.AdvanceBalance,
            Source = source,
            CollectionId = collectionId,
            PaymentProofId = paymentProofId,
            Note = note
        });
    }

    /// <summary>
    /// Debits money out of a member's wallet (manual admin adjustment or refund on move-out) and
    /// appends a ledger entry. Never overdraws — the amount is capped at the current balance.
    /// Returns the amount actually debited.
    /// </summary>
    public decimal Debit(Member member, decimal amount, string source, string note)
    {
        if (amount <= 0 || member.AdvanceBalance <= 0) return 0m;
        var applied = Math.Min(amount, member.AdvanceBalance);
        member.AdvanceBalance -= applied;
        db.AdvanceLedger.Add(new AdvanceLedgerEntry
        {
            MemberId = member.Id,
            Date = DateTime.UtcNow,
            Type = "Debit",
            Amount = applied,
            BalanceAfter = member.AdvanceBalance,
            Source = source,
            Note = note
        });
        return applied;
    }

    /// <summary>
    /// Applies available wallet balance to a single charge (auto-adjust). Settles as much of the
    /// outstanding balance as the wallet allows: fully → Status "Paid" (mode "Advance"), partially →
    /// "Partial". Respects the member's <see cref="Member.AdvanceMode"/> ("Manual" wallets are left
    /// untouched). Returns the amount applied.
    /// </summary>
    public decimal ApplyToCharge(Member member, Collection charge)
    {
        if (member.AdvanceMode != "Auto") return 0m;

        var payable = charge.Amount - charge.AmountPaid;
        if (payable <= 0 || member.AdvanceBalance <= 0) return 0m;

        var apply = Math.Min(member.AdvanceBalance, payable);
        member.AdvanceBalance -= apply;
        charge.AmountPaid += apply;

        if (charge.AmountPaid >= charge.Amount)
        {
            charge.Status = "Paid";
            charge.PaymentDate = DateTime.UtcNow;
            charge.PaymentMode = "Advance";
        }
        else
        {
            charge.Status = "Partial";
        }

        db.AdvanceLedger.Add(new AdvanceLedgerEntry
        {
            MemberId = member.Id,
            Date = DateTime.UtcNow,
            Type = "Debit",
            Amount = apply,
            BalanceAfter = member.AdvanceBalance,
            Source = "BillAdjustment",
            CollectionId = charge.Id,
            Note = $"Applied to invoice #{charge.Id} ({charge.Month:D2}/{charge.Year})"
        });
        return apply;
    }

    /// <summary>
    /// Allocates a real payment received from a member across their outstanding dues, then credits
    /// any leftover to the wallet. Directly settles invoices with the received money (does NOT touch
    /// the existing wallet balance) — only the surplus becomes a wallet credit. Target selection:
    /// <list type="bullet">
    /// <item>"AdvancePayment" — nothing settled; the whole amount is credited to the wallet.</item>
    /// <item>"CurrentMonthOnly" — settles only the most recent due; remainder to the wallet.</item>
    /// <item>"CurrentPlusArrears" (default) — settles oldest-first across all dues; remainder to the wallet.</item>
    /// </list>
    /// Mutates the passed <paramref name="dues"/> and member; caller commits.
    /// </summary>
    public PaymentAllocationResult AllocatePayment(Member member, decimal amountReceived,
        string paymentType, string? paymentMode, DateTime paymentDate, List<Collection> dues, string? remarks = null)
    {
        var remaining = amountReceived;
        var settled = 0;
        var partial = 0;

        IEnumerable<Collection> targets = paymentType switch
        {
            "AdvancePayment" => Enumerable.Empty<Collection>(),
            "CurrentMonthOnly" => dues.OrderByDescending(d => d.Year).ThenByDescending(d => d.Month).Take(1),
            _ => dues.OrderBy(d => d.Year).ThenBy(d => d.Month)
        };

        foreach (var charge in targets)
        {
            if (remaining <= 0) break;
            var payable = charge.Amount - charge.AmountPaid;
            if (payable <= 0) continue;

            var apply = Math.Min(remaining, payable);
            charge.AmountPaid += apply;
            remaining -= apply;
            charge.PaymentDate = paymentDate;
            charge.PaymentMode = paymentMode;
            if (!string.IsNullOrWhiteSpace(remarks)) charge.Remarks = remarks;

            if (charge.AmountPaid >= charge.Amount)
            {
                charge.Status = "Paid";
                settled++;
            }
            else
            {
                charge.Status = "Partial";
                partial++;
            }
        }

        var credited = remaining;
        if (credited > 0)
        {
            var note = string.IsNullOrWhiteSpace(remarks)
                ? $"Advance from {paymentType} payment of {amountReceived:0.00}"
                : $"Advance from {paymentType} payment of {amountReceived:0.00} — {remarks}";
            Credit(member, credited, "Payment", note);
        }

        return new PaymentAllocationResult(
            amountReceived, amountReceived - credited, credited, settled, partial, member.AdvanceBalance);
    }
}

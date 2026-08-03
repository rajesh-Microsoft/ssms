using SMMS.Api.Data;
using SMMS.Api.Models;
using SMMS.Api.Services.Billing;
using SMMS.Api.Services.Storage;

namespace SMMS.Api.Services.Payments;

/// <summary>
/// Orchestrates the manual-UPI payment lifecycle: resident submits proof against a charge,
/// admin approves (marking the charge Paid) or rejects. Ownership/authorization is enforced by
/// the calling controller; this service owns the state transitions, persistence and audit trail.
/// </summary>
public class PaymentService(SmmsDbContext db, IFileStorage storage, AuditService audit, AdvanceService advance)
{
    /// <summary>Records a resident's payment proof for a charge in Pending state.</summary>
    public async Task<PaymentProof> SubmitProofAsync(
        Collection charge, Member member, int userId,
        Stream? screenshot, string? originalFileName, string? contentType, string? upiReference,
        CancellationToken ct = default)
    {
        string? storedPath = null;
        if (screenshot is not null)
            storedPath = await storage.SaveAsync(screenshot, "payment-proofs", originalFileName ?? "proof", ct);

        var proof = new PaymentProof
        {
            CollectionId = charge.Id,
            MemberId = member.Id,
            SubmittedByUserId = userId,
            Amount = charge.Amount,
            UpiReference = upiReference,
            FileName = originalFileName,
            StoredPath = storedPath,
            ContentType = contentType,
            Status = "Pending",
            SubmittedAt = DateTime.UtcNow,
            GatewayName = "ManualUPI"
        };
        db.PaymentProofs.Add(proof);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Payments", "SubmitProof",
            $"Flat {member.Flat} submitted UPI proof of {charge.Amount:0.00} for {PaymentNumbering.InvoiceNumber(charge)}");
        return proof;
    }

    /// <summary>Approves a pending proof and marks the linked charge Paid.</summary>
    public async Task ApproveAsync(PaymentProof proof, int reviewerUserId, CancellationToken ct = default)
    {
        proof.Status = "Approved";
        proof.ReviewedByUserId = reviewerUserId;
        proof.ReviewedAt = DateTime.UtcNow;

        await CompleteChargeAsync(proof, ct);
        await audit.LogAsync("Payments", "Approve",
            $"Approved payment proof #{proof.Id} (charge {proof.CollectionId}); marked Paid");
    }

    public async Task ApproveGatewayPaymentAsync(
        PaymentProof proof, string paymentId, CancellationToken ct = default)
    {
        if (proof.Status == "Approved") return;

        proof.Status = "Approved";
        proof.UpiReference = paymentId;
        proof.ReviewedAt = DateTime.UtcNow;
        proof.ReviewRemarks = "Automatically verified by Razorpay.";

        await CompleteChargeAsync(proof, ct);
        await audit.LogAsync("Payments", "GatewayApprove",
            $"Razorpay payment {paymentId} verified for charge {proof.CollectionId}; marked Paid");
    }

    private async Task CompleteChargeAsync(PaymentProof proof, CancellationToken ct)
    {
        var charge = await db.Collections.FindAsync([proof.CollectionId], ct);
        if (charge is not null)
        {
            charge.Status = "Paid";
            charge.PaymentDate = DateTime.UtcNow;
            // Gateway payments can be card/netbanking/wallet, so don't label everything UPI.
            charge.PaymentMode = proof.GatewayName == "ManualUPI" ? "UPI" : proof.GatewayName ?? "UPI";
            charge.AmountPaid = charge.Amount;

            // Any amount received beyond the invoice becomes advance credit (a liability, not income).
            var surplus = proof.Amount - charge.Amount;
            if (surplus > 0)
            {
                var member = await db.Members.FindAsync([proof.MemberId], ct);
                if (member is not null)
                    advance.Credit(member, surplus, "Payment",
                        $"Advance from overpayment on invoice #{charge.Id}", charge.Id, proof.Id);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Rejects a pending proof with an optional reason. The charge stays unpaid.</summary>
    public async Task RejectAsync(PaymentProof proof, int reviewerUserId, string? remarks, CancellationToken ct = default)
    {
        proof.Status = "Rejected";
        proof.ReviewedByUserId = reviewerUserId;
        proof.ReviewedAt = DateTime.UtcNow;
        proof.ReviewRemarks = remarks;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Payments", "Reject",
            $"Rejected payment proof #{proof.Id} (charge {proof.CollectionId}){(string.IsNullOrWhiteSpace(remarks) ? "" : $": {remarks}")}");
    }
}

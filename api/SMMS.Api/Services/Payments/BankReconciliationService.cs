using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;

namespace SMMS.Api.Services.Payments;

/// <summary>
/// Reconciles imported bank-statement credits against resident-submitted <see cref="PaymentProof"/>s.
/// Parses a CSV export, stores incoming credits as <see cref="BankTransaction"/>s (deduped), then
/// matches them to pending proofs by exact UTR first, falling back to amount+date. Exact single-UTR
/// matches are auto-confirmed (the proof is approved and its charge marked Paid); ambiguous ones are
/// surfaced as suggestions for the admin to confirm.
/// </summary>
public class BankReconciliationService(SmmsDbContext db, PaymentService payments, AuditService audit)
{
    private static readonly string[] DateHeaders = { "transaction date", "value date", "txn date", "date", "tran date" };
    private static readonly string[] NarrationHeaders = { "transaction remarks", "transaction description", "narration", "description", "particulars", "remarks" };
    private static readonly string[] CreditHeaders = { "deposit amount(inr)", "deposit amount", "credit amount", "credit(inr)", "credit", "deposit", "cr amount" };
    private static readonly string[] DebitHeaders = { "withdrawal amount(inr)", "withdrawal amount", "debit amount", "debit(inr)", "debit", "withdrawal", "dr amount" };

    private const int DateWindowDays = 5;

    // ── Import ──────────────────────────────────────────────────────────────
    public async Task<ImportResultDto> ImportCsvAsync(Stream csv, CancellationToken ct = default)
    {
        var parsed = ParseCsv(csv);
        var batch = "BATCH-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        // Existing keys for idempotent re-imports of overlapping statements.
        var existing = await db.BankTransactions
            .Select(t => new { t.Reference, t.TxnDate, t.Amount, t.Narration })
            .ToListAsync(ct);
        var existingRefs = existing.Where(e => !string.IsNullOrWhiteSpace(e.Reference))
            .Select(e => NormalizeRef(e.Reference)!).ToHashSet();
        var existingFallback = existing
            .Select(e => FallbackKey(e.TxnDate, e.Amount, e.Narration)).ToHashSet();

        int imported = 0, skippedDup = 0;
        var newRows = new List<BankTransaction>();
        foreach (var row in parsed.Credits)
        {
            var reference = ExtractReference(row.Narration);
            var normRef = NormalizeRef(reference);
            bool dup = (normRef is not null && existingRefs.Contains(normRef))
                       || existingFallback.Contains(FallbackKey(row.Date, row.Amount, row.Narration));
            if (dup) { skippedDup++; continue; }

            var txn = new BankTransaction
            {
                TxnDate = row.Date,
                Narration = Trunc(row.Narration, 500),
                Reference = reference,
                Amount = row.Amount,
                Status = "Unmatched",
                ImportBatch = batch,
                ImportedAt = DateTime.UtcNow
            };
            newRows.Add(txn);
            if (normRef is not null) existingRefs.Add(normRef);
            existingFallback.Add(FallbackKey(row.Date, row.Amount, row.Narration));
            imported++;
        }

        if (newRows.Count > 0)
        {
            db.BankTransactions.AddRange(newRows);
            await db.SaveChangesAsync(ct);
        }

        // Auto-confirm unambiguous exact-UTR matches.
        int autoMatched = await AutoMatchAsync(newRows, ct);

        await audit.LogAsync("Reconciliation", "Import",
            $"Imported {imported} bank credit(s) (batch {batch}); {autoMatched} auto-matched, {skippedDup} duplicate(s) skipped");

        var dtos = await BuildDtosAsync(newRows.Select(r => r.Id).ToList(), ct);
        return new ImportResultDto(batch, parsed.TotalRows, imported, skippedDup, parsed.SkippedDebits, autoMatched, dtos);
    }

    // ── Matching ────────────────────────────────────────────────────────────
    private async Task<int> AutoMatchAsync(List<BankTransaction> txns, CancellationToken ct)
    {
        var unmatched = txns.Where(t => t.Status == "Unmatched" && !string.IsNullOrWhiteSpace(t.Reference)).ToList();
        if (unmatched.Count == 0) return 0;

        var proofs = await PendingProofsAsync(ct);
        int count = 0;
        foreach (var t in unmatched)
        {
            var normRef = NormalizeRef(t.Reference)!;
            var hits = proofs.Where(p => NormalizeRef(p.UpiReference) == normRef && p.Amount == t.Amount).ToList();
            if (hits.Count == 1)
            {
                await LinkAndApproveAsync(t, hits[0], ct);
                proofs.Remove(hits[0]);
                count++;
            }
        }
        return count;
    }

    private List<MatchCandidateDto> Candidates(BankTransaction t, IEnumerable<PaymentProof> proofs)
    {
        var normRef = NormalizeRef(t.Reference);
        var result = new List<MatchCandidateDto>();
        foreach (var p in proofs)
        {
            string? confidence = null, reason = null;
            if (normRef is not null && NormalizeRef(p.UpiReference) == normRef)
            {
                confidence = "Exact";
                reason = "UTR reference matches";
            }
            else if (p.Amount == t.Amount && Math.Abs((p.SubmittedAt.Date - t.TxnDate.Date).TotalDays) <= DateWindowDays)
            {
                confidence = "Likely";
                reason = $"Amount matches, submitted within {DateWindowDays} days";
            }
            if (confidence is null || reason is null) continue;
            result.Add(new MatchCandidateDto(
                p.Id, p.CollectionId,
                p.Collection is not null ? PaymentNumbering.InvoiceNumber(p.Collection) : $"INV{p.CollectionId:D6}",
                p.Collection is not null ? PaymentNumbering.BillingLabel(p.Collection.Month, p.Collection.Year) : "",
                p.Member?.Flat, p.Member?.Name, p.Amount, p.UpiReference, confidence, reason));
        }
        // Exact matches first, then likely.
        return result.OrderBy(c => c.Confidence == "Exact" ? 0 : 1).ToList();
    }

    // ── Confirm / Ignore ─────────────────────────────────────────────────────
    public async Task<bool> ConfirmAsync(int bankTxnId, int proofId, CancellationToken ct = default)
    {
        var txn = await db.BankTransactions.FirstOrDefaultAsync(t => t.Id == bankTxnId, ct);
        if (txn is null || txn.Status == "Matched") return false;
        var proof = await db.PaymentProofs.FirstOrDefaultAsync(p => p.Id == proofId, ct);
        if (proof is null) return false;
        await LinkAndApproveAsync(txn, proof, ct);
        await audit.LogAsync("Reconciliation", "Confirm",
            $"Reconciled bank credit #{txn.Id} ({txn.Amount:0.00}) to payment proof #{proof.Id}");
        return true;
    }

    public async Task<bool> IgnoreAsync(int bankTxnId, CancellationToken ct = default)
    {
        var txn = await db.BankTransactions.FirstOrDefaultAsync(t => t.Id == bankTxnId, ct);
        if (txn is null || txn.Status == "Matched") return false;
        txn.Status = "Ignored";
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Reconciliation", "Ignore", $"Ignored bank credit #{txn.Id} ({txn.Amount:0.00})");
        return true;
    }

    private async Task LinkAndApproveAsync(BankTransaction txn, PaymentProof proof, CancellationToken ct)
    {
        if (proof.Status == "Pending")
            await payments.ApproveAsync(proof, proof.ReviewedByUserId ?? proof.SubmittedByUserId, ct);
        txn.Status = "Matched";
        txn.MatchedPaymentProofId = proof.Id;
        txn.MatchedCollectionId = proof.CollectionId;
        await db.SaveChangesAsync(ct);
    }

    // ── Queries ──────────────────────────────────────────────────────────────
    public async Task<ReconciliationSummaryDto> SummaryAsync(CancellationToken ct = default)
    {
        var unmatched = await db.BankTransactions.Where(t => t.Status == "Unmatched").ToListAsync(ct);
        var matched = await db.BankTransactions.CountAsync(t => t.Status == "Matched", ct);
        var pendingProofs = await db.PaymentProofs.CountAsync(p => p.Status == "Pending", ct);
        return new ReconciliationSummaryDto(unmatched.Count, unmatched.Sum(t => t.Amount), matched, pendingProofs);
    }

    public async Task<IEnumerable<BankTransactionDto>> ListAsync(string? status, CancellationToken ct = default)
    {
        var q = db.BankTransactions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
        var ids = await q.OrderByDescending(t => t.TxnDate).ThenByDescending(t => t.Id).Select(t => t.Id).ToListAsync(ct);
        return await BuildDtosAsync(ids, ct);
    }

    private async Task<List<BankTransactionDto>> BuildDtosAsync(List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new List<BankTransactionDto>();
        var txns = await db.BankTransactions.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        txns = ids.Select(id => txns.First(t => t.Id == id)).ToList(); // preserve order
        var proofs = txns.Any(t => t.Status == "Unmatched") ? await PendingProofsAsync(ct) : new List<PaymentProof>();
        return txns.Select(t => new BankTransactionDto(
            t.Id, t.TxnDate, t.Narration, t.Reference, t.Amount, t.Status,
            t.MatchedPaymentProofId, t.MatchedCollectionId,
            t.Status == "Unmatched" ? Candidates(t, proofs) : Enumerable.Empty<MatchCandidateDto>())).ToList();
    }

    private async Task<List<PaymentProof>> PendingProofsAsync(CancellationToken ct) =>
        await db.PaymentProofs
            .Include(p => p.Collection).Include(p => p.Member)
            .Where(p => p.Status == "Pending")
            .ToListAsync(ct);

    // ── Parsing helpers ──────────────────────────────────────────────────────
    private sealed record CreditRow(DateTime Date, string Narration, decimal Amount);
    private sealed record ParsedStatement(int TotalRows, int SkippedDebits, List<CreditRow> Credits);

    private static ParsedStatement ParseCsv(Stream csv)
    {
        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string[]>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            lines.Add(SplitCsvLine(line));
        }
        if (lines.Count == 0) throw new InvalidOperationException("The file appears to be empty.");

        // Find the header row (first row that has a narration + a credit/debit column).
        int headerIdx = -1;
        Dictionary<string, int> idx = new();
        for (int i = 0; i < lines.Count; i++)
        {
            var map = MapHeaders(lines[i]);
            if (map.ContainsKey("narration") && (map.ContainsKey("credit") || map.ContainsKey("debit")))
            {
                headerIdx = i; idx = map; break;
            }
        }
        if (headerIdx < 0)
            throw new InvalidOperationException("Could not find recognizable columns (narration + deposit/withdrawal). Please upload a bank statement CSV.");

        int total = 0, skippedDebits = 0;
        var credits = new List<CreditRow>();
        for (int i = headerIdx + 1; i < lines.Count; i++)
        {
            var cells = lines[i];
            string Get(string key) => idx.TryGetValue(key, out var c) && c < cells.Length ? cells[c].Trim() : "";
            var narration = Get("narration");
            var debit = ToDecimal(Get("debit"));
            var credit = ToDecimal(Get("credit"));
            if (string.IsNullOrWhiteSpace(narration) && credit == 0 && debit == 0) continue;
            total++;
            if (credit <= 0) { if (debit > 0) skippedDebits++; continue; }
            var date = ParseDate(Get("date")) ?? DateTime.UtcNow.Date;
            credits.Add(new CreditRow(date, narration, credit));
        }
        return new ParsedStatement(total, skippedDebits, credits);
    }

    private static Dictionary<string, int> MapHeaders(string[] header)
    {
        var map = new Dictionary<string, int>();
        for (int c = 0; c < header.Length; c++)
        {
            var h = header[c].Trim().ToLowerInvariant();
            if (h.Length == 0) continue;
            if (!map.ContainsKey("date") && DateHeaders.Contains(h)) map["date"] = c;
            else if (!map.ContainsKey("narration") && NarrationHeaders.Contains(h)) map["narration"] = c;
            else if (!map.ContainsKey("credit") && CreditHeaders.Contains(h)) map["credit"] = c;
            else if (!map.ContainsKey("debit") && DebitHeaders.Contains(h)) map["debit"] = c;
        }
        return map;
    }

    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    private static readonly Regex TwelveDigit = new(@"\b\d{12}\b", RegexOptions.Compiled);
    private static readonly Regex LongDigit = new(@"\b\d{9,22}\b", RegexOptions.Compiled);

    private static string? ExtractReference(string narration)
    {
        if (string.IsNullOrWhiteSpace(narration)) return null;
        var m = TwelveDigit.Match(narration);
        if (m.Success) return m.Value;
        m = LongDigit.Match(narration);
        return m.Success ? m.Value : null;
    }

    private static string? NormalizeRef(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var cleaned = new string(reference.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string FallbackKey(DateTime date, decimal amount, string narration) =>
        $"{date:yyyyMMdd}|{amount:0.00}|{(narration ?? "").Trim().ToLowerInvariant()}";

    private static decimal ToDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        var t = value.Replace(",", "").Replace("\u20b9", "").Replace("INR", "", StringComparison.OrdinalIgnoreCase).Trim();
        return decimal.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string[] formats = { "dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yy", "dd-MMM-yyyy", "dd-MMM-yy", "d/M/yyyy" };
        if (DateTime.TryParseExact(value.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)) return d;
        return DateTime.TryParse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out d) ? d : null;
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}

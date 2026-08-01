"""
icici_pdf_to_csv.py — Convert an ICICI 'OpTransactionHistory' PDF statement into
a CSV that the SMMS Bank-Statement Reconciliation importer can read.

The importer only accepts CSV. This script reads the PDF, uses the column
x-coordinates to correctly separate Withdrawal (debit) vs Deposit (credit)
amounts, stitches the wrapped UPI remark lines back together (so the 12-digit
UTR is preserved), and writes columns the importer recognises:

    Transaction Date, Transaction Remarks, Deposit Amount(INR),
    Withdrawal Amount(INR), Balance(INR)

Usage:
    python tools/icici_pdf_to_csv.py --pdf data/OpTransactionHistory01-08-2026.pdf \
        --out data/OpTransactionHistory01-08-2026.csv
"""
import argparse
import csv
import re
from pathlib import Path

import pdfplumber

AMOUNT_RE = re.compile(r"^[\d,]+\.\d{2}$")
ANCHOR_SNO_MAX_X = 60      # S.No sits far left
LEFT_COL_MAX_X = 395       # anything left of the amount columns is remarks/date/sno


def col_of(x1: float) -> str:
    """Classify an amount word by the right edge (x1) of its column."""
    if 448 <= x1 <= 458:
        return "withdrawal"
    if 514 <= x1 <= 524:
        return "deposit"
    if 566 <= x1 <= 576:
        return "balance"
    return "?"


def parse_pdf(pdf_path: Path):
    rows = []
    with pdfplumber.open(pdf_path) as pdf:
        for page in pdf.pages:
            words = page.extract_words(use_text_flow=False)
            # Anchor = a line beginning with an integer S.No followed by a dd.mm.yyyy date.
            anchors = []
            for w in words:
                if w["x0"] < ANCHOR_SNO_MAX_X and w["text"].isdigit():
                    # Is there a date word on the same visual line?
                    same_line = [d for d in words
                                 if abs(d["top"] - w["top"]) < 4
                                 and re.fullmatch(r"\d{2}\.\d{2}\.\d{4}", d["text"])]
                    if same_line:
                        anchors.append((w, same_line[0]))
            anchors.sort(key=lambda a: a[0]["top"])

            for i, (sno_w, date_w) in enumerate(anchors):
                top = sno_w["top"]
                next_top = anchors[i + 1][0]["top"] if i + 1 < len(anchors) else 10_000
                line_words = [w for w in words if abs(w["top"] - top) < 4]

                amounts = {"withdrawal": None, "deposit": None, "balance": None}
                for w in line_words:
                    if AMOUNT_RE.fullmatch(w["text"]):
                        c = col_of(w["x1"])
                        if c in amounts:
                            amounts[c] = w["text"].replace(",", "")

                # Remarks = left-column words strictly below this anchor, above the next.
                remark_words = [w for w in words
                                if top < w["top"] < next_top and w["x1"] < LEFT_COL_MAX_X]
                remark_words.sort(key=lambda w: (round(w["top"]), w["x0"]))
                narration = " ".join(w["text"] for w in remark_words)

                rows.append({
                    "date": date_w["text"].replace(".", "/"),
                    "narration": narration,
                    "deposit": amounts["deposit"] or "",
                    "withdrawal": amounts["withdrawal"] or "",
                    "balance": amounts["balance"] or "",
                })
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pdf", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    rows = parse_pdf(Path(args.pdf))
    with open(args.out, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["Transaction Date", "Transaction Remarks",
                    "Deposit Amount(INR)", "Withdrawal Amount(INR)", "Balance(INR)"])
        for r in rows:
            w.writerow([r["date"], r["narration"], r["deposit"], r["withdrawal"], r["balance"]])

    credits = sum(1 for r in rows if r["deposit"])
    debits = sum(1 for r in rows if r["withdrawal"])
    print(f"Wrote {len(rows)} rows -> {args.out}  (credits={credits}, debits={debits})")
    for r in rows:
        kind = "CR" if r["deposit"] else "DR"
        amt = r["deposit"] or r["withdrawal"]
        print(f"  {r['date']}  {kind} {amt:>10}  {r['narration'][:70]}")


if __name__ == "__main__":
    main()

"""
bank_statement_to_smms.py

Converts an ICICI (or similar) internet-banking transaction history export
into an updated SMMS master workbook - i.e. the same 6-sheet format used by
SMMS_BankImport_v5_fixed.html (Members, Collections, Expenses, Settings,
Users, AuditLog) - so it can be opened directly with "Open Excel" in the app,
with no manual re-typing of transactions.

USAGE
-----
    python tools/bank_statement_to_smms.py ^
        --bank data/OpTransactionHistory24-06-2026.xlsx ^
        --master data/SMMS_Master_Data.xlsx ^
        --output data/SMMS_Master_Data_Updated.xlsx

If --bank / --master / --output are omitted, sensible defaults are used
(see argparse section below) pointing at the project's data/ folder. The bank statement file may be left open in
Excel - the script will transparently work off a temp copy if it can't get
a direct read lock.

WHAT IT DOES
------------
1. Reads the bank statement's single transaction sheet (columns similar to
   'S No.', 'Value Date', 'Transaction Date', 'Cheque Number',
   'Transaction Remarks', 'Withdrawal Amount(INR)', 'Deposit Amount(INR)',
   'Balance(INR)'). Wrapped-text continuation rows (blank S No. with only
   more Remarks text) are stitched back onto the previous transaction.
2. Deposits (credit) become Collections rows:
     - Tries to find an explicit flat number (e.g. "Flat 403 J", "203
       mainta", bare "/401/") in the remarks and matches it against the
       known flats from the Members sheet -> confident match.
     - If no flat number is found, the payer name is kept and the row is
       flagged with "VERIFY FLAT" so nothing is silently mis-assigned to
       the wrong flat. All money is still captured in the ledger.
     - Detects an explicit month name in the remarks (e.g. "March mont",
       "May month") so backlog payments are booked to the month they were
       actually meant for rather than the transaction date's month.
3. Withdrawals (debit) become Expenses rows:
     - Vendor/payee name is extracted from the remarks.
     - Category is guessed via keyword rules (water, electricity,
       watchman salary, CCTV, repairs, garbage, etc.), defaulting to
       'Society expenses'.
4. Skips rows that look like duplicates of transactions already present in
   the master file (same amount + date + matched flat/vendor), so the
   script is safe to re-run on overlapping statements.
5. Appends everything to copies of the existing Members/Collections/
   Expenses/Settings/Users/AuditLog sheets (nothing existing is deleted or
   overwritten) and writes the result to --output. An AuditLog row records
   the import.
6. Prints a console summary, including a clear "NEEDS REVIEW" list for any
   collection row where the flat could not be confidently determined -
   just open the output file and fill in the Flat/Floor columns for those
   rows.
"""

import argparse
import re
import shutil
import sys
import tempfile
from datetime import datetime
from pathlib import Path

import openpyxl

MONTHS = ['', 'January', 'February', 'March', 'April', 'May', 'June', 'July',
          'August', 'September', 'October', 'November', 'December']

# Project root is the parent of this tools/ folder; data files live in data/.
DATA_DIR = Path(__file__).resolve().parent.parent / 'data'

MONTH_ALIASES = {
    'january': 1, 'jan': 1, 'february': 2, 'feb': 2, 'march': 3, 'mar': 3,
    'april': 4, 'apr': 4, 'may': 5, 'june': 6, 'jun': 6, 'july': 7, 'jul': 7,
    'august': 8, 'aug': 8, 'september': 9, 'sept': 9, 'sep': 9,
    'october': 10, 'oct': 10, 'november': 11, 'nov': 11,
    'december': 12, 'dec': 12,
}
# Sort longest-first so "september" matches before "sep" would confuse things.
MONTH_ALIAS_ITEMS = sorted(MONTH_ALIASES.items(), key=lambda kv: -len(kv[0]))

CATEGORY_RULES = [
    (re.compile(r'watchman|security\s*guard|\bsalary\b', re.I), 'Watchman Salary'),
    (re.compile(r'electric|tgspdcl|power\s*bill', re.I), 'Electricity Bill'),
    (re.compile(r'water\s*tank|manjeera|majeera|hmwssb|water\s*bill', re.I), 'Water Bill'),
    (re.compile(r'garbage', re.I), 'Garbage'),
    (re.compile(r'cctv|camera', re.I), 'CCTV Camera'),
    (re.compile(r'bore|plumber|softener|labour|labor|repair', re.I), 'Repairs'),
    (re.compile(r'garden|landscap|plant', re.I), 'Gardening'),
    (re.compile(r'lift|elevator', re.I), 'Lift Maintenance'),
    (re.compile(r'festival|diwali|holi|\bpuja\b|navratri', re.I), 'Festival'),
]
DEFAULT_EXPENSE_CATEGORY = 'Society expenses'

BANK_COL_ALIASES = {
    'sno': ['S No.', 'S No', 'SNo', 'Sr No', 'Sr. No.'],
    'date': ['Transaction Date', 'Value Date', 'Date', 'Txn Date'],
    'remarks': ['Transaction Remarks', 'Transaction Description', 'Description',
                'Narration', 'Particulars', 'Remarks'],
    'withdrawal': ['Withdrawal Amount(INR)', 'Withdrawal Amount', 'Debit Amount', 'Debit'],
    'deposit': ['Deposit Amount(INR)', 'Deposit Amount', 'Credit Amount', 'Credit'],
}


# --------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------
def open_workbook_safely(path):
    """Open an xlsx even if it's currently locked open in Excel."""
    try:
        return openpyxl.load_workbook(path, data_only=True)
    except PermissionError:
        tmp = Path(tempfile.gettempdir()) / f"_smms_readcopy_{Path(path).name}"
        shutil.copy2(path, tmp)
        return openpyxl.load_workbook(tmp, data_only=True)


def to_float(value):
    if value is None:
        return 0.0
    if isinstance(value, (int, float)):
        return float(value)
    text = str(value).replace(',', '').replace('₹', '').strip()
    if not text:
        return 0.0
    try:
        return float(text)
    except ValueError:
        return 0.0


def parse_date(value):
    if value is None:
        return None
    if isinstance(value, datetime):
        return value
    text = str(value).strip()
    for fmt in ('%d/%m/%Y', '%d-%m-%Y', '%Y-%m-%d', '%m/%d/%Y'):
        try:
            return datetime.strptime(text, fmt)
        except ValueError:
            continue
    return None


def build_col_index(header, aliases):
    lower = {str(h).strip().lower(): i for i, h in enumerate(header) if h is not None}
    result = {}
    for key, names in aliases.items():
        for name in names:
            n = name.strip().lower()
            if n in lower:
                result[key] = lower[n]
                break
    return result


def sheet_to_dicts(ws):
    rows = list(ws.iter_rows(values_only=True))
    if not rows:
        return [], []
    header = [str(h).strip() if h is not None else '' for h in rows[0]]
    data = []
    for r in rows[1:]:
        if all(v is None for v in r):
            continue
        data.append(dict(zip(header, r)))
    return header, data


def next_id(rows, key='id', start=1):
    max_id = start - 1
    for r in rows:
        v = r.get(key)
        try:
            v = int(v)
        except (TypeError, ValueError):
            continue
        max_id = max(max_id, v)
    return max_id + 1


# --------------------------------------------------------------------------
# Bank statement parsing
# --------------------------------------------------------------------------
def parse_bank_statement(path):
    wb = open_workbook_safely(path)
    ws = wb[wb.sheetnames[0]]
    rows = list(ws.iter_rows(values_only=True))
    if not rows:
        raise ValueError('Bank statement sheet is empty.')

    header = [h for h in rows[0]]
    idx = build_col_index(header, BANK_COL_ALIASES)
    for required in ('remarks', 'withdrawal', 'deposit'):
        if required not in idx:
            raise ValueError(
                f"Could not find a '{required}' column in the bank statement. "
                f"Header found: {header}"
            )

    txns = []
    current = None
    for row in rows[1:]:
        if all(v is None for v in row):
            continue
        sno = row[idx['sno']] if 'sno' in idx else None
        remarks_piece = row[idx['remarks']] if idx.get('remarks') is not None else None

        if sno in (None, ''):
            # Wrapped-text continuation of the previous transaction's remarks.
            if current is not None and remarks_piece:
                current['remarks'] = (current['remarks'] or '') + str(remarks_piece)
            continue

        date_val = row[idx['date']] if 'date' in idx else None
        withdrawal = row[idx['withdrawal']]
        deposit = row[idx['deposit']]
        current = {
            'sno': sno,
            'date': parse_date(date_val),
            'raw_date': date_val,
            'remarks': str(remarks_piece or '').strip(),
            'withdrawal': to_float(withdrawal),
            'deposit': to_float(deposit),
        }
        txns.append(current)
    return txns


def extract_flat(remarks, known_flats):
    m = re.search(r'flat\s*(?:no\.?|#|:)?\s*(\d{3})', remarks, re.I)
    if m and m.group(1) in known_flats:
        return m.group(1)
    for m in re.finditer(r'(?<!\d)(\d{3})(?!\d)', remarks):
        if m.group(1) in known_flats:
            return m.group(1)
    return None


def extract_payer_name(remarks):
    parts = remarks.split('/')
    if len(parts) > 1 and parts[1].strip():
        return parts[1].strip().title()
    return 'Unknown'


def extract_month_override(remarks, txn_dt):
    text = remarks.lower()
    for name, num in MONTH_ALIAS_ITEMS:
        if re.search(r'\b' + re.escape(name), text):
            year = txn_dt.year if txn_dt else datetime.now().year
            if txn_dt and num > txn_dt.month:
                year -= 1
            return num, year
    return None


def detect_category(remarks):
    for pattern, label in CATEGORY_RULES:
        if pattern.search(remarks):
            return label
    return DEFAULT_EXPENSE_CATEGORY


def detect_payment_mode(remarks):
    if remarks.upper().startswith('UPI'):
        return 'UPI'
    if 'NEFT' in remarks.upper():
        return 'NEFT'
    if 'IMPS' in remarks.upper():
        return 'IMPS'
    return 'Bank Transfer'


# --------------------------------------------------------------------------
# Main conversion
# --------------------------------------------------------------------------
def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--bank', default=str(DATA_DIR / 'OpTransactionHistory24-06-2026.xlsx'),
                         help='Path to the raw bank statement export (xlsx).')
    parser.add_argument('--master', default=str(DATA_DIR / 'SMMS_Master_Data.xlsx'),
                         help='Path to the existing SMMS master workbook.')
    parser.add_argument('--output', default=None,
                         help='Path for the generated workbook (default: <master>_Updated.xlsx).')
    args = parser.parse_args()

    bank_path = Path(args.bank)
    master_path = Path(args.master)
    if not bank_path.exists():
        sys.exit(f"Bank statement not found: {bank_path}")
    if not master_path.exists():
        sys.exit(f"Master data file not found: {master_path}")

    output_path = Path(args.output) if args.output else master_path.with_name(
        master_path.stem + '_Updated' + master_path.suffix
    )

    master_wb = open_workbook_safely(master_path)
    sheet_names = master_wb.sheetnames

    members_header, members = sheet_to_dicts(master_wb['Members']) if 'Members' in sheet_names else ([], [])
    col_header, collections = sheet_to_dicts(master_wb['Collections']) if 'Collections' in sheet_names else ([], [])
    exp_header, expenses = sheet_to_dicts(master_wb['Expenses']) if 'Expenses' in sheet_names else ([], [])
    set_header, settings = sheet_to_dicts(master_wb['Settings']) if 'Settings' in sheet_names else ([], [])
    usr_header, users = sheet_to_dicts(master_wb['Users']) if 'Users' in sheet_names else ([], [])
    audit_header, audit_log = sheet_to_dicts(master_wb['AuditLog']) if 'AuditLog' in sheet_names else ([], [])

    if not col_header:
        col_header = ['id', 'memberId', 'memberName', 'flat', 'floor', 'year', 'month',
                      'monthNum', 'status', 'amount', 'due', 'paymentDate', 'paymentMode', 'remarks']
    if not exp_header:
        exp_header = ['id', 'expenseDate', 'category', 'description', 'vendor', 'amount',
                      'paymentMode', 'month', 'monthNum', 'year', 'remarks']
    if not audit_header:
        audit_header = ['id', 'timestamp', 'user', 'module', 'action', 'details']

    known_flats = {str(m.get('flat', '')).strip() for m in members if str(m.get('flat', '')).strip()}
    flat_to_member = {}
    for m in members:
        flat = str(m.get('flat', '')).strip()
        if flat:
            flat_to_member[flat] = m

    def norm_remarks(text):
        # Strip our own 'Bank Import:' / review prefixes so re-runs on the
        # same statement still recognise rows already imported previously.
        text = str(text or '').strip().lower()
        text = re.sub(r'^\u26a0 verify flat - bank import:\s*', '', text)
        text = re.sub(r'^bank import:\s*', '', text)
        return text

    existing_collection_keys = {
        (str(c.get('paymentDate', '')).strip(), round(to_float(c.get('amount')), 2), norm_remarks(c.get('remarks')))
        for c in collections
    }
    existing_expense_keys = {
        (str(e.get('expenseDate', '')).strip(), round(to_float(e.get('amount')), 2), norm_remarks(e.get('remarks')))
        for e in expenses
    }

    txns = parse_bank_statement(bank_path)

    col_id = next_id(collections)
    exp_id = next_id(expenses)

    new_collections = []
    new_expenses = []
    review_needed = []
    skipped_duplicates = 0

    for t in txns:
        remarks = t['remarks']
        txn_dt = t['date']
        date_str = txn_dt.strftime('%d-%m-%Y') if txn_dt else str(t['raw_date'] or '')
        payment_mode = detect_payment_mode(remarks)

        if t['deposit'] and not t['withdrawal']:
            flat = extract_flat(remarks, known_flats)
            month_override = extract_month_override(remarks, txn_dt)
            if month_override:
                month_num, year = month_override
            elif txn_dt:
                month_num, year = txn_dt.month, txn_dt.year
            else:
                month_num, year = 0, datetime.now().year

            payer_name = extract_payer_name(remarks)

            if flat:
                member = flat_to_member.get(flat, {})
                member_id = member.get('id', '')
                member_name = member.get('name', payer_name)
                floor = member.get('floor', flat[0] if flat else '')
                note = f"Bank Import: {remarks}"
                confident = True
            else:
                member_id = ''
                member_name = payer_name
                floor = ''
                note = f"\u26a0 VERIFY FLAT - Bank Import: {remarks}"
                confident = False

            dedupe_key = (date_str, round(t['deposit'], 2), norm_remarks(remarks))
            if dedupe_key in existing_collection_keys:
                skipped_duplicates += 1
                continue
            existing_collection_keys.add(dedupe_key)

            row = {
                'id': col_id,
                'memberId': member_id,
                'memberName': member_name,
                'flat': flat or '',
                'floor': floor,
                'year': year,
                'month': MONTHS[month_num] if month_num else '',
                'monthNum': month_num,
                'status': 'Paid',
                'amount': t['deposit'],
                'due': 0,
                'paymentDate': date_str,
                'paymentMode': payment_mode,
                'remarks': note,
            }
            new_collections.append(row)
            col_id += 1

            if not confident:
                review_needed.append({
                    'sno': t['sno'], 'date': date_str, 'amount': t['deposit'],
                    'payer': payer_name, 'remarks': remarks,
                })

        elif t['withdrawal'] and not t['deposit']:
            vendor = extract_payer_name(remarks)
            category = detect_category(remarks)
            month_override = extract_month_override(remarks, txn_dt)
            if month_override:
                month_num, year = month_override
            elif txn_dt:
                month_num, year = txn_dt.month, txn_dt.year
            else:
                month_num, year = 0, datetime.now().year

            dedupe_key = (date_str, round(t['withdrawal'], 2), norm_remarks(remarks))
            if dedupe_key in existing_expense_keys:
                skipped_duplicates += 1
                continue
            existing_expense_keys.add(dedupe_key)

            row = {
                'id': exp_id,
                'expenseDate': date_str,
                'category': category,
                'description': remarks,
                'vendor': vendor,
                'amount': t['withdrawal'],
                'paymentMode': payment_mode,
                'month': MONTHS[month_num] if month_num else '',
                'monthNum': month_num,
                'year': year,
                'remarks': f"Bank Import: {remarks}",
            }
            new_expenses.append(row)
            exp_id += 1

        else:
            # Both zero, or both non-zero (ambiguous) - skip but report.
            review_needed.append({
                'sno': t['sno'], 'date': date_str, 'amount': t['deposit'] or t['withdrawal'],
                'payer': 'AMBIGUOUS ROW (both/neither debit & credit set)', 'remarks': remarks,
            })

    # -- Write output workbook -------------------------------------------
    out_wb = openpyxl.Workbook()
    out_wb.remove(out_wb.active)

    def write_sheet(name, header, rows):
        ws = out_wb.create_sheet(name)
        ws.append(header)
        for r in rows:
            ws.append([r.get(h, '') for h in header])

    write_sheet('Members', members_header or ['id', 'name', 'flat', 'floor', 'mobile', 'email', 'status'], members)
    write_sheet('Collections', col_header, collections + new_collections)
    write_sheet('Expenses', exp_header, expenses + new_expenses)
    if set_header:
        write_sheet('Settings', set_header, settings)
    if usr_header:
        write_sheet('Users', usr_header, users)

    audit_id = next_id(audit_log, start=1) if audit_log else 1
    try:
        audit_id = max(audit_id, int(datetime.now().timestamp() * 1000))
    except Exception:
        pass
    new_audit_row = {
        'id': audit_id,
        'timestamp': datetime.now().strftime('%d/%m/%Y, %I:%M:%S %p').lower(),
        'user': 'Admin',
        'module': 'Bank Import',
        'action': 'Import',
        'details': (f"Imported {len(new_collections)} collections and {len(new_expenses)} expenses "
                    f"from {bank_path.name} ({skipped_duplicates} duplicate rows skipped)."),
    }
    write_sheet('AuditLog', audit_header, audit_log + [new_audit_row])

    out_wb.save(output_path)

    # -- Console summary ----------------------------------------------------
    total_credit = sum(r['amount'] for r in new_collections)
    total_debit = sum(r['amount'] for r in new_expenses)
    print('=' * 70)
    print(f"Bank statement : {bank_path}")
    print(f"Master data    : {master_path}")
    print(f"Output written : {output_path}")
    print('-' * 70)
    print(f"New collections added : {len(new_collections)}  (Total ₹{total_credit:,.2f})")
    print(f"New expenses added    : {len(new_expenses)}  (Total ₹{total_debit:,.2f})")
    print(f"Duplicate rows skipped: {skipped_duplicates}")
    print('-' * 70)
    if review_needed:
        print(f"NEEDS REVIEW ({len(review_needed)} row(s)) - flat/member could not be confidently matched:")
        for r in review_needed:
            print(f"  S.No {r['sno']} | {r['date']} | ₹{r['amount']} | {r['payer']}")
            print(f"      remarks: {r['remarks']}")
        print("  -> Open the output file, Collections sheet, and fill in the Flat/Floor/MemberId")
        print("     columns for rows whose Remarks start with '⚠ VERIFY FLAT'.")
    else:
        print("All collection rows were matched to a known flat. Nothing needs manual review.")
    print('=' * 70)


if __name__ == '__main__':
    main()

from pathlib import Path
import re
p = Path('SMMS_Master_Data.xlsx')
raw = p.read_bytes()
text = ''.join(chr(b) if 32 <= b < 127 else ' ' for b in raw)
keywords = ['Members','Collections','Expenses','AuditLog','Settings','MemberId','MemberName','Flat','Floor','Amount','PaymentDate','ExpenseDate','Category','Description','Vendor','PaymentMode','Remarks']
print('found keywords:')
for kw in keywords:
    if kw in text:
        print(kw)
print('--- snippets ---')
for m in re.finditer(r'([A-Za-z0-9_ ]{4,})', text):
    s = m.group(1)
    if any(kw in s for kw in keywords):
        print(repr(s))
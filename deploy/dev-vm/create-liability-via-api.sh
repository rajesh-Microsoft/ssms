#!/bin/bash
# Creates a society liability via the real API (so SocietyLiabilityService.Create runs and books
# the paired funded expense + audit entry). Secrets are read from the container env, never echoed.
set -e
TENANT="${1:?tenant key required}"
MEMBER_ID="${2:?member id required}"
AMOUNT="${3:?amount required}"
CATEGORY="${4:?category required}"
PURPOSE="${5:?purpose required}"
LDATE="${6:?date required (YYYY-MM-DD)}"

SUPER_USER=$(docker exec smms-api printenv ControlPlane__SuperAdmin__Username)
SUPER_PW=$(docker exec smms-api printenv ControlPlane__SuperAdmin__Password)

jget() { python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('$1',''))"; }

LOGIN_BODY=$(U="$SUPER_USER" P="$SUPER_PW" python3 -c "import json,os;print(json.dumps({'username':os.environ['U'],'password':os.environ['P']}))")
PLAT=$(curl -sS -k -X POST https://admin.yuvaansoft.shop/api/platform/auth/login \
  -H 'Content-Type: application/json' \
  -d "$LOGIN_BODY" | jget token)
[ -n "$PLAT" ] || { echo "platform login FAILED"; exit 1; }
echo "platform login OK"

IMP=$(curl -sS -k -X POST "https://admin.yuvaansoft.shop/api/platform/societies/$TENANT/impersonate" \
  -H "Authorization: Bearer $PLAT" | jget token)
[ -n "$IMP" ] || { echo "impersonate FAILED"; exit 1; }
echo "impersonate $TENANT OK"

BODY=$(M="$MEMBER_ID" D="$LDATE" A="$AMOUNT" C="$CATEGORY" R="$PURPOSE" python3 -c "
import json,os
print(json.dumps({
 'source':'MemberContribution',
 'memberId':int(os.environ['M']),
 'contributorName':None,
 'date':os.environ['D']+'T00:00:00Z',
 'amount':float(os.environ['A']),
 'category':os.environ['C'],
 'purpose':os.environ['R'],
}))")

echo "POST body: $BODY"
echo "--- response ---"
curl -sS -k -X POST "https://$TENANT.yuvaansoft.shop/api/society-liabilities" \
  -H "Authorization: Bearer $IMP" \
  -H 'Content-Type: application/json' \
  -d "$BODY" -w '\nHTTP %{http_code}\n'
echo done

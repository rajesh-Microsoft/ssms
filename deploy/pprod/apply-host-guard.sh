#!/bin/bash
# Adds the look-alike dev host guard to pre-prod nginx.
# Backs up first, refuses to reload a config that does not pass nginx -t, and
# restores the backup if it does not.
set -u
CONF=/opt/smms/app/deploy/pprod/nginx.pprod.conf
STAMP=$(date +%Y%m%dT%H%M%SZ)

[ -f "$CONF" ] || { echo "ABORT: $CONF not found"; exit 1; }

if grep -q 'server_name ~\^dev-' "$CONF" && docker exec smms-pprod-ui grep -q 'server_name ~\^dev-' /etc/nginx/conf.d/default.conf; then
  echo "Guard already present and live. Nothing to do."
  echo "__GUARD_OK__"; exit 0
fi

# A previous run may have left the guard on disk without the container seeing it.
if grep -q 'server_name ~\^dev-' "$CONF"; then
  echo "Guard is on disk but not live; rewriting in place."
  cat "$CONF" > /tmp/conf.keep
  cat /tmp/conf.keep > "$CONF"
  rm -f /tmp/conf.keep
  docker exec smms-pprod-ui nginx -t && docker exec smms-pprod-ui nginx -s reload && echo "reloaded"
  echo "__GUARD_OK__"; exit 0
fi

cp "$CONF" "$CONF.bak-$STAMP"
echo "backup: $CONF.bak-$STAMP"

cat > /tmp/guard.conf <<'GUARD'
# ── Look-alike dev hosts (dev-*.ssms.yuvaansoft.shop) ──
# Nothing points a dev-* record at this VM today. The block below is the
# catch-all, so if one ever appeared it would be served this stack's real data
# behind a hostname that reads like development. Refuse the shape outright.
# Regex server_names are tried in order, so this must stay above the catch-all.
server {
    listen 443 ssl;
    server_name ~^dev-.*\.ssms\.yuvaansoft\.shop$;

    ssl_certificate     /etc/letsencrypt/live/ssms.yuvaansoft.shop/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/ssms.yuvaansoft.shop/privkey.pem;

    add_header X-Robots-Tag "noindex, nofollow" always;
    default_type text/plain;

    return 404 "This hostname does not exist.\nPre-production is pprod-<tenant>.ssms.yuvaansoft.shop.\nDevelopment is dev-<tenant>.yuvaansoft.shop on a different server.\n";
}

GUARD

MARKER='# ── Every society subdomain'
grep -q "$MARKER" "$CONF" || { echo "ABORT: anchor comment not found, refusing to guess where to insert"; exit 1; }

awk -v marker="$MARKER" '
  index($0, marker) && !inserted {
    while ((getline line < "/tmp/guard.conf") > 0) print line
    inserted = 1
  }
  { print }
' "$CONF" > /tmp/nginx.pprod.new

# Write THROUGH the existing file. `mv` would give it a new inode and the
# container's single-file bind mount would keep serving the old one.
cat /tmp/nginx.pprod.new > "$CONF"
rm -f /tmp/nginx.pprod.new /tmp/guard.conf
echo "inserted guard block"

if ! docker exec smms-pprod-ui grep -q 'server_name ~\^dev-' /etc/nginx/conf.d/default.conf; then
  cat "$CONF.bak-$STAMP" > "$CONF"
  echo "ABORT: the container cannot see the change, original restored, nothing reloaded"
  exit 1
fi
echo "container sees the new config"

if docker exec smms-pprod-ui nginx -t 2>&1; then
  docker exec smms-pprod-ui nginx -s reload
  echo "reloaded"
else
  cat "$CONF.bak-$STAMP" > "$CONF"
  echo "ABORT: nginx -t failed, original restored, nothing reloaded"
  exit 1
fi

echo "===== VERIFY ====="
R="--resolve pprod-aadya.ssms.yuvaansoft.shop:443:127.0.0.1 --resolve dev-aadya.ssms.yuvaansoft.shop:443:127.0.0.1 --resolve pprod-demo.ssms.yuvaansoft.shop:443:127.0.0.1"
curl -s $R -o /dev/null -w '  dev-aadya.ssms   -> %{http_code} (expect 404)\n' https://dev-aadya.ssms.yuvaansoft.shop/
curl -s $R -o /dev/null -w '  pprod-aadya.ssms -> %{http_code} (expect 200)\n' https://pprod-aadya.ssms.yuvaansoft.shop/
curl -s $R -o /dev/null -w '  pprod-demo.ssms  -> %{http_code} (expect 200)\n' https://pprod-demo.ssms.yuvaansoft.shop/
echo "  body:"; curl -s $R https://dev-aadya.ssms.yuvaansoft.shop/ | sed 's/^/    /'
echo "  api health:"; curl -s $R -o /dev/null -w '    pprod-aadya /api/health -> %{http_code}\n' https://pprod-aadya.ssms.yuvaansoft.shop/api/health
docker ps --filter name=smms-pprod --format '  {{.Names}} {{.Status}}'
echo "__GUARD_OK__"

#!/bin/bash
# Reports readiness of the two operator-supplied prerequisites. Prints no secrets.
if [ -f /root/.secrets/cloudflare.ini ]; then
  echo "cloudflare.ini: PRESENT ($(stat -c %a /root/.secrets/cloudflare.ini))"
  grep -q 'dns_cloudflare_api_token' /root/.secrets/cloudflare.ini && echo "token line: PRESENT" || echo "token line: MISSING"
  grep -q 'PASTE_YOUR_TOKEN_HERE' /root/.secrets/cloudflare.ini && echo "token value: STILL PLACEHOLDER" || echo "token value: looks set"
else
  echo "cloudflare.ini: MISSING"
fi
echo "--- cert ---"
if [ -d /etc/letsencrypt/live/ssms.yuvaansoft.shop ]; then certbot certificates 2>/dev/null | grep -E 'Certificate Name|Domains|Expiry'; else echo "no cert yet"; fi
echo "--- containers ---"
docker ps -a --format '{{.Names}} {{.Status}}' || true

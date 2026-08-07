#!/bin/bash
set -euo pipefail

credentials=/root/.secrets/cloudflare.ini
cert_name=ssms.yuvaansoft.shop

test -f "$credentials"
test "$(stat -c %a "$credentials")" = "600"

certbot certonly \
  --dns-cloudflare \
  --dns-cloudflare-credentials "$credentials" \
  --dns-cloudflare-propagation-seconds 30 \
  --non-interactive \
  --agree-tos \
  --email hello@yuvaansoft.shop \
  --cert-name "$cert_name" \
  -d ssms.yuvaansoft.shop \
  -d '*.ssms.yuvaansoft.shop'

test -s "/etc/letsencrypt/live/$cert_name/fullchain.pem"
test -s "/etc/letsencrypt/live/$cert_name/privkey.pem"

cd /opt/smms/app
docker compose config --quiet
docker compose up -d smms-ui

install -d -m 755 /etc/letsencrypt/renewal-hooks/deploy
cat > /etc/letsencrypt/renewal-hooks/deploy/reload-smms-pprod-nginx.sh <<'EOF'
#!/bin/bash
docker exec smms-pprod-ui nginx -s reload
EOF
chmod 755 /etc/letsencrypt/renewal-hooks/deploy/reload-smms-pprod-nginx.sh

echo "--- certificate ---"
certbot certificates 2>/dev/null | grep -E 'Certificate Name|Domains|Expiry Date'
echo "--- containers ---"
docker ps --format '{{.Names}} {{.Status}}'
echo "--- local HTTPS probe ---"
curl --resolve pprod-aadya.ssms.yuvaansoft.shop:443:127.0.0.1 \
  -fsS -o /dev/null -w 'aadya health -> %{http_code}\n' \
  https://pprod-aadya.ssms.yuvaansoft.shop/api/health
#!/bin/bash
# Writes the pre-prod compose + nginx config and generates .env secrets ON the VM,
# so no secret is ever printed to the operator's console or stored on the workstation.
set -euo pipefail
APP=/opt/smms/app
cd "$APP"

mkdir -p deploy/pprod

cat > deploy/pprod/nginx.pprod.conf <<'NGINXEOF'
__NGINX_CONF__
NGINXEOF

cat > docker-compose.yml <<'COMPOSEEOF'
__COMPOSE__
COMPOSEEOF

# Generate secrets once; never regenerate on re-run or the DB password would drift
# from the one baked into the running SQL Server volume.
if [ ! -f .env ]; then
  SA="Pp$(openssl rand -base64 18 | tr -d '/+=' )#7"
  SUPER="$(openssl rand -base64 15 | tr -d '/+=')"
  JWT="$(openssl rand -base64 48 | tr -d '\n')"
  cat > .env <<EOF
PPROD_SA_PASSWORD=$SA
PPROD_SUPERADMIN_PASSWORD=$SUPER
PPROD_JWT_KEY=$JWT
RAZORPAY_ENABLED=false
RAZORPAY_KEY_ID=
RAZORPAY_KEY_SECRET=
RAZORPAY_WEBHOOK_SECRET=
RAZORPAY_ALLOWED_SOCIETIES=
EOF
  chmod 600 .env
  chown ssmsadmin:ssmsadmin .env
  echo ".env CREATED"
else
  echo ".env already exists, left untouched"
fi

echo "--- compose project name ---"
grep -m1 '^name:' docker-compose.yml
echo "--- .env keys (values hidden) ---"
sed 's/=.*/=<set>/' .env
echo "--- validate compose ---"
docker compose config --quiet && echo "compose OK"

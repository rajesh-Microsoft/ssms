#!/bin/bash
# Runs on the pre-prod VM via `az vm run-command` (SSH is blocked by local endpoint security).
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

apt-get update -qq
apt-get install -y -qq ca-certificates curl gnupg lsb-release unzip jq >/dev/null

install -m 0755 -d /etc/apt/keyrings
if [ ! -f /etc/apt/keyrings/docker.asc ]; then
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
fi

echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" \
  > /etc/apt/sources.list.d/docker.list

apt-get update -qq
apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin >/dev/null

systemctl enable --now docker
usermod -aG docker ssmsadmin || true

# certbot with the Cloudflare DNS plugin, for the *.ssms.yuvaansoft.shop wildcard
apt-get install -y -qq certbot python3-certbot-dns-cloudflare >/dev/null

echo "--- versions ---"
docker --version
docker compose version
certbot --version 2>&1 | head -n 1
echo "--- resources ---"
free -m | sed -n 2p
df -h / | tail -n 1

#!/bin/bash
set -euo pipefail

certbot renew \
  --cert-name ssms.yuvaansoft.shop \
  --dry-run \
  --no-random-sleep-on-renew

docker exec smms-pprod-ui nginx -t
echo "Certificate renewal and nginx reload test passed."
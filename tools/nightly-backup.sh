#!/usr/bin/env bash
# Nightly job: back up all SMMS databases, ship the tarball to Azure Blob
# Storage over the private endpoint, then prune old local copies.
set -euo pipefail

# cron runs with a minimal PATH; azcopy lives in /usr/local/bin.
export PATH="/usr/local/bin:/usr/bin:/bin"

# Authenticate to storage as the VM's system-assigned managed identity.
export AZCOPY_AUTO_LOGIN_TYPE=MSI

# Read the SA password from the running container so no second copy is on disk.
SA_PASSWORD="$(sudo docker exec sqlserver printenv SA_PASSWORD)"
export SA_PASSWORD

"${HOME}/backup-smms-dbs.sh"

DEST="https://smmsbackup866121.blob.core.windows.net/sqlbackups/$(date -u +%Y/%m)/"
azcopy copy "${HOME}/smms-backups/*.tar.gz*" "$DEST" \
  --overwrite ifSourceNewer --log-level ERROR

# The VM disk is tight, so keep only three days of tarballs locally.
find "${HOME}/smms-backups" -name '*.tar.gz*' -mtime +3 -delete
azcopy jobs clean --with-status=all >/dev/null

echo "OK $(date -u +%Y-%m-%dT%H:%M:%SZ) -> ${DEST}"

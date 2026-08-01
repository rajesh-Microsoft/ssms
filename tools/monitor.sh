#!/usr/bin/env bash
# Quick VM + Docker health snapshot for SMMS. Run: bash ~/smms/SMMS/tools/monitor.sh
set -euo pipefail

line() { printf '\n\033[1;36m== %s ==\033[0m\n' "$1"; }

line "Uptime / Load"
uptime

line "Memory"
free -h

line "Disk"
df -h / | sed -n '1,2p'

line "Docker containers"
docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'

line "Docker resource usage"
docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}'

line "API health"
curl -fsS http://localhost:7253/api/health || echo "  API /health did NOT respond OK"
echo

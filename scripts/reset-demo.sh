#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ ! -f "$repo_root/.env" ]]; then
  echo "Missing .env." >&2
  exit 1
fi

set -a
# shellcheck disable=SC1091
source "$repo_root/.env"
set +a

base_url="${DEMO_BASE_URL:-http://localhost:${APP_PORT:-8085}}"
curl --fail --silent --show-error \
  --request POST \
  --header "Content-Type: application/json" \
  --header "X-Demo-Admin-Token: ${DEMO_ADMIN_TOKEN}" \
  --data '{"confirmation":"RESET_DEMO"}' \
  "${base_url%/}/api/demo/reset"
echo

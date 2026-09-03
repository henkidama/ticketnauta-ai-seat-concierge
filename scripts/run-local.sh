#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ ! -f "$repo_root/.env" ]]; then
  echo "Missing .env. Copy .env.example to .env and fill in local values." >&2
  exit 1
fi

set -a
# shellcheck disable=SC1091
source "$repo_root/.env"
set +a

export ConnectionStrings__DemoDatabase="Host=${DB_HOST};Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD};SSL Mode=${DB_SSL_MODE};Timeout=10;Command Timeout=30;Pooling=true"
export Demo__AdminToken="$DEMO_ADMIN_TOKEN"
export Demo__HoldMinutes="$HOLD_MINUTES"
export ASPNETCORE_URLS="http://localhost:${APP_PORT}"

dotnet run --project "$repo_root/src/Ticketnauta.WebMcp.Api/Ticketnauta.WebMcp.Api.csproj"

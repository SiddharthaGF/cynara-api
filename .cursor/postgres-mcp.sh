#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
NETWORK="${POSTGRES_DOCKER_NETWORK:-cynara-net}"
HOST_PORT="${POSTGRES_HOST_PORT:-5432}"

if ! docker network inspect "${NETWORK}" >/dev/null 2>&1; then
  echo "Docker network '${NETWORK}' not found. Run 'make up' first." >&2
  exit 1
fi

DB_URI="${POSTGRES_DATABASE_URI:-postgresql://postgres:postgres@postgresql:5432/postgres}"

exec docker run -i --rm \
  --network "${NETWORK}" \
  -e DATABASE_URI="${DB_URI}" \
  crystaldba/postgres-mcp
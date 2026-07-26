#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && Fpwd)"
ENV_FILE="${ROOT}/.env"

if [[ -z "${DATABASE_URL:-}" ]]; then
  if [[ -f "${ENV_FILE}" ]]; then
    # shellcheck disable=SC1090
    source <(grep '^DATABASE_URL=' "${ENV_FILE}" | sed 's/^/export /')
  fi

  if [[ -z "${DATABASE_URL:-}" ]]; then
    echo "Missing DATABASE_URL. Set it in ${ENV_FILE} or as an environment variable." >&2
    echo "See .env.example for a template." >&2
    exit 1
  fi
fi

exec npx -y @modelcontextprotocol/server-postgres "${DATABASE_URL}"

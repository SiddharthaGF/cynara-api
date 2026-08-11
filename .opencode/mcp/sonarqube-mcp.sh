#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
TOKEN_FILE="${SONARQUBE_TOKEN_FILE:-${ROOT}/.sonar/token}"
NETWORK="${SONARQUBE_DOCKER_NETWORK:-cynara-net}"

if [[ -z "${SONARQUBE_TOKEN:-}" ]]; then
  if [[ ! -f "${TOKEN_FILE}" ]]; then
    FALLBACK="${HOME}/.config/sonarqube/token"
    if [[ -f "${FALLBACK}" ]]; then
      TOKEN_FILE="${FALLBACK}"
    else
      echo "Missing SonarQube token. Set SONARQUBE_TOKEN, run make sonar-bootstrap, or create ${TOKEN_FILE}." >&2
      exit 1
    fi
  fi
  SONARQUBE_TOKEN="$(tr -d '[:space:]' <"${TOKEN_FILE}")"
  export SONARQUBE_TOKEN
fi

export SONARQUBE_URL="${SONARQUBE_URL:-http://sonarqube:9000}"
export SONARQUBE_IDE_PORT="${SONARQUBE_IDE_PORT:-64120}"

if ! docker network inspect "${NETWORK}" >/dev/null 2>&1; then
  echo "Docker network '${NETWORK}' not found. Run 'make sonar-up' first." >&2
  exit 1
fi

exec docker run -i --rm \
  --network "${NETWORK}" \
  -e SONARQUBE_TOKEN \
  -e SONARQUBE_URL \
  -e SONARQUBE_IDE_PORT \
  mcp/sonarqube

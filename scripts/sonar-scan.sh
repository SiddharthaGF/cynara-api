#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"

SOLUTION="${SOLUTION:-Cynara.Api.sln}"
CONFIGURATION="${CONFIGURATION:-Debug}"
SONAR_HOST_URL="${SONAR_HOST_URL:-http://localhost:9000}"
SONAR_PROJECT_KEY="${SONAR_PROJECT_KEY:-cynara-api}"
SONAR_PROJECT_NAME="${SONAR_PROJECT_NAME:-Cynara API}"
TOKEN_FILE="${ROOT}/.sonar/token"
SONAR_TOKEN="${SONAR_TOKEN:-}"

if [[ -z "${SONAR_TOKEN}" && -f "${TOKEN_FILE}" ]]; then
  SONAR_TOKEN="$(tr -d '[:space:]' <"${TOKEN_FILE}")"
fi

if [[ -z "${SONAR_TOKEN}" ]]; then
  echo "Missing SONAR_TOKEN. Run 'make sonar-bootstrap' or export SONAR_TOKEN." >&2
  exit 1
fi

status="$(curl -fsS "${SONAR_HOST_URL}/api/system/status" 2>/dev/null \
  | python3 -c "import sys,json; print(json.load(sys.stdin).get('status',''))" \
  2>/dev/null || true)"
if [[ "${status}" != "UP" ]]; then
  echo "SonarQube is not UP at ${SONAR_HOST_URL}. Run 'make sonar-up' first." >&2
  exit 1
fi

dotnet tool restore

dotnet tool run dotnet-sonarscanner -- begin \
  /k:"${SONAR_PROJECT_KEY}" \
  /n:"${SONAR_PROJECT_NAME}" \
  /d:sonar.host.url="${SONAR_HOST_URL}" \
  /d:sonar.token="${SONAR_TOKEN}" \
  /d:sonar.exclusions="**/bin/**,**/obj/**,**/publish/**,**/.sonar/**"

dotnet build "${SOLUTION}" -c "${CONFIGURATION}" --no-incremental --disable-build-servers

dotnet tool run dotnet-sonarscanner -- end /d:sonar.token="${SONAR_TOKEN}"

echo
echo "Analysis uploaded. Open ${SONAR_HOST_URL}/dashboard?id=${SONAR_PROJECT_KEY}"

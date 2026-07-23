#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SONAR_HOST_URL="${SONAR_HOST_URL:-http://localhost:9000}"
SONAR_ADMIN_PASSWORD="${SONAR_ADMIN_PASSWORD:-CynaraSonarAdmin1!}"
SONAR_TOKEN_NAME="${SONAR_TOKEN_NAME:-cynara-api-local}"
SONAR_DIR="${ROOT}/.sonar"
TOKEN_FILE="${SONAR_DIR}/token"

mkdir -p "${SONAR_DIR}"

echo "Waiting for SonarQube at ${SONAR_HOST_URL} ..."
for _ in $(seq 1 90); do
  status="$(curl -fsS "${SONAR_HOST_URL}/api/system/status" 2>/dev/null \
    | python3 -c "import sys,json; print(json.load(sys.stdin).get('status',''))" \
    2>/dev/null || true)"
  if [[ "${status}" == "UP" ]]; then
    break
  fi
  sleep 2
done

if [[ "${status:-}" != "UP" ]]; then
  echo "SonarQube did not become UP. Is 'make sonar-up' running?" >&2
  exit 1
fi

auth_ok() {
  local user="$1"
  local pass="$2"
  curl -fsS -u "${user}:${pass}" \
    "${SONAR_HOST_URL}/api/authentication/validate" \
    | python3 -c "import sys,json; raise SystemExit(0 if json.load(sys.stdin).get('valid') else 1)"
}

ADMIN_PASS=""
if auth_ok admin admin; then
  echo "Changing default admin password..."
  curl -fsS -u admin:admin -X POST \
    "${SONAR_HOST_URL}/api/users/change_password" \
    --data-urlencode "login=admin" \
    --data-urlencode "previousPassword=admin" \
    --data-urlencode "password=${SONAR_ADMIN_PASSWORD}" \
    >/dev/null
  ADMIN_PASS="${SONAR_ADMIN_PASSWORD}"
elif auth_ok admin "${SONAR_ADMIN_PASSWORD}"; then
  ADMIN_PASS="${SONAR_ADMIN_PASSWORD}"
else
  echo "Cannot authenticate as admin. Set SONAR_ADMIN_PASSWORD to the current password." >&2
  exit 1
fi

if [[ -f "${TOKEN_FILE}" && -s "${TOKEN_FILE}" ]]; then
  echo "Token already present at ${TOKEN_FILE}"
else
  echo "Creating analysis token '${SONAR_TOKEN_NAME}'..."
  curl -fsS -u "admin:${ADMIN_PASS}" -X POST \
    "${SONAR_HOST_URL}/api/user_tokens/revoke" \
    --data-urlencode "name=${SONAR_TOKEN_NAME}" \
    >/dev/null 2>&1 || true

  token="$(curl -fsS -u "admin:${ADMIN_PASS}" -X POST \
    "${SONAR_HOST_URL}/api/user_tokens/generate" \
    --data-urlencode "name=${SONAR_TOKEN_NAME}" \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['token'])")"

  umask 077
  printf '%s\n' "${token}" >"${TOKEN_FILE}"
  echo "Wrote ${TOKEN_FILE}"
fi

echo "Configuring local quality gate 'Cynara Local' (no coverage requirement)..."
curl -fsS -u "admin:${ADMIN_PASS}" -X POST \
  "${SONAR_HOST_URL}/api/qualitygates/create" \
  --data-urlencode "name=Cynara Local" \
  >/dev/null 2>&1 || true

python3 - "${SONAR_HOST_URL}" "admin" "${ADMIN_PASS}" <<'PY'
import json, sys, urllib.parse, urllib.request, urllib.error, base64
host, user, password = sys.argv[1:4]
basic = "Basic " + base64.b64encode(f"{user}:{password}".encode()).decode()
gate_name = "Cynara Local"
profile_name = "Cynara C#"
project_key = "cynara-api"
max_file_loc = "400"

def get(path):
    req = urllib.request.Request(host + path, headers={"Authorization": basic})
    with urllib.request.urlopen(req) as response:
        return json.load(response)

def post(path, data):
    body = urllib.parse.urlencode(data).encode()
    req = urllib.request.Request(
        host + path,
        data=body,
        method="POST",
        headers={
            "Authorization": basic,
            "Content-Type": "application/x-www-form-urlencoded",
        },
    )
    try:
        with urllib.request.urlopen(req) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else {}
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()

# --- Quality gate -----------------------------------------------------------
show = get("/api/qualitygates/show?name=" + urllib.parse.quote(gate_name))
for condition in show.get("conditions", []):
    post("/api/qualitygates/delete_condition", {"id": condition["id"]})

for metric, op, error in (
    ("new_violations", "GT", "0"),
    ("new_duplicated_lines_density", "GT", "3"),
):
    post(
        "/api/qualitygates/create_condition",
        {"gateName": gate_name, "metric": metric, "op": op, "error": error},
    )

post(
    "/api/qualitygates/select",
    {"projectKey": project_key, "gateName": gate_name},
)
print("Quality gate assigned:", gate_name)

# --- Quality profile (file size warning via S104) ---------------------------
profiles = get("/api/qualityprofiles/search?language=cs")["profiles"]
by_name = {profile["name"]: profile for profile in profiles}
sonar_way = by_name["Sonar way"]
profile = by_name.get(profile_name)
if profile is None:
    status, copied = post(
        "/api/qualityprofiles/copy",
        {"fromKey": sonar_way["key"], "toName": profile_name},
    )
    if status >= 400:
        raise SystemExit(f"copy profile failed: {status} {copied}")
    profile = get(
        "/api/qualityprofiles/search?language=cs&qualityProfile="
        + urllib.parse.quote(profile_name)
    )["profiles"][0]

status, activated = post(
    "/api/qualityprofiles/activate_rule",
    {
        "key": profile["key"],
        "rule": "csharpsquid:S104",
        "severity": "MINOR",
        "params": f"maximumFileLocThreshold={max_file_loc}",
    },
)
if status >= 400:
    raise SystemExit(f"activate S104 failed: {status} {activated}")

post(
    "/api/qualityprofiles/add_project",
    {
        "language": "cs",
        "qualityProfile": profile_name,
        "project": project_key,
    },
)
print(
    f"Quality profile assigned: {profile_name} "
    f"(S104 maximumFileLocThreshold={max_file_loc}, severity=MINOR)"
)
PY

echo "UI: ${SONAR_HOST_URL}  (admin / ${SONAR_ADMIN_PASSWORD})"

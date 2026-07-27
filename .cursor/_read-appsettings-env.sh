#!/usr/bin/env bash
# Shared helper: load Cynara.Api/appsettings.json (and its environment-specific
# overlays, and the local-only appsettings.Mcp.json when present) as a flat bag
# of environment variables in ASP.NET Core convention (Section__SubKey).
#
# This lets each MCP launcher script pull credentials, URLs, and tokens out of
# the same configuration source the API uses, instead of duplicating them in
# shell literals. Sources read, lowest priority first, so callers can override
# anything on the command line:
#
#   1. appsettings.json (committed, only non-secret defaults)
#   2. appsettings.{ASPNETCORE_ENVIRONMENT}.json (env-specific overrides, committed)
#   3. appsettings.Mcp.json (gitignored, secrets for local MCPs)
#   4. Already-set environment variables (always wins)
#
# Usage from another .sh file:
#   source "$(dirname "$0")/_read-appsettings-env.sh"
#   echo "$ConnectionStrings__Default"
#
# Notes for maintainers:
# - We use `declare -x` rather than `export` because some ASP.NET Core keys
#   contain dots (Logging:LogLevel:Microsoft.AspNetCore) which become dots in
#   the env var name and bash forbids them at parse time. We rewrite each
#   dot to '_' before declaring.
# - Everything must run in the global scope of the source shell, otherwise
#   the variables disappear when the function returns. That is why this file
#   inlines the loop body instead of calling out to a helper function.
set -euo pipefail

readonly CYNARA_MCP_HELPER_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly CYNARA_MCP_HELPER_API_DIR="${CYNARA_MCP_HELPER_SCRIPT_DIR}/../src/Cynara.Api"

# Allow callers to defer (e.g. so they can set ASPNETCORE_ENVIRONMENT first).
if [[ "${CYNARA_MCP_DEFER_LOAD:-0}" == "1" ]]; then
return 0
fi

for _cynara_mcp_file in \
  "${CYNARA_MCP_HELPER_API_DIR}/appsettings.json" \
  "${CYNARA_MCP_HELPER_API_DIR}/appsettings.${ASPNETCORE_ENVIRONMENT:-Production}.json" \
  "${CYNARA_MCP_HELPER_API_DIR}/appsettings.Mcp.json"; do
[[ -f "${_cynara_mcp_file}" ]] || continue
# Emit one `declare -x NAME="value"` line per variable, then eval in the
# *current* shell so the variables land in the caller's environment.
while IFS= read -r _cynara_mcp_line || [[ -n "${_cynara_mcp_line}" ]]; do
[[ -z "${_cynara_mcp_line}" ]] && continue
eval "${_cynara_mcp_line}"
done < <(python3 - "${_cynara_mcp_file}" <<'PY'
import json, shlex, sys

path = sys.argv[1]
try:
    with open(path) as fh:
        data = json.load(fh)
except (OSError, ValueError):
    sys.exit(0)

def flatten(prefix, value):
    if isinstance(value, dict):
        for key, sub in value.items():
            yield from flatten(f"{prefix}__{key}" if prefix else key, sub)
    elif isinstance(value, list):
        for i, item in enumerate(value):
            yield from flatten(f"{prefix}__{i}", item)
    else:
        if isinstance(value, bool):
            yield prefix, "true" if value else "false"
        elif value is None:
            yield prefix, ""
        else:
            yield prefix, str(value)

for name, value in flatten("", data):
    parts = name.split("__")
    if name.startswith("_") or "//" in parts:
        continue
    safe = name.replace('.', '_')
    print(f"declare -x {safe}={shlex.quote(value)}")
PY
)
done

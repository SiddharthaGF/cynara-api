#!/usr/bin/env python3
"""Seed a complex demo form for preview testing."""

from __future__ import annotations

import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

API_BASE = "http://localhost:5080"
SCRIPT_DIR = Path(__file__).resolve().parent
DATA_DIR = SCRIPT_DIR / "seed-data"
ACTOR = "demo-seed"
FORM_CODE = "demo-showcase"
COMPONENT_CODE = "patient-demographics"


def request(method: str, path: str, payload: dict | None = None) -> dict:
    url = f"{API_BASE}{path}"
    data = None
    headers = {
        "Content-Type": "application/json",
        "X-Actor-Id": ACTOR,
    }
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as response:
            body = response.read().decode("utf-8")
            return json.loads(body) if body else {}
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"{method} {path} failed ({error.code}): {detail}") from error


def load_json(path: Path) -> str:
    return json.dumps(json.loads(path.read_text(encoding="utf-8")), separators=(",", ":"))


def ensure_component() -> None:
    try:
        request("GET", f"/api/components/{COMPONENT_CODE}")
        print(f"→ Component '{COMPONENT_CODE}' already exists")
        return
    except RuntimeError as error:
        if "404" not in str(error):
            raise

    print(f"→ Creating component '{COMPONENT_CODE}'")
    request(
        "POST",
        "/api/components",
        {
            "code": COMPONENT_CODE,
            "name": "Datos demográficos del paciente",
            "clinicalSchemaJson": load_json(DATA_DIR / "patient-demographics-clinical.json"),
            "uiSchemaJson": load_json(DATA_DIR / "patient-demographics-ui.json"),
        },
    )
    draft = request("GET", f"/api/components/{COMPONENT_CODE}/draft")
    print(f"→ Publishing component '{COMPONENT_CODE}' (rowVersion={draft['rowVersion']})")
    request(
        "POST",
        f"/api/components/{COMPONENT_CODE}/draft/publish",
        {"rowVersion": draft["rowVersion"]},
    )


def upsert_form() -> None:
    clinical = load_json(DATA_DIR / "demo-showcase-clinical.json")
    ui = load_json(DATA_DIR / "demo-showcase-ui.json")
    rules = load_json(DATA_DIR / "demo-showcase-rules.json")

    try:
        request("GET", f"/api/forms/{FORM_CODE}")
        draft = request("GET", f"/api/forms/{FORM_CODE}/draft")
        print(
            f"→ Updating form '{FORM_CODE}' draft (rowVersion={draft['rowVersion']})"
        )
        request(
            "PUT",
            f"/api/forms/{FORM_CODE}/draft",
            {
                "clinicalSchemaJson": clinical,
                "uiSchemaJson": ui,
                "rulesSchemaJson": rules,
                "rowVersion": draft["rowVersion"],
            },
        )
    except RuntimeError as error:
        if "404" not in str(error):
            raise
        print(f"→ Creating form '{FORM_CODE}'")
        request(
            "POST",
            "/api/forms",
            {
                "code": FORM_CODE,
                "name": "Showcase clínico (preview)",
                "clinicalSchemaJson": clinical,
                "uiSchemaJson": ui,
                "rulesSchemaJson": rules,
            },
        )


def main() -> int:
    print(f"→ Checking API at {API_BASE}")
    request("GET", "/health")
    ensure_component()
    upsert_form()
    print("→ Done. Open:")
    print("  /forms/demo-showcase/designer")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as error:
        print(error, file=sys.stderr)
        raise SystemExit(1) from error

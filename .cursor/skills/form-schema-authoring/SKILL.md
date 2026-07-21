---
name: form-schema-authoring
description:
  'Trigger: form schema, clinical schema, UI schema, rules, condiciones, tipos,
  limites, generar formulario, AI form chat. Author Cynara form triples and
  refuse unsupported features. Canonical copy lives in cynara-api-nest.'
license: Apache-2.0
metadata:
  author: ailuracode
  version: '1.3-pointer'
---

# Form schema authoring (pointer)

**Canonical skill:** `../cynara-api-nest/.cursor/skills/form-schema-authoring/`

This repo keeps only a discovery stub so Cursor/OpenCode still match the
triggers. Do **not** author or edit schema rules from a local copy here.

## Before any form-schema work

1. Read the canonical `SKILL.md` at the path above (absolute if needed:
   `/home/lives/ailuracode/cynara/cynara-api-nest/.cursor/skills/form-schema-authoring/SKILL.md`).
2. Load any `assets/` or `references/` files that skill points to — only from
   that canonical directory.
3. Follow that skill’s hard rules, output contract, and unsupported-features
   list as the single source of truth.

If the sibling repo is missing, stop and tell the user the canonical skill could
not be loaded instead of inventing schema rules.

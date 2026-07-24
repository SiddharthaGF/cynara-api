---
name: form-schema-authoring
description:
  'Trigger: form schema, clinical schema, UI schema, rules, condiciones, tipos,
  limites, generar formulario, AI form chat, validar schema. Author Cynara form
  triples, validate resulting schemas before reply, refuse unsupported features.'
license: Apache-2.0
metadata:
  author: ailuracode
  version: '1.6'
---

## Activation Contract

Author or correct the open form’s clinical+ui+rules triple; refuse engine-impossible
or out-of-scope asks. Coexists with the manual designer — never replace it.

## Hard Rules

- Scope: this form only. No internet/browse/side-effects. Tools only for the
  Pre-response gate when the host provides them.
- **In scope** → answer; keep draft if no edit. **Out of scope / impossible /
  partial** → limitation in `assistantMessage`, **unchanged draft** (offer
  alternative; wait for accept). See `references/unsupported-features.md`.
- Modes: prefer `patch` (minimal); `unchanged` for Q&A/refuse; `replace` only
  for major rebuilds; clear via `patch.clear: true`.
- Layers: clinical = `type`/`code`/constraints; UI = labels/widgets/layout;
  rules = runtime AST. `ui`/`rules`.`clinicalSchemaVersion` ===
  `clinical.schemaVersion`.
- Identity: `id` kebab-case, unique among siblings; `code` unique in clinical.
  UI keys + layout `fieldId` → clinical `id`. Rule `{ref}` → clinical `code`.
- Types only: `text` `textarea` `number` `integer` `boolean` `date` `datetime`
  `time` `choice` `group` `repeater` `component-ref`. Widgets from
  `assets/widget-map.json`. No invented types (`coded-value`, etc.).
- Rules AST only (`ref`|`lit`|`op`+`args`). Ops: `eq` `neq` `gt` `gte` `lt`
  `lte` `and` `or` `not` `empty` `coalesce` `add` `sub` `mul` `div`.
  `calculate` → target `readOnly: true`. Conditionals → `rules.fields[id]`, not
  UI `hidden` (unless permanently hidden).
- Preserve `id`/`code` on edits; never invent `component-ref` without
  `componentCode`.
- **Pre-response gate (blocking):** for mutating `patch`/`replace`, materialize
  the resulting triple and pass `references/validation-checklist.md` (tools →
  `Schemas/v1/*-schema.schema.json` when available). Fail → fix → recheck. Skip
  only for `unchanged` or `patch.clear`.

## Chat voice

Designer language, 1–3 sentences (≤5 for limits). No schema keys, paths,
widgets, AST/ops, or `id`/`code` dumps unless renaming.

## Decision Gates

| Need | Choose |
| --- | --- |
| Short / long text | `text`+`text-input` (`pattern`/length) / `textarea` (length only) |
| Decimal / int | `number`/`integer` + matching input; bounds as needed |
| Yes/no | `boolean` + `checkbox`/`toggle` |
| Date/time | `date`/`datetime`/`time` + picker; optional bounds |
| Options | `choice`; radio ≤5, select >5; multi → checkbox-group/multi-select |
| Nested / repeat | `group` / `repeater` (+ layout) |
| Required / conditional | clinical `required` / `requiredWhen` `visibleWhen` `enabledWhen` |
| Derived / cross-field | `readOnly`+`calculate` / `validations[]` (`code` `message` `assert` [`when`]) |
| Single-field format | clinical constraints — **not** `validations[]` |
| Unsupported | echo draft; `references/unsupported-features.md` |

`validations[].code`: `^[A-Z][A-Z0-9_]{2,63}$`.

## Execution Steps

1. Classify: edit | partial | impossible | out-of-scope.
2. Refuse/partial → unchanged draft + limitation; stop.
3. Build clinical → UI (labels + layout) → rules (allowed ops only).
4. Materialize result; run Pre-response gate; emit only when clean.

## Output Contract

Final answer = **only** a ` ```json ` fenced object (open ` ```json `, close
` ``` `; no prose outside). Prefer `patch`.

Keys: `summary`, `assistantMessage`, `mode`, then `patch` **or**
`clinical`+`ui`+`rules`. No top-level `{ "error": ... }`. Mutating replies must
have passed the Pre-response gate.

## References

- `assets/output-template.json` · `assets/rules-examples.json` ·
  `assets/widget-map.json`
- `references/engine-features.md` · `references/unsupported-features.md` ·
  `references/validation-checklist.md` · `references/docs.md`
- `../../../src/Cynara.Infrastructure/Schemas/v1/{clinical,ui,rules}-schema.schema.json`

# Validation checklist

Fail with stable `code` + JSON Pointer `path` + `message`.

## Pre-response gate

Before any mutating `patch`/`replace` reply (skip `unchanged` / `patch.clear`):

1. Materialize the **resulting** triple (apply patch onto current draft, or use
   replace payloads) — not the patch fragment alone.
2. Structural check vs `Schemas/v1/{clinical,ui,rules}-schema.schema.json`.
3. Run Clinical / UI / Rules rows below.
4. Ops + identity patterns only as listed.
5. Fail → fix → repeat. Never emit a known-invalid triple.

## Clinical

| Code | Check |
| --- | --- |
| `DUPLICATE_FIELD_ID` | `id` unique among siblings |
| `DUPLICATE_FIELD_CODE` | `code` unique in clinical |
| `REPEATER_MIN_MAX_INVALID` | `minItems` ≤ `maxItems` |
| `CHOICE_DEFAULT_NOT_IN_OPTIONS` | `default` ∈ `options[].value` |
| `NUMERIC_MIN_MAX_INVALID` | `minimum` ≤ `maximum` |
| `TEXT_MIN_MAX_INVALID` | `minLength` ≤ `maxLength` |
| `REPEATER_NOT_REPEATABLE` | `repeatable` ≠ `false` |
| `COMPONENT_VERSION_REQUIRED` | only required at publish |

## UI

| Code | Check |
| --- | --- |
| `UNKNOWN_CLINICAL_FIELD` | `fields` keys = clinical `id`s |
| `UNKNOWN_LAYOUT_FIELD` | layout `fieldId` = clinical `id` |
| `LAYOUT_GROUP_CHILD_MISMATCH` | group children = group items |
| `LAYOUT_REPEATER_CHILD_MISMATCH` | repeater `itemTemplate` = repeater items |
| `CLINICAL_VERSION_MISMATCH` | `ui.clinicalSchemaVersion` === clinical |

## Rules

| Code | Check |
| --- | --- |
| `RULE_UNKNOWN_FIELD` | rules `fields` keys = clinical `id`s |
| `RULE_UNKNOWN_FIELD_REF` | `{ref}` = clinical `code` |
| `RULE_CALCULATE_NOT_READONLY` | calculate targets `readOnly` |
| `RULE_SELF_REFERENCE` | no self-ref in calculate |
| `RULE_CYCLIC_DEPENDENCY` | no calculate cycles |
| `RULE_CLINICAL_VERSION_MISMATCH` | rules version === clinical |
| `RULE_DUPLICATE_VALIDATION_CODE` | unique `validations[].code` |

## Ops / identity / where validations live

Ops: `eq` `neq` `gt` `gte` `lt` `lte` · `and` `or` `not` · `empty`
`coalesce` · `add` `sub` `mul` `div`.

- `id`: `^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$` (≤64)
- `code`: `^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$` (≤128)
- `validations[].code`: `^[A-Z][A-Z0-9_]{2,63}$`

| Need | Where |
| --- | --- |
| format/length/required (one field) | clinical field props (`pattern`, etc.) |
| cross-field assert | `rules.validations[]` (`code` `message` `assert` [`when`]) |
| conditional required/visible | `rules.fields[id].requiredWhen` / `visibleWhen` |

Never put `pattern` inside `validations[]` items.

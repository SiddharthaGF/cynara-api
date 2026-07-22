# Validation checklist

Validate in this order. Failures must use stable `code` + JSON Pointer `path` +
`message`.

## Structural (`JsonSchema.Net` / JSON Schema)

1. Clinical against `src/Cynara.Infrastructure/Schemas/v1/clinical-schema.schema.json`
2. UI against `src/Cynara.Infrastructure/Schemas/v1/ui-schema.schema.json`
3. Rules against `src/Cynara.Infrastructure/Schemas/v1/rules-schema.schema.json`

## Clinical semantics

| Code                            | Check                                                |
| ------------------------------- | ---------------------------------------------------- |
| `DUPLICATE_FIELD_ID`            | `id` unique among siblings                           |
| `DUPLICATE_FIELD_CODE`          | `code` unique across entire clinical schema          |
| `REPEATER_MIN_MAX_INVALID`      | `minItems` ≤ `maxItems`                              |
| `CHOICE_DEFAULT_NOT_IN_OPTIONS` | `default` ∈ `options[].value`                        |
| `NUMERIC_MIN_MAX_INVALID`       | `minimum` ≤ `maximum`                                |
| `TEXT_MIN_MAX_INVALID`          | `minLength` ≤ `maxLength`                            |
| `REPEATER_NOT_REPEATABLE`       | `repeatable` must not be `false`                     |
| `COMPONENT_VERSION_REQUIRED`    | `componentVersion` required only for publish context |

## UI semantics

| Code                             | Check                                                    |
| -------------------------------- | -------------------------------------------------------- |
| `UNKNOWN_CLINICAL_FIELD`         | every `fields` key is a clinical `id`                    |
| `UNKNOWN_LAYOUT_FIELD`           | every layout `fieldId` is a clinical `id`                |
| `LAYOUT_GROUP_CHILD_MISMATCH`    | group children are direct clinical group items           |
| `LAYOUT_REPEATER_CHILD_MISMATCH` | repeater `itemTemplate` fields are direct repeater items |
| `CLINICAL_VERSION_MISMATCH`      | `ui.clinicalSchemaVersion` === `clinical.schemaVersion`  |

## Rules semantics

| Code                             | Check                                                      |
| -------------------------------- | ---------------------------------------------------------- |
| `RULE_UNKNOWN_FIELD`             | rules `fields` keys are clinical `id`s                     |
| `RULE_UNKNOWN_FIELD_REF`         | every `{ "ref" }` is a clinical `code`                     |
| `RULE_CALCULATE_NOT_READONLY`    | `calculate` targets are `readOnly: true`                   |
| `RULE_SELF_REFERENCE`            | calculate must not ref its own code                        |
| `RULE_CYCLIC_DEPENDENCY`         | no cycles among calculated fields                          |
| `RULE_CLINICAL_VERSION_MISMATCH` | `rules.clinicalSchemaVersion` === `clinical.schemaVersion` |
| `RULE_DUPLICATE_VALIDATION_CODE` | unique `validations[].code`                                |

## Expression operators

Comparison: `eq`, `neq`, `gt`, `gte`, `lt`, `lte`  
Boolean: `and`, `or`, `not`  
Utility: `empty`, `coalesce`  
Arithmetic (for `calculate`): `add`, `sub`, `mul`, `div`

## Identity patterns

- `id`: `^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$` (max 64)
- `code` (clinical field): `^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$` (max 128)
- `validations[].code`: `^[A-Z][A-Z0-9_]{2,63}$` (SCREAMING_SNAKE, e.g.
  `FULL_NAME_REQUIRED`)

## Where to put “validations”

| Need                                                     | Where                                           |
| -------------------------------------------------------- | ----------------------------------------------- |
| Regex / minLength / maxLength / required on one question | `clinical.fields[]` properties                  |
| Cross-field assert (A vs B)                              | `rules.validations[]` with AST `assert`         |
| Conditional required/visible                             | `rules.fields[id].requiredWhen` / `visibleWhen` |

Never put `pattern` or extra keys inside `rules.validations[]` items — only
`code`, `message`, `assert`, optional `when`.

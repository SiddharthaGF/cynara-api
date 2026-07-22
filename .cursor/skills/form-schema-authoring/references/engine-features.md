# Question engine features (authoring catalog)

Use the full runtime engine, not only the simplified designer inspector. Prefer
rich AST when the requirement needs `and`/`or`/`not`/`empty`/`gt`/etc. Simple
`eq` is fine when that is enough.

Do **not** invent `coded-value` — it exists in the meta-schema but is not
designer-supported yet.

## Clinical field types and constraints

Common on every field: `id`, `code`, `type`, optional `required`, `readOnly`,
`description`, `default`.

| Type            | Value                         | Extra clinical constraints                                              |
| --------------- | ----------------------------- | ----------------------------------------------------------------------- |
| `text`          | string                        | `minLength`, `maxLength`, `pattern`                                     |
| `textarea`      | string                        | `minLength`, `maxLength` (no `pattern`)                                 |
| `number`        | number                        | `minimum`, `maximum`, `multipleOf`, `decimalPlaces` (0–10)              |
| `integer`       | integer                       | `minimum`, `maximum`                                                    |
| `boolean`       | boolean                       | —                                                                       |
| `date`          | `YYYY-MM-DD`                  | `minimum`, `maximum` (date strings)                                     |
| `datetime`      | ISO date-time                 | `minimum`, `maximum`                                                    |
| `time`          | ISO time                      | `minimum`, `maximum`                                                    |
| `choice`        | string (or string[] if multi) | `options[{value,label}]` required; `allowMultiple`; `default` ∈ options |
| `group`         | object                        | `items[]` (≥1 nested fields)                                            |
| `repeater`      | array                         | `items[]`; `repeatable` must be true if present; `minItems`/`maxItems`  |
| `component-ref` | ref                           | `componentCode` required; `componentVersion` optional in draft          |

## UI presentation (per clinical `id`)

| Property      | Use                                                     |
| ------------- | ------------------------------------------------------- |
| `label`       | Required in practice for user-facing fields             |
| `helpText`    | Short guidance near the field                           |
| `placeholder` | Empty-state hint                                        |
| `widget`      | Must match type (`assets/widget-map.json`)              |
| `width`       | `full` \| `half` \| `third` \| `quarter`                |
| `hidden`      | Static default hide; runtime `visibleWhen` can override |
| `timePresets` | For `time`/`datetime`: `["now"]` only                   |
| `order`       | Sort when layout omitted                                |

### Layout nodes

- `section`: `{ type, title, children[, id, description] }`
- `field`: `{ type: "field", fieldId }`
- `group`: `{ type: "group", fieldId, children }` — children must be direct
  clinical group items
- `repeater`:
  `{ type: "repeater", fieldId, itemTemplate[, addButtonLabel, removeButtonLabel] }`

## Rules: field behaviors (keyed by clinical `id`)

| Key            | When        | Effect                                                 |
| -------------- | ----------- | ------------------------------------------------------ |
| `visibleWhen`  | boolean AST | false → hide                                           |
| `enabledWhen`  | boolean AST | false → disable                                        |
| `requiredWhen` | boolean AST | true → required at runtime                             |
| `calculate`    | any AST     | derived value; clinical field MUST be `readOnly: true` |

Omitted rule → static clinical/UI defaults apply.

## Rules: cross-field `validations[]`

Each item: `code` (SCREAMING_SNAKE), `message`, `assert` (boolean AST), optional
`when` (boolean AST). No other keys.

## Expression AST

Nodes: `{ "ref": "<clinical.code>" }` | `{ "lit": <json> }` |
`{ "op": "<op>", "args": [...] }`.

| Ops                              | Arity | Use              |
| -------------------------------- | ----- | ---------------- |
| `eq` `neq` `gt` `gte` `lt` `lte` | 2     | compare          |
| `and` `or`                       | ≥2    | combine          |
| `not`                            | 1     | negate           |
| `empty`                          | 1     | missing/blank    |
| `coalesce`                       | ≥2    | first non-empty  |
| `add` `sub` `mul` `div`          | 2     | `calculate` only |

`{ "ref" }` in conditions reads the field's value as boolean only when the field
is boolean; otherwise wrap in comparison/`empty`.

## Condition cookbooks (copy patterns)

Show field when choice equals a value:

```json
{
  "visibleWhen": {
    "op": "eq",
    "args": [{ "ref": "assessment.consciousness" }, { "lit": "alert" }]
  }
}
```

Show when NOT empty:

```json
{
  "visibleWhen": {
    "op": "not",
    "args": [{ "op": "empty", "args": [{ "ref": "medication.name" }] }]
  }
}
```

Require when another boolean is true:

```json
{
  "requiredWhen": { "ref": "patient.hasAllergies" }
}
```

Enable unless choice is a sentinel:

```json
{
  "enabledWhen": {
    "op": "neq",
    "args": [{ "ref": "assessment.consciousness" }, { "lit": "unresponsive" }]
  }
}
```

Compound show (A and B):

```json
{
  "visibleWhen": {
    "op": "and",
    "args": [
      { "op": "eq", "args": [{ "ref": "intake.smoking" }, { "lit": "yes" }] },
      { "op": "eq", "args": [{ "ref": "intake.consent" }, { "lit": true }] }
    ]
  }
}
```

Calculate BMI-style (target clinical field `readOnly: true`):

```json
{
  "calculate": {
    "op": "div",
    "args": [
      { "ref": "vital.weight-kg" },
      {
        "op": "mul",
        "args": [{ "ref": "vital.height-m" }, { "ref": "vital.height-m" }]
      }
    ]
  }
}
```

Cross-field assert (only when both filled):

```json
{
  "code": "BP_SYSTOLIC_GT_DIASTOLIC",
  "message": "Systolic must be greater than diastolic",
  "when": {
    "op": "and",
    "args": [
      {
        "op": "not",
        "args": [{ "op": "empty", "args": [{ "ref": "vital.bp.systolic" }] }]
      },
      {
        "op": "not",
        "args": [{ "op": "empty", "args": [{ "ref": "vital.bp.diastolic" }] }]
      }
    ]
  },
  "assert": {
    "op": "gt",
    "args": [{ "ref": "vital.bp.systolic" }, { "ref": "vital.bp.diastolic" }]
  }
}
```

## Where constraints live

| Need                                            | Put in                                             |
| ----------------------------------------------- | -------------------------------------------------- |
| required / min-max / pattern / length / options | clinical field                                     |
| label / widget / width / layout                 | UI                                                 |
| show/hide / enable / conditional required       | `rules.fields[id].*`                               |
| derived value                                   | `rules.fields[id].calculate` + clinical `readOnly` |
| A vs B / cross-field                            | `rules.validations[]`                              |

Never put `pattern` inside `validations[]`. Never put labels in clinical.

## Also see

- `unsupported-features.md` — types, widgets, operators, and product actions
  that must be refused (echo draft + explain).

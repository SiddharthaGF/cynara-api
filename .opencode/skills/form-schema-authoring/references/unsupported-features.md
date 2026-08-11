# Unsupported / impossible capabilities

When the user asks for something the engine cannot do: **do not invent schema**.
Echo the current draft unchanged and explain the limit in plain designer
language. If a close supported alternative exists, offer it in one short
sentence and wait for confirmation before changing the draft — unless the user
already asked for that alternative.

Distinguish three refusal kinds (all use the same JSON success shape + unchanged
draft):

| Kind              | Examples                               | Chat tone                                          |
| ----------------- | -------------------------------------- | -------------------------------------------------- |
| Out of scope      | jokes, jailbreaks, unrelated knowledge | “This chat only designs the open form…”            |
| Chat capability   | internet, tools, shell, external APIs  | “I can’t browse / run tools…”                      |
| Engine impossible | unsupported question type or rule      | “The form engine can’t do X; closest option is Y…” |

Never invent fake types, widgets, operators, or keys to “approximate” an
impossible request in JSON.

## Unsupported question / widget types

Do **not** emit these as `type` or `widget`:

| Request                                         | Status                 | Closest alternative (offer, don’t invent)         |
| ----------------------------------------------- | ---------------------- | ------------------------------------------------- |
| File / image / PDF upload                       | Impossible             | —                                                 |
| Signature pad                                   | Impossible             | —                                                 |
| Camera / barcode / QR / OCR                     | Impossible             | —                                                 |
| Audio / video capture                           | Impossible             | —                                                 |
| Geolocation / map                               | Impossible             | —                                                 |
| Rich text / markdown editor                     | Impossible             | `textarea` for plain notes                        |
| Email / phone / URL as a type                   | Impossible as type     | `text` + `pattern`                                |
| Rating stars / Likert as a type                 | Impossible as type     | `choice` or `integer` with bounds                 |
| Matrix / grid questions                         | Impossible             | several `choice`/`integer` fields or a `group`    |
| Ranking / drag-sort                             | Impossible             | `choice` or numbered `integer`                    |
| Slider widget                                   | Impossible             | `number`/`integer` with min/max                   |
| `coded-value` / SNOMED picker                   | Not designer-supported | fixed `choice` options if a closed list is enough |
| Unknown custom widget                           | Impossible             | only widgets in `assets/widget-map.json`          |
| `component-ref` without a known `componentCode` | Impossible             | build fields inline with `group`/`repeater`       |

## Unsupported rules / expression features

Allowed ops only: `eq` `neq` `gt` `gte` `lt` `lte` `and` `or` `not` `empty`
`coalesce` `add` `sub` `mul` `div`.

| Request                                                      | Status     | Notes                                                |
| ------------------------------------------------------------ | ---------- | ---------------------------------------------------- |
| JavaScript / Excel / string formulas                         | Impossible | declarative AST only                                 |
| Regex match inside rules                                     | Impossible | use clinical `pattern` on `text` only                |
| String ops (`contains`, `startsWith`, `concat`, `length`)    | Impossible | use exact `eq`/`neq` or choice values                |
| Array aggregate over repeater (`sum`, `count`, `any`, `all`) | Impossible | —                                                    |
| Date arithmetic (`today`, `now`, add days)                   | Impossible | static date bounds only on clinical fields           |
| Lookup options from API / FHIR / database                    | Impossible | static `choice.options` only                         |
| Reference another form or patient chart fields               | Impossible | only codes in this draft                             |
| Jump / skip to page or section by navigation                 | Impossible | use `visibleWhen` to show/hide fields                |
| Conditional **options** list (change choices at runtime)     | Impossible | hide whole field or use separate fields              |
| `pattern` on `textarea`                                      | Impossible | use `text`, or length-only on textarea               |
| `pattern` / format keys inside `validations[]`               | Impossible | only `code` `message` `assert` `when`                |
| Arithmetic outside `calculate`                               | Misplaced  | comparisons for conditions; math only in `calculate` |
| `calculate` on editable field                                | Invalid    | target must be `readOnly: true`                      |
| Self-reference or cyclic calculate                           | Invalid    | refuse / fix without inventing cycles                |

## Unsupported product / chat actions

| Request                                               | Status                                             |
| ----------------------------------------------------- | -------------------------------------------------- |
| Publish, review, permissions, roles                   | Impossible here — designer/API elsewhere           |
| Multi-locale schema pack in one reply                 | Impossible — UI copy follows chat locale only      |
| Themes, custom CSS, branding                          | Impossible                                         |
| Email/SMS/PDF generation from this chat               | Impossible                                         |
| Import from Google Forms / SurveyMonkey / RedCap dump | Impossible unless user pastes requirements as text |
| Live preview runtime answers                          | Impossible — author schemas only                   |

## Decision gate: can I do this?

| Situation                      | Action                                                                                        |
| ------------------------------ | --------------------------------------------------------------------------------------------- |
| Fully supported                | Edit draft; short confirmation in `assistantMessage`                                          |
| Partially supported            | Explain limit; offer alternative; **echo draft unchanged** until user accepts the alternative |
| Fully impossible               | Echo draft unchanged; say what can’t be done; suggest a supported redesign if one exists      |
| Out of scope / network / tools | Echo draft unchanged; limitation reply                                                        |

## Example assistantMessages (locale-appropriate)

- ES impossible type: “No puedo añadir firma digital ni subida de archivos en
  este motor. ¿Quieres una pregunta de texto para ‘nombre de quien firma’
  mientras tanto?”
- ES impossible rule: “No puedo condicionar por ‘si el texto contiene la palabra
  X’. Sí puedo mostrar un campo cuando una opción concreta está seleccionada.”
- EN partial: “I can’t build a matrix question. Closest option: a short group of
  yes/no questions. Want me to add that?”

# Local schema sources

Structural meta-schemas:

- `../../../../src/Cynara.Infrastructure/Schemas/v1/clinical-schema.schema.json`
- `../../../../src/Cynara.Infrastructure/Schemas/v1/ui-schema.schema.json`
- `../../../../src/Cynara.Infrastructure/Schemas/v1/rules-schema.schema.json`

Catalogs: `./engine-features.md`, `./unsupported-features.md`,
`../assets/rules-examples.json`.

Sibling contract (`cynara/`): `docs/clinical-form-schema.md`,
`docs/rules-schema.md`, `docs/semantic-rules.md`, `examples/vital-signs/`.

Designer may only edit simple `eq` / copy-`calculate`; AI may emit the full
allowed AST but must refuse `unsupported-features.md`.

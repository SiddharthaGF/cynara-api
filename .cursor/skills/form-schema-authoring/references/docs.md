# Local schema sources

Authoritative meta-schemas (structural validation):

- `../../../../src/Cynara.Infrastructure/Schemas/v1/clinical-schema.schema.json`
- `../../../../src/Cynara.Infrastructure/Schemas/v1/ui-schema.schema.json`
- `../../../../src/Cynara.Infrastructure/Schemas/v1/rules-schema.schema.json`

Skill authoring catalogs:

- `./engine-features.md` — supported types, constraints, conditions, AST
  cookbooks
- `./unsupported-features.md` — what is impossible and how to refuse
- `../assets/rules-examples.json`

Runtime validation entry point:

- `../../../../src/Cynara.Infrastructure/Schemas/JsonSchemaValidator.cs` —
  `JsonSchema.Net` (Draft 2020-12) structural checks against the schema files
  above plus the C# compilation/semantic rule checks wired in
  `src/Cynara.Infrastructure/DependencyInjection.cs`

Contract docs (sibling repo `cynara/` when available in the workspace):

- `../../../../../cynara/docs/clinical-form-schema.md`
- `../../../../../cynara/docs/rules-schema.md`
- `../../../../../cynara/docs/semantic-rules.md`
- `../../../../../cynara/examples/vital-signs/` — full triple example (conditions +
  cross-field validation)

Designer widget defaults (web):

- `../../../../../cynara-web/src/features/forms/designer/fieldInspectorMeta.ts`

Note: the designer inspector UI only edits simple `eq` conditions and
`calculate` as a field copy. The AI authoring path may emit the full rules AST
when asked — but must refuse operators/types listed in
`unsupported-features.md`.

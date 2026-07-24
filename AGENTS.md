# Cynara API Agent Guide

## Project Type

This repository is the primary ASP.NET Core backend for Cynara, a configurable
clinical platform. It implements the technology-neutral clinical form schema
contract defined in the `cynara` repository and is consumed by `cynara-web`
(and other clients) over HTTP.

Treat form/component lifecycle, schema validation, compilation, response
validation, review workflows, concurrency, and audit events as business-critical
behavior. Do not silently drop unknown fields or weaken contract checks.

## Stack

- .NET 10 / ASP.NET Core minimal APIs
- Modular layered solution: Api modules → Application modules → Domain;
  Infrastructure modules provide persistence and schema validation
- EF Core + SQLite (default local DB)
- JSON Schema Draft 2020-12 via `JsonSchema.Net`
- xUnit + `WebApplicationFactory` integration tests

Default listen URL: `http://localhost:5000`.

## Commands

```bash
dotnet restore
dotnet run --project src/Cynara.Api

make format        # write formatting changes
make format-check  # verify only
make lint          # build -warnaserror
make test          # dotnet test
make check         # restore + format-check + lint + test
make fix           # format + safe analyzer fixes
make sonar         # SonarQube Community Build: up + bootstrap + scan
make sonar-up      # start local SonarQube + Postgres (Docker)
make sonar-scan    # SonarScanner for .NET → http://localhost:9000
# Bootstrap also assigns profile "Cynara C#" (S104 file LOC ≤400).
make seed          # seed demo showcase via Application services (any configured DB)
```

Run the narrowest relevant checks first. Prefer `make test` (or a filtered
`dotnet test`) for behavior changes; run `make check` before claiming the
change is ready. Do not claim tests were run when only format/lint ran.

Husky.Net installs a pre-commit hook on restore that formats staged `.cs` files
and builds with `-warnaserror`. If the hook rewrites files, re-stage and commit
again. Disable hooks only with `HUSKY=0` when the user explicitly asks.

## Source Layout

- `src/Cynara.Api/`: host, composition root, cross-cutting HTTP concerns, and
  feature endpoint modules under `Modules/`.
- `src/Cynara.Api/Modules/`: minimal API route groups for Forms, Components,
  FormResponses, Audit, and Health.
- `src/Cynara.Application/`: business workflows, contracts, DTOs, compilers,
  validators, rule engine, audit writer, and `CynaraException` hierarchy.
- `src/Cynara.Application/Modules/`: feature-owned services, contracts, and
  persistence ports. Separate query and lifecycle services when a feature has
  distinct read and state-transition workflows.
- `src/Cynara.Domain/`: entities and status enums (forms, components, responses,
  audit).
- `src/Cynara.Infrastructure/`: EF Core database context, module repositories,
  module entity configurations, DI, embedded JSON Schema files under
  `Schemas/v1/`, and demo seed fixtures under `SeedData/`.
- `tests/Cynara.Api.Tests/`: integration and workflow tests against the API.
- `tools/Cynara.Seed/`: in-process CLI that seeds the demo showcase form via
  Application services (same path as preview startup seeding).
- `scripts/`: local SonarQube bootstrap and scan helpers.

Keep HTTP concerns in Api modules. Put workflows and persistence ports in the
owning Application module. Keep EF implementations and entity configurations in
the matching Infrastructure module. The composition root only wires modules;
it must not contain business rules.

### Module boundaries

Each feature module follows the same shape:

```text
Api/Modules/<Feature>/                 HTTP endpoints
Application/Modules/<Feature>/        use cases, DTO contracts, ports
Infrastructure/Modules/<Feature>/     EF repositories and configurations
Domain/<Feature>/                     entities and status rules
```

Application services depend on ports, not EF or `CynaraDbContext`. Repositories
track and stage changes only. Workflows inject `IUnitOfWork` and own the single
`SaveChangesAsync` boundary for each operation.

`IAuditWriter` stages audit events through the current unit of work; it must not
persist independently. Mutations and their audit records must commit together.

## Endpoint Surface (mental map)

- Forms: draft CRUD, submit/withdraw/reject review, publish, retire version
- Components: draft CRUD, publish, retire version
- Form responses: create/update/complete/soft-delete, revisions
- Audit: list events
- Health: `GET /health`

Actor identity comes from request context (headers/helpers on endpoints); preserve
audit emission on mutating workflows.

## Implementation Rules

- Keep nullable reference types and analyzers intact. Do not add broad
  `#pragma` / suppression noise to silence build warnings.
- Prefer file-scoped namespaces, explicit accessibility, and existing
  `.editorconfig` / `.globalconfig` style (`dotnet format`, max line length 80).
- Throw `CynaraException` subtypes (`NotFoundException`, `ConflictException`,
  `ValidationException`, `ConcurrencyException`, `InvalidStateException`,
  `FormResponseValidationException`) from Application; let the Api exception
  handler map them to Problem Details. Do not invent ad-hoc status codes in
  services.
- Preserve structural JSON Schema validation and semantic compilation/rule
  checks. Schema files under Infrastructure must stay aligned with the `cynara`
  contract.
- Treat draft → review → publish and response draft → complete as explicit state
  machines. Reject illegal transitions with `InvalidStateException` (or the
  existing equivalent), never by silently no-oping.
- Honor concurrency tokens / optimistic concurrency; surface
  `ConcurrencyException` rather than overwriting.
- Keep canonical JSON serialization and content hashing behavior stable when
  touching compilation or persistence of schema payloads.
- Keep EF entity mappings in module-owned `IEntityTypeConfiguration<T>` classes;
  `CynaraDbContext` should only expose sets and compose configurations.
- Keep repository methods free of commits. Use `IUnitOfWork` from the workflow
  that coordinates the mutation.
- Discard assignments (`_ =`) only where the codebase already does for
  deliberately unused returns; do not “clean” that style casually.
- Do not overwrite or revert unrelated work already present in the working tree.

## Testing Expectations

- Add or update tests in `tests/Cynara.Api.Tests/` for lifecycle, validation,
  review, compilation, or audit behavior changes.
- Prefer HTTP-level tests via `WebApplicationFactory<Program>` for API contract
  changes; unit-test pure validators/rule helpers when that is clearer.
- Seed or arrange data through the public API or existing test helpers; avoid
  brittle direct DB coupling unless the scenario requires it.
- After meaningful changes, run at least:

```bash
make test
```

For broader readiness (format + analyzers + tests):

```bash
make check
```

## Related Repositories

| Repository | Role |
|------------|------|
| `cynara` | Schema contract, docs, fixtures |
| `cynara-web` | Primary React frontend |

When authoring form schema triples (clinical / UI / rules), use the
`form-schema-authoring` skill.

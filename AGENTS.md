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

- .NET 9 / ASP.NET Core minimal APIs
- Clean-ish layered solution: Api → Application → Domain; Infrastructure wires
  persistence and schema validation
- EF Core + SQLite (default local DB)
- JSON Schema Draft 2020-12 via `JsonSchema.Net`
- xUnit + `WebApplicationFactory` integration tests

Default listen URL: `http://localhost:5080`.

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
```

Run the narrowest relevant checks first. Prefer `make test` (or a filtered
`dotnet test`) for behavior changes; run `make check` before claiming the
change is ready. Do not claim tests were run when only format/lint ran.

Husky.Net installs a pre-commit hook on restore that formats staged `.cs` files
and builds with `-warnaserror`. If the hook rewrites files, re-stage and commit
again. Disable hooks only with `HUSKY=0` when the user explicitly asks.

## Source Layout

- `src/Cynara.Api/`: host, `Program.cs`, endpoint mapping, exception → Problem
  Details.
- `src/Cynara.Api/Endpoints/`: minimal API route groups
  (`/api/forms`, `/api/components`, `/api/responses`, `/api/audit`).
- `src/Cynara.Application/`: services, DTOs, compilers, validators, rule engine,
  `CynaraException` hierarchy.
- `src/Cynara.Domain/`: entities and status enums (forms, components, responses,
  audit).
- `src/Cynara.Infrastructure/`: EF Core (`CynaraDbContext`), repositories, DI,
  embedded JSON Schema files under `Schemas/v1/`.
- `tests/Cynara.Api.Tests/`: integration and workflow tests against the API.
- `scripts/`: seed helpers and sample clinical/UI/rules JSON.

Keep HTTP concerns in Api endpoints. Put domain workflows in Application
services. Persistence implementations stay in Infrastructure behind Application
interfaces (`IFormRepository`, etc.).

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
| `cynara-api-nest` | NestJS reference/alternate API |

When authoring form schema triples (clinical / UI / rules), use the
`form-schema-authoring` skill (canonical copy in `cynara-api-nest`).

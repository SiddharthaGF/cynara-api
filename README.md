# Cynara API

Primary backend for [Cynara](https://github.com/ailuracode/cynara): a configurable clinical platform for hospitals.

Built with **ASP.NET Core**. Implements the technology-neutral [clinical form schema contract](https://github.com/ailuracode/cynara/blob/main/docs/clinical-form-schema.md) defined in the `cynara` repository.

## Related repositories

| Repository | Role |
|------------|------|
| [cynara](https://github.com/ailuracode/cynara) | Schema contract, docs, fixtures |
| [cynara-web](https://github.com/ailuracode/cynara-web) | React frontend (primary) |

## Contract conformance

Validation must pass both layers defined in the contract:

1. **Structural** — JSON Schema Draft 2020-12 against `schemas/v1/*.schema.json` from `cynara`
2. **Semantic** — rules in [`semantic-rules.md`](https://github.com/ailuracode/cynara/blob/main/docs/semantic-rules.md)

Recommended libraries: `JsonSchema.Net` or `NJsonSchema` with `System.Text.Json`.

Use the fixture suite in [`cynara/tests/fixtures/`](https://github.com/ailuracode/cynara/tree/main/tests/fixtures) as the conformance baseline.

## Getting started

Prerequisites: [.NET SDK 9](https://dotnet.microsoft.com/download)

```bash
dotnet restore          # also installs git hooks via Husky.Net
dotnet run --project src/Cynara.Api
```

The API listens on `http://localhost:5080` by default.

In Development, interactive OpenAPI docs are at
[`http://localhost:5080/scalar/v1`](http://localhost:5080/scalar/v1)
(JSON document: [`/swagger/v1/swagger.json`](http://localhost:5080/swagger/v1/swagger.json)).

The HTTP contract is **JSON:API** (`application/vnd.api+json`) via
`JsonApiDotNetCore`, with resource routes under `/api` (for example
`/api/formDefinitions`, `/api/formVersions`). Workflow actions such as
publish/review/complete remain custom routes on those resources. Sample
requests live in [`http/cynara.http`](http/cynara.http).

### Notable libraries

| Library | Role |
|---------|------|
| `JsonApiDotNetCore` | JSON:API resources, query, pagination |
| `JsonApiDotNetCore.OpenApi.Swashbuckle` | OpenAPI document for JSON:API (experimental) |
| `Scalar.AspNetCore` | OpenAPI UI in Development (points at Swagger JSON) |
| `Microsoft.Extensions.AI` + `OpenAI` | Chat completions for Form AI |
| `Polly` | Retries/timeouts on non-streaming AI calls |
| `FluentValidation` | Request DTO validation for retained command models |
| `Stateless` | Form/component/response status transitions |
| `Verify.Xunit` | Snapshot tests for stable contracts |
| `Testcontainers.MsSql` | Optional SQL Server integration tests |
| `JsonSchema.Net` | Structural schema validation (Draft 2020-12) |

### WSL (Windows Subsystem for Linux)

Run everything **inside the WSL terminal**, not PowerShell or CMD.

```bash
cd ~/ailuracode/cynara/cynara-api
dotnet run --project src/Cynara.Api
```

- **From WSL:** `http://localhost:5080/health`
- **From Windows browser:** `http://localhost:5080/health` (WSL2 forwards localhost by default)
- **If Windows cannot reach localhost:** use the WSL IP instead, e.g. `http://172.22.252.7:5080/health` (`hostname -I` shows the current IP)

If you see `address already in use`, another instance is already bound to port 5080:

```bash
fuser -k 5080/tcp
dotnet run --project src/Cynara.Api
```

For interactive shells, ensure the .NET SDK is on your PATH (add to `~/.bashrc` if needed):

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

Git hooks load `.husky/env.sh` automatically so commits from the IDE work even when your shell profile is not sourced.

## Code quality

Linting and formatting follow .NET conventions via [NetAnalyzers](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview), `.editorconfig`, and the SDK [`dotnet format`](https://learn.microsoft.com/dotnet/core/tools/dotnet-format) command.


```bash
make format        # write formatting changes
make format-check  # verify only (--verify-no-changes)
make lint          # dotnet build --no-restore -warnaserror
make test          # SQLite suite (excludes Category=MsSql)
make test-mssql    # SQL Server via Testcontainers (needs Docker)
make check         # full local/CI validation
make fix           # format + apply safe analyzer fixes
make sonar         # local SonarQube Community Build analysis
make seed          # seed demo showcase into configured DB (no HTTP)
```

Default `make test` keeps the fast in-memory SQLite host. `make test-mssql`
starts a disposable SQL Server container and runs the `Category=MsSql` smoke
tests against the same API path (`Database:Provider=SqlServer`).

Seed uses the same `Database:Provider` / `ConnectionStrings:Default` resolution as the API
(`appsettings`, env vars, or CLI). Examples:

```bash
make seed
make seed SEED_ARGS='--provider SqlServer --connection "Server=...;Database=cynara;..."'
Database__Provider=SqlServer ConnectionStrings__Default='...' make seed
```

Rules live in `.editorconfig`, `.globalconfig`, and `Directory.Build.props`.

### SonarQube Community Build (code smells)

Local smell / quality-gate analysis uses [SonarQube Community Build](https://docs.sonarsource.com/sonarqube-community-build/) via Docker and the [SonarScanner for .NET](https://docs.sonarsource.com/sonarqube-community-build/analyzing-source-code/scanners/dotnet/introduction/).

Prerequisites: Docker Desktop (or Docker Engine) with Compose.

```bash
make sonar              # start server, bootstrap token, scan
# or step by step:
make sonar-up           # SonarQube + Postgres on :9000
make sonar-bootstrap    # change default admin password + write .sonar/token
make sonar-scan         # begin → build → end; opens project cynara-api
make sonar-down         # stop containers (keeps volumes)
```

- UI: http://localhost:9000
- Default bootstrap password: `CynaraSonarAdmin1!` (override with `SONAR_ADMIN_PASSWORD`)
- Token path: `.sonar/token` (gitignored), or export `SONAR_TOKEN`
- Compose file: `docker/sonarqube/docker-compose.yml`
- Local quality gate: `Cynara Local` (`new_violations=0`, no coverage requirement)
- Local C# profile: `Cynara C#` — Sonar S104 file size warning at **400 LOC**
  (`maximumFileLocThreshold=400`, severity MINOR)

First boot can take 1–2 minutes while Elasticsearch starts. Re-run `make sonar-scan` after code changes to refresh findings.

## Architecture

Cynara API is a modular monolith. The HTTP contract remains stable while each
feature owns its endpoints, application workflows, persistence ports, and EF
adapters.

| Module | Responsibility |
|--------|----------------|
| `Forms` | Form definitions, drafts, versioning, review, publication, compilation |
| `Components` | Reusable component definitions and versions |
| `FormResponses` | Response drafts, completion, validation, and revisions |
| `Audit` | Audit event writing and filtered audit queries |
| `Health` | Service health endpoint |

### Feature structure

```text
src/Cynara.Api/Modules/<Feature>/
src/Cynara.Application/Modules/<Feature>/
src/Cynara.Infrastructure/Modules/<Feature>/
src/Cynara.Domain/<Feature>/
```

Application modules expose ports and workflows. Infrastructure modules implement
those ports with EF Core repositories and entity configurations. API modules only
translate HTTP requests into application calls.

### Transaction boundary

Repositories stage changes but do not call `SaveChangesAsync`. Each mutating
workflow injects `IUnitOfWork` and commits once. `IAuditWriter` stages the audit
event in the same unit of work, so a business mutation and its audit record cannot
commit independently.

### Service responsibilities

Features with separate read and state-transition concerns use distinct services:

- Forms: `FormService` and `FormReviewService`
- Components: `ComponentQueriesService` and `ComponentLifecycleService`
- Form responses: `FormResponseQueriesService` and `FormResponseLifecycleService`

Avoid adding a generic repository or moving business rules into endpoints. Keep
state transitions and validation in Application, domain entities in Domain, and
database concerns in Infrastructure.

### Git hooks (Husky.Net)

On `dotnet restore`, [Husky.Net](https://github.com/alirezanet/Husky.Net) installs a `pre-commit` hook that:

1. **Formats** staged `.cs` files (`dotnet format`)
2. **Lints** the solution (`dotnet build -warnaserror`)

Tests run separately via `make test` or CI (`make check`). Disable hooks with `HUSKY=0 git commit`.

NuGet vulnerability audit is disabled locally (`NuGetAudit=false` in `Directory.Build.props`) so pre-commit does not require nuget.org. Enable it in CI with `-p:NuGetAudit=true` if needed.

If a previous failed restore cached `NU1900` under `obj/`, run once:

```bash
dotnet clean && dotnet restore
```

If formatting changes files, re-stage and commit again:

```bash
git add -u && git commit
```

Manual hook setup:

```bash
dotnet tool restore
dotnet husky install
```

## Project layout

```
src/Cynara.Api/                 ASP.NET Core host and HTTP modules
src/Cynara.Application/         Workflows, ports, DTOs, validators, compilers
src/Cynara.Domain/               Entities and domain status models
src/Cynara.Infrastructure/      EF Core, repositories, schemas, SeedData
tests/Cynara.Api.Tests/         Integration and workflow tests
tools/Cynara.Seed/              In-process demo showcase seeder CLI
scripts/                        Local SonarQube bootstrap/scan helpers
```

## License

MIT — see [LICENSE](LICENSE).

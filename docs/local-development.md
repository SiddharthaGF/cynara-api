# Local development workflow

Reproducible setup for `cynara-api` on a clean checkout. Targets .NET 10
(`global.json` pins `10.0.302` with `rollForward: latestFeature`), ASP.NET
Core minimal APIs, EF Core + SQL Server, and JSON:API over
`application/vnd.api+json`.

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | `10.0.302` (or any `10.0.x` with `latestFeature`) | Pin via [`global.json`](../global.json); install via [dotnet.microsoft.com](https://dotnet.microsoft.com/download) or your package manager. |
| Docker Desktop | optional | Only required for `make mssql-up` (running the API locally or seeding the demo showcase) and `make sonar*`. Not needed to run `make test`. |
| Git | recent | Required by Husky.Net and `git config` actions. |
| Bash | POSIX | Used by `Makefile`, Husky hooks, and SonarQube scripts. |
| curl or HTTPie | optional | For hitting [`http/cynara.http`](../http/cynara.http) sample requests. |

### Platform notes

- **WSL (Windows Subsystem for Linux)** — run everything from the WSL
  shell, not PowerShell or CMD. See [WSL section](#wsl-windows-subsystem-for-linux)
  for browser port forwarding.
- **macOS / Linux** — works as written. On Apple Silicon, install the
  `arm64` .NET 10 SDK; no other platform steps differ.
- **Windows native** — supported for VS Code / `dotnet` work, but the CI
  pipeline targets Linux and Windows deployments use the published
  artifact under `publish/`. Use WSL if you want a Linux-like dev loop.

### Verify the SDK

```bash
dotnet --version          # 10.0.x
dotnet --list-sdks        # confirm a 10.0.x SDK is present
```

If `dotnet` is not on `PATH` (common with manual installs under
`~/.dotnet/dotnet`), add to `~/.bashrc`:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

Husky's [`env.sh`](../.husky/env.sh) does the same lookup automatically so
IDE-launched commits work even when the shell profile is not sourced.

## First-time setup

```bash
git clone https://github.com/ailuracode/cynara-api.git
cd cynara-api
dotnet restore          # also installs Husky.Net pre-commit hooks
make mssql-up           # start the local MSSQL container
```

`dotnet restore` triggers [`Directory.Build.targets`](../Directory.Build.targets)
which runs `dotnet tool restore` + `dotnet husky install`. If the stamp file
already exists from a prior checkout, the hooks are not re-installed — to
re-run manually:

```bash
dotnet tool restore
dotnet husky install
```

The API ships with `ConnectionStrings:Default` pointing at the local MSSQL
container in `appsettings.json` and `appsettings.Development.json`. Override
via environment variables when needed (preferred for secrets):

```bash
export ConnectionStrings__Default='Server=localhost,1433;Database=cynara;User Id=sa;Password=...;Encrypt=False;TrustServerCertificate=True;'
```

`__` is the standard .NET configuration section separator.

### Why EF Core In-Memory for tests?

The integration suite runs against `Microsoft.EntityFrameworkCore.InMemory`
because every concurrency, validation, and tenant-isolation behaviour
asserted by the tests is verified through the HTTP layer (or via explicit
`CynaraException` types) — the tests do not depend on the database engine
rejecting invalid inserts, applying FK cascades, or comparing `rowversion`
columns. `MigrateAsync` is replaced by `EnsureCreatedAsync` in
`InitializeDatabaseAsync` when the provider is non-relational.

Trade-offs accepted by this choice:

- **Speed and zero setup** — full suite in ~20 s on a developer laptop,
  no container, no `docker compose up`.
- **No relational engine** — FK constraints, identity columns, and
  filtered unique indexes are not enforced by the test store. Add coverage
  for any behaviour that depends on engine-rejected DML.
- **No real concurrency tokens** — `RowVersion` concurrency is checked in
  the application layer before `SaveChangesAsync`, so HTTP-level tests
  still observe `409 Conflict` correctly. If you add a test that asserts
  `DbUpdateConcurrencyException` directly, it will not fire under
  In-Memory.

## Quick reference

| What | Command |
|------|---------|
| Restore + build | `dotnet restore && dotnet build -warnaserror` |
| Format / format-check | `make format` / `make format-check` |
| Lint | `make lint` |
| Run integration tests | `make test` (EF Core In-Memory, no Docker) |
| Full local CI check | `make check` |
| Local MSSQL container | `make mssql-up` / `make mssql-down` / `make mssql-logs` |
| Apply pending migrations | `make migrate` |
| Seed the demo showcase | `make seed` |
| Local SonarQube scan | `make sonar` |
| Watch the API on `:5000` | `dotnet run --project src/Cynara.Api` |

## Database

`cynara-api` uses EF Core against SQL Server exclusively. Schema management
goes through EF Migrations under `src/Cynara.Infrastructure/Migrations/`.

> **Integration tests do not need SQL Server.** The test suite runs against
> EF Core In-Memory — see [Quick reference](#quick-reference). Docker is only
> required when running the API locally or when seeding the demo showcase.

### Local MSSQL container

```bash
make mssql-up           # docker compose -f docker/mssql/docker-compose.yml up -d
make mssql-down         # stops the container (keeps the volume)
make mssql-logs         # tail the SQL Server logs
```

Defaults baked into `docker/mssql/docker-compose.yml`:

| Item | Value |
|------|-------|
| Image | `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` |
| Port | `localhost:1433` |
| SA password | `CynaraSqlDev!2026` |
| Database (created by EF Migrations) | `cynara` |
| Volume | `mssql_mssql_data` (persistent) |

The container comes with an empty `master` database. EF Migrations creates the
`cynara` database and the schema on first API start. To reset, drop the
database (`docker exec cynara-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost
-U sa -P 'CynaraSqlDev!2026' -C -No -Q "DROP DATABASE cynara;"`) and restart
the API; `MigrateAsync` will recreate everything from scratch.

### Schema initialization

`InitializeDatabaseAsync` is invoked from
[`WebApplicationExtensions.UseCynaraApiAsync`](../src/Cynara.Api/Hosting/WebApplicationExtensions.cs)
on every startup. For relational providers it calls `MigrateAsync`; for
non-relational stores (e.g. the EF Core In-Memory provider used by the test
suite) it calls `EnsureCreatedAsync` instead, since `MigrateAsync` requires
a relational schema history table. Pending migrations are applied in order;
nothing is dropped. Add new migrations with:

```bash
dotnet ef migrations add <Name> \
  --project src/Cynara.Infrastructure \
  --startup-project src/Cynara.Api
```

## Seed data

The demo showcase form is seeded via
[`tools/Cynara.Seed`](../tools/Cynara.Seed) using the same
`AddCynaraInfrastructure(...)` path as the API host. The seeder reads the
same `appsettings.json` / `appsettings.{Environment}.json` files plus
environment variables and CLI flags:

```bash
make seed
# explicit connection:
make seed SEED_ARGS='--connection "Server=...;Database=cynara;..."'
# or env vars:
ConnectionStrings__Default='Server=...;Database=cynara;...' make seed
```

Seeded fixtures live under
[`src/Cynara.Infrastructure/SeedData/`](../src/Cynara.Infrastructure/SeedData)
(`demo-showcase-*.json` and `patient-demographics-*.json`). After seeding,
browse `http://localhost:5000/api/formDefinitions?filter=code:demo-showcase`
or `http://localhost:5000/api/formVersions?...`.

The seeder is idempotent: re-running `make seed` updates the demo
component and form rather than duplicating them.

## API startup

### Run from the command line

```bash
dotnet run --project src/Cynara.Api
# listens on http://localhost:5000 by default (see launchSettings.json)
```

Smoke tests:

```bash
curl -s http://localhost:5000/health
# → {"service":"cynara-api","status":"ok","probes":[...]}
curl -s http://localhost:5000/api/formDefinitions
```

The HTTP sample requests in [`http/cynara.http`](../http/cynara.http)
exercise the full lifecycle. Open with the VS Code REST Client
extension, Rider's HTTP client, or `curl`.

### Run from VS Code

The repository ships three launch configurations in
[`.vscode/launch.json`](../.vscode/launch.json):

| Configuration | Purpose |
|---------------|---------|
| `Cynara API (Debug)` | C# Dev Tools debug session against `Cynara.Api.csproj` on http://localhost:5000. |
| `.NET Core Launch (web)` | Built `dotnet` debug session with `ASPNETCORE_ENVIRONMENT=Development`; runs the pre-launch `build` task. |
| `.NET Core Attach` | Attach to a running API process for live debugging. |

Tasks in [`.vscode/tasks.json`](../.vscode/tasks.json):

| Task | Command |
|------|---------|
| `watch` | `dotnet watch run --project Cynara.Api.sln` — auto-rebuild on changes. |
| `build` | `dotnet build Cynara.Api.sln` |
| `publish` | `dotnet publish Cynara.Api.sln` |

### Common startup failures

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `Now listening on: http://localhost:5000` missing | Another process owns the port | `fuser -k 5000/tcp` (or pick another port via `ASPNETCORE_URLS=http://localhost:5050`). |
| `/health` returns 503 with `probes[database].status: fail` | Connection string wrong / DB unreachable | Verify `ConnectionStrings:Default`; ensure `make mssql-up` is running. |
| `/health` returns 503 with `probes[schemas].status: fail` and `Missing N file(s); first missing: ...` | Build artifacts are missing the JSON Schema files copied from `src/Cynara.Infrastructure/Schemas/v1/` | Run `dotnet build` — `Cynara.Api.csproj` `<Content>` items copy them to `Schemas/v1/*.schema.json`. |
| `Login failed for user 'sa'` | SA password mismatch | Confirm the password in `ConnectionStrings:Default` matches `MSSQL_SA_PASSWORD` in `docker/mssql/docker-compose.yml`. |
| Husky pre-commit refuses to run | `dotnet` missing on PATH inside IDE | Husky's `env.sh` adds `$HOME/.dotnet/dotnet`; restart the IDE so it re-sources the shell. |
| `NU1900` warnings from a stale restore cache | Locked NuGet packages from a prior half-completed restore | `dotnet clean && dotnet restore`. |
| `dotnet format` rewrote files during pre-commit | Style drift | `git add -u && git commit` to re-stage the reformatted files. |

### WSL (Windows Subsystem for Linux)

From the WSL shell:

```bash
cd ~/ailuracode/cynara/cynara-api
make mssql-up
dotnet run --project src/Cynara.Api
```

Browser URLs (after the API prints `Now listening on: http://localhost:5000`):

- From WSL: `http://localhost:5000/health`
- From Windows: `http://localhost:5000/health` — WSL2 forwards localhost by
  default.
- If Windows cannot reach `localhost`: use the WSL IP, e.g.
  `http://172.22.252.7:5000/health` (`hostname -I` shows the current IP).
- `address already in use`: another instance is bound — `fuser -k 5000/tcp`
  then re-run.

## Health and readiness

`GET /health` returns `200 OK` when both probes succeed; otherwise
`503 Service Unavailable`. The endpoint is excluded from the OpenAPI
document.

```json
{
  "service": "cynara-api",
  "status": "ok",
  "probes": [
    { "name": "database", "status": "ok", "detail": null },
    { "name": "schemas",  "status": "ok", "detail": null }
  ]
}
```

Probes:

| Probe | Check | Failure detail hint |
|-------|-------|---------------------|
| `database` | `Database.CanConnectAsync()` against SQL Server | Exception type and message (e.g. login failures, network errors). |
| `schemas` | All three required JSON Schema files exist on disk (`Schemas/v1/clinical-schema.schema.json`, `ui-schema.schema.json`, `rules-schema.schema.json`) | `Missing N file(s); first missing: <path>`. |

The probe set is deliberately small — no external HTTP calls, no AI
provider check, no migration check (migrations are applied by
`MigrateAsync` at startup, before the probe runs). Add new probes inside
`src/Cynara.Api/Modules/Health/HealthEndpoints.cs` and keep them bounded;
`/health` must not become a load test against your dependencies.

Use `/health` for liveness/readiness from Docker, Kubernetes, or your
local `curl`. There is no separate `/ready` route — the same endpoint
serves both.

## CORS and frontend integration

`Cors:AllowedOrigins` in `appsettings.json` ships with the production
worker (`https://cynara-web.livesanty.workers.dev`). For local development
against [`cynara-web`](https://github.com/ailuracode/cynara-web), add the dev
server origin. The default Vite port is `5173`; add the production preview URL
and any extra local origins you actually use. The frontend's
[`docs/local-development.md`](https://github.com/ailuracode/cynara-web/blob/main/docs/local-development.md)
covers the matching `VITE_API_ORIGIN` setup.

Override individual CORS origins via environment variables:

```bash
export Cors__AllowedOrigins__0='http://localhost:5173'
export Cors__AllowedOrigins__1='https://cynara-web.livesanty.workers.dev'
```

CORS defaults:

- **Headers:** any (`AllowAnyHeader()`)
- **Methods:** any (`AllowAnyMethod()`)
- **Origins:** explicit allow-list (no wildcards — `WithOrigins` is
  used, not `AllowAnyOrigin`)

Auditing identity is set with the `X-Actor-Id` request header. It is
documented in OpenAPI under the `ActorId` security scheme; the API does
not authenticate the value (it is a maquette), so production must add
real auth before exposing this surface.

## Form AI (optional)

Form AI calls an OpenAI-compatible chat endpoint. The configuration is
read by `OpenAiConfiguration` from the following environment variables
(no committed secrets):

| Variable | Default | Purpose |
|----------|---------|---------|
| `OPENAI_API_KEY` | _(empty)_ | Required for any AI call. `IsConfigured` is `false` when missing — endpoints return a clear error instead of timing out. |
| `OPENAI_BASE_URL` | `https://api.openai.com/v1` | Override for OpenRouter, Azure, or local models. |
| `OPENAI_MODEL` | `gpt-4o-mini` | Model id passed to the chat client. |
| `OPENAI_JSON_OBJECT` | `true` | Request `response_format: { type: json_object }`. |
| `OPENAI_NETWORK_TIMEOUT_SECONDS` | `600` | Per-request ceiling. Increase for very large form authoring. |
| `OPENAI_FIRST_CHUNK_TIMEOUT_SECONDS` | `90` | TTFB budget for streaming; long TTFBs are the main cause of stream timeouts. |
| `OPENAI_MAX_OUTPUT_TOKENS` | _(unset)_ | Optional cap on output tokens. |
| `OPENAI_TEMPERATURE` | _(unset)_ | Optional sampling temperature. |
| `OPENAI_TOP_P` | _(unset)_ | Optional nucleus sampling parameter. |

Without an API key, Form AI endpoints report a configuration error
rather than failing silently — see
`tests/Cynara.Api.Tests/FormAiEndpointTests.cs` for behavior.

### Why EF Core In-Memory for tests?

The integration suite runs against `Microsoft.EntityFrameworkCore.InMemory`
because every concurrency, validation, and tenant-isolation behaviour
asserted by the tests is verified through the HTTP layer (or via explicit
`CynaraException` types) — the tests do not depend on the database engine
rejecting invalid inserts, applying FK cascades, or comparing `rowversion`
columns. `MigrateAsync` is replaced by `EnsureCreatedAsync` in
`InitializeDatabaseAsync` when the provider is non-relational.

Trade-offs accepted by this choice:

- **Speed and zero setup** — full suite in ~20 s on a developer laptop,
  no container, no `docker compose up`.
- **No relational engine** — FK constraints, identity columns, and
  filtered unique indexes are not enforced by the test store. Add coverage
  for any behaviour that depends on engine-rejected DML.
- **No real concurrency tokens** — `RowVersion` concurrency is checked in
  the application layer before `SaveChangesAsync`, so HTTP-level tests
  still observe `409 Conflict` correctly. If you add a test that asserts
  `DbUpdateConcurrencyException` directly, it will not fire under
  In-Memory.

| Task | Command |
|------|---------|
| Restore + install hooks | `dotnet restore` |
| Format code | `make format` |
| Format check (CI parity) | `make format-check` |
| Build with `-warnaserror` | `make lint` |
| Run integration tests | `make test` (EF Core In-Memory, no Docker) |
| Format + lint + test | `make check` |
| Apply safe analyzer fixes | `make fix` |
| Seed the demo showcase | `make seed` (boots MSSQL via `make mssql-up`) |
| Local SonarQube analysis | `make sonar` (or `make sonar-up` / `make sonar-scan`) |
| Start MSSQL container | `make mssql-up` |
| Stop MSSQL container | `make mssql-down` |
| Start API (default port 5000) | `dotnet run --project src/Cynara.Api` |
| Start API with auto-reload | `dotnet watch run --project src/Cynara.Api` |
| Hit the health probe | `curl -s http://localhost:5000/health` |
| OpenAPI document | `curl -s http://localhost:5000/swagger/v1/swagger.json` |
| Scalar UI (Development only) | <http://localhost:5000/scalar/v1> |

## Related docs

- [`README.md`](../README.md) — high-level overview, code quality,
  architecture.
- [`AGENTS.md`](../AGENTS.md) — implementation rules, module layout,
  testing expectations.
- [`Makefile`](../Makefile) — canonical command surface.
- [`.github/workflows/pipeline.yml`](../.github/workflows/pipeline.yml) —
  CI parity for `make check` + publish.
- [`docs/`](../) — workflow and contributor docs (this file).

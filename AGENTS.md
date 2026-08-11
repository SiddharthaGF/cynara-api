# Cynara API Agent Guide

## Project Type

This repository is the primary ASP.NET Core backend for Cynara, a configurable
clinical platform. It implements the technology-neutral clinical form and
workflow schema contract (meta-schemas under
`src/Cynara.Infrastructure/Schemas/v1/`, served over HTTP at `/schemas/v1`) and
is consumed by `cynara-web` (and other clients) over HTTP.

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
dotnet tool restore
dotnet run --project src/Cynara.Api

# Build pipeline is Cake — wrap calls with `dotnet cake`.
dotnet cake --target=Format         # write formatting changes
dotnet cake --target=FormatCheck    # verify only
dotnet cake --target=Lint           # build -warnaserror
dotnet cake --target=Test           # dotnet test
dotnet cake --target=Check          # restore + format-check + lint + test
dotnet cake --target=Fix            # format + safe analyzer fixes
dotnet cake --target=Seed           # seed demo showcase via Application services
dotnet cake --target=Sonar          # SonarQube Community Build: up + bootstrap + scan
dotnet cake --target=SonarUp        # start local SonarQube + Postgres (Docker)
dotnet cake --target=SonarBootstrap # change admin password + write .sonar/token
dotnet cake --target=SonarScan      # SonarScanner for .NET → http://localhost:9000
# Bootstrap also assigns profile "Cynara C#" (S104 file LOC ≤400).
```

Run the narrowest relevant checks first. Prefer `dotnet cake --target=Test`
(or a filtered `dotnet test`) for behavior changes; run
`dotnet cake --target=Check` before claiming the change is ready. Do not
claim tests were run when only format/lint ran.

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
  checks. Schema files under Infrastructure are the canonical contract; keep
  them aligned with the served `/schemas/v1` documents.
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
dotnet cake --target=Test
```

For broader readiness (format + analyzers + tests):

```bash
dotnet cake --target=Check
```

## Related Repositories

| Repository | Role |
|------------|------|
| `cynara-web` | Primary React frontend |

When authoring form schema triples (clinical / UI / rules) or workflow
schemas, use the `form-schema-authoring` skill.

## Commit Messages

Only create a commit when the user explicitly asks. Follow Conventional Commits
in English (match existing history).

Format:

```
type(scope): imperative summary

Optional body explaining why, not a file list.
```

- Subject: ≤72 chars, imperative mood, no trailing period
- Body: wrap ~72 chars; focus on **why**, not what changed file-by-file
- Pass the message via HEREDOC (`git commit -m "$(cat <<'EOF' ... EOF)"`)

Types: `feat` | `fix` | `refactor` | `perf` | `test` | `chore` | `docs` |
`build` | `ci`.

Scopes (prefer these): `api` · `forms` · `components` · `responses` · `audit` ·
`schema` · `persistence` · `validation` · `config` · `deps` · `tests`.

Omit scope only when the change is truly cross-cutting. If a Linear/issue id is
known (e.g. `CYN-11`), append it: `feat(forms): add draft withdraw-review
endpoint (CYN-11)`.

Examples:

```
feat(forms): enforce review gate before publish
fix(validation): reject out-of-step numeric answers
refactor(persistence): share soft-delete helpers for drafts
test(responses): cover complete-after-soft-delete conflict
chore(config): tighten editorconfig for test projects
```

Anti-patterns: vague file dumps (`update stuff`, `fix bugs`), past tense, or
trailing periods.

## Pull Requests

Only open a PR when the user explicitly asks. Use `gh` for GitHub tasks. Write
titles and bodies in English (match existing PRs).

Before opening:

1. Inspect branch state vs base (`git status`, `git diff`, `git log`,
   `git diff main...HEAD` or the repo's default base).
2. Push with `-u` if the branch is not on the remote yet.
3. Prefer focused PRs; if a change is large, ask about splitting before opening.

Title: imperative, specific, ≤90 chars, no trailing period. Prefer the
Conventional Commit shape (`feat(forms): add draft withdraw-review endpoint
(CYN-11)`) or a clear outcome title with ticket ids. Include Linear/issue ids
when known (`CYN-N`).

Body (pass via HEREDOC to `gh pr create`):

```markdown
## Summary
- 1–3 bullets: API/contract outcome or why this change exists
- Not a file list

## Test plan
- [ ] Concrete checks a reviewer can run
- [ ] Prefer `make test` or `make check` when lifecycle, validation, or
      endpoints changed
- [ ] Call out schema contract, review/publish, concurrency, or audit when
      those areas moved

Linear:
- https://linear.app/ailuracode/issue/CYN-N
```

Omit the Linear block only when no issue exists. Use `Closes #N` / `Fixes #N`
when linking a GitHub issue.

Anti-patterns: vague titles (`Update api`, `WIP`, `fix stuff`) or a body that is
a file dump with no test plan.

## SonarQube MCP Server

Guidelines when using the SonarQube MCP server (`.opencode/mcp/sonarqube-mcp.sh`).

### Basic usage

- **IMPORTANT**: After you finish generating or modifying any code files at the
  very end of the task, you MUST call the `analyze_file_list` tool (if it
  exists) to analyze the files you created or modified.
- **IMPORTANT**: When starting a new task, you MUST disable automatic analysis
  with the `toggle_automatic_analysis` tool if it exists.
- **IMPORTANT**: When you are done generating code at the very end of the task,
  you MUST re-enable automatic analysis with the `toggle_automatic_analysis`
  tool if it exists.

### Project Keys

- When a user mentions a project key, use `search_my_sonarqube_projects` first
  to find the exact project key.
- Don't guess project keys - always look them up.

### Code Language Detection

- When analyzing code snippets, try to detect the programming language from the
  code syntax. If unclear, ask the user or make an educated guess based on
  syntax.

### Branch and Pull Request Context

- Many operations support branch-specific analysis. If user mentions working on
  a feature branch, include the branch parameter.

### Code Issues and Violations

- After fixing issues, do not attempt to verify them using
  `search_sonar_issues_in_projects`, as the server will not yet reflect the
  updates.

### Troubleshooting

- SonarQube requires USER tokens (not project tokens). When the error
  `SonarQube answered with Not authorized` occurs, verify the token type.
- Use `search_my_sonarqube_projects` to find available projects; verify project
  key spelling and format.
- Ensure programming language is correctly specified. Remind users that snippet
  analysis doesn't replace full project scans. Provide full file content for
  better analysis results.

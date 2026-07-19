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
dotnet restore
dotnet run --project src/Cynara.Api
```

The API listens on `http://localhost:5080` by default.

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

Ensure the .NET SDK is on your PATH (add to `~/.bashrc` if needed):

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

## Code quality

Linting and formatting follow .NET conventions via [NetAnalyzers](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview), `.editorconfig`, and the SDK [`dotnet format`](https://learn.microsoft.com/dotnet/core/tools/dotnet-format) command.


```bash
make format        # write formatting changes
make format-check  # verify only (--verify-no-changes)
make lint          # dotnet build --no-restore -warnaserror
make test          # dotnet test --no-restore
make check         # full local/CI validation
make fix           # format + apply safe analyzer fixes
```

Rules live in `.editorconfig`, `.globalconfig`, and `Directory.Build.props`.

## Project layout

```
src/Cynara.Api/          ASP.NET Core Web API
tests/Cynara.Api.Tests/  Integration tests
schemas/                 Git submodule → ailuracode/cynara (contract files)
```

### Schema submodule

```bash
git submodule add https://github.com/ailuracode/cynara.git schemas
git submodule update --init --recursive
```

## License

MIT — see [LICENSE](LICENSE).

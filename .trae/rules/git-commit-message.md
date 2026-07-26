---
alwaysApply: true
scene: git_message
---

# Commit Messages

Only create a commit when the user explicitly asks. Follow Conventional Commits
in English (match existing history).

## Format

```
type(scope): imperative summary.
```

- Subject: ≤72 chars, imperative mood, no trailing period
- Body: wrap ~72 chars; focus on **why**, not what changed file-by-file
- Pass the message via HEREDOC (`git commit -m "$(cat <<'EOF' ... EOF)"`)

## Types

| type | use when |
|------|----------|
| `feat` | new user-facing capability / endpoint behavior |
| `fix` | bug fix |
| `refactor` | internal change, same behavior |
| `perf` | performance improvement |
| `test` | add or update tests only |
| `chore` | tooling, config, deps, ignore files |
| `docs` | documentation only |
| `build` | build / CI / Makefile / SDK tooling |
| `ci` | CI/CD configuration |

## Scopes (prefer these)

`api` · `forms` · `components` · `responses` · `audit` · `schema` ·
`persistence` · `validation` · `config` · `deps` · `tests`

Omit scope only when the change is truly cross-cutting.

## Ticket references

If a Linear/issue id is known (e.g. `CYN-11`), append it in the subject or body:
`feat(forms): add draft withdraw-review endpoint (CYN-11)`.

## Examples

```
feat(forms): enforce review gate before publish

fix(validation): reject out-of-step numeric answers

refactor(persistence): share soft-delete helpers for drafts

test(responses): cover complete-after-soft-delete conflict

chore(config): tighten editorconfig for test projects
```

## Anti-patterns

```
# vague / file dump
update stuff
fix bugs
feat: update FormService.cs and tests

# past tense / trailing period
Fixed the concurrency check.
```

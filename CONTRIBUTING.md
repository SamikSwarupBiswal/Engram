# Contributing to Engram

## Getting Started

1. Clone the repo
2. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
3. Build: `dotnet build Engram.sln`
4. Test: `dotnet test Engram.sln`

## Branch Naming

- `feature/<short-description>` — new features
- `fix/<short-description>` — bug fixes
- `docs/<short-description>` — documentation only
- `chore/<short-description>` — tooling, config, cleanup

## Commit Messages

This project uses structured commits tied to GSD phases:

```
type(scope): description

Examples:
  feat(store): implement append-only raw event writer
  fix(cli): handle empty raw directory in replay
  docs(01): add phase 1 summary
  test(store): add dedupe hash determinism test
  chore: update .editorconfig
```

Types: `feat`, `fix`, `docs`, `test`, `chore`, `refactor`, `spike`

## Pull Requests

1. Create a branch from `master`
2. Make changes with tests
3. Ensure `dotnet build` and `dotnet test` pass
4. Open a PR using the template
5. Get review and merge

## Code Style

- Follow `.editorconfig` rules (auto-enforced)
- Use `var` when type is apparent
- PascalCase for public members, `_camelCase` for private fields
- Interfaces prefixed with `I`
- Braces required for all control flow

## Testing

- All tests must run locally without cloud credentials
- Use xUnit with temp directories for file system tests
- Name tests: `Method_Scenario_ExpectedBehavior`
- Aim for deterministic, isolated tests

## Architecture Decisions

Significant decisions go in `docs/adr/` as Architecture Decision Records.
Use the template: `docs/adr/000-template.md`

## Project Planning

This project uses GSD (Get Shit Done) for phase management.
Planning artifacts live in `.planning/`. See `.planning/PROJECT.md` for
project direction and `.planning/ROADMAP.md` for the phase structure.

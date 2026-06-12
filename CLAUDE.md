# EBA Floor Plan Generator

Read `AGENTS.md` at the repo root — it contains the full agent guide: one-command
setup (self-installs a local .NET 8 SDK into `./.dotnet`), run/build/test
commands, the API smoke test, and the invariants that keep changes safe
(cache busters, golden fixtures, determinism, editor geometry rules).

Quick reference:

- Run the app: `./scripts/run-web.ps1` (Windows) or `./scripts/run-web.sh`
  (macOS/Linux) → http://localhost:5127
- Test: `./.dotnet/dotnet test FloorPlanGeneration.sln -c Debug`
  (stop the dev server first — it locks the Web DLL)

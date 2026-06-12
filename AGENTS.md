# EBA Floor Plan Generator — Agent Guide

This file is for AI coding agents (Claude Code, Codex, Copilot, Cursor, …).
Everything you need to set up, run, test, and safely modify this repository.

## What this is

A .NET 8 solution that generates residential floor plans from JSON constraints
or natural-language briefs, with a browser studio for editing and exporting them.

- `FloorPlanGeneration/` — the core engine (no external dependencies). Corridor +
  unit-band layouts for building floors (`layoutMode: "multi_unit"`, default) and
  whole-apartment room layouts (`layoutMode: "single_dwelling"`).
- `FloorPlanGeneration.Web/` — ASP.NET Core host + the vanilla-JS studio
  (`wwwroot/app.js`, `styles.css`, `index.html`). Stateless JSON API.
- `FloorPlanGeneration.Cli/` — headless JSON-in/JSON-out runner.
- `FloorPlanGeneration.Tests/` — xUnit suite (engine + frontend contract tests).
- `schemas/` — published input/output JSON Schemas. `samples/` — runnable inputs.

## Setup and run (one command, installs everything)

The run scripts check for a .NET 8 SDK and, if missing, download one into
`./.dotnet` (no admin rights, no global install, nothing outside the repo).

Windows (PowerShell):

```powershell
./scripts/run-web.ps1
```

macOS / Linux:

```bash
./scripts/run-web.sh
```

The app starts at **http://localhost:5127**. Smoke-test it:

```bash
curl http://localhost:5127/api/health
# -> {"ok":true,...}
curl -X POST http://localhost:5127/api/generate -H "Content-Type: application/json" \
  -d '{"sampleName":"rectangular-core","seed":7,"variants":4}'
# -> "validVariantCount":4
```

Headless generation without the web app: `./scripts/run-sample.ps1` (or `.sh`).

## Build and test

```bash
./.dotnet/dotnet build FloorPlanGeneration.sln -c Debug   # or plain `dotnet` if SDK 8 is global
./.dotnet/dotnet test FloorPlanGeneration.sln -c Debug
```

The suite must be fully green before any commit. **Stop the dev server before
running tests** — a running server locks `FloorPlanGeneration.Web.dll`.

## Optional: AI brief parsing (Claude and/or Codex)

If a `claude` (Claude Code) or `codex` (OpenAI Codex) CLI is on PATH — each
using its own subscription login — the server detects every installed one and
exposes them via `GET /api/prompt/status` (`{available, provider, providers}`).
Natural-language briefs are then interpreted by the chosen CLI (10–20 s per
parse) via `POST /api/prompt/parse` `{brief, provider?}`, with the built-in
heuristic parser as automatic fallback. When both CLIs are installed the studio
shows a provider picker next to the "reads the brief" toggle (persisted per
browser). Env overrides: `FLOORPLAN_AI_PROVIDER` (`claude`/`codex`/`off`
restricts detection), `FLOORPLAN_AI_MODEL`, `FLOORPLAN_AI_CLI` (full path to
one CLI). The app is fully functional without any AI CLI installed.

## Rules that keep changes safe

1. **Cache buster**: bump the `?v=` query on `app.js`/`styles.css` in
   `index.html` on every frontend change — the served files are no-store but
   open browser tabs are not.
2. **Golden fixtures**: any change to generation output requires regenerating
   `FloorPlanGeneration.Tests/Fixtures/golden-contracts.json` by running the
   CLI over the three core samples and re-extracting variant ids/seeds/scores/
   counts (see `ContractSchemaTests`). Never hand-edit the numbers.
3. **Determinism**: same input + seed must reproduce byte-identical variants.
   `Date.now()`-style entropy is forbidden in the engine; all randomness flows
   from the seeded `SeededRandom`.
4. **Editor invariants** (assert after touching `app.js` geometry code):
   every unit area equals the sum of its rooms' areas; every wall is
   axis-aligned; doors stay within 0.35 m of their host wall; nothing overlaps
   the core. Boundary-plane span absorption must use `absorbSpanOnLine`
   (transitive closure over all polygon edges on the line) — never grow spans
   only through already-admitted followers.
5. **Frontend contracts**: `WebFrontendRegressionTests` pins source-level
   contracts (function shapes, index.html structure). When you intentionally
   change a behavior, update the matching contract in the same commit.
6. **Source style**: max line length 160 chars across `.cs/.js/.css/.html`
   (enforced by a test); explicit types and no `var` in C#.

## Gotchas

- The brief textarea must never trigger auto-generation (it races the AI
  parse); the Generate-from-prompt button is its only trigger.
- `layoutMode` must never leak between briefs: a building brief on a dwelling
  document reloads a sample plate first.
- SVG filter lengths resolve in user units (meters here): a CSS
  `drop-shadow(0 8px 26px …)` on plan elements means a 26-meter blur that can
  freeze the rasterizer. Use stacked low-alpha rects for shadows.
- Engine polygons are closed rings (last point repeats the first); when moving
  vertices, mirror the closing vertex.

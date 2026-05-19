# Floor Plan Generation Engine

Rhino-first, headless floor plan generation MVP for multi-family residential floorplates.

The engine accepts architectural boundaries and fixed constraints as JSON, then returns ranked 2D floor plan variants with validation status and diagnostics. RhinoCommon/Grasshopper integration is intentionally kept as an adapter layer; the core engine is JSON I/O only.

## What is included

- `FloorPlanGeneration/` — deterministic C# engine, geometry validation, topology graph, candidate generation, ranking, diagnostics.
- `FloorPlanGeneration.Cli/` — JSON-in / JSON-out command-line wrapper.
- `FloorPlanGeneration.Tests/` — xUnit tests for valid generation and failure diagnostics.
- `docs/floor-plan-generation-engine.md` — detailed architecture, schemas, Rhino layer naming, and MVP limitations.
- `samples/floor-plan-generation/rectangular-core-input.json` — sample floorplate input.

## Quick start

```bash
dotnet build FloorPlanGeneration.sln
dotnet test FloorPlanGeneration.sln --no-build

dotnet run --project FloorPlanGeneration.Cli -- \
  --input samples/floor-plan-generation/rectangular-core-input.json \
  --output /tmp/floor-plan-output.json
```

## Design principles

- Headless JSON I/O core.
- Deterministic generation with seed-based variation.
- Honest validation failures with machine-readable diagnostics.
- Rhino/Grasshopper coupling only through future adapters.
- Predictable Rhino layer naming such as `FP::Input::Boundary` and `FP::Generated::Units`.

## MVP scope

This MVP targets rectangular, L-shaped, and moderately irregular floorplates with one fixed core. It is not a finished architectural design system; it is a production-oriented engine skeleton with explicit validation and extension points.

# Floor Plan Generation Engine

Rhino-first, headless floor plan generation MVP for multi-family residential floorplates.

The engine accepts architectural boundaries and fixed constraints as JSON, then returns ranked 2D floor plan variants with validation status and diagnostics. RhinoCommon/Grasshopper integration is intentionally kept as an adapter layer; the core engine is JSON I/O only.

## What is included

- `FloorPlanGeneration/` — deterministic C# engine, geometry validation, topology graph, candidate generation, ranking, diagnostics.
- `FloorPlanGeneration.Cli/` — JSON-in / JSON-out command-line wrapper.
- `FloorPlanGeneration.Tests/` — xUnit tests for valid generation and failure diagnostics.
- `docs/floor-plan-generation-engine.md` — detailed architecture, schemas, Rhino layer naming, and MVP limitations.
- `samples/floor-plan-generation/*.json` — rectangular, L-shaped, and infeasible sample inputs.

## Quick start

```bash
dotnet build FloorPlanGeneration.sln
dotnet test FloorPlanGeneration.sln --no-build

dotnet run --project FloorPlanGeneration.Cli -- \
  --input samples/floor-plan-generation/rectangular-core-input.json \
  --output /tmp/floor-plan-output.json \
  --summary
```

Useful CLI overrides:

```bash
dotnet run --project FloorPlanGeneration.Cli -- \
  --input samples/floor-plan-generation/l-shaped-core-input.json \
  --output /tmp/l-shaped-output.json \
  --seed 5601 \
  --variants 5 \
  --summary
```

The infeasible sample is expected to return exit code `2` and failed JSON with diagnostics:

```bash
dotnet run --project FloorPlanGeneration.Cli -- \
  --input samples/floor-plan-generation/infeasible-narrow-input.json \
  --summary
```

## Design principles

- Headless JSON I/O core.
- Deterministic generation with seed-based variation.
- Honest validation failures with machine-readable diagnostics.
- Rhino/Grasshopper coupling only through future adapters.
- Predictable future Rhino layer naming such as `FP::Input::Boundary`, `FP::Input::Fixed`, `FP::Generated::Units`, `FP::Generated::Corridors`, and `FP::Generated::Diagnostics`.

## MVP scope

This MVP targets rectangular, L-shaped, and moderately irregular floorplates with one fixed core. Candidate placement is obstacle-aware for holes and blocking fixed elements, but still heuristic and template-based. It is not a finished architectural design system; it is a production-oriented engine skeleton with explicit validation and extension points.

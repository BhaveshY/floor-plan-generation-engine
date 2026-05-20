# Floor Plan Generation Engine

Rhino-first, headless floor plan generation MVP for multi-family residential floorplates.

The engine accepts architectural boundaries and fixed constraints as JSON, then returns ranked 2D floor plan variants with validation status and diagnostics. RhinoCommon/Grasshopper integration is intentionally kept as an adapter layer; the core engine is JSON I/O only.

## Easiest Start

If you just want to try the engine and get an output file, run one of these commands from the repo folder.

Windows PowerShell:

```powershell
.\scripts\run-sample.ps1
```

macOS/Linux:

```bash
./scripts/run-sample.sh
```

The script writes `outputs/rectangular-core-output.json`. If .NET 8 is not installed, the script installs a local copy into `.dotnet/` for this folder.

To try another built-in sample:

```powershell
.\scripts\run-sample.ps1 -Sample l-shaped-core
```

Available sample names:

```bash
dotnet run --project FloorPlanGeneration.Cli -- --list-samples
```

## What is included

- `FloorPlanGeneration/` - deterministic C# engine, geometry validation, topology graph, candidate generation, ranking, diagnostics.
- `FloorPlanGeneration.Cli/` - JSON-in / JSON-out command-line wrapper.
- `FloorPlanGeneration.Tests/` - xUnit tests for valid generation and failure diagnostics.
- `schemas/*.schema.json` - published JSON Schema artifacts for the input and output contracts.
- `docs/floor-plan-generation-engine.md` - detailed architecture, schemas, Rhino layer naming, and MVP limitations.
- `docs/rhino-grasshopper-adapter-contract.md` - dependency-free adapter contract for Rhino and Grasshopper projects.
- `docs/roadmap.md` - concise remaining gaps and next integration steps.
- `samples/floor-plan-generation/*.json` - rectangular, L-shaped, moderately irregular, and infeasible sample inputs.

## Quick Start

```bash
dotnet build FloorPlanGeneration.sln
dotnet test FloorPlanGeneration.sln --no-build

dotnet run --project FloorPlanGeneration.Cli -- \
  --sample rectangular-core \
  --output outputs/rectangular-core-output.json \
  --summary
```

Validate a contract, cleanup, and feasibility dry run without generating variants:

```bash
dotnet run --project FloorPlanGeneration.Cli -- \
  --sample rectangular-core \
  --validate-only \
  --summary
```

Create a starter input file that you can edit:

```bash
dotnet run --project FloorPlanGeneration.Cli -- \
  --write-sample rectangular-core \
  --output my-first-floorplate.json
```

Print the published JSON Schema artifacts without reading engine input:

```bash
dotnet run --project FloorPlanGeneration.Cli -- --print-input-schema
dotnet run --project FloorPlanGeneration.Cli -- --print-output-schema
```

Useful CLI overrides:

```bash
dotnet run --project FloorPlanGeneration.Cli -- \
  --sample l-shaped-core \
  --output outputs/l-shaped-output.json \
  --seed 5601 \
  --variants 5 \
  --summary
```

The moderately irregular sample exercises a stepped floorplate with one fixed core:

```bash
dotnet run --project FloorPlanGeneration.Cli -- \
  --sample moderately-irregular-core \
  --output outputs/moderately-irregular-output.json \
  --summary
```

The infeasible sample is expected to return exit code `2` and failed JSON with diagnostics:

```bash
dotnet run --project FloorPlanGeneration.Cli -- \
  --sample infeasible-narrow \
  --summary
```

## Packaged Tool

After `dotnet pack`, the CLI can be installed as a local .NET tool:

```bash
dotnet tool install FloorPlanGeneration.Cli \
  --tool-path .tools \
  --add-source FloorPlanGeneration.Cli/bin/Release \
  --version 0.1.0

.tools/floorplan-gen --list-samples
.tools/floorplan-gen --sample rectangular-core --output outputs/rectangular-core-output.json --summary
```

## Design Principles

- Headless JSON I/O core.
- Deterministic generation with seed-based variation.
- Honest validation failures with machine-readable diagnostics.
- Rhino/Grasshopper coupling only through future adapters.
- Strict CLI JSON parsing: unknown JSON properties fail fast instead of being silently ignored.
- Published schema version `1.1` with strict input schema, output schema, and compact golden contract fixtures.
- Adapter-facing output metadata for schema version, seed, effective generation settings, floorplate bounds/areas, stable `externalId` values, and predictable layer names such as `FP::Input::Boundary`, `FP::Input::Fixed`, `FP::Generated::Units`, `FP::Generated::Corridors`, and `FP::Generated::Diagnostics`.

## MVP Scope

This MVP targets rectangular, L-shaped, and moderately irregular orthogonal floorplates with one fixed core. Candidate placement is obstacle-aware for holes and blocking fixed elements, but still heuristic and template-based. It is not a finished architectural design system; it is a production-oriented engine skeleton with explicit validation and extension points. See `docs/roadmap.md` for the remaining product gaps.

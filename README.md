# Floor Plan Generation Engine

Rhino-ready, headless floor plan generation app for multi-family residential floorplates.

The engine accepts architectural boundaries and fixed constraints as JSON, then returns ranked 2D floor plan variants with validation status, diagnostics, topology, and a portable hypergraph contract compatible with the `BhaveshY/hypergraph` `DataNode` shape. RhinoCommon/Grasshopper integration is intentionally kept as an adapter layer; the core engine is JSON I/O only.

## Easiest Start

If you want the visual app, run one of these commands from the repo folder.

Windows double-click:

```text
scripts\run-web.bat
```

Windows PowerShell:

```powershell
.\scripts\run-web.ps1
```

macOS/Linux:

```bash
./scripts/run-web.sh
```

The app opens at `http://localhost:5127` with sample loading, JSON file import, autosaved drafts, JSON editing, validation, generation, variant review, diagnostics, SVG preview, SVG export, and output export.

If you just want to get an output JSON file without opening the web app, run one of these commands.

Windows PowerShell:

```powershell
.\scripts\run-sample.ps1
```

Windows double-click:

```text
scripts\run-sample.bat
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

- `FloorPlanGeneration/` - deterministic C# engine, geometry validation, topology graph, portable hypergraph output, candidate generation, ranking, diagnostics.
- `FloorPlanGeneration.Cli/` - JSON-in / JSON-out command-line wrapper.
- `FloorPlanGeneration.Web/` - local web app for editing inputs, generating variants, previewing layouts, and inspecting diagnostics.
- `FloorPlanGeneration.Tests/` - xUnit tests for valid generation and failure diagnostics.
- `schemas/*.schema.json` - published JSON Schema artifacts for the input and output contracts.
- `docs/floor-plan-generation-engine.md` - detailed architecture, schemas, Rhino layer naming, and MVP limitations.
- `docs/rhino-grasshopper-adapter-contract.md` - dependency-free adapter contract for Rhino and Grasshopper projects.
- `docs/roadmap.md` - concise remaining gaps and next integration steps.
- `samples/floor-plan-generation/*.json` - rectangular, L-shaped, moderately irregular, and infeasible sample inputs.

## Quick Start

Run the web app:

```bash
dotnet run --project FloorPlanGeneration.Web --urls http://localhost:5127
```

Then open `http://localhost:5127`.

Useful local web API endpoints:

```bash
curl http://localhost:5127/api/manifest
curl http://localhost:5127/api/samples
curl -X POST http://localhost:5127/api/generate \
  -H "Content-Type: application/json" \
  -d "{\"sampleName\":\"rectangular-core\",\"variants\":1}"
```

Run the CLI:

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

After packing the CLI, it can be installed as a local .NET tool:

```bash
dotnet pack FloorPlanGeneration.Cli/FloorPlanGeneration.Cli.csproj --configuration Release

dotnet tool install FloorPlanGeneration.Cli \
  --tool-path .tools \
  --add-source FloorPlanGeneration.Cli/bin/Release \
  --version 0.1.0

.tools/floorplan-gen --list-samples
.tools/floorplan-gen --sample rectangular-core --output outputs/rectangular-core-output.json --summary
```

## AI Agent Contract

Claude Code, Codex, and other automation tools should use the structured CLI manifest instead of scraping this README:

```bash
dotnet run --project FloorPlanGeneration.Cli -- --ai-manifest
```

The CLI is designed to be agent-friendly:

- stdout is JSON for generation, validation, schema, sample, and manifest commands.
- `--summary` writes compact run status to stderr.
- stdin accepts EngineInput JSON when neither `--input` nor `--sample` is supplied.
- `--variants` accepts values from `1` through `20`.
- exit code `0` means success, `2` means failed engine/CLI JSON output, `3` means partial output with `--fail-on-partial`, and `64` means usage error.
- every generated variant includes `topology.hypergraph` with a recursive `DataNode` tree, explicit nodes, hyperedges, incidence records, and subdivision/adjacency/area/angle/incidence matrices.

## Design Principles

- Headless JSON I/O core.
- Deterministic generation with seed-based variation.
- Honest validation failures with machine-readable diagnostics.
- Rhino/Grasshopper coupling only through future adapters.
- Strict CLI JSON parsing: unknown JSON properties fail fast instead of being silently ignored.
- Published schema version `1.2` with strict input schema, output schema, and compact golden contract fixtures.
- Portable hypergraph output: recursive `DataNode` JSON (`name`, `area`, `angle`, `mergeid`, `final`, `children`, `connected`, `treeNodeMesh`) plus explicit hyperedges and matrices.
- Adapter-facing output metadata for schema version, seed, effective generation settings, floorplate bounds/areas, stable `externalId` values, and predictable layer names such as `FP::Input::Boundary`, `FP::Input::Fixed`, `FP::Generated::Units`, `FP::Generated::Corridors`, and `FP::Generated::Diagnostics`.

## MVP Scope

This MVP targets rectangular, L-shaped, and moderately irregular orthogonal floorplates with one fixed core. Candidate placement is obstacle-aware for holes and blocking fixed elements, but still heuristic and template-based. It is not a finished architectural design system; it is a production-oriented engine skeleton with explicit validation and extension points. See `docs/roadmap.md` for the remaining product gaps.

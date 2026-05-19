# Floor Plan Generation Engine

This document describes the MVP floor plan generation engine added under `FloorPlanGeneration*`. The engine is independent from the existing RGeoLib, notebook, sample, and Python API code; it is a small .NET library plus CLI that can later be wrapped by Grasshopper/Rhino components.

## Architecture

- `FloorPlanGeneration` targets `netstandard2.0` and contains the public `FloorPlanEngine`.
- `FloorPlanGeneration.Schema` defines JSON-serializable DTOs: `EngineInput`, `EngineOutput`, `LayoutVariant`, units, rooms, corridors, walls, openings, labels, diagnostics, metrics, and validation checks.
- `FloorPlanGeneration.Geometry` contains lightweight 2D primitives, polygon cleanup, predicate checks, and facade exposure helpers.
- `FloorPlanGeneration.Generation` contains deterministic seeded candidate generation, unit-mix selection, corridor strategy derivation, room templates, wall/opening/label output, and topology output.
- `FloorPlanGeneration.Topology` emits a simple graph of floorplate, fixed elements, corridor, unit, room, and facade relationships.
- `FloorPlanGeneration.Cli` is a `net8.0` command-line adapter using `System.Text.Json`.
- `FloorPlanGeneration.Tests` contains xUnit coverage for deterministic generation and failure paths.
- `schemas/` contains published Draft 2020-12 JSON Schema artifacts for the input and output contracts.

The public orchestration path is:

1. Normalize missing optional input sections and defaults.
2. Validate the input contract before generation: required outer boundary, finite numbers, positive rule values, supported strictness, unit target ranges, variant-count bounds, and scoring weights.
3. Clean the floorplate, holes, and fixed element polygons.
4. Stop on invalid geometry diagnostics such as self-intersection.
5. Run conservative feasibility checks for usable area and corridor-plus-unit-band depth.
6. Generate deterministic corridor/unit/room candidates from the cleaned input.
7. Split corridor and unit placement intervals around holes and blocking fixed elements.
8. Normalize generated output ordering, bounds, layers, and duplicate corridor connection labels.
9. Validate geometry containment, overlap, corridor width, doors, fixed-core access, daylight requirements, and strict unit-mix constraints.
10. Score each variant and rank valid variants before invalid variants.

## Input Schema

The CLI accepts camelCase JSON matching `EngineInput`.

The published schema is `schemas/floor-plan-engine-input.schema.json`. The CLI can print the same artifact without reading input:

```sh
dotnet run --project FloorPlanGeneration.Cli -- --print-input-schema
```

Main sections:

- `project`: `id`, `name`, `units`, `tolerance`, `seed`.
- `floorplate`: required `outer` polygon and optional `holes`.
- `fixedElements`: blocking or non-blocking polygons such as cores, stairs, shafts, and elevators.
- `access`: entry points, vertical core access points, optional corridor centerlines.
- `facade`: optional explicit daylight-capable segments. If omitted, the outer floorplate boundary is treated as daylight-capable.
- `program`: target unit types and room type rules.
- `rules`: minimum corridor width, room size, door width, daylight requirements, and minimum unit area.
- `generationSettings`: variant count, strictness, weighted variation, and scoring weights.

Polygon rings may be open or closed. The engine stores unique vertices internally and reports cleanup diagnostics when it removes duplicate, closing, or collinear points.

Example input:

```sh
samples/floor-plan-generation/rectangular-core-input.json
```

Additional samples:

- `samples/floor-plan-generation/l-shaped-core-input.json`: L-shaped floorplate with a blocking core; expected to produce valid variants.
- `samples/floor-plan-generation/moderately-irregular-core-input.json`: stepped orthogonal floorplate with one blocking core and core access point; expected to produce valid variants.
- `samples/floor-plan-generation/infeasible-narrow-input.json`: deliberately too narrow for the MVP corridor and unit-band heuristic; expected to return failed JSON with no variants.

The CLI uses strict `System.Text.Json` parsing. Unknown JSON properties are rejected with `cli.invalid_json` so misspelled contract fields do not silently fall back to defaults. Sample inputs are covered by schema tests.

## Output Schema

`EngineOutput` contains:

- `projectId`
- `status`: `succeeded`, `partial`, `failed`, or `validated` for `--validate-only`
- `metadata`: schema version, engine version, project units/tolerance, deterministic seed, effective generation settings, predictable layer names, and floorplate bounds/area summaries
- `variants`: ranked `LayoutVariant` results
- `diagnostics`: top-level cleanup, generation, validation, or CLI diagnostics

The published output schema is `schemas/floor-plan-engine-output.schema.json` and can be printed with:

```sh
dotnet run --project FloorPlanGeneration.Cli -- --print-output-schema
```

Each variant contains:

- `variantId`, `externalId`, `seed`, `status`
- `units`, each with stable `externalId`, type, polygon, bounds, layer, area, rooms, facade length, and score
- `rooms`, duplicated at variant level for easier adapters, each with stable `externalId`, bounds, and layer
- `corridors`, with stable `externalId`, polygon, bounds, layer, centerline, width, and connected ids
- `walls`, `doorsOpenings`, `labels`, each with stable `externalId` and predictable generated layer names
- `metrics`: gross area, sellable area, corridor area, net-gross ratio, efficiency, unit-mix match, score
- `validation`: checks with severity, source id, and reason
- `topology`: graph nodes and edges for containment, corridor access, and facade exposure, also carrying stable external ids

`metadata.layers` is the adapter-facing layer map. Current keys are `inputBoundary`, `inputHoles`, `inputFixed`, `inputAccess`, `inputFacade`, `units`, `rooms`, `corridors`, `walls`, `doors`, `labels`, `diagnostics`, and `topology`.

External ids use the deterministic URI-like pattern `fp://{projectId}/variants/{variantId}/{category}/{elementId}`. They are intended for Rhino user strings, Grasshopper data tree keys, BIM mapping, and compact golden contract tests.

## CLI Usage

Read from a file and write to a file:

```sh
dotnet run --project FloorPlanGeneration.Cli -- --input samples/floor-plan-generation/rectangular-core-input.json --output samples/floor-plan-generation/rectangular-core-output.json
```

Read from stdin and write to stdout:

```sh
dotnet run --project FloorPlanGeneration.Cli -- < samples/floor-plan-generation/rectangular-core-input.json
```

Validate only, without candidate generation:

```sh
dotnet run --project FloorPlanGeneration.Cli -- --input samples/floor-plan-generation/rectangular-core-input.json --validate-only --summary
```

Override the input seed or variant count without editing JSON:

```sh
dotnet run --project FloorPlanGeneration.Cli -- --input samples/floor-plan-generation/l-shaped-core-input.json --seed 5601 --variants 5 --summary
```

`--summary` writes a compact status line to stderr and leaves stdout clean for JSON when no `--output` path is supplied. `--fail-on-partial` returns a non-zero exit code for partial outputs, which is useful in automated checks.

Schema tooling:

- `--print-input-schema`: writes the current input schema JSON.
- `--print-output-schema`: writes the current output schema JSON.
- With `--output`, schema commands write to the supplied file and do not read stdin or `--input`.

Exit codes:

- `0`: engine status is not `failed`, including `validated`
- `2`: engine returned failed JSON output
- `3`: engine returned partial JSON output and `--fail-on-partial` was supplied
- `64`: CLI argument error

## Grasshopper Adapter Mapping

Recommended MVP Grasshopper components:

- `FP Boundary`: converts the outer floorplate curve and hole curves into `floorplate.outer` and `floorplate.holes`.
- `FP Fixed Elements`: converts core, stair, elevator, shaft, and other fixed curves into `fixedElements`.
- `FP Access`: maps entry points, vertical core access points, and optional corridor guide lines into `access`.
- `FP Program Rules`: builds `program`, `rules`, and `generationSettings` from value lists, sliders, and tables.
- `FP Generate`: calls `FloorPlanEngine.Generate(input)` and returns output JSON plus typed variant objects.
- `FP Variant Picker`: sorts or selects a `LayoutVariant` by rank, score, or id.
- `FP Bake Variant`: maps units, rooms, corridors, walls, doors, and labels to Rhino geometry and layers.
- `FP Diagnostics`: displays top-level and selected-variant diagnostics grouped by severity and source id.
- `FP Topology Graph`: exposes `TopologyGraph.Nodes` and `TopologyGraph.Edges` for adjacency analysis or graph visualization.

See `docs/rhino-grasshopper-adapter-contract.md` for the adapter boundary, component responsibilities, layer policy, external id policy, and failure handling rules.

Grasshopper geometry mapping:

- Closed planar boundary curve -> `floorplate.outer.points`
- Closed planar void curves -> `floorplate.holes`
- Core/stair/elevator/shaft curves -> `fixedElements`
- Corridor guide lines -> `access.corridorCenterlines`
- Door/core points -> `access.entryPoints` and `access.verticalCoreAccess`
- Facade sub-curves -> `facade.segments`
- Value lists/sliders/panels -> `program`, `rules`, and `generationSettings`

## Rhino Layers

Use these layer names when baking:

- `FP::Input::Boundary`
- `FP::Input::Holes`
- `FP::Input::Fixed`
- `FP::Input::Access`
- `FP::Input::Facade`
- `FP::Generated::Units`
- `FP::Generated::Rooms`
- `FP::Generated::Corridors`
- `FP::Generated::Walls`
- `FP::Generated::Doors`
- `FP::Generated::Labels`
- `FP::Generated::Diagnostics`
- `FP::Generated::Topology`

Labels produced by the engine already default to `FP::Generated::Labels`.

## Validation Guarantees

The MVP validates:

- Outer boundary has at least three points, finite coordinates, non-zero area, and no self-intersections.
- Holes and fixed elements are individually valid and contained by the floorplate.
- Clearly unusable floorplates fail before candidate generation when available area is below `rules.minUnitArea` or bounds cannot fit the MVP corridor width plus a unit band.
- Blocking fixed elements and holes are avoided by corridors and units.
- Generated corridors meet minimum width and stay inside the floorplate.
- Generated units stay inside the floorplate, do not overlap holes/fixed elements/corridors/other units, and meet minimum unit area.
- Generated rooms reference existing units, stay inside their units, have positive area, and satisfy configured daylight requirements for bedroom/living room types.
- Generated units have door/opening connections.
- Corridor connection lists include unit ids, vertical access point ids, and fixed core/stair/elevator ids when the corridor touches the fixed element, is face-adjacent, or shares a provided vertical core access point.
- Generated variants and elements expose non-empty `fp://` external ids that are unique within each variant.
- Generated elements use the published generated layer names.
- `generationSettings.strictness = "strict"` requires exact target unit counts when target counts are supplied.

Invalid contract values, invalid outer-boundary geometry, and conservative infeasibility checks stop before candidate generation and return failed diagnostics with no fake variants. When generation does run but cannot produce valid layouts, generation failure codes from variants are summarized at the top level.

## MVP Limits

- Geometry operations are lightweight polygon predicates and interval splitting, not a full constructive solid geometry kernel.
- Candidate generation is deterministic and heuristic; it does not solve a global optimization problem.
- Unit rooms are template-based rectangular subdivisions.
- Complex non-orthogonal, highly concave, multi-core, or code-constrained buildings may produce partial or failed variants.
- Facade handling is segment-length based and does not model view quality, obstructions, fire separation, or code-specific egress.
- Validation is architectural sanity checking, not permit-ready building code compliance.
- The engine does not mutate existing RGeoLib, samples, notebooks, or Python API surfaces.

## BIM and IFC Next Steps

Recommended next integration steps:

1. Add wall thickness offsets and room finish boundaries rather than centerline-only walls.
2. Derive IFC-compatible GUIDs from stable `externalId` values in an adapter project.
3. Map `UnitLayout` and `RoomLayout` to IFC spatial elements such as `IfcSpace` and zone/group objects.
4. Map `WallLayout` and `DoorOpening` to `IfcWall`/`IfcWallStandardCase` and `IfcDoor`.
5. Add storey/building/site metadata to `EngineInput.Project`.
6. Add an IFC export adapter separate from the core engine so the MVP library remains dependency-light.
7. Extend validation with egress distance, accessible clearances, shaft/fire separation, facade/window ratios, and local code profiles.

# Rhino and Grasshopper Adapter Contract

The core engine remains RhinoCommon-free and JSON-only. Rhino and Grasshopper integrations should live in adapter projects that translate Rhino geometry into `EngineInput`, call the engine or CLI, and bake selected `EngineOutput` variants back to Rhino layers.

A working reference implementation ships in [`adapters/grasshopper/`](../adapters/grasshopper/README.md): a paste-in Python script component (Rhino 7 GHPython and Rhino 8 Python 3) that generates plans from a written brief via the local server, outputs rooms/units/walls/doors/corridors as Rhino geometry, and bakes to the contract layers with stable external ids.

## Contract Inputs

Adapter components should emit JSON matching `schemas/floor-plan-engine-input.schema.json`.

Recommended components:

- `FP Boundary`: planar closed outer curve to `floorplate.outer.points`; optional void curves to `floorplate.holes`.
- `FP Fixed Elements`: core, stair, elevator, shaft, service, and keep-out curves to `fixedElements`.
- `FP Access`: entry points, vertical core access points, and optional corridor guide lines to `access`.
- `FP Facade`: facade segments and daylight-capable edge ids to `facade`.
- `FP Program Rules`: unit mix, room rules, rule set, scoring weights, strictness, seed, and variant count.
- `FP Generate`: validates against the input schema, calls `FloorPlanEngine.Generate` or the CLI, and returns output JSON plus typed adapter objects.
- `FP Validate`: calls `FloorPlanEngine.Validate` or CLI `--validate-only`.
- `FP Hypergraph`: reads `topology.hypergraph` from a selected variant and exposes the recursive `DataNode` tree, flattened nodes, hyperedges, incidence, and matrices.

## Contract Outputs

Adapter components should read JSON matching `schemas/floor-plan-engine-output.schema.json`.

Required adapter-facing fields:

- `metadata.schemaVersion`: currently `1.2`.
- `metadata.layers`: canonical layer map for input and generated geometry.
- `variantId` and `externalId`: stable variant identifiers.
- `externalId` on units, rooms, corridors, walls, doors, labels, topology nodes, and topology edges.
- `topology.hypergraph`: portable graph contract containing a `DataNode`-compatible recursive tree (`root`), flattened graph `nodes`, multi-member `hyperedges`, `incidence`, and matrix payloads.
- `validation.checks` and `diagnostics`: machine-readable status for UI panels and bake guards.

External ids use this deterministic pattern:

```text
fp://{projectId}/variants/{variantId}/{category}/{elementId}
```

Variant ids and external ids are seed-stable for the same input contract and engine version. Adapters can store them in Rhino user strings, Grasshopper data trees, hypergraph visualization keys, IFC property sets, or downstream BIM records.

## Baking Layers

Use `metadata.layers` instead of hard-coded strings where possible. Current generated layers are:

- `FP::Generated::Units`
- `FP::Generated::Rooms`
- `FP::Generated::Corridors`
- `FP::Generated::Walls`
- `FP::Generated::Doors`
- `FP::Generated::Labels`
- `FP::Generated::Diagnostics`
- `FP::Generated::Topology`

The engine validates generated layers before ranking variants. Adapter projects may create sublayers, but should preserve the engine layer name in object metadata.

## Failure Handling

Adapters must not bake failed variants as if they were usable plans.

- `status = validated`: input passed dry-run checks; no variants are present.
- `status = succeeded`: all generated variants passed validation.
- `status = partial`: at least one variant passed; failed variants are ranked later and carry diagnostics.
- `status = failed`: do not bake generated plans; display top-level diagnostics.

Infeasible or invalid inputs are expected product behavior. Show diagnostics grouped by `severity`, `code`, and `sourceId`.

## Dependency Boundary

Adapter projects may depend on RhinoCommon or Grasshopper SDKs. `FloorPlanGeneration` and `FloorPlanGeneration.Cli` must not. Keep geometry conversion, preview meshes, document layers, object attributes, and bake commands outside the core engine.

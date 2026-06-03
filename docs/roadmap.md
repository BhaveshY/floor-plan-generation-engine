# Roadmap

The current MVP is a headless, deterministic JSON-in/JSON-out engine for rectangular, L-shaped, and moderately irregular orthogonal floorplates with one fixed core. It is intentionally honest about invalid inputs and can validate contracts without generating variants.

The target product is more ambitious than this MVP. Floor Engine should become a graph-backed floor plan studio where AI prompts and mouse edits modify the same mathematical plan model. When a user drags a wall, increases a bedroom, moves a kitchen, locks a room, or asks the AI to adjust a 2BHK layout, the app should resize and rebalance neighboring spaces without breaking containment, adjacency, circulation, facade access, room minimums, or locked constraints.

Completed product contract work:

- Published Draft 2020-12 JSON Schema artifacts for input and output under `schemas/`.
- CLI schema tooling via `--print-input-schema` and `--print-output-schema`.
- Schema version `1.2` policy documented in `schemas/README.md`.
- Stable `externalId` values on generated variants, elements, topology nodes, and topology edges.
- Layer and external-id validation checks on generated variants.
- Door host-wall and connected-space validation checks on generated variants.
- GitHub Pages workflow for stable and versioned schema publication paths.
- Local web workbench with sample loading, JSON file import, autosaved drafts, JSON editing, validation, generation, SVG preview/export, diagnostics, and JSON export.
- AI-friendly CLI manifest via `--ai-manifest` / `--describe` for Codex, Claude Code, and other automation tools.
- Portable hypergraph output compatible with a `DataNode` JSON shape, including recursive tree, flattened nodes, multi-member hyperedges, incidence records, and subdivision/adjacency/area/angle/incidence matrices.
- Compact golden contract fixture coverage for rectangular, L-shaped, moderately irregular, and infeasible samples.
- Rhino/Grasshopper adapter contract documentation without adding RhinoCommon to the core.
- Initial named `PlanOperation` bridge through the core engine, CLI, local web API, and browser edit flow.

P0 remaining work:

1. Build a live graph editing kernel. The source of truth must be a `DataNode`/hypergraph-backed design graph, not SVG geometry or a generated variant snapshot.
2. Add constraint-preserving plan operations: move wall, resize room, split room, merge rooms, add/remove door, move kitchen, lock room, preserve wet wall, improve daylight, shorten corridor, and rebalance apartment program.
3. Route both mouse edits and AI prompts through the same operation API: `PlanOperation -> graph transaction -> constraint projection -> solver -> validation -> commit/reject/alternatives`.
4. Implement local re-solving so increasing one room automatically redistributes area from adjacent rooms/corridor bands while preserving graph connectivity, minimum sizes, containment, facade access, circulation access, door access, and locked elements.
5. Add operation history, undo/redo, named branches, and side-by-side alternatives so the user can explore options without losing a valid plan.
6. Port or wrap the useful `ramonweber/hypergraph` functionality as a safe adapter: reference layout transfer, split-tree replay, facade/circulation orientation scoring, room adjacency checks, and sample graph import/export.
7. Make the web studio manipulate walls, rooms, doors, dimensions, constraints, and graph overlays directly, with immediate validity feedback and graceful alternatives when an edit cannot be satisfied.
8. Expose the same operation API through the CLI so Claude Code, Codex, Rhino, Grasshopper, and scripts can generate, modify, validate, and export plans without scraping UI state.

Highest-value follow-up work:

1. Replace the interval-based candidate splitter with a stronger polygon partitioning/optimization layer for highly concave plans, multiple cores, and competing corridor strategies.
2. Expand validation to egress distance, travel paths, accessible clearances, shaft/fire separation, facade/window ratios, and local code profiles.
3. Add explicit Rhino/Grasshopper adapter projects for `FP Boundary`, `FP Fixed Elements`, `FP Generate`, `FP Diagnostics`, `FP Variant Picker`, `FP Hypergraph`, and `FP Bake Variant`.
4. Add IFC/BIM adapter output and IFC-compatible GUID derivation from stable `externalId` values while keeping RhinoCommon and IFC dependencies out of the core engine.
5. Broaden golden contract fixtures as new geometry strategies and validation profiles become externally stable.
6. Add signed desktop installers or published binaries around the web app once the UX stabilizes.

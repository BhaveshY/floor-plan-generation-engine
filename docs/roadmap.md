# Roadmap

The current MVP is a headless, deterministic JSON-in/JSON-out engine for rectangular, L-shaped, and moderately irregular orthogonal floorplates with one fixed core. It is intentionally honest about invalid inputs and can validate contracts without generating variants.

Completed product contract work:

- Published Draft 2020-12 JSON Schema artifacts for input and output under `schemas/`.
- CLI schema tooling via `--print-input-schema` and `--print-output-schema`.
- Schema version `1.1` policy documented in `schemas/README.md`.
- Stable `externalId` values on generated variants, elements, topology nodes, and topology edges.
- Layer and external-id validation checks on generated variants.
- Door host-wall and connected-space validation checks on generated variants.
- GitHub Pages workflow for stable and versioned schema publication paths.
- Compact golden contract fixture coverage for rectangular, L-shaped, moderately irregular, and infeasible samples.
- Rhino/Grasshopper adapter contract documentation without adding RhinoCommon to the core.

Highest-value remaining work:

1. Replace the interval-based candidate splitter with a stronger polygon partitioning/optimization layer for highly concave plans, multiple cores, and competing corridor strategies.
2. Expand validation to egress distance, travel paths, accessible clearances, shaft/fire separation, facade/window ratios, and local code profiles.
3. Add explicit Rhino/Grasshopper adapter projects for `FP Boundary`, `FP Fixed Elements`, `FP Generate`, `FP Diagnostics`, `FP Variant Picker`, and `FP Bake Variant`.
4. Add IFC/BIM adapter output and IFC-compatible GUID derivation from stable `externalId` values while keeping RhinoCommon and IFC dependencies out of the core engine.
5. Broaden golden contract fixtures as new geometry strategies and validation profiles become externally stable.

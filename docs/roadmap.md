# Roadmap

The current MVP is a headless, deterministic JSON-in/JSON-out engine for rectangular, L-shaped, and moderately irregular orthogonal floorplates with one fixed core. It is intentionally honest about invalid inputs and can now validate contracts without generating variants.

Highest-value remaining work:

1. Add a published JSON Schema artifact and versioned schema migration policy. The CLI rejects unknown properties and invalid contract values today, but there is not yet a standalone `.schema.json` file for external tooling.
2. Replace the interval-based candidate splitter with a stronger polygon partitioning/optimization layer for highly concave plans, multiple cores, and competing corridor strategies.
3. Expand validation to egress distance, travel paths, accessible clearances, shaft/fire separation, facade/window ratios, and local code profiles.
4. Add explicit Rhino/Grasshopper adapter projects for `FP Boundary`, `FP Fixed Elements`, `FP Generate`, `FP Diagnostics`, `FP Variant Picker`, and `FP Bake Variant`.
5. Add stable external ids/GUIDs and optional IFC/BIM adapter output while keeping RhinoCommon and IFC dependencies out of the core engine.
6. Add golden JSON fixtures for representative seeds once the output contract is ready to be treated as externally stable.

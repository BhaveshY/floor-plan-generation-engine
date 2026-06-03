# Product Vision Mockups

Generated concept images for the final Floor Plan Engine web app direction.

- `floor-engine-final-workbench.png` - complete desktop planning workbench.
- `floor-engine-guided-setup.png` - nontechnical guided setup flow.
- `floor-engine-plan-review.png` - plan review, variants, issues, unit schedule, and exports.
- `floor-engine-ai-export-hypergraph.png` - AI/CLI export, Rhino/IFC outputs, and hypergraph inspection.
- `floor-engine-plan-studio-main.png` - target canvas-first Plan Studio.
- `floor-engine-direct-editing.png` - direct wall, room, and constraint editing target.
- `floor-engine-spatial-graph-branches.png` - hypergraph branches, alternatives, and validation target.
- `floor-engine-rhino-handoff.png` - Rhino/Grasshopper handoff and baking target.
- `floor-engine-ai-command-session.png` - AI command flow for prompt edits and operation history.

These are visual targets, not literal screenshots of the current implementation.

The core interaction target is a smart graph-backed plan editor. After generating or loading a floor plan, the user should be able to drag walls, resize rooms, move doors, lock spaces, and issue AI instructions while the engine automatically rebalances neighboring spaces. The UI should feel simple, but every edit must resolve through the same mathematical model: live `DataNode`/hypergraph operations, constraint projection, solver regeneration, validation, and undoable commits.

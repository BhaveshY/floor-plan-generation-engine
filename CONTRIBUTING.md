# Contributing

Thanks for improving the floor plan generation engine.

## Development

Use the solution-level commands before opening a pull request:

```bash
dotnet restore FloorPlanGeneration.sln
dotnet build FloorPlanGeneration.sln --configuration Release --no-restore
dotnet test FloorPlanGeneration.sln --configuration Release --no-build
dotnet pack FloorPlanGeneration.sln --configuration Release --no-build
```

## Contract Changes

- Keep `schemas/*.schema.json`, DTOs, sample inputs, and golden contract fixtures in sync.
- Add or update tests for new diagnostics, validation checks, generated fields, and schema behavior.
- Breaking JSON changes require a major schema version, migration notes, and new versioned schema URLs.

## Scope

Keep RhinoCommon, Grasshopper SDK, IFC, and other adapter dependencies outside the core engine and CLI unless a dedicated adapter project is added.

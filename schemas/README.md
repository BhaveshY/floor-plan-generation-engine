# Floor Plan Engine Schemas

Published JSON Schema artifacts:

- `floor-plan-engine-input.schema.json`: accepted `EngineInput` contract.
- `floor-plan-engine-output.schema.json`: emitted `EngineOutput` contract.

The current contract version is `1.1`. The stable file names above point at the current supported contract; each schema also carries a versioned `$id` under `/schemas/1.1/`, and engine output reports the same version in `metadata.schemaVersion`.

The `Publish Schemas` GitHub Actions workflow publishes both stable and versioned paths to GitHub Pages:

- `/schemas/floor-plan-engine-input.schema.json`
- `/schemas/floor-plan-engine-output.schema.json`
- `/schemas/1.1/floor-plan-engine-input.schema.json`
- `/schemas/1.1/floor-plan-engine-output.schema.json`

If this is the first Pages deployment for the repository, configure Pages to use GitHub Actions as the source, then run the workflow once.

## CLI

Print the current schemas without reading engine input:

```sh
dotnet run --project FloorPlanGeneration.Cli -- --print-input-schema
dotnet run --project FloorPlanGeneration.Cli -- --print-output-schema
```

Write a schema artifact to a file:

```sh
dotnet run --project FloorPlanGeneration.Cli -- --print-input-schema --output /tmp/floor-plan-engine-input.schema.json
```

## Version Policy

- Patch releases may clarify descriptions or tighten tests without changing accepted or emitted JSON.
- Minor releases may add optional input fields or additive output fields. Consumers should ignore unknown output fields unless pinned to an exact schema.
- Breaking changes require a major version, a new `$id`, and migration notes before replacing the stable schema file names.
- The CLI remains strict for input JSON: unknown input properties are rejected so misspelled contract fields do not silently fall back to defaults.

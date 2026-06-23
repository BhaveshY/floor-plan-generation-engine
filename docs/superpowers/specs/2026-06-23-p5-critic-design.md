# P5 — Critic (design)

Date: 2026-06-23
Track: architectural-finetuning (deterministic, rule-based). Phases 0–4 done.
Status: approved to implement under the standing /goal directive ("implement
everything as suited best … keep working"). Design decisions delegated to the
implementer; flag-gated and default-off so the result is reviewable post-hoc
with zero effect on existing output.

## Goal (from the roadmap)

> P5 — critic. Automated quality gate that flags weak variants against the rule
> set (daylight, egress, proportion, adjacency).

The engine's existing `ValidationReport` is a **hard** gate: an error check fails
and the variant is marked `failed`. P5 adds a **soft** gate — a critic that
inspects each variant across four quality dimensions and emits findings for the
weak ones, *without* changing pass/fail, ordering, or geometry. A variant can
pass validation yet still be flagged by the critic (e.g. an awkwardly long room,
or a unit whose only door doesn't reach circulation). Deterministic, rule-based,
no ML.

## Constraints (AGENTS.md)

- **Determinism**: the critique is a pure function of the variants' geometry and
  metadata; no randomness.
- **Byte-identity**: with the flag off the output is byte-identical to today. The
  frozen-hash byte-identity test and `golden-contracts.json` must pass
  **unchanged**. The output serializer does **not** ignore nulls, so the new
  `critique` property carries `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`
  and is omitted entirely when null (flag off). The flag is **not** copied into
  the `metadata.generationSettings` summary.
- **Source style**: max line length 160 across `.cs`; explicit types, no `var`.

## Rollout: one opt-in flag (default false)

`GenerationSettings.CritiqueVariants` (bool, default `false`) — like the P4
recommender this is output annotation, so it lives in `GenerationSettings` next
to `recommendVariant`, not in `RuleSet`. Added to the `generationSettings` block
of the input schema (additive, optional, no version bump). When off,
`EngineOutput.Critique` stays null and is omitted.

## Output shape (additive, optional)

`EngineOutput.Critique` → `QualityCritique`:

- `flaggedVariantIds`: ids of variants with at least one finding (the weak ones).
- `variants`: one `VariantCritique` per variant, in the engine's output order:
  - `variantId`, `passed`, `findings` (possibly empty).
  - each `CritiqueFinding`: `dimension` (`daylight` | `egress` | `proportion` |
    `adjacency`), `severity` (`warning` | `info`), `message`, `score` (the
    dimension's [0,1] quality measure, or the offending ratio for proportion).

Every variant gets an entry (even clean ones, with empty findings) so the report
is a complete assessment, mirroring the recommender's full ranking. With at most
20 variants this stays small.

Added to `schemas/floor-plan-engine-output.schema.json` as an optional `critique`
property with `$defs/qualityCritique`, `$defs/variantCritique`, and
`$defs/critiqueFinding` (root keeps `additionalProperties:false`; `critique` is
not in `required`).

## Dimensions and thresholds

A `CritiqueThresholds` value type (with `Default()`, the `PortfolioPriors`
pattern) holds the gate levels; defaults are internal constants, not exposed via
input schema (YAGNI — the flag alone gates the feature). Habitable rooms are
`bedroom`/`living` by `RoomType`.

1. **daylight** (severity `warning`). score = habitable rooms with `Daylight` /
   habitable rooms (1.0 if none). Flagged when score < `DaylightFloor` (default
   1.0). Independent of `RequireDaylightFor*`, so it adds value even when those
   hard rules are off. Message: "N of M habitable rooms lack daylight."
2. **egress** (severity `warning`). Only evaluated when the variant has corridors
   (skipped in `single_dwelling`, which has none). score = units with a door
   whose `ConnectsSpaces` includes both the unit and a corridor id / total units.
   Flagged when score < `EgressFloor` (default 1.0). Message: "N of M units lack
   a door to circulation."
3. **proportion** (severity `info`). For each habitable room, aspect ratio =
   max(Bounds.Width, Bounds.Height) / min(...). score = habitable rooms with
   ratio ≤ `MaxAspectRatio` (default 3.0) / habitable rooms. Flagged when score <
   1.0. Message names the worst room and its ratio.
4. **adjacency** (severity `info`). score = `AdjacencyScorer.Score` (P3) over the
   variant's units. Flagged when score < `AdjacencyFloor` (default 0.5, the prior's
   neutral default). Message: "adjacency preference X below target 0.50."

Each dimension is a small private evaluator returning a nullable finding; the
critic gathers the non-null findings per variant. A dimension that doesn't apply
(no habitable rooms, no corridors, no interior adjacencies) emits no finding.

## Components

`VariantCritic` (public static, pure — the `VariantRecommender`/`AdjacencyScorer`
pattern):

```
QualityCritique Critique(IReadOnlyList<LayoutVariant> variants, PortfolioPriors priors,
                         double tolerance, CritiqueThresholds thresholds)
```

Wired in `FloorPlanEngine.Generate` after the sort/status (alongside the P4
hook): when `CritiqueVariants` is on, `output.Critique = VariantCritic.Critique(
output.Variants, PortfolioPriors.Default(), input.Tolerance, CritiqueThresholds.Default())`.

## Approaches considered

1. **(chosen) Additive, flag-gated `critique` object from a pure `VariantCritic`,
   soft findings that never change pass/fail or order.** Minimal, deterministic,
   byte-identical off-path, reuses existing signals (`Daylight`, doors/corridors,
   bounds, `AdjacencyScorer`).
2. **Fold the critique into `ValidationReport` as warning-severity checks.**
   Rejected: validation is consumed as the hard gate and is pinned by golden
   fixtures; widening it risks byte-identity and conflates hard/soft semantics.
3. **Always emit the critique (no flag).** Rejected: breaks the track's
   byte-identity discipline and would force re-baselining the frozen hash/goldens.

## Files touched

- `FloorPlanGeneration/Generation/VariantCritic.cs` — pure critic + dimension evaluators.
- `FloorPlanGeneration/Schema/EngineSchema.cs` — `CritiqueVariants` flag +
  `QualityCritique` / `VariantCritique` / `CritiqueFinding` / `CritiqueThresholds`
  types + `EngineOutput.Critique` (`JsonIgnore` when-writing-null).
- `FloorPlanGeneration/FloorPlanEngine.cs` — gated wiring after the sort.
- `schemas/floor-plan-engine-input.schema.json` — `critiqueVariants`.
- `schemas/floor-plan-engine-output.schema.json` — optional `critique` + `$defs`.
- `FloorPlanGeneration.Tests/VariantCriticTests.cs` — unit tests.
- `FloorPlanGeneration.Tests/VariantCriticEngineTests.cs` — engine + serialization tests.

## Testing

- **No regression (off-path)**: frozen-hash and `golden-contracts.json` pass
  **unchanged** (flag default off ⇒ `critique` omitted).
- **Critic (pure)**: a synthetic variant with a non-daylit habitable room is
  flagged on `daylight`; a long room is flagged on `proportion`; a unit with no
  corridor door is flagged on `egress`; a low-adjacency layout is flagged on
  `adjacency`; a clean variant has no findings and is absent from
  `flaggedVariantIds`; thresholds are honoured.
- **On-path engine**: with the flag on, `critique` is present, covers every
  variant in order, and same seed + flag ⇒ identical critique.
- **Serialization**: flag off ⇒ no `critique` key; flag on ⇒ present and validates
  against the output schema.

## Risks

- Adding an output field risks byte-identity → resolved by `JsonIgnore`
  when-writing-null + keeping the flag out of the metadata summary; covered by the
  frozen-hash test staying green.
- Threshold/message wording is cosmetic → kept short, deterministic, asserted on
  dimension/severity/flagged-state, not exact prose.
- Over-flagging (e.g. daylight floor 1.0) → defaults are documented and live in
  `CritiqueThresholds`; the critic is opt-in and never changes pass/fail, so a
  noisy finding is advisory only.

# P4 — Recommender (design)

Date: 2026-06-23
Track: architectural-finetuning (deterministic, rule-based). Phases 0–3 done.
Status: approved to implement under the standing /goal directive ("implement
everything as suited best … keep working"). Design decisions delegated to the
implementer; flag-gated and default-off so the result is reviewable post-hoc
with zero effect on existing output.

## Goal (from the roadmap)

> P4 — recommender. Rank the generated variants per brief and surface the best
> one.

The engine already sorts variants by `(validation passed, score desc, id)`, so a
ranking and a "best" exist implicitly. What P4 adds is an **explicit, explainable
recommendation**: a machine-readable pointer to the recommended variant, a 1-based
ranking with each variant's score and what it leads on, and a short human
rationale. The ranking is already "per brief" because the score folds in the
brief-supplied `scoringWeights`. Deterministic and rule-based — no ML.

## Constraints (AGENTS.md)

- **Determinism**: the recommendation is a pure function of the variants' metrics;
  no randomness.
- **Byte-identity**: with the flag off the output is byte-identical to today. The
  frozen-hash byte-identity test and `golden-contracts.json` must pass
  **unchanged**. The output serializer does **not** ignore nulls, so the new
  `recommendation` property carries
  `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` and is therefore
  omitted entirely when null (flag off) rather than emitted as `"recommendation":null`.
- **Source style**: max line length 160 across `.cs`; explicit types, no `var`.

## Rollout: one opt-in flag (default false)

`GenerationSettings.RecommendVariant` (bool, default `false`) — the recommender
is output annotation, so it lives with the other output/generation controls in
`GenerationSettings` (next to `weightedVariation`, `scoringWeights`), not in
`RuleSet`. Added to the `generationSettings` block of the input schema (additive,
optional, no version bump). It is **not** copied into the output
`metadata.generationSettings` summary, so the off-path output is unchanged.

When off, `EngineOutput.Recommendation` stays null and is omitted.

## Output shape (additive, optional)

`EngineOutput.Recommendation` → `VariantRecommendation`:

- `recommendedVariantId`: the surfaced best variant's id.
- `rationale`: one human-readable sentence naming the variant, its leading
  criteria, score, and (when relevant) that no variant passed validation.
- `ranking`: list of `VariantRanking` in output order:
  - `variantId`, `rank` (1-based), `score`, `passed`, `highlights` (e.g.
    `["highest efficiency", "best unit-mix match"]`).

Added to `schemas/floor-plan-engine-output.schema.json` as an optional
`recommendation` property with a `$defs/recommendation` + `$defs/variantRanking`
(root keeps `additionalProperties:false`; `recommendation` is not in `required`).

## Components

`VariantRecommender` (public static, pure — the `RoomProportions`/`AdjacencyScorer`
pattern):

```
VariantRecommendation Recommend(IReadOnlyList<LayoutVariant> rankedVariants)
```

- **Recommended**: the first variant whose validation passed; if none passed, the
  first variant (already the top-scored by the engine's sort).
- **Highlights**: for each leading metric (efficiency, net-gross ratio,
  unit-mix match), the variant(s) achieving the max value (within a small
  tolerance) get the corresponding label. Deterministic; ties all get the label.
- **Ranking**: variants in the given (already-sorted) order, `rank = 1..N`,
  carrying `score`/`passed`/`highlights`.
- **Rationale**: built from the recommended variant's highlights + score + variant
  count; a distinct phrasing when no variant passed validation.

Wired in `FloorPlanEngine.Generate` after the variants are sorted and the status
is set: when `RecommendVariant` is on, `output.Recommendation =
VariantRecommender.Recommend(output.Variants)`.

## Approaches considered

1. **(chosen) Additive, flag-gated `recommendation` object computed by a pure
   `VariantRecommender` over the already-sorted variants.** Minimal, deterministic,
   byte-identical off-path, and explainable.
2. **Re-rank with a separate recommender weighting independent of the score.**
   Rejected for P4: the score already encodes the brief weights; a second,
   divergent ranking would confuse consumers. (A learned/blended re-ranker is a
   future P4.x if needed.)
3. **Always emit the recommendation (no flag).** Rejected: breaks the track's
   byte-identity discipline and would force re-baselining the frozen hash/goldens.

## Files touched

- `FloorPlanGeneration/Generation/VariantRecommender.cs` — pure recommender.
- `FloorPlanGeneration/Schema/EngineSchema.cs` — `RecommendVariant` flag +
  `VariantRecommendation` / `VariantRanking` types + `EngineOutput.Recommendation`
  (with `JsonIgnore` when-writing-null).
- `FloorPlanGeneration/FloorPlanEngine.cs` — gated wiring after the sort.
- `schemas/floor-plan-engine-input.schema.json` — `recommendVariant`.
- `schemas/floor-plan-engine-output.schema.json` — optional `recommendation`.
- `FloorPlanGeneration.Tests/VariantRecommenderTests.cs` — unit + engine tests.

## Testing

- **No regression (off-path)**: frozen-hash byte-identity test and
  `golden-contracts.json` pass **unchanged** (flag default off ⇒ `recommendation`
  omitted from the serialized output).
- **Recommender (pure)**: recommended is the top passing variant; falls back to
  the first when none pass; ranking is 1..N in order with scores carried;
  the metric leader earns the right highlight; rationale is non-empty and names
  the recommended variant.
- **On-path engine**: with the flag on, `recommendation` is present,
  `recommendedVariantId` equals the first passing variant, and `ranking` covers
  every variant; same seed + flag ⇒ identical recommendation.
- **Serialization**: flag off ⇒ the serialized output contains no
  `recommendation` key; flag on ⇒ it does and validates against the output schema.

## Risks

- Adding an output field risks breaking byte-identity → resolved by the
  `JsonIgnore` when-writing-null attribute plus keeping it out of the metadata
  summary; covered by the frozen-hash test staying green.
- Rationale wording is cosmetic → kept short, deterministic, and asserted only for
  non-emptiness and the variant id, not exact prose.

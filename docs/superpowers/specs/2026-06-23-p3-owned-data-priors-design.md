# P3 — Owned-Data Priors (design)

Date: 2026-06-23
Track: architectural-finetuning (deterministic, rule-based). Phases 0, 1, 2 done.
Status: approved to implement under the standing /goal directive ("implement
everything as suited best according to the plan and the material shared … keep
working till everything is properly implemented"). Design decisions delegated to
the implementer; flag-gated and default-off so the result is safely reviewable
post-hoc with zero effect on existing behaviour.

## Goal (from the roadmap)

> P3 — owned-data priors. Bias proportions/adjacencies toward EBA's own past
> projects (sidesteps the AGPL / CC-BY-NC / portal-scraping blockers). **Priors,
> not embedded ML.**

So: gently pull the engine's room/unit **proportions** and room **adjacencies**
toward values typical of EBA's own portfolio, using static statistical priors —
no learned model, no runtime corpus dependency, fully deterministic.

## What a "prior" is here

A prior is a compact statistical summary over a corpus of plans, stored as a
versioned JSON asset committed to the repo and loaded deterministically. No
training and no inference — just typical values the engine is nudged toward.

The asset is the single source of "owned-ness". Today it ships with **seed
values distilled from published German residential norms** (Neufert
"Bauentwurfslehre", DIN 18040-2, VDI 6000) and is **clearly labelled as a
placeholder for EBA-portfolio statistics** — it is not represented as measured
from EBA projects. A documented regeneration contract (below) lets real EBA data
replace the asset later **without any code change**.

## Constraints (AGENTS.md)

- **Determinism**: all randomness flows from `SeededRandom`; priors are static
  data, so every prior-driven path is deterministic.
- **Byte-identity**: the opted-out path (`usePortfolioPriors = false`) stays
  byte-identical to today. The frozen
  `MultiUnitMode_FurnitureMinimumsOff_GeometryIsByteIdenticalToFrozenBaseline`
  hash and `golden-contracts.json` must pass **unchanged**.
- **No new runtime dependencies / no package managers**: the asset is plain JSON
  read through the existing `System.Text.Json`, embedded as a resource so the
  engine needs no working-directory assumption.
- **Source style**: max line length 160 across `.cs`; explicit types, no `var`.

## Rollout: one opt-in `RuleSet` flag (default false)

Mirror the Phase 0/1/2 pattern exactly (`GridModule`, `ApplyFurnitureMinimums`,
`CorridorSpine`, `DriftToMargin`):

- `RuleSet.UsePortfolioPriors` (bool, default `false`).

Set the default in the `RuleSet` constructor and add `usePortfolioPriors` to the
`rules` block of `schemas/floor-plan-engine-input.schema.json` (additive,
optional, no schema version bump — same as the prior flags under 1.2). When the
flag is off, every code path below is the existing path verbatim.

## The priors asset

`FloorPlanGeneration/Data/portfolio-priors.json`, embedded as a resource and
committed to the repo. Versioned with `priorsVersion: 1`. Shape:

- `unitAreas`: map `unitType` → `{ meanArea, minTypical, maxTypical }` in m².
- `facadeBand`: map `unitType` → `{ wetBandDepthFraction, shares: [ { roomType,
  share } … ] }` where `shares` are the typical widths of the facade-band rooms
  as fractions that sum to ~1 for that unit type.
- `roomAspects`: map `roomType` → `typicalAspect` (long:short).
- `adjacency`: list of `{ a, b, weight }` with `weight` in `[0,1]`, the typical
  neighbour affinity for the unordered room-type pair `{a,b}`. Missing pairs are
  treated as a neutral default (configurable `defaultWeight`, e.g. 0.5).

A `PortfolioPriors` class loads and validates the asset and exposes typed
lookups (`UnitAreaMean(type)`, `FacadeShares(unitType)`, `AdjacencyWeight(a,b)`,
…). Validation is strict and fails with a clear engine error **only when the flag
is on** (the asset is never read when off). Loading is cached/static so repeated
generations don't re-parse.

### Regeneration contract (deferred, documented only)

When EBA project files become available, a future offline pass extracts, per
plan: unit areas by type, facade-band room width shares by type, room aspect
ratios, and room-adjacency counts (shared interior wall ⇒ a neighbour pair). It
aggregates these (mean / robust quantiles / normalised co-occurrence) into the
exact JSON shape above and overwrites the asset. No parser is built now — there
is no corpus in the repo and no package manager to add one — but the asset shape
is fixed so the ingestion has a stable target.

## Lever A — proportion pull (generation; geometry changes when on)

New pure helper, sibling to the Phase-1 routines in `RoomProportions`:

```
double[] PullToTargets(widths, targetShares, minWidths, maxWidths, strength)
```

Moves each segment from its current width toward `total * targetShare`, scaled by
`strength` in `[0,1]`, then projects back so the sum is preserved exactly and no
segment leaves `[min, max]` (a non-positive bound means unconstrained on that
side). Deterministic, no RNG, returns a new array, input unchanged — same
discipline as `GrowToMinimums` / `ShrinkToMaximums`.

Applied **only when `UsePortfolioPriors` is on**:

1. **Facade-band split** (`RoomTemplateGenerator`): after the existing Phase-1
   min/max pass (`GrowFacadeBand`), pull the facade-band room widths toward
   `facadeBand.shares` for the unit type. Minimums still win (the pull is
   clamped), band edges never move, areas still sum to the unit.
2. **Unit bay target area** (`UnitMixPlanner` / `CandidateGenerator`): bias the
   chosen bay's desired area toward `unitAreas.meanArea` for the type instead of
   the `(minArea + maxArea) / 2` midpoint.

Because this shifts the on-path geometry (and the on-path RNG stream is its own
tested baseline, exactly like P2 drift), the off-path is unaffected.

## Lever B — adjacency match (scoring; ranking changes when on, geometry unchanged)

Add an `adjacencyMatch` term to `ScoreVariant`:

- For every interior wall shared by two rooms (reuse the shared-segment predicate
  already used to emit room-partition walls), look up `adjacency.AdjacencyWeight`
  for that room-type pair. The term is the mean preference over all realized
  interior adjacencies, `Clamp01`-ed; a variant with no interior adjacencies
  scores a neutral `defaultWeight`.
- Fold it into the weighted sum with default weight **0.0 when the flag is off**
  (so the score is byte-identical: `+ 0.0 * term` and `weightTotal + 0.0` change
  nothing), and a small positive default (**0.10**, with the existing
  normalisation by `weightTotal` absorbing the extra weight) **only when the flag
  is on**. The term is computed only when its weight is positive, so the asset is
  never touched off-path.

`ScoreVariant` already receives `CleanedInput`, so it reads
`Source.Rules.UsePortfolioPriors` and the loaded priors from there; no signature
churn beyond passing the priors object.

## Approaches considered

1. **(chosen) Static committed priors asset + deterministic proportion pull +
   adjacency scoring term, flag-gated.** Honest about provenance, zero runtime
   dependencies, byte-identical off-path, and a direct extension of the Phase-1
   data-table + sum-preserving-transform pattern already in the codebase.
2. **Runtime corpus ingestion** (parse DXF/IFC/CAD at generation time). Rejected:
   needs parsers, the corpus present on disk, and risks nondeterminism; no corpus
   is available and no package manager exists to add a parser.
3. **Embedded learned model.** Explicitly rejected by the roadmap ("priors, not
   embedded ML").

## Files touched

- `FloorPlanGeneration/Data/portfolio-priors.json` — new embedded asset.
- `FloorPlanGeneration/Generation/PortfolioPriors.cs` — loader + typed lookups.
- `FloorPlanGeneration/Geometry/RoomProportions.cs` — new `PullToTargets`.
- `FloorPlanGeneration/Schema/EngineSchema.cs` — `UsePortfolioPriors` + default.
- `schemas/floor-plan-engine-input.schema.json` — `usePortfolioPriors`.
- `FloorPlanGeneration/Generation/RoomTemplateGenerator.cs`,
  `UnitMixPlanner.cs`, `CandidateGenerator.cs` — gated proportion pull.
- `FloorPlanGeneration/FloorPlanEngine.cs` — gated adjacency scoring term.
- `FloorPlanGeneration.Tests/PortfolioPriorsTests.cs` — new on-path tests.

## Testing

- **No regression (off-path)**: frozen-hash byte-identity test and
  `golden-contracts.json` pass **unchanged** (flag default off; samples don't set
  it).
- **`PullToTargets` (pure)**: sum preserved to tolerance; no segment leaves
  `[min,max]`; output moves toward targets; no-op when already on target or when
  `strength = 0`; degenerate inputs (single segment, all-equal, zero total)
  handled.
- **Asset**: loads and validates; a malformed/missing asset throws a clear error
  **only when the flag is on**.
- **On-path determinism**: same seed + flag on → byte-identical across two runs.
- **Proportion property**: with the flag on, facade-band room width ratios move
  measurably toward the priors versus off, while room areas still sum to the unit
  and Phase-1 minimums still hold.
- **Adjacency property**: a variant richer in prior-preferred adjacencies scores
  `>=` one poorer in them (flag on); with the flag off the score is identical to
  the historic baseline.
- **Validity**: all variants still pass validation with the flag on.

## Risks

- The pull could fight Phase-1 minimums on tiny plates → resolved by clamping to
  `[min,max]` with sum-preserving projection: minimums win, the pull is
  best-effort.
- The adjacency term could be mis-weighted → small default weight, ranking-only
  effect, flag-gated.
- Seed priors are not literally EBA data → labelled a placeholder with a
  regeneration contract; swapping in real statistics needs no code change.

# P2 — Corridor Spine + Drift-to-Margin (design)

Date: 2026-06-23
Track: architectural-finetuning (deterministic, rule-based). Phase 0 & 1 done.
Status: approved, ready for implementation.

## Goal

Make floor-plan circulation *deliberate* and stop leftover slack from forming
slivers in the middle of the plan. Two independent, pure-geometry rules, both
opt-in and off by default so historic output stays byte-identical:

1. **Corridor spine** — replace the seeded random pick among candidate corridor
   placements with a deterministic, geometry-scored ranked choice.
2. **Drift-to-margin** — replace the random bay-fill direction with one that
   anchors the cursor at the interior end of each band interval, so the leftover
   slack accrues to the building-perimeter bay instead of a bay jammed against
   the core mid-plan.

## Constraints (from AGENTS.md)

- **Determinism**: same input + seed must reproduce byte-identical variants. All
  randomness flows from the seeded `SeededRandom`. The on-path additions must be
  deterministic (no new entropy sources).
- **Byte-identity**: the opted-out path (`corridorSpine=false`,
  `driftToMargin=false`) must stay byte-identical to today. The frozen
  `MultiUnitMode_FurnitureMinimumsOff_GeometryIsByteIdenticalToFrozenBaseline`
  hash and `golden-contracts.json` must pass unchanged.
- **Source style**: max line length 160 across `.cs`; explicit types, no `var`.

## Rollout: two opt-in `RuleSet` flags (default false)

Mirror the Phase 0/1 pattern exactly (`GridModule = 0.0`,
`ApplyFurnitureMinimums = false`):

- `RuleSet.CorridorSpine` (bool, default `false`)
- `RuleSet.DriftToMargin` (bool, default `false`)

Set defaults in the `RuleSet` constructor and add `corridorSpine` /
`driftToMargin` to the `rules` block of
`schemas/floor-plan-engine-input.schema.json` (additive, optional, no schema
version bump — same as `applyFurnitureMinimums` under 1.2). Flags are
independent and composable; either can be enabled alone.

When both are off, every code path below is the existing path verbatim.

## (A) Corridor spine — `CandidateGenerator`

Today: `TryResolveCorridor` collects candidate placements in tiers
(core-adjacent → central fractions → secondary axis) and `PickCorridorCandidate`
returns `candidates[(variantIndex + random.Next(0, count)) % count]` — a seeded
random pick. Central-fraction candidates also receive per-variant jitter
(`random.Range(-0.025, 0.025)`), so each variant's pool differs slightly.

When `CorridorSpine` is **on**:

1. **Stable pool**: skip the per-variant jitter so every variant scores the same
   candidate pool. (Jitter only exists to diversify the random pick; ranked
   selection makes it unnecessary and would muddy the ranked semantics.)
2. **Score** each candidate with `SpineScore` (pure geometry, deterministic).
3. **Sort** candidates by score descending with a deterministic tie-break, then
   variant *i* selects `sorted[i % count]`. No `SeededRandom` call in the pick.
   Result: variant-01 = best spine, variant-02 = 2nd-best, … wrapping around.

`SpineScore` (reuses existing `CorridorBandDepths` / `MaxUsefulBandDepth`):

- **Double-loaded** dominates: large bonus when both bands host units (both
  `>= bandThreshold`); smaller bonus for single-loaded.
- **Balance**: `1 − |nearDepth − farDepth| / (nearDepth + farDepth)` — rewards a
  corridor sitting between two comparable bands.
- **Proportion penalty**: subtract for any band deeper than `MaxUsefulBandDepth`
  (oversized units), scaled by the overflow fraction.
- **Core-adjacency** bonus when the corridor touches the core.
- **Tie-break**: deterministic by orientation then centerline coordinate, so
  equal-scoring candidates order stably.

When `CorridorSpine` is **off**: jitter + `PickCorridorCandidate` random pick,
untouched.

Out of scope (future P2.x): widening the candidate pool beyond the existing
core-adjacent / central tiers.

## (B) Drift-to-margin — `SplitHorizontalInterval` / `SplitVerticalInterval`

Today: `bool reverse = random.NextDouble() < 0.5; cursor = reverse ? maxX : minX`.
Bays are walked from `cursor`; the last bay absorbs the remainder
(`if (remaining - desiredWidth < MinAnyUnitWidth) desiredWidth = remaining`), so
the slack lands wherever the walk terminates — random, and possibly against the
core mid-plan when a fixed element splits the band into intervals.

When `DriftToMargin` is **on**:

- Classify each interval end as a **margin** (touches the floorplate
  perimeter, within tolerance) or **interior** (abuts the core / a fixed
  element). Anchor the cursor at the interior end so the walk terminates at the
  perimeter and the terminal (margin) bay absorbs the remainder. Interior bays
  keep clean grid-module widths (Phase 0 `SnapBayWidth`).
- Direction rule for a horizontal interval `[start (minX), end (maxX)]` clipped
  to `[corridor.MinX, corridor.MaxX]`, with axis bounds `[plate.MinX, plate.MaxX]`:
  - low end margin & high end interior → `reverse = true` (start high/interior,
    terminate low/margin);
  - high end margin & low end interior → `reverse = false`;
  - both margin or both interior → deterministic default `reverse = false`.
  (Vertical interval is the Y-axis analogue.)
- The deterministic `reverse` **replaces** the random draw (the draw existed only
  to choose `reverse`); the on-path therefore does not consume that random. This
  shifts the on-path RNG stream relative to the off-path, which is fine — the
  on-path is its own tested baseline and the off-path is unchanged.

When `DriftToMargin` is **off**: random `reverse`, untouched.

## Files touched

- `FloorPlanGeneration/Schema/EngineSchema.cs` — two flags + defaults.
- `schemas/floor-plan-engine-input.schema.json` — two `rules` properties.
- `FloorPlanGeneration/Generation/CandidateGenerator.cs` — `SpineScore`, ranked
  selection branch, gated jitter, deterministic `reverse` for drift.
- `FloorPlanGeneration.Tests/CorridorSpineTests.cs` — new on-path tests.

## Testing

- **No regression (off-path)**: existing frozen-hash byte-identity test and
  `golden-contracts.json` must pass **unchanged** (flags default off; samples do
  not set them).
- **Determinism (on-path)**: same seed + flags on → byte-identical across two runs.
- **Ranked spine**: with `corridorSpine` on, variant-01's corridor centerline is
  the top-`SpineScore` candidate, and across variants the chosen placements
  follow score order (no dependence on `SeededRandom` for the pick).
- **Drift property**: with `driftToMargin` on, on the rectangular sample (central
  core splitting the band), the bay abutting the core is a clean grid-module
  width and the bay at the floorplate perimeter carries the slack; assert no
  sub-`MinAnyUnitWidth` bay sits against the core (no mid-plan sliver).
- **Composition**: both flags on produces valid, deterministic, all-passing
  variants.
- All validation invariants (areas, axis-aligned walls, doors on host walls,
  nothing overlapping the core) continue to hold on the on-path.

## Risks

- A candidate pool of size 1–2 (typical with a central core) means several of
  the N variants share the top spine; unit-packing variety still differentiates
  them. Acceptable and matches the "deliberate spine" intent.
- If `SpineScore` mis-ranks, ranked-per-variant still surfaces alternatives in
  lower variants — a safety net the future recommender (P4) / critic (P5) use.

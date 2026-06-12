using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FloorPlanGeneration.Generation;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;
using FloorPlanGeneration.Topology;
using FloorPlanGeneration.Validation;

namespace FloorPlanGeneration
{
    /// <summary>
    /// Public orchestration entry point for the floor plan generation MVP.
    /// </summary>
    public sealed class FloorPlanEngine
    {
        public EngineOutput Generate(EngineInput input)
        {
            try
            {
                return GenerateCore(input);
            }
            catch (Exception)
            {
                return new EngineOutput
                {
                    ProjectId = ResolveProjectId(input),
                    Status = "failed",
                    Diagnostics = new List<Diagnostic>
                    {
                        Diagnostic.Error(
                            "engine.exception",
                            "Floor plan generation failed unexpectedly. Review the input contract and try again.")
                    }
                };
            }
        }

        public EngineOutput Run(EngineInput input)
        {
            return Generate(input);
        }

        public EngineOutput Validate(EngineInput input)
        {
            try
            {
                return ValidateCore(input);
            }
            catch (Exception)
            {
                return new EngineOutput
                {
                    ProjectId = ResolveProjectId(input),
                    Status = "failed",
                    Diagnostics = new List<Diagnostic>
                    {
                        Diagnostic.Error(
                            "engine.exception",
                            "Floor plan validation failed unexpectedly. Review the input contract and try again.")
                    }
                };
            }
        }

        private EngineOutput GenerateCore(EngineInput input)
        {
            EngineOutput output = new EngineOutput
            {
                ProjectId = ResolveProjectId(input),
                Status = "failed"
            };

            if (input == null)
            {
                output.Diagnostics.Add(Diagnostic.Error("input.null", "Engine input cannot be null."));
                return output;
            }

            output.Diagnostics.AddRange(InputContractValidator.Validate(input));
            if (HasErrors(output.Diagnostics))
            {
                output.Status = "failed";
                output.Metadata = BuildMetadata(input, null);
                return output;
            }

            List<Diagnostic> defaultsDiagnostics = new List<Diagnostic>();
            EnsureDefaults(input, defaultsDiagnostics);
            output.ProjectId = ResolveProjectId(input);
            output.Diagnostics.AddRange(defaultsDiagnostics);

            CleanedInput cleaned = CleanInput(input);
            output.Diagnostics.AddRange(cleaned.Diagnostics);
            output.Metadata = BuildMetadata(input, cleaned);
            if (HasErrors(output.Diagnostics))
            {
                output.Status = "failed";
                return output;
            }

            output.Diagnostics.AddRange(AnalyzeFeasibility(cleaned));
            if (HasErrors(output.Diagnostics))
            {
                output.Status = "failed";
                return output;
            }

            CandidateGenerator generator = new CandidateGenerator(cleaned);
            List<LayoutVariant> candidates = generator.Generate();
            output.Diagnostics.AddRange(generator.Diagnostics);
            if (candidates.Count == 0)
            {
                output.Diagnostics.Add(Diagnostic.Error("generation.no_candidates", "Candidate generation produced no variants."));
                output.Status = "failed";
                return output;
            }

            foreach (LayoutVariant variant in candidates)
            {
                NormalizeVariant(variant, output.ProjectId);
                ValidateVariant(variant, cleaned, generator.MixPlanner);
                ScoreVariant(variant, cleaned, generator.MixPlanner);
                AddDiagnosticsFromValidation(variant);
            }

            output.Variants = candidates
                .OrderBy(v => v.Validation.Passed ? 0 : 1)
                .ThenByDescending(v => v.Metrics.Score)
                .ThenBy(v => v.VariantId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AddOutputValidationSummary(output);
            int validCount = output.Variants.Count(v => v.Validation.Passed);
            if (validCount == output.Variants.Count)
            {
                output.Status = "succeeded";
            }
            else if (validCount > 0)
            {
                output.Status = "partial";
                output.Diagnostics.Add(Diagnostic.Warning(
                    "validation.partial",
                    "Some generated variants failed validation and were ranked after valid variants."));
            }
            else
            {
                output.Status = "failed";
                output.Diagnostics.Add(Diagnostic.Error("validation.no_valid_variants", "No generated variants passed validation."));
            }

            if (!string.Equals(output.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                AddVariantGenerationDiagnosticSummary(output, includeWarnings: string.Equals(output.Status, "failed", StringComparison.OrdinalIgnoreCase));
            }

            return output;
        }

        private EngineOutput ValidateCore(EngineInput input)
        {
            EngineOutput output = new EngineOutput
            {
                ProjectId = ResolveProjectId(input),
                Status = "failed"
            };

            if (input == null)
            {
                output.Diagnostics.Add(Diagnostic.Error("input.null", "Engine input cannot be null."));
                return output;
            }

            output.Diagnostics.AddRange(InputContractValidator.Validate(input));
            if (HasErrors(output.Diagnostics))
            {
                output.Metadata = BuildMetadata(input, null);
                return output;
            }

            List<Diagnostic> defaultsDiagnostics = new List<Diagnostic>();
            EnsureDefaults(input, defaultsDiagnostics);
            output.ProjectId = ResolveProjectId(input);
            output.Diagnostics.AddRange(defaultsDiagnostics);

            CleanedInput cleaned = CleanInput(input);
            output.Diagnostics.AddRange(cleaned.Diagnostics);
            output.Metadata = BuildMetadata(input, cleaned);
            if (HasErrors(output.Diagnostics))
            {
                return output;
            }

            output.Diagnostics.AddRange(AnalyzeFeasibility(cleaned));
            if (HasErrors(output.Diagnostics))
            {
                return output;
            }

            output.Status = "validated";
            output.Diagnostics.Add(Diagnostic.Info(
                "input.validated",
                "Input contract, geometry cleanup, and MVP feasibility checks passed.",
                output.ProjectId));
            return output;
        }

        private static CleanedInput CleanInput(EngineInput input)
        {
            double tolerance = Math.Max(1e-6, input.Project.Tolerance);
            CleanedInput cleaned = new CleanedInput
            {
                Source = input,
                Tolerance = tolerance
            };

            CleanPolygonResult outer = GeometryCleaner.CleanPolygon(input.Floorplate.Outer, tolerance, clockwise: false);
            cleaned.Diagnostics.AddRange(outer.Diagnostics);
            if (!outer.IsValid)
            {
                return cleaned;
            }

            cleaned.Floorplate = outer.Polygon;

            foreach (PolygonInput holeInput in input.Floorplate.Holes)
            {
                CleanPolygonResult hole = GeometryCleaner.CleanPolygon(holeInput, tolerance, clockwise: true);
                cleaned.Diagnostics.AddRange(hole.Diagnostics);
                if (!hole.IsValid)
                {
                    continue;
                }

                if (!GeometryPredicates.ContainsPolygon(cleaned.Floorplate, hole.Polygon, tolerance))
                {
                    cleaned.Diagnostics.Add(Diagnostic.Error(
                        "geometry.hole_outside_floorplate",
                        "Floorplate hole is not contained by the outer boundary.",
                        hole.Polygon.SourceId));
                    continue;
                }

                if (cleaned.Holes.Any(existing => GeometryPredicates.PolygonsOverlapArea(existing, hole.Polygon, tolerance)))
                {
                    cleaned.Diagnostics.Add(Diagnostic.Error("geometry.hole_overlap", "Floorplate holes overlap each other.", hole.Polygon.SourceId));
                    continue;
                }

                cleaned.Holes.Add(hole.Polygon);
            }

            foreach (FixedElementInput fixedInput in input.FixedElements)
            {
                CleanPolygonResult fixedPolygon = GeometryCleaner.CleanPolygon(fixedInput.Polygon, tolerance, clockwise: false);
                cleaned.Diagnostics.AddRange(fixedPolygon.Diagnostics);
                if (!fixedPolygon.IsValid)
                {
                    continue;
                }

                if (!GeometryPredicates.ContainsPolygon(cleaned.Floorplate, fixedPolygon.Polygon, tolerance))
                {
                    cleaned.Diagnostics.Add(Diagnostic.Error(
                        "geometry.fixed_element_outside_floorplate",
                        "Fixed element is not contained by the outer boundary.",
                        fixedInput.Id));
                    continue;
                }

                if (cleaned.Holes.Any(h => GeometryPredicates.PolygonsOverlapArea(h, fixedPolygon.Polygon, tolerance)))
                {
                    cleaned.Diagnostics.Add(Diagnostic.Error(
                        "geometry.fixed_element_overlaps_hole",
                        "Fixed element overlaps a floorplate hole.",
                        fixedInput.Id));
                    continue;
                }

                cleaned.FixedElements.Add(new CleanedFixedElement
                {
                    Id = string.IsNullOrWhiteSpace(fixedInput.Id)
                        ? "fixed-" + (cleaned.FixedElements.Count + 1).ToString("00", CultureInfo.InvariantCulture)
                        : fixedInput.Id,
                    Type = string.IsNullOrWhiteSpace(fixedInput.Type) ? "fixed" : fixedInput.Type,
                    Polygon = fixedPolygon.Polygon,
                    BlocksGeneration = fixedInput.BlocksGeneration
                });
            }

            if (!cleaned.Floorplate.IsOrthogonal(tolerance))
            {
                cleaned.Diagnostics.Add(Diagnostic.Warning(
                    "geometry.non_orthogonal_floorplate",
                    "MVP generation approximates best on orthogonal floorplates.",
                    cleaned.Floorplate.SourceId));
            }

            return cleaned;
        }

        private static IEnumerable<Diagnostic> AnalyzeFeasibility(CleanedInput input)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            double tolerance = input.Tolerance;
            double grossArea = GrossArea(input);
            double blockingArea = input.FixedElements.Where(f => f.BlocksGeneration).Sum(f => f.Polygon.Area());
            double usableArea = Math.Max(0.0, grossArea - blockingArea);
            if (usableArea + tolerance < input.Source.Rules.MinUnitArea)
            {
                diagnostics.Add(Diagnostic.Error(
                    "input.insufficient_usable_area",
                    "Usable floorplate area after holes and blocking fixed elements is below the configured minimum unit area.",
                    input.Floorplate.SourceId));
            }

            if (IsSingleDwelling(input.Source))
            {
                // A single dwelling has no corridor or unit bands; the plate only
                // needs to host its room program, which the generator validates.
                Bounds2 dwellingBounds = input.Floorplate.Bounds();
                double minRoomSide = Math.Min(input.Source.Rules.MinRoomDepth, input.Source.Rules.MinRoomWidth);
                if (dwellingBounds.Width + tolerance < minRoomSide * 1.6 || dwellingBounds.Height + tolerance < minRoomSide * 1.6)
                {
                    diagnostics.Add(Diagnostic.Error(
                        "input.floorplate_too_small_for_dwelling",
                        "Floorplate bounds cannot host a dwelling room program at the configured room minimums.",
                        input.Floorplate.SourceId));
                }

                return diagnostics;
            }

            Bounds2 bounds = input.Floorplate.Bounds();
            double corridorWidth = Math.Max(input.Source.Rules.MinCorridorWidth, 1.2);
            double minCorridorLength = Math.Max(corridorWidth * 4.0, 7.0);
            double minUnitBandDepth = Math.Max(5.0, Math.Min(input.Source.Rules.MinRoomDepth, input.Source.Rules.MinRoomWidth) * 1.8);
            bool horizontalCandidatePossible =
                bounds.Width + tolerance >= minCorridorLength &&
                (bounds.Height - corridorWidth) + tolerance >= minUnitBandDepth;
            bool verticalCandidatePossible =
                bounds.Height + tolerance >= minCorridorLength &&
                (bounds.Width - corridorWidth) + tolerance >= minUnitBandDepth;

            if (!horizontalCandidatePossible && !verticalCandidatePossible)
            {
                diagnostics.Add(Diagnostic.Error(
                    "input.floorplate_too_narrow_for_mvp",
                    "Floorplate bounds cannot fit the MVP corridor width plus a usable unit band depth.",
                    input.Floorplate.SourceId));
            }

            return diagnostics;
        }

        internal static bool IsSingleDwelling(EngineInput input)
        {
            return input != null &&
                input.GenerationSettings != null &&
                string.Equals(input.GenerationSettings.LayoutMode, "single_dwelling", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateVariant(LayoutVariant variant, CleanedInput input, UnitMixPlanner mixPlanner)
        {
            ValidationReport report = new ValidationReport();
            double tolerance = input.Tolerance;
            bool candidateDidNotFail = !string.Equals(variant.Status, "failed", StringComparison.OrdinalIgnoreCase);
            AddCheck(report, "candidate_generated", candidateDidNotFail, "error", "Candidate generation failed before validation.", variant.VariantId);

            double grossArea = GrossArea(input);
            AddCheck(report, "gross_area_positive", grossArea > tolerance, "error", "Cleaned gross floorplate area must be positive.", "floorplate");
            AddCheck(report, "units_present", variant.Units.Count > 0, "error", "Variant must contain at least one unit.", variant.VariantId);
            if (IsSingleDwelling(input.Source))
            {
                AddCheck(
                    report,
                    "dwelling_has_no_corridor",
                    variant.Corridors.Count == 0,
                    "error",
                    "A single dwelling must not contain corridor circulation.",
                    variant.VariantId);
            }
            else
            {
                AddCheck(report, "corridors_present", variant.Corridors.Count > 0, "error", "Variant must contain at least one corridor.", variant.VariantId);
            }

            List<Polygon2> corridorPolygons = new List<Polygon2>();
            foreach (CorridorLayout corridor in variant.Corridors)
            {
                Polygon2 corridorPolygon = ToPolygon(corridor.Polygon);
                corridorPolygons.Add(corridorPolygon);
                AddCheck(
                    report,
                    "corridor_width",
                    corridor.Width + tolerance >= input.Source.Rules.MinCorridorWidth,
                    "error",
                    "Corridor width is below the configured minimum.",
                    corridor.Id);
                AddCheck(
                    report,
                    "corridor_inside_floorplate",
                    GeometryPredicates.ContainsPolygon(input.Floorplate, corridorPolygon, tolerance),
                    "error",
                    "Corridor is not contained by the cleaned floorplate.",
                    corridor.Id);
                AddCheck(
                    report,
                    "corridor_avoids_holes",
                    !input.Holes.Any(h => GeometryPredicates.PolygonsOverlapArea(corridorPolygon, h, tolerance)),
                    "error",
                    "Corridor overlaps a floorplate hole.",
                    corridor.Id);
                AddCheck(
                    report,
                    "corridor_avoids_fixed_elements",
                    !input.FixedElements
                        .Where(f => f.BlocksGeneration)
                        .Any(f => GeometryPredicates.PolygonsOverlapArea(corridorPolygon, f.Polygon, tolerance)),
                    "error",
                    "Corridor overlaps a blocking fixed element.",
                    corridor.Id);
            }

            Dictionary<string, Polygon2> unitPolygons = new Dictionary<string, Polygon2>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < variant.Units.Count; i++)
            {
                UnitLayout unit = variant.Units[i];
                Polygon2 unitPolygon = ToPolygon(unit.Polygon);
                unitPolygons[unit.Id] = unitPolygon;
                UnitTypeTarget target = mixPlanner.FindTarget(unit.Type);
                AddCheck(
                    report,
                    "unit_inside_floorplate",
                    GeometryPredicates.ContainsPolygon(input.Floorplate, unitPolygon, tolerance),
                    "error",
                    "Unit is not contained by the cleaned floorplate.",
                    unit.Id);
                AddCheck(
                    report,
                    "unit_avoids_holes",
                    !input.Holes.Any(h =>
                        GeometryPredicates.PolygonsOverlapArea(unitPolygon, h, tolerance) ||
                        GeometryPredicates.ContainsPoint(h, unitPolygon.Centroid(), tolerance, false)),
                    "error",
                    "Unit overlaps a floorplate hole.",
                    unit.Id);
                AddCheck(
                    report,
                    "unit_avoids_fixed_elements",
                    !input.FixedElements
                        .Where(f => f.BlocksGeneration)
                        .Any(f => GeometryPredicates.PolygonsOverlapArea(unitPolygon, f.Polygon, tolerance)),
                    "error",
                    "Unit overlaps a blocking fixed element.",
                    unit.Id);
                AddCheck(
                    report,
                    "unit_avoids_corridor",
                    !corridorPolygons.Any(c => GeometryPredicates.PolygonsOverlapArea(unitPolygon, c, tolerance)),
                    "error",
                    "Unit overlaps corridor area.",
                    unit.Id);
                AddCheck(
                    report,
                    "unit_min_area",
                    unit.Area + tolerance >= input.Source.Rules.MinUnitArea,
                    "error",
                    "Unit area is below the configured minimum.",
                    unit.Id);
                AddCheck(
                    report,
                    "unit_target_area",
                    unit.Area + tolerance >= target.MinArea && unit.Area <= target.MaxArea + tolerance,
                    IsStrict(input) ? "error" : "warning",
                    "Unit area falls outside the target type range.",
                    unit.Id);

                for (int j = i + 1; j < variant.Units.Count; j++)
                {
                    UnitLayout other = variant.Units[j];
                    Polygon2 otherPolygon = ToPolygon(other.Polygon);
                    AddCheck(
                        report,
                        "unit_non_overlap",
                        !GeometryPredicates.PolygonsOverlapArea(unitPolygon, otherPolygon, tolerance),
                        "error",
                        "Units overlap each other.",
                        unit.Id + "/" + other.Id);
                }
            }

            foreach (RoomLayout room in variant.Rooms)
            {
                Polygon2 roomPolygon = ToPolygon(room.Polygon);
                bool hasUnit = unitPolygons.ContainsKey(room.UnitId);
                AddCheck(report, "room_has_unit", hasUnit, "error", "Room references an unknown unit.", room.Id);
                if (hasUnit)
                {
                    AddCheck(
                        report,
                        "room_inside_unit",
                        GeometryPredicates.ContainsPolygon(unitPolygons[room.UnitId], roomPolygon, tolerance),
                        "error",
                        "Room is not contained by its unit.",
                        room.Id);
                }

                AddCheck(report, "room_area_positive", room.Area > tolerance, "error", "Room area must be positive.", room.Id);
                if (RequiresDaylight(room, input.Source.Rules))
                {
                    AddCheck(
                        report,
                        "required_daylight",
                        room.Daylight,
                        "error",
                        "Habitable room requiring daylight has no daylight facade exposure.",
                        room.Id);
                }
            }

            foreach (UnitLayout unit in variant.Units)
            {
                bool hasDoor = variant.DoorsOpenings.Any(d => d.ConnectsSpaces.Contains(unit.Id));
                AddCheck(report, "unit_has_door", hasDoor, "error", "Unit must have a door/opening connection.", unit.Id);
            }

            HashSet<string> wallIds = new HashSet<string>(variant.Walls.Select(w => w.Id), StringComparer.OrdinalIgnoreCase);
            HashSet<string> knownSpaceIds = new HashSet<string>(variant.Units.Select(u => u.Id), StringComparer.OrdinalIgnoreCase);
            foreach (CorridorLayout corridor in variant.Corridors)
            {
                knownSpaceIds.Add(corridor.Id);
            }

            foreach (RoomLayout room in variant.Rooms)
            {
                knownSpaceIds.Add(room.Id);
            }

            foreach (DoorOpening door in variant.DoorsOpenings)
            {
                AddCheck(report, "door_host_wall_exists", wallIds.Contains(door.HostWall), "error", "Door/opening references a missing host wall.", door.Id);
                bool connectsKnownSpaces = door.ConnectsSpaces != null &&
                    door.ConnectsSpaces.Count >= 2 &&
                    door.ConnectsSpaces.All(id => knownSpaceIds.Contains(id));
                AddCheck(report, "door_connects_known_spaces", connectsKnownSpaces, "error", "Door/opening must connect known spaces.", door.Id);
            }

            AddCheck(
                report,
                "stable_external_ids",
                HasStableExternalIds(variant),
                "error",
                "Generated variants and elements must expose unique stable external ids.",
                variant.VariantId);
            AddCheck(
                report,
                "generated_layers",
                HasExpectedGeneratedLayers(variant),
                "error",
                "Generated elements must use the published generated layer names.",
                variant.VariantId);
            AddCheck(
                report,
                "hypergraph_contract",
                HasValidHypergraph(variant),
                "error",
                "Variant topology must expose a valid hypergraph contract.",
                variant.VariantId);

            if (IsStrict(input))
            {
                AddCheck(
                    report,
                    "strict_unit_mix",
                    mixPlanner.StrictCountsSatisfied(variant.Units),
                    "error",
                    "Strict target unit counts were not met.",
                    variant.VariantId);
            }

            report.Passed = report.Checks.All(c => c.Passed || !IsError(c.Severity));
            variant.Validation = report;
            variant.Status = report.Passed ? "valid" : "failed";
        }

        private static void ScoreVariant(LayoutVariant variant, CleanedInput input, UnitMixPlanner mixPlanner)
        {
            double grossArea = GrossArea(input);
            double sellableArea = variant.Units.Sum(u => u.Area);
            double corridorArea = variant.Corridors.Sum(c => ToPolygon(c.Polygon).Area());
            double netGross = grossArea <= 0.0 ? 0.0 : sellableArea / grossArea;
            double efficiency = sellableArea + corridorArea <= 0.0 ? 0.0 : sellableArea / (sellableArea + corridorArea);
            double mixMatch = mixPlanner.MixMatchScore(variant.Units);
            double unitQuality = variant.Units.Count == 0 ? 0.0 : variant.Units.Average(u => u.Score);
            double daylightQuality = DaylightQuality(variant, input.Source.Rules);

            double score =
                GetWeight(input, "efficiency", 0.30) * Clamp01(efficiency) +
                GetWeight(input, "netGrossRatio", 0.20) * Clamp01(netGross) +
                GetWeight(input, "unitMixMatch", 0.25) * Clamp01(mixMatch) +
                GetWeight(input, "unitQuality", 0.15) * Clamp01(unitQuality) +
                GetWeight(input, "daylight", 0.10) * Clamp01(daylightQuality);

            double weightTotal =
                GetWeight(input, "efficiency", 0.30) +
                GetWeight(input, "netGrossRatio", 0.20) +
                GetWeight(input, "unitMixMatch", 0.25) +
                GetWeight(input, "unitQuality", 0.15) +
                GetWeight(input, "daylight", 0.10);

            score = weightTotal <= 0.0 ? 0.0 : score / weightTotal;
            if (!variant.Validation.Passed)
            {
                score *= 0.25;
            }

            variant.Metrics = new VariantMetrics
            {
                GrossArea = Round(grossArea),
                SellableArea = Round(sellableArea),
                CorridorArea = Round(corridorArea),
                NetGrossRatio = Round(netGross),
                Efficiency = Round(efficiency),
                UnitMixMatch = Round(mixMatch),
                Score = Round(score)
            };
        }

        private static void NormalizeVariant(LayoutVariant variant, string projectId)
        {
            if (variant.Diagnostics == null)
            {
                variant.Diagnostics = new List<Diagnostic>();
            }

            variant.Units = (variant.Units ?? new List<UnitLayout>()).OrderBy(u => u.Id, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (UnitLayout unit in variant.Units)
            {
                unit.Rooms = (unit.Rooms ?? new List<RoomLayout>()).OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
            }

            variant.Rooms = (variant.Rooms ?? new List<RoomLayout>()).OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
            variant.Corridors = (variant.Corridors ?? new List<CorridorLayout>()).OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ToList();
            variant.Walls = (variant.Walls ?? new List<WallLayout>()).OrderBy(w => w.Id, StringComparer.OrdinalIgnoreCase).ToList();
            variant.DoorsOpenings = (variant.DoorsOpenings ?? new List<DoorOpening>()).OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();
            variant.Labels = (variant.Labels ?? new List<LabelLayout>()).OrderBy(l => l.Id, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (CorridorLayout corridor in variant.Corridors)
            {
                corridor.Layer = string.IsNullOrWhiteSpace(corridor.Layer) ? LayerNames.GeneratedCorridors : corridor.Layer;
                corridor.Bounds = ToPolygon(corridor.Polygon).Bounds();
                if (corridor.Connections == null)
                {
                    corridor.Connections = new List<string>();
                    continue;
                }

                int before = corridor.Connections.Count;
                corridor.Connections = corridor.Connections
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (corridor.Connections.Count != before)
                {
                    variant.Diagnostics.Add(Diagnostic.Info(
                        "repair.corridor_connections_deduplicated",
                        "Removed duplicate or empty corridor connections.",
                        corridor.Id));
                }
            }

            foreach (UnitLayout unit in variant.Units)
            {
                unit.Layer = string.IsNullOrWhiteSpace(unit.Layer) ? LayerNames.GeneratedUnits : unit.Layer;
                unit.Bounds = ToPolygon(unit.Polygon).Bounds();
            }

            foreach (RoomLayout room in variant.Rooms)
            {
                room.Layer = string.IsNullOrWhiteSpace(room.Layer) ? LayerNames.GeneratedRooms : room.Layer;
                room.Bounds = ToPolygon(room.Polygon).Bounds();
            }

            foreach (WallLayout wall in variant.Walls)
            {
                wall.Layer = string.IsNullOrWhiteSpace(wall.Layer) ? LayerNames.GeneratedWalls : wall.Layer;
            }

            foreach (DoorOpening door in variant.DoorsOpenings)
            {
                door.Layer = string.IsNullOrWhiteSpace(door.Layer) ? LayerNames.GeneratedDoors : door.Layer;
            }

            foreach (LabelLayout label in variant.Labels)
            {
                label.Layer = string.IsNullOrWhiteSpace(label.Layer) ? LayerNames.GeneratedLabels : label.Layer;
            }

            AssignExternalIds(variant, projectId);
        }

        private static void AssignExternalIds(LayoutVariant variant, string projectId)
        {
            string variantId = string.IsNullOrWhiteSpace(variant.VariantId) ? "variant" : variant.VariantId;
            variant.ExternalId = BuildExternalId(projectId, "variants", variantId);

            foreach (CorridorLayout corridor in variant.Corridors)
            {
                corridor.ExternalId = BuildVariantExternalId(projectId, variantId, "corridors", corridor.Id);
            }

            foreach (UnitLayout unit in variant.Units)
            {
                unit.ExternalId = BuildVariantExternalId(projectId, variantId, "units", unit.Id);
            }

            foreach (RoomLayout room in variant.Rooms)
            {
                room.ExternalId = BuildVariantExternalId(projectId, variantId, "rooms", room.Id);
            }

            foreach (WallLayout wall in variant.Walls)
            {
                wall.ExternalId = BuildVariantExternalId(projectId, variantId, "walls", wall.Id);
            }

            foreach (DoorOpening door in variant.DoorsOpenings)
            {
                door.ExternalId = BuildVariantExternalId(projectId, variantId, "doors", door.Id);
            }

            foreach (LabelLayout label in variant.Labels)
            {
                label.ExternalId = BuildVariantExternalId(projectId, variantId, "labels", label.Id);
            }

            if (variant.Topology == null)
            {
                return;
            }

            foreach (SpaceNode node in variant.Topology.Nodes ?? new List<SpaceNode>())
            {
                node.ExternalId = BuildVariantExternalId(projectId, variantId, "topology/nodes", node.Id);
            }

            int edgeIndex = 0;
            foreach (AdjacencyEdge edge in variant.Topology.Edges ?? new List<AdjacencyEdge>())
            {
                edgeIndex++;
                string localId = edgeIndex.ToString("000", CultureInfo.InvariantCulture) + "-" + edge.From + "-" + edge.Kind + "-" + edge.To;
                edge.ExternalId = BuildVariantExternalId(projectId, variantId, "topology/edges", localId);
            }

            if (variant.Topology.Hypergraph == null)
            {
                return;
            }

            foreach (HypergraphNode node in variant.Topology.Hypergraph.Nodes ?? new List<HypergraphNode>())
            {
                node.ExternalId = BuildVariantExternalId(projectId, variantId, "topology/hypergraph/nodes", node.Id);
            }

            foreach (Hyperedge edge in variant.Topology.Hypergraph.Hyperedges ?? new List<Hyperedge>())
            {
                edge.ExternalId = BuildVariantExternalId(projectId, variantId, "topology/hypergraph/hyperedges", edge.Id);
            }

            foreach (HypergraphIncidence incidence in variant.Topology.Hypergraph.Incidence ?? new List<HypergraphIncidence>())
            {
                incidence.ExternalId = BuildVariantExternalId(projectId, variantId, "topology/hypergraph/incidence", incidence.Id);
            }
        }

        private static string BuildVariantExternalId(string projectId, string variantId, string category, string localId)
        {
            return BuildExternalId(projectId, "variants", variantId) + "/" + category + "/" + EscapeSegment(localId);
        }

        private static string BuildExternalId(string projectId, string category, string localId)
        {
            string resolvedProjectId = string.IsNullOrWhiteSpace(projectId) ? "project" : projectId;
            string resolvedLocalId = string.IsNullOrWhiteSpace(localId) ? "item" : localId;
            return "fp://" + EscapeSegment(resolvedProjectId) + "/" + category + "/" + EscapeSegment(resolvedLocalId);
        }

        private static string EscapeSegment(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static void AddDiagnosticsFromValidation(LayoutVariant variant)
        {
            foreach (ValidationCheck check in variant.Validation.Checks.Where(c => !c.Passed))
            {
                string code = "validation." + check.Name;
                if (IsError(check.Severity))
                {
                    variant.Diagnostics.Add(Diagnostic.Error(code, check.Reason, check.SourceId));
                }
                else
                {
                    variant.Diagnostics.Add(Diagnostic.Warning(code, check.Reason, check.SourceId));
                }
            }
        }

        private static bool HasStableExternalIds(LayoutVariant variant)
        {
            List<string> externalIds = new List<string> { variant.ExternalId };
            externalIds.AddRange(variant.Corridors.Select(c => c.ExternalId));
            externalIds.AddRange(variant.Units.Select(u => u.ExternalId));
            externalIds.AddRange(variant.Rooms.Select(r => r.ExternalId));
            externalIds.AddRange(variant.Walls.Select(w => w.ExternalId));
            externalIds.AddRange(variant.DoorsOpenings.Select(d => d.ExternalId));
            externalIds.AddRange(variant.Labels.Select(l => l.ExternalId));
            if (variant.Topology != null)
            {
                externalIds.AddRange((variant.Topology.Nodes ?? new List<SpaceNode>()).Select(n => n.ExternalId));
                externalIds.AddRange((variant.Topology.Edges ?? new List<AdjacencyEdge>()).Select(e => e.ExternalId));
                if (variant.Topology.Hypergraph != null)
                {
                    externalIds.AddRange((variant.Topology.Hypergraph.Nodes ?? new List<HypergraphNode>()).Select(n => n.ExternalId));
                    externalIds.AddRange((variant.Topology.Hypergraph.Hyperedges ?? new List<Hyperedge>()).Select(e => e.ExternalId));
                    externalIds.AddRange((variant.Topology.Hypergraph.Incidence ?? new List<HypergraphIncidence>()).Select(i => i.ExternalId));
                }
            }

            List<string> required = externalIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            return required.Count == externalIds.Count &&
                required.All(id => id.StartsWith("fp://", StringComparison.Ordinal)) &&
                required.Count == required.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }

        private static bool HasValidHypergraph(LayoutVariant variant)
        {
            return variant.Topology != null &&
                variant.Topology.Hypergraph != null &&
                HypergraphBuilder.Validate(variant.Topology.Hypergraph).Count == 0;
        }

        private static bool HasExpectedGeneratedLayers(LayoutVariant variant)
        {
            return variant.Units.All(u => string.Equals(u.Layer, LayerNames.GeneratedUnits, StringComparison.Ordinal)) &&
                variant.Rooms.All(r => string.Equals(r.Layer, LayerNames.GeneratedRooms, StringComparison.Ordinal)) &&
                variant.Corridors.All(c => string.Equals(c.Layer, LayerNames.GeneratedCorridors, StringComparison.Ordinal)) &&
                variant.Walls.All(w => string.Equals(w.Layer, LayerNames.GeneratedWalls, StringComparison.Ordinal)) &&
                variant.DoorsOpenings.All(d => string.Equals(d.Layer, LayerNames.GeneratedDoors, StringComparison.Ordinal)) &&
                variant.Labels.All(l => string.Equals(l.Layer, LayerNames.GeneratedLabels, StringComparison.Ordinal));
        }

        private static void AddOutputValidationSummary(EngineOutput output)
        {
            foreach (string name in output.Variants
                .SelectMany(v => v.Validation.Checks)
                .Where(c => !c.Passed && IsError(c.Severity))
                .Select(c => c.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                output.Diagnostics.Add(Diagnostic.Error("validation." + name, "One or more variants failed the " + name + " validation check.", "variants"));
            }
        }

        private static void AddVariantGenerationDiagnosticSummary(EngineOutput output, bool includeWarnings)
        {
            var groupedDiagnostics = output.Variants
                .SelectMany(v => v.Diagnostics)
                .Where(d => d.Code != null && d.Code.StartsWith("generation.", StringComparison.OrdinalIgnoreCase))
                .Where(d => IsError(d.Severity) || (includeWarnings && string.Equals(d.Severity, "warning", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, Diagnostic> group in groupedDiagnostics)
            {
                if (output.Diagnostics.Any(d => string.Equals(d.Code, group.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Diagnostic first = group.First();
                string message = group.Count() == output.Variants.Count
                    ? "All generated variants reported: " + first.Message
                    : group.Count().ToString(CultureInfo.InvariantCulture) + " generated variant(s) reported: " + first.Message;

                if (IsError(first.Severity))
                {
                    output.Diagnostics.Add(Diagnostic.Error(first.Code, message, "variants"));
                }
                else
                {
                    output.Diagnostics.Add(Diagnostic.Warning(first.Code, message, "variants"));
                }
            }
        }

        private static void EnsureDefaults(EngineInput input, List<Diagnostic> diagnostics)
        {
            if (input.Project == null) input.Project = new ProjectInfo();
            if (string.IsNullOrWhiteSpace(input.Project.Id)) input.Project.Id = "project";
            if (string.IsNullOrWhiteSpace(input.Project.Name)) input.Project.Name = "Floor Plan Generation Project";
            if (string.IsNullOrWhiteSpace(input.Project.Units)) input.Project.Units = "m";
            if (input.Project.Tolerance <= 0.0 || double.IsNaN(input.Project.Tolerance) || double.IsInfinity(input.Project.Tolerance))
            {
                input.Project.Tolerance = 0.01;
                diagnostics.Add(Diagnostic.Warning("input.default_tolerance", "Project tolerance was invalid and was reset to 0.01.", input.Project.Id));
            }

            if (input.Floorplate == null) input.Floorplate = new FloorplateInput();
            if (input.Floorplate.Outer == null) input.Floorplate.Outer = new PolygonInput();
            if (input.Floorplate.Holes == null) input.Floorplate.Holes = new List<PolygonInput>();
            if (input.Floorplate.Outer.Points == null) input.Floorplate.Outer.Points = new List<Point2>();

            if (input.FixedElements == null) input.FixedElements = new List<FixedElementInput>();
            input.FixedElements = input.FixedElements.Where(f => f != null).ToList();
            foreach (FixedElementInput fixedElement in input.FixedElements)
            {
                if (fixedElement.Polygon == null) fixedElement.Polygon = new PolygonInput();
                if (fixedElement.Polygon.Points == null) fixedElement.Polygon.Points = new List<Point2>();
                if (string.IsNullOrWhiteSpace(fixedElement.Type)) fixedElement.Type = "fixed";
            }

            if (input.Access == null) input.Access = new AccessInput();
            if (input.Access.EntryPoints == null) input.Access.EntryPoints = new List<Point2>();
            if (input.Access.VerticalCoreAccess == null) input.Access.VerticalCoreAccess = new List<Point2>();
            if (input.Access.CorridorStartPoints == null) input.Access.CorridorStartPoints = new List<Point2>();
            if (input.Access.CorridorEndPoints == null) input.Access.CorridorEndPoints = new List<Point2>();
            if (input.Access.CorridorCenterlines == null) input.Access.CorridorCenterlines = new List<LineInput>();
            input.Access.CorridorCenterlines = input.Access.CorridorCenterlines.Where(l => l != null).ToList();
            foreach (LineInput line in input.Access.CorridorCenterlines)
            {
                if (line.Start == null) line.Start = new Point2();
                if (line.End == null) line.End = new Point2();
            }

            if (input.Facade == null) input.Facade = new FacadeInput();
            if (input.Facade.Segments == null) input.Facade.Segments = new List<FacadeSegmentInput>();
            input.Facade.Segments = input.Facade.Segments.Where(s => s != null).ToList();
            foreach (FacadeSegmentInput segment in input.Facade.Segments)
            {
                if (segment.Start == null) segment.Start = new Point2();
                if (segment.End == null) segment.End = new Point2();
            }

            if (input.Facade.DaylightCapableEdges == null) input.Facade.DaylightCapableEdges = new List<string>();
            if (input.Facade.NonDaylightEdges == null) input.Facade.NonDaylightEdges = new List<string>();

            if (input.Program == null) input.Program = new ProgramBrief();
            if (input.Program.TargetUnitTypes == null) input.Program.TargetUnitTypes = new List<UnitTypeTarget>();
            input.Program.TargetUnitTypes = input.Program.TargetUnitTypes.Where(t => t != null).ToList();
            if (input.Program.RoomTypes == null) input.Program.RoomTypes = new List<RoomTypeRule>();
            input.Program.RoomTypes = input.Program.RoomTypes.Where(r => r != null).ToList();

            if (input.Rules == null) input.Rules = new RuleSet();
            if (input.Rules.MinCorridorWidth <= 0.0) input.Rules.MinCorridorWidth = 1.8;
            if (input.Rules.MinRoomWidth <= 0.0) input.Rules.MinRoomWidth = 2.4;
            if (input.Rules.MinRoomDepth <= 0.0) input.Rules.MinRoomDepth = 2.4;
            if (input.Rules.DoorWidth <= 0.0) input.Rules.DoorWidth = 0.9;
            if (input.Rules.MinUnitArea <= 0.0) input.Rules.MinUnitArea = 25.0;

            if (input.GenerationSettings == null) input.GenerationSettings = new GenerationSettings();
            if (input.GenerationSettings.VariantCount <= 0) input.GenerationSettings.VariantCount = 1;
            if (input.GenerationSettings.VariantCount > 20) input.GenerationSettings.VariantCount = 20;
            if (string.IsNullOrWhiteSpace(input.GenerationSettings.Strictness)) input.GenerationSettings.Strictness = "balanced";
            if (input.GenerationSettings.ScoringWeights == null)
            {
                input.GenerationSettings.ScoringWeights =
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void AddCheck(ValidationReport report, string name, bool passed, string severity, string reason, string sourceId)
        {
            report.Checks.Add(new ValidationCheck
            {
                Name = name,
                Passed = passed,
                Severity = severity,
                Reason = passed ? string.Empty : reason,
                SourceId = sourceId ?? string.Empty
            });
        }

        private static bool RequiresDaylight(RoomLayout room, RuleSet rules)
        {
            string roomType = room.RoomType ?? string.Empty;
            bool bedroom = roomType.IndexOf("bedroom", StringComparison.OrdinalIgnoreCase) >= 0;
            bool living = roomType.IndexOf("living", StringComparison.OrdinalIgnoreCase) >= 0;
            return (bedroom && rules.RequireDaylightForBedrooms) || (living && rules.RequireDaylightForLiving);
        }

        private static double DaylightQuality(LayoutVariant variant, RuleSet rules)
        {
            List<RoomLayout> required = variant.Rooms.Where(r => RequiresDaylight(r, rules)).ToList();
            if (required.Count == 0)
            {
                return 1.0;
            }

            return required.Count(r => r.Daylight) / (double)required.Count;
        }

        private static Polygon2 ToPolygon(PolygonInput input)
        {
            if (input == null || input.Points == null)
            {
                return new Polygon2();
            }

            List<Point2> points = input.Points.Where(p => p != null).Select(p => p.Clone()).ToList();
            if (points.Count > 1 && points[0].EqualsWithin(points[points.Count - 1], 1e-9))
            {
                points.RemoveAt(points.Count - 1);
            }

            return new Polygon2(input.Id, points);
        }

        private static double GrossArea(CleanedInput input)
        {
            return Math.Max(0.0, input.Floorplate.Area() - input.Holes.Sum(h => h.Area()));
        }

        private static EngineMetadata BuildMetadata(EngineInput input, CleanedInput cleaned)
        {
            EngineMetadata metadata = new EngineMetadata();
            if (input == null)
            {
                return metadata;
            }

            if (input.Project != null)
            {
                metadata.ProjectUnits = string.IsNullOrWhiteSpace(input.Project.Units) ? "m" : input.Project.Units;
                metadata.Tolerance = input.Project.Tolerance;
                metadata.Seed = input.Project.Seed;
            }

            if (input.GenerationSettings != null)
            {
                metadata.GenerationSettings = new GenerationSettingsSummary
                {
                    VariantCount = input.GenerationSettings.VariantCount,
                    TimeLimitMilliseconds = input.GenerationSettings.TimeLimitMilliseconds,
                    Strictness = string.IsNullOrWhiteSpace(input.GenerationSettings.Strictness) ? "balanced" : input.GenerationSettings.Strictness,
                    WeightedVariation = input.GenerationSettings.WeightedVariation,
                    LayoutMode = string.IsNullOrWhiteSpace(input.GenerationSettings.LayoutMode) ? "multi_unit" : input.GenerationSettings.LayoutMode,
                    ScoringWeights = input.GenerationSettings.ScoringWeights != null
                        ? new Dictionary<string, double>(input.GenerationSettings.ScoringWeights, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                };
            }

            if (cleaned != null && cleaned.Floorplate != null && cleaned.Floorplate.Count > 0)
            {
                double grossArea = GrossArea(cleaned);
                double holeArea = cleaned.Holes.Sum(h => h.Area());
                double blockingArea = cleaned.FixedElements.Where(f => f.BlocksGeneration).Sum(f => f.Polygon.Area());
                metadata.Floorplate = new FloorplateSummary
                {
                    Bounds = cleaned.Floorplate.Bounds(),
                    GrossArea = Round(grossArea),
                    HoleArea = Round(holeArea),
                    BlockingFixedElementArea = Round(blockingArea),
                    UsableArea = Round(Math.Max(0.0, grossArea - blockingArea))
                };
            }

            return metadata;
        }

        private static double GetWeight(CleanedInput input, string key, double fallback)
        {
            double value;
            if (input.Source.GenerationSettings.ScoringWeights != null &&
                input.Source.GenerationSettings.ScoringWeights.TryGetValue(key, out value) &&
                value >= 0.0)
            {
                return value;
            }

            return fallback;
        }

        private static bool IsStrict(CleanedInput input)
        {
            return string.Equals(input.Source.GenerationSettings.Strictness, "strict", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasErrors(IEnumerable<Diagnostic> diagnostics)
        {
            return diagnostics.Any(d => IsError(d.Severity));
        }

        private static bool IsError(string severity)
        {
            return string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase);
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        private static double Round(double value)
        {
            return Math.Round(value, 4, MidpointRounding.AwayFromZero);
        }

        private static string ResolveProjectId(EngineInput input)
        {
            if (input == null || input.Project == null || string.IsNullOrWhiteSpace(input.Project.Id))
            {
                return "project";
            }

            return input.Project.Id;
        }
    }
}

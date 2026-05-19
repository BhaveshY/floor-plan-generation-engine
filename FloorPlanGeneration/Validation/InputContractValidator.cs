using System;
using System.Collections.Generic;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;

namespace FloorPlanGeneration.Validation
{
    internal static class InputContractValidator
    {
        public static List<Diagnostic> Validate(EngineInput input)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (input == null)
            {
                diagnostics.Add(Diagnostic.Error("input.null", "Engine input cannot be null."));
                return diagnostics;
            }

            ValidateProject(input.Project, diagnostics);
            ValidateFloorplate(input.Floorplate, diagnostics);
            ValidateFixedElements(input.FixedElements, diagnostics);
            ValidateAccess(input.Access, diagnostics);
            ValidateFacade(input.Facade, diagnostics);
            ValidateProgram(input.Program, diagnostics);
            ValidateRules(input.Rules, diagnostics);
            ValidateGenerationSettings(input.GenerationSettings, diagnostics);
            return diagnostics;
        }

        private static void ValidateProject(ProjectInfo project, List<Diagnostic> diagnostics)
        {
            if (project == null)
            {
                diagnostics.Add(Diagnostic.Warning("input.default_project", "Project section was omitted; engine defaults will be used."));
                return;
            }

            if (string.IsNullOrWhiteSpace(project.Units))
            {
                diagnostics.Add(Diagnostic.Warning("input.default_units", "Project units were omitted; meters will be used."));
            }

            if (!IsPositiveFinite(project.Tolerance))
            {
                diagnostics.Add(Diagnostic.Error("input.invalid_tolerance", "Project tolerance must be a positive finite number.", project.Id));
            }
        }

        private static void ValidateFloorplate(FloorplateInput floorplate, List<Diagnostic> diagnostics)
        {
            if (floorplate == null)
            {
                diagnostics.Add(Diagnostic.Error("input.floorplate_required", "Floorplate section is required."));
                return;
            }

            if (floorplate.Outer == null)
            {
                diagnostics.Add(Diagnostic.Error("input.floorplate_outer_required", "Floorplate outer polygon is required."));
            }
            else
            {
                ValidatePolygonContract(floorplate.Outer, "input.floorplate_outer_too_few_points", diagnostics);
            }

            if (floorplate.Holes == null)
            {
                return;
            }

            foreach (PolygonInput hole in floorplate.Holes)
            {
                ValidatePolygonContract(hole, "input.hole_too_few_points", diagnostics);
            }
        }

        private static void ValidateFixedElements(List<FixedElementInput> fixedElements, List<Diagnostic> diagnostics)
        {
            if (fixedElements == null)
            {
                return;
            }

            foreach (FixedElementInput fixedElement in fixedElements)
            {
                if (fixedElement == null)
                {
                    diagnostics.Add(Diagnostic.Error("input.null_fixed_element", "Fixed element entries cannot be null."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(fixedElement.Id))
                {
                    diagnostics.Add(Diagnostic.Warning("input.default_fixed_element_id", "Fixed element id was omitted; a generated id will be used."));
                }

                ValidatePolygonContract(fixedElement.Polygon, "input.fixed_element_too_few_points", diagnostics);
            }
        }

        private static void ValidateAccess(AccessInput access, List<Diagnostic> diagnostics)
        {
            if (access == null)
            {
                return;
            }

            ValidatePoints(access.EntryPoints, "input.invalid_entry_point", diagnostics);
            ValidatePoints(access.VerticalCoreAccess, "input.invalid_vertical_core_access", diagnostics);
            ValidatePoints(access.CorridorStartPoints, "input.invalid_corridor_start_point", diagnostics);
            ValidatePoints(access.CorridorEndPoints, "input.invalid_corridor_end_point", diagnostics);

            if (access.CorridorCenterlines == null)
            {
                return;
            }

            foreach (LineInput line in access.CorridorCenterlines)
            {
                if (line == null || !IsFinitePoint(line.Start) || !IsFinitePoint(line.End))
                {
                    diagnostics.Add(Diagnostic.Error("input.invalid_corridor_centerline", "Corridor centerlines require finite start and end points.", line != null ? line.Id : string.Empty));
                }
            }
        }

        private static void ValidateFacade(FacadeInput facade, List<Diagnostic> diagnostics)
        {
            if (facade == null || facade.Segments == null)
            {
                return;
            }

            foreach (FacadeSegmentInput segment in facade.Segments)
            {
                if (segment == null || !IsFinitePoint(segment.Start) || !IsFinitePoint(segment.End))
                {
                    diagnostics.Add(Diagnostic.Error("input.invalid_facade_segment", "Facade segments require finite start and end points.", segment != null ? segment.Id : string.Empty));
                }
            }
        }

        private static void ValidateProgram(ProgramBrief program, List<Diagnostic> diagnostics)
        {
            if (program == null || program.TargetUnitTypes == null || program.TargetUnitTypes.Count == 0)
            {
                diagnostics.Add(Diagnostic.Warning("input.default_unit_mix", "No target unit types were supplied; engine default unit mix will be used."));
                return;
            }

            foreach (UnitTypeTarget target in program.TargetUnitTypes)
            {
                if (target == null)
                {
                    diagnostics.Add(Diagnostic.Error("input.null_unit_type", "Unit type target entries cannot be null."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(target.Type))
                {
                    diagnostics.Add(Diagnostic.Error("input.invalid_unit_type", "Unit type target requires a non-empty type."));
                }

                if (!IsPositiveFinite(target.MinArea) || !IsPositiveFinite(target.MaxArea) || target.MaxArea < target.MinArea)
                {
                    diagnostics.Add(Diagnostic.Error("input.invalid_unit_type_area_range", "Unit type target area range must be positive and maxArea must be greater than or equal to minArea.", target.Type));
                }

                if (target.TargetCount < 0)
                {
                    diagnostics.Add(Diagnostic.Error("input.invalid_unit_type_target_count", "Unit type targetCount cannot be negative.", target.Type));
                }

                if (!IsFinite(target.TargetRatio) || target.TargetRatio < 0.0)
                {
                    diagnostics.Add(Diagnostic.Error("input.invalid_unit_type_target_ratio", "Unit type targetRatio cannot be negative or non-finite.", target.Type));
                }

                if (!IsFinite(target.Weight) || target.Weight < 0.0)
                {
                    diagnostics.Add(Diagnostic.Error("input.invalid_unit_type_weight", "Unit type weight cannot be negative or non-finite.", target.Type));
                }
            }
        }

        private static void ValidateRules(RuleSet rules, List<Diagnostic> diagnostics)
        {
            if (rules == null)
            {
                diagnostics.Add(Diagnostic.Warning("input.default_rules", "Rules section was omitted; engine defaults will be used."));
                return;
            }

            ValidatePositiveRule(rules.MinCorridorWidth, "input.invalid_min_corridor_width", "Minimum corridor width must be positive and finite.", diagnostics);
            ValidatePositiveRule(rules.MinRoomWidth, "input.invalid_min_room_width", "Minimum room width must be positive and finite.", diagnostics);
            ValidatePositiveRule(rules.MinRoomDepth, "input.invalid_min_room_depth", "Minimum room depth must be positive and finite.", diagnostics);
            ValidatePositiveRule(rules.DoorWidth, "input.invalid_door_width", "Door width must be positive and finite.", diagnostics);
            ValidatePositiveRule(rules.MinUnitArea, "input.invalid_min_unit_area", "Minimum unit area must be positive and finite.", diagnostics);
        }

        private static void ValidateGenerationSettings(GenerationSettings settings, List<Diagnostic> diagnostics)
        {
            if (settings == null)
            {
                diagnostics.Add(Diagnostic.Warning("input.default_generation_settings", "Generation settings were omitted; engine defaults will be used."));
                return;
            }

            if (settings.VariantCount <= 0 || settings.VariantCount > 20)
            {
                diagnostics.Add(Diagnostic.Error("input.invalid_variant_count", "Generation variantCount must be between 1 and 20."));
            }

            if (settings.TimeLimitMilliseconds <= 0)
            {
                diagnostics.Add(Diagnostic.Error("input.invalid_time_limit", "Generation timeLimitMilliseconds must be positive."));
            }

            string strictness = settings.Strictness ?? string.Empty;
            if (!string.Equals(strictness, "relaxed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(strictness, "balanced", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(strictness, "strict", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Diagnostic.Error("input.invalid_strictness", "Generation strictness must be relaxed, balanced, or strict."));
            }

            if (settings.ScoringWeights == null)
            {
                return;
            }

            foreach (KeyValuePair<string, double> weight in settings.ScoringWeights)
            {
                if (!IsFinite(weight.Value) || weight.Value < 0.0)
                {
                    diagnostics.Add(Diagnostic.Error("input.invalid_scoring_weight", "Scoring weights cannot be negative or non-finite.", weight.Key));
                }
            }
        }

        private static void ValidatePolygonContract(PolygonInput polygon, string tooFewCode, List<Diagnostic> diagnostics)
        {
            if (polygon == null || polygon.Points == null || polygon.Points.Count < 3)
            {
                diagnostics.Add(Diagnostic.Error(tooFewCode, "Polygon requires at least three points.", polygon != null ? polygon.Id : string.Empty));
                return;
            }

            ValidatePoints(polygon.Points, "input.invalid_polygon_coordinate", diagnostics, polygon.Id);
        }

        private static void ValidatePoints(List<Point2> points, string code, List<Diagnostic> diagnostics, string sourceId = "")
        {
            if (points == null)
            {
                return;
            }

            foreach (Point2 point in points)
            {
                if (!IsFinitePoint(point))
                {
                    diagnostics.Add(Diagnostic.Error(code, "Point coordinates must be finite numbers.", sourceId));
                }
            }
        }

        private static void ValidatePositiveRule(double value, string code, string message, List<Diagnostic> diagnostics)
        {
            if (!IsPositiveFinite(value))
            {
                diagnostics.Add(Diagnostic.Error(code, message));
            }
        }

        private static bool IsPositiveFinite(double value)
        {
            return IsFinite(value) && value > 0.0;
        }

        private static bool IsFinitePoint(Point2 point)
        {
            return point != null && IsFinite(point.X) && IsFinite(point.Y);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}

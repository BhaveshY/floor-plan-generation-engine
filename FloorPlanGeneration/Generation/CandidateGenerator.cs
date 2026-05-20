using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;
using FloorPlanGeneration.Topology;

namespace FloorPlanGeneration.Generation
{
    internal sealed class CandidateGenerator
    {
        private readonly CleanedInput _input;
        private readonly UnitMixPlanner _mixPlanner;
        private readonly RoomTemplateGenerator _roomGenerator;
        private readonly double _tolerance;

        public CandidateGenerator(CleanedInput input)
        {
            _input = input;
            _mixPlanner = new UnitMixPlanner(input.Source.Program);
            _roomGenerator = new RoomTemplateGenerator(input);
            _tolerance = input.Tolerance;
            Diagnostics = new List<Diagnostic>();
        }

        public UnitMixPlanner MixPlanner
        {
            get { return _mixPlanner; }
        }

        public List<Diagnostic> Diagnostics { get; private set; }

        public List<LayoutVariant> Generate()
        {
            int requested = _input.Source.GenerationSettings != null ? _input.Source.GenerationSettings.VariantCount : 8;
            requested = Math.Max(1, Math.Min(20, requested));
            int timeLimitMilliseconds = _input.Source.GenerationSettings != null ? _input.Source.GenerationSettings.TimeLimitMilliseconds : 1000;
            timeLimitMilliseconds = Math.Max(1, timeLimitMilliseconds);
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<LayoutVariant> variants = new List<LayoutVariant>();

            for (int i = 0; i < requested; i++)
            {
                if (i > 0 && stopwatch.ElapsedMilliseconds >= timeLimitMilliseconds)
                {
                    Diagnostics.Add(Diagnostic.Warning(
                        "generation.time_limit_reached",
                        "Generation stopped after reaching timeLimitMilliseconds; returned completed variants only.",
                        "generationSettings.timeLimitMilliseconds"));
                    break;
                }

                int seed = CombineSeed(_input.Source.Project.Seed, i);
                SeededRandom random = new SeededRandom(seed);
                List<Diagnostic> diagnostics = new List<Diagnostic>();
                CorridorStrategy corridor = TryResolveCorridor(i, random, diagnostics);
                LayoutVariant variant = BuildVariant(i, seed, corridor, random, diagnostics);
                variants.Add(variant);
            }

            return variants;
        }

        private LayoutVariant BuildVariant(int index, int seed, CorridorStrategy corridor, SeededRandom random, List<Diagnostic> diagnostics)
        {
            LayoutVariant variant = new LayoutVariant
            {
                VariantId = "variant-" + (index + 1).ToString("00", CultureInfo.InvariantCulture),
                Seed = seed,
                Status = "candidate"
            };
            variant.Diagnostics.AddRange(diagnostics);

            if (corridor == null)
            {
                variant.Status = "failed";
                variant.Diagnostics.Add(Diagnostic.Error("generation.corridor_missing", "Could not derive a corridor strategy inside the cleaned floorplate."));
                return variant;
            }

            Polygon2 corridorPolygon = CorridorPolygon(corridor);
            variant.Corridors.Add(new CorridorLayout
            {
                Id = corridor.Id,
                Polygon = GeometryCleaner.ToPolygonInput(corridorPolygon),
                Centerline = CorridorCenterline(corridor),
                Width = corridor.Width,
                Connections = CorridorConnections(corridorPolygon)
            });

            Dictionary<string, int> currentCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<Polygon2> placedUnits = new List<Polygon2>();

            if (corridor.Orientation == CorridorOrientation.Horizontal)
            {
                AddHorizontalBand(variant, corridor, _input.Floorplate.Bounds().MinY, corridor.MinY, "south", placedUnits, currentCounts, random);
                AddHorizontalBand(variant, corridor, corridor.MaxY, _input.Floorplate.Bounds().MaxY, "north", placedUnits, currentCounts, random);
            }
            else
            {
                AddVerticalBand(variant, corridor, _input.Floorplate.Bounds().MinX, corridor.MinX, "west", placedUnits, currentCounts, random);
                AddVerticalBand(variant, corridor, corridor.MaxX, _input.Floorplate.Bounds().MaxX, "east", placedUnits, currentCounts, random);
            }

            if (variant.Units.Count == 0)
            {
                variant.Status = "failed";
                variant.Diagnostics.Add(Diagnostic.Error("generation.no_units", "No valid unit bays could be placed adjacent to the corridor."));
                variant.Topology = BuildTopology(variant, corridorPolygon);
                return variant;
            }

            int doorIndex = 0;
            foreach (UnitLayout unit in variant.Units)
            {
                UnitTypeTarget target = _mixPlanner.FindTarget(unit.Type);
                _roomGenerator.PopulateUnit(unit, corridor, target);
                variant.Rooms.AddRange(unit.Rooms);
                variant.DoorsOpenings.Add(_roomGenerator.CreateUnitDoor(unit, corridor, doorIndex++));
                variant.Walls.AddRange(_roomGenerator.CreateUnitWalls(unit, corridor));
                variant.Labels.AddRange(_roomGenerator.CreateLabels(unit));
                variant.Corridors[0].Connections.Add(unit.Id);
            }

            variant.Topology = BuildTopology(variant, corridorPolygon);
            return variant;
        }

        private void AddHorizontalBand(
            LayoutVariant variant,
            CorridorStrategy corridor,
            double bandMinY,
            double bandMaxY,
            string side,
            List<Polygon2> placedUnits,
            Dictionary<string, int> currentCounts,
            SeededRandom random)
        {
            double depth = bandMaxY - bandMinY;
            if (depth < Math.Max(5.0, _input.Source.Rules.MinRoomDepth * 1.8))
            {
                variant.Diagnostics.Add(Diagnostic.Warning("generation.band_too_shallow", "Skipped " + side + " unit band because usable depth is below room minimums."));
                return;
            }

            List<Interval1D> intervals = HorizontalUsableIntervals(bandMinY, bandMaxY);
            foreach (Interval1D interval in intervals)
            {
                double start = Math.Max(interval.Start, corridor.MinX);
                double end = Math.Min(interval.End, corridor.MaxX);
                if (end - start <= MinAnyUnitWidth() + _tolerance)
                {
                    continue;
                }

                SplitHorizontalInterval(variant, corridor, start, end, bandMinY, bandMaxY, side, placedUnits, currentCounts, random);
            }
        }

        private void AddVerticalBand(
            LayoutVariant variant,
            CorridorStrategy corridor,
            double bandMinX,
            double bandMaxX,
            string side,
            List<Polygon2> placedUnits,
            Dictionary<string, int> currentCounts,
            SeededRandom random)
        {
            double depth = bandMaxX - bandMinX;
            if (depth < Math.Max(5.0, _input.Source.Rules.MinRoomWidth * 1.8))
            {
                variant.Diagnostics.Add(Diagnostic.Warning("generation.band_too_shallow", "Skipped " + side + " unit band because usable depth is below room minimums."));
                return;
            }

            List<Interval1D> intervals = VerticalUsableIntervals(bandMinX, bandMaxX);
            foreach (Interval1D interval in intervals)
            {
                double start = Math.Max(interval.Start, corridor.MinY);
                double end = Math.Min(interval.End, corridor.MaxY);
                if (end - start <= MinAnyUnitWidth() + _tolerance)
                {
                    continue;
                }

                SplitVerticalInterval(variant, corridor, start, end, bandMinX, bandMaxX, side, placedUnits, currentCounts, random);
            }
        }

        private void SplitHorizontalInterval(
            LayoutVariant variant,
            CorridorStrategy corridor,
            double minX,
            double maxX,
            double minY,
            double maxY,
            string side,
            List<Polygon2> placedUnits,
            Dictionary<string, int> currentCounts,
            SeededRandom random)
        {
            double x = minX;
            double depth = maxY - minY;
            int safety = 0;
            while (maxX - x > MinAnyUnitWidth() + _tolerance && safety++ < 50)
            {
                double remaining = maxX - x;
                double bayAreaHint = Math.Min(remaining, 8.0 + random.Range(-1.0, 1.0)) * depth;
                string type = _mixPlanner.ChooseUnitType(bayAreaHint, currentCounts, _input.Source.GenerationSettings.WeightedVariation, random);
                UnitTypeTarget target = _mixPlanner.FindTarget(type);
                double desiredWidth = ((target.MinArea + target.MaxArea) * 0.5) / Math.Max(depth, 1.0);
                desiredWidth = Clamp(desiredWidth + random.Range(-0.45, 0.45), MinUnitWidth(type), Math.Min(12.0, remaining));
                if (remaining - desiredWidth < MinAnyUnitWidth() + _tolerance)
                {
                    desiredWidth = remaining;
                }

                Polygon2 unitPolygon = Polygon2.Rectangle("unit-" + side + "-" + (variant.Units.Count + 1).ToString("00", CultureInfo.InvariantCulture), x, minY, x + desiredWidth, maxY);
                if (TryAddUnit(variant, unitPolygon, type, placedUnits, corridor))
                {
                    if (!currentCounts.ContainsKey(type))
                    {
                        currentCounts[type] = 0;
                    }

                    currentCounts[type]++;
                }
                else
                {
                    variant.Diagnostics.Add(Diagnostic.Warning("generation.unit_bay_rejected", "Rejected " + unitPolygon.SourceId + " because it was outside the usable floorplate or conflicted with fixed elements.", unitPolygon.SourceId));
                }

                x += desiredWidth;
            }
        }

        private void SplitVerticalInterval(
            LayoutVariant variant,
            CorridorStrategy corridor,
            double minY,
            double maxY,
            double minX,
            double maxX,
            string side,
            List<Polygon2> placedUnits,
            Dictionary<string, int> currentCounts,
            SeededRandom random)
        {
            double y = minY;
            double depth = maxX - minX;
            int safety = 0;
            while (maxY - y > MinAnyUnitWidth() + _tolerance && safety++ < 50)
            {
                double remaining = maxY - y;
                double bayAreaHint = Math.Min(remaining, 8.0 + random.Range(-1.0, 1.0)) * depth;
                string type = _mixPlanner.ChooseUnitType(bayAreaHint, currentCounts, _input.Source.GenerationSettings.WeightedVariation, random);
                UnitTypeTarget target = _mixPlanner.FindTarget(type);
                double desiredHeight = ((target.MinArea + target.MaxArea) * 0.5) / Math.Max(depth, 1.0);
                desiredHeight = Clamp(desiredHeight + random.Range(-0.45, 0.45), MinUnitWidth(type), Math.Min(12.0, remaining));
                if (remaining - desiredHeight < MinAnyUnitWidth() + _tolerance)
                {
                    desiredHeight = remaining;
                }

                Polygon2 unitPolygon = Polygon2.Rectangle("unit-" + side + "-" + (variant.Units.Count + 1).ToString("00", CultureInfo.InvariantCulture), minX, y, maxX, y + desiredHeight);
                if (TryAddUnit(variant, unitPolygon, type, placedUnits, corridor))
                {
                    if (!currentCounts.ContainsKey(type))
                    {
                        currentCounts[type] = 0;
                    }

                    currentCounts[type]++;
                }
                else
                {
                    variant.Diagnostics.Add(Diagnostic.Warning("generation.unit_bay_rejected", "Rejected " + unitPolygon.SourceId + " because it was outside the usable floorplate or conflicted with fixed elements.", unitPolygon.SourceId));
                }

                y += desiredHeight;
            }
        }

        private bool TryAddUnit(LayoutVariant variant, Polygon2 unitPolygon, string type, List<Polygon2> placedUnits, CorridorStrategy corridor)
        {
            if (!IsUsable(unitPolygon, placedUnits))
            {
                return false;
            }

            Polygon2 corridorPolygon = CorridorPolygon(corridor);
            if (GeometryPredicates.PolygonsOverlapArea(unitPolygon, corridorPolygon, _tolerance))
            {
                return false;
            }

            UnitLayout unit = new UnitLayout
            {
                Id = unitPolygon.SourceId,
                Type = type,
                Polygon = GeometryCleaner.ToPolygonInput(unitPolygon),
                Area = Math.Round(unitPolygon.Area(), 4),
                FacadeLength = Math.Round(FacadeAnalyzer.DaylightFacadeLength(unitPolygon, _input.Floorplate, _input.Source.Facade, _tolerance), 4)
            };
            variant.Units.Add(unit);
            placedUnits.Add(unitPolygon);
            return true;
        }

        private bool IsUsable(Polygon2 polygon, List<Polygon2> existing)
        {
            if (!GeometryPredicates.ContainsPolygon(_input.Floorplate, polygon, _tolerance))
            {
                return false;
            }

            foreach (Polygon2 hole in _input.Holes)
            {
                if (GeometryPredicates.PolygonsOverlapArea(polygon, hole, _tolerance) ||
                    GeometryPredicates.ContainsPoint(hole, polygon.Centroid(), _tolerance, false))
                {
                    return false;
                }
            }

            foreach (CleanedFixedElement fixedElement in _input.FixedElements.Where(f => f.BlocksGeneration))
            {
                if (GeometryPredicates.PolygonsOverlapArea(polygon, fixedElement.Polygon, _tolerance))
                {
                    return false;
                }
            }

            foreach (Polygon2 other in existing)
            {
                if (GeometryPredicates.PolygonsOverlapArea(polygon, other, _tolerance))
                {
                    return false;
                }
            }

            return polygon.Area() >= _input.Source.Rules.MinUnitArea;
        }

        private CorridorStrategy TryResolveCorridor(int variantIndex, SeededRandom random, List<Diagnostic> diagnostics)
        {
            if (_input.Source.Access != null &&
                _input.Source.Access.CorridorCenterlines != null &&
                _input.Source.Access.CorridorCenterlines.Count > 0)
            {
                LineInput line = _input.Source.Access.CorridorCenterlines[variantIndex % _input.Source.Access.CorridorCenterlines.Count];
                CorridorStrategy provided = CorridorFromLine(line, diagnostics);
                if (provided != null)
                {
                    diagnostics.Add(Diagnostic.Info("generation.corridor_from_input", "Used provided corridor centerline.", line.Id));
                    return provided;
                }
            }

            Bounds2 bounds = _input.Floorplate.Bounds();
            double width = Math.Max(_input.Source.Rules.MinCorridorWidth, 1.2);
            CorridorOrientation primary = bounds.Width >= bounds.Height ? CorridorOrientation.Horizontal : CorridorOrientation.Vertical;
            CorridorStrategy strategy = TryCoreAdjacentCorridor(primary, width, diagnostics);
            if (strategy != null)
            {
                return strategy;
            }

            strategy = TryCoreAdjacentCorridor(primary == CorridorOrientation.Horizontal ? CorridorOrientation.Vertical : CorridorOrientation.Horizontal, width, diagnostics);
            if (strategy != null)
            {
                return strategy;
            }

            double[] offsets = new[] { 0.0, -0.08, 0.08, -0.16, 0.16, -0.24, 0.24, -0.32, 0.32 };
            foreach (double offset in offsets)
            {
                double jitter = _input.Source.GenerationSettings.WeightedVariation ? random.Range(-0.025, 0.025) : 0.0;
                double fraction = Clamp(0.5 + offset + jitter, 0.20, 0.80);
                strategy = TryCorridorAtFraction(primary, fraction, width, diagnostics);
                if (strategy != null)
                {
                    diagnostics.Add(Diagnostic.Info("generation.corridor_derived", "Derived central corridor from cleaned floorplate scan."));
                    return strategy;
                }
            }

            CorridorOrientation secondary = primary == CorridorOrientation.Horizontal ? CorridorOrientation.Vertical : CorridorOrientation.Horizontal;
            foreach (double offset in offsets)
            {
                double fraction = Clamp(0.5 + offset, 0.20, 0.80);
                strategy = TryCorridorAtFraction(secondary, fraction, width, diagnostics);
                if (strategy != null)
                {
                    diagnostics.Add(Diagnostic.Warning("generation.corridor_secondary_axis", "Primary corridor axis failed; derived corridor on secondary axis."));
                    return strategy;
                }
            }

            return null;
        }

        private CorridorStrategy CorridorFromLine(LineInput line, List<Diagnostic> diagnostics)
        {
            double width = Math.Max(_input.Source.Rules.MinCorridorWidth, 1.2);
            double dx = Math.Abs(line.End.X - line.Start.X);
            double dy = Math.Abs(line.End.Y - line.Start.Y);
            if (dx >= dy)
            {
                double centerY = (line.Start.Y + line.End.Y) * 0.5;
                CorridorStrategy strategy = new CorridorStrategy
                {
                    Id = string.IsNullOrWhiteSpace(line.Id) ? "corridor-1" : line.Id,
                    Orientation = CorridorOrientation.Horizontal,
                    MinX = Math.Min(line.Start.X, line.End.X),
                    MaxX = Math.Max(line.Start.X, line.End.X),
                    MinY = centerY - (width * 0.5),
                    MaxY = centerY + (width * 0.5),
                    Width = width
                };
                return CorridorIsUsable(strategy) ? strategy : null;
            }

            double centerX = (line.Start.X + line.End.X) * 0.5;
            CorridorStrategy vertical = new CorridorStrategy
            {
                Id = string.IsNullOrWhiteSpace(line.Id) ? "corridor-1" : line.Id,
                Orientation = CorridorOrientation.Vertical,
                MinX = centerX - (width * 0.5),
                MaxX = centerX + (width * 0.5),
                MinY = Math.Min(line.Start.Y, line.End.Y),
                MaxY = Math.Max(line.Start.Y, line.End.Y),
                Width = width
            };
            return CorridorIsUsable(vertical) ? vertical : null;
        }

        private CorridorStrategy TryCoreAdjacentCorridor(CorridorOrientation orientation, double width, List<Diagnostic> diagnostics)
        {
            CleanedFixedElement core = _input.FixedElements.FirstOrDefault(f => f.Type.IndexOf("core", StringComparison.OrdinalIgnoreCase) >= 0);
            if (core == null)
            {
                return null;
            }

            Bounds2 coreBounds = core.Polygon.Bounds();
            if (orientation == CorridorOrientation.Horizontal)
            {
                CorridorStrategy above = TryBuildHorizontalCorridor(coreBounds.MaxY + (width * 0.5), width);
                if (above != null)
                {
                    diagnostics.Add(Diagnostic.Info("generation.corridor_core_adjacent", "Derived corridor adjacent to fixed core.", core.Id));
                    return above;
                }

                CorridorStrategy below = TryBuildHorizontalCorridor(coreBounds.MinY - (width * 0.5), width);
                if (below != null)
                {
                    diagnostics.Add(Diagnostic.Info("generation.corridor_core_adjacent", "Derived corridor adjacent to fixed core.", core.Id));
                    return below;
                }
            }
            else
            {
                CorridorStrategy right = TryBuildVerticalCorridor(coreBounds.MaxX + (width * 0.5), width);
                if (right != null)
                {
                    diagnostics.Add(Diagnostic.Info("generation.corridor_core_adjacent", "Derived corridor adjacent to fixed core.", core.Id));
                    return right;
                }

                CorridorStrategy left = TryBuildVerticalCorridor(coreBounds.MinX - (width * 0.5), width);
                if (left != null)
                {
                    diagnostics.Add(Diagnostic.Info("generation.corridor_core_adjacent", "Derived corridor adjacent to fixed core.", core.Id));
                    return left;
                }
            }

            return null;
        }

        private CorridorStrategy TryCorridorAtFraction(CorridorOrientation orientation, double fraction, double width, List<Diagnostic> diagnostics)
        {
            Bounds2 bounds = _input.Floorplate.Bounds();
            if (orientation == CorridorOrientation.Horizontal)
            {
                return TryBuildHorizontalCorridor(bounds.MinY + (bounds.Height * fraction), width);
            }

            return TryBuildVerticalCorridor(bounds.MinX + (bounds.Width * fraction), width);
        }

        private CorridorStrategy TryBuildHorizontalCorridor(double centerY, double width)
        {
            double minY = centerY - (width * 0.5);
            double maxY = centerY + (width * 0.5);
            if (maxY <= _input.Floorplate.Bounds().MinY + _tolerance || minY >= _input.Floorplate.Bounds().MaxY - _tolerance)
            {
                return null;
            }

            double lowY;
            double midY;
            double highY;
            SampleSpan(minY, maxY, out lowY, out midY, out highY);
            List<Interval1D> lower = GeometryPredicates.HorizontalInsideIntervals(_input.Floorplate, lowY, _tolerance);
            List<Interval1D> center = GeometryPredicates.HorizontalInsideIntervals(_input.Floorplate, midY, _tolerance);
            List<Interval1D> upper = GeometryPredicates.HorizontalInsideIntervals(_input.Floorplate, highY, _tolerance);
            List<Interval1D> shared = GeometryPredicates.IntersectIntervals(GeometryPredicates.IntersectIntervals(lower, center, _tolerance), upper, _tolerance);
            List<Interval1D> candidateIntervals = shared
                .SelectMany(i => SubtractBlockingIntervals(i, minY, maxY, CorridorOrientation.Horizontal))
                .ToList();
            Interval1D best = candidateIntervals.OrderByDescending(i => i.Length).FirstOrDefault();
            if (best == null || best.Length < Math.Max(width * 4.0, 7.0))
            {
                return null;
            }

            CorridorStrategy strategy = new CorridorStrategy
            {
                Id = "corridor-1",
                Orientation = CorridorOrientation.Horizontal,
                MinX = best.Start,
                MaxX = best.End,
                MinY = minY,
                MaxY = maxY,
                Width = width
            };
            return CorridorIsUsable(strategy) ? strategy : null;
        }

        private CorridorStrategy TryBuildVerticalCorridor(double centerX, double width)
        {
            double minX = centerX - (width * 0.5);
            double maxX = centerX + (width * 0.5);
            if (maxX <= _input.Floorplate.Bounds().MinX + _tolerance || minX >= _input.Floorplate.Bounds().MaxX - _tolerance)
            {
                return null;
            }

            double lowX;
            double midX;
            double highX;
            SampleSpan(minX, maxX, out lowX, out midX, out highX);
            List<Interval1D> left = GeometryPredicates.VerticalInsideIntervals(_input.Floorplate, lowX, _tolerance);
            List<Interval1D> center = GeometryPredicates.VerticalInsideIntervals(_input.Floorplate, midX, _tolerance);
            List<Interval1D> right = GeometryPredicates.VerticalInsideIntervals(_input.Floorplate, highX, _tolerance);
            List<Interval1D> shared = GeometryPredicates.IntersectIntervals(GeometryPredicates.IntersectIntervals(left, center, _tolerance), right, _tolerance);
            List<Interval1D> candidateIntervals = shared
                .SelectMany(i => SubtractBlockingIntervals(i, minX, maxX, CorridorOrientation.Vertical))
                .ToList();
            Interval1D best = candidateIntervals.OrderByDescending(i => i.Length).FirstOrDefault();
            if (best == null || best.Length < Math.Max(width * 4.0, 7.0))
            {
                return null;
            }

            CorridorStrategy strategy = new CorridorStrategy
            {
                Id = "corridor-1",
                Orientation = CorridorOrientation.Vertical,
                MinX = minX,
                MaxX = maxX,
                MinY = best.Start,
                MaxY = best.End,
                Width = width
            };
            return CorridorIsUsable(strategy) ? strategy : null;
        }

        private List<Interval1D> HorizontalUsableIntervals(double minY, double maxY)
        {
            double lowY;
            double midY;
            double highY;
            SampleSpan(minY, maxY, out lowY, out midY, out highY);

            List<Interval1D> lower = GeometryPredicates.HorizontalInsideIntervals(_input.Floorplate, lowY, _tolerance);
            List<Interval1D> center = GeometryPredicates.HorizontalInsideIntervals(_input.Floorplate, midY, _tolerance);
            List<Interval1D> upper = GeometryPredicates.HorizontalInsideIntervals(_input.Floorplate, highY, _tolerance);
            return GeometryPredicates.IntersectIntervals(GeometryPredicates.IntersectIntervals(lower, center, _tolerance), upper, _tolerance)
                .SelectMany(i => SubtractBlockingIntervals(i, minY, maxY, CorridorOrientation.Horizontal))
                .OrderBy(i => i.Start)
                .ToList();
        }

        private List<Interval1D> VerticalUsableIntervals(double minX, double maxX)
        {
            double lowX;
            double midX;
            double highX;
            SampleSpan(minX, maxX, out lowX, out midX, out highX);

            List<Interval1D> left = GeometryPredicates.VerticalInsideIntervals(_input.Floorplate, lowX, _tolerance);
            List<Interval1D> center = GeometryPredicates.VerticalInsideIntervals(_input.Floorplate, midX, _tolerance);
            List<Interval1D> right = GeometryPredicates.VerticalInsideIntervals(_input.Floorplate, highX, _tolerance);
            return GeometryPredicates.IntersectIntervals(GeometryPredicates.IntersectIntervals(left, center, _tolerance), right, _tolerance)
                .SelectMany(i => SubtractBlockingIntervals(i, minX, maxX, CorridorOrientation.Vertical))
                .OrderBy(i => i.Start)
                .ToList();
        }

        private List<Interval1D> SubtractBlockingIntervals(Interval1D source, double spanMin, double spanMax, CorridorOrientation orientation)
        {
            List<Interval1D> remaining = new List<Interval1D> { source };
            foreach (Bounds2 blocker in BlockingBounds())
            {
                bool overlapsSpan;
                double blockedStart;
                double blockedEnd;
                if (orientation == CorridorOrientation.Horizontal)
                {
                    overlapsSpan = RangesOverlapArea(blocker.MinY, blocker.MaxY, spanMin, spanMax);
                    blockedStart = blocker.MinX;
                    blockedEnd = blocker.MaxX;
                }
                else
                {
                    overlapsSpan = RangesOverlapArea(blocker.MinX, blocker.MaxX, spanMin, spanMax);
                    blockedStart = blocker.MinY;
                    blockedEnd = blocker.MaxY;
                }

                if (!overlapsSpan)
                {
                    continue;
                }

                remaining = SubtractInterval(remaining, blockedStart - _tolerance, blockedEnd + _tolerance);
                if (remaining.Count == 0)
                {
                    break;
                }
            }

            return remaining.Where(i => i.Length > _tolerance).ToList();
        }

        private IEnumerable<Bounds2> BlockingBounds()
        {
            foreach (Polygon2 hole in _input.Holes)
            {
                yield return hole.Bounds();
            }

            foreach (CleanedFixedElement fixedElement in _input.FixedElements.Where(f => f.BlocksGeneration))
            {
                yield return fixedElement.Polygon.Bounds();
            }
        }

        private List<Interval1D> SubtractInterval(IEnumerable<Interval1D> intervals, double removeStart, double removeEnd)
        {
            List<Interval1D> result = new List<Interval1D>();
            foreach (Interval1D interval in intervals)
            {
                if (removeEnd <= interval.Start + _tolerance || removeStart >= interval.End - _tolerance)
                {
                    result.Add(interval);
                    continue;
                }

                if (removeStart - interval.Start > _tolerance)
                {
                    result.Add(new Interval1D(interval.Start, Math.Max(interval.Start, removeStart)));
                }

                if (interval.End - removeEnd > _tolerance)
                {
                    result.Add(new Interval1D(Math.Min(interval.End, removeEnd), interval.End));
                }
            }

            return result;
        }

        private bool RangesOverlapArea(double firstMin, double firstMax, double secondMin, double secondMax)
        {
            return firstMax > secondMin + _tolerance && secondMax > firstMin + _tolerance;
        }

        private void SampleSpan(double min, double max, out double low, out double mid, out double high)
        {
            double span = Math.Max(0.0, max - min);
            double inset = Math.Min(Math.Max(_tolerance * 2.0, span * 0.01), span * 0.25);
            low = min + inset;
            mid = (min + max) * 0.5;
            high = max - inset;
            if (high < low)
            {
                low = mid;
                high = mid;
            }
        }

        private bool CorridorIsUsable(CorridorStrategy strategy)
        {
            Polygon2 polygon = CorridorPolygon(strategy);
            if (!GeometryPredicates.ContainsPolygon(_input.Floorplate, polygon, _tolerance))
            {
                return false;
            }

            foreach (Polygon2 hole in _input.Holes)
            {
                if (GeometryPredicates.PolygonsOverlapArea(polygon, hole, _tolerance))
                {
                    return false;
                }
            }

            foreach (CleanedFixedElement fixedElement in _input.FixedElements.Where(f => f.BlocksGeneration))
            {
                if (GeometryPredicates.PolygonsOverlapArea(polygon, fixedElement.Polygon, _tolerance))
                {
                    return false;
                }
            }

            return true;
        }

        private List<string> CorridorConnections(Polygon2 corridorPolygon)
        {
            List<string> connections = new List<string>();
            foreach (CleanedFixedElement fixedElement in _input.FixedElements)
            {
                if ((fixedElement.Type.IndexOf("core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     fixedElement.Type.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     fixedElement.Type.IndexOf("elevator", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    FixedElementConnectsToCorridor(corridorPolygon, fixedElement))
                {
                    connections.Add(fixedElement.Id);
                }
            }

            int accessIndex = 0;
            foreach (Point2 point in _input.Source.Access.VerticalCoreAccess)
            {
                accessIndex++;
                if (GeometryPredicates.ContainsPoint(corridorPolygon, point, _tolerance, true))
                {
                    connections.Add("vertical_access_" + accessIndex.ToString(CultureInfo.InvariantCulture));
                }
            }

            return connections;
        }

        private TopologyGraph BuildTopology(LayoutVariant variant, Polygon2 corridorPolygon)
        {
            TopologyGraph graph = new TopologyGraph();
            graph.AddNode("floorplate", "floorplate", _input.Floorplate.SourceId);

            foreach (CleanedFixedElement fixedElement in _input.FixedElements)
            {
                graph.AddNode(fixedElement.Id, fixedElement.Type, fixedElement.Id, "floorplate");
                graph.AddEdge(fixedElement.Id, "floorplate", "belongs_to", "fixed_element");
                if (FixedElementConnectsToCorridor(corridorPolygon, fixedElement))
                {
                    graph.AddEdge(fixedElement.Id, variant.Corridors[0].Id, "connects_to_corridor", "touches corridor boundary");
                }
            }

            foreach (CorridorLayout corridor in variant.Corridors)
            {
                graph.AddNode(corridor.Id, "corridor", corridor.Id, "floorplate");
                graph.AddEdge(corridor.Id, "floorplate", "belongs_to", "circulation");
            }

            foreach (UnitLayout unit in variant.Units)
            {
                graph.AddNode(unit.Id, "unit", unit.Id, "floorplate");
                graph.AddEdge(unit.Id, "floorplate", "belongs_to", "unit containment");
                graph.AddEdge(unit.Id, variant.Corridors[0].Id, "has_door", "unit entry");
                if (unit.FacadeLength > _tolerance)
                {
                    graph.AddEdge(unit.Id, "outside", "has_facade", "daylight facade exposure");
                }

                foreach (RoomLayout room in unit.Rooms)
                {
                    graph.AddNode(room.Id, "room:" + room.RoomType, room.Id, unit.Id);
                    graph.AddEdge(room.Id, unit.Id, "belongs_to", "room containment");
                    if (room.Daylight)
                    {
                        graph.AddEdge(room.Id, "outside", "has_facade", "room daylight exposure");
                    }

                    if (room.RoomType.IndexOf("bedroom", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        room.RoomType.IndexOf("living", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        graph.AddEdge(room.Id, "outside", "constraint_requires_facade", "habitable room daylight requirement");
                    }
                }
            }

            graph.Hypergraph = BuildHypergraph(variant, graph);
            return graph;
        }

        private FloorPlanHypergraph BuildHypergraph(LayoutVariant variant, TopologyGraph graph)
        {
            double grossArea = Math.Round(Math.Max(
                _input.Floorplate.Area() - _input.Holes.Sum(h => h.Area()),
                variant.Units.Sum(u => u.Area) + variant.Corridors.Sum(c => c.Bounds != null ? c.Bounds.Area : 0.0)),
                4);

            HypergraphDataNode root = NewDataNode("root", grossArea, 0.0, "root", false, GeometryCleaner.ToPolygonInput(_input.Floorplate));
            HypergraphDataNode circulation = NewDataNode(
                "circulation",
                Math.Round(variant.Corridors.Sum(c => c.Bounds != null ? c.Bounds.Area : 0.0), 4),
                0.0,
                "circulation",
                false,
                null);
            foreach (CorridorLayout corridor in variant.Corridors)
            {
                HypergraphDataNode corridorNode = NewDataNode(corridor.Id, PolygonArea(corridor.Polygon), CorridorAngle(corridor.Centerline), "corridor", true, corridor.Polygon);
                corridorNode.Connected = variant.Units
                    .Where(unit => variant.DoorsOpenings.Any(door => door.ConnectsSpaces.Contains(unit.Id) && door.ConnectsSpaces.Contains(corridor.Id)))
                    .Select(unit => unit.Id)
                    .Concat(_input.FixedElements.Where(f => graph.Edges.Any(e => e.From == f.Id && e.To == corridor.Id)).Select(f => f.Id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                circulation.Children.Add(corridorNode);
            }

            HypergraphDataNode unitsGroup = NewDataNode("units", Math.Round(variant.Units.Sum(u => u.Area), 4), Math.PI * 0.5, "units", false, null);
            foreach (UnitLayout unit in variant.Units)
            {
                HypergraphDataNode unitNode = NewDataNode(unit.Id, unit.Area, 0.0, unit.Type, false, unit.Polygon);
                foreach (RoomLayout room in unit.Rooms)
                {
                    HypergraphDataNode roomNode = NewDataNode(room.Id, room.Area, RoomSplitAngle(room), NormalizeRoomMergeId(room.RoomType), true, room.Polygon);
                    roomNode.Connected = RoomConnections(room, variant);
                    unitNode.Children.Add(roomNode);
                }

                if (unitNode.Children.Count == 0)
                {
                    unitNode.Final = true;
                }

                unitsGroup.Children.Add(unitNode);
            }

            if (circulation.Children.Count > 0)
            {
                root.Children.Add(circulation);
            }

            if (unitsGroup.Children.Count > 0)
            {
                root.Children.Add(unitsGroup);
            }

            if (_input.FixedElements.Count > 0)
            {
                HypergraphDataNode fixedGroup = NewDataNode(
                    "fixed-elements",
                    Math.Round(_input.FixedElements.Sum(f => f.Polygon.Area()), 4),
                    0.0,
                    "fixed",
                    false,
                    null);
                foreach (CleanedFixedElement fixedElement in _input.FixedElements)
                {
                    HypergraphDataNode fixedNode = NewDataNode(fixedElement.Id, fixedElement.Polygon.Area(), fixedElement.Polygon.Bounds().Width >= fixedElement.Polygon.Bounds().Height ? 0.0 : Math.PI * 0.5, fixedElement.Type, true, GeometryCleaner.ToPolygonInput(fixedElement.Polygon));
                    fixedNode.Connected = graph.Edges
                        .Where(e => string.Equals(e.From, fixedElement.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Kind, "connects_to_corridor", StringComparison.OrdinalIgnoreCase))
                        .Select(e => e.To)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    fixedGroup.Children.Add(fixedNode);
                }

                root.Children.Add(fixedGroup);
            }

            FloorPlanHypergraph hypergraph = new FloorPlanHypergraph
            {
                Root = root,
                Nodes = BuildHypergraphNodes(root, variant, graph),
                Hyperedges = new List<Hyperedge>(),
                Incidence = new List<HypergraphIncidence>(),
                Matrices = new HypergraphMatrices()
            };

            AddTopologyOnlyNodes(hypergraph, graph);
            AddOutsideNodeIfNeeded(hypergraph, graph);
            AddWallAndDoorNodes(hypergraph, variant);
            AddSubdivisionHyperedges(hypergraph, root);
            AddDataNodeAdjacencyHyperedges(hypergraph, root);
            AddTopologyHyperedges(hypergraph, graph);
            AddDoorHyperedges(hypergraph, variant);
            BuildIncidence(hypergraph);
            hypergraph.Matrices = BuildMatrices(hypergraph);
            return hypergraph;
        }

        private static HypergraphDataNode NewDataNode(string name, double area, double angle, string mergeId, bool final, PolygonInput polygon)
        {
            Point2 centroid = polygon != null ? PolygonCentroid(polygon) : new Point2();
            double roundedArea = Math.Round(Math.Max(0.0, area), 4);
            return new HypergraphDataNode
            {
                Name = name,
                Area = roundedArea,
                Angle = Math.Round(angle, 6),
                MergeId = string.IsNullOrWhiteSpace(mergeId) ? name : mergeId,
                Final = final,
                Children = new List<HypergraphDataNode>(),
                Connected = new List<string>(),
                TreeNodeMesh = new HypergraphTreeNodeMesh
                {
                    Area = roundedArea,
                    Centroid = new HypergraphCentroid
                    {
                        X = Math.Round(centroid.X, 4),
                        Y = Math.Round(centroid.Y, 4),
                        Z = 0.0,
                        Mag = Math.Round(Math.Sqrt(centroid.X * centroid.X + centroid.Y * centroid.Y), 4)
                    }
                }
            };
        }

        private List<HypergraphNode> BuildHypergraphNodes(HypergraphDataNode root, LayoutVariant variant, TopologyGraph graph)
        {
            List<HypergraphNode> nodes = new List<HypergraphNode>();
            AddHypergraphNodeRecursive(root, string.Empty, 0, variant, graph, nodes);
            return nodes;
        }

        private void AddHypergraphNodeRecursive(HypergraphDataNode dataNode, string parentId, int level, LayoutVariant variant, TopologyGraph graph, List<HypergraphNode> nodes)
        {
            nodes.Add(new HypergraphNode
            {
                Id = dataNode.Name,
                Kind = NodeKind(dataNode, variant, graph),
                ReferenceId = dataNode.Name,
                ParentId = parentId,
                MergeId = dataNode.MergeId,
                Final = dataNode.Final,
                Level = level,
                Area = dataNode.Area,
                Angle = dataNode.Angle,
                Centroid = new Point2(dataNode.TreeNodeMesh.Centroid.X, dataNode.TreeNodeMesh.Centroid.Y),
                Children = (dataNode.Children ?? new List<HypergraphDataNode>()).Select(c => c.Name).ToList(),
                Connected = dataNode.Connected != null ? dataNode.Connected.ToList() : new List<string>(),
                Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "source", "hypergraph.DataNode" }
                }
            });

            foreach (HypergraphDataNode child in dataNode.Children ?? new List<HypergraphDataNode>())
            {
                AddHypergraphNodeRecursive(child, dataNode.Name, level + 1, variant, graph, nodes);
            }
        }

        private static string NodeKind(HypergraphDataNode node, LayoutVariant variant, TopologyGraph graph)
        {
            if (variant.Corridors.Any(c => string.Equals(c.Id, node.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return "corridor";
            }

            UnitLayout unit = variant.Units.FirstOrDefault(u => string.Equals(u.Id, node.Name, StringComparison.OrdinalIgnoreCase));
            if (unit != null)
            {
                return "unit:" + unit.Type;
            }

            RoomLayout room = variant.Rooms.FirstOrDefault(r => string.Equals(r.Id, node.Name, StringComparison.OrdinalIgnoreCase));
            if (room != null)
            {
                return "room:" + room.RoomType;
            }

            SpaceNode existing = graph.Nodes.FirstOrDefault(n => string.Equals(n.Id, node.Name, StringComparison.OrdinalIgnoreCase));
            return existing != null ? existing.Kind : "group:" + node.MergeId;
        }

        private static void AddTopologyOnlyNodes(FloorPlanHypergraph hypergraph, TopologyGraph graph)
        {
            foreach (SpaceNode topologyNode in graph.Nodes)
            {
                if (hypergraph.Nodes.Any(n => string.Equals(n.Id, topologyNode.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                HypergraphNode node = new HypergraphNode
                {
                    Id = topologyNode.Id,
                    Kind = topologyNode.Kind,
                    ReferenceId = topologyNode.ReferenceId,
                    ParentId = string.IsNullOrWhiteSpace(topologyNode.ParentId) && string.Equals(topologyNode.Id, "floorplate", StringComparison.OrdinalIgnoreCase)
                        ? "root"
                        : topologyNode.ParentId,
                    MergeId = string.IsNullOrWhiteSpace(topologyNode.Kind) ? topologyNode.Id : topologyNode.Kind,
                    Final = true,
                    Level = string.Equals(topologyNode.Id, "floorplate", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                    Children = graph.Nodes
                        .Where(candidate => string.Equals(candidate.ParentId, topologyNode.Id, StringComparison.OrdinalIgnoreCase))
                        .Select(candidate => candidate.Id)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "source", "TopologyGraph.Nodes" }
                    }
                };

                if (string.Equals(topologyNode.Id, "floorplate", StringComparison.OrdinalIgnoreCase) && hypergraph.Root != null && hypergraph.Root.TreeNodeMesh != null)
                {
                    node.Area = hypergraph.Root.Area;
                    if (hypergraph.Root.TreeNodeMesh.Centroid != null)
                    {
                        node.Centroid = new Point2(hypergraph.Root.TreeNodeMesh.Centroid.X, hypergraph.Root.TreeNodeMesh.Centroid.Y);
                    }
                }

                hypergraph.Nodes.Add(node);
            }
        }

        private static void AddOutsideNodeIfNeeded(FloorPlanHypergraph hypergraph, TopologyGraph graph)
        {
            if (!graph.Edges.Any(e => string.Equals(e.To, "outside", StringComparison.OrdinalIgnoreCase) || string.Equals(e.From, "outside", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (hypergraph.Nodes.Any(n => string.Equals(n.Id, "outside", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            hypergraph.Nodes.Add(new HypergraphNode
            {
                Id = "outside",
                Kind = "external:facade",
                ReferenceId = "outside",
                MergeId = "outside",
                Final = true,
                Level = 0,
                Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "source", "implicit facade exterior" }
                }
            });
        }

        private static void AddWallAndDoorNodes(FloorPlanHypergraph hypergraph, LayoutVariant variant)
        {
            foreach (WallLayout wall in variant.Walls)
            {
                if (hypergraph.Nodes.Any(n => string.Equals(n.Id, wall.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                hypergraph.Nodes.Add(new HypergraphNode
                {
                    Id = wall.Id,
                    Kind = "wall:" + wall.LayerType,
                    ReferenceId = wall.Id,
                    MergeId = "wall",
                    Final = true,
                    Level = 0,
                    Area = Math.Round(Math.Max(0.0, wall.Thickness * Distance(wall.Centerline.Start, wall.Centerline.End)), 4),
                    Centroid = new Point2((wall.Centerline.Start.X + wall.Centerline.End.X) * 0.5, (wall.Centerline.Start.Y + wall.Centerline.End.Y) * 0.5),
                    Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "source", "generated wall" } }
                });
            }

            foreach (DoorOpening door in variant.DoorsOpenings)
            {
                if (hypergraph.Nodes.Any(n => string.Equals(n.Id, door.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                hypergraph.Nodes.Add(new HypergraphNode
                {
                    Id = door.Id,
                    Kind = "door",
                    ReferenceId = door.Id,
                    MergeId = "door",
                    Final = true,
                    Level = 0,
                    Area = Math.Round(Math.Max(0.0, door.Width), 4),
                    Centroid = door.Location.Clone(),
                    Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "source", "generated door opening" } }
                });
            }
        }

        private static void AddSubdivisionHyperedges(FloorPlanHypergraph hypergraph, HypergraphDataNode node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Children != null && node.Children.Count > 0)
            {
                Hyperedge edge = new Hyperedge
                {
                    Id = "subdivision-" + node.Name,
                    Kind = "subdivision",
                    Weight = Math.Round(node.Children.Sum(c => c.Area), 4),
                    Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "source", "DataNode.children" },
                        { "parent", node.Name }
                    }
                };
                edge.Members.Add(new HyperedgeMember { NodeId = node.Name, Role = "parent" });
                foreach (HypergraphDataNode child in node.Children)
                {
                    edge.Members.Add(new HyperedgeMember { NodeId = child.Name, Role = "child" });
                }

                hypergraph.Hyperedges.Add(edge);
            }

            foreach (HypergraphDataNode child in node.Children ?? new List<HypergraphDataNode>())
            {
                AddSubdivisionHyperedges(hypergraph, child);
            }
        }

        private static void AddDataNodeAdjacencyHyperedges(FloorPlanHypergraph hypergraph, HypergraphDataNode root)
        {
            HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HypergraphDataNode node in HypergraphBuilder.FlattenDataNodes(root))
            {
                foreach (string connected in node.Connected ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(connected) || !hypergraph.Nodes.Any(n => string.Equals(n.Id, connected, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string a = string.CompareOrdinal(node.Name, connected) <= 0 ? node.Name : connected;
                    string b = string.CompareOrdinal(node.Name, connected) <= 0 ? connected : node.Name;
                    string id = "adjacency-" + a + "-" + b;
                    if (!emitted.Add(id))
                    {
                        continue;
                    }

                    Hyperedge edge = new Hyperedge
                    {
                        Id = id,
                        Kind = "adjacency",
                        Weight = 1.0,
                        Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            { "source", "DataNode.connected" }
                        }
                    };
                    edge.Members.Add(new HyperedgeMember { NodeId = a, Role = "space" });
                    edge.Members.Add(new HyperedgeMember { NodeId = b, Role = "space" });
                    hypergraph.Hyperedges.Add(edge);
                }
            }
        }

        private static void AddTopologyHyperedges(FloorPlanHypergraph hypergraph, TopologyGraph graph)
        {
            int index = 0;
            foreach (AdjacencyEdge simpleEdge in graph.Edges)
            {
                if (!hypergraph.Nodes.Any(n => string.Equals(n.Id, simpleEdge.From, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!hypergraph.Nodes.Any(n => string.Equals(n.Id, simpleEdge.To, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                index++;
                Hyperedge edge = new Hyperedge
                {
                    Id = "topology-" + index.ToString("000", CultureInfo.InvariantCulture) + "-" + simpleEdge.Kind + "-" + simpleEdge.From + "-" + simpleEdge.To,
                    Kind = HyperedgeKind(simpleEdge.Kind),
                    Weight = 1.0,
                    Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "source", "TopologyGraph.Edges" },
                        { "simpleEdgeKind", simpleEdge.Kind },
                        { "reason", simpleEdge.Reason ?? string.Empty }
                    }
                };
                edge.Members.Add(new HyperedgeMember { NodeId = simpleEdge.From, Role = "from" });
                edge.Members.Add(new HyperedgeMember { NodeId = simpleEdge.To, Role = "to" });
                hypergraph.Hyperedges.Add(edge);
            }
        }

        private static void AddDoorHyperedges(FloorPlanHypergraph hypergraph, LayoutVariant variant)
        {
            foreach (DoorOpening door in variant.DoorsOpenings)
            {
                Hyperedge edge = new Hyperedge
                {
                    Id = "door-" + door.Id,
                    Kind = "door",
                    Weight = Math.Round(Math.Max(0.0, door.Width), 4),
                    Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "source", "DoorOpening" },
                        { "doorId", door.Id },
                        { "hostWall", door.HostWall ?? string.Empty }
                    }
                };
                edge.Members.Add(new HyperedgeMember { NodeId = door.Id, Role = "opening" });
                if (hypergraph.Nodes.Any(n => string.Equals(n.Id, door.HostWall, StringComparison.OrdinalIgnoreCase)))
                {
                    edge.Members.Add(new HyperedgeMember { NodeId = door.HostWall, Role = "host_wall" });
                }

                foreach (string spaceId in door.ConnectsSpaces ?? new List<string>())
                {
                    if (hypergraph.Nodes.Any(n => string.Equals(n.Id, spaceId, StringComparison.OrdinalIgnoreCase)))
                    {
                        edge.Members.Add(new HyperedgeMember { NodeId = spaceId, Role = "connected_space" });
                    }
                }

                if (edge.Members.Count >= 2)
                {
                    hypergraph.Hyperedges.Add(edge);
                }
            }
        }

        private static void BuildIncidence(FloorPlanHypergraph hypergraph)
        {
            hypergraph.Incidence = new List<HypergraphIncidence>();
            int index = 0;
            foreach (Hyperedge edge in hypergraph.Hyperedges)
            {
                foreach (HyperedgeMember member in edge.Members)
                {
                    index++;
                    hypergraph.Incidence.Add(new HypergraphIncidence
                    {
                        Id = "incidence-" + index.ToString("0000", CultureInfo.InvariantCulture),
                        HyperedgeId = edge.Id,
                        NodeId = member.NodeId,
                        Role = member.Role,
                        Weight = edge.Weight == 0.0 ? 1.0 : edge.Weight
                    });
                }
            }
        }

        private static HypergraphMatrices BuildMatrices(FloorPlanHypergraph hypergraph)
        {
            HypergraphMatrices matrices = new HypergraphMatrices();
            matrices.NodeOrder = hypergraph.Nodes.Select(n => n.Id).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            matrices.HyperedgeOrder = hypergraph.Hyperedges.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            Dictionary<string, int> nodeIndex = matrices.NodeOrder.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> edgeIndex = matrices.HyperedgeOrder.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);

            matrices.SubdivisionConnectivity = ZeroMatrix(matrices.NodeOrder.Count, matrices.NodeOrder.Count);
            matrices.AdjacencyConnectivity = ZeroMatrix(matrices.NodeOrder.Count, matrices.NodeOrder.Count);
            matrices.Area = ZeroMatrix(matrices.NodeOrder.Count, matrices.NodeOrder.Count);
            matrices.Angle = ZeroMatrix(matrices.NodeOrder.Count, matrices.NodeOrder.Count);
            matrices.Incidence = ZeroMatrix(matrices.NodeOrder.Count, matrices.HyperedgeOrder.Count);

            Dictionary<string, HypergraphNode> nodes = hypergraph.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
            foreach (Hyperedge edge in hypergraph.Hyperedges)
            {
                if (edge.Kind == "subdivision")
                {
                    string parentId = edge.Members.Where(m => m.Role == "parent").Select(m => m.NodeId).FirstOrDefault();
                    foreach (HyperedgeMember child in edge.Members.Where(m => m.Role == "child"))
                    {
                        if (parentId != null && nodeIndex.ContainsKey(parentId) && nodeIndex.ContainsKey(child.NodeId))
                        {
                            int row = nodeIndex[parentId];
                            int column = nodeIndex[child.NodeId];
                            matrices.SubdivisionConnectivity[row][column] = 1.0;
                            HypergraphNode childNode;
                            if (nodes.TryGetValue(child.NodeId, out childNode))
                            {
                                matrices.Area[row][column] = childNode.Area;
                                matrices.Angle[row][column] = childNode.Angle == 0.0 ? Math.Round(Math.PI * 2.0, 6) : childNode.Angle;
                            }
                        }
                    }
                }

                if (IsAdjacencyMatrixKind(edge.Kind))
                {
                    List<string> memberIds = edge.Members.Select(m => m.NodeId).Where(id => nodeIndex.ContainsKey(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    for (int i = 0; i < memberIds.Count; i++)
                    {
                        for (int j = i + 1; j < memberIds.Count; j++)
                        {
                            int a = nodeIndex[memberIds[i]];
                            int b = nodeIndex[memberIds[j]];
                            matrices.AdjacencyConnectivity[a][b] = 1.0;
                            matrices.AdjacencyConnectivity[b][a] = 1.0;
                        }
                    }
                }
            }

            foreach (HypergraphIncidence incidence in hypergraph.Incidence)
            {
                if (nodeIndex.ContainsKey(incidence.NodeId) && edgeIndex.ContainsKey(incidence.HyperedgeId))
                {
                    matrices.Incidence[nodeIndex[incidence.NodeId]][edgeIndex[incidence.HyperedgeId]] = incidence.Weight == 0.0 ? 1.0 : incidence.Weight;
                }
            }

            return matrices;
        }

        private static bool IsAdjacencyMatrixKind(string kind)
        {
            return string.Equals(kind, "adjacency", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "circulation_access", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "door", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "facade", StringComparison.OrdinalIgnoreCase);
        }

        private static string HyperedgeKind(string simpleKind)
        {
            switch (simpleKind)
            {
                case "belongs_to":
                    return "containment";
                case "has_door":
                case "connects_to_corridor":
                    return "circulation_access";
                case "has_facade":
                    return "facade";
                case "constraint_requires_facade":
                    return "constraint";
                default:
                    return string.IsNullOrWhiteSpace(simpleKind) ? "relationship" : simpleKind;
            }
        }

        private static List<List<double>> ZeroMatrix(int rows, int columns)
        {
            List<List<double>> matrix = new List<List<double>>();
            for (int i = 0; i < rows; i++)
            {
                List<double> row = new List<double>();
                for (int j = 0; j < columns; j++)
                {
                    row.Add(0.0);
                }

                matrix.Add(row);
            }

            return matrix;
        }

        private List<string> RoomConnections(RoomLayout room, LayoutVariant variant)
        {
            List<string> connected = new List<string>();
            Polygon2 roomPolygon = ToPolygon(room.Polygon);
            foreach (RoomLayout other in variant.Rooms.Where(r => !string.Equals(r.Id, room.Id, StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.Equals(other.UnitId, room.UnitId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (GeometryPredicates.SharedBoundaryLength(roomPolygon, ToPolygon(other.Polygon), _tolerance) > _tolerance)
                {
                    connected.Add(other.Id);
                }
            }

            return connected.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string NormalizeRoomMergeId(string roomType)
        {
            string value = roomType ?? string.Empty;
            if (value.IndexOf("bath", StringComparison.OrdinalIgnoreCase) >= 0) return "bath";
            if (value.IndexOf("kitchen", StringComparison.OrdinalIgnoreCase) >= 0) return "kitchen";
            if (value.IndexOf("living", StringComparison.OrdinalIgnoreCase) >= 0) return "living";
            if (value.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0) return "bed";
            if (value.IndexOf("foyer", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("entry", StringComparison.OrdinalIgnoreCase) >= 0) return "foyer";
            if (value.IndexOf("extra", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("storage", StringComparison.OrdinalIgnoreCase) >= 0) return "extra";
            return string.IsNullOrWhiteSpace(value) ? "room" : value;
        }

        private static double RoomSplitAngle(RoomLayout room)
        {
            if (room == null || room.Bounds == null)
            {
                return 0.0;
            }

            return room.Bounds.Width >= room.Bounds.Height ? 0.0 : Math.PI * 0.5;
        }

        private static double CorridorAngle(LineInput line)
        {
            if (line == null || line.Start == null || line.End == null)
            {
                return 0.0;
            }

            double dx = line.End.X - line.Start.X;
            double dy = line.End.Y - line.Start.Y;
            double angle = Math.Atan2(dy, dx);
            return angle < 0.0 ? angle + Math.PI * 2.0 : angle;
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

        private static double PolygonArea(PolygonInput input)
        {
            return Math.Round(Math.Max(0.0, ToPolygon(input).Area()), 4);
        }

        private static Point2 PolygonCentroid(PolygonInput input)
        {
            Polygon2 polygon = ToPolygon(input);
            return polygon.Count == 0 ? new Point2() : polygon.Centroid();
        }

        private static double Distance(Point2 a, Point2 b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        private bool FixedElementConnectsToCorridor(Polygon2 corridorPolygon, CleanedFixedElement fixedElement)
        {
            if (GeometryPredicates.TouchesOrOverlaps(corridorPolygon, fixedElement.Polygon, _tolerance))
            {
                return true;
            }

            Bounds2 corridorBounds = corridorPolygon.Bounds();
            Bounds2 fixedBounds = fixedElement.Polygon.Bounds();
            bool xOverlap = Math.Min(corridorBounds.MaxX, fixedBounds.MaxX) - Math.Max(corridorBounds.MinX, fixedBounds.MinX) > _tolerance;
            bool yOverlap = Math.Min(corridorBounds.MaxY, fixedBounds.MaxY) - Math.Max(corridorBounds.MinY, fixedBounds.MinY) > _tolerance;
            bool horizontalFaceAdjacent = yOverlap &&
                (Math.Abs(corridorBounds.MaxX - fixedBounds.MinX) <= _tolerance ||
                 Math.Abs(fixedBounds.MaxX - corridorBounds.MinX) <= _tolerance);
            bool verticalFaceAdjacent = xOverlap &&
                (Math.Abs(corridorBounds.MaxY - fixedBounds.MinY) <= _tolerance ||
                 Math.Abs(fixedBounds.MaxY - corridorBounds.MinY) <= _tolerance);
            if (horizontalFaceAdjacent || verticalFaceAdjacent)
            {
                return true;
            }

            foreach (Point2 accessPoint in _input.Source.Access.VerticalCoreAccess)
            {
                if (GeometryPredicates.ContainsPoint(corridorPolygon, accessPoint, _tolerance, true) &&
                    GeometryPredicates.ContainsPoint(fixedElement.Polygon, accessPoint, _tolerance, true))
                {
                    return true;
                }
            }

            return false;
        }

        private static Polygon2 CorridorPolygon(CorridorStrategy corridor)
        {
            return Polygon2.Rectangle(corridor.Id, corridor.MinX, corridor.MinY, corridor.MaxX, corridor.MaxY);
        }

        private static LineInput CorridorCenterline(CorridorStrategy corridor)
        {
            if (corridor.Orientation == CorridorOrientation.Horizontal)
            {
                double y = (corridor.MinY + corridor.MaxY) * 0.5;
                return new LineInput
                {
                    Id = corridor.Id + "-centerline",
                    Start = new Point2(corridor.MinX, y),
                    End = new Point2(corridor.MaxX, y)
                };
            }

            double x = (corridor.MinX + corridor.MaxX) * 0.5;
            return new LineInput
            {
                Id = corridor.Id + "-centerline",
                Start = new Point2(x, corridor.MinY),
                End = new Point2(x, corridor.MaxY)
            };
        }

        private double MinAnyUnitWidth()
        {
            return Math.Max(4.0, _input.Source.Rules.MinRoomWidth + 1.2);
        }

        private double MinUnitWidth(string type)
        {
            if (type.IndexOf("two", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Math.Max(8.4, _input.Source.Rules.MinRoomWidth * 3.0);
            }

            if (type.IndexOf("one", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("1", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Math.Max(6.4, _input.Source.Rules.MinRoomWidth * 2.4);
            }

            return Math.Max(4.2, _input.Source.Rules.MinRoomWidth * 1.6);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
            {
                return min;
            }

            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int CombineSeed(int seed, int index)
        {
            unchecked
            {
                int hash = seed == 0 ? 17 : seed;
                hash = (hash * 397) ^ (index + 1);
                return hash;
            }
        }
    }
}

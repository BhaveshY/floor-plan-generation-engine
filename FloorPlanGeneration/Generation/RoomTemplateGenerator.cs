using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;

namespace FloorPlanGeneration.Generation
{
    internal sealed class RoomTemplateGenerator
    {
        // How hard owned-data proportion priors pull the facade-band split toward the
        // prior shares (architectural-finetuning Phase 3). Moderate: biases the
        // bedroom:living proportion without overriding the seeded/min-driven split.
        private const double PortfolioPriorStrength = 0.5;

        private readonly CleanedInput _input;
        private readonly double _tolerance;
        private Dictionary<string, double> _furnitureMinWidth;
        private Dictionary<string, double> _furnitureMaxAspect;
        private PortfolioPriors _priors;

        public RoomTemplateGenerator(CleanedInput input)
        {
            _input = input;
            _tolerance = input.Tolerance;
        }

        public void PopulateUnit(
            UnitLayout unit, CorridorStrategy corridor, UnitTypeTarget target,
            RoomStyle style, int unitIndex, List<Diagnostic> diagnostics)
        {
            if (style == null)
            {
                style = RoomStyle.Default();
            }

            if (diagnostics != null && !string.IsNullOrWhiteSpace(unit.Type) && !TryNormalizeUnitType(unit.Type, out _))
            {
                diagnostics.Add(Diagnostic.Warning(
                    "generation.unit_type_unrecognized",
                    "Unit type '" + unit.Type + "' is not a recognized layout type; rendering it as a one-bedroom unit.",
                    unit.Id));
            }

            Polygon2 unitPolygon = ToPolygon(unit.Polygon);
            Bounds2 bounds = unitPolygon.Bounds();
            bool mirror = style.MirrorUnit(unitIndex);
            if (corridor.Orientation == CorridorOrientation.Horizontal)
            {
                PopulateHorizontalUnit(unit, corridor, target, bounds, style, mirror);
            }
            else
            {
                PopulateVerticalUnit(unit, corridor, target, bounds, style, mirror);
            }

            unit.Rooms = unit.Rooms.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
            unit.Score = Math.Round(UnitRoomScore(unit, target), 4);
        }

        public DoorOpening CreateUnitDoor(UnitLayout unit, CorridorStrategy corridor, int index)
        {
            Polygon2 polygon = ToPolygon(unit.Polygon);
            Bounds2 bounds = polygon.Bounds();
            Point2 location;
            if (corridor.Orientation == CorridorOrientation.Horizontal)
            {
                double y = Math.Abs(bounds.MinY - corridor.MaxY) <= Math.Abs(bounds.MaxY - corridor.MinY)
                    ? bounds.MinY
                    : bounds.MaxY;
                location = new Point2((bounds.MinX + bounds.MaxX) * 0.5, y);
            }
            else
            {
                double x = Math.Abs(bounds.MinX - corridor.MaxX) <= Math.Abs(bounds.MaxX - corridor.MinX)
                    ? bounds.MinX
                    : bounds.MaxX;
                location = new Point2(x, (bounds.MinY + bounds.MaxY) * 0.5);
            }

            return new DoorOpening
            {
                Id = "door-" + unit.Id,
                Location = location,
                Width = _input.Source.Rules.DoorWidth,
                HostWall = EntryWallId(unit.Id, polygon, corridor),
                ConnectsSpaces = new List<string> { unit.Id, corridor.Id }
            };
        }

        public IEnumerable<WallLayout> CreateUnitWalls(UnitLayout unit, CorridorStrategy corridor)
        {
            Polygon2 polygon = ToPolygon(unit.Polygon);
            int entryEdgeIndex = EntryEdgeIndex(polygon, corridor);
            int index = 0;
            foreach (LineSegment2 edge in polygon.Edges())
            {
                index++;
                yield return new WallLayout
                {
                    Id = index == entryEdgeIndex ? "wall-entry-" + unit.Id : "wall-" + unit.Id + "-" + index.ToString(CultureInfo.InvariantCulture),
                    Centerline = new LineInput
                    {
                        Id = "wall-" + unit.Id + "-" + index.ToString(CultureInfo.InvariantCulture),
                        Start = edge.Start.Clone(),
                        End = edge.End.Clone()
                    },
                    Thickness = 0.18,
                    LayerType = "unit_demising"
                };
            }

            HashSet<string> emittedPartitions = new HashSet<string>(StringComparer.Ordinal);
            foreach (RoomLayout room in unit.Rooms)
            {
                Polygon2 roomPolygon = ToPolygon(room.Polygon);
                int roomEdgeIndex = 0;
                foreach (LineSegment2 edge in roomPolygon.Edges())
                {
                    roomEdgeIndex++;
                    if (SharesUnitExterior(edge, polygon))
                    {
                        continue;
                    }

                    if (!emittedPartitions.Add(SegmentKey(edge)))
                    {
                        continue;
                    }

                    yield return new WallLayout
                    {
                        Id = "wall-" + room.Id + "-" + roomEdgeIndex.ToString(CultureInfo.InvariantCulture),
                        Centerline = new LineInput
                        {
                            Id = "wall-" + room.Id + "-" + roomEdgeIndex.ToString(CultureInfo.InvariantCulture),
                            Start = edge.Start.Clone(),
                            End = edge.End.Clone()
                        },
                        Thickness = 0.10,
                        LayerType = "room_partition"
                    };
                }
            }
        }

        private string EntryWallId(string unitId, Polygon2 polygon, CorridorStrategy corridor)
        {
            int index = EntryEdgeIndex(polygon, corridor);
            return index > 0 ? "wall-entry-" + unitId : string.Empty;
        }

        private int EntryEdgeIndex(Polygon2 polygon, CorridorStrategy corridor)
        {
            Bounds2 bounds = polygon.Bounds();
            int index = 0;
            foreach (LineSegment2 edge in polygon.Edges())
            {
                index++;
                if (corridor.Orientation == CorridorOrientation.Horizontal)
                {
                    double entryY = Math.Abs(bounds.MinY - corridor.MaxY) <= Math.Abs(bounds.MaxY - corridor.MinY)
                        ? bounds.MinY
                        : bounds.MaxY;
                    if (edge.IsHorizontal(_tolerance) && Math.Abs(edge.Start.Y - entryY) <= _tolerance)
                    {
                        return index;
                    }
                }
                else
                {
                    double entryX = Math.Abs(bounds.MinX - corridor.MaxX) <= Math.Abs(bounds.MaxX - corridor.MinX)
                        ? bounds.MinX
                        : bounds.MaxX;
                    if (edge.IsVertical(_tolerance) && Math.Abs(edge.Start.X - entryX) <= _tolerance)
                    {
                        return index;
                    }
                }
            }

            return 1;
        }

        private bool SharesUnitExterior(LineSegment2 roomEdge, Polygon2 unitPolygon)
        {
            return unitPolygon.Edges().Any(unitEdge => GeometryPredicates.SharedSegmentLength(roomEdge, unitEdge, _tolerance) > _tolerance);
        }

        private static string SegmentKey(LineSegment2 edge)
        {
            string first = PointKey(edge.Start);
            string second = PointKey(edge.End);
            return string.CompareOrdinal(first, second) <= 0 ? first + "|" + second : second + "|" + first;
        }

        private static string PointKey(Point2 point)
        {
            return Math.Round(point.X, 6).ToString("0.######", CultureInfo.InvariantCulture) + "," +
                Math.Round(point.Y, 6).ToString("0.######", CultureInfo.InvariantCulture);
        }

        public IEnumerable<LabelLayout> CreateLabels(UnitLayout unit)
        {
            Polygon2 unitPolygon = ToPolygon(unit.Polygon);
            yield return new LabelLayout
            {
                Id = "label-" + unit.Id,
                TargetId = unit.Id,
                Text = unit.Id + " " + unit.Type + " " + Math.Round(unit.Area, 1).ToString(CultureInfo.InvariantCulture) + " m2",
                Location = unitPolygon.Centroid(),
                Layer = "FP::Generated::Labels"
            };

            foreach (RoomLayout room in unit.Rooms)
            {
                Polygon2 roomPolygon = ToPolygon(room.Polygon);
                yield return new LabelLayout
                {
                    Id = "label-" + room.Id,
                    TargetId = room.Id,
                    Text = room.RoomType + " " + Math.Round(room.Area, 1).ToString(CultureInfo.InvariantCulture) + " m2",
                    Location = roomPolygon.Centroid(),
                    Layer = "FP::Generated::Labels"
                };
            }
        }

        private void PopulateHorizontalUnit(UnitLayout unit, CorridorStrategy corridor, UnitTypeTarget target, Bounds2 b, RoomStyle style, bool mirror)
        {
            double width = b.Width;
            double depth = b.Height;
            double wetDepth = WetDepth(depth, style.WetFraction);
            bool aboveCorridor = b.MinY >= corridor.MaxY - _tolerance;
            double wetMinY = aboveCorridor ? b.MinY : b.MaxY - wetDepth;
            double wetMaxY = aboveCorridor ? b.MinY + wetDepth : b.MaxY;
            double facadeMinY = aboveCorridor ? wetMaxY : b.MinY;
            double facadeMaxY = aboveCorridor ? b.MaxY : wetMinY;

            string type = NormalizeUnitType(unit.Type);
            if (type == "studio")
            {
                double bathWidth = Clamp(width * style.StudioBathFraction, _input.Source.Rules.MinRoomWidth, Math.Min(3.4, width * 0.55));
                AddRoomX(unit, "bathroom", b, mirror, b.MinX, wetMinY, b.MinX + bathWidth, wetMaxY, false);
                AddRoomX(unit, "kitchen", b, mirror, b.MinX + bathWidth, wetMinY, b.MaxX, wetMaxY, false);
                AddRoomX(unit, "living_sleeping", b, mirror, b.MinX, facadeMinY, b.MaxX, facadeMaxY, true);
                return;
            }

            if (type == "two_bed")
            {
                double bedWidth = Math.Max(_input.Source.Rules.MinRoomWidth, width * style.TwoBedFraction);
                double livingStart = b.MinX + (bedWidth * 2.0);
                if (b.MaxX - livingStart < _input.Source.Rules.MinRoomWidth)
                {
                    bedWidth = width / 3.0;
                    livingStart = b.MinX + (bedWidth * 2.0);
                }

                double[] fb = GrowFacadeBand(
                    "two_bed",
                    new[] { "bedroom", "bedroom", "living" },
                    new[] { bedWidth, bedWidth, b.MaxX - livingStart },
                    _input.Source.Rules.MinRoomWidth, facadeMaxY - facadeMinY);
                double bed1End = b.MinX + fb[0];
                // Off-path reuses the historic single-expression boundary so opted-out
                // plans stay bit-for-bit identical (fb[0]+fb[1] reassociates the same
                // doubles and drifts ~1 ULP, which the byte-identity contract forbids).
                bool twoBedFacadeAdjusted = _input.Source.Rules.ApplyFurnitureMinimums || _input.Source.Rules.UsePortfolioPriors;
                double bed2End = twoBedFacadeAdjusted ? bed1End + fb[1] : livingStart;

                double wetSplit = Math.Min(3.2, width * style.WetSplitFraction);
                AddRoomX(unit, "bathroom", b, mirror, b.MinX, wetMinY, b.MinX + wetSplit, wetMaxY, false);
                AddRoomX(unit, "kitchen", b, mirror, b.MinX + wetSplit, wetMinY, b.MaxX, wetMaxY, false);
                AddRoomX(unit, "bedroom", b, mirror, b.MinX, facadeMinY, bed1End, facadeMaxY, true);
                AddRoomX(unit, "bedroom", b, mirror, bed1End, facadeMinY, bed2End, facadeMaxY, true);
                AddRoomX(unit, "living", b, mirror, bed2End, facadeMinY, b.MaxX, facadeMaxY, true);
                return;
            }

            double bedroomWidth = Clamp(
                width * style.OneBedFraction,
                _input.Source.Rules.MinRoomWidth + 0.4,
                Math.Max(_input.Source.Rules.MinRoomWidth, width - _input.Source.Rules.MinRoomWidth));
            double[] oneBedFb = GrowFacadeBand(
                "one_bed",
                new[] { "bedroom", "living" },
                new[] { bedroomWidth, b.MaxX - (b.MinX + bedroomWidth) },
                _input.Source.Rules.MinRoomWidth, facadeMaxY - facadeMinY);
            double oneBedEnd = b.MinX + oneBedFb[0];

            double oneBedWetSplit = Math.Min(3.2, width * (style.WetSplitFraction + 0.06));
            AddRoomX(unit, "bathroom", b, mirror, b.MinX, wetMinY, b.MinX + oneBedWetSplit, wetMaxY, false);
            AddRoomX(unit, "kitchen", b, mirror, b.MinX + oneBedWetSplit, wetMinY, b.MaxX, wetMaxY, false);
            AddRoomX(unit, "bedroom", b, mirror, b.MinX, facadeMinY, oneBedEnd, facadeMaxY, true);
            AddRoomX(unit, "living", b, mirror, oneBedEnd, facadeMinY, b.MaxX, facadeMaxY, true);
        }

        private void PopulateVerticalUnit(UnitLayout unit, CorridorStrategy corridor, UnitTypeTarget target, Bounds2 b, RoomStyle style, bool mirror)
        {
            double width = b.Width;
            double depth = b.Height;
            double wetDepth = WetDepth(width, style.WetFraction);
            bool rightOfCorridor = b.MinX >= corridor.MaxX - _tolerance;
            double wetMinX = rightOfCorridor ? b.MinX : b.MaxX - wetDepth;
            double wetMaxX = rightOfCorridor ? b.MinX + wetDepth : b.MaxX;
            double facadeMinX = rightOfCorridor ? wetMaxX : b.MinX;
            double facadeMaxX = rightOfCorridor ? b.MaxX : wetMinX;

            string type = NormalizeUnitType(unit.Type);
            if (type == "studio")
            {
                double bathDepth = Clamp(depth * style.StudioBathFraction, _input.Source.Rules.MinRoomDepth, Math.Min(3.4, depth * 0.55));
                AddRoomY(unit, "bathroom", b, mirror, wetMinX, b.MinY, wetMaxX, b.MinY + bathDepth, false);
                AddRoomY(unit, "kitchen", b, mirror, wetMinX, b.MinY + bathDepth, wetMaxX, b.MaxY, false);
                AddRoomY(unit, "living_sleeping", b, mirror, facadeMinX, b.MinY, facadeMaxX, b.MaxY, true);
                return;
            }

            if (type == "two_bed")
            {
                double bedDepth = Math.Max(_input.Source.Rules.MinRoomDepth, depth * style.TwoBedFraction);
                double livingStart = b.MinY + (bedDepth * 2.0);
                if (b.MaxY - livingStart < _input.Source.Rules.MinRoomDepth)
                {
                    bedDepth = depth / 3.0;
                    livingStart = b.MinY + (bedDepth * 2.0);
                }

                double[] fb = GrowFacadeBand(
                    "two_bed",
                    new[] { "bedroom", "bedroom", "living" },
                    new[] { bedDepth, bedDepth, b.MaxY - livingStart },
                    _input.Source.Rules.MinRoomDepth, facadeMaxX - facadeMinX);
                double bed1End = b.MinY + fb[0];
                // Off-path reuses the historic single-expression boundary so opted-out
                // plans stay bit-for-bit identical (see PopulateHorizontalUnit).
                bool twoBedFacadeAdjusted = _input.Source.Rules.ApplyFurnitureMinimums || _input.Source.Rules.UsePortfolioPriors;
                double bed2End = twoBedFacadeAdjusted ? bed1End + fb[1] : livingStart;

                double wetSplit = Math.Min(3.2, depth * style.WetSplitFraction);
                AddRoomY(unit, "bathroom", b, mirror, wetMinX, b.MinY, wetMaxX, b.MinY + wetSplit, false);
                AddRoomY(unit, "kitchen", b, mirror, wetMinX, b.MinY + wetSplit, wetMaxX, b.MaxY, false);
                AddRoomY(unit, "bedroom", b, mirror, facadeMinX, b.MinY, facadeMaxX, bed1End, true);
                AddRoomY(unit, "bedroom", b, mirror, facadeMinX, bed1End, facadeMaxX, bed2End, true);
                AddRoomY(unit, "living", b, mirror, facadeMinX, bed2End, facadeMaxX, b.MaxY, true);
                return;
            }

            double bedroomDepth = Clamp(
                depth * style.OneBedFraction,
                _input.Source.Rules.MinRoomDepth + 0.4,
                Math.Max(_input.Source.Rules.MinRoomDepth, depth - _input.Source.Rules.MinRoomDepth));
            double[] oneBedFb = GrowFacadeBand(
                "one_bed",
                new[] { "bedroom", "living" },
                new[] { bedroomDepth, b.MaxY - (b.MinY + bedroomDepth) },
                _input.Source.Rules.MinRoomDepth, facadeMaxX - facadeMinX);
            double oneBedEnd = b.MinY + oneBedFb[0];

            double oneBedWetSplit = Math.Min(3.2, depth * (style.WetSplitFraction + 0.06));
            AddRoomY(unit, "bathroom", b, mirror, wetMinX, b.MinY, wetMaxX, b.MinY + oneBedWetSplit, false);
            AddRoomY(unit, "kitchen", b, mirror, wetMinX, b.MinY + oneBedWetSplit, wetMaxX, b.MaxY, false);
            AddRoomY(unit, "bedroom", b, mirror, facadeMinX, b.MinY, facadeMaxX, oneBedEnd, true);
            AddRoomY(unit, "living", b, mirror, facadeMinX, oneBedEnd, facadeMaxX, b.MaxY, true);
        }

        private void AddRoomX(
            UnitLayout unit, string roomType, Bounds2 b, bool mirror,
            double minX, double minY, double maxX, double maxY, bool expectsDaylight)
        {
            if (mirror)
            {
                double mirroredMin = b.MinX + (b.MaxX - maxX);
                double mirroredMax = b.MaxX - (minX - b.MinX);
                minX = mirroredMin;
                maxX = mirroredMax;
            }

            AddRoom(unit, roomType, b, minX, minY, maxX, maxY, expectsDaylight);
        }

        private void AddRoomY(
            UnitLayout unit, string roomType, Bounds2 b, bool mirror,
            double minX, double minY, double maxX, double maxY, bool expectsDaylight)
        {
            if (mirror)
            {
                double mirroredMin = b.MinY + (b.MaxY - maxY);
                double mirroredMax = b.MaxY - (minY - b.MinY);
                minY = mirroredMin;
                maxY = mirroredMax;
            }

            AddRoom(unit, roomType, b, minX, minY, maxX, maxY, expectsDaylight);
        }

        // Snaps a room's interior partitions onto the planning grid before placing
        // it, so wet/day splits and bedroom columns line up across the plan. The
        // unit's own envelope edges (which abut the corridor or floorplate) are left
        // exactly where they are, so the unit keeps tiling watertight against its
        // neighbours; adjacent rooms share each interior coordinate, so both snap
        // identically and no gap opens. A module of 0 is a no-op (gridless layouts
        // stay byte-identical).
        private void AddRoom(UnitLayout unit, string roomType, Bounds2 b, double minX, double minY, double maxX, double maxY, bool expectsDaylight)
        {
            double module = _input.Source.Rules.GridModule;
            double minRoomW = _input.Source.Rules.MinRoomWidth;
            double minRoomD = _input.Source.Rules.MinRoomDepth;
            minX = SnapInterior(minX, b.MinX, b.MaxX, minRoomW, module);
            maxX = SnapInterior(maxX, b.MinX, b.MaxX, minRoomW, module);
            minY = SnapInterior(minY, b.MinY, b.MaxY, minRoomD, module);
            maxY = SnapInterior(maxY, b.MinY, b.MaxY, minRoomD, module);
            AddRoom(unit, roomType, minX, minY, maxX, maxY, expectsDaylight);
        }

        // Best-effort proportions a facade band's partitions toward each room's
        // furniture span (German Neufert / DIN 18040-2): grows every room up to its
        // minimum width, then caps any room whose width would exceed maxAspect x the
        // band depth (crossSpan) so it does not degenerate into an over-long slot.
        // Slack only moves between neighbours that stay within their own bounds so the
        // band still spans its facade exactly. A no-op unless ApplyFurnitureMinimums is
        // set (so opted-out plans stay byte-identical). structuralMin keeps every donor
        // — and any type without a furniture rule — at or above the unit's own minimum
        // room dimension; the aspect cap is never allowed below that floor. Only the
        // facade band (bedrooms + living) is ever passed here; the wet rooms
        // (bathroom/kitchen) are placed separately and are never members of this array,
        // so they are left exactly as constructed. Runs before SnapInterior.
        private double[] GrowFacadeBand(string unitType, IReadOnlyList<string> types, double[] widths, double structuralMin, double crossSpan)
        {
            bool furniture = _input.Source.Rules.ApplyFurnitureMinimums;
            bool priors = _input.Source.Rules.UsePortfolioPriors;
            if (!furniture && !priors)
            {
                return widths;
            }

            if (furniture && _furnitureMinWidth == null)
            {
                _furnitureMinWidth = FurnitureDefaults.MinWidthByType(_input.Source.Program);
                _furnitureMaxAspect = FurnitureDefaults.MaxAspectByType(_input.Source.Program);
            }

            double[] mins = new double[widths.Length];
            double[] maxs = new double[widths.Length];
            for (int i = 0; i < widths.Length; i++)
            {
                double furnitureMin = furniture && _furnitureMinWidth.TryGetValue(types[i], out double m) ? m : 0.0;
                mins[i] = Math.Max(structuralMin, furnitureMin);
                double aspect = furniture && _furnitureMaxAspect.TryGetValue(types[i], out double a) ? a : 0.0;
                // Cap never drops below the floor: the firmer minimum wins over the
                // softer aspect preference, keeping the [min, max] box non-empty.
                maxs[i] = aspect > 0.0 && crossSpan > 0.0 ? Math.Max(mins[i], aspect * crossSpan) : 0.0;
            }

            double[] result = furniture ? RoomProportions.ConstrainToBounds(widths, mins, maxs) : widths;
            if (priors)
            {
                double[] shares = FacadeShareWeights(unitType, types);
                if (shares != null)
                {
                    result = RoomProportions.PullToTargets(result, shares, mins, maxs, PortfolioPriorStrength);
                }
            }

            return result;
        }

        // The prior's facade shares are ordered to match a unit type's facade rooms; this
        // returns them aligned to the live room sequence, or null when the prior has no
        // entry or the sequence has drifted from the prior (then the pull is skipped).
        private double[] FacadeShareWeights(string unitType, IReadOnlyList<string> types)
        {
            if (_priors == null)
            {
                _priors = PortfolioPriors.Default();
            }

            IReadOnlyList<FacadeShare> shares = _priors.FacadeShares(unitType);
            if (shares.Count != types.Count)
            {
                return null;
            }

            double[] weights = new double[types.Count];
            for (int i = 0; i < types.Count; i++)
            {
                if (!string.Equals(shares[i].RoomType, types[i], StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                weights[i] = shares[i].Share;
            }

            return weights;
        }

        private double SnapInterior(double value, double envLo, double envHi, double minSpan, double module)
        {
            if (Math.Abs(value - envLo) <= _tolerance || Math.Abs(value - envHi) <= _tolerance)
            {
                return value;
            }

            return Grid.SnapWithin(value, envLo + minSpan, envHi - minSpan, 0.0, module);
        }

        private void AddRoom(UnitLayout unit, string roomType, double minX, double minY, double maxX, double maxY, bool expectsDaylight)
        {
            Polygon2 polygon = Polygon2.Rectangle(
                unit.Id + "-" + roomType + "-" + (unit.Rooms.Count + 1).ToString(CultureInfo.InvariantCulture),
                minX,
                minY,
                maxX,
                maxY);
            bool daylight = expectsDaylight && FacadeAnalyzer.HasDaylightExposure(polygon, _input.Floorplate, _input.Source.Facade, _tolerance);
            Bounds2 bounds = polygon.Bounds();
            RoomLayout room = new RoomLayout
            {
                Id = polygon.SourceId,
                UnitId = unit.Id,
                RoomType = roomType,
                Polygon = GeometryCleaner.ToPolygonInput(polygon),
                Area = Math.Round(polygon.Area(), 4),
                Dimensions = new SpaceDimensions { Width = Math.Round(bounds.Width, 4), Depth = Math.Round(bounds.Height, 4) },
                Daylight = daylight
            };
            unit.Rooms.Add(room);
        }

        private double WetDepth(double totalDepth, double fraction)
        {
            double minDepth = Math.Max(2.2, _input.Source.Rules.MinRoomDepth);
            double wetDepth = Math.Min(3.2, Math.Max(minDepth, totalDepth * fraction));
            if (totalDepth - wetDepth < _input.Source.Rules.MinRoomDepth)
            {
                wetDepth = Math.Max(1.8, totalDepth - _input.Source.Rules.MinRoomDepth);
            }

            return wetDepth;
        }

        private static double UnitRoomScore(UnitLayout unit, UnitTypeTarget target)
        {
            if (unit.Area <= 0.0)
            {
                return 0.0;
            }

            double desired = (target.MinArea + target.MaxArea) * 0.5;
            double areaFit = 1.0 - Math.Min(1.0, Math.Abs(unit.Area - desired) / Math.Max(desired, 1.0));
            double roomCoverage = Math.Min(1.0, unit.Rooms.Sum(r => r.Area) / unit.Area);
            return Math.Max(0.0, Math.Min(1.0, (areaFit * 0.45) + (roomCoverage * 0.55)));
        }

        private static string NormalizeUnitType(string type)
        {
            TryNormalizeUnitType(type, out string normalized);
            return normalized;
        }

        // Maps the unit-type aliases the planner/brief may use onto the canonical strings
        // the layout branches switch on. Returns false (with the one_bed fallback) for any
        // unrecognised type so the caller can surface a warning instead of silently
        // mis-rendering it -- this silent fallthrough was the root cause of the historic
        // two_bed -> one_bed bug.
        private static bool TryNormalizeUnitType(string type, out string normalized)
        {
            if (string.Equals(type, "two_bed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "2-bed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "2_bed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "two-bedroom", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "two_bed";
                return true;
            }

            if (string.Equals(type, "one_bed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "1-bed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "1_bed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "one-bedroom", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "one_bed";
                return true;
            }

            if (string.Equals(type, "studio", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "studio";
                return true;
            }

            normalized = "one_bed";
            return false;
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

        private static Polygon2 ToPolygon(PolygonInput input)
        {
            List<Point2> points = input.Points.Select(p => p.Clone()).ToList();
            if (points.Count > 1 && points[0].EqualsWithin(points[points.Count - 1], 1e-9))
            {
                points.RemoveAt(points.Count - 1);
            }

            return new Polygon2(input.Id, points);
        }
    }
}

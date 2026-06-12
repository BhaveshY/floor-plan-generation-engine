using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;

namespace FloorPlanGeneration.Generation
{
    /// <summary>
    /// Generates a complete single apartment ("single_dwelling" layout mode):
    /// the floorplate is one unit, rooms partition it directly — a wet cluster
    /// (bath / foyer / kitchen) along the seeded entry facade and daylight rooms
    /// (living, bedrooms) on the opposite band. No corridor, no core, no bands.
    /// All geometry is produced in an entry-at-bottom frame and mapped to the
    /// chosen entry side, so one layout path serves all four orientations.
    /// </summary>
    internal sealed class DwellingTemplateGenerator
    {
        private readonly CleanedInput _input;
        private readonly double _tolerance;

        private sealed class FrameRect
        {
            public string RoomType;
            public double X0;
            public double Y0;
            public double X1;
            public double Y1;
            public bool ExpectsDaylight;
        }

        public DwellingTemplateGenerator(CleanedInput input)
        {
            _input = input;
            _tolerance = input.Tolerance;
        }

        public void Populate(
            LayoutVariant variant,
            UnitMixPlanner mixPlanner,
            RoomStyle style,
            SeededRandom random,
            int variantIndex,
            List<Diagnostic> diagnostics)
        {
            Bounds2 bounds = _input.Floorplate.Bounds();
            string type = DwellingType(mixPlanner);
            UnitTypeTarget target = mixPlanner.FindTarget(type);

            string[] sides = new[] { "south", "east", "north", "west" };
            string entrySide = sides[(variantIndex + random.Next(0, sides.Length)) % sides.Length];
            bool vertical = entrySide == "east" || entrySide == "west";
            double frameWidth = vertical ? bounds.Height : bounds.Width;
            double frameDepth = vertical ? bounds.Width : bounds.Height;

            UnitLayout unit = new UnitLayout
            {
                Id = "unit-dwelling-01",
                Type = type,
                Polygon = GeometryCleaner.ToPolygonInput(
                    Polygon2.Rectangle("unit-dwelling-01", bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY)),
                Area = Math.Round(bounds.Width * bounds.Height, 4),
                FacadeLength = Math.Round(2.0 * (bounds.Width + bounds.Height), 4)
            };
            variant.Units.Add(unit);

            List<FrameRect> rooms = LayoutRooms(frameWidth, frameDepth, type, style, diagnostics);
            foreach (FrameRect room in rooms)
            {
                Bounds2 world = MapRect(room, bounds, entrySide);
                AddRoom(unit, room.RoomType, world, room.ExpectsDaylight);
            }

            // The entry door opens into the room that actually sits on the entry
            // facade (foyer when present, else the kitchen — never the bathroom);
            // interior circulation hangs off the foyer or, failing that, living.
            string entryRoomId = RoomIdByType(unit, "foyer") ?? RoomIdByType(unit, "kitchen") ?? FirstRoomId(unit);
            string hubRoomId = RoomIdByType(unit, "foyer") ?? RoomIdContaining(unit, "living") ?? entryRoomId;

            unit.Rooms = unit.Rooms.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
            unit.Score = Math.Round(DwellingScore(unit, target), 4);

            Dictionary<string, string> wallBySegment = new Dictionary<string, string>(StringComparer.Ordinal);
            variant.Walls.AddRange(CreateWalls(unit, bounds, entrySide, wallBySegment));
            variant.DoorsOpenings.AddRange(CreateDoors(unit, bounds, entrySide, entryRoomId, hubRoomId, wallBySegment));
            variant.Labels.AddRange(CreateLabels(unit));
            variant.Rooms.AddRange(unit.Rooms);
        }

        /// <summary>
        /// Frame layout (entry along y = 0): a wet band with bath / foyer / kitchen
        /// at the entry, then a daylight band split into living and bedrooms with
        /// the living central so every bedroom can open onto it.
        /// </summary>
        private List<FrameRect> LayoutRooms(double width, double depth, string type, RoomStyle style, List<Diagnostic> diagnostics)
        {
            double minW = Math.Max(1.2, _input.Source.Rules.MinRoomWidth);
            double minD = Math.Max(1.2, _input.Source.Rules.MinRoomDepth);
            int bedrooms = BedroomsFor(type);
            int fittable = Math.Max(0, (int)Math.Floor((width + _tolerance) / minW) - 1);
            if (bedrooms > fittable)
            {
                diagnostics.Add(Diagnostic.Warning(
                    "dwelling.bedrooms_reduced",
                    "Plate width cannot host every bedroom beside the living room; reduced the bedroom count to fit.",
                    "floorplate"));
                bedrooms = fittable;
            }

            double wetDepth = Clamp(depth * style.WetFraction, Math.Max(1.8, minD * 0.75), Math.Min(3.2, depth - minD));
            List<FrameRect> rooms = new List<FrameRect>();

            // Wet band: [bath][foyer][kitchen], the foyer dropped on tight plates.
            double bathWidth = Clamp(width * style.WetSplitFraction, Math.Min(minW, 1.8), Math.Max(Math.Min(minW, 1.8), width * 0.40));
            double foyerWidth = Clamp(width * 0.24, 1.2, 2.8);
            bool hasFoyer = width - bathWidth - foyerWidth >= Math.Min(minW, 1.8) && BedroomsFor(type) > 0;
            if (!hasFoyer)
            {
                foyerWidth = 0.0;
            }

            double cursor = 0.0;
            rooms.Add(WetRoom("bathroom", ref cursor, bathWidth, wetDepth));
            if (hasFoyer)
            {
                rooms.Add(WetRoom("foyer", ref cursor, foyerWidth, wetDepth));
            }

            rooms.Add(WetRoom("kitchen", ref cursor, width - cursor, wetDepth));

            // Daylight band: living central, bedrooms flanking it.
            string livingType = bedrooms == 0 ? "living_sleeping" : "living";
            if (bedrooms == 0)
            {
                rooms.Add(DayRoom(livingType, 0.0, width, wetDepth, depth));
            }
            else
            {
                double bedShare = Clamp((1.0 - style.OneBedFraction) / bedrooms, 0.0, 1.0);
                double[] widths = new double[bedrooms + 1];
                for (int i = 0; i < bedrooms; i++)
                {
                    widths[i] = Math.Max(minW, width * bedShare);
                }

                double living = width - widths.Take(bedrooms).Sum();
                if (living < minW)
                {
                    double deficit = minW - living;
                    for (int i = 0; i < bedrooms; i++)
                    {
                        widths[i] = Math.Max(minW, widths[i] - (deficit / bedrooms));
                    }

                    living = Math.Max(minW, width - widths.Take(bedrooms).Sum());
                }

                widths[bedrooms] = living;

                // Order with living central: bed1 | living | bed2 | bed3...
                List<string> order = new List<string>();
                List<double> orderedWidths = new List<double>();
                order.Add("bedroom");
                orderedWidths.Add(widths[0]);
                order.Add(livingType);
                orderedWidths.Add(widths[bedrooms]);
                for (int i = 1; i < bedrooms; i++)
                {
                    order.Add("bedroom");
                    orderedWidths.Add(widths[i]);
                }

                if (style.MirrorEvenUnits)
                {
                    order.Reverse();
                    orderedWidths.Reverse();
                }

                // Absorb rounding drift into the widest room so the band tiles exactly.
                double drift = width - orderedWidths.Sum();
                int widest = orderedWidths.IndexOf(orderedWidths.Max());
                orderedWidths[widest] += drift;

                double x = 0.0;
                for (int i = 0; i < order.Count; i++)
                {
                    rooms.Add(DayRoom(order[i], x, x + orderedWidths[i], wetDepth, depth));
                    x += orderedWidths[i];
                }
            }

            return rooms;
        }

        private static FrameRect WetRoom(string type, ref double cursor, double width, double wetDepth)
        {
            FrameRect room = new FrameRect
            {
                RoomType = type,
                X0 = cursor,
                Y0 = 0.0,
                X1 = cursor + width,
                Y1 = wetDepth,
                ExpectsDaylight = false
            };
            cursor += width;
            return room;
        }

        private static FrameRect DayRoom(string type, double x0, double x1, double wetDepth, double depth)
        {
            return new FrameRect
            {
                RoomType = type,
                X0 = x0,
                Y0 = wetDepth,
                X1 = x1,
                Y1 = depth,
                ExpectsDaylight = true
            };
        }

        private static Bounds2 MapRect(FrameRect rect, Bounds2 bounds, string entrySide)
        {
            switch (entrySide)
            {
                case "north":
                    return NewBounds(bounds.MinX + rect.X0, bounds.MaxY - rect.Y1, bounds.MinX + rect.X1, bounds.MaxY - rect.Y0);
                case "west":
                    return NewBounds(bounds.MinX + rect.Y0, bounds.MinY + rect.X0, bounds.MinX + rect.Y1, bounds.MinY + rect.X1);
                case "east":
                    return NewBounds(bounds.MaxX - rect.Y1, bounds.MinY + rect.X0, bounds.MaxX - rect.Y0, bounds.MinY + rect.X1);
                default:
                    return NewBounds(bounds.MinX + rect.X0, bounds.MinY + rect.Y0, bounds.MinX + rect.X1, bounds.MinY + rect.Y1);
            }
        }

        private static Bounds2 NewBounds(double minX, double minY, double maxX, double maxY)
        {
            // No rounding: adjacent rooms share the exact same boundary doubles,
            // so the plan is watertight for the editor's boundary-move math.
            return new Bounds2(minX, minY, maxX, maxY);
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

        private void AddRoom(UnitLayout unit, string roomType, Bounds2 world, bool expectsDaylight)
        {
            Polygon2 polygon = Polygon2.Rectangle(
                unit.Id + "-" + roomType + "-" + (unit.Rooms.Count + 1).ToString(CultureInfo.InvariantCulture),
                world.MinX,
                world.MinY,
                world.MaxX,
                world.MaxY);
            bool daylight = expectsDaylight &&
                FacadeAnalyzer.HasDaylightExposure(polygon, _input.Floorplate, _input.Source.Facade, _tolerance);
            unit.Rooms.Add(new RoomLayout
            {
                Id = polygon.SourceId,
                UnitId = unit.Id,
                RoomType = roomType,
                Polygon = GeometryCleaner.ToPolygonInput(polygon),
                Area = Math.Round(polygon.Area(), 4),
                Dimensions = new SpaceDimensions
                {
                    Width = Math.Round(world.Width, 4),
                    Depth = Math.Round(world.Height, 4)
                },
                Daylight = daylight
            });
        }

        private IEnumerable<WallLayout> CreateWalls(UnitLayout unit, Bounds2 bounds, string entrySide, Dictionary<string, string> wallBySegment)
        {
            Polygon2 unitPolygon = ToPolygon(unit.Polygon);
            int entryEdgeIndex = EntryEdgeIndex(unitPolygon, bounds, entrySide);
            int index = 0;
            List<WallLayout> walls = new List<WallLayout>();
            foreach (LineSegment2 edge in unitPolygon.Edges())
            {
                index++;
                string id = index == entryEdgeIndex
                    ? "wall-entry-" + unit.Id
                    : "wall-" + unit.Id + "-" + index.ToString(CultureInfo.InvariantCulture);
                walls.Add(new WallLayout
                {
                    Id = id,
                    Centerline = new LineInput
                    {
                        Id = "wall-" + unit.Id + "-" + index.ToString(CultureInfo.InvariantCulture),
                        Start = edge.Start.Clone(),
                        End = edge.End.Clone()
                    },
                    Thickness = 0.18,
                    LayerType = "unit_demising"
                });
                wallBySegment[SegmentKey(edge)] = id;
            }

            foreach (RoomLayout room in unit.Rooms)
            {
                Polygon2 roomPolygon = ToPolygon(room.Polygon);
                int roomEdgeIndex = 0;
                foreach (LineSegment2 edge in roomPolygon.Edges())
                {
                    roomEdgeIndex++;
                    if (unitPolygon.Edges().Any(unitEdge => GeometryPredicates.SharedSegmentLength(edge, unitEdge, _tolerance) > _tolerance))
                    {
                        continue;
                    }

                    string key = SegmentKey(edge);
                    if (wallBySegment.ContainsKey(key))
                    {
                        continue;
                    }

                    string id = "wall-" + room.Id + "-" + roomEdgeIndex.ToString(CultureInfo.InvariantCulture);
                    walls.Add(new WallLayout
                    {
                        Id = id,
                        Centerline = new LineInput
                        {
                            Id = id,
                            Start = edge.Start.Clone(),
                            End = edge.End.Clone()
                        },
                        Thickness = 0.10,
                        LayerType = "room_partition"
                    });
                    wallBySegment[key] = id;
                }
            }

            return walls;
        }

        private IEnumerable<DoorOpening> CreateDoors(
            UnitLayout unit,
            Bounds2 bounds,
            string entrySide,
            string entryRoomId,
            string hubRoomId,
            Dictionary<string, string> wallBySegment)
        {
            List<DoorOpening> doors = new List<DoorOpening>();
            double doorWidth = _input.Source.Rules.DoorWidth;
            RoomLayout entryRoom = unit.Rooms.FirstOrDefault(r => string.Equals(r.Id, entryRoomId, StringComparison.OrdinalIgnoreCase))
                ?? unit.Rooms.FirstOrDefault();
            RoomLayout hub = unit.Rooms.FirstOrDefault(r => string.Equals(r.Id, hubRoomId, StringComparison.OrdinalIgnoreCase))
                ?? entryRoom;
            if (entryRoom == null)
            {
                return doors;
            }

            // Entry door: centered on the entry room's stretch of the entry facade.
            Bounds2 entryBounds = ToPolygon(entryRoom.Polygon).Bounds();
            Point2 entryLocation = EntryDoorLocation(entryBounds, bounds, entrySide);
            doors.Add(new DoorOpening
            {
                Id = "door-" + unit.Id,
                Location = entryLocation,
                Width = doorWidth,
                HostWall = "wall-entry-" + unit.Id,
                ConnectsSpaces = new List<string> { unit.Id, entryRoom.Id }
            });

            // Interior doors: every room connects to the hub when they share a
            // wall; rooms without hub contact fall back to any already-connected
            // neighbour so the plan is always fully circulable.
            HashSet<string> connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entryRoom.Id };
            List<RoomLayout> pending = unit.Rooms.Where(r => !connected.Contains(r.Id)).ToList();
            int doorIndex = 0;
            int safety = 0;
            while (pending.Count > 0 && safety++ < 24)
            {
                bool progressed = false;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    RoomLayout room = pending[i];
                    RoomLayout host = unit.Rooms
                        .Where(other => connected.Contains(other.Id))
                        .OrderByDescending(other => string.Equals(other.Id, hub.Id, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                        .FirstOrDefault(other => SharedSpan(room, other, out _, out _) > Math.Max(doorWidth, 0.7));
                    if (host == null)
                    {
                        continue;
                    }

                    Point2 location;
                    string hostWall = SharedWallId(room, host, wallBySegment, out location);
                    if (hostWall == null)
                    {
                        continue;
                    }

                    doorIndex++;
                    doors.Add(new DoorOpening
                    {
                        Id = "door-" + unit.Id + "-room-" + doorIndex.ToString("00", CultureInfo.InvariantCulture),
                        Location = location,
                        Width = Math.Min(doorWidth, 0.9),
                        HostWall = hostWall,
                        ConnectsSpaces = new List<string> { host.Id, room.Id }
                    });
                    connected.Add(room.Id);
                    pending.RemoveAt(i);
                    progressed = true;
                }

                if (!progressed)
                {
                    break;
                }
            }

            return doors;
        }

        /// <summary>
        /// Length of the shared axis-aligned boundary between two rectangular
        /// rooms, plus the line it sits on (constant axis value and span).
        /// </summary>
        private double SharedSpan(RoomLayout a, RoomLayout b, out bool verticalWall, out double[] line)
        {
            Bounds2 ba = ToPolygon(a.Polygon).Bounds();
            Bounds2 bb = ToPolygon(b.Polygon).Bounds();
            verticalWall = false;
            line = null;

            double xOverlap = Math.Min(ba.MaxX, bb.MaxX) - Math.Max(ba.MinX, bb.MinX);
            double yOverlap = Math.Min(ba.MaxY, bb.MaxY) - Math.Max(ba.MinY, bb.MinY);
            if (xOverlap > _tolerance &&
                (Math.Abs(ba.MaxY - bb.MinY) <= _tolerance || Math.Abs(bb.MaxY - ba.MinY) <= _tolerance))
            {
                double y = Math.Abs(ba.MaxY - bb.MinY) <= _tolerance ? ba.MaxY : ba.MinY;
                line = new[] { y, Math.Max(ba.MinX, bb.MinX), Math.Min(ba.MaxX, bb.MaxX) };
                return xOverlap;
            }

            if (yOverlap > _tolerance &&
                (Math.Abs(ba.MaxX - bb.MinX) <= _tolerance || Math.Abs(bb.MaxX - ba.MinX) <= _tolerance))
            {
                double x = Math.Abs(ba.MaxX - bb.MinX) <= _tolerance ? ba.MaxX : ba.MinX;
                verticalWall = true;
                line = new[] { x, Math.Max(ba.MinY, bb.MinY), Math.Min(ba.MaxY, bb.MaxY) };
                return yOverlap;
            }

            return 0.0;
        }

        private string SharedWallId(RoomLayout room, RoomLayout host, Dictionary<string, string> wallBySegment, out Point2 location)
        {
            location = null;
            bool verticalWall;
            double[] line;
            if (SharedSpan(room, host, out verticalWall, out line) <= _tolerance || line == null)
            {
                return null;
            }

            double mid = (line[1] + line[2]) * 0.5;
            location = verticalWall ? new Point2(line[0], mid) : new Point2(mid, line[0]);

            // The host wall is whichever emitted partition covers the shared line;
            // both rooms' edges were emitted (or deduped) into wallBySegment, so
            // probing each room's matching edge always resolves an id.
            foreach (RoomLayout candidate in new[] { room, host })
            {
                foreach (LineSegment2 edge in ToPolygon(candidate.Polygon).Edges())
                {
                    bool edgeVertical = Math.Abs(edge.Start.X - edge.End.X) <= _tolerance;
                    if (edgeVertical != verticalWall)
                    {
                        continue;
                    }

                    double edgeAxis = edgeVertical ? edge.Start.X : edge.Start.Y;
                    if (Math.Abs(edgeAxis - line[0]) > _tolerance)
                    {
                        continue;
                    }

                    string id;
                    if (wallBySegment.TryGetValue(SegmentKey(edge), out id))
                    {
                        return id;
                    }
                }
            }

            return null;
        }

        private Point2 EntryDoorLocation(Bounds2 hubBounds, Bounds2 bounds, string entrySide)
        {
            switch (entrySide)
            {
                case "north":
                    return new Point2((hubBounds.MinX + hubBounds.MaxX) * 0.5, bounds.MaxY);
                case "west":
                    return new Point2(bounds.MinX, (hubBounds.MinY + hubBounds.MaxY) * 0.5);
                case "east":
                    return new Point2(bounds.MaxX, (hubBounds.MinY + hubBounds.MaxY) * 0.5);
                default:
                    return new Point2((hubBounds.MinX + hubBounds.MaxX) * 0.5, bounds.MinY);
            }
        }

        private int EntryEdgeIndex(Polygon2 unitPolygon, Bounds2 bounds, string entrySide)
        {
            int index = 0;
            foreach (LineSegment2 edge in unitPolygon.Edges())
            {
                index++;
                bool horizontal = edge.IsHorizontal(_tolerance);
                switch (entrySide)
                {
                    case "north":
                        if (horizontal && Math.Abs(edge.Start.Y - bounds.MaxY) <= _tolerance) { return index; }
                        break;
                    case "west":
                        if (!horizontal && Math.Abs(edge.Start.X - bounds.MinX) <= _tolerance) { return index; }
                        break;
                    case "east":
                        if (!horizontal && Math.Abs(edge.Start.X - bounds.MaxX) <= _tolerance) { return index; }
                        break;
                    default:
                        if (horizontal && Math.Abs(edge.Start.Y - bounds.MinY) <= _tolerance) { return index; }
                        break;
                }
            }

            return 1;
        }

        private static IEnumerable<LabelLayout> CreateLabels(UnitLayout unit)
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
                yield return new LabelLayout
                {
                    Id = "label-" + room.Id,
                    TargetId = room.Id,
                    Text = room.RoomType + " " + Math.Round(room.Area, 1).ToString(CultureInfo.InvariantCulture) + " m2",
                    Location = ToPolygon(room.Polygon).Centroid(),
                    Layer = "FP::Generated::Labels"
                };
            }
        }

        private static string DwellingType(UnitMixPlanner mixPlanner)
        {
            UnitTypeTarget counted = mixPlanner.Targets.FirstOrDefault(t => t.TargetCount > 0);
            if (counted != null)
            {
                return counted.Type;
            }

            UnitTypeTarget weighted = mixPlanner.Targets.OrderByDescending(t => t.TargetRatio).FirstOrDefault();
            return weighted != null && weighted.TargetRatio > 0.0 ? weighted.Type : "one_bed";
        }

        private static int BedroomsFor(string type)
        {
            string value = (type ?? string.Empty).ToLowerInvariant();
            if (value.Contains("studio") || value.Contains("rk")) { return 0; }
            if (value.Contains("three") || value.Contains("3")) { return 3; }
            if (value.Contains("two") || value.Contains("2")) { return 2; }
            return 1;
        }

        private static string RoomIdByType(UnitLayout unit, string roomType)
        {
            RoomLayout room = unit.Rooms.FirstOrDefault(r => string.Equals(r.RoomType, roomType, StringComparison.OrdinalIgnoreCase));
            return room != null ? room.Id : null;
        }

        private static string RoomIdContaining(UnitLayout unit, string fragment)
        {
            RoomLayout room = unit.Rooms.FirstOrDefault(r => r.RoomType.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
            return room != null ? room.Id : null;
        }

        private static string FirstRoomId(UnitLayout unit)
        {
            return unit.Rooms.Count > 0 ? unit.Rooms[0].Id : null;
        }

        private static double DwellingScore(UnitLayout unit, UnitTypeTarget target)
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

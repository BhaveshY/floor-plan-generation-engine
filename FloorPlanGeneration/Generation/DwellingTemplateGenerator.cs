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

            // A living room set back behind a balcony still gets daylight through
            // the balcony's open facade; this forces its Daylight flag on so the
            // validator's habitable-daylight rule is satisfied.
            public bool ForceDaylight;
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
                AddRoom(unit, room.RoomType, world, room.ExpectsDaylight, room.ForceDaylight);
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
        /// Frame layout (entry along y = 0): a wet band of service rooms (baths,
        /// kitchen, optional foyer / store / utility / pooja) at the entry, then a
        /// daylight band with the living room central and bedrooms — plus any
        /// study and dining — flanking it. An optional balcony is stacked at the
        /// far facade in front of the living. Counts come from the dwelling
        /// program; each is clamped to what the plate can actually host. The
        /// single-bathroom, no-extras program reproduces the historic layout
        /// byte-for-byte so existing plans stay deterministic.
        /// </summary>
        private List<FrameRect> LayoutRooms(double width, double depth, string type, RoomStyle style, List<Diagnostic> diagnostics)
        {
            double minW = Math.Max(1.2, _input.Source.Rules.MinRoomWidth);
            double minD = Math.Max(1.2, _input.Source.Rules.MinRoomDepth);

            DwellingProgram program = _input.Source.Program != null && _input.Source.Program.Dwelling != null
                ? _input.Source.Program.Dwelling
                : new DwellingProgram();
            int bedrooms = program.Bedrooms >= 0 ? program.Bedrooms : BedroomsFor(type);
            int bathrooms = Math.Max(1, program.Bathrooms);
            int kitchens = Math.Max(1, program.Kitchens);
            int livings = Math.Max(1, program.Livings);
            int study = Math.Max(0, program.Study);
            int dining = Math.Max(0, program.Dining);
            int store = Math.Max(0, program.Store);
            int utility = Math.Max(0, program.Utility);
            int pooja = Math.Max(0, program.Pooja);
            int balcony = Math.Max(0, program.Balcony);

            double wetLo = Math.Max(1.8, minD * 0.75);
            double wetHi = Math.Min(3.2, depth - minD);
            double wetDepth = Clamp(depth * style.WetFraction, wetLo, wetHi);
            // The wet/day split is one shared partition line: snapping it moves both
            // bands together, so watertight tiling holds. SnapWithin keeps the raw
            // value when the grid would push the split outside its usable range.
            wetDepth = Grid.SnapWithin(wetDepth, wetLo, wetHi, 0.0, _input.Source.Rules.GridModule);
            List<FrameRect> rooms = new List<FrameRect>();

            BuildWetBand(rooms, width, wetDepth, minW, bedrooms, ref bathrooms, ref kitchens, ref pooja, ref store, ref utility, style, diagnostics);
            BuildDayBand(rooms, width, depth, wetDepth, minW, minD, ref bedrooms, livings, ref study, ref dining, ref balcony, style, diagnostics);

            return rooms;
        }

        /// <summary>
        /// Lays the entry-facade service band left to right. The kitchen always
        /// takes the remainder; baths and interior service rooms are dropped (with
        /// a diagnostic) before they would starve the kitchen below a usable width.
        /// </summary>
        private void BuildWetBand(
            List<FrameRect> rooms,
            double width,
            double wetDepth,
            double minW,
            int bedrooms,
            ref int bathrooms,
            ref int kitchens,
            ref int pooja,
            ref int store,
            ref int utility,
            RoomStyle style,
            List<Diagnostic> diagnostics)
        {
            double foyerWidth = Clamp(width * 0.24, 1.2, 2.8);
            double minKitchen = Math.Min(minW, 1.8);

            if (bathrooms == 1 && kitchens == 1 && pooja == 0 && store == 0 && utility == 0)
            {
                // Historic single-bathroom, single-kitchen wet band, preserved byte-for-byte
                // when the grid is off; its bath/foyer/kitchen partitions snap when it is on.
                double bathSimple = Clamp(width * style.WetSplitFraction, Math.Min(minW, 1.8), Math.Max(Math.Min(minW, 1.8), width * 0.40));
                bool foyerSimple = width - bathSimple - foyerWidth >= minKitchen && bedrooms > 0;
                List<string> simpleTypes = new List<string> { "bathroom" };
                List<double> simpleWidths = new List<double> { bathSimple };
                double simpleUsed = bathSimple;
                if (foyerSimple)
                {
                    simpleTypes.Add("foyer");
                    simpleWidths.Add(foyerWidth);
                    simpleUsed += foyerWidth;
                }

                simpleTypes.Add("kitchen");
                simpleWidths.Add(width - simpleUsed);
                PlaceWetRooms(rooms, simpleTypes, simpleWidths, wetDepth, Math.Min(1.2, minKitchen));
                return;
            }

            // Every kitchen must clear a usable width, so the bath/service/foyer
            // fit checks reserve room for all of them, not just one.
            double kitchenReserve = kitchens * minKitchen;
            double bathWidth = Clamp(width * style.WetSplitFraction, Math.Max(1.4, Math.Min(minW, 1.8)), 2.6);
            double serviceWidth = Clamp(width * 0.16, 1.2, 2.2);
            double used = 0.0;
            int placedBaths = 0;
            for (int i = 0; i < bathrooms; i++)
            {
                if (width - used - bathWidth < kitchenReserve)
                {
                    break;
                }

                used += bathWidth;
                placedBaths++;
            }

            if (placedBaths < bathrooms)
            {
                diagnostics.Add(Diagnostic.Warning(
                    "dwelling.bathrooms_reduced",
                    "Plate width cannot host every bathroom beside the kitchen; reduced the bathroom count to fit.",
                    "floorplate"));
            }

            bathrooms = Math.Max(1, placedBaths);

            int placedPooja = FitService(ref used, width, serviceWidth, kitchenReserve, pooja);
            int placedStore = FitService(ref used, width, serviceWidth, kitchenReserve, store);
            int placedUtility = FitService(ref used, width, serviceWidth, kitchenReserve, utility);
            ReportDropped(diagnostics, "pooja room", pooja - placedPooja);
            ReportDropped(diagnostics, "store", store - placedStore);
            ReportDropped(diagnostics, "utility room", utility - placedUtility);
            pooja = placedPooja;
            store = placedStore;
            utility = placedUtility;

            bool hasFoyer = bedrooms > 0 && width - used - foyerWidth >= kitchenReserve;
            if (hasFoyer)
            {
                used += foyerWidth;
            }

            // Collect the wet rooms left-to-right, then snap their shared partitions to
            // the grid in one pass (SnapBoundaries freezes the [0, width] band edges and
            // lets the last kitchen absorb the drift, so the band still tiles exactly).
            List<string> wetTypes = new List<string>();
            List<double> wetWidths = new List<double>();
            for (int i = 0; i < bathrooms; i++) { wetTypes.Add("bathroom"); wetWidths.Add(bathWidth); }
            for (int i = 0; i < pooja; i++) { wetTypes.Add("pooja"); wetWidths.Add(serviceWidth); }
            for (int i = 0; i < store; i++) { wetTypes.Add("store"); wetWidths.Add(serviceWidth); }
            for (int i = 0; i < utility; i++) { wetTypes.Add("utility"); wetWidths.Add(serviceWidth); }
            if (hasFoyer)
            {
                wetTypes.Add("foyer");
                wetWidths.Add(foyerWidth);
            }

            // Split the remaining width into the requested kitchens; the last one
            // absorbs rounding drift so the wet band still tiles exactly.
            int fittableKitchens = Math.Max(1, (int)Math.Floor((width - used + _tolerance) / minKitchen));
            if (fittableKitchens < kitchens)
            {
                ReportDropped(diagnostics, "kitchen", kitchens - fittableKitchens);
                kitchens = fittableKitchens;
            }

            double kitchenTotal = width - used;
            double kitchenCursor = used;
            for (int i = 0; i < kitchens; i++)
            {
                double kitchenWidth = i == kitchens - 1 ? width - kitchenCursor : kitchenTotal / kitchens;
                wetTypes.Add("kitchen");
                wetWidths.Add(kitchenWidth);
                kitchenCursor += kitchenWidth;
            }

            PlaceWetRooms(rooms, wetTypes, wetWidths, wetDepth, Math.Min(1.2, minKitchen));
        }

        /// <summary>
        /// Lays the daylight band: the living room stays central while bedrooms,
        /// study and dining flank it (bedrooms keep priority when the plate is
        /// short). An optional balcony is carved from the far facade in front of
        /// the living, which then draws daylight through it.
        /// </summary>
        private void BuildDayBand(
            List<FrameRect> rooms,
            double width,
            double depth,
            double wetDepth,
            double minW,
            double minD,
            ref int bedrooms,
            int livings,
            ref int study,
            ref int dining,
            ref int balcony,
            RoomStyle style,
            List<Diagnostic> diagnostics)
        {
            int maxColumns = Math.Max(1, (int)Math.Floor((width + _tolerance) / minW));

            // Bedrooms keep first claim on the band but always leave at least one
            // column for a living; the requested livings then fill what is left,
            // and study/dining share whatever columns remain after that.
            if (bedrooms > maxColumns - 1)
            {
                diagnostics.Add(Diagnostic.Warning(
                    "dwelling.bedrooms_reduced",
                    "Plate width cannot host every bedroom beside the living room; reduced the bedroom count to fit.",
                    "floorplate"));
                bedrooms = Math.Max(0, maxColumns - 1);
            }

            int requestedLivings = livings;
            livings = Math.Max(1, Math.Min(livings, maxColumns - bedrooms));
            if (livings < requestedLivings)
            {
                ReportDropped(diagnostics, "living room", requestedLivings - livings);
            }

            int fittable = Math.Max(0, maxColumns - bedrooms - livings);

            string livingType = bedrooms == 0 ? "living_sleeping" : "living";
            if (study == 0 && dining == 0 && balcony == 0 && livings == 1)
            {
                BuildSimpleDayBand(rooms, width, depth, wetDepth, minW, bedrooms, livingType, style);
                return;
            }

            List<string> flank = new List<string>();
            for (int i = 0; i < bedrooms; i++) { flank.Add("bedroom"); }
            List<string> extras = new List<string>();
            for (int i = 0; i < study; i++) { extras.Add("study"); }
            for (int i = 0; i < dining; i++) { extras.Add("dining"); }
            if (extras.Count > fittable)
            {
                int dropped = extras.Count - fittable;
                extras = extras.Take(Math.Max(0, fittable)).ToList();
                ReportDropped(diagnostics, "study/dining room", dropped);
            }

            study = extras.Count(t => t == "study");
            dining = extras.Count(t => t == "dining");
            flank.AddRange(extras);

            int flankCount = flank.Count;
            double[] widths = new double[flankCount + 1];
            double flankShare = flankCount > 0 ? Clamp((1.0 - style.OneBedFraction) / flankCount, 0.0, 1.0) : 0.0;
            for (int i = 0; i < flankCount; i++)
            {
                widths[i] = Math.Max(minW, width * flankShare);
            }

            double living = width - widths.Take(flankCount).Sum();
            double minLiving = livings * minW;
            if (living < minLiving && flankCount > 0)
            {
                double deficit = minLiving - living;
                for (int i = 0; i < flankCount; i++)
                {
                    widths[i] = Math.Max(minW, widths[i] - (deficit / flankCount));
                }

                living = Math.Max(minLiving, width - widths.Take(flankCount).Sum());
            }

            widths[flankCount] = flankCount > 0 ? living : width;

            // Split the living total into the requested living columns and seat
            // them centrally; for a single living this matches the historic order.
            double livingEach = widths[flankCount] / livings;
            List<string> order = new List<string>();
            List<double> orderedWidths = new List<double>();
            if (flankCount == 0)
            {
                for (int i = 0; i < livings; i++)
                {
                    order.Add(livingType);
                    orderedWidths.Add(livingEach);
                }
            }
            else
            {
                order.Add(flank[0]);
                orderedWidths.Add(widths[0]);
                for (int i = 0; i < livings; i++)
                {
                    order.Add(livingType);
                    orderedWidths.Add(livingEach);
                }

                for (int i = 1; i < flankCount; i++)
                {
                    order.Add(flank[i]);
                    orderedWidths.Add(widths[i]);
                }
            }

            if (style.MirrorEvenUnits)
            {
                order.Reverse();
                orderedWidths.Reverse();
            }

            double drift = width - orderedWidths.Sum();
            int widest = orderedWidths.IndexOf(orderedWidths.Max());
            orderedWidths[widest] += drift;

            // Hand balconies to living columns first, then bedrooms, until the
            // requested count is met or the daylight columns run out.
            bool[] hasBalcony = new bool[order.Count];
            int balconyBudget = balcony;
            for (int pass = 0; pass < 2 && balconyBudget > 0; pass++)
            {
                string target = pass == 0 ? livingType : "bedroom";
                for (int i = 0; i < order.Count && balconyBudget > 0; i++)
                {
                    if (!hasBalcony[i] && order[i] == target)
                    {
                        hasBalcony[i] = true;
                        balconyBudget--;
                    }
                }
            }

            ReportDropped(diagnostics, "balcony", balconyBudget);
            balcony -= balconyBudget;

            double dayDepth = depth - wetDepth;
            double balconyDepth = balcony > 0 ? Clamp(dayDepth * 0.22, 1.2, 1.8) : 0.0;
            if (balcony > 0 && dayDepth - balconyDepth < minD)
            {
                ReportDropped(diagnostics, "balcony", balcony);
                balcony = 0;
                balconyDepth = 0.0;
                for (int i = 0; i < hasBalcony.Length; i++) { hasBalcony[i] = false; }
            }

            // Snap the interior partitions to the planning grid (no-op when the grid
            // is off); neighbouring rooms share each boundary, so tiling stays exact.
            double[] bx = Grid.SnapBoundaries(0.0, orderedWidths, minW, _input.Source.Rules.GridModule);
            for (int i = 0; i < order.Count; i++)
            {
                double x0 = bx[i];
                double x1 = bx[i + 1];
                if (hasBalcony[i])
                {
                    // The room opens onto its balcony, which carries the daylight
                    // exposure on the far facade, so it is force-lit.
                    rooms.Add(new FrameRect
                    {
                        RoomType = order[i],
                        X0 = x0,
                        Y0 = wetDepth,
                        X1 = x1,
                        Y1 = depth - balconyDepth,
                        ExpectsDaylight = true,
                        ForceDaylight = true
                    });
                    rooms.Add(new FrameRect
                    {
                        RoomType = "balcony",
                        X0 = x0,
                        Y0 = depth - balconyDepth,
                        X1 = x1,
                        Y1 = depth,
                        ExpectsDaylight = false
                    });
                }
                else
                {
                    rooms.Add(DayRoom(order[i], x0, x1, wetDepth, depth));
                }
            }
        }

        /// <summary>
        /// The historic daylight band: living central, bedrooms flanking it,
        /// reproduced verbatim so the default program stays byte-identical.
        /// </summary>
        private void BuildSimpleDayBand(
            List<FrameRect> rooms, double width, double depth, double wetDepth, double minW, int bedrooms, string livingType, RoomStyle style)
        {
            if (bedrooms == 0)
            {
                rooms.Add(DayRoom(livingType, 0.0, width, wetDepth, depth));
                return;
            }

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

            // Snap interior partitions to the planning grid (no-op when the grid is
            // off, so the historic default program stays byte-identical).
            double[] bx = Grid.SnapBoundaries(0.0, orderedWidths, minW, _input.Source.Rules.GridModule);
            for (int i = 0; i < order.Count; i++)
            {
                rooms.Add(DayRoom(order[i], bx[i], bx[i + 1], wetDepth, depth));
            }
        }

        private static int FitService(ref double used, double width, double serviceWidth, double minKitchen, int requested)
        {
            int placed = 0;
            for (int i = 0; i < requested; i++)
            {
                if (width - used - serviceWidth < minKitchen)
                {
                    break;
                }

                used += serviceWidth;
                placed++;
            }

            return placed;
        }

        // Snaps the wet band's left-to-right partitions onto the planning grid before
        // emitting the rooms (no-op when the grid is off, so the historic cursor-placed
        // band stays byte-identical). The band tiles [0, width]; SnapBoundaries freezes
        // those edges and the last room absorbs the drift, so tiling stays exact.
        private void PlaceWetRooms(List<FrameRect> rooms, List<string> types, List<double> widths, double wetDepth, double minSegment)
        {
            double[] bx = Grid.SnapBoundaries(0.0, widths, minSegment, _input.Source.Rules.GridModule);
            for (int i = 0; i < types.Count; i++)
            {
                rooms.Add(new FrameRect
                {
                    RoomType = types[i],
                    X0 = bx[i],
                    Y0 = 0.0,
                    X1 = bx[i + 1],
                    Y1 = wetDepth,
                    ExpectsDaylight = false
                });
            }
        }

        private static void ReportDropped(List<Diagnostic> diagnostics, string label, int dropped)
        {
            if (dropped > 0)
            {
                diagnostics.Add(Diagnostic.Warning(
                    "dwelling.room_dropped",
                    "Plate is too small to host the requested " + label + "; it was left out to keep the plan buildable.",
                    "floorplate"));
            }
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

        private void AddRoom(UnitLayout unit, string roomType, Bounds2 world, bool expectsDaylight, bool forceDaylight = false)
        {
            Polygon2 polygon = Polygon2.Rectangle(
                unit.Id + "-" + roomType + "-" + (unit.Rooms.Count + 1).ToString(CultureInfo.InvariantCulture),
                world.MinX,
                world.MinY,
                world.MaxX,
                world.MaxY);
            bool daylight = forceDaylight || (expectsDaylight &&
                FacadeAnalyzer.HasDaylightExposure(polygon, _input.Floorplate, _input.Source.Facade, _tolerance));
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

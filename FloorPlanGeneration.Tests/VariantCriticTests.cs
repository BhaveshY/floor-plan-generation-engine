using System.Collections.Generic;
using System.Linq;
using FloorPlanGeneration.Generation;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    // Phase 5 (architectural-finetuning): the critic is a soft quality gate that flags
    // weak variants across daylight/egress/proportion/adjacency without changing pass/fail
    // or ordering. VariantCritic is pure and deterministic.
    public sealed class VariantCriticTests
    {
        private const double Tol = 0.01;

        [Fact]
        public void Critique_CleanVariantHasNoFindingsAndIsNotFlagged()
        {
            RoomLayout bedroom = Room("R1", "U1", "bedroom", daylight: true, 0, 0, 4, 4);
            UnitLayout unit = Unit("U1", "studio", bedroom);
            LayoutVariant variant = Variant("v1", new[] { unit }, new[] { bedroom }, NoCorridors(), NoDoors(), passed: true);

            QualityCritique critique = VariantCritic.Critique(new[] { variant }, PortfolioPriors.Default(), Tol, CritiqueThresholds.Default());

            Assert.Single(critique.Variants);
            Assert.Empty(critique.Variants[0].Findings);
            Assert.DoesNotContain("v1", critique.FlaggedVariantIds);
        }

        [Fact]
        public void Critique_FlagsHabitableRoomWithoutDaylightAsWarning()
        {
            RoomLayout bedroom = Room("R1", "U1", "bedroom", daylight: false, 0, 0, 4, 4);
            UnitLayout unit = Unit("U1", "studio", bedroom);
            LayoutVariant variant = Variant("v1", new[] { unit }, new[] { bedroom }, NoCorridors(), NoDoors(), passed: true);

            QualityCritique critique = VariantCritic.Critique(new[] { variant }, PortfolioPriors.Default(), Tol, CritiqueThresholds.Default());

            CritiqueFinding finding = Assert.Single(critique.Variants[0].Findings.Where(f => f.Dimension == "daylight"));
            Assert.Equal("warning", finding.Severity);
            Assert.Contains("v1", critique.FlaggedVariantIds);
        }

        [Fact]
        public void Critique_FlagsLongRoomOnProportionAsInfo()
        {
            RoomLayout bedroom = Room("R1", "U1", "bedroom", daylight: true, 0, 0, 12, 3);
            UnitLayout unit = Unit("U1", "studio", bedroom);
            LayoutVariant variant = Variant("v1", new[] { unit }, new[] { bedroom }, NoCorridors(), NoDoors(), passed: true);

            QualityCritique critique = VariantCritic.Critique(new[] { variant }, PortfolioPriors.Default(), Tol, CritiqueThresholds.Default());

            CritiqueFinding finding = Assert.Single(critique.Variants[0].Findings.Where(f => f.Dimension == "proportion"));
            Assert.Equal("info", finding.Severity);
            Assert.DoesNotContain(critique.Variants[0].Findings, f => f.Dimension == "daylight");
        }

        [Fact]
        public void Critique_HonorsRaisedAspectThreshold()
        {
            RoomLayout bedroom = Room("R1", "U1", "bedroom", daylight: true, 0, 0, 12, 3);
            UnitLayout unit = Unit("U1", "studio", bedroom);
            LayoutVariant variant = Variant("v1", new[] { unit }, new[] { bedroom }, NoCorridors(), NoDoors(), passed: true);
            CritiqueThresholds lenient = new CritiqueThresholds { DaylightFloor = 1.0, EgressFloor = 1.0, MaxAspectRatio = 5.0, AdjacencyFloor = 0.5 };

            QualityCritique critique = VariantCritic.Critique(new[] { variant }, PortfolioPriors.Default(), Tol, lenient);

            Assert.DoesNotContain(critique.Variants[0].Findings, f => f.Dimension == "proportion");
        }

        [Fact]
        public void Critique_FlagsUnitWithoutCorridorDoorOnEgress()
        {
            RoomLayout bedroom = Room("R1", "U1", "bedroom", daylight: true, 0, 0, 4, 4);
            UnitLayout unit = Unit("U1", "studio", bedroom);
            CorridorLayout corridor = new CorridorLayout { Id = "C1" };
            LayoutVariant variant = Variant("v1", new[] { unit }, new[] { bedroom }, new[] { corridor }, NoDoors(), passed: true);

            QualityCritique critique = VariantCritic.Critique(new[] { variant }, PortfolioPriors.Default(), Tol, CritiqueThresholds.Default());

            CritiqueFinding finding = Assert.Single(critique.Variants[0].Findings.Where(f => f.Dimension == "egress"));
            Assert.Equal("warning", finding.Severity);
        }

        [Fact]
        public void Critique_DoesNotFlagEgressWhenUnitReachesCorridor()
        {
            RoomLayout bedroom = Room("R1", "U1", "bedroom", daylight: true, 0, 0, 4, 4);
            UnitLayout unit = Unit("U1", "studio", bedroom);
            CorridorLayout corridor = new CorridorLayout { Id = "C1" };
            DoorOpening door = new DoorOpening { Id = "D1", ConnectsSpaces = new List<string> { "U1", "C1" } };
            LayoutVariant variant = Variant("v1", new[] { unit }, new[] { bedroom }, new[] { corridor }, new[] { door }, passed: true);

            QualityCritique critique = VariantCritic.Critique(new[] { variant }, PortfolioPriors.Default(), Tol, CritiqueThresholds.Default());

            Assert.DoesNotContain(critique.Variants[0].Findings, f => f.Dimension == "egress");
        }

        [Fact]
        public void Critique_FlagsLowAdjacencyAsInfo()
        {
            // kitchen|bedroom prior is 0.40 (< the 0.50 floor); the two rooms share a wall.
            RoomLayout kitchen = Room("R1", "U1", "kitchen", daylight: false, 0, 0, 2, 2);
            RoomLayout bedroom = Room("R2", "U1", "bedroom", daylight: true, 2, 0, 4, 2);
            UnitLayout unit = Unit("U1", "two_bed", kitchen, bedroom);
            LayoutVariant variant = Variant("v1", new[] { unit }, new[] { kitchen, bedroom }, NoCorridors(), NoDoors(), passed: true);

            QualityCritique critique = VariantCritic.Critique(new[] { variant }, PortfolioPriors.Default(), Tol, CritiqueThresholds.Default());

            CritiqueFinding finding = Assert.Single(critique.Variants[0].Findings.Where(f => f.Dimension == "adjacency"));
            Assert.Equal("info", finding.Severity);
            Assert.True(finding.Score < 0.5);
        }

        [Fact]
        public void Critique_CoversEveryVariantInGivenOrder()
        {
            RoomLayout bedroom = Room("R1", "U1", "bedroom", daylight: true, 0, 0, 4, 4);
            UnitLayout unit = Unit("U1", "studio", bedroom);
            LayoutVariant first = Variant("v1", new[] { unit }, new[] { bedroom }, NoCorridors(), NoDoors(), passed: true);
            LayoutVariant second = Variant("v2", new[] { unit }, new[] { bedroom }, NoCorridors(), NoDoors(), passed: true);

            QualityCritique critique = VariantCritic.Critique(new[] { first, second }, PortfolioPriors.Default(), Tol, CritiqueThresholds.Default());

            Assert.Equal(new[] { "v1", "v2" }, critique.Variants.Select(v => v.VariantId).ToArray());
        }

        private static RoomLayout Room(string id, string unitId, string type, bool daylight, double x0, double y0, double x1, double y1)
        {
            return new RoomLayout
            {
                Id = id,
                UnitId = unitId,
                RoomType = type,
                Daylight = daylight,
                Area = (x1 - x0) * (y1 - y0),
                Polygon = Rect(id, x0, y0, x1, y1),
                Bounds = new Bounds2 { MinX = x0, MinY = y0, MaxX = x1, MaxY = y1 }
            };
        }

        private static UnitLayout Unit(string id, string type, params RoomLayout[] rooms)
        {
            return new UnitLayout { Id = id, Type = type, Rooms = rooms.ToList() };
        }

        private static LayoutVariant Variant(
            string id,
            IEnumerable<UnitLayout> units,
            IEnumerable<RoomLayout> rooms,
            IEnumerable<CorridorLayout> corridors,
            IEnumerable<DoorOpening> doors,
            bool passed)
        {
            return new LayoutVariant
            {
                VariantId = id,
                Units = units.ToList(),
                Rooms = rooms.ToList(),
                Corridors = corridors.ToList(),
                DoorsOpenings = doors.ToList(),
                Validation = new ValidationReport { Passed = passed }
            };
        }

        private static PolygonInput Rect(string id, double x0, double y0, double x1, double y1)
        {
            return new PolygonInput
            {
                Id = id,
                Points = new List<Point2>
                {
                    new Point2(x0, y0),
                    new Point2(x1, y0),
                    new Point2(x1, y1),
                    new Point2(x0, y1)
                }
            };
        }

        private static CorridorLayout[] NoCorridors()
        {
            return new CorridorLayout[0];
        }

        private static DoorOpening[] NoDoors()
        {
            return new DoorOpening[0];
        }
    }
}

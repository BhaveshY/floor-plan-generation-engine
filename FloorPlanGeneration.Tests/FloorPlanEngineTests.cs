using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FloorPlanGeneration;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class FloorPlanEngineTests
    {
        [Fact]
        public void RectangularInput_ReturnsDeterministicRankedVariants()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 1234, variantCount: 4));

            Assert.Equal("succeeded", output.Status);
            Assert.Equal(4, output.Variants.Count);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            Assert.All(output.Variants, v => Assert.NotEmpty(v.Units));

            List<double> scores = output.Variants.Select(v => v.Metrics.Score).ToList();
            Assert.Equal(scores.OrderByDescending(s => s).ToList(), scores);

            EngineOutput repeated = new FloorPlanEngine().Generate(RectangularInput(seed: 1234, variantCount: 4));
            Assert.Equal(Signatures(output), Signatures(repeated));
        }

        [Fact]
        public void SameSeed_ProducesSameVariantIdsScoresAndLayoutCounts()
        {
            EngineOutput left = new FloorPlanEngine().Generate(RectangularInput(seed: 8128, variantCount: 5));
            EngineOutput right = new FloorPlanEngine().Generate(RectangularInput(seed: 8128, variantCount: 5));

            Assert.Equal(Signatures(left), Signatures(right));
        }

        [Fact]
        public void SelfIntersectingBoundary_ReturnsFailedDiagnosticsInsteadOfFakePlan()
        {
            EngineInput input = RectangularInput(seed: 1, variantCount: 3);
            input.Floorplate.Outer.Id = "bowtie";
            input.Floorplate.Outer.Points = new List<Point2>
            {
                new Point2(0, 0),
                new Point2(10, 10),
                new Point2(0, 10),
                new Point2(10, 0)
            };

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.Empty(output.Variants);
            Assert.Contains(output.Diagnostics, d => d.Code == "geometry.self_intersection" && d.Severity == "error");
        }

        [Fact]
        public void StrictInfeasibleUnitMix_ReportsValidationFailure()
        {
            EngineInput input = RectangularInput(seed: 7, variantCount: 3);
            input.GenerationSettings.Strictness = "strict";
            input.Program.TargetUnitTypes = new List<UnitTypeTarget>
            {
                new UnitTypeTarget
                {
                    Type = "studio",
                    MinArea = 26.0,
                    MaxArea = 48.0,
                    TargetCount = 99,
                    Weight = 1.0
                }
            };

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.NotEmpty(output.Variants);
            Assert.All(output.Variants, v => Assert.False(v.Validation.Passed));
            Assert.Contains(output.Diagnostics, d => d.Code == "validation.strict_unit_mix" && d.Severity == "error");
            Assert.Contains(output.Variants.SelectMany(v => v.Validation.Checks), c => c.Name == "strict_unit_mix" && !c.Passed);
        }

        [Fact]
        public void LShapedInput_SplitsUsableBandsAroundCoreAndRemainsDeterministic()
        {
            EngineOutput output = new FloorPlanEngine().Generate(LShapedInput(seed: 5601, variantCount: 5));

            Assert.Equal("succeeded", output.Status);
            Assert.Equal(5, output.Variants.Count);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            Assert.All(output.Variants, v => Assert.Contains(v.Units, u => u.Id.StartsWith("unit-south-", System.StringComparison.OrdinalIgnoreCase)));
            Assert.All(output.Variants, v => Assert.Contains(v.Units, u => u.Id.StartsWith("unit-north-", System.StringComparison.OrdinalIgnoreCase)));
            Assert.All(output.Variants, v => Assert.DoesNotContain(v.Diagnostics, d => d.Code == "generation.unit_bay_rejected"));

            EngineOutput repeated = new FloorPlanEngine().Generate(LShapedInput(seed: 5601, variantCount: 5));
            Assert.Equal(Signatures(output), Signatures(repeated));
        }

        [Fact]
        public void NarrowFloorplate_ReturnsFailedDiagnosticsWithoutVariants()
        {
            EngineInput input = RectangularInput(seed: 11, variantCount: 3);
            input.Project.Id = "narrow-infeasible-test";
            input.Floorplate.Outer.Points = new List<Point2>
            {
                new Point2(0, 0),
                new Point2(12, 0),
                new Point2(12, 4),
                new Point2(0, 4)
            };
            input.FixedElements.Clear();
            input.Rules.MinCorridorWidth = 2.0;
            input.Rules.MinUnitArea = 25.0;

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.Empty(output.Variants);
            Assert.Contains(output.Diagnostics, d => d.Code == "input.floorplate_too_narrow_for_mvp" && d.Severity == "error");
        }

        private static IEnumerable<string> Signatures(EngineOutput output)
        {
            return output.Variants.Select(v => string.Join(
                "|",
                v.VariantId,
                v.Metrics.Score.ToString("0.0000", CultureInfo.InvariantCulture),
                v.Units.Count.ToString(CultureInfo.InvariantCulture),
                v.Rooms.Count.ToString(CultureInfo.InvariantCulture),
                v.Corridors.Count.ToString(CultureInfo.InvariantCulture),
                v.Validation.Passed.ToString()));
        }

        private static EngineInput RectangularInput(int seed, int variantCount)
        {
            EngineInput input = new EngineInput();
            input.Project.Id = "rectangular-test";
            input.Project.Name = "Rectangular Test";
            input.Project.Seed = seed;
            input.Project.Tolerance = 0.01;

            input.Floorplate.Outer = new PolygonInput
            {
                Id = "outer",
                Points = new List<Point2>
                {
                    new Point2(0, 0),
                    new Point2(36, 0),
                    new Point2(36, 18),
                    new Point2(0, 18)
                }
            };

            input.Program.TargetUnitTypes = new List<UnitTypeTarget>
            {
                new UnitTypeTarget { Type = "studio", MinArea = 28.0, MaxArea = 58.0, TargetRatio = 0.40, Weight = 1.0 },
                new UnitTypeTarget { Type = "one_bed", MinArea = 48.0, MaxArea = 78.0, TargetRatio = 0.45, Weight = 1.0 },
                new UnitTypeTarget { Type = "two_bed", MinArea = 70.0, MaxArea = 108.0, TargetRatio = 0.15, Weight = 0.7 }
            };

            input.Rules.MinCorridorWidth = 1.8;
            input.Rules.MinRoomWidth = 2.4;
            input.Rules.MinRoomDepth = 2.4;
            input.Rules.MinUnitArea = 25.0;
            input.Rules.RequireDaylightForBedrooms = true;
            input.Rules.RequireDaylightForLiving = true;

            input.GenerationSettings.VariantCount = variantCount;
            input.GenerationSettings.Strictness = "balanced";
            input.GenerationSettings.WeightedVariation = true;
            return input;
        }

        private static EngineInput LShapedInput(int seed, int variantCount)
        {
            EngineInput input = RectangularInput(seed, variantCount);
            input.Project.Id = "l-shaped-test";
            input.Project.Name = "L-Shaped Test";
            input.Floorplate.Outer = new PolygonInput
            {
                Id = "l-shaped-outer",
                Points = new List<Point2>
                {
                    new Point2(0, 0),
                    new Point2(44, 0),
                    new Point2(44, 18),
                    new Point2(28, 18),
                    new Point2(28, 30),
                    new Point2(0, 30)
                }
            };

            input.FixedElements = new List<FixedElementInput>
            {
                new FixedElementInput
                {
                    Id = "core-l1",
                    Type = "core",
                    BlocksGeneration = true,
                    Polygon = new PolygonInput
                    {
                        Id = "core-l1",
                        Points = new List<Point2>
                        {
                            new Point2(18, 8),
                            new Point2(24, 8),
                            new Point2(24, 14),
                            new Point2(18, 14)
                        }
                    }
                }
            };

            input.Access.VerticalCoreAccess = new List<Point2> { new Point2(21, 14) };
            input.GenerationSettings.WeightedVariation = true;
            return input;
        }
    }
}

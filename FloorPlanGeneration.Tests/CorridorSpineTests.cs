using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorPlanGeneration;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    // Phase 2 (architectural-finetuning): corridorSpine ranks candidate corridor
    // placements by pure geometry so circulation is deliberate (not a seeded pick);
    // driftToMargin fills unit bays from the interior end of each band interval so
    // leftover slack accrues to the building perimeter instead of mid-plan slivers.
    // Both flags are opt-in; the off-path is covered by the byte-identity / golden tests.
    public sealed class CorridorSpineTests
    {
        private const double Tol = 0.01;

        [Fact]
        public void CorridorSpine_SameSeedReproducesIdenticalVariants()
        {
            EngineInput a = RectangularSample(seed: 4242, variantCount: 5, corridorSpine: true, driftToMargin: false);
            EngineInput b = RectangularSample(seed: 4242, variantCount: 5, corridorSpine: true, driftToMargin: false);

            EngineOutput left = new FloorPlanEngine().Generate(a);
            EngineOutput right = new FloorPlanEngine().Generate(b);

            Assert.Equal("succeeded", left.Status);
            Assert.Equal(Signatures(left), Signatures(right));
        }

        [Fact]
        public void CorridorSpine_SpinePlacementIsSeedInvariant()
        {
            // The spine is scored by pure geometry, so generation-index 0 always
            // takes the same (top-ranked) placement no matter the project seed —
            // the defining difference from the historic seeded pick.
            List<double> centerYs = new List<double>();
            foreach (int seed in new[] { 1, 7, 23, 101, 5555, 99999 })
            {
                EngineInput input = RectangularSample(seed, variantCount: 4, corridorSpine: true, driftToMargin: false);
                EngineOutput output = new FloorPlanEngine().Generate(input);
                Assert.Equal("succeeded", output.Status);
                centerYs.Add(CorridorCenterY(ByGenIndex(output, "variant-01")));
            }

            Assert.All(centerYs, y => Assert.InRange(Math.Abs(y - centerYs[0]), 0.0, 1e-9));
        }

        [Fact]
        public void CorridorSpine_RanksMoreBalancedSpineFirst()
        {
            // Off-centre core (y in [7,13]) leaves two well-proportioned core-adjacent
            // spines with different band balance. The deliberate spine ranks the more
            // balanced double-loaded split first, so generation-index 0 is never worse
            // than index 1 — for every seed.
            foreach (int seed in new[] { 2, 13, 64, 777, 31337 })
            {
                EngineInput input = OffCentreCoreSample(seed, variantCount: 4);
                EngineOutput output = new FloorPlanEngine().Generate(input);
                Assert.Equal("succeeded", output.Status);

                double first = BandImbalance(ByGenIndex(output, "variant-01"));
                double second = BandImbalance(ByGenIndex(output, "variant-02"));
                Assert.True(first <= second + 1e-9, "variant-01 spine (" + first + ") less balanced than variant-02 (" + second + ").");
            }
        }

        [Fact]
        public void CorridorSpine_ProducesValidVariants()
        {
            EngineInput input = RectangularSample(seed: 20260623, variantCount: 6, corridorSpine: true, driftToMargin: false);
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, variant => Assert.True(variant.Validation.Passed));
            Assert.All(output.Variants, variant => Assert.Single(variant.Corridors));
        }

        [Fact]
        public void DriftToMargin_SameSeedReproducesIdenticalVariants()
        {
            EngineInput a = RectangularSample(seed: 808, variantCount: 5, corridorSpine: false, driftToMargin: true);
            EngineInput b = RectangularSample(seed: 808, variantCount: 5, corridorSpine: false, driftToMargin: true);

            EngineOutput left = new FloorPlanEngine().Generate(a);
            EngineOutput right = new FloorPlanEngine().Generate(b);

            Assert.Equal("succeeded", left.Status);
            Assert.Equal(Signatures(left), Signatures(right));
        }

        [Fact]
        public void DriftToMargin_AnchorsBaysAtInteriorCoreEnd()
        {
            // The central core (x in [18,24]) splits one unit band into a [0,18] and a
            // [24,42] interval. With driftToMargin on, the cursor anchors at the core
            // (interior) end of each interval, so the core-adjacent bay is placed first
            // and the slack-absorbing bay lands at the perimeter — for every seed.
            int intervalsChecked = 0;
            foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                EngineInput input = RectangularSample(seed, variantCount: 3, corridorSpine: false, driftToMargin: true);
                EngineOutput output = new FloorPlanEngine().Generate(input);
                Assert.Equal("succeeded", output.Status);

                foreach (LayoutVariant variant in output.Variants)
                {
                    intervalsChecked += AssertCoreAnchoredBays(variant);
                }
            }

            Assert.True(intervalsChecked > 0, "No core-split intervals were exercised — the test would be vacuous.");
        }

        [Fact]
        public void DriftToMargin_ProducesValidVariants()
        {
            EngineInput input = RectangularSample(seed: 20260623, variantCount: 6, corridorSpine: false, driftToMargin: true);
            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, variant => Assert.True(variant.Validation.Passed));
        }

        [Fact]
        public void CorridorSpineAndDrift_ComposeDeterministicallyAndStayValid()
        {
            EngineInput a = RectangularSample(seed: 31415, variantCount: 6, corridorSpine: true, driftToMargin: true);
            EngineInput b = RectangularSample(seed: 31415, variantCount: 6, corridorSpine: true, driftToMargin: true);

            EngineOutput left = new FloorPlanEngine().Generate(a);
            EngineOutput right = new FloorPlanEngine().Generate(b);

            Assert.Equal("succeeded", left.Status);
            Assert.All(left.Variants, variant => Assert.True(variant.Validation.Passed));
            Assert.Equal(Signatures(left), Signatures(right));
        }

        // For each core-split interval in the variant, asserts the core-adjacent bay was
        // placed before the perimeter bay. Returns the number of intervals it checked.
        private static int AssertCoreAnchoredBays(LayoutVariant variant)
        {
            const double coreLeft = 18.0;
            const double coreRight = 24.0;
            const double plateLeft = 0.0;
            const double plateRight = 42.0;

            double centerY = CorridorCenterY(variant);
            double half = variant.Corridors[0].Width * 0.5;
            double corridorMinY = centerY - half;
            double corridorMaxY = centerY + half;

            Dictionary<string, int> placement = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < variant.Units.Count; i++)
            {
                placement[variant.Units[i].Id] = i;
            }

            int checks = 0;
            foreach (bool south in new[] { true, false })
            {
                List<UnitLayout> band = variant.Units
                    .Where(u => south ? Centroid(u).Y < corridorMinY : Centroid(u).Y > corridorMaxY)
                    .ToList();

                // Only the band the core actually splits has a clean empty gap at [18,24]:
                // bays sit on both sides and no bay's interior intrudes into the core span.
                // In the band the core does not reach, the fill is one continuous left-to-right
                // run whose direction is legitimately seeded (a bay boundary can even land inside
                // [18,24]), so skip it — checking sub-intervals of a continuous run is meaningless.
                bool leftSide = band.Any(u => MaxX(u) <= coreLeft + Tol);
                bool rightSide = band.Any(u => MinX(u) >= coreRight - Tol);
                bool overlapsCore = band.Any(u => MinX(u) < coreRight - Tol && MaxX(u) > coreLeft + Tol);
                if (!leftSide || !rightSide || overlapsCore)
                {
                    continue;
                }

                checks += CheckInterval(band, placement, plateLeft, coreLeft, anchorHigh: true);
                checks += CheckInterval(band, placement, coreRight, plateRight, anchorHigh: false);
            }

            return checks;
        }

        // anchorHigh = the interior (core) end is the high coordinate, so the bay touching
        // it (largest maxX) must be placed before the perimeter bay (smallest minX).
        private static int CheckInterval(
            List<UnitLayout> band,
            Dictionary<string, int> placement,
            double intervalMin,
            double intervalMax,
            bool anchorHigh)
        {
            List<UnitLayout> inside = band
                .Where(u => MinX(u) >= intervalMin - Tol && MaxX(u) <= intervalMax + Tol)
                .ToList();
            if (inside.Count < 2)
            {
                return 0;
            }

            UnitLayout coreBay = anchorHigh
                ? inside.OrderByDescending(u => MaxX(u)).First()
                : inside.OrderBy(u => MinX(u)).First();
            UnitLayout perimeterBay = anchorHigh
                ? inside.OrderBy(u => MinX(u)).First()
                : inside.OrderByDescending(u => MaxX(u)).First();

            if (ReferenceEquals(coreBay, perimeterBay))
            {
                return 0;
            }

            Assert.True(
                placement[coreBay.Id] < placement[perimeterBay.Id],
                "Core-adjacent bay " + coreBay.Id + " was not placed before perimeter bay " + perimeterBay.Id + ".");
            return 1;
        }

        private static LayoutVariant ByGenIndex(EngineOutput output, string variantId)
        {
            return output.Variants.Single(v => string.Equals(v.VariantId, variantId, StringComparison.Ordinal));
        }

        private static double CorridorCenterY(LayoutVariant variant)
        {
            LineInput centerline = variant.Corridors[0].Centerline;
            return (centerline.Start.Y + centerline.End.Y) * 0.5;
        }

        // |near band depth - far band depth| for a horizontal spine on the 42x22 plate.
        private static double BandImbalance(LayoutVariant variant)
        {
            double centerY = CorridorCenterY(variant);
            double half = variant.Corridors[0].Width * 0.5;
            double near = (centerY - half) - 0.0;
            double far = 22.0 - (centerY + half);
            return Math.Abs(near - far);
        }

        private static Point2 Centroid(UnitLayout unit)
        {
            List<Point2> points = unit.Polygon.Points;
            double sumX = 0.0;
            double sumY = 0.0;
            foreach (Point2 point in points)
            {
                sumX += point.X;
                sumY += point.Y;
            }

            int count = Math.Max(points.Count, 1);
            return new Point2(sumX / count, sumY / count);
        }

        private static double MinX(UnitLayout unit)
        {
            return unit.Polygon.Points.Min(p => p.X);
        }

        private static double MaxX(UnitLayout unit)
        {
            return unit.Polygon.Points.Max(p => p.X);
        }

        private static IEnumerable<string> Signatures(EngineOutput output)
        {
            return output.Variants.Select(v => string.Join(
                "|",
                v.VariantId,
                v.Metrics.Score.ToString("0.0000", CultureInfo.InvariantCulture),
                v.Units.Count.ToString(CultureInfo.InvariantCulture),
                v.Rooms.Count.ToString(CultureInfo.InvariantCulture),
                CorridorCenterY(v).ToString("0.0000", CultureInfo.InvariantCulture)));
        }

        private static EngineInput RectangularSample(int seed, int variantCount, bool corridorSpine, bool driftToMargin)
        {
            EngineInput input = LoadSample("rectangular-core-input.json");
            input.Project.Seed = seed;
            input.GenerationSettings.VariantCount = variantCount;
            input.Rules.CorridorSpine = corridorSpine;
            input.Rules.DriftToMargin = driftToMargin;
            return input;
        }

        private static EngineInput OffCentreCoreSample(int seed, int variantCount)
        {
            EngineInput input = RectangularSample(seed, variantCount, corridorSpine: true, driftToMargin: false);
            input.FixedElements[0].Polygon.Points = new List<Point2>
            {
                new Point2(18.0, 7.0),
                new Point2(24.0, 7.0),
                new Point2(24.0, 13.0),
                new Point2(18.0, 13.0)
            };
            return input;
        }

        private static EngineInput LoadSample(string fileName)
        {
            string path = Path.Combine(RepositoryRoot(), "samples", "floor-plan-generation", fileName);
            return JsonSerializer.Deserialize<EngineInput>(File.ReadAllText(path), JsonOptions());
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
            };
        }

        private static string RepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        }
    }
}

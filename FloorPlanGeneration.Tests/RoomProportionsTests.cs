using FloorPlanGeneration.Geometry;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class RoomProportionsTests
    {
        [Fact]
        public void GrowToMinimums_RaisesUnderMinRoomByStealingFromOverMinNeighbour()
        {
            // Room 0 is below its 3.0 m minimum; room 1 sits 1.6 m above its own
            // 2.4 m minimum, so it can donate exactly the 1.0 m needed.
            double[] result = RoomProportions.GrowToMinimums(
                new[] { 2.0, 4.0 },
                new[] { 3.0, 2.4 });

            Assert.Equal(3.0, result[0], 6);
            Assert.Equal(3.0, result[1], 6);
        }

        [Fact]
        public void GrowToMinimums_PreservesTotalWidth()
        {
            double[] widths = { 2.0, 2.5, 3.0, 6.0 };
            double[] result = RoomProportions.GrowToMinimums(widths, new[] { 3.0, 3.0, 2.4, 2.4 });

            Assert.Equal(13.5, result[0] + result[1] + result[2] + result[3], 6);
        }

        [Fact]
        public void GrowToMinimums_IsNoOpWhenEverySegmentAlreadyMeetsItsMinimum()
        {
            double[] result = RoomProportions.GrowToMinimums(
                new[] { 3.0, 4.0, 3.5 },
                new[] { 2.4, 2.4, 2.4 });

            Assert.Equal(3.0, result[0], 6);
            Assert.Equal(4.0, result[1], 6);
            Assert.Equal(3.5, result[2], 6);
        }

        [Fact]
        public void GrowToMinimums_BestEffortPartiallyFillsWhenBandIsTooSmall()
        {
            // Needs 1.5 m (1.0 + 0.5) but only 0.6 m of slack exists, so scale = 0.4:
            // neither deficit room reaches its 3.0 m target and the donor lands at 2.4.
            double[] result = RoomProportions.GrowToMinimums(
                new[] { 2.0, 2.5, 3.0 },
                new[] { 3.0, 3.0, 2.4 });

            Assert.Equal(2.4, result[0], 6);
            Assert.Equal(2.7, result[1], 6);
            Assert.Equal(2.4, result[2], 6);
            Assert.Equal(7.5, result[0] + result[1] + result[2], 6);
        }

        [Fact]
        public void GrowToMinimums_NeverPushesADonorBelowItsOwnMinimum()
        {
            double[] mins = { 3.0, 3.0, 2.4 };
            double[] result = RoomProportions.GrowToMinimums(new[] { 2.0, 2.5, 3.0 }, mins);

            for (int i = 0; i < result.Length; i++)
            {
                Assert.True(result[i] >= mins[i] - 1e-9 || result[i] < mins[i],
                    "no donor may fall below its own minimum");
            }

            // The single donor (room 2) is pulled down to exactly its minimum, never under.
            Assert.True(result[2] >= 2.4 - 1e-9);
        }

        [Fact]
        public void GrowToMinimums_FreezesSegmentsWithoutAPositiveMinimum()
        {
            // Room 1 has no minimum, so it is neither grown nor raided; with no other
            // donor the deficit on room 0 simply cannot be filled (band untouched).
            double[] result = RoomProportions.GrowToMinimums(
                new[] { 2.0, 4.0 },
                new[] { 3.0, 0.0 });

            Assert.Equal(2.0, result[0], 6);
            Assert.Equal(4.0, result[1], 6);
        }

        [Fact]
        public void ShrinkToMaximums_ShrinksOverMaxSegmentByGivingToUnderMaxNeighbour()
        {
            // Room 0 is 2.0 m over its 4.0 m cap; room 1 has exactly 2.0 m of
            // headroom below its 5.0 m cap, so the excess transfers cleanly.
            double[] result = RoomProportions.ShrinkToMaximums(
                new[] { 6.0, 3.0 },
                new[] { 4.0, 5.0 });

            Assert.Equal(4.0, result[0], 6);
            Assert.Equal(5.0, result[1], 6);
        }

        [Fact]
        public void ShrinkToMaximums_PreservesTotalWidth()
        {
            double[] widths = { 7.0, 3.0, 2.5, 4.0 };
            double[] result = RoomProportions.ShrinkToMaximums(widths, new[] { 4.0, 4.0, 4.0, 4.0 });

            Assert.Equal(16.5, result[0] + result[1] + result[2] + result[3], 6);
        }

        [Fact]
        public void ShrinkToMaximums_IsNoOpWhenEverySegmentWithinItsMaximum()
        {
            double[] result = RoomProportions.ShrinkToMaximums(
                new[] { 3.0, 4.0, 3.5 },
                new[] { 4.0, 4.0, 4.0 });

            Assert.Equal(3.0, result[0], 6);
            Assert.Equal(4.0, result[1], 6);
            Assert.Equal(3.5, result[2], 6);
        }

        [Fact]
        public void ShrinkToMaximums_BestEffortPartiallyShrinksWhenNeighboursCannotAbsorbAll()
        {
            // 2.0 m of excess (room 0) but only 0.5 m of headroom (room 1), so
            // scale = 0.25: room 0 only comes down to 5.5 and room 1 fills to its cap.
            double[] result = RoomProportions.ShrinkToMaximums(
                new[] { 6.0, 4.5 },
                new[] { 4.0, 5.0 });

            Assert.Equal(5.5, result[0], 6);
            Assert.Equal(5.0, result[1], 6);
            Assert.Equal(10.5, result[0] + result[1], 6);
        }

        [Fact]
        public void ShrinkToMaximums_FreezesSegmentsWithoutAPositiveMaximum()
        {
            // Room 1 is unconstrained (max 0), so it neither shrinks nor absorbs; with
            // no other receiver the excess on room 0 cannot be placed (band untouched).
            double[] result = RoomProportions.ShrinkToMaximums(
                new[] { 6.0, 3.0 },
                new[] { 4.0, 0.0 });

            Assert.Equal(6.0, result[0], 6);
            Assert.Equal(3.0, result[1], 6);
        }

        [Fact]
        public void ConstrainToBounds_GrowsDeficitsThenCapsExcessKeepingSumAndEdges()
        {
            // Room 0 starts below its 3.0 m floor, room 1 well over its 4.0 m cap.
            // Grow-to-min lifts room 0 to 3.0 (pulling from room 1 -> 5.0), then
            // shrink-to-max caps room 1 at 4.0 and hands the 1.0 m to room 0 -> 4.0.
            double[] result = RoomProportions.ConstrainToBounds(
                new[] { 2.0, 6.0 },
                new[] { 3.0, 2.4 },
                new[] { 4.0, 4.0 });

            Assert.Equal(4.0, result[0], 6);
            Assert.Equal(4.0, result[1], 6);
            Assert.Equal(8.0, result[0] + result[1], 6);
        }

        [Fact]
        public void ConstrainToBounds_KeepsEverySegmentWithinItsBoundsWhenFeasible()
        {
            double[] min = { 3.0, 2.4, 2.4 };
            double[] max = { 4.0, 4.0, 6.0 };
            double[] result = RoomProportions.ConstrainToBounds(
                new[] { 2.0, 7.0, 3.0 }, min, max);

            for (int i = 0; i < result.Length; i++)
            {
                Assert.True(result[i] >= min[i] - 1e-6, "segment fell below its minimum");
                Assert.True(result[i] <= max[i] + 1e-6, "segment stayed above its maximum");
            }

            Assert.Equal(12.0, result[0] + result[1] + result[2], 6);
        }

        // Phase 3 (architectural-finetuning): PullToTargets nudges each segment toward
        // a target share of the band total, sum-preserving and clamped to [min,max], so
        // owned-data proportion priors can bias the split without breaking watertight
        // tiling or the Phase-1 minimums. strength in [0,1] scales the nudge.

        [Fact]
        public void PullToTargets_StrengthZeroIsANoOp()
        {
            double[] result = RoomProportions.PullToTargets(
                new[] { 4.0, 6.0 }, new[] { 0.5, 0.5 }, new[] { 0.0, 0.0 }, new[] { 0.0, 0.0 }, 0.0);

            Assert.Equal(4.0, result[0], 6);
            Assert.Equal(6.0, result[1], 6);
        }

        [Fact]
        public void PullToTargets_FullStrengthHitsTargetSharesWhenUnconstrained()
        {
            double[] result = RoomProportions.PullToTargets(
                new[] { 4.0, 6.0 }, new[] { 0.5, 0.5 }, new[] { 0.0, 0.0 }, new[] { 0.0, 0.0 }, 1.0);

            Assert.Equal(5.0, result[0], 6);
            Assert.Equal(5.0, result[1], 6);
        }

        [Fact]
        public void PullToTargets_PartialStrengthMovesProportionOfTheWayToTarget()
        {
            // Target is {5,5}; at strength 0.5 each segment moves half the distance.
            double[] result = RoomProportions.PullToTargets(
                new[] { 4.0, 6.0 }, new[] { 0.5, 0.5 }, new[] { 0.0, 0.0 }, new[] { 0.0, 0.0 }, 0.5);

            Assert.Equal(4.5, result[0], 6);
            Assert.Equal(5.5, result[1], 6);
        }

        [Fact]
        public void PullToTargets_NormalisesSharesThatDoNotSumToOne()
        {
            // Shares {1,3} over a band of 12 normalise to {0.25,0.75} -> targets {3,9}.
            double[] result = RoomProportions.PullToTargets(
                new[] { 6.0, 6.0 }, new[] { 1.0, 3.0 }, new[] { 0.0, 0.0 }, new[] { 0.0, 0.0 }, 1.0);

            Assert.Equal(3.0, result[0], 6);
            Assert.Equal(9.0, result[1], 6);
        }

        [Fact]
        public void PullToTargets_PreservesTotalWidth()
        {
            double[] widths = { 3.0, 5.0, 8.0, 4.0 };
            double[] result = RoomProportions.PullToTargets(
                widths, new[] { 0.4, 0.2, 0.1, 0.3 }, new[] { 2.0, 2.0, 2.0, 2.0 }, new[] { 0.0, 0.0, 0.0, 0.0 }, 0.7);

            Assert.Equal(20.0, result[0] + result[1] + result[2] + result[3], 6);
        }

        [Fact]
        public void PullToTargets_RespectsMinimumsAndStillPreservesSum()
        {
            // Target {5,5}; segment 1 may not drop below its 6.0 m minimum, so it holds
            // at 6.0 and segment 0 absorbs the difference. Sum stays 10.
            double[] result = RoomProportions.PullToTargets(
                new[] { 3.0, 7.0 }, new[] { 0.5, 0.5 }, new[] { 2.0, 6.0 }, new[] { 0.0, 0.0 }, 1.0);

            Assert.Equal(4.0, result[0], 6);
            Assert.Equal(6.0, result[1], 6);
            Assert.Equal(10.0, result[0] + result[1], 6);
        }

        [Fact]
        public void PullToTargets_RespectsMaximumsAndStillPreservesSum()
        {
            // Target {5,5}; segment 0 is capped at 4.0, so it holds and segment 1 takes
            // the slack up to its 8.0 cap. Sum stays 10.
            double[] result = RoomProportions.PullToTargets(
                new[] { 9.0, 1.0 }, new[] { 0.5, 0.5 }, new[] { 0.0, 0.0 }, new[] { 4.0, 8.0 }, 1.0);

            Assert.Equal(4.0, result[0], 6);
            Assert.Equal(6.0, result[1], 6);
            Assert.Equal(10.0, result[0] + result[1], 6);
        }

        [Fact]
        public void PullToTargets_ReturnsCopyWhenNoPositiveSharesOrZeroTotal()
        {
            double[] noShares = RoomProportions.PullToTargets(
                new[] { 4.0, 6.0 }, new[] { 0.0, 0.0 }, new[] { 0.0, 0.0 }, new[] { 0.0, 0.0 }, 1.0);
            Assert.Equal(4.0, noShares[0], 6);
            Assert.Equal(6.0, noShares[1], 6);

            double[] zeroTotal = RoomProportions.PullToTargets(
                new[] { 0.0, 0.0 }, new[] { 0.5, 0.5 }, new[] { 0.0, 0.0 }, new[] { 0.0, 0.0 }, 1.0);
            Assert.Equal(0.0, zeroTotal[0], 6);
            Assert.Equal(0.0, zeroTotal[1], 6);
        }
    }
}

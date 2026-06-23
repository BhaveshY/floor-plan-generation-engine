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
    }
}

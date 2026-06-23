using System.Collections.Generic;
using FloorPlanGeneration.Generation;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    // Phase 3 (architectural-finetuning): the adjacency-match scoring lever. AdjacencyScorer
    // is a pure function of a layout's realized room adjacencies and the owned-data priors.
    public sealed class AdjacencyScorerTests
    {
        private const double Tol = 0.01;

        [Fact]
        public void Score_UsesThePriorWeightForAnAdjacentRoomPair()
        {
            PortfolioPriors priors = PortfolioPriors.Default();
            UnitLayout unit = Unit(Rect("kitchen", 0.0, 0.0, 4.0, 3.0), Rect("living", 4.0, 0.0, 8.0, 3.0));

            double score = AdjacencyScorer.Score(new[] { unit }, priors, Tol);

            Assert.Equal(priors.AdjacencyWeight("kitchen", "living"), score, 6);
        }

        [Fact]
        public void Score_ReturnsNeutralDefaultWhenNoRoomsShareAWall()
        {
            PortfolioPriors priors = PortfolioPriors.Default();
            // A 1.0 m gap between the two rooms: no shared interior wall, so no adjacency.
            UnitLayout unit = Unit(Rect("kitchen", 0.0, 0.0, 4.0, 3.0), Rect("living", 5.0, 0.0, 9.0, 3.0));

            double score = AdjacencyScorer.Score(new[] { unit }, priors, Tol);

            Assert.Equal(priors.AdjacencyWeight("unlisted_a", "unlisted_b"), score, 6);
        }

        [Fact]
        public void Score_RanksPreferredAdjacenciesAboveWeakOnes()
        {
            PortfolioPriors priors = PortfolioPriors.Default();
            UnitLayout strong = Unit(Rect("kitchen", 0.0, 0.0, 4.0, 3.0), Rect("living", 4.0, 0.0, 8.0, 3.0));
            UnitLayout weak = Unit(Rect("bathroom", 0.0, 0.0, 4.0, 3.0), Rect("living", 4.0, 0.0, 8.0, 3.0));

            double strongScore = AdjacencyScorer.Score(new[] { strong }, priors, Tol);
            double weakScore = AdjacencyScorer.Score(new[] { weak }, priors, Tol);

            Assert.True(strongScore > weakScore, "kitchen-living adjacency should outscore bathroom-living");
        }

        [Fact]
        public void Score_AveragesOverAllRealizedAdjacencies()
        {
            PortfolioPriors priors = PortfolioPriors.Default();
            // Three rooms in a row: bedroom|bathroom and bathroom|living are adjacent;
            // bedroom and living do not touch. The score is the mean of the two pairs.
            UnitLayout unit = Unit(
                Rect("bedroom", 0.0, 0.0, 3.0, 3.0),
                Rect("bathroom", 3.0, 0.0, 6.0, 3.0),
                Rect("living", 6.0, 0.0, 9.0, 3.0));

            double expected = (priors.AdjacencyWeight("bedroom", "bathroom") + priors.AdjacencyWeight("bathroom", "living")) / 2.0;
            double score = AdjacencyScorer.Score(new[] { unit }, priors, Tol);

            Assert.Equal(expected, score, 6);
        }

        private static UnitLayout Unit(params RoomLayout[] rooms)
        {
            return new UnitLayout { Rooms = new List<RoomLayout>(rooms) };
        }

        private static RoomLayout Rect(string roomType, double minX, double minY, double maxX, double maxY)
        {
            return new RoomLayout
            {
                RoomType = roomType,
                Polygon = new PolygonInput
                {
                    Id = roomType,
                    Points = new List<Point2>
                    {
                        new Point2(minX, minY),
                        new Point2(maxX, minY),
                        new Point2(maxX, maxY),
                        new Point2(minX, maxY)
                    }
                }
            };
        }
    }
}

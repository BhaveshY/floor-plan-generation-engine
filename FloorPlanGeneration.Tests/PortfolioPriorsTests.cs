using System;
using System.Linq;
using FloorPlanGeneration.Generation;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    // Phase 3 (architectural-finetuning): owned-data priors ship as a typed static
    // table (the FurnitureDefaults pattern). These tests pin the seed table's shape
    // and the lookup contracts the proportion-pull and adjacency-scoring levers rely on.
    public sealed class PortfolioPriorsTests
    {
        [Fact]
        public void Default_ExposesUnitAreaMeansAndFallsBackForUnknownType()
        {
            PortfolioPriors priors = PortfolioPriors.Default();

            Assert.True(priors.UnitAreaMean("one_bed", 0.0) > 0.0);
            Assert.True(priors.UnitAreaMean("two_bed", 0.0) > priors.UnitAreaMean("one_bed", 0.0));
            Assert.Equal(99.0, priors.UnitAreaMean("penthouse", 99.0), 6);
        }

        [Fact]
        public void Default_FacadeSharesAreOrderedAndSumToOneForKnownTypes()
        {
            PortfolioPriors priors = PortfolioPriors.Default();

            System.Collections.Generic.IReadOnlyList<FacadeShare> twoBed = priors.FacadeShares("two_bed");
            Assert.Equal(3, twoBed.Count);
            Assert.Equal("bedroom", twoBed[0].RoomType);
            Assert.Equal("bedroom", twoBed[1].RoomType);
            Assert.Equal("living", twoBed[2].RoomType);
            Assert.Equal(1.0, twoBed.Sum(s => s.Share), 6);

            System.Collections.Generic.IReadOnlyList<FacadeShare> oneBed = priors.FacadeShares("one_bed");
            Assert.Equal(2, oneBed.Count);
            Assert.Equal(1.0, oneBed.Sum(s => s.Share), 6);

            // A unit type whose facade band is a single room has no split to bias.
            Assert.Empty(priors.FacadeShares("studio"));
        }

        [Fact]
        public void Default_AdjacencyIsSymmetricCaseInsensitiveAndDefaultsForUnknownPairs()
        {
            PortfolioPriors priors = PortfolioPriors.Default();

            double kitchenLiving = priors.AdjacencyWeight("kitchen", "living");
            Assert.True(kitchenLiving > 0.5, "kitchen-living should be a preferred adjacency");
            Assert.Equal(kitchenLiving, priors.AdjacencyWeight("LIVING", "Kitchen"), 6);

            double bathLiving = priors.AdjacencyWeight("bathroom", "living");
            Assert.True(bathLiving < kitchenLiving, "bathroom-living is weaker than kitchen-living");

            // Unlisted pair returns the neutral default in (0,1).
            double unknown = priors.AdjacencyWeight("foo", "bar");
            Assert.InRange(unknown, 0.0, 1.0);
            Assert.Equal(unknown, priors.AdjacencyWeight("bar", "foo"), 6);
        }

        [Fact]
        public void Default_ReportsCurrentVersion()
        {
            Assert.Equal(PortfolioPriors.CurrentVersion, PortfolioPriors.Default().Version);
        }
    }
}

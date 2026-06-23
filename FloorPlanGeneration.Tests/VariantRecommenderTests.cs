using System.Collections.Generic;
using System.Linq;
using FloorPlanGeneration.Generation;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    // Phase 4 (architectural-finetuning): the recommender surfaces an explicit, explainable
    // recommendation over the engine's already-sorted variants. VariantRecommender is pure.
    public sealed class VariantRecommenderTests
    {
        [Fact]
        public void Recommend_SurfacesTheTopPassingVariant()
        {
            List<LayoutVariant> variants = new List<LayoutVariant>
            {
                Variant("variant-01", score: 0.90, eff: 0.9, ngr: 0.8, mix: 0.7, passed: false),
                Variant("variant-02", score: 0.80, eff: 0.7, ngr: 0.9, mix: 0.8, passed: true),
                Variant("variant-03", score: 0.70, eff: 0.6, ngr: 0.6, mix: 0.6, passed: true),
            };

            VariantRecommendation recommendation = VariantRecommender.Recommend(variants);

            // variant-01 outscores all but failed validation, so the first PASSING one wins.
            Assert.Equal("variant-02", recommendation.RecommendedVariantId);
        }

        [Fact]
        public void Recommend_FallsBackToTheFirstVariantWhenNonePass()
        {
            List<LayoutVariant> variants = new List<LayoutVariant>
            {
                Variant("variant-01", score: 0.50, eff: 0.5, ngr: 0.5, mix: 0.5, passed: false),
                Variant("variant-02", score: 0.40, eff: 0.4, ngr: 0.4, mix: 0.4, passed: false),
            };

            VariantRecommendation recommendation = VariantRecommender.Recommend(variants);

            Assert.Equal("variant-01", recommendation.RecommendedVariantId);
        }

        [Fact]
        public void Recommend_RanksEveryVariantOneBasedInOrderCarryingScores()
        {
            List<LayoutVariant> variants = new List<LayoutVariant>
            {
                Variant("variant-01", score: 0.90, eff: 0.9, ngr: 0.8, mix: 0.7, passed: true),
                Variant("variant-02", score: 0.80, eff: 0.7, ngr: 0.9, mix: 0.8, passed: true),
            };

            VariantRecommendation recommendation = VariantRecommender.Recommend(variants);

            Assert.Equal(2, recommendation.Ranking.Count);
            Assert.Equal("variant-01", recommendation.Ranking[0].VariantId);
            Assert.Equal(1, recommendation.Ranking[0].Rank);
            Assert.Equal(0.90, recommendation.Ranking[0].Score, 6);
            Assert.Equal(2, recommendation.Ranking[1].Rank);
            Assert.True(recommendation.Ranking[0].Passed);
        }

        [Fact]
        public void Recommend_LabelsTheMetricLeaders()
        {
            List<LayoutVariant> variants = new List<LayoutVariant>
            {
                Variant("variant-01", score: 0.90, eff: 0.95, ngr: 0.50, mix: 0.50, passed: true),
                Variant("variant-02", score: 0.80, eff: 0.50, ngr: 0.95, mix: 0.95, passed: true),
            };

            VariantRecommendation recommendation = VariantRecommender.Recommend(variants);

            VariantRanking first = recommendation.Ranking.Single(r => r.VariantId == "variant-01");
            VariantRanking second = recommendation.Ranking.Single(r => r.VariantId == "variant-02");
            Assert.Contains(first.Highlights, h => h.Contains("efficiency"));
            Assert.Contains(second.Highlights, h => h.Contains("net"));
            Assert.Contains(second.Highlights, h => h.Contains("mix"));
            Assert.DoesNotContain(first.Highlights, h => h.Contains("net"));
        }

        [Fact]
        public void Recommend_RationaleNamesTheRecommendedVariant()
        {
            List<LayoutVariant> variants = new List<LayoutVariant>
            {
                Variant("variant-01", score: 0.90, eff: 0.9, ngr: 0.8, mix: 0.7, passed: true),
            };

            VariantRecommendation recommendation = VariantRecommender.Recommend(variants);

            Assert.False(string.IsNullOrWhiteSpace(recommendation.Rationale));
            Assert.Contains("variant-01", recommendation.Rationale);
        }

        private static LayoutVariant Variant(string id, double score, double eff, double ngr, double mix, bool passed)
        {
            return new LayoutVariant
            {
                VariantId = id,
                Metrics = new VariantMetrics { Score = score, Efficiency = eff, NetGrossRatio = ngr, UnitMixMatch = mix },
                Validation = new ValidationReport { Passed = passed }
            };
        }
    }
}

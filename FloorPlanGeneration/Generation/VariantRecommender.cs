using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FloorPlanGeneration.Schema;

namespace FloorPlanGeneration.Generation
{
    /// <summary>
    /// Surfaces an explicit, explainable recommendation over the engine's already-sorted
    /// variants (architectural-finetuning Phase 4). The recommended variant is the
    /// top-ranked one that passes validation, falling back to the first variant when none
    /// pass; each variant is ranked 1..N in the given order, labelled with the metric
    /// criteria it leads on. Pure and deterministic — a function of the variants' metrics
    /// only, with no randomness and no mutation of the variants. Consumed by the engine
    /// only when <see cref="GenerationSettings.RecommendVariant"/> is set.
    /// </summary>
    public static class VariantRecommender
    {
        // A variant whose metric is within this tolerance of the set maximum is a co-leader;
        // ties deterministically all earn the highlight.
        private const double LeaderTolerance = 1e-9;

        public static VariantRecommendation Recommend(IReadOnlyList<LayoutVariant> rankedVariants)
        {
            VariantRecommendation recommendation = new VariantRecommendation();
            if (rankedVariants == null || rankedVariants.Count == 0)
            {
                return recommendation;
            }

            double maxEfficiency = rankedVariants.Max(variant => Metrics(variant).Efficiency);
            double maxNetGross = rankedVariants.Max(variant => Metrics(variant).NetGrossRatio);
            double maxUnitMix = rankedVariants.Max(variant => Metrics(variant).UnitMixMatch);

            for (int i = 0; i < rankedVariants.Count; i++)
            {
                LayoutVariant variant = rankedVariants[i];
                VariantMetrics metrics = Metrics(variant);
                List<string> highlights = new List<string>();
                if (metrics.Efficiency >= maxEfficiency - LeaderTolerance)
                {
                    highlights.Add("highest efficiency");
                }

                if (metrics.NetGrossRatio >= maxNetGross - LeaderTolerance)
                {
                    highlights.Add("best net-to-gross ratio");
                }

                if (metrics.UnitMixMatch >= maxUnitMix - LeaderTolerance)
                {
                    highlights.Add("best unit-mix match");
                }

                recommendation.Ranking.Add(new VariantRanking
                {
                    VariantId = variant.VariantId,
                    Rank = i + 1,
                    Score = metrics.Score,
                    Passed = Passed(variant),
                    Highlights = highlights
                });
            }

            LayoutVariant recommended = rankedVariants.FirstOrDefault(Passed) ?? rankedVariants[0];
            recommendation.RecommendedVariantId = recommended.VariantId;
            recommendation.Rationale = BuildRationale(recommendation, recommended, rankedVariants);
            return recommendation;
        }

        private static string BuildRationale(
            VariantRecommendation recommendation,
            LayoutVariant recommended,
            IReadOnlyList<LayoutVariant> variants)
        {
            VariantRanking ranking = recommendation.Ranking.First(
                entry => string.Equals(entry.VariantId, recommended.VariantId, StringComparison.Ordinal));
            string score = ranking.Score.ToString("0.000", CultureInfo.InvariantCulture);
            string leads = ranking.Highlights.Count > 0
                ? " It leads on " + string.Join(", ", ranking.Highlights) + "."
                : string.Empty;
            string count = variants.Count == 1 ? "1 variant" : variants.Count.ToString(CultureInfo.InvariantCulture) + " variants";

            if (variants.Any(Passed))
            {
                return "Recommends " + recommended.VariantId + " (score " + score
                    + "), the top-ranked variant that passes validation out of " + count + " evaluated." + leads;
            }

            return "No variant passes validation; recommends " + recommended.VariantId + " (score " + score
                + ") as the best-scoring of " + count + " evaluated." + leads;
        }

        private static VariantMetrics Metrics(LayoutVariant variant)
        {
            return variant.Metrics ?? new VariantMetrics();
        }

        private static bool Passed(LayoutVariant variant)
        {
            return variant.Validation != null && variant.Validation.Passed;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorPlanGeneration;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    // Phase 4 (architectural-finetuning): on-path behaviour of recommendVariant. The
    // off-path (flag default off) byte-identity is covered by the frozen-hash and
    // golden-contracts tests; here we pin that the flag stays null+omitted when off and
    // attaches a deterministic recommendation over every variant when on.
    public sealed class VariantRecommenderEngineTests
    {
        [Fact]
        public void RecommendVariant_Off_LeavesRecommendationNullAndOmitsItFromJson()
        {
            EngineOutput output = Generate(seed: 909, recommend: false);

            Assert.Null(output.Recommendation);
            string json = JsonSerializer.Serialize(output, JsonOptions());
            Assert.DoesNotContain("\"recommendation\"", json);
        }

        [Fact]
        public void RecommendVariant_On_AttachesRecommendationOverEveryVariant()
        {
            EngineOutput output = Generate(seed: 909, recommend: true);

            Assert.NotNull(output.Recommendation);
            Assert.Equal(output.Variants.Count, output.Recommendation.Ranking.Count);

            string expected = output.Variants.FirstOrDefault(v => v.Validation.Passed)?.VariantId
                ?? output.Variants[0].VariantId;
            Assert.Equal(expected, output.Recommendation.RecommendedVariantId);

            for (int i = 0; i < output.Variants.Count; i++)
            {
                Assert.Equal(output.Variants[i].VariantId, output.Recommendation.Ranking[i].VariantId);
                Assert.Equal(i + 1, output.Recommendation.Ranking[i].Rank);
            }

            Assert.Contains(
                output.Recommendation.Ranking,
                r => string.Equals(r.VariantId, output.Recommendation.RecommendedVariantId, StringComparison.Ordinal));
        }

        [Fact]
        public void RecommendVariant_On_SameSeedReproducesIdenticalRecommendation()
        {
            EngineOutput left = Generate(seed: 13579, recommend: true);
            EngineOutput right = Generate(seed: 13579, recommend: true);

            string leftJson = JsonSerializer.Serialize(left.Recommendation, JsonOptions());
            string rightJson = JsonSerializer.Serialize(right.Recommendation, JsonOptions());
            Assert.Equal(leftJson, rightJson);
        }

        [Fact]
        public void RecommendVariant_On_SerializesRecommendationKeyThatRoundTrips()
        {
            EngineOutput output = Generate(seed: 909, recommend: true);

            string json = JsonSerializer.Serialize(output, JsonOptions());
            Assert.Contains("\"recommendation\"", json);

            EngineOutput roundTripped = JsonSerializer.Deserialize<EngineOutput>(json, JsonOptions());
            Assert.NotNull(roundTripped.Recommendation);
            Assert.Equal(output.Recommendation.RecommendedVariantId, roundTripped.Recommendation.RecommendedVariantId);
        }

        private static EngineOutput Generate(int seed, bool recommend)
        {
            EngineInput input = LoadSample("rectangular-core-input.json");
            input.Project.Seed = seed;
            input.GenerationSettings.VariantCount = 5;
            input.GenerationSettings.RecommendVariant = recommend;
            return new FloorPlanEngine().Generate(input);
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

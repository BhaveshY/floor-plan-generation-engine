using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorPlanGeneration;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    // Phase 5 (architectural-finetuning): on-path behaviour of critiqueVariants. Off-path
    // byte-identity is covered by the frozen-hash and golden-contracts tests; here we pin
    // that the flag stays null+omitted when off and attaches a deterministic critique over
    // every variant when on.
    public sealed class VariantCriticEngineTests
    {
        [Fact]
        public void CritiqueVariants_Off_LeavesCritiqueNullAndOmitsItFromJson()
        {
            EngineOutput output = Generate(seed: 5150, critique: false);

            Assert.Null(output.Critique);
            string json = JsonSerializer.Serialize(output, JsonOptions());
            Assert.DoesNotContain("\"critique\"", json);
        }

        [Fact]
        public void CritiqueVariants_On_AttachesCritiqueOverEveryVariant()
        {
            EngineOutput output = Generate(seed: 5150, critique: true);

            Assert.NotNull(output.Critique);
            Assert.Equal(output.Variants.Count, output.Critique.Variants.Count);
            for (int i = 0; i < output.Variants.Count; i++)
            {
                Assert.Equal(output.Variants[i].VariantId, output.Critique.Variants[i].VariantId);
            }

            string[] variantIds = output.Variants.Select(v => v.VariantId).ToArray();
            Assert.All(output.Critique.FlaggedVariantIds, id => Assert.Contains(id, variantIds));

            // A flagged variant has at least one finding; a non-flagged one has none.
            foreach (VariantCritique assessment in output.Critique.Variants)
            {
                bool flagged = output.Critique.FlaggedVariantIds.Contains(assessment.VariantId);
                Assert.Equal(flagged, assessment.Findings.Count > 0);
            }
        }

        [Fact]
        public void CritiqueVariants_On_SameSeedReproducesIdenticalCritique()
        {
            EngineOutput left = Generate(seed: 24680, critique: true);
            EngineOutput right = Generate(seed: 24680, critique: true);

            string leftJson = JsonSerializer.Serialize(left.Critique, JsonOptions());
            string rightJson = JsonSerializer.Serialize(right.Critique, JsonOptions());
            Assert.Equal(leftJson, rightJson);
        }

        [Fact]
        public void CritiqueVariants_On_SerializesCritiqueKeyThatRoundTrips()
        {
            EngineOutput output = Generate(seed: 5150, critique: true);

            string json = JsonSerializer.Serialize(output, JsonOptions());
            Assert.Contains("\"critique\"", json);

            EngineOutput roundTripped = JsonSerializer.Deserialize<EngineOutput>(json, JsonOptions());
            Assert.NotNull(roundTripped.Critique);
            Assert.Equal(output.Critique.Variants.Count, roundTripped.Critique.Variants.Count);
        }

        private static EngineOutput Generate(int seed, bool critique)
        {
            EngineInput input = LoadSample("rectangular-core-input.json");
            input.Project.Seed = seed;
            input.GenerationSettings.VariantCount = 5;
            input.GenerationSettings.CritiqueVariants = critique;
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

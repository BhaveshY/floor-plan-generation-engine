using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorPlanGeneration;
using FloorPlanGeneration.Generation;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    // Phase 3 (architectural-finetuning): on-path behaviour of usePortfolioPriors.
    // Lever A pulls the facade-band room split toward owned-data proportion priors;
    // both levers are exercised here. The off-path (flag default off) is covered by
    // the frozen byte-identity hash and golden-contracts tests.
    public sealed class PortfolioPriorsEngineTests
    {
        [Fact]
        public void UsePortfolioPriors_PullsOneBedFacadeSplitTowardThePrior()
        {
            // Enabling the flag consumes no randomness (the pull is deterministic), so a
            // unit with a given id is the same bay on both paths — only its internal
            // bedroom:living split differs, which lets us compare per unit.
            double target = PortfolioPriors.Default().FacadeShares("one_bed")[0].Share;

            EngineOutput off = Generate(seed: 4242, usePriors: false);
            EngineOutput on = Generate(seed: 4242, usePriors: true);
            Assert.Equal("succeeded", on.Status);

            int compared = 0;
            int moved = 0;
            foreach (LayoutVariant onVar in on.Variants)
            {
                LayoutVariant offVar = off.Variants.Single(v => string.Equals(v.VariantId, onVar.VariantId, StringComparison.Ordinal));
                foreach (UnitLayout onUnit in onVar.Units.Where(u => string.Equals(u.Type, "one_bed", StringComparison.OrdinalIgnoreCase)))
                {
                    UnitLayout offUnit = offVar.Units.Single(u => string.Equals(u.Id, onUnit.Id, StringComparison.Ordinal));
                    double onShare = BedroomFacadeShare(onUnit);
                    double offShare = BedroomFacadeShare(offUnit);
                    if (double.IsNaN(onShare) || double.IsNaN(offShare))
                    {
                        continue;
                    }

                    compared++;
                    Assert.True(
                        Math.Abs(onShare - target) <= Math.Abs(offShare - target) + 1e-9,
                        "priors should pull the one_bed bedroom share toward the prior (" + target + "): off=" + offShare + " on=" + onShare);
                    if (Math.Abs(onShare - offShare) > 1e-6)
                    {
                        moved++;
                    }
                }
            }

            Assert.True(compared > 0, "no one_bed units were available to compare");
            Assert.True(moved > 0, "the proportion pull never changed any split — the test would be vacuous");
        }

        [Fact]
        public void UsePortfolioPriors_SameSeedReproducesIdenticalVariants()
        {
            EngineOutput left = Generate(seed: 31415, usePriors: true);
            EngineOutput right = Generate(seed: 31415, usePriors: true);

            Assert.Equal("succeeded", left.Status);
            Assert.Equal(Signatures(left), Signatures(right));
        }

        [Fact]
        public void UsePortfolioPriors_ProducesValidVariants()
        {
            EngineOutput output = Generate(seed: 20260623, usePriors: true);

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, variant => Assert.True(variant.Validation.Passed));
        }

        [Fact]
        public void UsePortfolioPriors_AdjacencyTermDrivesScoreWhenItIsTheSoleWeight()
        {
            // Zero every other scoring weight so a passing variant's normalised score is
            // exactly the adjacency-match term — proving the term is actually wired in.
            EngineInput input = LoadSample("rectangular-core-input.json");
            input.Project.Seed = 777;
            input.GenerationSettings.VariantCount = 4;
            input.Rules.UsePortfolioPriors = true;
            input.GenerationSettings.ScoringWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "efficiency", 0.0 },
                { "netGrossRatio", 0.0 },
                { "unitMixMatch", 0.0 },
                { "unitQuality", 0.0 },
                { "daylight", 0.0 },
                { "adjacencyMatch", 1.0 }
            };

            EngineOutput output = new FloorPlanEngine().Generate(input);
            Assert.Equal("succeeded", output.Status);

            PortfolioPriors priors = PortfolioPriors.Default();
            int verified = 0;
            foreach (LayoutVariant variant in output.Variants.Where(v => v.Validation.Passed))
            {
                double expected = AdjacencyScorer.Score(variant.Units, priors, input.Project.Tolerance);
                Assert.Equal(Math.Round(expected, 4), variant.Metrics.Score, 3);
                verified++;
            }

            Assert.True(verified > 0, "no passing variants to check the adjacency wiring against");
        }

        private static double BedroomFacadeShare(UnitLayout unit)
        {
            double bedroom = unit.Rooms.Where(r => string.Equals(r.RoomType, "bedroom", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Area);
            double living = unit.Rooms.Where(r => string.Equals(r.RoomType, "living", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Area);
            double facade = bedroom + living;
            return facade <= 0.0 ? double.NaN : bedroom / facade;
        }

        private static EngineOutput Generate(int seed, bool usePriors)
        {
            EngineInput input = LoadSample("rectangular-core-input.json");
            input.Project.Seed = seed;
            input.GenerationSettings.VariantCount = 5;
            input.Rules.UsePortfolioPriors = usePriors;
            return new FloorPlanEngine().Generate(input);
        }

        private static IEnumerable<string> Signatures(EngineOutput output)
        {
            return output.Variants.Select(v => string.Join(
                "|",
                v.VariantId,
                v.Metrics.Score.ToString("0.0000", CultureInfo.InvariantCulture),
                v.Units.Count.ToString(CultureInfo.InvariantCulture),
                v.Rooms.Count.ToString(CultureInfo.InvariantCulture),
                v.Rooms.Sum(r => r.Area).ToString("0.0000", CultureInfo.InvariantCulture)));
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

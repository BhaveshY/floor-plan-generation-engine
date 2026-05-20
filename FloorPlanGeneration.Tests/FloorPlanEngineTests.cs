using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorPlanGeneration;
using FloorPlanGeneration.Cli;
using FloorPlanGeneration.Geometry;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class FloorPlanEngineTests
    {
        [Fact]
        public void RectangularInput_ReturnsDeterministicRankedVariants()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 1234, variantCount: 4));

            Assert.Equal("succeeded", output.Status);
            Assert.NotNull(output.Metadata);
            Assert.Equal("1.1", output.Metadata.SchemaVersion);
            Assert.Equal(1234, output.Metadata.Seed);
            Assert.Equal(4, output.Metadata.GenerationSettings.VariantCount);
            Assert.Equal("balanced", output.Metadata.GenerationSettings.Strictness);
            Assert.Equal("FP::Generated::Units", output.Metadata.Layers["units"]);
            Assert.Equal("FP::Generated::Diagnostics", output.Metadata.Layers["diagnostics"]);
            Assert.Equal(648.0, output.Metadata.Floorplate.GrossArea);
            Assert.Equal(648.0, output.Metadata.Floorplate.UsableArea);
            Assert.Equal(36.0, output.Metadata.Floorplate.Bounds.Width);
            Assert.Equal(18.0, output.Metadata.Floorplate.Bounds.Height);
            Assert.Equal(4, output.Variants.Count);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            Assert.All(output.Variants, v => Assert.NotEmpty(v.Units));
            Assert.All(output.Variants.SelectMany(v => v.Units), u =>
            {
                Assert.Equal("FP::Generated::Units", u.Layer);
                Assert.True(u.Bounds.Area > 0.0);
            });
            Assert.All(output.Variants.SelectMany(v => v.Rooms), r =>
            {
                Assert.Equal("FP::Generated::Rooms", r.Layer);
                Assert.True(r.Bounds.Area > 0.0);
            });
            Assert.All(output.Variants.SelectMany(v => v.Corridors), c =>
            {
                Assert.Equal("FP::Generated::Corridors", c.Layer);
                Assert.True(c.Bounds.Area > 0.0);
            });

            List<double> scores = output.Variants.Select(v => v.Metrics.Score).ToList();
            Assert.Equal(scores.OrderByDescending(s => s).ToList(), scores);

            EngineOutput repeated = new FloorPlanEngine().Generate(RectangularInput(seed: 1234, variantCount: 4));
            Assert.Equal(Signatures(output), Signatures(repeated));
        }

        [Fact]
        public void GeneratedElementsExposeStableExternalIdsAndLayerValidationChecks()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 20260519, variantCount: 3));

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, variant =>
            {
                string variantPrefix = "fp://rectangular-test/variants/" + variant.VariantId;
                Assert.Equal(variantPrefix, variant.ExternalId);
                Assert.Contains(variant.Validation.Checks, c => c.Name == "stable_external_ids" && c.Passed);
                Assert.Contains(variant.Validation.Checks, c => c.Name == "generated_layers" && c.Passed);

                Assert.All(variant.Units, unit =>
                {
                    Assert.StartsWith(variantPrefix + "/units/", unit.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedUnits, unit.Layer);
                });
                Assert.All(variant.Rooms, room =>
                {
                    Assert.StartsWith(variantPrefix + "/rooms/", room.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedRooms, room.Layer);
                });
                Assert.All(variant.Corridors, corridor =>
                {
                    Assert.StartsWith(variantPrefix + "/corridors/", corridor.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedCorridors, corridor.Layer);
                });
                Assert.All(variant.Walls, wall =>
                {
                    Assert.StartsWith(variantPrefix + "/walls/", wall.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedWalls, wall.Layer);
                });
                Assert.All(variant.DoorsOpenings, door =>
                {
                    Assert.StartsWith(variantPrefix + "/doors/", door.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedDoors, door.Layer);
                });
                Assert.All(variant.Labels, label =>
                {
                    Assert.StartsWith(variantPrefix + "/labels/", label.ExternalId, StringComparison.Ordinal);
                    Assert.Equal(LayerNames.GeneratedLabels, label.Layer);
                });
            });

            EngineOutput repeated = new FloorPlanEngine().Generate(RectangularInput(seed: 20260519, variantCount: 3));
            Assert.Equal(
                output.Variants.SelectMany(v => v.Units.Select(u => u.ExternalId)),
                repeated.Variants.SelectMany(v => v.Units.Select(u => u.ExternalId)));
        }

        [Fact]
        public void DoorOpeningsReferenceHostWallsAtTheirActualLocations()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 20260519, variantCount: 3));

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, variant =>
            {
                Assert.Contains(variant.Validation.Checks, c => c.Name == "door_host_wall_exists" && c.Passed);
                Assert.Contains(variant.Validation.Checks, c => c.Name == "door_connects_known_spaces" && c.Passed);
                Dictionary<string, WallLayout> walls = variant.Walls.ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);

                Assert.All(variant.DoorsOpenings, door =>
                {
                    Assert.True(walls.ContainsKey(door.HostWall), "Missing host wall " + door.HostWall + " for " + door.Id);
                    WallLayout host = walls[door.HostWall];
                    Assert.True(
                        GeometryPredicates.OnSegment(host.Centerline.Start, host.Centerline.End, door.Location, 0.01),
                        door.Id + " location is not on " + door.HostWall);
                });
            });
        }

        [Fact]
        public void RoomPartitionWallsDoNotDuplicateUnitExteriorEdges()
        {
            EngineOutput output = new FloorPlanEngine().Generate(RectangularInput(seed: 1441, variantCount: 2));

            Assert.Equal("succeeded", output.Status);
            Assert.All(output.Variants, variant =>
            {
                List<LineSegment2> unitExteriorEdges = variant.Units
                    .SelectMany(u => ToPolygon(u.Polygon).Edges())
                    .ToList();
                List<WallLayout> partitions = variant.Walls
                    .Where(w => string.Equals(w.LayerType, "room_partition", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                List<string> partitionKeys = partitions.Select(w => SegmentKey(w.Centerline)).ToList();

                Assert.Equal(partitionKeys.Count, partitionKeys.Distinct(StringComparer.Ordinal).Count());
                Assert.All(partitions, wall =>
                {
                    LineSegment2 segment = new LineSegment2(wall.Centerline.Start, wall.Centerline.End);
                    Assert.DoesNotContain(unitExteriorEdges, edge => GeometryPredicates.SharedSegmentLength(segment, edge, 0.01) > 0.01);
                });
            });
        }

        [Fact]
        public void SameSeed_ProducesSameVariantIdsScoresAndLayoutCounts()
        {
            EngineOutput left = new FloorPlanEngine().Generate(RectangularInput(seed: 8128, variantCount: 5));
            EngineOutput right = new FloorPlanEngine().Generate(RectangularInput(seed: 8128, variantCount: 5));

            Assert.Equal(Signatures(left), Signatures(right));
        }

        [Fact]
        public void ValidateInput_ReturnsDryRunMetadataWithoutGeneratingVariants()
        {
            EngineOutput output = new FloorPlanEngine().Validate(RectangularInput(seed: 44, variantCount: 3));

            Assert.Equal("validated", output.Status);
            Assert.Empty(output.Variants);
            Assert.NotNull(output.Metadata);
            Assert.Equal("rectangular-test", output.ProjectId);
            Assert.Equal(44, output.Metadata.Seed);
            Assert.Equal(3, output.Metadata.GenerationSettings.VariantCount);
            Assert.Equal(648.0, output.Metadata.Floorplate.GrossArea);
            Assert.Equal(0.0, output.Metadata.Floorplate.BlockingFixedElementArea);
            Assert.DoesNotContain(output.Diagnostics, d => d.Severity == "error");
            Assert.Contains(output.Diagnostics, d => d.Code == "input.validated" && d.Severity == "info");
        }

        [Fact]
        public void InvalidInputContract_FailsBeforeCandidateGeneration()
        {
            EngineInput input = RectangularInput(seed: 5, variantCount: 2);
            input.GenerationSettings.VariantCount = 0;
            input.GenerationSettings.Strictness = "chaotic";
            input.Program.TargetUnitTypes[0].MaxArea = input.Program.TargetUnitTypes[0].MinArea - 1.0;

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.Empty(output.Variants);
            Assert.Contains(output.Diagnostics, d => d.Code == "input.invalid_variant_count" && d.Severity == "error");
            Assert.Contains(output.Diagnostics, d => d.Code == "input.invalid_strictness" && d.Severity == "error");
            Assert.Contains(output.Diagnostics, d => d.Code == "input.invalid_unit_type_area_range" && d.Severity == "error");
        }

        [Fact]
        public void SelfIntersectingBoundary_ReturnsFailedDiagnosticsInsteadOfFakePlan()
        {
            EngineInput input = RectangularInput(seed: 1, variantCount: 3);
            input.Floorplate.Outer.Id = "bowtie";
            input.Floorplate.Outer.Points = new List<Point2>
            {
                new Point2(0, 0),
                new Point2(10, 10),
                new Point2(0, 10),
                new Point2(10, 0)
            };

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.Empty(output.Variants);
            Assert.Contains(output.Diagnostics, d => d.Code == "geometry.self_intersection" && d.Severity == "error");
        }

        [Fact]
        public void StrictInfeasibleUnitMix_ReportsValidationFailure()
        {
            EngineInput input = RectangularInput(seed: 7, variantCount: 3);
            input.GenerationSettings.Strictness = "strict";
            input.Program.TargetUnitTypes = new List<UnitTypeTarget>
            {
                new UnitTypeTarget
                {
                    Type = "studio",
                    MinArea = 26.0,
                    MaxArea = 48.0,
                    TargetCount = 99,
                    Weight = 1.0
                }
            };

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.NotEmpty(output.Variants);
            Assert.All(output.Variants, v => Assert.False(v.Validation.Passed));
            Assert.Contains(output.Diagnostics, d => d.Code == "validation.strict_unit_mix" && d.Severity == "error");
            Assert.Contains(output.Variants.SelectMany(v => v.Validation.Checks), c => c.Name == "strict_unit_mix" && !c.Passed);
        }

        [Fact]
        public void LShapedInput_SplitsUsableBandsAroundCoreAndRemainsDeterministic()
        {
            EngineOutput output = new FloorPlanEngine().Generate(LShapedInput(seed: 5601, variantCount: 5));

            Assert.Equal("succeeded", output.Status);
            Assert.Equal(5, output.Variants.Count);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            Assert.All(output.Variants, v => Assert.Contains(v.Units, u => u.Id.StartsWith("unit-south-", System.StringComparison.OrdinalIgnoreCase)));
            Assert.All(output.Variants, v => Assert.Contains(v.Units, u => u.Id.StartsWith("unit-north-", System.StringComparison.OrdinalIgnoreCase)));
            Assert.All(output.Variants, v => Assert.DoesNotContain(v.Diagnostics, d => d.Code == "generation.unit_bay_rejected"));

            EngineOutput repeated = new FloorPlanEngine().Generate(LShapedInput(seed: 5601, variantCount: 5));
            Assert.Equal(Signatures(output), Signatures(repeated));
        }

        [Fact]
        public void ModeratelyIrregularInput_GeneratesValidDeterministicVariants()
        {
            EngineOutput output = new FloorPlanEngine().Generate(ModeratelyIrregularInput(seed: 9901, variantCount: 4));

            Assert.Equal("succeeded", output.Status);
            Assert.Equal(4, output.Variants.Count);
            Assert.Equal(1588.0, output.Metadata.Floorplate.GrossArea);
            Assert.Equal(36.0, output.Metadata.Floorplate.BlockingFixedElementArea);
            Assert.Equal(1552.0, output.Metadata.Floorplate.UsableArea);
            Assert.All(output.Variants, v => Assert.True(v.Validation.Passed));
            Assert.All(output.Variants, v => Assert.Contains(v.Corridors[0].Connections, c => c == "core-ir1"));
            Assert.All(output.Variants, v => Assert.True(v.Units.Count >= 8));
            Assert.All(output.Variants.SelectMany(v => v.Units), u => Assert.True(u.Bounds.Area > 0.0));

            EngineOutput repeated = new FloorPlanEngine().Generate(ModeratelyIrregularInput(seed: 9901, variantCount: 4));
            Assert.Equal(Signatures(output), Signatures(repeated));
        }

        [Fact]
        public void CliValidateOnly_ReturnsValidatedJsonWithoutVariants()
        {
            string outputPath = Path.Combine(Path.GetTempPath(), "floor-plan-validate-" + System.Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".json");
            string inputPath = Path.Combine(RepositoryRoot(), "samples", "floor-plan-generation", "rectangular-core-input.json");
            StringWriter stderr = new StringWriter(CultureInfo.InvariantCulture);

            try
            {
                int exitCode = CliApplication.Run(
                    new[] { "--input", inputPath, "--output", outputPath, "--validate-only", "--summary" },
                    TextReader.Null,
                    TextWriter.Null,
                    stderr);

                Assert.Equal(0, exitCode);
                EngineOutput output = JsonSerializer.Deserialize<EngineOutput>(File.ReadAllText(outputPath), JsonOptions());
                Assert.Equal("validated", output.Status);
                Assert.Empty(output.Variants);
                Assert.Equal("rectangular-core-sample", output.ProjectId);
                Assert.NotNull(output.Metadata);
                Assert.Equal(20260519, output.Metadata.Seed);
                Assert.Contains("status=validated", stderr.ToString());
            }
            finally
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }

        [Fact]
        public void CliListSamples_PrintsFriendlySampleNamesWithoutReadingInput()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new[] { "--list-samples" },
                new ThrowingTextReader(),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(0, exitCode);
            Assert.Contains("rectangular-core", stdout.ToString());
            Assert.Contains("moderately-irregular-core", stdout.ToString());
        }

        [Fact]
        public void CliAiManifest_ReturnsStructuredAutomationContractWithoutReadingInput()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new[] { "--ai-manifest" },
                new ThrowingTextReader(),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(0, exitCode);
            using JsonDocument manifest = JsonDocument.Parse(stdout.ToString());
            Assert.Equal("floorplan-gen", manifest.RootElement.GetProperty("name").GetString());
            Assert.Contains(
                manifest.RootElement.GetProperty("samples").EnumerateArray(),
                sample => sample.GetProperty("name").GetString() == "rectangular-core");
            Assert.Equal(
                "https://bhaveshy.github.io/floor-plan-generation-engine/schemas/1.1/floor-plan-engine-input.schema.json",
                manifest.RootElement.GetProperty("schemas").GetProperty("input").GetString());
            Assert.Contains(
                manifest.RootElement.GetProperty("automationNotes").EnumerateArray(),
                note => note.GetString().Contains("--validate-only", StringComparison.Ordinal));
        }

        [Fact]
        public void CliRejectsVariantOverrideOutsideAutomationContract()
        {
            int exitCode = CliApplication.Run(
                new[] { "--sample", "rectangular-core", "--variants", "21" },
                new ThrowingTextReader(),
                new StringWriter(CultureInfo.InvariantCulture),
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(64, exitCode);
        }

        [Fact]
        public void CliSample_GeneratesOutputWithoutManualInputPath()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new[] { "--sample", "rectangular-core", "--variants", "1" },
                new ThrowingTextReader(),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(0, exitCode);
            EngineOutput output = JsonSerializer.Deserialize<EngineOutput>(stdout.ToString(), JsonOptions());
            Assert.Equal("succeeded", output.Status);
            Assert.Equal("rectangular-core-sample", output.ProjectId);
            Assert.Single(output.Variants);
        }

        [Fact]
        public void CliWriteSample_WritesStarterJsonWithoutRunningGeneration()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new[] { "--write-sample", "l-shaped-core" },
                new ThrowingTextReader(),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(0, exitCode);
            EngineInput input = JsonSerializer.Deserialize<EngineInput>(stdout.ToString(), JsonOptions());
            Assert.Equal("l-shaped-core-sample", input.Project.Id);
            Assert.NotEmpty(input.Floorplate.Outer.Points);
        }

        [Fact]
        public void ModeratelyIrregularSampleJson_GeneratesValidOutput()
        {
            string outputPath = Path.Combine(Path.GetTempPath(), "floor-plan-irregular-" + System.Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".json");
            string inputPath = Path.Combine(RepositoryRoot(), "samples", "floor-plan-generation", "moderately-irregular-core-input.json");

            try
            {
                int exitCode = CliApplication.Run(
                    new[] { "--input", inputPath, "--output", outputPath },
                    TextReader.Null,
                    TextWriter.Null,
                    new StringWriter(CultureInfo.InvariantCulture));

                Assert.Equal(0, exitCode);
                EngineOutput output = JsonSerializer.Deserialize<EngineOutput>(File.ReadAllText(outputPath), JsonOptions());
                Assert.Equal("succeeded", output.Status);
                Assert.Equal(4, output.Variants.Count);
                Assert.Equal(1588.0, output.Metadata.Floorplate.GrossArea);
                Assert.All(output.Variants, v => Assert.Contains(v.Corridors[0].Connections, c => c == "core-ir1"));
            }
            finally
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }

        [Fact]
        public void CliRejectsUnknownJsonProperties()
        {
            string inputJson = "{ \"unknownTopLevel\": true, \"project\": { \"id\": \"bad\" }, \"floorplate\": { \"outer\": { \"points\": [] } } }";
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new string[0],
                new StringReader(inputJson),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(2, exitCode);
            EngineOutput output = JsonSerializer.Deserialize<EngineOutput>(stdout.ToString(), JsonOptions());
            Assert.Equal("failed", output.Status);
            Assert.Contains(output.Diagnostics, d => d.Code == "cli.invalid_json" && d.Severity == "error");
        }

        [Fact]
        public void NarrowFloorplate_ReturnsFailedDiagnosticsWithoutVariants()
        {
            EngineInput input = RectangularInput(seed: 11, variantCount: 3);
            input.Project.Id = "narrow-infeasible-test";
            input.Floorplate.Outer.Points = new List<Point2>
            {
                new Point2(0, 0),
                new Point2(12, 0),
                new Point2(12, 4),
                new Point2(0, 4)
            };
            input.FixedElements.Clear();
            input.Rules.MinCorridorWidth = 2.0;
            input.Rules.MinUnitArea = 25.0;

            EngineOutput output = new FloorPlanEngine().Generate(input);

            Assert.Equal("failed", output.Status);
            Assert.Empty(output.Variants);
            Assert.Contains(output.Diagnostics, d => d.Code == "input.floorplate_too_narrow_for_mvp" && d.Severity == "error");
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

        private static IEnumerable<string> Signatures(EngineOutput output)
        {
            return output.Variants.Select(v => string.Join(
                "|",
                v.VariantId,
                v.Metrics.Score.ToString("0.0000", CultureInfo.InvariantCulture),
                v.Units.Count.ToString(CultureInfo.InvariantCulture),
                v.Rooms.Count.ToString(CultureInfo.InvariantCulture),
                v.Corridors.Count.ToString(CultureInfo.InvariantCulture),
                v.Validation.Passed.ToString()));
        }

        private static Polygon2 ToPolygon(PolygonInput input)
        {
            List<Point2> points = input.Points.Select(p => p.Clone()).ToList();
            if (points.Count > 1 && points[0].EqualsWithin(points[points.Count - 1], 1e-9))
            {
                points.RemoveAt(points.Count - 1);
            }

            return new Polygon2(input.Id, points);
        }

        private static string SegmentKey(LineInput line)
        {
            return SegmentKey(new LineSegment2(line.Start, line.End));
        }

        private static string SegmentKey(LineSegment2 segment)
        {
            string first = PointKey(segment.Start);
            string second = PointKey(segment.End);
            return string.CompareOrdinal(first, second) <= 0 ? first + "|" + second : second + "|" + first;
        }

        private static string PointKey(Point2 point)
        {
            return Math.Round(point.X, 6).ToString("0.######", CultureInfo.InvariantCulture) + "," +
                Math.Round(point.Y, 6).ToString("0.######", CultureInfo.InvariantCulture);
        }

        private sealed class ThrowingTextReader : TextReader
        {
            public override string ReadToEnd()
            {
                throw new InvalidOperationException("This command should not read stdin.");
            }
        }

        private static EngineInput RectangularInput(int seed, int variantCount)
        {
            EngineInput input = new EngineInput();
            input.Project.Id = "rectangular-test";
            input.Project.Name = "Rectangular Test";
            input.Project.Seed = seed;
            input.Project.Tolerance = 0.01;

            input.Floorplate.Outer = new PolygonInput
            {
                Id = "outer",
                Points = new List<Point2>
                {
                    new Point2(0, 0),
                    new Point2(36, 0),
                    new Point2(36, 18),
                    new Point2(0, 18)
                }
            };

            input.Program.TargetUnitTypes = new List<UnitTypeTarget>
            {
                new UnitTypeTarget { Type = "studio", MinArea = 28.0, MaxArea = 58.0, TargetRatio = 0.40, Weight = 1.0 },
                new UnitTypeTarget { Type = "one_bed", MinArea = 48.0, MaxArea = 78.0, TargetRatio = 0.45, Weight = 1.0 },
                new UnitTypeTarget { Type = "two_bed", MinArea = 70.0, MaxArea = 108.0, TargetRatio = 0.15, Weight = 0.7 }
            };

            input.Rules.MinCorridorWidth = 1.8;
            input.Rules.MinRoomWidth = 2.4;
            input.Rules.MinRoomDepth = 2.4;
            input.Rules.MinUnitArea = 25.0;
            input.Rules.RequireDaylightForBedrooms = true;
            input.Rules.RequireDaylightForLiving = true;

            input.GenerationSettings.VariantCount = variantCount;
            input.GenerationSettings.Strictness = "balanced";
            input.GenerationSettings.WeightedVariation = true;
            return input;
        }

        private static EngineInput LShapedInput(int seed, int variantCount)
        {
            EngineInput input = RectangularInput(seed, variantCount);
            input.Project.Id = "l-shaped-test";
            input.Project.Name = "L-Shaped Test";
            input.Floorplate.Outer = new PolygonInput
            {
                Id = "l-shaped-outer",
                Points = new List<Point2>
                {
                    new Point2(0, 0),
                    new Point2(44, 0),
                    new Point2(44, 18),
                    new Point2(28, 18),
                    new Point2(28, 30),
                    new Point2(0, 30)
                }
            };

            input.FixedElements = new List<FixedElementInput>
            {
                new FixedElementInput
                {
                    Id = "core-l1",
                    Type = "core",
                    BlocksGeneration = true,
                    Polygon = new PolygonInput
                    {
                        Id = "core-l1",
                        Points = new List<Point2>
                        {
                            new Point2(18, 8),
                            new Point2(24, 8),
                            new Point2(24, 14),
                            new Point2(18, 14)
                        }
                    }
                }
            };

            input.Access.VerticalCoreAccess = new List<Point2> { new Point2(21, 14) };
            input.GenerationSettings.WeightedVariation = true;
            return input;
        }

        private static EngineInput ModeratelyIrregularInput(int seed, int variantCount)
        {
            EngineInput input = RectangularInput(seed, variantCount);
            input.Project.Id = "moderately-irregular-test";
            input.Project.Name = "Moderately Irregular Test";
            input.Floorplate.Outer = new PolygonInput
            {
                Id = "moderately-irregular-outer",
                Points = new List<Point2>
                {
                    new Point2(0, 0),
                    new Point2(52, 0),
                    new Point2(52, 16),
                    new Point2(46, 16),
                    new Point2(46, 28),
                    new Point2(34, 28),
                    new Point2(34, 34),
                    new Point2(0, 34)
                }
            };

            input.FixedElements = new List<FixedElementInput>
            {
                new FixedElementInput
                {
                    Id = "core-ir1",
                    Type = "core",
                    BlocksGeneration = true,
                    Polygon = new PolygonInput
                    {
                        Id = "core-ir1",
                        Points = new List<Point2>
                        {
                            new Point2(22, 12),
                            new Point2(28, 12),
                            new Point2(28, 18),
                            new Point2(22, 18)
                        }
                    }
                }
            };

            input.Access.VerticalCoreAccess = new List<Point2> { new Point2(22, 18) };
            input.Program.TargetUnitTypes = new List<UnitTypeTarget>
            {
                new UnitTypeTarget { Type = "studio", MinArea = 30.0, MaxArea = 95.0, TargetRatio = 0.30, Weight = 1.0 },
                new UnitTypeTarget { Type = "one_bed", MinArea = 55.0, MaxArea = 150.0, TargetRatio = 0.50, Weight = 1.0 },
                new UnitTypeTarget { Type = "two_bed", MinArea = 80.0, MaxArea = 190.0, TargetRatio = 0.20, Weight = 0.8 }
            };
            return input;
        }
    }
}

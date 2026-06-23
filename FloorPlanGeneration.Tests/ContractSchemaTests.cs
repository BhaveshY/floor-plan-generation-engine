using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FloorPlanGeneration.Cli;
using FloorPlanGeneration.Schema;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class ContractSchemaTests
    {
        [Theory]
        [InlineData("rectangular-core-input.json")]
        [InlineData("l-shaped-core-input.json")]
        [InlineData("moderately-irregular-core-input.json")]
        [InlineData("infeasible-narrow-input.json")]
        public void SampleInputsValidateAgainstPublishedInputSchema(string sampleFile)
        {
            using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(SchemaPath("floor-plan-engine-input.schema.json")));
            using JsonDocument instance = JsonDocument.Parse(File.ReadAllText(SamplePath(sampleFile)));

            List<string> errors = SchemaAssertions.Validate(schema.RootElement, instance.RootElement);

            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }

        [Fact]
        public void GeneratedOutputValidatesAgainstPublishedOutputSchema()
        {
            EngineInput input = JsonSerializer.Deserialize<EngineInput>(
                File.ReadAllText(SamplePath("rectangular-core-input.json")),
                JsonOptions());
            input.GenerationSettings.VariantCount = 2;

            EngineOutput output = new FloorPlanEngine().Generate(input);
            using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(SchemaPath("floor-plan-engine-output.schema.json")));
            using JsonDocument instance = JsonDocument.Parse(JsonSerializer.Serialize(output, JsonOptions()));

            List<string> errors = SchemaAssertions.Validate(schema.RootElement, instance.RootElement);

            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }

        [Fact]
        public void GeneratedOutputWithRecommendationValidatesAgainstPublishedOutputSchema()
        {
            EngineInput input = JsonSerializer.Deserialize<EngineInput>(
                File.ReadAllText(SamplePath("rectangular-core-input.json")),
                JsonOptions());
            input.GenerationSettings.VariantCount = 3;
            input.GenerationSettings.RecommendVariant = true;

            EngineOutput output = new FloorPlanEngine().Generate(input);
            using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(SchemaPath("floor-plan-engine-output.schema.json")));
            using JsonDocument instance = JsonDocument.Parse(JsonSerializer.Serialize(output, JsonOptions()));

            Assert.NotNull(output.Recommendation);
            Assert.True(instance.RootElement.TryGetProperty("recommendation", out _));
            List<string> errors = SchemaAssertions.Validate(schema.RootElement, instance.RootElement);

            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }

        [Fact]
        public void CliPrintInputSchema_WritesPublishedSchemaWithoutReadingInput()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new[] { "--print-input-schema" },
                new ThrowingTextReader(),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(0, exitCode);
            using JsonDocument printed = JsonDocument.Parse(stdout.ToString());
            Assert.Equal(
                "https://bhaveshy.github.io/floor-plan-generation-engine/schemas/1.2/floor-plan-engine-input.schema.json",
                printed.RootElement.GetProperty("$id").GetString());
        }

        [Fact]
        public void CliPrintOutputSchema_WritesPublishedSchemaWithoutReadingInput()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = CliApplication.Run(
                new[] { "--print-output-schema" },
                new ThrowingTextReader(),
                stdout,
                new StringWriter(CultureInfo.InvariantCulture));

            Assert.Equal(0, exitCode);
            using JsonDocument printed = JsonDocument.Parse(stdout.ToString());
            Assert.Equal(
                "https://bhaveshy.github.io/floor-plan-generation-engine/schemas/1.2/floor-plan-engine-output.schema.json",
                printed.RootElement.GetProperty("$id").GetString());
        }

        [Fact]
        public void GoldenContractFixtures_MatchRepresentativeSeededOutputs()
        {
            List<GoldenContractCase> cases = JsonSerializer.Deserialize<List<GoldenContractCase>>(
                File.ReadAllText(FixturePath("golden-contracts.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            foreach (GoldenContractCase golden in cases)
            {
                StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);
                int exitCode = CliApplication.Run(
                    new[] { "--input", SamplePath(golden.Input) },
                    TextReader.Null,
                    stdout,
                    new StringWriter(CultureInfo.InvariantCulture));

                EngineOutput output = JsonSerializer.Deserialize<EngineOutput>(stdout.ToString(), JsonOptions());
                int expectedExit = string.Equals(golden.Status, "failed", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

                Assert.Equal(expectedExit, exitCode);
                Assert.Equal(golden.ProjectId, output.ProjectId);
                Assert.Equal(golden.Status, output.Status);
                Assert.Equal(golden.SchemaVersion, output.Metadata.SchemaVersion);
                Assert.Equal(golden.Seed, output.Metadata.Seed);
                Assert.Equal(golden.VariantCount, output.Variants.Count);
                Assert.Equal(golden.GrossArea, output.Metadata.Floorplate.GrossArea);
                Assert.Equal(golden.UsableArea, output.Metadata.Floorplate.UsableArea);
                Assert.Equal("FP::Generated::Units", output.Metadata.Layers["units"]);
                Assert.Equal("FP::Generated::Diagnostics", output.Metadata.Layers["diagnostics"]);

                Assert.Equal(
                    golden.RequiredDiagnosticCodes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase),
                    output.Diagnostics
                        .Select(d => d.Code)
                        .Intersect(golden.RequiredDiagnosticCodes, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
                Assert.Equal(golden.Variants.Count, output.Variants.Count);
                for (int i = 0; i < golden.Variants.Count; i++)
                {
                    GoldenVariantContract expected = golden.Variants[i];
                    LayoutVariant actual = output.Variants[i];
                    Assert.Equal(expected.Id, actual.VariantId);
                    Assert.Equal(expected.Seed, actual.Seed);
                    Assert.InRange(Math.Abs(expected.Score - actual.Metrics.Score), 0.0, 0.0001);
                    Assert.Equal(expected.Units, actual.Units.Count);
                    Assert.Equal(expected.Rooms, actual.Rooms.Count);
                    Assert.Equal(expected.Corridors, actual.Corridors.Count);
                    Assert.Equal(expected.FirstUnit, actual.Units.FirstOrDefault()?.Id ?? string.Empty);
                    Assert.Equal(expected.TopologyNodes, actual.Topology.Nodes.Count);
                    Assert.Equal(expected.TopologyEdges, actual.Topology.Edges.Count);
                    Assert.Equal("fp://" + golden.ProjectId + "/variants/" + actual.VariantId, actual.ExternalId);
                    Assert.All(actual.Units, unit => Assert.StartsWith(actual.ExternalId + "/units/", unit.ExternalId, StringComparison.Ordinal));
                }
            }
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

        private static string SchemaPath(string fileName)
        {
            return Path.Combine(RepositoryRoot(), "schemas", fileName);
        }

        private static string SamplePath(string fileName)
        {
            return Path.Combine(RepositoryRoot(), "samples", "floor-plan-generation", fileName);
        }

        private static string FixturePath(string fileName)
        {
            return Path.Combine(RepositoryRoot(), "FloorPlanGeneration.Tests", "Fixtures", fileName);
        }

        private sealed class ThrowingTextReader : TextReader
        {
            public override string ReadToEnd()
            {
                throw new InvalidOperationException("The CLI schema commands must not read engine input.");
            }
        }

        private sealed class GoldenContractCase
        {
            public GoldenContractCase()
            {
                Input = string.Empty;
                ProjectId = string.Empty;
                Status = string.Empty;
                SchemaVersion = string.Empty;
                RequiredDiagnosticCodes = new List<string>();
                Variants = new List<GoldenVariantContract>();
            }

            public string Input { get; set; }
            public string ProjectId { get; set; }
            public string Status { get; set; }
            public string SchemaVersion { get; set; }
            public int Seed { get; set; }
            public int VariantCount { get; set; }
            public double GrossArea { get; set; }
            public double UsableArea { get; set; }
            public List<string> RequiredDiagnosticCodes { get; set; }
            public List<GoldenVariantContract> Variants { get; set; }
        }

        private sealed class GoldenVariantContract
        {
            public GoldenVariantContract()
            {
                Id = string.Empty;
                FirstUnit = string.Empty;
            }

            public string Id { get; set; }
            public int Seed { get; set; }
            public double Score { get; set; }
            public int Units { get; set; }
            public int Rooms { get; set; }
            public int Corridors { get; set; }
            public string FirstUnit { get; set; }
            public int TopologyNodes { get; set; }
            public int TopologyEdges { get; set; }
        }
    }

    internal static class SchemaAssertions
    {
        public static List<string> Validate(JsonElement schemaRoot, JsonElement instance)
        {
            List<string> errors = new List<string>();
            ValidateAgainst(schemaRoot, schemaRoot, instance, "$", errors);
            return errors;
        }

        private static void ValidateAgainst(JsonElement schema, JsonElement root, JsonElement instance, string path, List<string> errors)
        {
            if (schema.TryGetProperty("$ref", out JsonElement reference))
            {
                schema = ResolveReference(root, reference.GetString());
            }

            if (schema.TryGetProperty("const", out JsonElement constant) && !JsonEqual(constant, instance))
            {
                errors.Add(path + " must equal " + constant);
            }

            if (schema.TryGetProperty("enum", out JsonElement enumValues) && !enumValues.EnumerateArray().Any(v => JsonEqual(v, instance)))
            {
                errors.Add(path + " must match one of the declared enum values.");
            }

            string type = schema.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() : string.Empty;
            if (!string.IsNullOrEmpty(type) && !HasType(instance, type))
            {
                errors.Add(path + " must be " + type + " but was " + instance.ValueKind + ".");
                return;
            }

            if (type == "object" || schema.TryGetProperty("properties", out _))
            {
                ValidateObject(schema, root, instance, path, errors);
            }
            else if (type == "array")
            {
                ValidateArray(schema, root, instance, path, errors);
            }
            else if (type == "number" || type == "integer")
            {
                ValidateNumber(schema, instance, path, errors);
            }
            else if (type == "string")
            {
                ValidateString(schema, instance, path, errors);
            }
        }

        private static void ValidateObject(JsonElement schema, JsonElement root, JsonElement instance, string path, List<string> errors)
        {
            if (instance.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (schema.TryGetProperty("required", out JsonElement required))
            {
                foreach (JsonElement property in required.EnumerateArray())
                {
                    if (!instance.TryGetProperty(property.GetString(), out _))
                    {
                        errors.Add(path + "." + property.GetString() + " is required.");
                    }
                }
            }

            Dictionary<string, JsonElement> propertySchemas = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (schema.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonProperty propertySchema in properties.EnumerateObject())
                {
                    propertySchemas[propertySchema.Name] = propertySchema.Value;
                    if (instance.TryGetProperty(propertySchema.Name, out JsonElement value))
                    {
                        ValidateAgainst(propertySchema.Value, root, value, path + "." + propertySchema.Name, errors);
                    }
                }
            }

            bool allowsAdditional = true;
            JsonElement additionalSchema = default;
            bool hasAdditionalSchema = false;
            if (schema.TryGetProperty("additionalProperties", out JsonElement additional))
            {
                if (additional.ValueKind == JsonValueKind.False)
                {
                    allowsAdditional = false;
                }
                else if (additional.ValueKind == JsonValueKind.Object)
                {
                    hasAdditionalSchema = true;
                    additionalSchema = additional;
                }
            }

            foreach (JsonProperty property in instance.EnumerateObject())
            {
                if (propertySchemas.ContainsKey(property.Name))
                {
                    continue;
                }

                if (!allowsAdditional)
                {
                    errors.Add(path + "." + property.Name + " is not allowed by the schema.");
                }
                else if (hasAdditionalSchema)
                {
                    ValidateAgainst(additionalSchema, root, property.Value, path + "." + property.Name, errors);
                }
            }
        }

        private static void ValidateArray(JsonElement schema, JsonElement root, JsonElement instance, string path, List<string> errors)
        {
            if (instance.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            if (schema.TryGetProperty("minItems", out JsonElement minItems) && instance.GetArrayLength() < minItems.GetInt32())
            {
                errors.Add(path + " must contain at least " + minItems.GetInt32().ToString(CultureInfo.InvariantCulture) + " item(s).");
            }

            if (!schema.TryGetProperty("items", out JsonElement itemSchema))
            {
                return;
            }

            int index = 0;
            foreach (JsonElement item in instance.EnumerateArray())
            {
                ValidateAgainst(itemSchema, root, item, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", errors);
                index++;
            }
        }

        private static void ValidateNumber(JsonElement schema, JsonElement instance, string path, List<string> errors)
        {
            if (!instance.TryGetDouble(out double value))
            {
                return;
            }

            if (schema.TryGetProperty("minimum", out JsonElement minimum) && value < minimum.GetDouble())
            {
                errors.Add(path + " must be greater than or equal to " + minimum.GetDouble().ToString(CultureInfo.InvariantCulture) + ".");
            }

            if (schema.TryGetProperty("maximum", out JsonElement maximum) && value > maximum.GetDouble())
            {
                errors.Add(path + " must be less than or equal to " + maximum.GetDouble().ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        private static void ValidateString(JsonElement schema, JsonElement instance, string path, List<string> errors)
        {
            string value = instance.GetString() ?? string.Empty;
            if (schema.TryGetProperty("minLength", out JsonElement minLength) && value.Length < minLength.GetInt32())
            {
                errors.Add(path + " must contain at least " + minLength.GetInt32().ToString(CultureInfo.InvariantCulture) + " character(s).");
            }

            if (schema.TryGetProperty("pattern", out JsonElement pattern) && !Regex.IsMatch(value, pattern.GetString()))
            {
                errors.Add(path + " must match pattern " + pattern.GetString() + ".");
            }
        }

        private static JsonElement ResolveReference(JsonElement root, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("#/$defs/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Only local $defs references are supported by the test validator: " + reference);
            }

            string name = reference.Substring("#/$defs/".Length);
            return root.GetProperty("$defs").GetProperty(name);
        }

        private static bool HasType(JsonElement instance, string type)
        {
            switch (type)
            {
                case "object":
                    return instance.ValueKind == JsonValueKind.Object;
                case "array":
                    return instance.ValueKind == JsonValueKind.Array;
                case "string":
                    return instance.ValueKind == JsonValueKind.String;
                case "number":
                    return instance.ValueKind == JsonValueKind.Number;
                case "integer":
                    return instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _);
                case "boolean":
                    return instance.ValueKind == JsonValueKind.True || instance.ValueKind == JsonValueKind.False;
                default:
                    throw new InvalidOperationException("Unsupported schema type in tests: " + type);
            }
        }

        private static bool JsonEqual(JsonElement left, JsonElement right)
        {
            return left.ValueKind == right.ValueKind && left.ToString() == right.ToString();
        }
    }
}

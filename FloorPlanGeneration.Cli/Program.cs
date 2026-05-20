using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FloorPlanGeneration;
using FloorPlanGeneration.Schema;

namespace FloorPlanGeneration.Cli
{
    public static class CliApplication
    {
        private const string Usage = "Usage: FloorPlanGeneration.Cli [--input path|--sample name] [--output path] [--seed n] [--variants n] [--validate-only] [--summary] [--fail-on-partial] [--list-samples|--write-sample name] [--print-input-schema|--print-output-schema|--ai-manifest]";

        private static readonly Dictionary<string, string> Samples = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "rectangular-core", "rectangular-core-input.json" },
            { "l-shaped-core", "l-shaped-core-input.json" },
            { "moderately-irregular-core", "moderately-irregular-core-input.json" },
            { "infeasible-narrow", "infeasible-narrow-input.json" }
        };

        public static int Run(string[] args, TextReader inputReader, TextWriter outputWriter, TextWriter errorWriter)
        {
            CliOptions options;
            if (!TryParseArgs(args, out options))
            {
                errorWriter.WriteLine(Usage);
                return 64;
            }

            if (options.Help)
            {
                outputWriter.WriteLine(Usage);
                return 0;
            }

            if (options.ListSamples)
            {
                foreach (string sample in Samples.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    outputWriter.WriteLine(sample);
                }

                return 0;
            }

            if (options.AiManifest)
            {
                outputWriter.WriteLine(JsonSerializer.Serialize(BuildAiManifest(), JsonOptions()));
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(options.WriteSampleName))
            {
                string sampleJson;
                if (!TryReadSample(options.WriteSampleName, out sampleJson))
                {
                    errorWriter.WriteLine("Unknown sample '" + options.WriteSampleName + "'. Run --list-samples to see available samples.");
                    return 64;
                }

                WriteOutput(options.OutputPath, sampleJson, outputWriter);
                return 0;
            }

            if (options.PrintInputSchema || options.PrintOutputSchema)
            {
                try
                {
                    string schema = ReadSchemaArtifact(options.PrintInputSchema
                        ? "floor-plan-engine-input.schema.json"
                        : "floor-plan-engine-output.schema.json");
                    WriteOutput(options.OutputPath, schema, outputWriter);
                    return 0;
                }
                catch (Exception ex)
                {
                    errorWriter.WriteLine("Schema artifact could not be read: " + ex.Message);
                    return 2;
                }
            }

            EngineOutput output;
            try
            {
                string json = ReadInput(options, inputReader);
                if (string.IsNullOrWhiteSpace(json))
                {
                    output = FailedOutput("cli.empty_input", "No JSON input was provided.");
                }
                else
                {
                    EngineInput input = JsonSerializer.Deserialize<EngineInput>(json, JsonOptions());
                    ApplyOverrides(input, options);
                    FloorPlanEngine engine = new FloorPlanEngine();
                    output = options.ValidateOnly ? engine.Validate(input) : engine.Generate(input);
                }
            }
            catch (JsonException ex)
            {
                output = FailedOutput("cli.invalid_json", "Input JSON could not be parsed: " + ex.Message);
            }
            catch (Exception ex)
            {
                output = FailedOutput("cli.exception", "CLI execution failed: " + ex.Message);
            }

            WriteOutput(options.OutputPath, JsonSerializer.Serialize(output, JsonOptions()), outputWriter);
            if (options.Summary)
            {
                WriteSummary(output, options.OutputPath, errorWriter);
            }

            if (string.Equals(output.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (options.FailOnPartial && string.Equals(output.Status, "partial", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            return 0;
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                WriteIndented = true
            };
        }

        private static string ReadInput(CliOptions options, TextReader inputReader)
        {
            if (!string.IsNullOrWhiteSpace(options.SampleName))
            {
                string sampleJson;
                if (!TryReadSample(options.SampleName, out sampleJson))
                {
                    throw new ArgumentException("Unknown sample '" + options.SampleName + "'. Run --list-samples to see available samples.");
                }

                return sampleJson;
            }

            if (!string.IsNullOrWhiteSpace(options.InputPath))
            {
                return File.ReadAllText(options.InputPath);
            }

            return inputReader.ReadToEnd();
        }

        private static bool TryReadSample(string sampleName, out string json)
        {
            json = string.Empty;
            string fileName;
            if (!Samples.TryGetValue(sampleName ?? string.Empty, out fileName))
            {
                return false;
            }

            foreach (string root in SampleSearchRoots())
            {
                string candidate = Path.Combine(root, "samples", "floor-plan-generation", fileName);
                if (File.Exists(candidate))
                {
                    json = File.ReadAllText(candidate);
                    return true;
                }
            }

            throw new FileNotFoundException(fileName);
        }

        private static string ReadSchemaArtifact(string fileName)
        {
            foreach (string root in SchemaSearchRoots())
            {
                string candidate = Path.Combine(root, "schemas", fileName);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
            }

            throw new FileNotFoundException(fileName);
        }

        private static IEnumerable<string> SampleSearchRoots()
        {
            return ArtifactSearchRoots();
        }

        private static IEnumerable<string> SchemaSearchRoots()
        {
            return ArtifactSearchRoots();
        }

        private static IEnumerable<string> ArtifactSearchRoots()
        {
            HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in SearchRootChain(AppContext.BaseDirectory, roots))
            {
                yield return root;
            }

            foreach (string root in SearchRootChain(Directory.GetCurrentDirectory(), roots))
            {
                yield return root;
            }
        }

        private static IEnumerable<string> SearchRootChain(string start, HashSet<string> roots)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                yield break;
            }

            DirectoryInfo directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory != null)
            {
                if (roots.Add(directory.FullName))
                {
                    yield return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        private static void WriteOutput(string outputPath, string json, TextWriter outputWriter)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputWriter.WriteLine(json);
                return;
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, json + Environment.NewLine);
        }

        private static void ApplyOverrides(EngineInput input, CliOptions options)
        {
            if (input == null)
            {
                return;
            }

            if (options.SeedOverride.HasValue)
            {
                if (input.Project == null) input.Project = new ProjectInfo();
                input.Project.Seed = options.SeedOverride.Value;
            }

            if (options.VariantCountOverride.HasValue)
            {
                if (input.GenerationSettings == null) input.GenerationSettings = new GenerationSettings();
                input.GenerationSettings.VariantCount = options.VariantCountOverride.Value;
            }
        }

        private static void WriteSummary(EngineOutput output, string outputPath, TextWriter errorWriter)
        {
            int variantCount = output.Variants != null ? output.Variants.Count : 0;
            int validCount = output.Variants != null ? output.Variants.Count(v => v.Validation != null && v.Validation.Passed) : 0;
            int diagnosticCount = output.Diagnostics != null ? output.Diagnostics.Count : 0;
            double bestScore = output.Variants != null && output.Variants.Count > 0 ? output.Variants.Max(v => v.Metrics != null ? v.Metrics.Score : 0.0) : 0.0;
            string target = string.IsNullOrWhiteSpace(outputPath) ? "stdout" : outputPath;

            errorWriter.WriteLine(
                "status={0} variants={1} valid={2} bestScore={3:0.####} diagnostics={4} output={5}",
                output.Status,
                variantCount,
                validCount,
                bestScore,
                diagnosticCount,
                target);
        }

        private static EngineOutput FailedOutput(string code, string message)
        {
            return new EngineOutput
            {
                ProjectId = "project",
                Status = "failed",
                Diagnostics = new List<Diagnostic>
                {
                    Diagnostic.Error(code, message)
                }
            };
        }

        private static bool TryParseArgs(string[] args, out CliOptions options)
        {
            options = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--help" || arg == "-h")
                {
                    options.Help = true;
                    continue;
                }

                if (arg == "--input")
                {
                    if (++i >= args.Length) return false;
                    options.InputPath = args[i];
                    continue;
                }

                if (arg == "--sample")
                {
                    if (++i >= args.Length) return false;
                    options.SampleName = args[i];
                    continue;
                }

                if (arg == "--write-sample")
                {
                    if (++i >= args.Length) return false;
                    options.WriteSampleName = args[i];
                    continue;
                }

                if (arg == "--list-samples")
                {
                    options.ListSamples = true;
                    continue;
                }

                if (arg == "--ai-manifest" || arg == "--describe")
                {
                    options.AiManifest = true;
                    continue;
                }

                if (arg == "--output")
                {
                    if (++i >= args.Length) return false;
                    options.OutputPath = args[i];
                    continue;
                }

                if (arg == "--seed")
                {
                    if (++i >= args.Length) return false;
                    int seed;
                    if (!int.TryParse(args[i], out seed)) return false;
                    options.SeedOverride = seed;
                    continue;
                }

                if (arg == "--variants")
                {
                    if (++i >= args.Length) return false;
                    int variantCount;
                    if (!int.TryParse(args[i], out variantCount) || variantCount <= 0 || variantCount > 20) return false;
                    options.VariantCountOverride = variantCount;
                    continue;
                }

                if (arg == "--summary")
                {
                    options.Summary = true;
                    continue;
                }

                if (arg == "--validate-only" || arg == "--validate")
                {
                    options.ValidateOnly = true;
                    continue;
                }

                if (arg == "--fail-on-partial")
                {
                    options.FailOnPartial = true;
                    continue;
                }

                if (arg == "--print-input-schema")
                {
                    options.PrintInputSchema = true;
                    continue;
                }

                if (arg == "--print-output-schema")
                {
                    options.PrintOutputSchema = true;
                    continue;
                }

                return false;
            }

            if (!string.IsNullOrWhiteSpace(options.InputPath) && !string.IsNullOrWhiteSpace(options.SampleName))
            {
                return false;
            }

            int metadataCommands =
                (options.PrintInputSchema ? 1 : 0) +
                (options.PrintOutputSchema ? 1 : 0) +
                (options.AiManifest ? 1 : 0);
            return metadataCommands <= 1;
        }

        private static object BuildAiManifest()
        {
            return new
            {
                name = "floorplan-gen",
                version = "0.1.0",
                contract = "JSON-in/JSON-out floor plan generation CLI for AI agents and scripts.",
                stdout = "Engine output JSON is written to stdout unless --output is supplied. --summary writes only to stderr.",
                stdin = "When neither --input nor --sample is supplied, EngineInput JSON is read from stdin.",
                commands = new object[]
                {
                    new
                    {
                        name = "generate",
                        args = "--input <path>|--sample <name> [--output <path>] [--seed <int>] [--variants <1-20>] [--summary]",
                        result = "EngineOutput JSON with status succeeded, partial, or failed."
                    },
                    new
                    {
                        name = "validate",
                        args = "--input <path>|--sample <name> --validate-only [--summary]",
                        result = "EngineOutput JSON with status validated or failed and no generated variants."
                    },
                    new
                    {
                        name = "write-sample",
                        args = "--write-sample <name> [--output <path>]",
                        result = "Starter EngineInput JSON."
                    },
                    new
                    {
                        name = "schemas",
                        args = "--print-input-schema or --print-output-schema",
                        result = "Published JSON Schema artifact."
                    }
                },
                samples = Samples.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).Select(name => new
                {
                    name,
                    fileName = Samples[name],
                    description = SampleDescription(name)
                }).ToList(),
                schemas = new
                {
                    input = "https://bhaveshy.github.io/floor-plan-generation-engine/schemas/1.2/floor-plan-engine-input.schema.json",
                    output = "https://bhaveshy.github.io/floor-plan-generation-engine/schemas/1.2/floor-plan-engine-output.schema.json"
                },
                exitCodes = new[]
                {
                    new { code = 0, meaning = "Success, including validated outputs." },
                    new { code = 2, meaning = "Engine or CLI returned failed JSON output." },
                    new { code = 3, meaning = "Partial output with --fail-on-partial." },
                    new { code = 64, meaning = "CLI usage error." }
                },
                automationNotes = new[]
                {
                    "Prefer --sample for smoke tests and --input for user files.",
                    "Use --validate-only before generation when editing JSON programmatically.",
                    "Use --summary for logs; parse JSON from stdout or the --output file.",
                    "Do not scrape human README text when --ai-manifest and schema commands are available."
                },
                examples = new[]
                {
                    "floorplan-gen --sample rectangular-core --variants 2",
                    "floorplan-gen --input input.json --output output.json --summary",
                    "floorplan-gen --input input.json --validate-only --summary",
                    "floorplan-gen --write-sample rectangular-core --output starter.json"
                }
            };
        }

        private static string SampleDescription(string sampleName)
        {
            switch (sampleName)
            {
                case "rectangular-core":
                    return "Simple rectangular floorplate with one core.";
                case "l-shaped-core":
                    return "L-shaped orthogonal floorplate with one blocking core.";
                case "moderately-irregular-core":
                    return "Stepped orthogonal floorplate with one blocking core.";
                case "infeasible-narrow":
                    return "Intentionally infeasible sample for diagnostics and failure handling.";
                default:
                    return "Bundled floor plan engine sample.";
            }
        }

        private sealed class CliOptions
        {
            public string InputPath { get; set; }
            public string OutputPath { get; set; }
            public string SampleName { get; set; }
            public string WriteSampleName { get; set; }
            public bool Help { get; set; }
            public int? SeedOverride { get; set; }
            public int? VariantCountOverride { get; set; }
            public bool Summary { get; set; }
            public bool FailOnPartial { get; set; }
            public bool ValidateOnly { get; set; }
            public bool ListSamples { get; set; }
            public bool AiManifest { get; set; }
            public bool PrintInputSchema { get; set; }
            public bool PrintOutputSchema { get; set; }
        }
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            return CliApplication.Run(args, Console.In, Console.Out, Console.Error);
        }
    }
}

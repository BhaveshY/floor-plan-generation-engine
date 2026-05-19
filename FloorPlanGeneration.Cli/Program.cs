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
        private const string Usage = "Usage: FloorPlanGeneration.Cli [--input path] [--output path] [--seed n] [--variants n] [--validate-only] [--summary] [--fail-on-partial]";

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

            EngineOutput output;
            try
            {
                string json = ReadInput(options.InputPath, inputReader);
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

        private static string ReadInput(string inputPath, TextReader inputReader)
        {
            if (!string.IsNullOrWhiteSpace(inputPath))
            {
                return File.ReadAllText(inputPath);
            }

            return inputReader.ReadToEnd();
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
                    if (!int.TryParse(args[i], out variantCount) || variantCount <= 0) return false;
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

                return false;
            }

            return true;
        }

        private sealed class CliOptions
        {
            public string InputPath { get; set; }
            public string OutputPath { get; set; }
            public bool Help { get; set; }
            public int? SeedOverride { get; set; }
            public int? VariantCountOverride { get; set; }
            public bool Summary { get; set; }
            public bool FailOnPartial { get; set; }
            public bool ValidateOnly { get; set; }
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

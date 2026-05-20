using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FloorPlanGeneration.Schema;

namespace FloorPlanGeneration.VectorworksMcp
{
    public static class VectorworksMcpApplication
    {
        private const string LatestProtocolVersion = "2025-11-25";
        private const string Usage = "Usage: floorplan-vectorworks-mcp [--stdio] [--self-test] [--version] [--help]";

        private static readonly string[] SupportedProtocolVersions =
        {
            "2025-11-25",
            "2025-06-18",
            "2025-03-26",
            "2024-11-05"
        };

        private static readonly Dictionary<string, string> Samples = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "rectangular-core", "rectangular-core-input.json" },
            { "l-shaped-core", "l-shaped-core-input.json" },
            { "moderately-irregular-core", "moderately-irregular-core-input.json" },
            { "infeasible-narrow", "infeasible-narrow-input.json" }
        };

        public static int Run(string[] args, TextReader inputReader, TextWriter outputWriter, TextWriter errorWriter)
        {
            if (args == null)
            {
                args = new string[0];
            }

            if (args.Any(arg => arg == "--help" || arg == "-h"))
            {
                outputWriter.WriteLine(Usage);
                return 0;
            }

            if (args.Any(arg => arg == "--version"))
            {
                outputWriter.WriteLine(ServerVersion());
                return 0;
            }

            if (args.Any(arg => arg == "--self-test"))
            {
                return RunSelfTest(outputWriter, errorWriter);
            }

            if (args.Any(arg => arg != "--stdio"))
            {
                errorWriter.WriteLine(Usage);
                return 64;
            }

            return RunStdio(inputReader, outputWriter, errorWriter);
        }

        private static int RunStdio(TextReader inputReader, TextWriter outputWriter, TextWriter errorWriter)
        {
            string line;
            while ((line = inputReader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                line = line.TrimStart('\uFEFF');
                JsonObject request;
                try
                {
                    JsonNode node = JsonNode.Parse(line);
                    request = node as JsonObject;
                    if (request == null)
                    {
                        WriteError(outputWriter, null, -32600, "Invalid request: JSON-RPC message must be an object.");
                        continue;
                    }
                }
                catch (JsonException ex)
                {
                    WriteError(outputWriter, null, -32700, "Parse error: " + ex.Message);
                    continue;
                }

                bool hasId = request.TryGetPropertyValue("id", out JsonNode idNode);
                JsonNode id = CloneNode(idNode);
                string method = ReadString(request, "method");

                if (string.IsNullOrWhiteSpace(method))
                {
                    if (hasId)
                    {
                        WriteError(outputWriter, id, -32600, "Invalid request: method is required.");
                    }

                    continue;
                }

                if (!hasId)
                {
                    HandleNotification(method, errorWriter);
                    continue;
                }

                try
                {
                    HandleRequest(request, id, outputWriter);
                }
                catch (Exception ex)
                {
                    WriteError(outputWriter, id, -32603, "Internal error: " + ex.Message);
                }
            }

            return 0;
        }

        private static void HandleNotification(string method, TextWriter errorWriter)
        {
            if (!string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
            {
                errorWriter.WriteLine("Ignored MCP notification: " + method);
            }
        }

        private static void HandleRequest(JsonObject request, JsonNode id, TextWriter outputWriter)
        {
            string jsonrpc = ReadString(request, "jsonrpc");
            if (!string.Equals(jsonrpc, "2.0", StringComparison.Ordinal))
            {
                WriteError(outputWriter, id, -32600, "Invalid request: jsonrpc must be '2.0'.");
                return;
            }

            string method = ReadString(request, "method");
            switch (method)
            {
                case "initialize":
                    WriteResult(outputWriter, id, BuildInitializeResult(request));
                    return;
                case "ping":
                    WriteResult(outputWriter, id, new JsonObject());
                    return;
                case "tools/list":
                    WriteResult(outputWriter, id, BuildToolsListResult());
                    return;
                case "tools/call":
                    HandleToolCall(request, id, outputWriter);
                    return;
                default:
                    WriteError(outputWriter, id, -32601, "Method not found: " + method);
                    return;
            }
        }

        private static JsonObject BuildInitializeResult(JsonObject request)
        {
            JsonObject parameters = request["params"] as JsonObject;
            string requestedVersion = parameters == null ? string.Empty : ReadString(parameters, "protocolVersion");
            string negotiatedVersion = SupportedProtocolVersions.Contains(requestedVersion, StringComparer.Ordinal)
                ? requestedVersion
                : LatestProtocolVersion;

            return new JsonObject
            {
                ["protocolVersion"] = negotiatedVersion,
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject
                    {
                        ["listChanged"] = false
                    }
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "floor-plan-vectorworks-mcp",
                    ["title"] = "Floor Plan Vectorworks MCP",
                    ["version"] = ServerVersion(),
                    ["description"] = "Vectorworks-ready floor plan generation tools backed by the FloorPlanGeneration engine."
                },
                ["instructions"] = "Use vectorworks_generate_floor_plan with either a sampleName or an EngineInput JSON object. Outputs include canonical Vectorworks layer names and stable externalId values for adapter import."
            };
        }

        private static JsonObject BuildToolsListResult()
        {
            return new JsonObject
            {
                ["tools"] = new JsonArray
                {
                    Tool(
                        "vectorworks_health",
                        "Vectorworks MCP Health",
                        "Check that the server can find bundled samples and schemas, and report install/runtime details.",
                        EmptyInputSchema(),
                        readOnly: true,
                        OutputSchema()),
                    Tool(
                        "vectorworks_list_samples",
                        "List Floor Plan Samples",
                        "List bundled starter floorplate samples that can be used to test Vectorworks import workflows.",
                        EmptyInputSchema(),
                        readOnly: true,
                        OutputSchema()),
                    Tool(
                        "vectorworks_get_sample_input",
                        "Get Sample Engine Input",
                        "Return a bundled EngineInput JSON sample by name.",
                        SampleInputSchema(),
                        readOnly: true,
                        OutputSchema()),
                    Tool(
                        "vectorworks_get_contract_schema",
                        "Get Floor Engine Schema",
                        "Return the published input or output JSON Schema artifact for adapter validation.",
                        ContractSchemaInputSchema(),
                        readOnly: true,
                        OutputSchema()),
                    Tool(
                        "vectorworks_validate_floor_plan",
                        "Validate Floor Plan Input",
                        "Dry-run an EngineInput contract before generating variants. This is useful before baking geometry in Vectorworks.",
                        FloorPlanInputSchema(includeVariantControls: true),
                        readOnly: true,
                        OutputSchema()),
                    Tool(
                        "vectorworks_generate_floor_plan",
                        "Generate Vectorworks Floor Plan",
                        "Generate ranked floor plan variants with Vectorworks-ready layers, stable external ids, topology, diagnostics, and validation checks.",
                        FloorPlanInputSchema(includeVariantControls: true),
                        readOnly: false,
                        OutputSchema())
                }
            };
        }

        private static void HandleToolCall(JsonObject request, JsonNode id, TextWriter outputWriter)
        {
            JsonObject parameters = request["params"] as JsonObject;
            if (parameters == null)
            {
                WriteError(outputWriter, id, -32602, "Invalid params: tools/call requires a params object.");
                return;
            }

            string name = ReadString(parameters, "name");
            JsonObject arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
            if (string.IsNullOrWhiteSpace(name))
            {
                WriteError(outputWriter, id, -32602, "Invalid params: tool name is required.");
                return;
            }

            try
            {
                switch (name)
                {
                    case "vectorworks_health":
                        WriteResult(outputWriter, id, ToolResult(BuildHealthResult(), isError: false));
                        return;
                    case "vectorworks_list_samples":
                        WriteResult(outputWriter, id, ToolResult(BuildListSamplesResult(), isError: false));
                        return;
                    case "vectorworks_get_sample_input":
                        WriteResult(outputWriter, id, ToolResult(GetSampleInput(arguments), isError: false));
                        return;
                    case "vectorworks_get_contract_schema":
                        WriteResult(outputWriter, id, ToolResult(GetContractSchema(arguments), isError: false));
                        return;
                    case "vectorworks_validate_floor_plan":
                        WriteResult(outputWriter, id, ToolResult(RunEngineTool(arguments, validateOnly: true), isError: false));
                        return;
                    case "vectorworks_generate_floor_plan":
                        WriteResult(outputWriter, id, ToolResult(RunEngineTool(arguments, validateOnly: false), isError: false));
                        return;
                    default:
                        WriteError(outputWriter, id, -32602, "Unknown tool: " + name);
                        return;
                }
            }
            catch (ToolInputException ex)
            {
                WriteResult(outputWriter, id, ToolResult(ToolError(ex.Code, ex.Message), isError: true));
            }
            catch (JsonException ex)
            {
                WriteResult(outputWriter, id, ToolResult(ToolError("mcp.invalid_json", "Tool JSON could not be parsed: " + ex.Message), isError: true));
            }
            catch (Exception ex)
            {
                WriteResult(outputWriter, id, ToolResult(ToolError("mcp.tool_failed", ex.Message), isError: true));
            }
        }

        private static JsonObject BuildHealthResult()
        {
            return new JsonObject
            {
                ["ok"] = true,
                ["server"] = new JsonObject
                {
                    ["name"] = "floor-plan-vectorworks-mcp",
                    ["version"] = ServerVersion(),
                    ["protocolVersion"] = LatestProtocolVersion,
                    ["runtime"] = ".NET " + Environment.Version
                },
                ["artifacts"] = new JsonObject
                {
                    ["sampleCount"] = Samples.Count,
                    ["samplesAvailable"] = Samples.Keys.All(name => TryReadSample(name, out _)),
                    ["inputSchemaAvailable"] = TryReadSchema("floor-plan-engine-input.schema.json", out _),
                    ["outputSchemaAvailable"] = TryReadSchema("floor-plan-engine-output.schema.json", out _)
                },
                ["vectorworks"] = new JsonObject
                {
                    ["layers"] = JsonSerializer.SerializeToNode(LayerNames.DefaultMap(), JsonOptions()),
                    ["externalIds"] = "Generated objects carry stable fp:// externalId values for adapter-side object metadata."
                }
            };
        }

        private static JsonObject BuildListSamplesResult()
        {
            JsonArray samples = new JsonArray();
            foreach (string sampleName in Samples.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                samples.Add(new JsonObject
                {
                    ["name"] = sampleName,
                    ["fileName"] = Samples[sampleName],
                    ["description"] = SampleDescription(sampleName)
                });
            }

            return new JsonObject
            {
                ["samples"] = samples
            };
        }

        private static JsonObject GetSampleInput(JsonObject arguments)
        {
            string sampleName = ReadString(arguments, "sampleName");
            if (string.IsNullOrWhiteSpace(sampleName))
            {
                throw new ToolInputException("mcp.missing_sample_name", "Provide sampleName.");
            }

            if (!TryReadSample(sampleName, out string json))
            {
                throw new ToolInputException("mcp.sample_not_found", "Unknown sample '" + sampleName + "'. Use vectorworks_list_samples to see available names.");
            }

            return new JsonObject
            {
                ["sampleName"] = sampleName,
                ["input"] = JsonNode.Parse(json)
            };
        }

        private static JsonObject GetContractSchema(JsonObject arguments)
        {
            string schemaType = ReadString(arguments, "schemaType");
            string fileName;
            if (string.Equals(schemaType, "input", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "floor-plan-engine-input.schema.json";
            }
            else if (string.Equals(schemaType, "output", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "floor-plan-engine-output.schema.json";
            }
            else
            {
                throw new ToolInputException("mcp.invalid_schema_type", "schemaType must be 'input' or 'output'.");
            }

            if (!TryReadSchema(fileName, out string json))
            {
                throw new ToolInputException("mcp.schema_not_found", "Schema artifact could not be found: " + fileName);
            }

            return new JsonObject
            {
                ["schemaType"] = schemaType.ToLowerInvariant(),
                ["fileName"] = fileName,
                ["schema"] = JsonNode.Parse(json)
            };
        }

        private static JsonObject RunEngineTool(JsonObject arguments, bool validateOnly)
        {
            try
            {
                EngineInput input = ReadEngineInput(arguments);
                ApplyOverrides(input, arguments);
                EngineOutput output = validateOnly
                    ? new FloorPlanEngine().Validate(input)
                    : new FloorPlanEngine().Generate(input);

                bool failed = string.Equals(output.Status, "failed", StringComparison.OrdinalIgnoreCase);
                bool partial = string.Equals(output.Status, "partial", StringComparison.OrdinalIgnoreCase);
                bool failOnPartial = ReadBoolean(arguments, "failOnPartial", fallback: false);
                JsonObject structured = BuildEngineResult(output, validateOnly);
                structured["isError"] = failed || (partial && failOnPartial);
                return structured;
            }
            catch (ToolInputException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw new ToolInputException("mcp.invalid_engine_input", "Engine input JSON could not be parsed: " + ex.Message);
            }
        }

        private static EngineInput ReadEngineInput(JsonObject arguments)
        {
            JsonNode inputNode = arguments["input"];
            string sampleName = ReadString(arguments, "sampleName");
            bool hasInput = inputNode != null;
            bool hasSample = !string.IsNullOrWhiteSpace(sampleName);

            if (hasInput && hasSample)
            {
                throw new ToolInputException("mcp.ambiguous_input", "Provide either input or sampleName, not both.");
            }

            if (hasSample)
            {
                if (!TryReadSample(sampleName, out string sampleJson))
                {
                    throw new ToolInputException("mcp.sample_not_found", "Unknown sample '" + sampleName + "'. Use vectorworks_list_samples to see available names.");
                }

                return JsonSerializer.Deserialize<EngineInput>(sampleJson, JsonOptions());
            }

            if (!hasInput)
            {
                throw new ToolInputException("mcp.missing_engine_input", "Provide an EngineInput object in input, or provide sampleName.");
            }

            if (inputNode is not JsonObject)
            {
                throw new ToolInputException("mcp.invalid_engine_input", "input must be an EngineInput JSON object.");
            }

            return inputNode.Deserialize<EngineInput>(JsonOptions());
        }

        private static void ApplyOverrides(EngineInput input, JsonObject arguments)
        {
            if (input == null)
            {
                return;
            }

            if (TryReadInt(arguments, "seed", out int seed))
            {
                if (input.Project == null) input.Project = new ProjectInfo();
                input.Project.Seed = seed;
            }

            if (TryReadInt(arguments, "variants", out int variants))
            {
                if (variants < 1 || variants > 20)
                {
                    throw new ToolInputException("mcp.invalid_variant_count", "variants must be between 1 and 20.");
                }

                if (input.GenerationSettings == null) input.GenerationSettings = new GenerationSettings();
                input.GenerationSettings.VariantCount = variants;
            }
        }

        private static JsonObject BuildEngineResult(EngineOutput output, bool validateOnly)
        {
            int variantCount = output.Variants != null ? output.Variants.Count : 0;
            int validVariantCount = output.Variants != null ? output.Variants.Count(v => v.Validation != null && v.Validation.Passed) : 0;
            LayoutVariant bestVariant = output.Variants == null
                ? null
                : output.Variants
                    .Where(v => v.Validation != null && v.Validation.Passed)
                    .OrderByDescending(v => v.Metrics != null ? v.Metrics.Score : 0.0)
                    .FirstOrDefault() ?? output.Variants.FirstOrDefault();

            return new JsonObject
            {
                ["status"] = output.Status,
                ["projectId"] = output.ProjectId,
                ["mode"] = validateOnly ? "validate" : "generate",
                ["schemaVersion"] = output.Metadata != null ? output.Metadata.SchemaVersion : "1.1",
                ["variantCount"] = variantCount,
                ["validVariantCount"] = validVariantCount,
                ["bestVariantId"] = bestVariant != null ? bestVariant.VariantId : string.Empty,
                ["bestScore"] = bestVariant != null && bestVariant.Metrics != null ? bestVariant.Metrics.Score : 0.0,
                ["diagnosticCount"] = output.Diagnostics != null ? output.Diagnostics.Count : 0,
                ["vectorworks"] = new JsonObject
                {
                    ["layers"] = JsonSerializer.SerializeToNode(output.Metadata != null && output.Metadata.Layers != null ? output.Metadata.Layers : LayerNames.DefaultMap(), JsonOptions()),
                    ["bestVariantExternalId"] = bestVariant != null ? bestVariant.ExternalId : string.Empty,
                    ["importNotes"] = new JsonArray
                    {
                        "Create or reuse metadata.layers before baking generated geometry.",
                        "Store externalId on baked Vectorworks objects so repeated imports can update instead of duplicate.",
                        "Do not bake failed outputs as usable plans; display diagnostics first."
                    }
                },
                ["diagnostics"] = JsonSerializer.SerializeToNode(output.Diagnostics ?? new List<Diagnostic>(), JsonOptions()),
                ["output"] = JsonSerializer.SerializeToNode(output, JsonOptions())
            };
        }

        private static JsonObject ToolResult(JsonObject structured, bool isError)
        {
            bool resultIsError = isError || ReadBoolean(structured, "isError", fallback: false);
            if (structured.ContainsKey("isError"))
            {
                structured.Remove("isError");
            }

            string text = structured.ToJsonString(IndentedJsonOptions());
            JsonObject result = new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = text
                    }
                },
                ["structuredContent"] = structured
            };

            if (resultIsError)
            {
                result["isError"] = true;
            }

            return result;
        }

        private static JsonObject Tool(
            string name,
            string title,
            string description,
            JsonObject inputSchema,
            bool readOnly,
            JsonObject outputSchema)
        {
            return new JsonObject
            {
                ["name"] = name,
                ["title"] = title,
                ["description"] = description,
                ["inputSchema"] = inputSchema,
                ["outputSchema"] = outputSchema,
                ["annotations"] = new JsonObject
                {
                    ["title"] = title,
                    ["readOnlyHint"] = readOnly,
                    ["destructiveHint"] = false,
                    ["idempotentHint"] = readOnly
                }
            };
        }

        private static JsonObject EmptyInputSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["additionalProperties"] = false
            };
        }

        private static JsonObject SampleInputSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["sampleName"] = SampleNameSchema()
                },
                ["required"] = new JsonArray { "sampleName" },
                ["additionalProperties"] = false
            };
        }

        private static JsonObject ContractSchemaInputSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["schemaType"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray { "input", "output" },
                        ["description"] = "Which published JSON Schema artifact to return."
                    }
                },
                ["required"] = new JsonArray { "schemaType" },
                ["additionalProperties"] = false
            };
        }

        private static JsonObject FloorPlanInputSchema(bool includeVariantControls)
        {
            JsonObject properties = new JsonObject
            {
                ["input"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "EngineInput JSON matching schemas/floor-plan-engine-input.schema.json."
                },
                ["sampleName"] = SampleNameSchema(),
                ["seed"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Optional deterministic seed override."
                },
                ["failOnPartial"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Mark partial generation results as tool errors so the caller can retry or adjust input."
                }
            };

            if (includeVariantControls)
            {
                properties["variants"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 1,
                    ["maximum"] = 20,
                    ["description"] = "Optional generated variant count override."
                };
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["anyOf"] = new JsonArray
                {
                    new JsonObject { ["required"] = new JsonArray { "input" } },
                    new JsonObject { ["required"] = new JsonArray { "sampleName" } }
                },
                ["additionalProperties"] = false
            };
        }

        private static JsonObject SampleNameSchema()
        {
            JsonArray names = new JsonArray();
            foreach (string name in Samples.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }

            return new JsonObject
            {
                ["type"] = "string",
                ["enum"] = names,
                ["description"] = "Bundled sample input name."
            };
        }

        private static JsonObject OutputSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = true
            };
        }

        private static JsonObject ToolError(string code, string message)
        {
            return new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message
                }
            };
        }

        private static int RunSelfTest(TextWriter outputWriter, TextWriter errorWriter)
        {
            try
            {
                if (!TryReadSample("rectangular-core", out string sampleJson))
                {
                    throw new ToolInputException("mcp.sample_not_found", "Bundled rectangular-core sample could not be found.");
                }

                EngineInput input = JsonSerializer.Deserialize<EngineInput>(sampleJson, JsonOptions());
                if (input.GenerationSettings == null) input.GenerationSettings = new GenerationSettings();
                input.GenerationSettings.VariantCount = 1;

                EngineOutput output = new FloorPlanEngine().Generate(input);
                int variantCount = output.Variants != null ? output.Variants.Count : 0;
                int validVariantCount = output.Variants != null ? output.Variants.Count(v => v.Validation != null && v.Validation.Passed) : 0;
                LayoutVariant bestVariant = output.Variants != null ? output.Variants.FirstOrDefault() : null;
                bool ok = string.Equals(output.Status, "succeeded", StringComparison.OrdinalIgnoreCase);

                JsonObject result = new JsonObject
                {
                    ["ok"] = ok,
                    ["status"] = output.Status,
                    ["projectId"] = output.ProjectId,
                    ["schemaVersion"] = output.Metadata != null ? output.Metadata.SchemaVersion : "1.1",
                    ["variantCount"] = variantCount,
                    ["validVariantCount"] = validVariantCount,
                    ["bestVariantId"] = bestVariant != null ? bestVariant.VariantId : string.Empty,
                    ["diagnosticCount"] = output.Diagnostics != null ? output.Diagnostics.Count : 0,
                    ["samplesAvailable"] = Samples.Keys.All(name => TryReadSample(name, out _)),
                    ["schemasAvailable"] = TryReadSchema("floor-plan-engine-input.schema.json", out _) &&
                        TryReadSchema("floor-plan-engine-output.schema.json", out _)
                };

                outputWriter.WriteLine(result.ToJsonString(IndentedJsonOptions()));
                return ok ? 0 : 2;
            }
            catch (Exception ex)
            {
                JsonObject result = ToolError("mcp.self_test_failed", ex.Message);
                result["ok"] = false;
                outputWriter.WriteLine(result.ToJsonString(IndentedJsonOptions()));
                errorWriter.WriteLine(ex.Message);
                return 2;
            }
        }

        private static bool TryReadSample(string sampleName, out string json)
        {
            json = string.Empty;
            if (!Samples.TryGetValue(sampleName ?? string.Empty, out string fileName))
            {
                return false;
            }

            foreach (string root in ArtifactSearchRoots())
            {
                string candidate = Path.Combine(root, "samples", "floor-plan-generation", fileName);
                if (File.Exists(candidate))
                {
                    json = File.ReadAllText(candidate);
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadSchema(string fileName, out string json)
        {
            json = string.Empty;
            foreach (string root in ArtifactSearchRoots())
            {
                string candidate = Path.Combine(root, "schemas", fileName);
                if (File.Exists(candidate))
                {
                    json = File.ReadAllText(candidate);
                    return true;
                }
            }

            return false;
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

            string repoRoot = Environment.GetEnvironmentVariable("FLOOR_ENGINE_REPO");
            foreach (string root in SearchRootChain(repoRoot, roots))
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

        private static string SampleDescription(string sampleName)
        {
            switch (sampleName)
            {
                case "rectangular-core":
                    return "Simple rectangular floorplate starter input.";
                case "l-shaped-core":
                    return "L-shaped orthogonal floorplate with one blocking core.";
                case "moderately-irregular-core":
                    return "Stepped orthogonal floorplate with one blocking core.";
                case "infeasible-narrow":
                    return "Intentionally infeasible narrow sample for diagnostics and UI testing.";
                default:
                    return "Bundled floor plan engine sample.";
            }
        }

        private static string ReadString(JsonObject obj, string propertyName)
        {
            if (obj == null || !obj.TryGetPropertyValue(propertyName, out JsonNode node) || node == null)
            {
                return string.Empty;
            }

            try
            {
                return node.GetValue<string>() ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }

        private static bool ReadBoolean(JsonObject obj, string propertyName, bool fallback)
        {
            if (obj == null || !obj.TryGetPropertyValue(propertyName, out JsonNode node) || node == null)
            {
                return fallback;
            }

            try
            {
                return node.GetValue<bool>();
            }
            catch (InvalidOperationException)
            {
                return fallback;
            }
        }

        private static bool TryReadInt(JsonObject obj, string propertyName, out int value)
        {
            value = 0;
            if (obj == null || !obj.TryGetPropertyValue(propertyName, out JsonNode node) || node == null)
            {
                return false;
            }

            try
            {
                value = node.GetValue<int>();
                return true;
            }
            catch (Exception)
            {
                throw new ToolInputException("mcp.invalid_integer", propertyName + " must be an integer.");
            }
        }

        private static JsonNode CloneNode(JsonNode node)
        {
            if (node == null)
            {
                return null;
            }

            return JsonNode.Parse(node.ToJsonString());
        }

        private static void WriteResult(TextWriter outputWriter, JsonNode id, JsonObject result)
        {
            WriteMessage(outputWriter, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = CloneNode(id),
                ["result"] = result
            });
        }

        private static void WriteError(TextWriter outputWriter, JsonNode id, int code, string message)
        {
            WriteMessage(outputWriter, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = CloneNode(id),
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message
                }
            });
        }

        private static void WriteMessage(TextWriter outputWriter, JsonObject message)
        {
            outputWriter.WriteLine(message.ToJsonString(JsonOptions()));
            outputWriter.Flush();
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                WriteIndented = false
            };
        }

        private static JsonSerializerOptions IndentedJsonOptions()
        {
            JsonSerializerOptions options = JsonOptions();
            options.WriteIndented = true;
            return options;
        }

        private static string ServerVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        }

        private sealed class ToolInputException : Exception
        {
            public ToolInputException(string code, string message)
                : base(message)
            {
                Code = code;
            }

            public string Code { get; }
        }
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            return VectorworksMcpApplication.Run(args, Console.In, Console.Out, Console.Error);
        }
    }
}

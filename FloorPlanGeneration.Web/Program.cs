using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FloorPlanGeneration;
using FloorPlanGeneration.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

Dictionary<string, string> samples = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    { "rectangular-core", "rectangular-core-input.json" },
    { "l-shaped-core", "l-shaped-core-input.json" },
    { "moderately-irregular-core", "moderately-irregular-core-input.json" },
    { "infeasible-narrow", "infeasible-narrow-input.json" }
};

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    ConfigureJson(options.SerializerOptions);
});

WebApplication app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Json(new
{
    ok = true,
    engine = "FloorPlanGeneration",
    schemaVersion = "1.1",
    samples = samples.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
}));

app.MapGet("/api/manifest", () => Results.Json(new
{
    name = "floor-plan-engine-web-api",
    version = "0.1.0",
    contract = "Local JSON API for validating and generating floor plan variants.",
    endpoints = new object[]
    {
        new
        {
            method = "GET",
            path = "/api/health",
            result = "Engine status, schema version, and bundled sample names."
        },
        new
        {
            method = "GET",
            path = "/api/samples",
            result = "Bundled sample names, file names, and descriptions."
        },
        new
        {
            method = "GET",
            path = "/api/samples/{name}",
            result = "EngineInput JSON for a bundled sample."
        },
        new
        {
            method = "GET",
            path = "/api/schemas/input or /api/schemas/output",
            result = "Packaged JSON Schema artifact."
        },
        new
        {
            method = "POST",
            path = "/api/generate",
            body = "{ input, sampleName, seed, variants, validateOnly }",
            result = "Response summary plus full EngineOutput JSON."
        }
    },
    limits = new
    {
        variantsMin = 1,
        variantsMax = 20
    },
    automationNotes = new[]
    {
        "Provide either input or sampleName, not both.",
        "Set validateOnly to true for fast contract and feasibility checks.",
        "The output property contains the complete EngineOutput contract."
    }
}));

app.MapGet("/api/samples", () =>
{
    var result = samples.Keys
        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
        .Select(name => new
        {
            name,
            fileName = samples[name],
            description = SampleDescription(name)
        });

    return Results.Json(result);
});

app.MapGet("/api/samples/{name}", (string name) =>
{
    if (!TryReadSample(name, samples, out string json))
    {
        return Results.NotFound(new { error = "sample_not_found", message = "Unknown sample '" + name + "'." });
    }

    return Results.Content(json, "application/json");
});

app.MapGet("/api/schemas/{kind}", (string kind) =>
{
    string fileName;
    if (string.Equals(kind, "input", StringComparison.OrdinalIgnoreCase))
    {
        fileName = "floor-plan-engine-input.schema.json";
    }
    else if (string.Equals(kind, "output", StringComparison.OrdinalIgnoreCase))
    {
        fileName = "floor-plan-engine-output.schema.json";
    }
    else
    {
        return Results.BadRequest(new { error = "invalid_schema_kind", message = "Use 'input' or 'output'." });
    }

    if (!TryReadArtifact(Path.Combine("schemas", fileName), out string json))
    {
        return Results.NotFound(new { error = "schema_not_found", message = fileName + " was not found." });
    }

    return Results.Content(json, "application/json");
});

app.MapPost("/api/generate", (GenerationRequest request) =>
{
    EngineInput input;
    try
    {
        input = ResolveInput(request, samples);
        ApplyOverrides(input, request);
    }
    catch (Exception ex) when (ex is ArgumentException || ex is JsonException || ex is FileNotFoundException)
    {
        return Results.BadRequest(new
        {
            error = "invalid_input",
            message = ex.Message
        });
    }

    FloorPlanEngine engine = new FloorPlanEngine();
    EngineOutput output = request != null && request.ValidateOnly
        ? engine.Validate(input)
        : engine.Generate(input);

    return Results.Json(BuildResponse(output, request != null && request.ValidateOnly));
});

app.MapFallbackToFile("index.html");

app.Run();

static EngineInput ResolveInput(GenerationRequest request, Dictionary<string, string> samples)
{
    if (request == null)
    {
        throw new ArgumentException("Request body is required.");
    }

    bool hasInput = request.Input.HasValue && request.Input.Value.ValueKind == JsonValueKind.Object;
    bool hasSample = !string.IsNullOrWhiteSpace(request.SampleName);
    if (hasInput && hasSample)
    {
        throw new ArgumentException("Provide either input or sampleName, not both.");
    }

    JsonSerializerOptions options = EngineJsonOptions();
    if (hasSample)
    {
        if (!TryReadSample(request.SampleName, samples, out string sampleJson))
        {
            throw new ArgumentException("Unknown sample '" + request.SampleName + "'.");
        }

        return JsonSerializer.Deserialize<EngineInput>(sampleJson, options);
    }

    if (!hasInput)
    {
        throw new ArgumentException("Provide an EngineInput object.");
    }

    return request.Input.Value.Deserialize<EngineInput>(options);
}

static void ApplyOverrides(EngineInput input, GenerationRequest request)
{
    if (input == null || request == null)
    {
        return;
    }

    if (request.Seed.HasValue)
    {
        if (input.Project == null) input.Project = new ProjectInfo();
        input.Project.Seed = request.Seed.Value;
    }

    if (request.Variants.HasValue)
    {
        if (request.Variants.Value < 1 || request.Variants.Value > 20)
        {
            throw new ArgumentException("variants must be between 1 and 20.");
        }

        if (input.GenerationSettings == null) input.GenerationSettings = new GenerationSettings();
        input.GenerationSettings.VariantCount = request.Variants.Value;
    }
}

static object BuildResponse(EngineOutput output, bool validateOnly)
{
    LayoutVariant best = output.Variants == null
        ? null
        : output.Variants
            .Where(v => v.Validation != null && v.Validation.Passed)
            .OrderByDescending(v => v.Metrics != null ? v.Metrics.Score : 0.0)
            .FirstOrDefault() ?? output.Variants.FirstOrDefault();

    return new
    {
        status = output.Status,
        mode = validateOnly ? "validate" : "generate",
        projectId = output.ProjectId,
        schemaVersion = output.Metadata != null ? output.Metadata.SchemaVersion : "1.1",
        variantCount = output.Variants != null ? output.Variants.Count : 0,
        validVariantCount = output.Variants != null ? output.Variants.Count(v => v.Validation != null && v.Validation.Passed) : 0,
        diagnosticCount = output.Diagnostics != null ? output.Diagnostics.Count : 0,
        bestVariantId = best != null ? best.VariantId : string.Empty,
        bestScore = best != null && best.Metrics != null ? best.Metrics.Score : 0.0,
        output
    };
}

static bool TryReadSample(string sampleName, Dictionary<string, string> samples, out string json)
{
    json = string.Empty;
    if (!samples.TryGetValue(sampleName ?? string.Empty, out string fileName))
    {
        return false;
    }

    return TryReadArtifact(Path.Combine("samples", "floor-plan-generation", fileName), out json);
}

static bool TryReadArtifact(string relativePath, out string text)
{
    text = string.Empty;
    foreach (string root in ArtifactSearchRoots())
    {
        string candidate = Path.Combine(root, relativePath);
        if (File.Exists(candidate))
        {
            text = File.ReadAllText(candidate);
            return true;
        }
    }

    return false;
}

static IEnumerable<string> ArtifactSearchRoots()
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

static IEnumerable<string> SearchRootChain(string start, HashSet<string> roots)
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

static string SampleDescription(string sampleName)
{
    switch (sampleName)
    {
        case "rectangular-core":
            return "Simple rectangular floorplate with one core. Best for first runs.";
        case "l-shaped-core":
            return "L-shaped orthogonal floorplate with a blocking core.";
        case "moderately-irregular-core":
            return "Stepped orthogonal floorplate with a blocking core and richer layout.";
        case "infeasible-narrow":
            return "Intentionally infeasible sample for checking diagnostics.";
        default:
            return "Bundled floor plan engine sample.";
    }
}

static JsonSerializerOptions EngineJsonOptions()
{
    JsonSerializerOptions options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    return options;
}

static void ConfigureJson(JsonSerializerOptions options)
{
    options.PropertyNameCaseInsensitive = true;
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.WriteIndented = true;
}

public sealed class GenerationRequest
{
    public string SampleName { get; set; }
    public JsonElement? Input { get; set; }
    public int? Seed { get; set; }
    public int? Variants { get; set; }
    public bool ValidateOnly { get; set; }
}

public partial class Program
{
}

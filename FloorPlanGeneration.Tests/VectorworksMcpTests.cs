using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FloorPlanGeneration.VectorworksMcp;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class VectorworksMcpTests
    {
        [Fact]
        public void InitializeAndListTools_ReturnsVectorworksToolCatalog()
        {
            string[] lines = RunMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"tests\",\"version\":\"1.0\"}}}",
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");

            Assert.Equal(2, lines.Length);

            using JsonDocument initialize = JsonDocument.Parse(lines[0]);
            Assert.Equal("2025-11-25", initialize.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
            Assert.True(initialize.RootElement.GetProperty("result").GetProperty("capabilities").TryGetProperty("tools", out _));

            using JsonDocument tools = JsonDocument.Parse(lines[1]);
            string[] names = tools.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .ToArray();

            Assert.Contains("vectorworks_health", names);
            Assert.Contains("vectorworks_list_samples", names);
            Assert.Contains("vectorworks_validate_floor_plan", names);
            Assert.Contains("vectorworks_generate_floor_plan", names);
        }

        [Fact]
        public void GenerateFloorPlanTool_WithSample_ReturnsStructuredEngineOutput()
        {
            string[] lines = RunMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":\"generate\",\"method\":\"tools/call\",\"params\":{\"name\":\"vectorworks_generate_floor_plan\",\"arguments\":{\"sampleName\":\"rectangular-core\",\"variants\":1}}}");

            Assert.Single(lines);

            using JsonDocument response = JsonDocument.Parse(lines[0]);
            JsonElement result = response.RootElement.GetProperty("result");
            Assert.False(result.TryGetProperty("isError", out _));

            JsonElement structured = result.GetProperty("structuredContent");
            Assert.Equal("succeeded", structured.GetProperty("status").GetString());
            Assert.Equal("rectangular-core-sample", structured.GetProperty("projectId").GetString());
            Assert.Equal(1, structured.GetProperty("variantCount").GetInt32());
            Assert.Equal("FP::Generated::Units", structured.GetProperty("vectorworks").GetProperty("layers").GetProperty("units").GetString());
            Assert.Equal(1, structured.GetProperty("output").GetProperty("variants").GetArrayLength());

            string text = result.GetProperty("content")[0].GetProperty("text").GetString();
            using JsonDocument textJson = JsonDocument.Parse(text);
            Assert.Equal("succeeded", textJson.RootElement.GetProperty("status").GetString());
        }

        [Fact]
        public void ToolInputError_ReturnsMcpToolErrorResult()
        {
            string[] lines = RunMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"vectorworks_generate_floor_plan\",\"arguments\":{\"sampleName\":\"missing-sample\"}}}");

            Assert.Single(lines);

            using JsonDocument response = JsonDocument.Parse(lines[0]);
            JsonElement result = response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            Assert.Equal("mcp.sample_not_found", result.GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public void SelfTest_GeneratesBundledSample()
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);
            StringWriter stderr = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = VectorworksMcpApplication.Run(new[] { "--self-test" }, TextReader.Null, stdout, stderr);

            Assert.Equal(0, exitCode);
            using JsonDocument response = JsonDocument.Parse(stdout.ToString());
            Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("succeeded", response.RootElement.GetProperty("status").GetString());
        }

        private static string[] RunMcp(params string[] requests)
        {
            StringWriter stdout = new StringWriter(CultureInfo.InvariantCulture);
            StringWriter stderr = new StringWriter(CultureInfo.InvariantCulture);
            string input = string.Join("\n", requests) + "\n";

            int exitCode = VectorworksMcpApplication.Run(new[] { "--stdio" }, new StringReader(input), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.True(string.IsNullOrWhiteSpace(stderr.ToString()), stderr.ToString());
            return stdout
                .ToString()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}

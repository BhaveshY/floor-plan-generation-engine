using System;
using System.IO;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class WebFrontendRegressionTests
    {
        [Fact]
        public void SetupEditsPreserveGeneratedResponseUntilRegeneration()
        {
            string app = ReadWebFile("app.js");
            string handler = SliceFunction(app, "handleSetupInput", "syncFormFromInput");

            Assert.DoesNotContain("state.response = null", handler, StringComparison.Ordinal);
            Assert.DoesNotContain("state.selectedVariantId = \"\"", handler, StringComparison.Ordinal);
            Assert.Contains("markInputDirty(\"Updating plan\", 650)", handler, StringComparison.Ordinal);
            Assert.True(
                handler.IndexOf("markInputDirty", StringComparison.Ordinal) < handler.IndexOf("renderAll()", StringComparison.Ordinal),
                "The stale state should be marked before renderAll so the generated preview is visibly preserved as stale.");
        }

        [Fact]
        public void JsonAndFormatEditsUseResponsePreservingInputPath()
        {
            string app = ReadWebFile("app.js");
            string setInput = SliceFunction(app, "setInput", "handleSetupInput");
            string applyJson = SliceFunction(app, "applyJsonFromEditor", "markInputDirty");
            string formatInput = SliceFunction(app, "formatInput", "copyOutput");

            Assert.Contains("if (!options.preserveResponse)", setInput, StringComparison.Ordinal);
            Assert.Contains("state.response = null", setInput, StringComparison.Ordinal);
            Assert.Contains("setInput(parsed, { preserveResponse: true })", applyJson, StringComparison.Ordinal);
            Assert.Contains("setInput(parsed, { preserveResponse: true })", formatInput, StringComparison.Ordinal);
        }

        [Fact]
        public void StaleOutputsDisableExportsAndSaveSvg()
        {
            string app = ReadWebFile("app.js");
            string renderAll = SliceFunction(app, "renderAll", "updateDirtyState");

            Assert.Contains("const exportReady = Boolean(output && !state.inputDirty)", renderAll, StringComparison.Ordinal);
            Assert.Contains("els.copyOutputBtn.disabled = !exportReady", renderAll, StringComparison.Ordinal);
            Assert.Contains("els.downloadOutputBtn.disabled = !exportReady", renderAll, StringComparison.Ordinal);
            Assert.Contains("button.disabled = !exportReady", renderAll, StringComparison.Ordinal);
            Assert.Contains("els.saveSvgBtn.disabled = state.inputDirty || !els.planSvg.childElementCount", renderAll, StringComparison.Ordinal);
        }

        [Fact]
        public void PlanSvgHasSourceGeometryEditHooks()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");

            Assert.Contains("els.planSvg.addEventListener(\"pointerdown\", handlePlanPointerDown)", app, StringComparison.Ordinal);
            Assert.Contains("function applyCanvasEdit", app, StringComparison.Ordinal);
            Assert.Contains("renderInputEditHandles(group, input)", app, StringComparison.Ordinal);
            Assert.Contains("data-edit-action", app, StringComparison.Ordinal);
            Assert.Contains(".edit-handle", styles, StringComparison.Ordinal);
            Assert.Contains("Regenerating from edited inputs", styles, StringComparison.Ordinal);
        }

        private static string ReadWebFile(string fileName)
        {
            return File.ReadAllText(Path.Combine(RepositoryRoot(), "FloorPlanGeneration.Web", "wwwroot", fileName));
        }

        private static string SliceFunction(string source, string functionName, string nextFunctionName)
        {
            int start = source.IndexOf("function " + functionName, StringComparison.Ordinal);
            int end = source.IndexOf("function " + nextFunctionName, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing function " + functionName + ".");
            Assert.True(end > start, "Missing next function " + nextFunctionName + ".");
            return source.Substring(start, end - start);
        }

        private static string RepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        }
    }
}

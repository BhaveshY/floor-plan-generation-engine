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
            string handler = SliceFunction(app, "handleSetupInput");

            Assert.DoesNotContain("state.response = null", handler, StringComparison.Ordinal);
            Assert.DoesNotContain("state.selectedVariantId = \"\"", handler, StringComparison.Ordinal);
            Assert.Contains("markInputDirty(\"Updating plan\", 650)", handler, StringComparison.Ordinal);
            AssertBefore(handler, "markInputDirty", "renderAll()", "The stale state should be marked before renderAll so the generated preview is visibly preserved as stale.");
        }

        [Fact]
        public void JsonAndFormatEditsUseResponsePreservingInputPath()
        {
            string app = ReadWebFile("app.js");
            string setInput = SliceFunction(app, "setInput");
            string applyJson = SliceFunction(app, "applyJsonFromEditor");
            string formatInput = SliceFunction(app, "formatInput");

            Assert.Contains("if (!options.preserveResponse)", setInput, StringComparison.Ordinal);
            Assert.Contains("state.response = null", setInput, StringComparison.Ordinal);
            Assert.Contains("setInput(parsed, { preserveResponse: true })", applyJson, StringComparison.Ordinal);
            Assert.Contains("setInput(parsed, { preserveResponse: true })", formatInput, StringComparison.Ordinal);
        }

        [Fact]
        public void StaleOutputsDisableExportsAndSaveSvg()
        {
            string app = ReadWebFile("app.js");
            string renderAll = SliceFunction(app, "renderAll");
            string updateDirtyState = SliceFunction(app, "updateDirtyState");

            Assert.Contains("const exportReady = Boolean(output && !state.inputDirty)", renderAll, StringComparison.Ordinal);
            Assert.Contains("els.copyOutputBtn.disabled = !exportReady", renderAll, StringComparison.Ordinal);
            Assert.Contains("els.downloadOutputBtn.disabled = !exportReady", renderAll, StringComparison.Ordinal);
            Assert.Contains("button.disabled = !exportReady", renderAll, StringComparison.Ordinal);
            Assert.Contains("els.saveSvgBtn.disabled = state.inputDirty || !els.planSvg.childElementCount", renderAll, StringComparison.Ordinal);
            Assert.Contains("const stalePreview = Boolean(state.inputDirty && state.response)", updateDirtyState, StringComparison.Ordinal);
            Assert.Contains("els.previewFrame.classList.toggle(\"is-stale\", stalePreview)", updateDirtyState, StringComparison.Ordinal);
            Assert.Contains("els.planSvg.classList.toggle(\"stale-preview\", stalePreview)", updateDirtyState, StringComparison.Ordinal);
        }

        [Fact]
        public void EditModeControlsAreExplicitAndGateCanvasEditHooks()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string handleCanvasAction = SliceFunction(app, "handleCanvasAction");
            string handlePlanPointerDown = SliceFunction(app, "handlePlanPointerDown");
            string renderInputEditHandles = SliceFunction(app, "renderInputEditHandles");
            string editHandle = SliceFunction(app, "editHandle");

            Assert.Contains("editMode: false", app, StringComparison.Ordinal);
            Assert.Contains("data-canvas-action=\"edit-toggle\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"editReadout\"", index, StringComparison.Ordinal);
            Assert.Contains("action === \"edit-toggle\"", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("state.editMode = !state.editMode", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("button.dataset.canvasAction === \"edit-toggle\"", app, StringComparison.Ordinal);
            Assert.Contains("editToggle.setAttribute(\"aria-pressed\", editActive ? \"true\" : \"false\")", app, StringComparison.Ordinal);
            Assert.Contains("els.planSvg.addEventListener(\"pointerdown\", handlePlanPointerDown)", app, StringComparison.Ordinal);
            Assert.Contains("!state.editMode", handlePlanPointerDown, StringComparison.Ordinal);
            Assert.Contains("function applyCanvasEdit", app, StringComparison.Ordinal);
            Assert.Contains("state.editMode", renderInputEditHandles, StringComparison.Ordinal);
            Assert.Contains("data-edit-action", editHandle, StringComparison.Ordinal);
            Assert.Contains(".edit-readout", styles, StringComparison.Ordinal);
            Assert.Contains(".edit-handle", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void PlanSelectionInspectorIsWiredToSelectablePreviewGeometry()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string renderAll = SliceFunction(app, "renderAll");
            string renderPreview = SliceFunction(app, "renderPreview");
            string selectableAttributes = SliceFunction(app, "selectableAttributes");
            string renderSelectionInspector = SliceFunction(app, "renderSelectionInspector");
            string selectedElementDetails = SliceFunction(app, "selectedElementDetails");
            string inspectorMarkup = SliceFunction(app, "inspectorMarkup");

            Assert.Contains("Plan Inspector", index, StringComparison.Ordinal);
            Assert.Contains("id=\"selectionInspector\"", index, StringComparison.Ordinal);
            Assert.Contains("els.selectionInspector.addEventListener(\"click\", handleInspectorAction)", app, StringComparison.Ordinal);
            Assert.Contains("renderSelectionInspector(output)", renderAll, StringComparison.Ordinal);
            Assert.Contains("data-select-kind", selectableAttributes, StringComparison.Ordinal);
            Assert.Contains("data-select-id", selectableAttributes, StringComparison.Ordinal);
            Assert.Contains("selected-element", app, StringComparison.Ordinal);
            Assert.Contains("selectableAttributes(\"floorplate\", \"floorplate\")", renderPreview, StringComparison.Ordinal);
            Assert.Contains("selectableAttributes(\"unit\", unit.id)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("selectableAttributes(\"room\", room.id)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("selectableAttributes(\"corridor\", corridor.id)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("selectableAttributes(\"door\", door.id)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("state.selection", selectedElementDetails, StringComparison.Ordinal);
            Assert.Contains("selectedVariant(output)", selectedElementDetails, StringComparison.Ordinal);
            Assert.Contains("els.selectionInspector.innerHTML", renderSelectionInspector, StringComparison.Ordinal);
            Assert.Contains("data-inspector-action", inspectorMarkup, StringComparison.Ordinal);
            Assert.Contains(".selection-inspector", styles, StringComparison.Ordinal);
            Assert.Contains(".selected-element", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void InspectorMutationsEditInputAndPreserveGeneratedOutputAsStale()
        {
            string app = ReadWebFile("app.js");
            string handleInspectorAction = SliceFunction(app, "handleInspectorAction");
            string applyInputMutation = SliceFunction(app, "applyInputMutation");
            string adjustUnitTarget = SliceFunction(app, "adjustUnitTargetFromInspector");

            Assert.Contains("[data-inspector-action]", handleInspectorAction, StringComparison.Ordinal);
            Assert.Contains("adjustFloorplateFromInspector(action)", handleInspectorAction, StringComparison.Ordinal);
            Assert.Contains("adjustCoreFromInspector(action)", handleInspectorAction, StringComparison.Ordinal);
            Assert.Contains("adjustUnitTargetFromInspector(action, detail)", handleInspectorAction, StringComparison.Ordinal);
            Assert.Contains("applyRoomMinimumFromInspector(detail)", handleInspectorAction, StringComparison.Ordinal);
            Assert.Contains("applyCorridorWidthFromInspector(detail)", handleInspectorAction, StringComparison.Ordinal);
            Assert.Contains("const input = ensureInputShape(state.input || {})", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("state.input = input", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("syncFormFromInput(state.input)", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("setEditorFromInput(state.input)", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("saveDraft()", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("markInputDirty(message, autoGenerateDelay)", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("renderAll()", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("ensureUnitTarget(input, type)", adjustUnitTarget, StringComparison.Ordinal);
            Assert.Contains("normalizeUnitTargetRatios(input.program.targetUnitTypes)", adjustUnitTarget, StringComparison.Ordinal);
            AssertBefore(applyInputMutation, "markInputDirty", "renderAll()", "Inspector edits should mark the generated preview stale before rerendering it.");
            Assert.DoesNotContain("state.response = null", applyInputMutation, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"state\.response\s*=(?!=)", applyInputMutation);
            Assert.DoesNotContain("state.selectedVariantId = \"\"", applyInputMutation, StringComparison.Ordinal);
            Assert.DoesNotContain("els.outputJson.textContent =", applyInputMutation, StringComparison.Ordinal);
        }

        private static string ReadWebFile(string fileName)
        {
            return File.ReadAllText(Path.Combine(RepositoryRoot(), "FloorPlanGeneration.Web", "wwwroot", fileName));
        }

        private static string SliceFunction(string source, string functionName)
        {
            int start = source.IndexOf("function " + functionName, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing function " + functionName + ".");
            int end = source.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
            return end > start ? source.Substring(start, end - start) : source.Substring(start);
        }

        private static void AssertBefore(string source, string earlier, string later, string message)
        {
            int earlierIndex = source.IndexOf(earlier, StringComparison.Ordinal);
            int laterIndex = source.IndexOf(later, StringComparison.Ordinal);
            Assert.True(earlierIndex >= 0, "Missing expected text: " + earlier + ".");
            Assert.True(laterIndex >= 0, "Missing expected text: " + later + ".");
            Assert.True(earlierIndex < laterIndex, message);
        }

        private static string RepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        }
    }
}

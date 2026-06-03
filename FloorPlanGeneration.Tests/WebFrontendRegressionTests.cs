using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FloorPlanGeneration.Tests
{
    public sealed class WebFrontendRegressionTests
    {
        private const int MaxWebSourceLineLength = 160;

        [Fact]
        public void WebAssetsAvoidVeryLongSourceLines()
        {
            List<string> violations = new List<string>();
            foreach (string fileName in new[] { "app.js", "index.html", "styles.css" })
            {
                string path = Path.Combine(RepositoryRoot(), "FloorPlanGeneration.Web", "wwwroot", fileName);
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Length > MaxWebSourceLineLength)
                    {
                        violations.Add(fileName + ":" + (i + 1) + " has " + lines[i].Length + " characters");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                "Web source lines over " + MaxWebSourceLineLength + " characters:\n" + string.Join("\n", violations));
        }

        [Fact]
        public void SourceFilesAvoidVeryLongLines()
        {
            List<string> violations = new List<string>();
            foreach (string path in EnumerateSourceFiles())
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Length > MaxWebSourceLineLength)
                    {
                        violations.Add(RelativePath(path) + ":" + (i + 1) + " has " + lines[i].Length + " characters");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                "Source lines over " + MaxWebSourceLineLength + " characters:\n" + string.Join("\n", violations));
        }

        [Fact]
        public void WebAssetsAvoidInlineStylesSoCspCanStayStrict()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string polygonEl = SliceFunction(app, "polygonEl");
            string rectEl = SliceFunction(app, "rectEl");
            string lineEl = SliceFunction(app, "lineEl");
            string svgEl = SliceFunction(app, "svgEl");

            Assert.DoesNotContain("style=\"", app, StringComparison.Ordinal);
            Assert.DoesNotContain("style=", app, StringComparison.Ordinal);
            Assert.DoesNotContain("style:", app, StringComparison.Ordinal);
            Assert.DoesNotContain(".style.", app, StringComparison.Ordinal);
            Assert.DoesNotContain("setAttribute(\"style", app, StringComparison.Ordinal);
            Assert.DoesNotContain("style=\"", index, StringComparison.Ordinal);
            Assert.DoesNotContain("style=", index, StringComparison.Ordinal);
            Assert.DoesNotContain("style:", index, StringComparison.Ordinal);
            Assert.Contains("els.emptyPreview.hidden", app, StringComparison.Ordinal);
            Assert.Contains("class=\"score-bar\"", app, StringComparison.Ordinal);
            Assert.Contains("node-depth-", app, StringComparison.Ordinal);
            Assert.DoesNotContain("style", polygonEl, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("style", rectEl, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("style", lineEl, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("style", svgEl, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".clipboard-scratch", styles, StringComparison.Ordinal);
            Assert.Contains(".score-bar::-webkit-progress-value", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void DefaultSvgLabelsStayOutOfPlanReviewMode()
        {
            string app = ReadWebFile("app.js");
            string renderPreview = SliceFunction(app, "renderPreview");
            string renderRoomLabels = SliceFunction(app, "renderRoomLabels");

            Assert.Contains("const labels = variant.labels || []", renderPreview, StringComparison.Ordinal);
            Assert.Contains("if (state.viewMode === \"circulation\")", renderPreview, StringComparison.Ordinal);
            Assert.Contains("class: \"svg-label unit-label\"", renderPreview, StringComparison.Ordinal);
            Assert.Contains("text.textContent = shortUnitType(unit.type)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("renderRoomLabels(variant)", renderPreview, StringComparison.Ordinal);
            AssertBefore(
                renderPreview,
                "if (state.viewMode === \"circulation\")",
                "class: \"svg-label unit-label\"",
                "Generated unit labels should only be added for the circulation overlay, not default review mode.");
            Assert.Contains("if (!state.editMode || state.viewMode !== \"circulation\")", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("return;", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("class: \"svg-label room-label\"", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("compactRoomLabel(room)", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("shouldShowRoomLabel(room, bounds, densePlan)", renderRoomLabels, StringComparison.Ordinal);
            Assert.DoesNotContain("labelText(label.text)", renderPreview, StringComparison.Ordinal);
        }

        [Fact]
        public void SetupEditsPreserveGeneratedResponseUntilRegeneration()
        {
            string app = ReadWebFile("app.js");
            string handler = SliceFunction(app, "handleSetupInput");

            Assert.DoesNotContain("state.response = null", handler, StringComparison.Ordinal);
            Assert.DoesNotContain("state.selectedVariantId = \"\"", handler, StringComparison.Ordinal);
            Assert.Contains("markInputDirty(\"Updating plan\", 650)", handler, StringComparison.Ordinal);
            AssertBefore(
                handler,
                "markInputDirty",
                "renderAll()",
                "The stale state should be marked before renderAll so the generated preview is visibly preserved as stale.");
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
            Assert.Contains("state.dragEdit = null", setInput, StringComparison.Ordinal);
            Assert.Contains("state.selection = null", setInput, StringComparison.Ordinal);
            Assert.Contains("state.editReadout = state.editMode ? editSummary(state.input) : \"\"", setInput, StringComparison.Ordinal);
            Assert.Contains("setInput(parsed, { preserveResponse: true })", applyJson, StringComparison.Ordinal);
            Assert.Contains("setInput(parsed, { preserveResponse: true })", formatInput, StringComparison.Ordinal);
        }

        [Fact]
        public void StaleOutputsDisableExportsAndSaveSvg()
        {
            string app = ReadWebFile("app.js");
            string renderAll = SliceFunction(app, "renderAll");
            string updateExportActions = SliceFunction(app, "updateExportActions");
            string updateDirtyState = SliceFunction(app, "updateDirtyState");

            Assert.Contains("updateExportActions(output)", renderAll, StringComparison.Ordinal);
            Assert.Contains("const exportReady = Boolean(output && !state.inputDirty && !state.busy)", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("copy-rhino", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("copy-ifc", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("download-json", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("copy-json", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("save-svg", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("const staleGeneratedPreview = Boolean(state.previewStale)", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("button.disabled = !exportReady ||", updateExportActions, StringComparison.Ordinal);
            Assert.Contains(
                "els.saveSvgBtn.disabled = !exportReady || !hasPreview || staleGeneratedPreview",
                updateExportActions,
                StringComparison.Ordinal);
            Assert.Contains(
                "const stalePreview = Boolean((state.inputDirty && state.response) || state.previewStale)",
                updateDirtyState,
                StringComparison.Ordinal);
            Assert.Contains("els.previewFrame.classList.toggle(\"is-stale\", stalePreview)", updateDirtyState, StringComparison.Ordinal);
            Assert.Contains("els.planSvg.classList.toggle(\"stale-preview\", stalePreview)", updateDirtyState, StringComparison.Ordinal);
        }

        [Fact]
        public void FailedOutputsKeepDiagnosticJsonButDisableVariantOnlyExports()
        {
            string app = ReadWebFile("app.js");
            string updateExportActions = SliceFunction(app, "updateExportActions");
            string handleExportAction = SliceFunction(app, "handleExportAction");

            Assert.Contains("const hasVariant = Boolean(selectedVariant(output))", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("const rawOutputActions = new Set", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("const variantRequiredActions = new Set", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("rawOutputActions.has(action)", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("variantRequiredActions.has(action)", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("!hasVariant", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("variantRequiredActions.has(action) && !selectedVariant(output)", handleExportAction, StringComparison.Ordinal);
            Assert.Contains("Generate a variant before exporting adapter payloads", handleExportAction, StringComparison.Ordinal);
        }

        [Fact]
        public void NonBlockingDiagnosticsUseReviewNoteLanguage()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string renderSubtitles = SliceFunction(app, "renderSubtitles");
            string renderDiagnostics = SliceFunction(app, "renderDiagnostics");
            string diagnosticSummary = SliceFunction(app, "diagnosticSummary");
            string diagnosticSubtitleText = SliceFunction(app, "diagnosticSubtitleText");

            Assert.Contains("<h3>Review Notes</h3>", index, StringComparison.Ordinal);
            Assert.Contains("diagnosticSummary(output)", renderSubtitles, StringComparison.Ordinal);
            Assert.Contains("diagnosticSubtitleText(summary)", renderSubtitles, StringComparison.Ordinal);
            Assert.DoesNotContain("issue${issueCount", renderSubtitles, StringComparison.Ordinal);

            Assert.Contains("summary.errorCount", renderDiagnostics, StringComparison.Ordinal);
            Assert.Contains("review note", renderDiagnostics, StringComparison.Ordinal);
            Assert.Contains("const diagnostics = collectDiagnostics(output)", diagnosticSummary, StringComparison.Ordinal);
            Assert.Contains("review note", diagnosticSubtitleText, StringComparison.Ordinal);
        }

        [Fact]
        public void FetchJsonUsesGenericFallbackForNonJsonErrorBodies()
        {
            string app = ReadWebFile("app.js");
            string fetchJson = SliceFunction(app, "fetchJson");

            Assert.DoesNotContain("let message = text", fetchJson, StringComparison.Ordinal);
            Assert.Contains("throw new Error(httpErrorMessage(response, text))", fetchJson, StringComparison.Ordinal);
            Assert.Contains("function httpErrorMessage", app, StringComparison.Ordinal);
            Assert.Contains("JSON.parse(text)", app, StringComparison.Ordinal);
            Assert.Contains("parsed.message", app, StringComparison.Ordinal);
            Assert.Contains("Request failed (${response.status || \"network\"})", app, StringComparison.Ordinal);
        }

        [Fact]
        public void DiagnosticsRendererBuildsMarkupBeforeSingleAssignment()
        {
            string app = ReadWebFile("app.js");
            string renderDiagnostics = SliceFunction(app, "renderDiagnostics");

            Assert.DoesNotContain("innerHTML +=", renderDiagnostics, StringComparison.Ordinal);
            Assert.Contains("const hiddenDiagnosticMarkup = hiddenDiagnosticCount > 0", renderDiagnostics, StringComparison.Ordinal);
            Assert.Contains("els.diagnosticList.innerHTML = diagnosticMarkup + hiddenDiagnosticMarkup", renderDiagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public void FailedRegenerationKeepsLastGeneratedPreviewVisible()
        {
            string app = ReadWebFile("app.js");
            string setInput = SliceFunction(app, "setInput");
            string runEngine = SliceFunction(app, "runEngine");
            string renderAll = SliceFunction(app, "renderAll");
            string currentVisualOutput = SliceFunction(app, "currentVisualOutput");
            string updateDirtyState = SliceFunction(app, "updateDirtyState");
            string renderError = SliceFunction(app, "renderError");
            string previewOutput = SliceFunction(app, "previewOutput");

            Assert.Contains("lastPreviewResponse: null", app, StringComparison.Ordinal);
            Assert.Contains("previewStale: false", app, StringComparison.Ordinal);
            Assert.Contains("state.lastPreviewResponse = null", setInput, StringComparison.Ordinal);
            Assert.Contains("state.previewStale = false", setInput, StringComparison.Ordinal);
            Assert.Contains("const hasPreview = hasGeneratedVariant(response.output)", runEngine, StringComparison.Ordinal);
            Assert.Contains("state.lastPreviewResponse = response", runEngine, StringComparison.Ordinal);
            Assert.Contains("state.previewStale = !hasPreview && Boolean(state.lastPreviewResponse)", runEngine, StringComparison.Ordinal);
            Assert.Contains("const visualOutput = currentVisualOutput()", renderAll, StringComparison.Ordinal);
            Assert.Contains("return previewOutput(output)", currentVisualOutput, StringComparison.Ordinal);
            Assert.Contains("renderPreview(visualOutput)", renderAll, StringComparison.Ordinal);
            Assert.Contains("renderDiagnostics(output)", renderAll, StringComparison.Ordinal);
            Assert.Contains("renderExportSummary(output)", renderAll, StringComparison.Ordinal);
            Assert.Contains("updateExportActions(output)", renderAll, StringComparison.Ordinal);
            Assert.Contains("state.previewStale", updateDirtyState, StringComparison.Ordinal);
            Assert.Contains("state.previewStale = Boolean(state.lastPreviewResponse)", renderError, StringComparison.Ordinal);
            Assert.Contains("return state.lastPreviewResponse ? state.lastPreviewResponse.output : output", previewOutput, StringComparison.Ordinal);
            Assert.Contains("action === \"save-svg\" && state.previewStale", app, StringComparison.Ordinal);
        }

        [Fact]
        public void FailedVariantsWithoutGeneratedUnitsDoNotReplaceLastGeneratedPreview()
        {
            string app = ReadWebFile("app.js");
            string hasGeneratedVariant = SliceFunction(app, "hasGeneratedVariant");

            Assert.Contains("variants.some(hasUsableVariantGeometry)", hasGeneratedVariant, StringComparison.Ordinal);
            Assert.Contains("function hasUsableVariantGeometry", app, StringComparison.Ordinal);
            Assert.Contains("(variant.units || []).length > 0", app, StringComparison.Ordinal);
            Assert.Contains("(variant.rooms || []).length > 0", app, StringComparison.Ordinal);
            Assert.DoesNotContain("output.variants.length > 0", hasGeneratedVariant, StringComparison.Ordinal);
        }

        [Fact]
        public void BusyAndDragStatesDoNotDoubleRunOrExportStaleOutput()
        {
            string app = ReadWebFile("app.js");
            string setInput = SliceFunction(app, "setInput");
            string markInputDirty = SliceFunction(app, "markInputDirty");
            string runEngine = SliceFunction(app, "runEngine");
            string setBusy = SliceFunction(app, "setBusy");
            string updateExportActions = SliceFunction(app, "updateExportActions");
            string handlePlanPointerMove = SliceFunction(app, "handlePlanPointerMove");
            string finishPlanPointerEdit = SliceFunction(app, "finishPlanPointerEdit");

            Assert.Contains("runSerial: 0", app, StringComparison.Ordinal);
            Assert.Contains("inputRevision: 0", app, StringComparison.Ordinal);
            Assert.Contains("pendingRunMode: \"\"", app, StringComparison.Ordinal);
            Assert.Contains("state.inputRevision += 1", setInput, StringComparison.Ordinal);
            Assert.Contains("state.runSerial += 1", setInput, StringComparison.Ordinal);
            Assert.Contains("state.inputRevision += 1", markInputDirty, StringComparison.Ordinal);
            Assert.Contains("state.runSerial += 1", markInputDirty, StringComparison.Ordinal);
            Assert.Contains("if (state.busy)", markInputDirty, StringComparison.Ordinal);
            Assert.Contains("state.pendingRunMode = \"generate\"", markInputDirty, StringComparison.Ordinal);
            Assert.Contains("autoGenerateDelay !== null", markInputDirty, StringComparison.Ordinal);
            Assert.Contains("autoGenerateDelay !== undefined", markInputDirty, StringComparison.Ordinal);
            Assert.Contains("markInputDirty(\"Editing plan\", null)", handlePlanPointerMove, StringComparison.Ordinal);
            Assert.Contains("markInputDirty(\"Regenerating edited plan\", 120)", finishPlanPointerEdit, StringComparison.Ordinal);
            Assert.Contains("if (state.busy)", runEngine, StringComparison.Ordinal);
            Assert.Contains("state.pendingRunMode === \"generate\" || !validateOnly", runEngine, StringComparison.Ordinal);
            Assert.Contains("const requestInputRevision = state.inputRevision", runEngine, StringComparison.Ordinal);
            Assert.Contains("state.busyRunId = runId", runEngine, StringComparison.Ordinal);
            Assert.Contains("runId !== state.runSerial || requestInputRevision !== state.inputRevision", runEngine, StringComparison.Ordinal);
            Assert.Contains("state.previewStale = Boolean(state.lastPreviewResponse)", runEngine, StringComparison.Ordinal);
            Assert.Contains("state.pendingRunMode = state.pendingRunMode || \"generate\"", runEngine, StringComparison.Ordinal);
            Assert.Contains("state.busyRunId === runId", runEngine, StringComparison.Ordinal);
            Assert.Contains("if (!state.busy && state.pendingRunMode)", runEngine, StringComparison.Ordinal);
            Assert.Contains("const nextMode = state.pendingRunMode", runEngine, StringComparison.Ordinal);
            Assert.Contains("state.pendingRunMode = \"\"", runEngine, StringComparison.Ordinal);
            Assert.Contains("runEngine(nextMode === \"validate\")", runEngine, StringComparison.Ordinal);
            Assert.Contains("state.busy = busy", setBusy, StringComparison.Ordinal);
            Assert.Contains("els.setupGenerateBtn", setBusy, StringComparison.Ordinal);
            Assert.Contains("updateExportActions", setBusy, StringComparison.Ordinal);
            Assert.Contains("const exportReady = Boolean(output && !state.inputDirty && !state.busy)", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("copy-hypergraph", updateExportActions, StringComparison.Ordinal);
            Assert.Contains("download-hypergraph", updateExportActions, StringComparison.Ordinal);
        }

        [Fact]
        public void DesignBriefKeepsEssentialControlsVisibleForNontechnicalUsers()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string bindEvents = SliceFunction(app, "bindEvents");
            string renderAll = SliceFunction(app, "renderAll");
            string renderSetupGuide = SliceFunction(app, "renderSetupGuide");
            string buildSetupReview = SliceFunction(app, "buildSetupReview");

            Assert.Contains("setupStep: \"floorplate\"", app, StringComparison.Ordinal);
            Assert.Contains("const setupSteps = [\"floorplate\", \"core\", \"rules\", \"mix\", \"generate\"]", app, StringComparison.Ordinal);
            Assert.Contains("data-setup-step-button=\"floorplate\"", index, StringComparison.Ordinal);
            Assert.Contains("data-setup-step-button=\"generate\"", index, StringComparison.Ordinal);
            Assert.Contains("data-setup-step=\"floorplate\"", index, StringComparison.Ordinal);
            Assert.Contains("data-setup-step=\"generate\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"setupReview\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"setupPrevBtn\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"setupNextBtn\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"setupGenerateBtn\"", index, StringComparison.Ordinal);
            Assert.Contains("Design Brief", index, StringComparison.Ordinal);
            Assert.Contains("class=\"brief-prompt\"", index, StringComparison.Ordinal);
            Assert.Contains("class=\"template-strip\"", index, StringComparison.Ordinal);
            Assert.Contains("setSetupStep(button.dataset.setupStepButton)", bindEvents, StringComparison.Ordinal);
            Assert.Contains("moveSetupStep(-1)", bindEvents, StringComparison.Ordinal);
            Assert.Contains("moveSetupStep(1)", bindEvents, StringComparison.Ordinal);
            Assert.Contains("await runEngine(false)", bindEvents, StringComparison.Ordinal);
            Assert.Contains("renderSetupGuide(output)", renderAll, StringComparison.Ordinal);
            Assert.Contains("panel.hidden = false", renderSetupGuide, StringComparison.Ordinal);
            Assert.Contains("els.setupPrevBtn.hidden = true", renderSetupGuide, StringComparison.Ordinal);
            Assert.Contains("els.setupNextBtn.hidden = true", renderSetupGuide, StringComparison.Ordinal);
            Assert.Contains("els.setupGenerateBtn.hidden = false", renderSetupGuide, StringComparison.Ordinal);
            Assert.Contains("buildSetupReview(output)", renderSetupGuide, StringComparison.Ordinal);
            Assert.Contains("Readiness", buildSetupReview, StringComparison.Ordinal);
            Assert.Contains(".guided-steps {\n  display: none;", styles.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.Contains(".brief-prompt", styles, StringComparison.Ordinal);
            Assert.Contains(".template-strip", styles, StringComparison.Ordinal);
            Assert.Contains(".setup-guide-actions", styles, StringComparison.Ordinal);
            Assert.Contains(".setup-review-card", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void MobileWorkbenchKeepsOnlyBriefPlanAndReviewAreas()
        {
            string styles = ReadWebFile("styles.css");
            string mobileMedia = SliceCssBlock(styles, "@media (max-width: 920px)");

            Assert.Contains("\"setup\"", mobileMedia, StringComparison.Ordinal);
            Assert.Contains("\"plan\"", mobileMedia, StringComparison.Ordinal);
            Assert.Contains("\"review\"", mobileMedia, StringComparison.Ordinal);
            Assert.DoesNotContain("\"schedule\"", mobileMedia, StringComparison.Ordinal);
            Assert.DoesNotContain("\"hypergraph\"", mobileMedia, StringComparison.Ordinal);
            Assert.Contains(".schedule-panel,\n.hypergraph-panel", styles.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.Contains("display: none", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void TopNavUsesControlledHashScrollBelowStickyHeader()
        {
            string app = ReadWebFile("app.js");
            string bindEvents = SliceFunction(app, "bindEvents");
            string navigateToHash = SliceFunction(app, "navigateToHash");
            string scrollToHashTarget = SliceFunction(app, "scrollToHashTarget");
            string normalizeNavHash = SliceFunction(app, "normalizeNavHash");

            Assert.Contains("event.preventDefault()", bindEvents, StringComparison.Ordinal);
            Assert.Contains("navigateToHash(link.getAttribute(\"href\"))", bindEvents, StringComparison.Ordinal);
            Assert.Contains("window.addEventListener(\"hashchange\"", bindEvents, StringComparison.Ordinal);
            Assert.Contains("scrollToHashTarget()", bindEvents, StringComparison.Ordinal);
            Assert.Contains("scrollToHashTarget(window.location.hash, true)", app, StringComparison.Ordinal);
            Assert.Contains("window.location.hash = hash", navigateToHash, StringComparison.Ordinal);
            Assert.Contains("document.getElementById(hash.slice(1))", scrollToHashTarget, StringComparison.Ordinal);
            Assert.Contains("topbar.getBoundingClientRect().height", scrollToHashTarget, StringComparison.Ordinal);
            Assert.Contains("window.scrollTo", scrollToHashTarget, StringComparison.Ordinal);
            Assert.Contains("\"#plan\"", normalizeNavHash, StringComparison.Ordinal);
        }

        [Fact]
        public void EditModeControlsAreExplicitAndGateCanvasEditHooks()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string normalizedStyles = styles.Replace("\r\n", "\n", StringComparison.Ordinal);
            string handleCanvasAction = SliceFunction(app, "handleCanvasAction");
            string handlePlanPointerDown = SliceFunction(app, "handlePlanPointerDown");
            string applyCanvasEdit = SliceFunction(app, "applyCanvasEdit");
            string renderInputEditHandles = SliceFunction(app, "renderInputEditHandles");
            string renderSelectionConstraintHandles = SliceFunction(app, "renderSelectionConstraintHandles");
            string editHandle = SliceFunction(app, "editHandle");

            Assert.Contains("editMode: false", app, StringComparison.Ordinal);
            Assert.Contains("data-canvas-action=\"edit-toggle\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"editReadout\"", index, StringComparison.Ordinal);
            Assert.Contains("action === \"edit-toggle\"", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("state.editMode = !state.editMode", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("state.selection = { kind: \"floorplate\", id: \"floorplate\" }", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("button.dataset.canvasAction === \"edit-toggle\"", app, StringComparison.Ordinal);
            Assert.Contains("editToggle.setAttribute(\"aria-pressed\", editActive ? \"true\" : \"false\")", app, StringComparison.Ordinal);
            Assert.Contains("els.planSvg.addEventListener(\"pointerdown\", handlePlanPointerDown)", app, StringComparison.Ordinal);
            Assert.Contains("!state.editMode", handlePlanPointerDown, StringComparison.Ordinal);
            Assert.Contains("function applyCanvasEdit", app, StringComparison.Ordinal);
            Assert.Contains("state.editMode", renderInputEditHandles, StringComparison.Ordinal);
            Assert.Contains("editHandleLabel(\"Width\"", renderInputEditHandles, StringComparison.Ordinal);
            Assert.Contains("editHandleLabel(\"Move\"", renderInputEditHandles, StringComparison.Ordinal);
            Assert.Contains("const handleInset", renderInputEditHandles, StringComparison.Ordinal);
            Assert.Contains("const dx = point.x - edit.startPoint.x", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("const dy = point.y - edit.startPoint.y", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("floorBounds.width + dx", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("floorBounds.height + dy", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("state.editMode", renderSelectionConstraintHandles, StringComparison.Ordinal);
            Assert.Contains("detail.source !== \"generated\"", renderSelectionConstraintHandles, StringComparison.Ordinal);
            Assert.Contains("data-edit-action", editHandle, StringComparison.Ordinal);
            Assert.Contains("class: `edit-handle edit-${action}`", editHandle, StringComparison.Ordinal);
            Assert.Contains(".edit-readout", styles, StringComparison.Ordinal);
            Assert.Contains(".edit-handle", styles, StringComparison.Ordinal);
            Assert.Contains(".edit-handle-label", styles, StringComparison.Ordinal);
            Assert.Contains(".edit-selection-box", styles, StringComparison.Ordinal);
            Assert.Contains(".canvas-tools {\n  position: absolute;\n  z-index: 2;\n  left: 50%;\n  bottom: 16px;", normalizedStyles, StringComparison.Ordinal);
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
            string renderDimensionGuides = SliceFunction(app, "renderDimensionGuides");
            string renderPlanGlyphLayer = SliceFunction(app, "renderPlanGlyphLayer");
            string renderDaylightAndWindowBands = SliceFunction(app, "renderDaylightAndWindowBands");
            string renderRoomFixtures = SliceFunction(app, "renderRoomFixtures");
            string appendRoomFixture = SliceFunction(app, "appendRoomFixture");
            string renderSelectedRoomHalo = SliceFunction(app, "renderSelectedRoomHalo");
            string renderDoorOpening = SliceFunction(app, "renderDoorOpening");
            string renderRoomLabels = SliceFunction(app, "renderRoomLabels");
            string compactRoomLabel = SliceFunction(app, "compactRoomLabel");
            string displayRoomType = SliceFunction(app, "displayRoomType");

            Assert.Contains("Selection", index, StringComparison.Ordinal);
            Assert.Contains("Constraints", index, StringComparison.Ordinal);
            Assert.Contains("id=\"selectionInspector\"", index, StringComparison.Ordinal);
            Assert.Contains("Room Schedule", index, StringComparison.Ordinal);
            Assert.Contains("id=\"roomScheduleList\"", index, StringComparison.Ordinal);
            Assert.Contains("els.selectionInspector.addEventListener(\"click\", handleInspectorAction)", app, StringComparison.Ordinal);
            Assert.Contains("els.roomScheduleList.addEventListener(\"click\", handleRoomScheduleClick)", app, StringComparison.Ordinal);
            Assert.Contains("renderSelectionInspector(visualOutput)", renderAll, StringComparison.Ordinal);
            Assert.Contains("renderRoomReviewSchedule(visualOutput)", renderAll, StringComparison.Ordinal);
            Assert.Contains("data-select-kind", selectableAttributes, StringComparison.Ordinal);
            Assert.Contains("data-select-id", selectableAttributes, StringComparison.Ordinal);
            Assert.Contains("selected-element", app, StringComparison.Ordinal);
            Assert.Contains("selectableAttributes(\"floorplate\", \"floorplate\")", renderPreview, StringComparison.Ordinal);
            Assert.Contains("selectableAttributes(\"unit\", unit.id)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("selectableAttributes(\"room\", room.id)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("selectableAttributes(\"corridor\", corridor.id)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("selectableAttributes(\"door\", door.id)", renderDoorOpening, StringComparison.Ordinal);
            Assert.Contains("renderDimensionGuides(bounds, metadata && metadata.projectUnits)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("renderPlanGlyphLayer(group, variant, bounds)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("renderSelectedRoomHalo(group, variant)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("renderWallSegment(group, wall)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("renderDoorOpening(group, door, wallById.get(door.hostWall), bounds)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("renderRoomLabels(variant)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("renderScaleBar(group, bounds, units || \"m\", offset)", renderDimensionGuides, StringComparison.Ordinal);
            Assert.Contains("renderDaylightAndWindowBands(layer, variant, bounds)", renderPlanGlyphLayer, StringComparison.Ordinal);
            Assert.Contains("renderCorridorCenterlines(layer, variant)", renderPlanGlyphLayer, StringComparison.Ordinal);
            Assert.Contains("renderRoomFixtures(layer, variant)", renderPlanGlyphLayer, StringComparison.Ordinal);
            Assert.Contains("room.daylight", renderDaylightAndWindowBands, StringComparison.Ordinal);
            Assert.Contains("closestFacadeSide(bounds, floorBounds)", renderDaylightAndWindowBands, StringComparison.Ordinal);
            Assert.Contains("appendRoomFixture(fixtures, room)", renderRoomFixtures, StringComparison.Ordinal);
            Assert.Contains("room.roomType || room.type", appendRoomFixture, StringComparison.Ordinal);
            Assert.Contains("appendBedroomFixture(group, bounds)", appendRoomFixture, StringComparison.Ordinal);
            Assert.Contains("appendBathroomFixture(group, bounds)", appendRoomFixture, StringComparison.Ordinal);
            Assert.Contains("appendKitchenFixture(group, bounds)", appendRoomFixture, StringComparison.Ordinal);
            Assert.Contains("appendLivingFixture(group, bounds)", appendRoomFixture, StringComparison.Ordinal);
            Assert.Contains("state.selection.kind !== \"room\"", renderSelectedRoomHalo, StringComparison.Ordinal);
            Assert.Contains("room-selected-halo", renderSelectedRoomHalo, StringComparison.Ordinal);
            Assert.Contains("door.hostWall", renderDoorOpening, StringComparison.Ordinal);
            Assert.Contains("door.width", renderDoorOpening, StringComparison.Ordinal);
            Assert.Contains("door-gap", renderDoorOpening, StringComparison.Ordinal);
            Assert.Contains("state.viewMode !== \"circulation\"", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("compactRoomLabel(room)", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("return \"BED\"", compactRoomLabel, StringComparison.Ordinal);
            Assert.Contains("return \"BATH\"", compactRoomLabel, StringComparison.Ordinal);
            Assert.Contains("return \"KIT\"", compactRoomLabel, StringComparison.Ordinal);
            Assert.Contains("room.roomType", displayRoomType, StringComparison.Ordinal);
            Assert.Contains("state.selection", selectedElementDetails, StringComparison.Ordinal);
            Assert.Contains("selectedVariant(output)", selectedElementDetails, StringComparison.Ordinal);
            Assert.Contains("unitForRoom(variant, room)", selectedElementDetails, StringComparison.Ordinal);
            Assert.Contains("unit: parentUnit", selectedElementDetails, StringComparison.Ordinal);
            Assert.Contains("function unitForRoom", app, StringComparison.Ordinal);
            Assert.Contains("els.selectionInspector.innerHTML", renderSelectionInspector, StringComparison.Ordinal);
            Assert.Contains("data-inspector-action", inspectorMarkup, StringComparison.Ordinal);
            Assert.Contains("Unit type", inspectorMarkup, StringComparison.Ordinal);
            Assert.Contains("displayRoomType(detail.item)", inspectorMarkup, StringComparison.Ordinal);
            Assert.Contains("detail.item.daylight", inspectorMarkup, StringComparison.Ordinal);
            Assert.Contains("detail.item.dimensions.width", inspectorMarkup, StringComparison.Ordinal);
            Assert.Contains("detail.item.thickness", inspectorMarkup, StringComparison.Ordinal);
            Assert.Contains("detail.item.hostWall", inspectorMarkup, StringComparison.Ordinal);
            Assert.Contains("detail.item.connectsSpaces", inspectorMarkup, StringComparison.Ordinal);
            Assert.DoesNotContain("detail.item.hasDaylight", inspectorMarkup, StringComparison.Ordinal);
            Assert.Contains(".selection-inspector", styles, StringComparison.Ordinal);
            Assert.Contains(".room-schedule-list", styles, StringComparison.Ordinal);
            Assert.Contains(".room-schedule-row", styles, StringComparison.Ordinal);
            Assert.Contains(".selected-element", styles, StringComparison.Ordinal);
            Assert.Contains(".dimension-guides", styles, StringComparison.Ordinal);
            Assert.Contains(".wall-hit", styles, StringComparison.Ordinal);
            Assert.Contains(".wall-backdrop", styles, StringComparison.Ordinal);
            Assert.Contains(".plan-visual-layer", styles, StringComparison.Ordinal);
            Assert.Contains(".room-fixtures", styles, StringComparison.Ordinal);
            Assert.Contains(".fixture-bed", styles, StringComparison.Ordinal);
            Assert.Contains(".fixture-sofa", styles, StringComparison.Ordinal);
            Assert.Contains(".fixture-bath", styles, StringComparison.Ordinal);
            Assert.Contains(".fixture-counter", styles, StringComparison.Ordinal);
            Assert.Contains(".daylight-band", styles, StringComparison.Ordinal);
            Assert.Contains(".scale-bar", styles, StringComparison.Ordinal);
            Assert.Contains(".corridor-centerline", styles, StringComparison.Ordinal);
            Assert.Contains(".room-selected-halo", styles, StringComparison.Ordinal);
            Assert.Contains(".room-selected-halo-handle", styles, StringComparison.Ordinal);
            Assert.Contains(".door-gap", styles, StringComparison.Ordinal);
            Assert.Contains(".door-swing", styles, StringComparison.Ordinal);
            Assert.Contains(".room-label", styles, StringComparison.Ordinal);
            Assert.Contains(".room-bedroom", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void TopologyRelationshipsAndGroupedRoomScheduleAreInspectable()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");
            string renderAll = SliceFunction(app, "renderAll");
            string selectedElementDetails = SliceFunction(app, "selectedElementDetails");
            string relationshipsForElement = SliceFunction(app, "relationshipsForElement");
            string relationshipLookupIds = SliceFunction(app, "relationshipLookupIds");
            string graphNodes = SliceFunction(app, "graphNodes");
            string relatedNodesFromHyperedges = SliceFunction(app, "relatedNodesFromHyperedges");
            string relationshipSectionsMarkup = SliceFunction(app, "relationshipSectionsMarkup");
            string relationshipChipGroup = SliceFunction(app, "relationshipChipGroup");
            string handleInspectorAction = SliceFunction(app, "handleInspectorAction");
            string renderRoomReviewSchedule = SliceFunction(app, "renderRoomReviewSchedule");
            string groupRoomsByUnit = SliceFunction(app, "groupRoomsByUnit");
            string unitRoomScheduleGroup = SliceFunction(app, "unitRoomScheduleGroup");
            string roomScheduleRow = SliceFunction(app, "roomScheduleRow");
            string handleRoomScheduleClick = SliceFunction(app, "handleRoomScheduleClick");

            Assert.Contains("renderSelectionInspector(visualOutput)", renderAll, StringComparison.Ordinal);
            Assert.Contains("renderRoomReviewSchedule(visualOutput)", renderAll, StringComparison.Ordinal);
            Assert.Contains("relationships: relationshipsForElement(variant, \"unit\", unit.id, unit)", selectedElementDetails, StringComparison.Ordinal);
            Assert.Contains("relationships: relationshipsForElement(variant, \"room\", room.id, room)", selectedElementDetails, StringComparison.Ordinal);
            Assert.Contains(
                "relationships: relationshipsForElement(variant, \"corridor\", corridor.id, corridor)",
                selectedElementDetails,
                StringComparison.Ordinal);
            Assert.Contains("relationships: relationshipsForElement(variant, \"wall\", wall.id, wall)", selectedElementDetails, StringComparison.Ordinal);
            Assert.Contains("relationships: relationshipsForElement(variant, \"door\", door.id, door)", selectedElementDetails, StringComparison.Ordinal);
            Assert.Contains("const topology = variant.topology || {}", relationshipsForElement, StringComparison.Ordinal);
            Assert.Contains("const hypergraph = topology.hypergraph || {}", relationshipsForElement, StringComparison.Ordinal);
            Assert.Contains("topology.edges || []", relationshipsForElement, StringComparison.Ordinal);
            Assert.Contains("hypergraph.hyperedges || []", relationshipsForElement, StringComparison.Ordinal);
            Assert.Contains(
                "relatedNodesFromHyperedges(variant, hyperedges, selectedIds, \"adjacency\", 8)",
                relationshipsForElement,
                StringComparison.Ordinal);
            Assert.Contains(
                "relatedNodesFromHyperedges(variant, hyperedges, selectedIds, \"circulation_access\", 6)",
                relationshipsForElement,
                StringComparison.Ordinal);
            Assert.Contains(
                "relatedNodesFromHyperedges(variant, hyperedges, selectedIds, \"door\", 4)",
                relationshipsForElement,
                StringComparison.Ordinal);
            Assert.Contains("doorsForElement(variant, selectedIds, item)", relationshipsForElement, StringComparison.Ordinal);
            Assert.Contains("item && item.hostWall", relationshipLookupIds, StringComparison.Ordinal);
            Assert.Contains("item && item.connectsSpaces", relationshipLookupIds, StringComparison.Ordinal);
            Assert.Contains("variant.topology.nodes", graphNodes, StringComparison.Ordinal);
            Assert.Contains("variant.topology.hypergraph.nodes", graphNodes, StringComparison.Ordinal);
            Assert.Contains("edge.members || []", relatedNodesFromHyperedges, StringComparison.Ordinal);
            Assert.Contains("relationshipItemForNode(variant, member.nodeId, edge)", relatedNodesFromHyperedges, StringComparison.Ordinal);
            Assert.Contains("Graph Relations", relationshipSectionsMarkup, StringComparison.Ordinal);
            Assert.Contains("relationship-stats", relationshipSectionsMarkup, StringComparison.Ordinal);
            Assert.Contains("relationshipChipGroup(label, items)", relationshipSectionsMarkup, StringComparison.Ordinal);
            Assert.Contains("data-inspector-select-kind", relationshipChipGroup, StringComparison.Ordinal);
            Assert.Contains("data-inspector-select-id", relationshipChipGroup, StringComparison.Ordinal);
            Assert.Contains("[data-inspector-select-id]", handleInspectorAction, StringComparison.Ordinal);
            Assert.Contains("state.selection = { kind, id }", handleInspectorAction, StringComparison.Ordinal);
            Assert.Contains("const roomsByUnit = groupRoomsByUnit(rooms)", renderRoomReviewSchedule, StringComparison.Ordinal);
            Assert.Contains(
                "unitRoomScheduleGroup(unit, " +
                "roomsByUnit.get(String(unit.id || \"\")) || [], output)",
                renderRoomReviewSchedule,
                StringComparison.Ordinal);
            Assert.Contains("Unassigned rooms", renderRoomReviewSchedule, StringComparison.Ordinal);
            Assert.Contains("roomScheduleRow(room, output)", renderRoomReviewSchedule, StringComparison.Ordinal);
            Assert.Contains("const groups = new Map()", groupRoomsByUnit, StringComparison.Ordinal);
            Assert.Contains("groups.get(unitId).push(room)", groupRoomsByUnit, StringComparison.Ordinal);
            Assert.Contains("data-schedule-unit-id", unitRoomScheduleGroup, StringComparison.Ordinal);
            Assert.Contains("(rooms || []).map((room) => roomScheduleRow(room, output)).join(\"\")", unitRoomScheduleGroup, StringComparison.Ordinal);
            Assert.Contains("data-schedule-room-id", roomScheduleRow, StringComparison.Ordinal);
            Assert.Contains("room.daylight ? \"Daylight\" : \"Interior\"", roomScheduleRow, StringComparison.Ordinal);
            Assert.Contains("state.selection = { kind: \"unit\"", handleRoomScheduleClick, StringComparison.Ordinal);
            Assert.Contains("state.selection = { kind: \"room\"", handleRoomScheduleClick, StringComparison.Ordinal);
            Assert.Contains(".relationship-stats", styles, StringComparison.Ordinal);
            Assert.Contains(".relationship-group", styles, StringComparison.Ordinal);
            Assert.Contains(".relationship-chips", styles, StringComparison.Ordinal);
            Assert.Contains(".room-schedule-group", styles, StringComparison.Ordinal);
            Assert.Contains(".room-schedule-unit", styles, StringComparison.Ordinal);
            Assert.Contains(".room-schedule-total", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void SelectedGeneratedGeometryCanvasEditsMutateInputConstraintsOnly()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");
            string renderPreview = SliceFunction(app, "renderPreview");
            string renderSelectionConstraintHandles = SliceFunction(app, "renderSelectionConstraintHandles");
            string renderPlanQuickActions = SliceFunction(app, "renderPlanQuickActions");
            string handlePlanClick = SliceFunction(app, "handlePlanClick");
            string handlePlanPointerDown = SliceFunction(app, "handlePlanPointerDown");
            string applyCanvasEdit = SliceFunction(app, "applyCanvasEdit");
            string selectionEditSnapshot = SliceFunction(app, "selectionEditSnapshot");
            string runSelectedPlanAction = SliceFunction(app, "runSelectedPlanAction");

            Assert.Contains("renderSelectionConstraintHandles(group, output)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("renderPlanQuickActions(output, bounds)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("data-plan-action", handlePlanClick, StringComparison.Ordinal);
            Assert.Contains("runSelectedPlanAction(planAction.dataset.planAction)", handlePlanClick, StringComparison.Ordinal);
            Assert.Contains("selection: selectionEditSnapshot(detail)", handlePlanPointerDown, StringComparison.Ordinal);
            Assert.Contains("unit-target-area", renderSelectionConstraintHandles, StringComparison.Ordinal);
            Assert.Contains("room-min-size", renderSelectionConstraintHandles, StringComparison.Ordinal);
            Assert.Contains("corridor-width", renderSelectionConstraintHandles, StringComparison.Ordinal);
            Assert.Contains("unit-target-area", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("ensureUnitTarget(input, edit.selection.unitType", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("detail.kind === \"room\" && detail.unit", app, StringComparison.Ordinal);
            Assert.Contains("actions.push([\"unit-more\", \"More like this\"]", app, StringComparison.Ordinal);
            Assert.Contains("input.rules.minRoomWidth", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("input.rules.minRoomDepth", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("input.rules.minCorridorWidth", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("refreshAccessFromCore(input)", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("unitType", selectionEditSnapshot, StringComparison.Ordinal);
            Assert.Contains("corridorWidth(detail)", selectionEditSnapshot, StringComparison.Ordinal);
            Assert.Contains("planActionsForDetail(detail)", runSelectedPlanAction, StringComparison.Ordinal);
            Assert.Contains(".plan-action-chip", styles, StringComparison.Ordinal);
            Assert.Contains(".edit-unit-target-area", styles, StringComparison.Ordinal);
            Assert.Contains(".edit-room-min-size", styles, StringComparison.Ordinal);
            Assert.Contains(".edit-corridor-width", styles, StringComparison.Ordinal);
            Assert.DoesNotContain("state.response =", applyCanvasEdit, StringComparison.Ordinal);
            Assert.DoesNotContain("variant.units", applyCanvasEdit, StringComparison.Ordinal);
            Assert.DoesNotContain("variant.rooms", applyCanvasEdit, StringComparison.Ordinal);
            Assert.DoesNotContain("variant.topology", applyCanvasEdit, StringComparison.Ordinal);
        }

        [Fact]
        public void HypergraphInspectionShowsContractTreeDiagramAndMatrix()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");
            string renderHypergraphPreview = SliceFunction(app, "renderHypergraphPreview");
            string hypergraphDiagramSvg = SliceFunction(app, "hypergraphDiagramSvg");
            string incidencePreview = SliceFunction(app, "incidencePreview");
            string hypergraphContractRows = SliceFunction(app, "hypergraphContractRows");

            Assert.Contains("hypergraphContractRows(hypergraph)", renderHypergraphPreview, StringComparison.Ordinal);
            Assert.Contains("hypergraphDiagramSvg(hypergraph)", renderHypergraphPreview, StringComparison.Ordinal);
            Assert.Contains("incidencePreview(hypergraph)", renderHypergraphPreview, StringComparison.Ordinal);
            Assert.Contains("hypergraphValidationSummary(variant)", renderHypergraphPreview, StringComparison.Ordinal);
            Assert.Contains("Showing ${treeRows.length} of ${totalTreeRows}", renderHypergraphPreview, StringComparison.Ordinal);
            Assert.Contains("DataNode keys", hypergraphContractRows, StringComparison.Ordinal);
            Assert.Contains("name, area, angle, mergeid, final, children, connected, treeNodeMesh", hypergraphContractRows, StringComparison.Ordinal);
            Assert.Contains("role=\"img\"", hypergraphDiagramSvg, StringComparison.Ordinal);
            Assert.Contains("hypergraph-link", hypergraphDiagramSvg, StringComparison.Ordinal);
            Assert.Contains("hypergraph-node", hypergraphDiagramSvg, StringComparison.Ordinal);
            Assert.Contains("hypergraph-edge", hypergraphDiagramSvg, StringComparison.Ordinal);
            Assert.Contains("matrices.nodeOrder", incidencePreview, StringComparison.Ordinal);
            Assert.Contains("matrices.hyperedgeOrder", incidencePreview, StringComparison.Ordinal);
            Assert.Contains("matrix-dot", incidencePreview, StringComparison.Ordinal);
            Assert.Contains("is-connected", incidencePreview, StringComparison.Ordinal);
            Assert.Contains("weighted incidence values", incidencePreview, StringComparison.Ordinal);
            Assert.Contains(".hypergraph-contract-grid", styles, StringComparison.Ordinal);
            Assert.Contains(".contract-status", styles, StringComparison.Ordinal);
            Assert.Contains(".hypergraph-diagram", styles, StringComparison.Ordinal);
            Assert.Contains(".hypergraph-link", styles, StringComparison.Ordinal);
            Assert.Contains(".matrix-dot", styles, StringComparison.Ordinal);
            Assert.Contains(".matrix-caption", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void ExportSummaryUsesActualRhinoIfcAndHypergraphActions()
        {
            string app = ReadWebFile("app.js");
            string renderExportSummary = SliceFunction(app, "renderExportSummary");
            string handleExportAction = SliceFunction(app, "handleExportAction");
            string buildRhinoHandoffText = SliceFunction(app, "buildRhinoHandoffText");

            Assert.Contains("data-export-action=\"copy-rhino\"", renderExportSummary, StringComparison.Ordinal);
            Assert.Contains("data-export-action=\"copy-ifc\"", renderExportSummary, StringComparison.Ordinal);
            Assert.Contains("data-export-action=\"copy-hypergraph\"", renderExportSummary, StringComparison.Ordinal);
            Assert.Contains("data-export-action=\"download-hypergraph\"", renderExportSummary, StringComparison.Ordinal);
            Assert.Contains("button.disabled", handleExportAction, StringComparison.Ordinal);
            Assert.Contains("Regenerate before exporting generated output", handleExportAction, StringComparison.Ordinal);
            Assert.Contains("buildHypergraphText()", handleExportAction, StringComparison.Ordinal);
            Assert.Contains("floor-plan-hypergraph.json", handleExportAction, StringComparison.Ordinal);
            Assert.Contains("geometry: {", buildRhinoHandoffText, StringComparison.Ordinal);
            Assert.Contains("units: variant.units || []", buildRhinoHandoffText, StringComparison.Ordinal);
            Assert.Contains("rooms: variant.rooms || []", buildRhinoHandoffText, StringComparison.Ordinal);
            Assert.Contains("walls: variant.walls || []", buildRhinoHandoffText, StringComparison.Ordinal);
            Assert.Contains("doorsOpenings: variant.doorsOpenings || []", buildRhinoHandoffText, StringComparison.Ordinal);
            Assert.Contains("hypergraph: variant.topology ? variant.topology.hypergraph : null", buildRhinoHandoffText, StringComparison.Ordinal);
        }

        [Fact]
        public void InspectorMutationsEditInputAndPreserveGeneratedOutputAsStale()
        {
            string app = ReadWebFile("app.js");
            string handleInspectorAction = SliceFunction(app, "handleInspectorAction");
            string runSelectedPlanAction = SliceFunction(app, "runSelectedPlanAction");
            string applyInputMutation = SliceFunction(app, "applyInputMutation");
            string adjustUnitTarget = SliceFunction(app, "adjustUnitTargetFromInspector");

            Assert.Contains("[data-inspector-action]", handleInspectorAction, StringComparison.Ordinal);
            Assert.Contains("runSelectedPlanAction(action)", handleInspectorAction, StringComparison.Ordinal);
            Assert.Contains("adjustFloorplateFromInspector(action)", runSelectedPlanAction, StringComparison.Ordinal);
            Assert.Contains("adjustCoreFromInspector(action)", runSelectedPlanAction, StringComparison.Ordinal);
            Assert.Contains("adjustUnitTargetFromInspector(action, detail)", runSelectedPlanAction, StringComparison.Ordinal);
            Assert.Contains("applyRoomMinimumFromInspector(detail)", runSelectedPlanAction, StringComparison.Ordinal);
            Assert.Contains("applyCorridorWidthFromInspector(detail)", runSelectedPlanAction, StringComparison.Ordinal);
            Assert.Contains("const input = ensureInputShape(state.input || {})", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("state.input = input", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("syncFormFromInput(state.input)", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("setEditorFromInput(state.input)", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("saveDraft()", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("markInputDirty(message, autoGenerateDelay)", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("renderAll()", applyInputMutation, StringComparison.Ordinal);
            Assert.Contains("ensureUnitTarget(input, type)", adjustUnitTarget, StringComparison.Ordinal);
            Assert.Contains("normalizeUnitTargetRatios(input.program.targetUnitTypes)", adjustUnitTarget, StringComparison.Ordinal);
            AssertBefore(
                applyInputMutation,
                "markInputDirty",
                "renderAll()",
                "Inspector edits should mark the generated preview stale before rerendering it.");
            Assert.DoesNotContain("state.response = null", applyInputMutation, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"state\.response\s*=(?!=)", applyInputMutation);
            Assert.DoesNotContain("state.selectedVariantId = \"\"", applyInputMutation, StringComparison.Ordinal);
            Assert.DoesNotContain("els.outputJson.textContent =", applyInputMutation, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedPreviewActionsUsePreservedVisualOutputAfterFailedRegeneration()
        {
            string app = ReadWebFile("app.js");
            string runSelectedPlanAction = SliceFunction(app, "runSelectedPlanAction");
            string handlePlanPointerDown = SliceFunction(app, "handlePlanPointerDown");

            Assert.Contains("function currentVisualOutput", app, StringComparison.Ordinal);
            Assert.Contains("selectedElementDetails(currentVisualOutput())", runSelectedPlanAction, StringComparison.Ordinal);
            Assert.Contains("selectedElementDetails(currentVisualOutput())", handlePlanPointerDown, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "selectedElementDetails(state.response ? state.response.output : null)",
                runSelectedPlanAction,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "selectedElementDetails(state.response ? state.response.output : null)",
                handlePlanPointerDown,
                StringComparison.Ordinal);
        }

        private static string ReadWebFile(string fileName)
        {
            return File.ReadAllText(Path.Combine(RepositoryRoot(), "FloorPlanGeneration.Web", "wwwroot", fileName));
        }

        private static IEnumerable<string> EnumerateSourceFiles()
        {
            string root = RepositoryRoot();
            foreach (string directory in new[]
            {
                "FloorPlanGeneration",
                "FloorPlanGeneration.Cli",
                "FloorPlanGeneration.Web",
                "FloorPlanGeneration.Tests"
            })
            {
                foreach (string path in Directory.EnumerateFiles(Path.Combine(root, directory), "*.*", SearchOption.AllDirectories))
                {
                    if (path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string extension = Path.GetExtension(path);
                    if (extension == ".cs" || extension == ".js" || extension == ".css" || extension == ".html")
                    {
                        yield return path;
                    }
                }
            }
        }

        private static string RelativePath(string path)
        {
            return Path.GetRelativePath(RepositoryRoot(), path);
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

        private static string SliceCssBlock(string source, string marker)
        {
            int start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing CSS marker " + marker + ".");
            int next = source.IndexOf("\n@media ", start + marker.Length, StringComparison.Ordinal);
            return next > start ? source.Substring(start, next - start) : source.Substring(start);
        }

        private static string RepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        }
    }
}

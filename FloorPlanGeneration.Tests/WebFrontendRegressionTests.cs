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
        public void SvgRoomLabelsAreGatedBehindTheLabelsToggle()
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

            // Room labels obey the labelsVisible toggle and only render in the 2D plan view
            // (never the circulation or axon overlays), regardless of the default state.
            Assert.Contains("if (!state.labelsVisible || state.viewMode !== \"plan\")", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("return;", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("class: \"svg-label room-label\"", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("compactRoomLabel(room)", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("shouldShowRoomLabel(room, bounds, densePlan)", renderRoomLabels, StringComparison.Ordinal);
            Assert.DoesNotContain("labelText(label.text)", renderPreview, StringComparison.Ordinal);
        }

        [Fact]
        public void LabelsToggleControlsRoomLabelsWithBoundedFontSizes()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string handleCanvasAction = SliceFunction(app, "handleCanvasAction");
            string updateCanvasUi = SliceFunction(app, "updateCanvasUi");
            string renderRoomLabels = SliceFunction(app, "renderRoomLabels");
            string roomLabelFontSize = SliceFunction(app, "roomLabelFontSize");
            string svgStyleElement = SliceFunction(app, "svgStyleElement");

            // Labels default ON so the standard view reads like a construction document
            // (every room named with its area); the canvas toggle can still hide them.
            Assert.Contains("labelsVisible: true", app, StringComparison.Ordinal);
            Assert.Contains("data-canvas-action=\"labels-toggle\"", index, StringComparison.Ordinal);
            Assert.Contains("action === \"labels-toggle\"", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("state.labelsVisible = !state.labelsVisible", handleCanvasAction, StringComparison.Ordinal);

            // The toggle drives both the button state and the CSS gate on the preview frame.
            Assert.Contains("button.dataset.canvasAction === \"labels-toggle\"", updateCanvasUi, StringComparison.Ordinal);
            Assert.Contains("els.previewFrame.classList.toggle(\"labels-on\", labelsActive)", updateCanvasUi, StringComparison.Ordinal);
            Assert.Contains(
                ".preview-frame:not(.labels-on) #planSvg[data-view-mode=\"plan\"] .room-label",
                styles,
                StringComparison.Ordinal);

            // Plan labels are hard-capped in model metres so a bad value can never reproduce
            // the meter-tall SVG text overlays this canvas used to show, including on export.
            Assert.Contains("const maxPlanLabelFontSize = 0.85", app, StringComparison.Ordinal);
            Assert.Contains("maxPlanLabelFontSize", roomLabelFontSize, StringComparison.Ordinal);
            Assert.DoesNotContain("font-size:1.35px", svgStyleElement, StringComparison.Ordinal);
        }

        [Fact]
        public void PlanCanvasKeepsDataDrivenWallHierarchyAndCalmRoomTints()
        {
            string styles = ReadWebFile("styles.css");
            string normalizedStyles = styles.Replace("\r\n", "\n", StringComparison.Ordinal);

            // Walls are solid ink poché; hierarchy comes from the per-wall thickness
            // geometry, so the base .wall rule must not fight it with stroke-width.
            Assert.Contains(".wall {\n  fill: var(--ink-900);\n  stroke: none;", normalizedStyles, StringComparison.Ordinal);
            Assert.Contains(".wall-unit_demising", styles, StringComparison.Ordinal);
            Assert.Contains("exterior and demising walls are simply thicker", styles, StringComparison.Ordinal);

            // Flat near-white zone tints — opaque, so alpha never stacks into mud.
            Assert.Contains("Flat near-white zone tints", styles, StringComparison.Ordinal);
            Assert.Contains(".room.room-bedroom", styles, StringComparison.Ordinal);
            Assert.Contains("fill: #f8f5ef", styles, StringComparison.Ordinal);
            Assert.Contains(".room.room-living", styles, StringComparison.Ordinal);
            Assert.Contains("fill: #faf6ee", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void MakeoverDesignSystemAndSidebarControlsArePresent()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string handleStepperClick = SliceFunction(app, "handleStepperClick");

            // Design tokens (calm teal accent, hairlines, radius/shadow scales).
            Assert.Contains("--teal-fill:", styles, StringComparison.Ordinal);
            Assert.Contains("--hairline:", styles, StringComparison.Ordinal);
            Assert.Contains("--radius-pill:", styles, StringComparison.Ordinal);
            Assert.Contains("--font-sans:", styles, StringComparison.Ordinal);

            // Rounded, softly shadowed panels (the flat box-shadow:none / radius:0 is gone).
            Assert.Contains("border-radius: var(--radius-xl)", styles, StringComparison.Ordinal);
            Assert.Contains("0 10px 28px rgba(15, 23, 42, 0.05)", styles, StringComparison.Ordinal);

            // Segmented pill nav with a flat teal active state. The decorative user
            // avatar was deliberately retired — the chrome carries no fake account UI.
            Assert.Contains("background: var(--teal-fill)", styles, StringComparison.Ordinal);
            Assert.DoesNotContain(".user-avatar", styles, StringComparison.Ordinal);

            // Intentional sidebar controls: steppers, range slider, custom preference checkboxes.
            Assert.Contains(".stepper", styles, StringComparison.Ordinal);
            Assert.Contains("input[type=\"range\"]::-webkit-slider-thumb", styles, StringComparison.Ordinal);
            Assert.Contains(".pref-row input[type=\"checkbox\"]:checked", styles, StringComparison.Ordinal);
            Assert.Contains(".status-pill", styles, StringComparison.Ordinal);

            // CSP-safe SVG chevron for selects (no remote URL, no icon font).
            Assert.Contains("background-image: url(\"data:image/svg+xml,", styles, StringComparison.Ordinal);

            // Markup wiring kept ids/hooks; added decorative + progressive-enhancement nodes only.
            Assert.Contains("<h1>EBA Floor Plan Generator</h1>", index, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"user-avatar\"", index, StringComparison.Ordinal);
            Assert.Contains("class=\"stepper\"", index, StringComparison.Ordinal);
            Assert.Contains("data-step=\"1\"", index, StringComparison.Ordinal);
            Assert.Contains("data-step=\"-1\"", index, StringComparison.Ordinal);
            Assert.Contains("class=\"pref-list\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"floorWidth\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"daylightBedrooms\"", index, StringComparison.Ordinal);

            // Stepper buttons are wired via delegation (no inline handlers — CSP-safe).
            Assert.Contains("els.setupForm.addEventListener(\"click\", handleStepperClick)", app, StringComparison.Ordinal);
            Assert.Contains("button.closest(\".stepper\")", handleStepperClick, StringComparison.Ordinal);
            Assert.Contains("input.dispatchEvent(new Event(\"input\", { bubbles: true }))", handleStepperClick, StringComparison.Ordinal);
        }

        [Fact]
        public void MakeoverCanvasRefinementsKeepBoundedLabelsAndCleanGeometry()
        {
            string app = ReadWebFile("app.js");
            string renderRoomLabels = SliceFunction(app, "renderRoomLabels");
            string roomLabelFontSize = SliceFunction(app, "roomLabelFontSize");
            string renderDoorOpening = SliceFunction(app, "renderDoorOpening");
            string renderDimensionGuides = SliceFunction(app, "renderDimensionGuides");
            string renderSelectedRoomHalo = SliceFunction(app, "renderSelectedRoomHalo");

            // Full-caps room names on sparse plans, compact tags on dense plans (keeps the gate hook).
            Assert.Contains("function planRoomLabelName", app, StringComparison.Ordinal);
            Assert.Contains("densePlan ? compactRoomLabel(room) : planRoomLabelName(room)", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("roomDimensionText(room, bounds, 2)", renderRoomLabels, StringComparison.Ordinal);

            // Labels stay bounded under the hard ceiling even with the longer names.
            Assert.Contains("maxPlanLabelFontSize", roomLabelFontSize, StringComparison.Ordinal);

            // Clean quarter-circle door swing and a left overall dimension line (framed plan).
            Assert.Contains("const swing = clamp(width, 0.6, 1.1)", renderDoorOpening, StringComparison.Ordinal);
            Assert.Contains("const leftX = bounds.minX - offset", renderDimensionGuides, StringComparison.Ordinal);
            Assert.Contains("renderScaleBar(group, bounds, units || \"m\", offset)", renderDimensionGuides, StringComparison.Ordinal);

            // Selection grips stay a constant on-screen size at any zoom: the radius
            // derives from the visible view width, not the element being selected.
            Assert.Contains("function geomHandleRadius", app, StringComparison.Ordinal);
            Assert.Contains("clamp(view * 0.011, 0.12, 0.6)", app, StringComparison.Ordinal);
        }

        [Fact]
        public void CanvasFillsFrameWithDrawingSheetAndRealThicknessWalls()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string previewViewBox = SliceFunction(app, "previewViewBox");
            string renderPreview = SliceFunction(app, "renderPreview");
            string renderWallSegment = SliceFunction(app, "renderWallSegment");
            string updateCanvasUi = SliceFunction(app, "updateCanvasUi");

            // The viewBox is aspect-matched to the live canvas element so the wide
            // multi-unit plan fills the frame instead of being letterboxed into a
            // dead vertical band above and below the drawing.
            Assert.Contains("function previewFrameAspect", app, StringComparison.Ordinal);
            Assert.Contains("const frameAspect = previewFrameAspect()", previewViewBox, StringComparison.Ordinal);
            Assert.Contains("el.clientWidth", app, StringComparison.Ordinal);

            // A north arrow makes the drawing-sheet margin purposeful (plan view only).
            Assert.Contains("function renderNorthArrow", app, StringComparison.Ordinal);
            Assert.Contains("renderNorthArrow(bounds)", renderPreview, StringComparison.Ordinal);
            Assert.Contains(".north-arrow-needle", styles, StringComparison.Ordinal);

            // Walls render at real model-metre thickness with a proportional white
            // casing instead of collapsing into sub-pixel non-scaling hairlines.
            Assert.Contains("in real metres", styles, StringComparison.Ordinal);
            Assert.Contains("formatNumber(thickness + 0.07, 3)", renderWallSegment, StringComparison.Ordinal);

            // Live zoom readout sits in the grouped canvas toolbar.
            Assert.Contains("id=\"zoomLevel\"", index, StringComparison.Ordinal);
            Assert.Contains("class=\"tool-divider\"", index, StringComparison.Ordinal);
            Assert.Contains("els.zoomLevel.textContent", updateCanvasUi, StringComparison.Ordinal);
            Assert.Contains(".canvas-zoom", styles, StringComparison.Ordinal);

            // Flat, calm studio surface behind the plan; the drawing floats on its own
            // white sheet whose soft edge is stacked rects, never an SVG blur filter
            // (blur at sheet scale freezes the rasterizer at screenshot zoom).
            Assert.Contains("background: #edeff1", styles, StringComparison.Ordinal);
            Assert.Contains(".plan-sheet", styles, StringComparison.Ordinal);
            Assert.Contains(".plan-sheet-shadow", styles, StringComparison.Ordinal);
            Assert.Contains("class: \"plan-sheet\"", app, StringComparison.Ordinal);
            Assert.DoesNotContain(".plan-sheet {\n  filter:", styles.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        }

        [Fact]
        public void PlanFixturesRenderConstructionDocumentDetail()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");
            string bathroom = SliceFunction(app, "appendBathroomFixture");
            string kitchen = SliceFunction(app, "appendKitchenFixture");
            string bedroom = SliceFunction(app, "appendBedroomFixture");
            string living = SliceFunction(app, "appendLivingFixture");
            string shower = SliceFunction(app, "appendShower");
            string hob = SliceFunction(app, "appendHob");

            // Bathrooms compose real plumbing fixtures: a WC and a vanity basin opposite a
            // tub (roomy) or a shower stall (tight) on the wet wall.
            Assert.Contains("appendToilet(", bathroom, StringComparison.Ordinal);
            Assert.Contains("appendBasin(", bathroom, StringComparison.Ordinal);
            Assert.Contains("appendShower(", bathroom, StringComparison.Ordinal);
            Assert.Contains("\"fixture-bath\"", bathroom, StringComparison.Ordinal);

            // The shower stall keeps its diagonal drain "X" plus a centre drain, guarded by a
            // minimum size so tight stalls do not clutter.
            Assert.Contains("if (width < 0.24 || height < 0.24)", shower, StringComparison.Ordinal);
            Assert.Contains("\"fixture-detail\"", shower, StringComparison.Ordinal);

            // Kitchens lay a counter run carrying a double sink, a hob, and a fridge.
            Assert.Contains("appendDoubleSink(", kitchen, StringComparison.Ordinal);
            Assert.Contains("appendHob(", kitchen, StringComparison.Ordinal);
            Assert.Contains("\"fixture-fridge\"", kitchen, StringComparison.Ordinal);
            Assert.Contains("\"fixture-counter\"", kitchen, StringComparison.Ordinal);

            // Roomy kitchens still gain cooktop burner rings; the size guard keeps the tight
            // unit kitchens of the multi-family core uncluttered.
            Assert.Contains("if (Math.min(width, height) > 0.3)", hob, StringComparison.Ordinal);
            Assert.Contains("\"fixture-burner\"", hob, StringComparison.Ordinal);

            // Bedrooms read as furnished: a bed with a headboard strip and pillows, plus a
            // wardrobe with sliding-door split lines.
            Assert.Contains("\"fixture-bed\"", bedroom, StringComparison.Ordinal);
            Assert.Contains("\"fixture-headboard\"", bedroom, StringComparison.Ordinal);
            Assert.Contains("\"fixture-pillow\"", bedroom, StringComparison.Ordinal);
            Assert.Contains("appendDoorSplits(", bedroom, StringComparison.Ordinal);

            // Living rooms anchor a sofa facing a media unit across a coffee table on a rug.
            Assert.Contains("appendSofa(", living, StringComparison.Ordinal);
            Assert.Contains("\"fixture-tv\"", living, StringComparison.Ordinal);
            Assert.Contains("\"fixture-table\"", living, StringComparison.Ordinal);
            Assert.Contains("\"fixture-rug\"", living, StringComparison.Ordinal);

            // Daylight openings render as a glazing band plus frame, mullion and jamb caps
            // rather than a flat gap, matching construction-document window symbols.
            Assert.Contains("function appendWindowSymbol", app, StringComparison.Ordinal);
            Assert.Contains(".window-mullion", styles, StringComparison.Ordinal);
            Assert.Contains(".window-jamb", styles, StringComparison.Ordinal);

            // Rounded fixture corners come through a presentation-attribute helper so the
            // detailed furniture needs no inline styles under the strict CSP.
            Assert.Contains("function roundedAttr(radius)", app, StringComparison.Ordinal);

            // New furniture classes back the richer fixtures; drain linework still uses a
            // model-unit stroke so it thickens with zoom instead of vanishing.
            Assert.Contains(".fixture-headboard", styles, StringComparison.Ordinal);
            Assert.Contains(".fixture-tv", styles, StringComparison.Ordinal);
            Assert.Contains(".fixture-rug", styles, StringComparison.Ordinal);
            Assert.Contains(".fixture-fridge", styles, StringComparison.Ordinal);
            Assert.Contains(".fixture-detail", styles, StringComparison.Ordinal);
            Assert.Contains(".fixture-burner", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void EditorShellSupportsFullScreenPanZoomGridAndUndo()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string handleCanvasAction = SliceFunction(app, "handleCanvasAction");
            string previewViewBox = SliceFunction(app, "previewViewBox");
            string toggleFullscreen = SliceFunction(app, "toggleFullscreen");
            string renderPlanGrid = SliceFunction(app, "renderPlanGrid");
            string finishPlanPointerEdit = SliceFunction(app, "finishPlanPointerEdit");
            string bindEvents = SliceFunction(app, "bindEvents");

            // The canvas toolbar exposes the new editor tools.
            Assert.Contains("data-canvas-action=\"fullscreen\"", index, StringComparison.Ordinal);
            Assert.Contains("data-canvas-action=\"grid-toggle\"", index, StringComparison.Ordinal);
            Assert.Contains("data-canvas-action=\"undo\"", index, StringComparison.Ordinal);
            Assert.Contains("data-canvas-action=\"redo\"", index, StringComparison.Ordinal);

            // Each tool is dispatched from the single canvas action handler.
            Assert.Contains("action === \"grid-toggle\"", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("state.gridVisible = !state.gridVisible", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("action === \"fullscreen\"", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("action === \"undo\"", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("action === \"redo\"", handleCanvasAction, StringComparison.Ordinal);

            // Full screen uses the Fullscreen API with a webkit fallback, never a hard reload.
            Assert.Contains("requestFullscreen", toggleFullscreen, StringComparison.Ordinal);
            Assert.Contains("exitFullscreen", toggleFullscreen, StringComparison.Ordinal);

            // Zoom now reaches well past 4x and the viewBox honours a clamped pan offset, so a
            // zoomed-in editor view stays navigable.
            Assert.Contains("const maxZoom = 8", app, StringComparison.Ordinal);
            Assert.Contains("clamp(Number(zoom) || 1, 1, maxZoom)", previewViewBox, StringComparison.Ordinal);
            Assert.Contains("state.panX = panX", previewViewBox, StringComparison.Ordinal);
            Assert.Contains("const frameAspect = previewFrameAspect()", previewViewBox, StringComparison.Ordinal);

            // The reference grid draws in model units inside the Y-flipped group.
            Assert.Contains("class: \"plan-grid\"", renderPlanGrid, StringComparison.Ordinal);
            Assert.Contains("grid-line-major", renderPlanGrid, StringComparison.Ordinal);
            Assert.Contains(".grid-line", styles, StringComparison.Ordinal);

            // Pan, wheel zoom, and keyboard shortcuts are wired up; completing a drag-edit
            // records an undo snapshot of the pre-edit input.
            Assert.Contains("handleCanvasWheel", bindEvents, StringComparison.Ordinal);
            Assert.Contains("handleEditorKeyDown", bindEvents, StringComparison.Ordinal);
            Assert.Contains("fullscreenchange", bindEvents, StringComparison.Ordinal);
            Assert.Contains(
                "pushUndoSnapshot({ input: state.dragEdit.startInput, geometry: state.dragEdit.startGeometry })",
                finishPlanPointerEdit,
                StringComparison.Ordinal);
            Assert.Contains("function zoomAround(", app, StringComparison.Ordinal);
            Assert.Contains("function undoEdit(", app, StringComparison.Ordinal);

            // Full-screen and pan affordances are class-driven so the strict CSP holds.
            Assert.Contains(".preview-frame:fullscreen", styles, StringComparison.Ordinal);
            Assert.Contains("cursor: grab", styles, StringComparison.Ordinal);

            // Edit-history integrity: loading a new document resets both stacks (undo never
            // crosses documents), and any fresh user edit invalidates a pending redo branch so
            // a stale redo can never silently overwrite the user's latest change.
            string setInput = SliceFunction(app, "setInput");
            string handleSetupInput = SliceFunction(app, "handleSetupInput");
            string generateFromPrompt = SliceFunction(app, "generateFromPrompt");
            Assert.Contains("state.undoStack = []", setInput, StringComparison.Ordinal);
            Assert.Contains("state.redoStack = []", setInput, StringComparison.Ordinal);
            Assert.Contains("state.redoStack = []", handleSetupInput, StringComparison.Ordinal);
            Assert.Contains("state.redoStack = []", generateFromPrompt, StringComparison.Ordinal);

            // Disabling a focused toolbar button moves focus to a sibling so keyboard users keep
            // their place instead of dropping to <body>.
            string setCanvasButtonDisabled = SliceFunction(app, "setCanvasButtonDisabled");
            Assert.Contains("document.activeElement === button", setCanvasButtonDisabled, StringComparison.Ordinal);

            // Fixture detail strokes round their coordinates like the rest of the pipeline.
            string lineEl = SliceFunction(app, "lineEl");
            Assert.Contains("x1: round(start.x)", lineEl, StringComparison.Ordinal);

            // The canvas toolbar is a named ARIA toolbar so its label is announced.
            Assert.Contains("class=\"canvas-tools\" role=\"toolbar\"", index, StringComparison.Ordinal);
        }

        [Fact]
        public void PlanReadsAsConstructionDocumentSheet()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");
            string renderDoorOpening = SliceFunction(app, "renderDoorOpening");
            string shouldShowRoomLabel = SliceFunction(app, "shouldShowRoomLabel");
            string renderRoomLabels = SliceFunction(app, "renderRoomLabels");

            // Doors must draw in real model metres like the walls do. The white gap is
            // sized to the host wall so it cleanly cuts the poché; the leaf and thin
            // swing arc render as visible scaling linework.
            Assert.Contains("Doors draw in real model metres", styles, StringComparison.Ordinal);
            Assert.Contains("const gapWidth = Math.max(wallStrokeWidth(wall) * 1.85, 0.22)", renderDoorOpening, StringComparison.Ordinal);
            Assert.Contains(".door-swing", styles, StringComparison.Ordinal);

            // Every habitable room on the dense floorplate gets labelled (relaxed gate), with a
            // compact area line beneath the name so the plan reads like a real document.
            Assert.Contains("minDimension >= 1.7 && area >= 3.2", shouldShowRoomLabel, StringComparison.Ordinal);
            Assert.Contains("const showMeta = densePlan ? minSpan >= 2.1 : minSpan >= 2.8", renderRoomLabels, StringComparison.Ordinal);
            Assert.Contains("formatNumber(areaValue, 1)", renderRoomLabels, StringComparison.Ordinal);

            // One warm-graphite ink at four strengths, windows punched as white breaks
            // through the poché, the drawing floating on a white sheet: a printed
            // architectural document, not an infographic.
            Assert.Contains("--ink-900: #21262b;", styles, StringComparison.Ordinal);
            Assert.Contains(".window-break", styles, StringComparison.Ordinal);
            Assert.Contains("\"window-break\"", SliceFunction(app, "appendWindowSymbol"), StringComparison.Ordinal);
            Assert.Contains("DRAWING INK SYSTEM", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void WallsRenderAsSolidInkPocheFootprintPolygons()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");
            string renderWallSegment = SliceFunction(app, "renderWallSegment");

            // Walls are real footprint polygons (centerline offset by half the data-driven
            // thickness, ends extended so corners read solid), not flat strokes — the
            // react-planner / CAD convention surfaced by the open-source research.
            Assert.Contains("function wallFootprint", app, StringComparison.Ordinal);
            Assert.Contains("polygonEl(footprint, wallClass, { ...attributes, \"data-wall-ref\": wall.id })", renderWallSegment, StringComparison.Ordinal);
            Assert.Contains("formatNumber(thickness + 0.07, 3)", renderWallSegment, StringComparison.Ordinal);

            // The poché fill is SOLID ink: a hatch tile aliases into grey mud at typical
            // zoom, so the live stylesheet must not reference the legacy patterns.
            Assert.Contains("Solid reads print-crisp", styles, StringComparison.Ordinal);
            Assert.DoesNotContain("url(#wallPocheLight)", styles, StringComparison.Ordinal);
            Assert.DoesNotContain("url(#wallPocheHeavy)", styles, StringComparison.Ordinal);
            Assert.Contains("fill: #1a1f24", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void DimensionRunsUseArchitecturalObliqueTicksAndWitnessLines()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");
            string dimensionGuides = SliceFunction(app, "renderDimensionGuides");
            string obliqueTick = SliceFunction(app, "appendObliqueTick");

            // Dimension stations are 45-degree oblique slashes (the architectural tick), not
            // perpendicular ticks, centred on the witness station.
            Assert.Contains("function appendObliqueTick", app, StringComparison.Ordinal);
            Assert.Contains("class: \"dimension-tick\"", obliqueTick, StringComparison.Ordinal);
            Assert.Contains("const half = size * 0.7", obliqueTick, StringComparison.Ordinal);
            Assert.Contains("appendObliqueTick(group, bounds.minX, topY, tick)", dimensionGuides, StringComparison.Ordinal);

            // Witness (extension) lines stand off the object by a gap and overrun the dimension
            // line, the NKBA/CAD convention surfaced by the open-source research.
            Assert.Contains("const witnessGap = Math.max(maxDim * 0.009, 0.16)", dimensionGuides, StringComparison.Ordinal);
            Assert.Contains("\"dimension-witness\"", dimensionGuides, StringComparison.Ordinal);
            Assert.Contains(".dimension-witness", styles, StringComparison.Ordinal);

            // The pinned scale bar and left dimension column stay put so the sheet keeps its
            // measured frame and the math driving offsets is untouched.
            Assert.Contains("const leftX = bounds.minX - offset", dimensionGuides, StringComparison.Ordinal);
            Assert.Contains("renderScaleBar(group, bounds, units || \"m\", offset)", dimensionGuides, StringComparison.Ordinal);
        }

        [Fact]
        public void ResizeHandlesShowDirectionalCursorsAndLiveDragReadout()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");
            string renderPreview = SliceFunction(app, "renderPreview");
            string pointerMove = SliceFunction(app, "handlePlanPointerMove");
            string readoutText = SliceFunction(app, "dragReadoutText");

            // A live dimension badge tracks the cursor through every resize/move drag; it is
            // re-rendered each frame and the drag remembers its last cursor point.
            Assert.Contains("function renderDragReadout", app, StringComparison.Ordinal);
            Assert.Contains("renderDragReadout(bounds)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("state.dragEdit.lastPoint = point", pointerMove, StringComparison.Ordinal);

            // The badge text is action-specific so the user reads the live value they control.
            Assert.Contains("m wide", readoutText, StringComparison.Ordinal);
            Assert.Contains("Core ${formatNumber(coreBounds.width, 1)}", readoutText, StringComparison.Ordinal);

            // Handles advertise their drag axis with directional cursors instead of a generic
            // pointer, so the resize affordance is obvious before the user grabs a handle.
            Assert.Contains("cursor: ew-resize", styles, StringComparison.Ordinal);
            Assert.Contains("cursor: ns-resize", styles, StringComparison.Ordinal);
            Assert.Contains("cursor: nwse-resize", styles, StringComparison.Ordinal);
            Assert.Contains(".drag-readout-bg", styles, StringComparison.Ordinal);

            // A generous invisible halo wraps each small handle dot so grabs are forgiving,
            // and hovering anywhere in the halo brightens the handle it belongs to.
            string editHandle = SliceFunction(app, "editHandle");
            Assert.Contains("class: \"edit-handle-hit\"", editHandle, StringComparison.Ordinal);
            Assert.Contains(".edit-handle-hit", styles, StringComparison.Ordinal);
            Assert.Contains(".edit-handle-group:hover .edit-handle", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void ExportedSvgMatchesLiveConstructionDocumentRendering()
        {
            string app = ReadWebFile("app.js");
            string svgStyle = SliceFunction(app, "svgStyleElement");
            string saveSvg = SliceFunction(app, "saveSvg");

            // Saved / Rhino-bound SVGs embed self-contained styles that mirror the live
            // ink rendering, with custom properties expanded to literals — a standalone
            // file has no app stylesheet to resolve var() against.
            Assert.Contains(".wall{fill:${ink900};stroke:none}", svgStyle, StringComparison.Ordinal);
            Assert.Contains(".plan-sheet{fill:#ffffff", svgStyle, StringComparison.Ordinal);
            Assert.Contains(".window-break{fill:#ffffff", svgStyle, StringComparison.Ordinal);
            Assert.Contains(".room-bedroom{fill:#f8f5ef}", svgStyle, StringComparison.Ordinal);
            Assert.Contains(".dimension-witness{", svgStyle, StringComparison.Ordinal);
            Assert.DoesNotContain("var(--", svgStyle, StringComparison.Ordinal);

            // Retired looks must not return: hatch-pattern walls, the highlighter
            // daylight band, the yellow/blue infographic palette.
            Assert.DoesNotContain("wallPoche", svgStyle, StringComparison.Ordinal);
            Assert.DoesNotContain("daylight-band", svgStyle, StringComparison.Ordinal);
            Assert.DoesNotContain("#f9d889", svgStyle, StringComparison.Ordinal);

            // Editor chrome never ships inside a saved drawing.
            Assert.Contains(".edit-handles, .edit-selection-handles, .room-selected-halo-group", saveSvg, StringComparison.Ordinal);
            Assert.Contains(".wall-hit, .core-grab-overlay, .plan-grid", saveSvg, StringComparison.Ordinal);
        }

        [Fact]
        public void StudioChromeUsesCalmAppleGradeTypographyAndControls()
        {
            string styles = ReadWebFile("styles.css");

            // Weights resolve to a clean 400/600/700 ramp on Windows/Segoe instead of the old
            // 450..850 ramp that collapsed; emphasis leans on size + colour, not black weight.
            Assert.Contains("--fw-regular: 400;", styles, StringComparison.Ordinal);
            Assert.Contains("--fw-heavy: 760;", styles, StringComparison.Ordinal);
            Assert.DoesNotContain("--fw-heavy: 850;", styles, StringComparison.Ordinal);

            // Calm reading rhythm and crisp display tracking.
            Assert.Contains("line-height: 1.45;", styles, StringComparison.Ordinal);
            Assert.Contains("letter-spacing: -0.014em;", styles, StringComparison.Ordinal);

            // Sidebar sections group by whitespace + a hairline divider, not nested boxes.
            Assert.Contains("fieldset + fieldset {", styles, StringComparison.Ordinal);

            // Tactile press, thin pill scrollbars and a branded text selection.
            Assert.Contains("button:active:not(:disabled) {", styles, StringComparison.Ordinal);
            Assert.Contains("scrollbar-width: thin;", styles, StringComparison.Ordinal);
            Assert.Contains("::-webkit-scrollbar-thumb {", styles, StringComparison.Ordinal);
            Assert.Contains("::selection {", styles, StringComparison.Ordinal);
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

            // The setup panel is one honest scrolling form. The fake step wizard
            // (chips that only re-highlighted, Back/Next that changed nothing) was
            // deliberately retired — its hooks must not creep back.
            Assert.DoesNotContain("data-setup-step-button", index, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"setupPrevBtn\"", index, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"setupNextBtn\"", index, StringComparison.Ordinal);
            Assert.DoesNotContain("setSetupStep", app, StringComparison.Ordinal);
            Assert.DoesNotContain("moveSetupStep", app, StringComparison.Ordinal);

            // What a non-technical user needs stays visible and wired: the brief
            // prompt, templates, the review card, and one primary generate action.
            Assert.Contains("id=\"setupReview\"", index, StringComparison.Ordinal);
            Assert.Contains("Design Brief", index, StringComparison.Ordinal);
            Assert.Contains("class=\"brief-prompt\"", index, StringComparison.Ordinal);
            Assert.Contains("class=\"template-strip\"", index, StringComparison.Ordinal);
            Assert.Contains("await runEngine(false)", bindEvents, StringComparison.Ordinal);
            Assert.Contains("renderSetupGuide(output)", renderAll, StringComparison.Ordinal);
            // The sticky duplicate generate button was retired (the header
            // Generate and the prompt button are the two honest actions); the
            // render path must tolerate its absence — an unguarded reference
            // here once killed renderAll and aborted boot.
            Assert.Contains("if (els.setupGenerateBtn)", renderSetupGuide, StringComparison.Ordinal);
            Assert.Contains("buildSetupReview(output)", renderSetupGuide, StringComparison.Ordinal);
            Assert.Contains("Readiness", buildSetupReview, StringComparison.Ordinal);
            Assert.Contains(".brief-prompt", styles, StringComparison.Ordinal);
            Assert.Contains(".template-strip", styles, StringComparison.Ordinal);
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
            // Entering edit mode must NOT yank the user's selection to the floorplate.
            Assert.Contains("selection survives the mode switch", handleCanvasAction, StringComparison.Ordinal);
            Assert.DoesNotContain("state.selection = { kind: \"floorplate\"", handleCanvasAction, StringComparison.Ordinal);
            Assert.Contains("button.dataset.canvasAction === \"edit-toggle\"", app, StringComparison.Ordinal);
            Assert.Contains("editToggle.setAttribute(\"aria-pressed\", editActive ? \"true\" : \"false\")", app, StringComparison.Ordinal);
            Assert.Contains("els.planSvg.addEventListener(\"pointerdown\", handlePlanPointerDown)", app, StringComparison.Ordinal);
            Assert.Contains("!state.editMode", handlePlanPointerDown, StringComparison.Ordinal);
            Assert.Contains("function applyCanvasEdit", app, StringComparison.Ordinal);
            // Constraint chrome is whisper-thin: label-free grips on the floorplate
            // boundary, the core grabbable by its whole body via an overlay that sits
            // ABOVE the walls so its press can't be stolen by a wall hit zone.
            Assert.Contains("state.editMode", renderInputEditHandles, StringComparison.Ordinal);
            Assert.Contains("\"Drag to set floorplate width\", \"ew\"", renderInputEditHandles, StringComparison.Ordinal);
            Assert.Contains("core-grab-overlay", renderInputEditHandles, StringComparison.Ordinal);
            Assert.DoesNotContain("editHandleLabel", app, StringComparison.Ordinal);
            Assert.Contains("const dx = point.x - edit.startPoint.x", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("const dy = point.y - edit.startPoint.y", applyCanvasEdit, StringComparison.Ordinal);
            // Floorplate drags snap to the same 0.5 m grid the steppers use.
            Assert.Contains("snapHalf(floorBounds.width + dx)", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("snapHalf(floorBounds.height + dy)", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("state.editMode", renderSelectionConstraintHandles, StringComparison.Ordinal);
            Assert.Contains("detail.source !== \"generated\"", renderSelectionConstraintHandles, StringComparison.Ordinal);
            Assert.Contains("data-edit-action", editHandle, StringComparison.Ordinal);
            Assert.Contains("class: `edit-handle edit-${action}`", editHandle, StringComparison.Ordinal);
            Assert.Contains(".edit-readout", styles, StringComparison.Ordinal);
            Assert.Contains(".edit-handle", styles, StringComparison.Ordinal);
            Assert.Contains(".edit-selection-box", styles, StringComparison.Ordinal);
            // The toolbar owns the TOP of the canvas; the bottom corners belong to the
            // title block and the live readout.
            Assert.Contains(".canvas-tools {\n  top: 12px;\n  bottom: auto;", normalizedStyles, StringComparison.Ordinal);
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
            string renderDaylightAndWindowBands = SliceFunction(app, "renderWindowLayer");
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
            // Windows moved to their own post-wall layer (renderWindowLayer): they
            // punch white openings through the poché and must paint above it.
            Assert.Contains("renderWindowLayer(group, variant, bounds)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("renderCorridorCenterlines(layer, variant)", renderPlanGlyphLayer, StringComparison.Ordinal);
            Assert.Contains("renderRoomFixtures(layer, variant)", renderPlanGlyphLayer, StringComparison.Ordinal);
            Assert.Contains("room.daylight", renderDaylightAndWindowBands, StringComparison.Ordinal);
            Assert.Contains("facadeSidesFor(bounds, floorBounds)", renderDaylightAndWindowBands, StringComparison.Ordinal);
            Assert.Contains("appendRoomFixture(wrap, room)", renderRoomFixtures, StringComparison.Ordinal);
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
            Assert.Contains("state.viewMode !== \"plan\"", renderRoomLabels, StringComparison.Ordinal);
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
            Assert.Contains(".window-break", styles, StringComparison.Ordinal);
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
        public void DirectGeometryEditsKeepPlansWatertightAndEngineInputClean()
        {
            string app = ReadWebFile("app.js");
            string renderPreview = SliceFunction(app, "renderPreview");
            string renderSelectionConstraintHandles = SliceFunction(app, "renderSelectionConstraintHandles");
            string handlePlanClick = SliceFunction(app, "handlePlanClick");
            string applyCanvasEdit = SliceFunction(app, "applyCanvasEdit");
            string finishGeomDrag = SliceFunction(app, "finishGeomDrag");
            string restoreDraft = SliceFunction(app, "restoreDraft");
            string boundaryLineRange = SliceFunction(app, "boundaryLineRange");

            // Rooms and units are edited DIRECTLY through the boundary-move engine:
            // every polygon sharing the dragged wall plane follows (span absorption),
            // walls and doors commit as explicit overrides identical to the live
            // preview, and the canvas chrome is grips — not floating action chips.
            Assert.Contains("renderSelectionConstraintHandles(group, output)", renderPreview, StringComparison.Ordinal);
            Assert.Contains("appendGeomResizeHandles(editGroup, bounds, detail.kind, detail.id)", renderSelectionConstraintHandles, StringComparison.Ordinal);
            Assert.DoesNotContain("renderPlanQuickActions", renderPreview, StringComparison.Ordinal);
            Assert.Contains("data-plan-action", handlePlanClick, StringComparison.Ordinal);
            Assert.Contains("function collectBoundaryEdges", app, StringComparison.Ordinal);
            Assert.Contains("function shiftFollowerPoints", app, StringComparison.Ordinal);
            Assert.Contains("function buildEditPointMapper", app, StringComparison.Ordinal);
            Assert.Contains("function commitWallDoorOverrides", app, StringComparison.Ordinal);
            Assert.Contains("setGeometryOverride(drag.variantId, drag.kind, drag.id, drag.current)", finishGeomDrag, StringComparison.Ordinal);

            // The core is a rigid obstacle: boundary planes stop at its face.
            Assert.Contains("fixedObstacleBounds()", boundaryLineRange, StringComparison.Ordinal);

            // Edits from retired math never replay: the draft store is version-gated.
            Assert.Contains("draft.editsVersion === geometryEditsVersion", restoreDraft, StringComparison.Ordinal);

            // Floor/core handle drags still mutate the ENGINE INPUT only — they never
            // poke generated variant geometry directly.
            Assert.Contains("input.rules.minRoomWidth", applyCanvasEdit, StringComparison.Ordinal);
            Assert.Contains("refreshAccessFromCore(input)", applyCanvasEdit, StringComparison.Ordinal);
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

        [Fact]
        public void PromptToFloorPlanParsesBriefIntoFeasibleReproducibleInput()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string parsePrompt = SliceFunction(app, "parsePrompt");
            string applyPromptToForm = SliceFunction(app, "applyPromptToForm");
            string projectNameFromBrief = SliceFunction(app, "projectNameFromBrief");
            string syncInputFromForm = SliceFunction(app, "syncInputFromForm");
            string syncFormFromInput = SliceFunction(app, "syncFormFromInput");
            string saveDraft = SliceFunction(app, "saveDraft");
            string restoreDraft = SliceFunction(app, "restoreDraft");

            // Prompt affordance + transparent intent region exist and are wired to the parser.
            Assert.Contains("id=\"promptGenerateBtn\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"promptUnderstood\"", index, StringComparison.Ordinal);
            Assert.Contains("class=\"primary prompt-generate\"", index, StringComparison.Ordinal);
            Assert.Contains("promptGenerateBtn: document.getElementById(\"promptGenerateBtn\")", app, StringComparison.Ordinal);
            Assert.Contains("() => generateFromPrompt()", app, StringComparison.Ordinal);

            // Real briefs arrive glued and typoed ("a1 bhkapartment", "2bhkflat"):
            // the text normalises (digit/letter splits, compound de-gluing) BEFORE
            // any pattern runs, so plain phrasings parse instead of falling through.
            Assert.Contains("function normalizePromptText", app, StringComparison.Ordinal);
            Assert.Contains("replace(/(\\d)([a-z])/g, \"$1 $2\")", app, StringComparison.Ordinal);
            Assert.Contains("normalizePromptText(text)", parsePrompt, StringComparison.Ordinal);

            // Every distinct brief explores a distinct layout: the seed derives from
            // the brief text (reproducible per text), variety words walk it forward,
            // and the form pipeline actually carries it into the engine request.
            Assert.Contains("function promptSeed", app, StringComparison.Ordinal);
            Assert.Contains("intent.seed = promptSeed(t)", parsePrompt, StringComparison.Ordinal);
            Assert.Contains("intent.shuffle", parsePrompt, StringComparison.Ordinal);
            Assert.Contains("els.seedInput.value = String(intent.seed + state.promptShuffle * 7919)", applyPromptToForm, StringComparison.Ordinal);

            // A brief naming a plate shape starts from that sample's geometry.
            Assert.Contains("intent.template = \"l-shaped-core\"", parsePrompt, StringComparison.Ordinal);

            // Unit-type detection tolerates plurals ("studios and one-beds", "two-beds").
            Assert.Contains("(?:bed|bedroom|bhk|br)s?", parsePrompt, StringComparison.Ordinal);

            // A compliance brief maps to the buildable "balanced" mode, never the
            // zero-yield "strict" mode, and the chip stays honest about that.
            Assert.Contains("note(\"code-aware rules\")", parsePrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("intent.strictness = \"strict\"", parsePrompt, StringComparison.Ordinal);

            // Numeric extremes are clamped to a feasible envelope so a brief cannot
            // brick generation (corridors 1.2..2.6 m, unit floor 16..50 m2).
            Assert.Contains("clamp(Number(corr[1]), 1.2, 2.6)", parsePrompt, StringComparison.Ordinal);
            Assert.Contains("clamp(Number(unitArea[1]), 16, 50)", parsePrompt, StringComparison.Ordinal);
            Assert.Contains("intent.minUnit = 38;", parsePrompt, StringComparison.Ordinal);

            // applyPromptToForm is authoritative: unmentioned knobs reset to feasible
            // engine defaults, so the same brief always yields the same plan.
            Assert.Contains("Number.isFinite(intent.minUnit) ? intent.minUnit : 25", applyPromptToForm, StringComparison.Ordinal);
            Assert.Contains("Number.isFinite(intent.corridor) ? intent.corridor : 1.8", applyPromptToForm, StringComparison.Ordinal);
            Assert.Contains("intent.strictness || \"balanced\"", applyPromptToForm, StringComparison.Ordinal);
            Assert.Contains("{ studio: 35, one_bed: 45, two_bed: 20 }", applyPromptToForm, StringComparison.Ordinal);

            // The brief is decoupled from project.name: it lives in state.brief, never in
            // the input object (which the engine rejects and exports must keep clean).
            Assert.Contains("state.brief = briefText;", syncInputFromForm, StringComparison.Ordinal);
            Assert.Contains("projectNameFromBrief(briefText)", syncInputFromForm, StringComparison.Ordinal);
            Assert.DoesNotContain("input.project.brief", app, StringComparison.Ordinal);
            Assert.Contains("typeof state.brief === \"string\"", syncFormFromInput, StringComparison.Ordinal);
            Assert.Contains("return deriveProjectName(parsePrompt(text), text)", projectNameFromBrief, StringComparison.Ordinal);

            // The brief round-trips through the existing draft mechanism.
            Assert.Contains("brief: state.brief,", saveDraft, StringComparison.Ordinal);
            Assert.Contains("state.brief = draft.brief;", restoreDraft, StringComparison.Ordinal);

            // Graceful, transparent safety net: relax one notch only when variants were
            // generated but all failed validation, then say so plainly.
            Assert.Contains("await relaxStrictnessIfNoVariants()", app, StringComparison.Ordinal);
            Assert.Contains("(current.variantCount || 0) === 0) { return; }", app, StringComparison.Ordinal);
            Assert.Contains("Relaxed to ${next} rules to fit this plate", app, StringComparison.Ordinal);
            Assert.Contains("prompt-chip prompt-chip-note", app, StringComparison.Ordinal);
            Assert.Contains("navigateToHash(\"#plan\")", app, StringComparison.Ordinal);

            // Styling hooks for the affordance, chips and the auto-adjust note.
            Assert.Contains(".prompt-generate {", styles, StringComparison.Ordinal);
            Assert.Contains(".prompt-understood {", styles, StringComparison.Ordinal);
            Assert.Contains(".prompt-chip {", styles, StringComparison.Ordinal);
            Assert.Contains(".prompt-chip-note {", styles, StringComparison.Ordinal);
        }

        [Fact]
        public void AiBriefAssistBridgesLocalClaudeCliWithHeuristicFallback()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string styles = ReadWebFile("styles.css");
            string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "FloorPlanGeneration.Web", "Program.cs"));
            string service = File.ReadAllText(Path.Combine(RepositoryRoot(), "FloorPlanGeneration.Web", "BriefIntentService.cs"));
            string generateFromPrompt = SliceFunction(app, "generateFromPrompt");
            string mergeAiIntent = SliceFunction(app, "mergeAiIntent");

            // Server exposes the bridge and the client probes it at boot; the page
            // never depends on it (heuristic parser remains the guaranteed path).
            Assert.Contains("/api/prompt/status", program, StringComparison.Ordinal);
            Assert.Contains("/api/prompt/parse", program, StringComparison.Ordinal);
            Assert.Contains("probeAiAssist()", app, StringComparison.Ordinal);
            Assert.Contains("const aiIntent = await requestAiIntent(text)", generateFromPrompt, StringComparison.Ordinal);
            Assert.Contains("intent = mergeAiIntent(intent, aiIntent)", generateFromPrompt, StringComparison.Ordinal);

            // The CLI is a pure text transform: no agent tools, empty scratch cwd,
            // hard timeout, and the brief is data the model must not obey.
            Assert.Contains("--disallowed-tools", service, StringComparison.Ordinal);
            Assert.Contains("floorplan-brief-parse", service, StringComparison.Ordinal);
            Assert.Contains("CliTimeoutMilliseconds", service, StringComparison.Ordinal);
            Assert.Contains("data, not instructions", service, StringComparison.Ordinal);

            // Every AI value is clamped server-side to the same buildable ranges the
            // form enforces, and again client-side on merge: defense in depth.
            Assert.Contains("Clamp(raw.Width.Value, 8.0, 200.0)", service, StringComparison.Ordinal);
            Assert.Contains("Clamp(raw.Corridor.Value, 1.2, 2.6)", service, StringComparison.Ordinal);
            Assert.Contains("clamp(ai.corridor, 1.2, 2.6)", mergeAiIntent, StringComparison.Ordinal);
            Assert.Contains("clamp(ai.minUnit, 16, 50)", mergeAiIntent, StringComparison.Ordinal);

            // Reproducibility contract: the layout seed stays brief-derived even when
            // the AI reads the brief, so merge never overrides seed or shuffle.
            Assert.DoesNotContain("merged.seed", mergeAiIntent, StringComparison.Ordinal);
            Assert.DoesNotContain("merged.shuffle", mergeAiIntent, StringComparison.Ordinal);

            // The user can see and control the assist, and provenance is shown.
            Assert.Contains("id=\"aiAssistRow\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"aiAssistToggle\"", index, StringComparison.Ordinal);
            Assert.Contains(".ai-assist {", styles, StringComparison.Ordinal);
            Assert.Contains("Brief interpreted by", app, StringComparison.Ordinal);
            Assert.Contains("used the built-in parser", app, StringComparison.Ordinal);

            // Claude and Codex subscriptions are both first-class: the server
            // detects every installed CLI, the client may request one per parse,
            // and the picker never arms the stale auto-generate pipeline.
            Assert.Contains("public string Provider { get; set; }", service, StringComparison.Ordinal);
            Assert.Contains("--output-last-message", service, StringComparison.Ordinal);
            Assert.Contains("providers = _cliPaths.Keys", service, StringComparison.Ordinal);
            Assert.Contains("id=\"aiProviderSelect\"", index, StringComparison.Ordinal);
            Assert.Contains("provider: selectedAiProvider()", app, StringComparison.Ordinal);
            Assert.Contains("event.target === els.aiProviderSelect", SliceFunction(app, "handleSetupInput"), StringComparison.Ordinal);
        }

        [Fact]
        public void GenerateFromPromptOwnsTheRunPipelineAgainstStaleAutoRuns()
        {
            string app = ReadWebFile("app.js");
            string handleSetupInput = SliceFunction(app, "handleSetupInput");
            string generateFromPrompt = SliceFunction(app, "generateFromPrompt");
            string awaitEngineIdle = SliceFunction(app, "awaitEngineIdle");

            // Regression: the brief textarea sits inside setupForm, so typing it (and
            // the blur fired by clicking the generate button) scheduled an auto-run
            // with the OLD settings. That stale run landed during the 15s AI parse,
            // re-rendered the old plan and overwrote the progress status — the whole
            // feature read as broken. The brief must never feed the auto-run pipeline.
            Assert.Contains("event.target === els.projectName", handleSetupInput, StringComparison.Ordinal);
            AssertBefore(
                handleSetupInput,
                "event.target === els.projectName",
                "markInputDirty(",
                "Brief edits must bail out before the auto-generate pipeline is armed.");

            // The prompt flow owns the pipeline for its whole async span: pending
            // timers and queued runs are cancelled up front, and its engine run only
            // starts once any in-flight run has drained, so the prompt's plan is the
            // one that renders last (and relax/navigation read the right response).
            Assert.Contains("clearAutoGenerate();", generateFromPrompt, StringComparison.Ordinal);
            Assert.Contains("state.pendingRunMode = \"\";", generateFromPrompt, StringComparison.Ordinal);
            Assert.Contains("await awaitEngineIdle(", generateFromPrompt, StringComparison.Ordinal);
            Assert.Contains("state.busy", awaitEngineIdle, StringComparison.Ordinal);

            // Progress is visible at the point of interaction, not only in a distant
            // status line: the button itself reports the AI read while it runs.
            Assert.Contains("Reading brief with", generateFromPrompt, StringComparison.Ordinal);
            Assert.Contains("els.promptGenerateBtn.textContent", generateFromPrompt, StringComparison.Ordinal);
        }

        [Fact]
        public void SingleDwellingBriefsGenerateApartmentPlansFromScratch()
        {
            string app = ReadWebFile("app.js");
            string service = File.ReadAllText(Path.Combine(RepositoryRoot(), "FloorPlanGeneration.Web", "BriefIntentService.cs"));
            string inputSchema = File.ReadAllText(Path.Combine(RepositoryRoot(), "schemas", "floor-plan-engine-input.schema.json"));
            string parsePrompt = SliceFunction(app, "parsePrompt");
            string generateFromPrompt = SliceFunction(app, "generateFromPrompt");
            string buildDwelling = SliceFunction(app, "buildSingleDwellingInput");
            string syncInputFromForm = SliceFunction(app, "syncInputFromForm");

            // "a 1 room kitchen apartment" and friends are ONE dwelling, not a
            // corridor building floor: the heuristic detects them and the flow
            // builds a from-scratch input instead of loading a sample template.
            Assert.Contains("intent.dwelling = \"single\"", parsePrompt, StringComparison.Ordinal);
            Assert.Contains("room\\s*(?:\\+|and|&)?\\s*kitchen", parsePrompt, StringComparison.Ordinal);
            Assert.Contains("intent.bedrooms = bedrooms", parsePrompt, StringComparison.Ordinal);
            Assert.Contains("setInput(buildSingleDwellingInput(intent))", generateFromPrompt, StringComparison.Ordinal);

            // Layout mode never sticks across briefs: a building brief arriving
            // while the current document is a dwelling resets to a sample plate.
            Assert.Contains("wantsTemplate || isDwellingInput(state.input)", generateFromPrompt, StringComparison.Ordinal);

            // The dwelling input is genuinely from scratch: no core, no template,
            // single-unit program, and the engine's single_dwelling layout mode.
            Assert.Contains("layoutMode: \"single_dwelling\"", buildDwelling, StringComparison.Ordinal);
            Assert.Contains("fixedElements: []", buildDwelling, StringComparison.Ordinal);
            Assert.Contains("targetCount: 1", buildDwelling, StringComparison.Ordinal);

            // Form syncing keeps the mode intact: no core injection, no unit-mix
            // overwrite, and the small-plate floor of 4 m applies.
            Assert.Contains("const dwellingMode = isDwellingInput(input)", syncInputFromForm, StringComparison.Ordinal);
            Assert.Contains("dwellingMode ? 4 : 8", syncInputFromForm, StringComparison.Ordinal);
            AssertBefore(
                syncInputFromForm,
                "if (dwellingMode) {",
                "applyCoreFromForm(input, width, depth);",
                "Core injection must be gated behind the non-dwelling branch.");

            // The AI bridge understands and sanitizes the dwelling fields.
            Assert.Contains("single\\\" | \\\"building", service, StringComparison.Ordinal);
            Assert.Contains("intent.Dwelling = dwelling", service, StringComparison.Ordinal);
            Assert.Contains("Clamp(raw.Bedrooms.Value, 0.0, 4.0)", service, StringComparison.Ordinal);

            // The published input schema documents the new mode.
            Assert.Contains("single_dwelling", inputSchema, StringComparison.Ordinal);
        }

        [Fact]
        public void BoundaryPlaneAbsorptionCatchesSliverOverlapsAndUiStaysModeAware()
        {
            string app = ReadWebFile("app.js");
            string index = ReadWebFile("index.html");
            string absorb = SliceFunction(app, "absorbSpanOnLine");
            string boundaryEdges = SliceFunction(app, "collectBoundaryEdges");
            string beginWallDrag = SliceFunction(app, "beginWallDrag");
            string renderLegend = SliceFunction(app, "renderLegend");
            string syncFormFromInput = SliceFunction(app, "syncFormFromInput");
            string inlineSummary = SliceFunction(app, "selectionInlineSummary");
            string setupGuide = SliceFunction(app, "renderSetupGuide");

            // Regression: span absorption used the coarse follower threshold
            // (0.15 m), so an 8 cm sliver of shared wall neither joined the plane
            // nor grew the span — half the wall moved, half stayed, leaving
            // overlapping rooms and detached doors. Growth now scans EVERY polygon
            // with a tiny positive threshold; both drag paths share the helper.
            Assert.Contains("> 1e-4", absorb, StringComparison.Ordinal);
            Assert.Contains("absorbSpanOnLine(variant, excludeIds", boundaryEdges, StringComparison.Ordinal);
            Assert.Contains("absorbSpanOnLine(", beginWallDrag, StringComparison.Ordinal);
            Assert.DoesNotContain("boundaryMinOverlap", boundaryEdges, StringComparison.Ordinal);
            Assert.DoesNotContain("boundaryMinOverlap", beginWallDrag, StringComparison.Ordinal);

            // Regression: removing the sticky generate button without guarding its
            // render reference killed renderAll and aborted boot entirely.
            Assert.Contains("if (els.setupGenerateBtn)", setupGuide, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"setupGenerateBtn\"", index, StringComparison.Ordinal);
            Assert.DoesNotContain("data-nav=\"rhino\"", index, StringComparison.Ordinal);

            // Mode-aware chrome: dwellings hide core/mix sections and the legend
            // only lists what the plan actually contains.
            Assert.Contains("id=\"coreFieldset\"", index, StringComparison.Ordinal);
            Assert.Contains("id=\"unitMixFieldset\"", index, StringComparison.Ordinal);
            Assert.Contains("coreFieldset.hidden = dwellingDocument", syncFormFromInput, StringComparison.Ordinal);
            Assert.Contains("unitMixFieldset.hidden = dwellingDocument", syncFormFromInput, StringComparison.Ordinal);
            Assert.Contains("hasCorridor", renderLegend, StringComparison.Ordinal);

            // Humans read "Bathroom · 8.7 m²", not engine ids.
            Assert.Contains("displayRoomType(detail.item)", inlineSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("${selectionKindLabel(detail.kind)} ${detail.id} ·", inlineSummary, StringComparison.Ordinal);
        }

        [Fact]
        public void GrasshopperAdapterShipsAndMirrorsTheStudioContracts()
        {
            string adapter = File.ReadAllText(Path.Combine(RepositoryRoot(), "adapters", "grasshopper", "fp_generate.py"));
            string harness = File.ReadAllText(Path.Combine(RepositoryRoot(), "adapters", "grasshopper", "test_fp_generate.py"));
            string guide = File.ReadAllText(Path.Combine(RepositoryRoot(), "adapters", "grasshopper", "README.md"));
            string app = ReadWebFile("app.js");

            // The paste-in component must keep working in Rhino 7 (IronPython 2.7)
            // and Rhino 8 (Python 3): stdlib HTTP only, with the py2 fallback.
            Assert.Contains("from urllib.request import Request, urlopen", adapter, StringComparison.Ordinal);
            Assert.Contains("from urllib2 import Request, urlopen", adapter, StringComparison.Ordinal);
            Assert.Contains("/api/generate", adapter, StringComparison.Ordinal);
            Assert.Contains("/api/prompt/parse", adapter, StringComparison.Ordinal);
            Assert.Contains("FP::Generated::Rooms", adapter, StringComparison.Ordinal);
            Assert.Contains("SetUserString(\"externalId\"", adapter, StringComparison.Ordinal);

            // The adapter mirrors the studio's dwelling presets and seed math so a
            // brief reproduces the same layout in Grasshopper and in the browser.
            // When the studio presets change, change the adapter in the same commit.
            foreach (string preset in new[]
            {
                "{\"width\": 7.2, \"depth\": 5.6, \"type\": \"studio\"",
                "{\"width\": 11.2, \"depth\": 8.4, \"type\": \"two_bed\"",
                "{\"width\": 15.0, \"depth\": 10.5, \"type\": \"three_bed\""
            })
            {
                Assert.Contains(preset, adapter, StringComparison.Ordinal);
            }

            Assert.Contains("0x811C9DC5", adapter, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("0x811c9dc5", app, StringComparison.OrdinalIgnoreCase);

            // The pipeline stays verifiable without Rhino, and the guide documents
            // the exact component wiring for both Rhino generations.
            Assert.Contains("run_component", harness, StringComparison.Ordinal);
            Assert.Contains("no corridors for a single dwelling", harness, StringComparison.Ordinal);
            Assert.Contains("Rhino 7", guide, StringComparison.Ordinal);
            Assert.Contains("Rhino 8", guide, StringComparison.Ordinal);
            Assert.Contains("fp_generate.py", guide, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowsPaintAboveWallsAndLabelsStayClearOfFixtures()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");
            string renderPreview = SliceFunction(app, "renderPreview");
            string glyphLayer = SliceFunction(app, "renderPlanGlyphLayer");

            // Regression: windows are openings punched through the wall poché, so
            // they must render AFTER the walls — drawn earlier they vanish under
            // the ink, which made every window invisible for an entire release.
            Assert.Contains("function renderWindowLayer", app, StringComparison.Ordinal);
            Assert.DoesNotContain("renderDaylightAndWindowBands", glyphLayer, StringComparison.Ordinal);
            AssertBefore(
                renderPreview,
                "renderWallSegment(group, wall)",
                "renderWindowLayer(group, variant, bounds)",
                "Walls must render before the window layer punches openings through them.");

            // A corner room glazes every side that truly lies on the floorplate
            // boundary, and the layer never blocks wall clicks.
            Assert.Contains("function facadeSidesFor", app, StringComparison.Ordinal);
            Assert.Contains(".slice(0, 2)", SliceFunction(app, "facadeSidesFor"), StringComparison.Ordinal);
            Assert.Contains(".plan-window-layer,", styles, StringComparison.Ordinal);

            // Fixtures never strike through labels: the rug is a fill-only floor
            // marking, the media wall faces the sofa from across the room, and
            // small wet rooms keep only their name (no cramped dimension line).
            Assert.Contains("fill: rgba(33, 38, 43, 0.04)", styles, StringComparison.Ordinal);
            Assert.DoesNotContain("stroke-dasharray", SliceCssRule(styles, ".fixture-rug {"), StringComparison.Ordinal);
            Assert.Contains("minSpan >= 2.8", app, StringComparison.Ordinal);
        }

        private static string SliceCssRule(string source, string selector)
        {
            int start = source.IndexOf(selector, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing css rule " + selector + ".");
            int end = source.IndexOf("}", start, StringComparison.Ordinal);
            return end > start ? source.Substring(start, end - start) : source.Substring(start);
        }

        [Fact]
        public void AxonIsAProjectedSceneAndCirculationIsADoorNetworkDiagram()
        {
            string app = ReadWebFile("app.js");
            string styles = ReadWebFile("styles.css");
            string axonView = SliceFunction(app, "renderAxonView");
            string overlay = SliceFunction(app, "renderCirculationOverlay");

            // Regression: "3D axon" used to be the flat plan with a skew transform.
            // It is now a real plan-oblique projection — rotated, depth-foreshortened,
            // extruded volumes painter-sorted back to front, with its own view box.
            Assert.DoesNotContain("skewX", app, StringComparison.Ordinal);
            Assert.Contains("function axonProjector", app, StringComparison.Ordinal);
            Assert.Contains("function axonBox", app, StringComparison.Ordinal);
            Assert.Contains("depthScale", app, StringComparison.Ordinal);
            Assert.Contains("boxes.sort((a, b) => b.depth - a.depth)", axonView, StringComparison.Ordinal);
            Assert.Contains("axonSettings.coreHeight", axonView, StringComparison.Ordinal);
            Assert.Contains("previewViewBox(projBounds", axonView, StringComparison.Ordinal);
            Assert.Contains(".axon-top.axon-wall", styles, StringComparison.Ordinal);
            Assert.Contains(".axon-side-y.axon-core", styles, StringComparison.Ordinal);

            // Circulation derives a movement diagram from the model's door network:
            // corridor spine with arrows, space→door→space flow lines, entry emphasis.
            Assert.Contains("door.connectsSpaces", overlay, StringComparison.Ordinal);
            Assert.Contains("circ-spine", overlay, StringComparison.Ordinal);
            Assert.Contains("circ-entry-dot", overlay, StringComparison.Ordinal);
            Assert.Contains("function nearestPointOnSegment", app, StringComparison.Ordinal);
            Assert.Contains("marker-end\": \"url(#circ-arrow)", app, StringComparison.Ordinal);
            Assert.Contains("id: \"circ-arrow\"", app, StringComparison.Ordinal);
            Assert.Contains("[data-view-mode=\"circulation\"] .wall", styles, StringComparison.Ordinal);
            Assert.Contains(".circ-flow", styles, StringComparison.Ordinal);

            // Walls carry their openings: door gaps bridged by lintels and facade
            // windows glazed as sill + glass + header bands, sharing the exact 2D
            // window-span geometry. Room tags keep the model readable, and floor
            // tints carry explicit fills (an unmatched class renders BLACK floors).
            Assert.Contains("function axonWallCuts", app, StringComparison.Ordinal);
            Assert.Contains("function axonWallVolumes", app, StringComparison.Ordinal);

            // Painter order beside the core: boxes sort by their NEAREST footprint
            // corner (a centroid key let walls slice through the core's mass), and
            // solid wall runs split at the core's boundary lines because a long
            // wall passing behind the core while extending toward the viewer has
            // no single correct depth key.
            Assert.Contains("nearestDepth", SliceFunction(app, "axonBox"), StringComparison.Ordinal);
            Assert.Contains("sortSplits", SliceFunction(app, "renderAxonView"), StringComparison.Ordinal);
            Assert.Contains("horizontal ? sortSplits.x : sortSplits.y", SliceFunction(app, "axonWallVolumes"), StringComparison.Ordinal);
            Assert.Contains("windowSpanForSide(roomBounds, side)", SliceFunction(app, "axonWallCuts"), StringComparison.Ordinal);
            Assert.Contains("axonOpenings.doorHead", app, StringComparison.Ordinal);
            Assert.Contains("\"axon-glass\"", app, StringComparison.Ordinal);
            Assert.Contains(".axon-side-y.axon-glass", styles, StringComparison.Ordinal);
            Assert.Contains(".axon-room-tag", styles, StringComparison.Ordinal);
            Assert.Contains(".axon-floor { stroke: none; fill:", styles, StringComparison.Ordinal);
            Assert.Contains(".axon-floor.room-bathroom", styles, StringComparison.Ordinal);

            // Wet rooms read as tiled floors in the 2D plan, under the fixtures.
            Assert.Contains("function renderWetRoomFloors", app, StringComparison.Ordinal);
            Assert.Contains("id: \"wetTile\"", app, StringComparison.Ordinal);
            Assert.Contains(".floor-tile", styles, StringComparison.Ordinal);

            // Save SVG must carry every view's classes as literals.
            string exportStyles = SliceFunction(app, "svgStyleElement");
            Assert.Contains(".axon-top.axon-glass", exportStyles, StringComparison.Ordinal);
            Assert.Contains(".axon-floor.room-bathroom", exportStyles, StringComparison.Ordinal);
            Assert.Contains(".circ-flow", exportStyles, StringComparison.Ordinal);
            Assert.Contains(".floor-tile", exportStyles, StringComparison.Ordinal);
        }

        [Fact]
        public void PlateRescaleCarriesCoreAlongTheSameAffineMap()
        {
            string app = ReadWebFile("app.js");
            string syncInputFromForm = SliceFunction(app, "syncInputFromForm");
            string rescaleCoreFields = SliceFunction(app, "rescaleCoreFields");

            // Regression: scaling a non-rectangular plate without moving the core
            // strands it outside the outline (a hard engine error). The core must
            // ride the exact affine map the outer polygon and holes use, BEFORE the
            // form's core fields are read back into the input.
            Assert.Contains("rescaleCoreFields(input, floorBounds, scaleX, scaleY)", syncInputFromForm, StringComparison.Ordinal);
            AssertBefore(
                syncInputFromForm,
                "rescaleCoreFields(input, floorBounds, scaleX, scaleY)",
                "applyCoreFromForm(input, width, depth)",
                "Core fields must be rescaled before they are read back into the input.");
            Assert.Contains("(coreBounds.minX - floorBounds.minX) * scaleX", rescaleCoreFields, StringComparison.Ordinal);
            Assert.Contains("coreBounds.width * scaleX", rescaleCoreFields, StringComparison.Ordinal);
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

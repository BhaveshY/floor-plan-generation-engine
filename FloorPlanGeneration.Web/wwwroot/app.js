const state = {
  samples: [],
  input: null,
  response: null,
  lastPreviewResponse: null,
  selectedVariantId: "",
  viewMode: "plan",
  zoom: 1,
  panX: 0,
  panY: 0,
  canvasTool: "select",
  gridVisible: false,
  isFullscreen: false,
  spaceHeld: false,
  panDrag: null,
  viewFrame: null,
  undoStack: [],
  redoStack: [],
  editMode: false,
  labelsVisible: true,
  editReadout: "",
  inputDirty: false,
  previewStale: false,
  autoGenerateTimer: 0,
  runSerial: 0,
  inputRevision: 0,
  pendingRunMode: "",
  busy: false,
  busyRunId: 0,
  dragEdit: null,
  geomDrag: null,
  wallDrag: null,
  // Manual edits stay valid exactly as long as the engine input that produced
  // their variants: both signatures are the stringified generate request.
  editsSignature: "",
  lastRunInputSignature: "",
  snapSuspended: false,
  // Direct, manual geometry edits the user makes on top of generated output,
  // keyed by variantId then kind ("room"/"unit") then element id. These are an
  // overlay on the engine result: they survive selection/zoom/undo but are
  // intentionally cleared whenever the engine regenerates a fresh layout.
  geometryEdits: {},
  selection: null,
  syncing: false,
  // Local AI brief parsing (Claude Code / Codex CLI on this machine). Detected
  // at boot via /api/prompt/status; the heuristic parser always remains the
  // fallback so the app never depends on it.
  aiBrief: { available: false, provider: "", providers: [] }
};

const draftKey = "floor-engine-web-draft-v2";
const aiAssistKey = "floor-engine-web-ai-assist";
const aiProviderKey = "floor-engine-web-ai-provider";
// Bumped whenever the manual-edit math changes shape. Restored drafts carrying
// edits from an older engine of this editor are discarded rather than replayed:
// overrides committed under retired math (pre span-absorption, pre explicit
// wall/door overrides) are exactly the bent-wall artifacts they would re-create.
const geometryEditsVersion = 3;
const unitTypes = ["studio", "one_bed", "two_bed"];
// Hard ceiling (in model metres) for any plan label so a bad data value can
// never render the meter-tall SVG text overlays that this canvas used to show.
const maxPlanLabelFontSize = 0.85;
// Canvas zoom ceiling. Raised well past 4x so the detailed room furniture can be
// inspected closely in the editor; pan keeps the zoomed view navigable.
const maxZoom = 8;
const undoStackLimit = 60;

const els = {
  sampleSelect: document.getElementById("sampleSelect"),
  setupForm: document.getElementById("setupForm"),
  setupSubtitle: document.getElementById("setupSubtitle"),
  setupReview: document.getElementById("setupReview"),
  setupGenerateBtn: document.getElementById("setupGenerateBtn"),
  projectName: document.getElementById("projectName"),
  aiAssistRow: document.getElementById("aiAssistRow"),
  aiAssistToggle: document.getElementById("aiAssistToggle"),
  aiAssistLabel: document.getElementById("aiAssistLabel"),
  aiProviderSelect: document.getElementById("aiProviderSelect"),
  promptGenerateBtn: document.getElementById("promptGenerateBtn"),
  promptUnderstood: document.getElementById("promptUnderstood"),
  floorWidth: document.getElementById("floorWidth"),
  floorDepth: document.getElementById("floorDepth"),
  coreX: document.getElementById("coreX"),
  coreY: document.getElementById("coreY"),
  coreWidth: document.getElementById("coreWidth"),
  coreDepth: document.getElementById("coreDepth"),
  minCorridorWidth: document.getElementById("minCorridorWidth"),
  minUnitArea: document.getElementById("minUnitArea"),
  minRoomWidth: document.getElementById("minRoomWidth"),
  strictnessInput: document.getElementById("strictnessInput"),
  daylightBedrooms: document.getElementById("daylightBedrooms"),
  daylightLiving: document.getElementById("daylightLiving"),
  variantInput: document.getElementById("variantInput"),
  seedInput: document.getElementById("seedInput"),
  inputEditor: document.getElementById("inputEditor"),
  runStatus: document.getElementById("runStatus"),
  resultSubtitle: document.getElementById("resultSubtitle"),
  planSubtitle: document.getElementById("planSubtitle"),
  scheduleSubtitle: document.getElementById("scheduleSubtitle"),
  loadSampleBtn: document.getElementById("loadSampleBtn"),
  validateBtn: document.getElementById("validateBtn"),
  generateBtn: document.getElementById("generateBtn"),
  openInputBtn: document.getElementById("openInputBtn"),
  applyJsonBtn: document.getElementById("applyJsonBtn"),
  inputFile: document.getElementById("inputFile"),
  formatBtn: document.getElementById("formatBtn"),
  downloadInputBtn: document.getElementById("downloadInputBtn"),
  variantSelect: document.getElementById("variantSelect"),
  saveSvgBtn: document.getElementById("saveSvgBtn"),
  planSvg: document.getElementById("planSvg"),
  previewFrame: document.querySelector(".preview-frame"),
  modelViewport: document.getElementById("modelViewport"),
  viewerCanvasHost: document.getElementById("viewerCanvasHost"),
  viewerOrbitBtn: document.getElementById("viewerOrbitBtn"),
  viewerWalkBtn: document.getElementById("viewerWalkBtn"),
  viewerGlbBtn: document.getElementById("viewerGlbBtn"),
  viewerHint: document.getElementById("viewerHint"),
  editReadout: document.getElementById("editReadout"),
  zoomLevel: document.getElementById("zoomLevel"),
  planTitleBlock: document.getElementById("planTitleBlock"),
  emptyPreview: document.getElementById("emptyPreview"),
  legendRow: document.getElementById("legendRow"),
  metricsRow: document.getElementById("metricsRow"),
  variantList: document.getElementById("variantList"),
  selectionInspector: document.getElementById("selectionInspector"),
  roomScheduleList: document.getElementById("roomScheduleList"),
  diagnosticList: document.getElementById("diagnosticList"),
  validationList: document.getElementById("validationList"),
  unitSchedule: document.getElementById("unitSchedule"),
  outputJson: document.getElementById("outputJson"),
  cliCommand: document.getElementById("cliCommand"),
  exportSummary: document.getElementById("exportSummary"),
  hypergraphPreview: document.getElementById("hypergraphPreview"),
  copyOutputBtn: document.getElementById("copyOutputBtn"),
  downloadOutputBtn: document.getElementById("downloadOutputBtn"),
  exportCardGrid: document.querySelector(".export-card-grid"),
  modeButtons: Array.from(document.querySelectorAll("[data-view-mode]")),
  canvasButtons: Array.from(document.querySelectorAll("[data-canvas-action]")),
  topNavLinks: Array.from(document.querySelectorAll(".top-nav a")),
  variantCountLabel: document.getElementById("variantCountLabel"),
  roomScheduleCountLabel: document.getElementById("roomScheduleCountLabel"),
  issueCountLabel: document.getElementById("issueCountLabel"),
  unitCountLabel: document.getElementById("unitCountLabel"),
  checkCountLabel: document.getElementById("checkCountLabel")
};

init();

async function init() {
  bindEvents();
  updateActiveNav();
  probeAiAssist();
  try {
    await loadSamples();
    if (!restoreDraft()) {
      await loadSelectedSample(false);
    }
    // Open straight onto a generated plan instead of a bare input outline.
    if (state.input && !state.response) {
      runEngine(false);
    }
  } catch (error) {
    els.sampleSelect.innerHTML = '<option value="">Manual input</option>';
    setInput(ensureInputShape({ project: { id: "manual-project", name: "Manual Floor Plan" } }));
    renderError("startup_failed", `The web app could not load its starter data. ${error.message}`);
    setStatus("Starter data unavailable");
    return;
  }
  renderAll();
  await runEngine(false);
  scrollToHashTarget(window.location.hash, true);
}

function bindEvents() {
  els.loadSampleBtn.addEventListener("click", () => loadSelectedSample(true));
  // Picking a template should immediately show its plan, like clicking Load.
  els.sampleSelect.addEventListener("change", () => loadSelectedSample(true));
  els.validateBtn.addEventListener("click", () => runEngine(true));
  els.generateBtn.addEventListener("click", () => runEngine(false));
  els.openInputBtn.addEventListener("click", () => els.inputFile.click());
  els.applyJsonBtn.addEventListener("click", applyJsonFromEditor);
  els.inputFile.addEventListener("change", openInputFile);
  els.setupForm.addEventListener("input", handleSetupInput);
  els.setupForm.addEventListener("change", handleSetupInput);
  els.setupForm.addEventListener("click", handleStepperClick);
  if (els.setupGenerateBtn) {
    els.setupGenerateBtn.addEventListener("click", async () => {
      await runEngine(false);
      navigateToHash("#plan");
    });
  }
  if (els.promptGenerateBtn) {
    els.promptGenerateBtn.addEventListener("click", () => generateFromPrompt());
  }
  if (els.aiAssistToggle) {
    els.aiAssistToggle.addEventListener("change", () => {
      try { localStorage.setItem(aiAssistKey, els.aiAssistToggle.checked ? "on" : "off"); } catch (error) { /* private mode */ }
    });
  }
  if (els.aiProviderSelect) {
    els.aiProviderSelect.addEventListener("change", () => {
      try { localStorage.setItem(aiProviderKey, els.aiProviderSelect.value); } catch (error) { /* private mode */ }
      refreshAiAssistLabel();
    });
  }
  els.inputEditor.addEventListener("input", saveDraft);
  els.formatBtn.addEventListener("click", formatInput);
  els.downloadInputBtn.addEventListener("click", () => downloadText("floor-plan-input.json", els.inputEditor.value));
  els.saveSvgBtn.addEventListener("click", saveSvg);
  els.exportSummary.addEventListener("click", handleExportAction);
  els.exportCardGrid.addEventListener("click", handleExportAction);
  els.modeButtons.forEach((button) => button.addEventListener("click", () => setViewMode(button.dataset.viewMode)));
  els.canvasButtons.forEach((button) => button.addEventListener("click", () => handleCanvasAction(button.dataset.canvasAction)));
  // Camera mode switches must run synchronously when the viewer is already
  // loaded: requestPointerLock only works inside the user's click gesture.
  if (els.viewerOrbitBtn) {
    els.viewerOrbitBtn.addEventListener("click", () => withViewer3d((viewer) => viewer.setCameraMode("orbit")));
  }
  if (els.viewerWalkBtn) {
    els.viewerWalkBtn.addEventListener("click", () => withViewer3d((viewer) => viewer.setCameraMode("walk")));
  }
  if (els.viewerGlbBtn) {
    els.viewerGlbBtn.addEventListener("click", () => withViewer3d((viewer) => viewer.exportGlb()));
  }
  els.topNavLinks.forEach((link) => link.addEventListener("click", (event) => {
    event.preventDefault();
    navigateToHash(link.getAttribute("href"));
  }));
  const topNav = document.querySelector(".top-nav");
  if (topNav) {
    topNav.addEventListener("click", (event) => {
      const action = event.target.closest ? event.target.closest("[data-nav]") : null;
      if (action) {
        event.preventDefault();
        handleTopNavAction(action.dataset.nav);
      }
    });
    topNav.addEventListener("keydown", (event) => {
      if (event.key !== "Enter" && event.key !== " ") {
        return;
      }
      const action = event.target.closest ? event.target.closest("[data-nav]") : null;
      if (action) {
        event.preventDefault();
        handleTopNavAction(action.dataset.nav);
      }
    });
  }
  window.addEventListener("hashchange", () => {
    updateActiveNav();
    scrollToHashTarget();
  });
  els.planSvg.addEventListener("click", handlePlanClick);
  els.emptyPreview.addEventListener("click", handleEmptyPreviewAction);
  els.planSvg.addEventListener("dblclick", handlePlanDoubleClick);
  els.planSvg.addEventListener("keydown", handlePlanActionKeyDown);
  els.planSvg.addEventListener("pointerdown", handlePlanPointerDown);
  window.addEventListener("pointermove", handlePlanPointerMove);
  window.addEventListener("pointerup", finishPlanPointerEdit);
  window.addEventListener("pointercancel", finishPlanPointerEdit);
  els.planSvg.addEventListener("wheel", handleCanvasWheel, { passive: false });
  els.planSvg.addEventListener("pointerdown", handleCanvasPanDown);
  window.addEventListener("pointermove", handleCanvasPanMove);
  window.addEventListener("pointerup", handleCanvasPanUp);
  window.addEventListener("pointercancel", handleCanvasPanUp);
  document.addEventListener("keydown", handleEditorKeyDown);
  document.addEventListener("keyup", handleEditorKeyUp);
  document.addEventListener("fullscreenchange", handleFullscreenChange);
  document.addEventListener("webkitfullscreenchange", handleFullscreenChange);
  els.selectionInspector.addEventListener("click", handleInspectorAction);
  els.roomScheduleList.addEventListener("click", handleRoomScheduleClick);
  els.variantSelect.addEventListener("change", () => {
    state.selectedVariantId = els.variantSelect.value;
    state.zoom = 1;
    state.panX = 0;
    state.panY = 0;
    renderAll();
  });

  els.inputEditor.addEventListener("dragover", (event) => {
    event.preventDefault();
    els.inputEditor.classList.add("dragging");
  });
  els.inputEditor.addEventListener("dragleave", () => els.inputEditor.classList.remove("dragging"));
  els.inputEditor.addEventListener("drop", openDroppedInputFile);

  let resizeFrame = 0;
  window.addEventListener("resize", () => {
    if (resizeFrame) {
      return;
    }
    resizeFrame = requestAnimationFrame(() => {
      resizeFrame = 0;
      if (els.planSvg.getAttribute("viewBox")) {
        renderPreview(currentVisualOutput());
      }
    });
  });
}

async function loadSamples() {
  const samples = await fetchJson("/api/samples");
  state.samples = samples;
  els.sampleSelect.innerHTML = samples
    .map((sample) => `<option value="${escapeHtml(sample.name)}">${escapeHtml(titleCase(sample.name))}</option>`)
    .join("");
  if (samples.some((sample) => sample.name === "rectangular-core")) {
    els.sampleSelect.value = "rectangular-core";
  }
}

async function loadSelectedSample(autoRun) {
  const name = els.sampleSelect.value || "rectangular-core";
  const sample = await fetchJson(`/api/samples/${encodeURIComponent(name)}`);
  setInput(sample);
  setStatus(`Loaded ${titleCase(name)}`);
  saveDraft();
  if (autoRun) {
    await runEngine(false);
  }
}

function restoreDraft() {
  try {
    const draft = JSON.parse(localStorage.getItem(draftKey) || "null");
    if (!draft || !draft.inputJson) {
      return false;
    }

    const parsed = JSON.parse(draft.inputJson);
    setInput(parsed);
    if (draft.geometryEdits && typeof draft.geometryEdits === "object"
      && draft.editsVersion === geometryEditsVersion) {
      state.geometryEdits = draft.geometryEdits;
      state.editsSignature = typeof draft.editsSignature === "string" ? draft.editsSignature : "";
    }
    if (typeof draft.brief === "string") {
      state.brief = draft.brief;
      els.projectName.value = draft.brief;
    }
    if (draft.sampleName && state.samples.some((sample) => sample.name === draft.sampleName)) {
      els.sampleSelect.value = draft.sampleName;
    }
    setStatus("Restored local draft");
    return true;
  } catch (_) {
    localStorage.removeItem(draftKey);
    return false;
  }
}

function saveDraft() {
  try {
    localStorage.setItem(draftKey, JSON.stringify({
      inputJson: els.inputEditor.value,
      sampleName: els.sampleSelect.value,
      brief: state.brief,
      // Manual room/wall edits ride along: variant ids are deterministic for a
      // given input+seed, so overrides re-apply cleanly after a reload.
      geometryEdits: state.geometryEdits,
      editsSignature: state.editsSignature,
      editsVersion: geometryEditsVersion,
      savedAt: new Date().toISOString()
    }));
  } catch (_) {
    // Draft persistence is a convenience; generation should keep working without it.
  }
}

function setInput(input, options = {}) {
  state.input = ensureInputShape(clone(input));
  state.brief = (state.input.project && state.input.project.name) || "";
  state.inputRevision += 1;
  state.runSerial += 1;
  state.inputDirty = false;
  clearAutoGenerate();
  state.dragEdit = null;
  state.selection = null;
  // A fresh document starts its own edit history; never let undo/redo reach back
  // into a previously loaded plan (or replay a stale redo onto this one).
  state.undoStack = [];
  state.redoStack = [];
  state.geometryEdits = {};
  state.editsSignature = "";
  state.editReadout = state.editMode ? editSummary(state.input) : "";
  if (!options.preserveResponse) {
    state.response = null;
    state.lastPreviewResponse = null;
    state.selectedVariantId = "";
    state.previewStale = false;
  }
  syncFormFromInput(state.input);
  setEditorFromInput(state.input);
  renderAll();
}

function handleSetupInput(event) {
  if (state.syncing) {
    return;
  }

  // Prompt-area controls are not live generation inputs. The brief (and the
  // blur fired by clicking "Generate from prompt"), the AI toggle and the
  // provider picker must never arm the auto-generate pipeline: a stale run
  // with the old settings would land mid-AI-parse, re-render the old plan and
  // bury the prompt's result. The button is the prompt's only trigger.
  if (event && (event.target === els.aiAssistToggle || event.target === els.aiProviderSelect)) {
    return;
  }
  if (event && els.projectName && event.target === els.projectName) {
    state.brief = els.projectName.value.trim();
    saveDraft();
    return;
  }

  // A new user edit starts a fresh history branch: any pending redo is now stale and
  // must not be replayable onto this edit. (This entry point only fires on genuine
  // user input — programmatic form sync sets state.syncing and returns above.)
  state.redoStack = [];
  syncInputFromForm();
  setEditorFromInput(state.input);
  saveDraft();
  markInputDirty("Updating plan", 650);
  renderAll();
}

function handleStepperClick(event) {
  const button = event.target.closest ? event.target.closest(".stepper [data-step]") : null;
  if (!button) {
    return;
  }

  const input = button.closest(".stepper") ? button.closest(".stepper").querySelector("input") : null;
  if (!input) {
    return;
  }

  event.preventDefault();
  const direction = Number(button.dataset.step) || 0;
  const stepAmount = Number(input.step) || 1;
  const min = input.min === "" ? Number.NEGATIVE_INFINITY : Number(input.min);
  const max = input.max === "" ? Number.POSITIVE_INFINITY : Number(input.max);
  const current = Number(input.value);
  const base = Number.isFinite(current) ? current : (Number.isFinite(min) ? min : 0);
  input.value = String(clamp(round(base + direction * stepAmount), min, max));
  input.dispatchEvent(new Event("input", { bubbles: true }));
}

function syncFormFromInput(input) {
  state.syncing = true;
  try {
    const floorBounds = boundsOfPoints(input.floorplate.outer.points);
    const core = firstCore(input);
    const coreBounds = core ? boundsOfPoints(core.polygon.points) : null;
    const settings = input.generationSettings || {};
    const rules = input.rules || {};
    const project = input.project || {};

    els.projectName.value = (typeof state.brief === "string" && state.brief.length) ? state.brief : (project.name || "");
    els.floorWidth.value = fieldNumber(floorBounds ? floorBounds.width : 42);
    els.floorDepth.value = fieldNumber(floorBounds ? floorBounds.height : 22);
    // A dwelling has no building core and no unit mix: hiding those sections
    // keeps the panel honest instead of showing stale, ignored values.
    const dwellingDocument = isDwellingInput(input);
    const coreFieldset = document.getElementById("coreFieldset");
    if (coreFieldset) { coreFieldset.hidden = dwellingDocument; }
    const unitMixFieldset = document.getElementById("unitMixFieldset");
    if (unitMixFieldset) { unitMixFieldset.hidden = dwellingDocument; }
    els.coreX.value = fieldNumber(coreBounds ? coreBounds.minX : 18);
    els.coreY.value = fieldNumber(coreBounds ? coreBounds.minY : 8);
    els.coreWidth.value = fieldNumber(coreBounds ? coreBounds.width : 6);
    els.coreDepth.value = fieldNumber(coreBounds ? coreBounds.height : 6);
    els.minCorridorWidth.value = fieldNumber(rules.minCorridorWidth || 1.8);
    els.minUnitArea.value = fieldNumber(rules.minUnitArea || 25);
    els.minRoomWidth.value = fieldNumber(rules.minRoomWidth || 2.4);
    els.strictnessInput.value = settings.strictness || "balanced";
    els.daylightBedrooms.checked = rules.requireDaylightForBedrooms !== false;
    els.daylightLiving.checked = rules.requireDaylightForLiving !== false;
    els.variantInput.value = settings.variantCount || 4;
    els.seedInput.value = Number.isFinite(Number(project.seed)) ? String(project.seed) : "1";

    unitTypes.forEach((type) => {
      const row = document.querySelector(`.mix-row[data-unit-type="${type}"]`);
      const target = findUnitTarget(input, type);
      row.querySelector('[data-field="targetRatio"]').value = fieldNumber((target.targetRatio || 0) * 100);
      row.querySelector('[data-field="minArea"]').value = fieldNumber(target.minArea || defaultUnitTarget(type).minArea);
      row.querySelector('[data-field="maxArea"]').value = fieldNumber(target.maxArea || defaultUnitTarget(type).maxArea);
    });

    els.setupSubtitle.textContent = project.name || titleCase(project.id || "project");
  } finally {
    state.syncing = false;
  }
}

function syncInputFromForm() {
  const input = ensureInputShape(state.input || {});
  const floorBounds = boundsOfPoints(input.floorplate.outer.points) || { minX: 0, minY: 0, width: 42, height: 22 };
  const dwellingMode = isDwellingInput(input);
  // Typed values respect the same envelope the steppers and canvas drags
  // enforce — a hand-entered "5" must not slip a 5 m floorplate past the form.
  // A single dwelling is legitimately small, so its floor allows 4 m.
  const width = clamp(readPositive(els.floorWidth, floorBounds.width), dwellingMode ? 4 : 8, 300);
  const depth = clamp(readPositive(els.floorDepth, floorBounds.height), dwellingMode ? 4 : 8, 300);
  const scaleX = floorBounds.width > 0 ? width / floorBounds.width : 1;
  const scaleY = floorBounds.height > 0 ? depth / floorBounds.height : 1;

  const briefText = els.projectName.value.trim();
  state.brief = briefText;
  input.project.name = projectNameFromBrief(briefText);
  input.project.id = slugify(input.project.name);
  input.project.seed = Math.trunc(readNumber(els.seedInput, input.project.seed || 1));
  input.project.units = input.project.units || "m";
  input.project.tolerance = Number.isFinite(Number(input.project.tolerance)) ? input.project.tolerance : 0.01;

  input.floorplate.outer.id = input.floorplate.outer.id || "floorplate-01";
  input.floorplate.outer.points = scalePointsToBox(input.floorplate.outer.points, floorBounds, width, depth);
  input.floorplate.holes = (input.floorplate.holes || []).map((hole) => ({
    ...hole,
    points: (hole.points || []).map((point) => ({
      x: round((point.x - floorBounds.minX) * scaleX),
      y: round((point.y - floorBounds.minY) * scaleY)
    }))
  }));
  rescaleCoreFields(input, floorBounds, scaleX, scaleY);

  input.rules.minCorridorWidth = clamp(readPositive(els.minCorridorWidth, input.rules.minCorridorWidth || 1.8), 0.9, 12);
  input.rules.minUnitArea = clamp(readPositive(els.minUnitArea, input.rules.minUnitArea || 25), 10, 400);
  input.rules.minRoomWidth = clamp(readPositive(els.minRoomWidth, input.rules.minRoomWidth || 2.4), 1.2, 20);
  input.rules.minRoomDepth = input.rules.minRoomDepth || input.rules.minRoomWidth;
  input.rules.doorWidth = input.rules.doorWidth || 0.9;
  input.rules.wetRoomAdjacencyPreferred = input.rules.wetRoomAdjacencyPreferred !== false;
  input.rules.requireDaylightForBedrooms = els.daylightBedrooms.checked;
  input.rules.requireDaylightForLiving = els.daylightLiving.checked;
  // Snap unit bays and room partitions to a 0.6 m planning grid so apartments
  // read as a regular bay rhythm instead of arbitrary widths (0 disables it).
  input.rules.gridModule = input.rules.gridModule || 0.6;

  input.generationSettings.variantCount = clamp(Math.trunc(readNumber(els.variantInput, input.generationSettings.variantCount || 4)), 1, 20);
  input.generationSettings.strictness = els.strictnessInput.value || "balanced";
  input.generationSettings.timeLimitMilliseconds = input.generationSettings.timeLimitMilliseconds || 1000;
  input.generationSettings.weightedVariation = input.generationSettings.weightedVariation !== false;
  input.generationSettings.scoringWeights = input.generationSettings.scoringWeights || defaultScoringWeights();

  if (dwellingMode) {
    // A dwelling has no building core and ignores the unit-mix rows: the plate
    // IS the single unit, and its type lives in program.targetUnitTypes already.
    input.fixedElements = [];
    input.access.entryPoints = [{ x: round(width / 2), y: 0 }];
    input.access.verticalCoreAccess = [];
    input.access.corridorStartPoints = input.access.corridorStartPoints || [];
    input.access.corridorEndPoints = input.access.corridorEndPoints || [];
    input.access.corridorCenterlines = input.access.corridorCenterlines || [];
  } else {
    applyCoreFromForm(input, width, depth);
    input.program.targetUnitTypes = readUnitMixFromForm(input);
  }

  if (!Array.isArray(input.program.roomTypes) || input.program.roomTypes.length === 0) {
    input.program.roomTypes = defaultRoomTypes();
  }

  state.input = input;
}

// When the plate is rescaled, the core must ride the same affine map as the
// outer polygon and holes. Rebuilding it from stale form coordinates keeps it
// inside the new BOUNDING BOX but, on a non-rectangular plate, can strand it
// outside the actual outline — a hard engine error. Scaling the existing core
// geometry preserves containment exactly, then the form fields follow suit.
function rescaleCoreFields(input, floorBounds, scaleX, scaleY) {
  if (Math.abs(scaleX - 1) < 1e-9 && Math.abs(scaleY - 1) < 1e-9) { return; }
  const core = firstCore(input);
  if (!core || !core.polygon || !Array.isArray(core.polygon.points) || !core.polygon.points.length) { return; }
  const coreBounds = boundsOfPoints(core.polygon.points);
  if (!coreBounds) { return; }
  els.coreX.value = fieldNumber(round((coreBounds.minX - floorBounds.minX) * scaleX));
  els.coreY.value = fieldNumber(round((coreBounds.minY - floorBounds.minY) * scaleY));
  els.coreWidth.value = fieldNumber(round(coreBounds.width * scaleX));
  els.coreDepth.value = fieldNumber(round(coreBounds.height * scaleY));
}

function applyCoreFromForm(input, width, depth) {
  let core = firstCore(input);
  if (!core) {
    core = {
      id: "core-01",
      type: "core",
      blocksGeneration: true,
      polygon: { id: "core-01", points: [] }
    };
    input.fixedElements.push(core);
  }

  const coreWidth = clamp(readPositive(els.coreWidth, 6), 1, Math.max(1, width));
  const coreDepth = clamp(readPositive(els.coreDepth, 6), 1, Math.max(1, depth));
  const x = clamp(readNumber(els.coreX, Math.max(0, (width - coreWidth) / 2)), 0, Math.max(0, width - coreWidth));
  const y = clamp(readNumber(els.coreY, Math.max(0, (depth - coreDepth) / 2)), 0, Math.max(0, depth - coreDepth));

  core.id = core.id || "core-01";
  core.type = core.type || "core";
  core.blocksGeneration = core.blocksGeneration !== false;
  core.polygon = {
    id: core.polygon && core.polygon.id ? core.polygon.id : core.id,
    points: rectPoints(x, y, coreWidth, coreDepth)
  };

  const coreCenterX = round(x + coreWidth / 2);
  input.access.entryPoints = [{ x: coreCenterX, y: 0 }];
  input.access.verticalCoreAccess = [{ x: coreCenterX, y: round(y + coreDepth) }];
  input.access.corridorStartPoints = input.access.corridorStartPoints || [];
  input.access.corridorEndPoints = input.access.corridorEndPoints || [];
  input.access.corridorCenterlines = input.access.corridorCenterlines || [];
}

function readUnitMixFromForm(input) {
  const previous = input.program.targetUnitTypes || [];
  return unitTypes.map((type) => {
    const row = document.querySelector(`.mix-row[data-unit-type="${type}"]`);
    const prior = previous.find((target) => target.type === type) || defaultUnitTarget(type);
    const ratio = clamp(readNumber(row.querySelector('[data-field="targetRatio"]'), (prior.targetRatio || 0) * 100), 0, 100) / 100;
    const minArea = readPositive(row.querySelector('[data-field="minArea"]'), prior.minArea);
    const maxArea = Math.max(minArea, readPositive(row.querySelector('[data-field="maxArea"]'), prior.maxArea));
    return {
      type,
      minArea,
      maxArea,
      targetCount: prior.targetCount || 0,
      targetRatio: ratio,
      weight: prior.weight || (type === "two_bed" ? 0.8 : 1.0)
    };
  });
}

// ---------------------------------------------------------------------------
// Prompt -> floor plan. A deterministic, CSP-safe natural-language parser that
// maps a written brief onto the existing input schema, then reuses the same
// form -> input pipeline the manual controls drive. No external model call.
// ---------------------------------------------------------------------------

// Real briefs arrive glued and typoed ("a1 bhkapartment", "2bhkflat", "40x22").
// Normalising before matching is what makes the parser forgiving: digits and
// letters get breathing room, common compound words split, punctuation calms.
function normalizePromptText(text) {
  return ` ${String(text || "").toLowerCase()} `
    .replace(/[,;:!?]/g, " ")
    // "a1bhk" -> "a 1bhk" -> "a 1 bhk"; "40x22" -> "40 x 22" (the dims regex
    // tolerates both, and bhk/bed tokens need the boundary to exist at all).
    .replace(/(\d)([a-z])/g, "$1 $2")
    .replace(/([a-z])(\d)/g, "$1 $2")
    // De-glue the usual suspects: "bhkapartment", "bedflat", "floorplan".
    .replace(/\b(bhk|bedroom|bed|br|rk)(apartments?|flats?|homes?|houses?|residences?|units?|plans?)\b/g, "$1 $2")
    // "bk" is the dropped-h typo for "bhk" ("2 bk apartment"); only fix it when a
    // count precedes it, so it can never collide with unrelated words.
    .replace(/\b(\d+|one|two|three|four|five|single|double)\s+bk\b/g, "$1 bhk")
    .replace(/\bfloorplans?\b/g, "floor plan")
    .replace(/\s+/g, " ");
}

// Stable 32-bit FNV-1a hash: the same brief always reproduces its layout, a
// different brief genuinely explores a different one.
function promptSeed(text) {
  let hash = 0x811c9dc5;
  const s = String(text || "");
  for (let i = 0; i < s.length; i += 1) {
    hash ^= s.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return (hash >>> 0) % 1000000 + 1;
}

function parsePrompt(text) {
  const t = normalizePromptText(text);
  const intent = { understood: [] };
  const note = (label) => { if (label && !intent.understood.includes(label)) { intent.understood.push(label); } };

  const types = [];
  const wantsNumber = (words) => new RegExp(`\\b(?:${words})\\s*(?:bed|bedroom|bhk|br)s?\\b`).test(t);
  if (/\bstudios?\b/.test(t) || /\b(?:1|one)\s*rk\b/.test(t)) { types.push("studio"); }
  if (wantsNumber("1|one|single") || /\bone[\s-]?bed\b/.test(t)) { types.push("one_bed"); }
  if (wantsNumber("2|two|double") || /\btwo[\s-]?bed\b/.test(t)) { types.push("two_bed"); }
  if (wantsNumber("3|three|3\\.5|4|four|5|five")) { types.push("two_bed"); }
  const mixTypes = [...new Set(types)];
  if (mixTypes.length) {
    intent.mix = {};
    const base = Math.floor(100 / mixTypes.length);
    mixTypes.forEach((type, index) => {
      intent.mix[type] = index === mixTypes.length - 1 ? 100 - base * (mixTypes.length - 1) : base;
    });
    note(`${mixTypes.map(unitTypeLabel).join(" + ")} units`);
  }

  // A brief about ONE apartment ("a 2 bhk flat", "1 RK", "house plan") asks for
  // a dwelling generated from scratch, not a corridor building floor. Plural or
  // building words keep the multi-unit pipeline.
  const wantsRoomKitchen = /\b(?:\d+|one)\s*rk\b/.test(t) || /\broom\s*(?:\+|and|&)?\s*kitchen\b/.test(t);
  const dwellingPlural = /\b(?:apartments|flats|homes|houses|units|residences|dwellings|studios)\b/.test(t);
  const buildingWords = /\b(?:building|block|tower|plate|complex|development|housing|corridor|core|mix|floors?)\b/.test(t);
  const singularDwelling =
    /\b(?:an?|one|single|my|this)\s+(?:\d+\s*)?(?:bhk|rk|bed(?:room)?s?)?\s*(?:apartment|flat|home|house|unit|dwelling)\b/.test(t);
  // Interior-room features ("2 bathrooms", "a pooja room", "a balcony") next to a
  // bedroom count describe one home's program, not a building floor — so "2 BHK
  // with 2 bathrooms" is a single dwelling even without the word "apartment", as
  // long as nothing plural or building-scale appears.
  const hasBedCount = /\b(?:\d+|one|two|three|four|single|double)\s*(?:bhk|bed(?:room)?s?|br)\b/.test(t);
  const wantsInteriorRooms =
    /\b(?:bath(?:room)?s?|toilets?|washrooms?|wcs?|kitchens?|kitchenettes?|pooja|puja|prayer\s*room|mandir)\b/.test(t)
    || /\b(?:stud(?:y|ies)|living|lounges?|drawing|dinings?|stores?|utility|laundry|balcon(?:y|ies)|terraces?)\b/.test(t);
  const dwellingProgram = hasBedCount && wantsInteriorRooms;
  if (wantsRoomKitchen || ((singularDwelling || dwellingProgram) && !dwellingPlural && !buildingWords)) {
    intent.dwelling = "single";
    const wordToNumber = { one: 1, single: 1, two: 2, double: 2, three: 3, four: 4 };
    let bedrooms = wantsRoomKitchen || /\bstudio\b/.test(t) ? 0 : 1;
    const bedCount = t.match(/\b(\d+|one|two|three|four|single|double)\s*(?:bhk|bed(?:room)?s?|br)\b/);
    if (bedCount) {
      const n = wordToNumber[bedCount[1]] !== undefined ? wordToNumber[bedCount[1]] : Number(bedCount[1]);
      if (Number.isFinite(n)) { bedrooms = clamp(Math.trunc(n), 0, 4); }
    }
    intent.bedrooms = bedrooms;
    note(bedrooms === 0 ? "single dwelling · 1 room + kitchen" : `single dwelling · ${bedrooms} bedroom${bedrooms > 1 ? "s" : ""}`);

    // Room program extras: an explicit count ("2 bathrooms") or, when a room is
    // simply named ("with a study and a pooja room"), one of it. These only
    // shape a single dwelling — the engine partitions the plate around them.
    const wordToCount = { a: 1, an: 1, one: 1, single: 1, two: 2, double: 2, three: 3, four: 4 };
    const countOf = (words) => {
      const counted = t.match(new RegExp(`\\b(\\d+|a|an|one|single|two|double|three|four)\\s+(?:${words})\\b`));
      if (counted) {
        const n = wordToCount[counted[1]] !== undefined ? wordToCount[counted[1]] : Number(counted[1]);
        if (Number.isFinite(n)) { return clamp(Math.trunc(n), 1, 4); }
      }
      return new RegExp(`\\b(?:${words})\\b`).test(t) ? 1 : 0;
    };
    const bathrooms = countOf("bath(?:room)?s?|toilets?|washrooms?|wcs?");
    if (bathrooms > 0) { intent.bathrooms = bathrooms; note(`${bathrooms} bathroom${bathrooms > 1 ? "s" : ""}`); }
    const kitchens = countOf("kitchens?|kitchenettes?");
    if (kitchens > 0) { intent.kitchens = kitchens; note(`${kitchens} kitchen${kitchens > 1 ? "s" : ""}`); }
    const study = countOf("stud(?:y|ies)|home\\s*offices?");
    if (study > 0) { intent.study = study; note(study > 1 ? `${study} studies` : "study"); }
    // A combined "living-dining" is one shared space, never a separate living or
    // dining room, so it suppresses both counts (the engine still seats one living).
    const livingDining = /\bliving[\s/-]*dining\b/.test(t);
    const livings = livingDining ? 0 : countOf("living\\s*(?:rooms?|areas?)|lounges?|drawing\\s*rooms?");
    if (livings > 0) { intent.livings = livings; note(livings > 1 ? `${livings} living rooms` : "living room"); }
    const dining = livingDining ? 0 : countOf("dining\\s*(?:rooms?|halls?|areas?)|separate\\s+dinings?");
    if (dining > 0) { intent.dining = dining; note(dining > 1 ? `${dining} dining rooms` : "separate dining"); }
    const store = countOf("store\\s*rooms?|storerooms?|storage|stores?");
    if (store > 0) { intent.store = store; note(store > 1 ? `${store} store rooms` : "store room"); }
    const utility = countOf("utility(?:\\s*rooms?)?|laundr(?:y|ies)");
    if (utility > 0) { intent.utility = utility; note(utility > 1 ? `${utility} utility rooms` : "utility"); }
    const pooja = countOf("pooja(?:\\s*rooms?)?|puja(?:\\s*rooms?)?|prayer\\s*rooms?|mandirs?");
    if (pooja > 0) { intent.pooja = pooja; note(pooja > 1 ? `${pooja} pooja rooms` : "pooja room"); }
    const balcony = countOf("balcon(?:y|ies)|terraces?|verandahs?|verandas?");
    if (balcony > 0) { intent.balcony = balcony; note(balcony > 1 ? `${balcony} balconies` : "balcony"); }
  }

  // Single-digit dimensions are real for dwellings ("a studio 7 x 5"); values
  // below 3 m are counts, not meters ("2 x 1 bed"), and are ignored.
  const wh = t.match(/(\d{1,3}(?:\.\d+)?)\s*(?:m|metres?|meters?)?\s*(?:x|by|×)\s*(\d{1,3}(?:\.\d+)?)/);
  if (wh && Number(wh[1]) >= 3 && Number(wh[2]) >= 3) {
    intent.width = Number(wh[1]);
    intent.depth = Number(wh[2]);
    note(`${round(intent.width)}×${round(intent.depth)} m plate`);
  } else {
    const wide = t.match(/(\d{2,3}(?:\.\d+)?)\s*(?:m|metres?|meters?)?\s*(?:wide|width|frontage)/);
    if (wide) { intent.width = Number(wide[1]); note(`${round(intent.width)} m wide`); }
    const deep = t.match(/(\d{2,3}(?:\.\d+)?)\s*(?:m|metres?|meters?)?\s*(?:deep|depth)/);
    if (deep) { intent.depth = Number(deep[1]); note(`${round(intent.depth)} m deep`); }
    const area = t.match(/(\d{3,5})\s*(?:sqm|sq\.?\s?m|m2|m²|square\s?(?:metres?|meters?))/);
    if (area && !Number.isFinite(intent.width)) {
      const value = Number(area[1]);
      intent.depth = Number.isFinite(intent.depth) ? intent.depth : 22;
      intent.width = Math.round(value / intent.depth);
      note(`~${value} m² plate`);
    }
  }

  const bedDay = /(?:daylight|sunlit|sunny|bright|natural light|naturally lit)[^.]{0,24}(?:bed|bedroom)/.test(t)
    || /(?:bed|bedroom)[^.]{0,24}(?:daylight|sunlight|natural light|window)/.test(t);
  const bedDark = /(?:interior|internal|windowless|no daylight)[^.]{0,18}(?:bed|bedroom)/.test(t);
  if (bedDark) { intent.daylightBedrooms = false; note("interior bedrooms ok"); }
  else if (bedDay) { intent.daylightBedrooms = true; note("daylight bedrooms"); }
  const livingDay = /(?:daylight|sunlit|bright|natural light|open)[^.]{0,24}(?:living|lounge)/.test(t)
    || /(?:living|lounge)[^.]{0,24}(?:daylight|sunlight|natural light|bright)/.test(t);
  if (livingDay) { intent.daylightLiving = true; note("daylight living"); }

  // Corridor width is clamped to a buildable band so a vague brief can never
  // starve the unit bands of depth. 1.8 m is the engine default; "wide" nudges
  // up, "narrow" down, both within code-sane limits.
  const corr = t.match(/(\d(?:\.\d+)?)\s*(?:m|metres?|meters?)?\s*(?:wide\s*)?corridor/);
  if (corr) { intent.corridor = clamp(Number(corr[1]), 1.2, 2.6); note(`${fieldNumber(intent.corridor)} m corridors`); }
  else if (/(?:wide|generous|broad)[^.]{0,14}corridor|corridor[^.]{0,14}(?:wide|generous)/.test(t)) {
    intent.corridor = 2; note("wide corridors");
  } else if (/(?:narrow|tight|slim|lean)[^.]{0,14}corridor/.test(t)) {
    intent.corridor = 1.4; note("tight corridors");
  }

  // Minimum unit area is capped at 50 so the global floor never exceeds the
  // smallest unit type's usable window (studio tops out at ~55 m²), which would
  // make that type impossible to place. "spacious"/"compact" stay feasible.
  const unitArea = t.match(/units?[^.]{0,18}?(?:at least\s*)?(\d{2,3})\s*(?:sqm|sq\.?\s?m|m2|m²)/);
  if (unitArea) { intent.minUnit = clamp(Number(unitArea[1]), 16, 50); note(`units ≥ ${fieldNumber(intent.minUnit)} m²`); }
  else if (/(?:spacious|large|generous|luxury|premium)[^.]{0,18}(?:unit|apartment|flat|home|residence|studio|bed|dwelling)/.test(t)
    || /(?:unit|apartment|flat|studio|home)s?[^.]{0,12}(?:spacious|large|generous)/.test(t)) {
    intent.minUnit = 38; note("spacious units");
  } else if (/(?:compact|efficient|micro|affordable|small)[^.]{0,18}(?:unit|apartment|flat|home|residence|studio|bed|dwelling)/.test(t)
    || /high[\s-]?density/.test(t)) {
    intent.minUnit = 22; note("compact units");
  }

  const variants = t.match(/(\d{1,2})\s*(?:variant|option|layout|scheme|alternative)s?/);
  if (variants) { intent.variants = clamp(Number(variants[1]), 1, 20); note(`${intent.variants} variants`); }

  // "Strict" validation rejects any unit whose achieved area drifts outside its
  // narrow per-type window, which reliably yields zero buildable variants. So a
  // compliance brief maps to "balanced" — it still enforces every code minimum
  // (corridor, room, daylight, unit area) but tolerates real-world area drift.
  if (/\b(?:strict|rigorous|code[\s-]?compliant|conservative|regulation|compliant)\b/.test(t)) {
    intent.strictness = "balanced"; note("code-aware rules");
  } else if (/\b(?:relaxed|loose|exploratory|experimental|flexible)\b/.test(t)) {
    intent.strictness = "relaxed"; note("relaxed rules");
  }

  // The plate template is a stylistic ask the samples can answer directly.
  if (/\bl[\s-]?shaped?\b/.test(t)) {
    intent.template = "l-shaped-core"; note("L-shaped core plate");
  } else if (/\b(?:irregular|angled|organic|articulated)\b/.test(t)) {
    intent.template = "moderately-irregular-core"; note("irregular core plate");
  } else if (/\b(?:rectangular|simple|straight|bar)\s*(?:core|plate|block|building)?\b/.test(t) && /core|plate|block|building|slab/.test(t)) {
    intent.template = "rectangular-core"; note("rectangular plate");
  }

  // Variety on request: "another", "different", "try again", "shuffle" walk
  // the layout seed forward so each ask explores a new scheme.
  intent.shuffle = /\b(?:another|different|again|new|next|alternative|shuffle|surprise|variation|fresh)\b/.test(t);

  // Every distinct brief deserves a distinct layout: the seed derives from the
  // text, so "1 BHK near the park" and "1 BHK with big kitchens" stop landing
  // on the same scheme while the same brief stays perfectly reproducible.
  intent.seed = promptSeed(t);

  return intent;
}

function unitTypeLabel(type) {
  return { studio: "Studio", one_bed: "1-Bed", two_bed: "2-Bed" }[type] || type;
}

// ---------------------------------------------------------------------------
// SINGLE DWELLING — a brief about one apartment builds an engine input from
// scratch: a small rectangular plate, no core, no corridor, layoutMode
// "single_dwelling". The engine partitions it into real rooms directly.
// ---------------------------------------------------------------------------
const dwellingPresets = {
  0: { width: 7.2, depth: 5.6, type: "studio", minArea: 24, maxArea: 45 },
  1: { width: 9.0, depth: 6.8, type: "one_bed", minArea: 40, maxArea: 70 },
  2: { width: 11.2, depth: 8.4, type: "two_bed", minArea: 65, maxArea: 110 },
  3: { width: 13.2, depth: 9.6, type: "three_bed", minArea: 90, maxArea: 150 },
  4: { width: 15.0, depth: 10.5, type: "three_bed", minArea: 110, maxArea: 180 }
};

function isDwellingInput(input) {
  return Boolean(input && input.generationSettings && input.generationSettings.layoutMode === "single_dwelling");
}

function buildSingleDwellingInput(intent) {
  const bedrooms = clamp(Math.trunc(Number.isFinite(intent.bedrooms) ? intent.bedrooms : 1), 0, 4);
  const bathrooms = clamp(Math.trunc(Number.isFinite(intent.bathrooms) ? intent.bathrooms : 1), 1, 4);
  const kitchens = clamp(Math.trunc(Number.isFinite(intent.kitchens) ? intent.kitchens : 1), 1, 4);
  const livings = clamp(Math.trunc(Number.isFinite(intent.livings) ? intent.livings : 1), 1, 3);
  const study = clamp(Math.trunc(Number.isFinite(intent.study) ? intent.study : 0), 0, 2);
  const dining = clamp(Math.trunc(Number.isFinite(intent.dining) ? intent.dining : 0), 0, 2);
  const store = clamp(Math.trunc(Number.isFinite(intent.store) ? intent.store : 0), 0, 2);
  const utility = clamp(Math.trunc(Number.isFinite(intent.utility) ? intent.utility : 0), 0, 2);
  const pooja = clamp(Math.trunc(Number.isFinite(intent.pooja) ? intent.pooja : 0), 0, 2);
  const balcony = clamp(Math.trunc(Number.isFinite(intent.balcony) ? intent.balcony : 0), 0, 4);
  const preset = dwellingPresets[bedrooms];

  // Size the plate so every requested room fits without the engine having to
  // drop any: the wet band (baths + services + kitchens) and the daylight band
  // (bedrooms + study + dining + the living columns) each span the full width.
  const wetNeed = bathrooms * 2.6 + (pooja + store + utility) * 2.2 + (bedrooms > 0 ? 2.8 : 0) + kitchens * 3.2;
  const dayNeed = (bedrooms + study + dining) * 2.6 + livings * 5.2;
  const neededWidth = Math.max(preset.width, wetNeed, dayNeed);
  const neededDepth = preset.depth + (balcony ? 1.9 : 0);
  const width = clamp(Number.isFinite(intent.width) ? intent.width : neededWidth, 4, 40);
  const depth = clamp(Number.isFinite(intent.depth) ? intent.depth : neededDepth, 4, 30);
  const area = round(width * depth);
  return ensureInputShape({
    project: { id: "single-dwelling", name: "Apartment Plan", units: "m", tolerance: 0.01, seed: 1 },
    floorplate: { outer: { id: "floorplate-01", points: rectPoints(0, 0, width, depth) }, holes: [] },
    fixedElements: [],
    access: {
      entryPoints: [{ x: round(width / 2), y: 0 }],
      verticalCoreAccess: [],
      corridorStartPoints: [],
      corridorEndPoints: [],
      corridorCenterlines: []
    },
    program: {
      targetUnitTypes: [{
        type: preset.type,
        minArea: Math.min(preset.minArea, area),
        maxArea: Math.max(preset.maxArea, area),
        targetCount: 1,
        targetRatio: 1,
        weight: 1
      }],
      roomTypes: defaultRoomTypes(),
      dwelling: { bedrooms, bathrooms, kitchens, livings, study, dining, store, utility, pooja, balcony }
    },
    rules: {
      minCorridorWidth: 1.2,
      minRoomWidth: 2.0,
      minRoomDepth: 2.0,
      doorWidth: 0.9,
      wetRoomAdjacencyPreferred: true,
      requireDaylightForBedrooms: true,
      requireDaylightForLiving: true,
      minUnitArea: 16,
      gridModule: 0.6
    },
    generationSettings: {
      variantCount: 4,
      timeLimitMilliseconds: 1000,
      strictness: "balanced",
      weightedVariation: true,
      layoutMode: "single_dwelling",
      scoringWeights: defaultScoringWeights()
    }
  });
}

// ---------------------------------------------------------------------------
// AI BRIEF ASSIST — when a local Claude Code (or Codex) CLI is installed, the
// server can ask it to read the brief properly: typology words, scale hints,
// and mixes the regex parser cannot infer. Strictly an enhancement: every
// response is sanitized server-side and the heuristic parser is the fallback.
// ---------------------------------------------------------------------------
async function probeAiAssist() {
  try {
    const response = await fetch("/api/prompt/status");
    if (!response.ok) { return; }
    const status = await response.json();
    state.aiBrief.available = Boolean(status && status.available);
    state.aiBrief.provider = (status && status.provider) || "";
    state.aiBrief.providers = Array.isArray(status && status.providers) ? status.providers : [];
  } catch (error) {
    state.aiBrief.available = false;
  }
  if (!els.aiAssistRow) { return; }
  els.aiAssistRow.hidden = !state.aiBrief.available;
  if (!state.aiBrief.available) { return; }
  let saved = "on";
  try { saved = localStorage.getItem(aiAssistKey) || "on"; } catch (error) { /* private mode */ }
  if (els.aiAssistToggle) { els.aiAssistToggle.checked = saved !== "off"; }
  // Both subscriptions are first-class: when more than one CLI is installed,
  // a small picker chooses who reads the brief (persisted per browser).
  if (els.aiProviderSelect) {
    const providers = state.aiBrief.providers.length ? state.aiBrief.providers : [state.aiBrief.provider];
    els.aiProviderSelect.innerHTML = providers
      .map((p) => `<option value="${escapeHtml(p)}">${escapeHtml(providerDisplayName(p))}</option>`)
      .join("");
    let savedProvider = "";
    try { savedProvider = localStorage.getItem(aiProviderKey) || ""; } catch (error) { /* private mode */ }
    els.aiProviderSelect.value = providers.includes(savedProvider) ? savedProvider : state.aiBrief.provider;
    els.aiProviderSelect.hidden = providers.length < 2;
  }
  refreshAiAssistLabel();
}

function aiAssistEnabled() {
  return state.aiBrief.available && (!els.aiAssistToggle || els.aiAssistToggle.checked);
}

function selectedAiProvider() {
  if (els.aiProviderSelect && !els.aiProviderSelect.hidden && els.aiProviderSelect.value) {
    return els.aiProviderSelect.value;
  }
  return state.aiBrief.provider || "claude";
}

function providerDisplayName(provider) {
  return provider === "codex" ? "Codex" : "Claude";
}

function aiProviderLabel() {
  return providerDisplayName(selectedAiProvider());
}

function refreshAiAssistLabel() {
  if (els.aiAssistLabel) {
    els.aiAssistLabel.textContent = `${aiProviderLabel()} reads the brief`;
  }
}

// Asks the server-side CLI bridge to interpret the brief. Returns a sanitized
// intent object or null; the caller falls back to the heuristic parse on null.
async function requestAiIntent(text) {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), 130000);
  try {
    const response = await fetch("/api/prompt/parse", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ brief: text, provider: selectedAiProvider() }),
      signal: controller.signal
    });
    if (!response.ok) { return null; }
    const outcome = await response.json();
    if (!outcome || !outcome.ok || !outcome.intent) { return null; }
    return outcome.intent;
  } catch (error) {
    return null;
  } finally {
    window.clearTimeout(timer);
  }
}

// AI wins for every field it explicitly set; the heuristic fills the rest.
// The layout seed stays brief-derived so the same brief remains reproducible
// regardless of which parser read it.
function mergeAiIntent(base, ai) {
  const merged = { ...base };
  if (ai.dwelling === "single" || ai.dwelling === "building") {
    merged.dwelling = ai.dwelling === "single" ? "single" : undefined;
  }
  if (Number.isFinite(ai.bedrooms)) { merged.bedrooms = clamp(Math.trunc(ai.bedrooms), 0, 4); }
  if (Number.isFinite(ai.bathrooms)) { merged.bathrooms = clamp(Math.trunc(ai.bathrooms), 1, 4); }
  if (Number.isFinite(ai.kitchens)) { merged.kitchens = clamp(Math.trunc(ai.kitchens), 1, 4); }
  if (Number.isFinite(ai.livings)) { merged.livings = clamp(Math.trunc(ai.livings), 1, 3); }
  if (Number.isFinite(ai.study)) { merged.study = clamp(Math.trunc(ai.study), 0, 2); }
  if (Number.isFinite(ai.dining)) { merged.dining = clamp(Math.trunc(ai.dining), 0, 2); }
  if (Number.isFinite(ai.store)) { merged.store = clamp(Math.trunc(ai.store), 0, 2); }
  if (Number.isFinite(ai.utility)) { merged.utility = clamp(Math.trunc(ai.utility), 0, 2); }
  if (Number.isFinite(ai.pooja)) { merged.pooja = clamp(Math.trunc(ai.pooja), 0, 2); }
  if (Number.isFinite(ai.balcony)) { merged.balcony = clamp(Math.trunc(ai.balcony), 0, 4); }
  if (Number.isFinite(ai.width)) { merged.width = ai.width; }
  if (Number.isFinite(ai.depth)) { merged.depth = ai.depth; }
  if (ai.template && ["rectangular-core", "l-shaped-core", "moderately-irregular-core"].includes(ai.template)) {
    merged.template = ai.template;
  }
  if (ai.mix && typeof ai.mix === "object") {
    const mix = {};
    unitTypes.forEach((type) => {
      const value = Number(ai.mix[type]);
      if (Number.isFinite(value) && value > 0) { mix[type] = clamp(value, 0, 100); }
    });
    if (Object.keys(mix).length) { merged.mix = mix; }
  }
  if (Number.isFinite(ai.corridor)) { merged.corridor = clamp(ai.corridor, 1.2, 2.6); }
  if (Number.isFinite(ai.minUnit)) { merged.minUnit = clamp(ai.minUnit, 16, 50); }
  if (Number.isFinite(ai.variants)) { merged.variants = clamp(Math.trunc(ai.variants), 1, 20); }
  if (["strict", "balanced", "relaxed"].includes(ai.strictness)) { merged.strictness = ai.strictness; }
  if (typeof ai.daylightBedrooms === "boolean") { merged.daylightBedrooms = ai.daylightBedrooms; }
  if (typeof ai.daylightLiving === "boolean") { merged.daylightLiving = ai.daylightLiving; }
  if (Array.isArray(ai.understood) && ai.understood.length) {
    merged.understood = ai.understood.filter((label) => typeof label === "string" && label.trim()).slice(0, 8);
  }
  return merged;
}

function applyPromptToForm(intent) {
  // The floorplate is a site constraint, not a stylistic choice, so it is only
  // touched when the brief actually names dimensions; everything else resets to
  // a feasible engine default when unmentioned, making each prompt reproducible.
  const dwellingFloor = isDwellingInput(state.input) ? 4 : 8;
  if (Number.isFinite(intent.width)) { els.floorWidth.value = fieldNumber(clamp(intent.width, dwellingFloor, 200)); }
  if (Number.isFinite(intent.depth)) { els.floorDepth.value = fieldNumber(clamp(intent.depth, dwellingFloor, 120)); }
  if (Number.isFinite(intent.seed)) {
    // "shuffle"-style briefs walk the seed forward on every ask; otherwise the
    // brief's own hash keeps the layout reproducible per text.
    state.promptShuffle = intent.shuffle ? (state.promptShuffle || 0) + 1 : 0;
    els.seedInput.value = String(intent.seed + state.promptShuffle * 7919);
  }
  els.minCorridorWidth.value = fieldNumber(Number.isFinite(intent.corridor) ? intent.corridor : 1.8);
  els.minUnitArea.value = fieldNumber(Number.isFinite(intent.minUnit) ? intent.minUnit : 25);
  els.variantInput.value = String(Number.isFinite(intent.variants) ? clamp(Math.trunc(intent.variants), 1, 20) : 4);
  els.strictnessInput.value = intent.strictness || "balanced";
  els.daylightBedrooms.checked = typeof intent.daylightBedrooms === "boolean" ? intent.daylightBedrooms : true;
  els.daylightLiving.checked = typeof intent.daylightLiving === "boolean" ? intent.daylightLiving : true;
  const mix = intent.mix || { studio: 35, one_bed: 45, two_bed: 20 };
  unitTypes.forEach((type) => {
    const row = document.querySelector(`.mix-row[data-unit-type="${type}"]`);
    const field = row ? row.querySelector('[data-field="targetRatio"]') : null;
    if (field) { field.value = String(mix[type] || 0); }
  });
}

// A short label is already a project name; a long brief is a sentence to be
// generated from, so it gets condensed into a clean, drawing-ready title rather
// than being stamped verbatim into the sheet's title block.
function projectNameFromBrief(text) {
  if (!text) { return "Floor Plan Project"; }
  if (text.length <= 42 && !/[.!?]/.test(text)) { return text; }
  return deriveProjectName(parsePrompt(text), text);
}

function deriveProjectName(intent, text) {
  const parts = [];
  if (intent.mix) { parts.push(Object.keys(intent.mix).map(unitTypeLabel).join("/")); }
  if (Number.isFinite(intent.width) && Number.isFinite(intent.depth)) {
    parts.push(`${round(intent.width)}×${round(intent.depth)}m`);
  }
  if (parts.length) { return `${parts.join(" · ")} Residence`; }
  const words = String(text || "").replace(/[^\w\s-]/g, " ").split(/\s+/).filter(Boolean).slice(0, 6);
  return words.length ? words.map((w) => w.charAt(0).toUpperCase() + w.slice(1)).join(" ") : "Floor Plan Project";
}

function renderPromptUnderstood(intent, text) {
  const host = els.promptUnderstood;
  if (!host) { return; }
  host.textContent = "";
  if (!text) { host.hidden = true; return; }
  host.hidden = false;
  const chips = (intent && intent.understood) || [];
  if (!chips.length) {
    const empty = document.createElement("span");
    empty.className = "prompt-understood-empty";
    empty.textContent = "No specific constraints found — generating from the current settings.";
    host.appendChild(empty);
    return;
  }
  const lead = document.createElement("span");
  lead.className = "prompt-understood-lead";
  lead.textContent = "Understood";
  host.appendChild(lead);
  chips.forEach((label) => {
    const chip = document.createElement("span");
    chip.className = "prompt-chip";
    chip.textContent = label;
    host.appendChild(chip);
  });
}

function appendPromptNote(message) {
  const host = els.promptUnderstood;
  if (!host) { return; }
  host.hidden = false;
  const node = document.createElement("span");
  node.className = "prompt-chip prompt-chip-note";
  node.textContent = message;
  host.appendChild(node);
}

// Safety net: if a brief turns out to be over-constrained for its plate and no
// variant validates, step strictness one notch looser and regenerate once, then
// say so plainly. A designer relaxes the brief to fit the site; so do we — but
// only one step, never silently, and never in a loop.
async function relaxStrictnessIfNoVariants() {
  const order = ["strict", "balanced", "relaxed"];
  const current = state.response;
  // Only relax when variants were actually generated but every one failed
  // validation (a rules-too-tight signal). A zero-variant result means the plate
  // geometry itself is infeasible, which looser validation cannot rescue.
  if (!current || (current.validVariantCount || 0) > 0 || (current.variantCount || 0) === 0) { return; }
  const next = order[order.indexOf(els.strictnessInput.value || "balanced") + 1];
  if (!next) { return; }
  els.strictnessInput.value = next;
  syncInputFromForm();
  setEditorFromInput(state.input);
  saveDraft();
  await runEngine(false);
  if (state.response && (state.response.validVariantCount || 0) > 0) {
    appendPromptNote(`Relaxed to ${next} rules to fit this plate`);
  }
}

async function generateFromPrompt() {
  const text = els.projectName.value.trim();
  let intent = parsePrompt(text);
  let aiUsed = false;
  let aiTried = false;
  const defaultButtonLabel = els.promptGenerateBtn ? els.promptGenerateBtn.textContent : "";
  // The prompt flow owns the run pipeline for its whole async span: cancel any
  // pending auto-run and queued mode so nothing stale renders over its result.
  clearAutoGenerate();
  state.pendingRunMode = "";
  if (els.promptGenerateBtn) { els.promptGenerateBtn.disabled = true; }
  try {
    if (text && aiAssistEnabled()) {
      aiTried = true;
      setStatus(`Reading the brief with ${aiProviderLabel()}…`);
      if (els.promptGenerateBtn) {
        els.promptGenerateBtn.textContent = `Reading brief with ${aiProviderLabel()}…`;
      }
      const aiIntent = await requestAiIntent(text);
      if (aiIntent) {
        intent = mergeAiIntent(intent, aiIntent);
        aiUsed = true;
      }
    }
    if (els.promptGenerateBtn) { els.promptGenerateBtn.textContent = "Generating plan…"; }
    // A run that slipped in before this flow started must fully drain first,
    // so the prompt's run is the one that renders last and relax/navigation
    // read the prompt's response rather than a stale one.
    clearAutoGenerate();
    state.pendingRunMode = "";
    await awaitEngineIdle(10000);
    // Generating a fresh plan from a prompt starts a new edit history.
    state.undoStack = [];
    state.redoStack = [];
    state.geometryEdits = {};
    // A brief about one apartment generates a dwelling from scratch; a brief
    // that names a plate shape starts from that sample's geometry instead. A
    // building brief arriving while the current document is a dwelling must
    // reset to a building baseline — layout mode never sticks across briefs.
    if (intent.dwelling === "single") {
      const briefText = text;
      setInput(buildSingleDwellingInput(intent));
      els.projectName.value = briefText;
      state.brief = briefText;
    } else {
      const wantsTemplate = intent.template && state.samples.some((sample) => sample.name === intent.template);
      if (wantsTemplate || isDwellingInput(state.input)) {
        if (wantsTemplate && els.sampleSelect.value !== intent.template) {
          els.sampleSelect.value = intent.template;
        }
        if (!els.sampleSelect.value && state.samples.length) {
          els.sampleSelect.value = state.samples[0].name;
        }
        const briefText = text;
        await loadSelectedSample(false);
        els.projectName.value = briefText;
      }
    }
    applyPromptToForm(intent);
    syncInputFromForm();
    setEditorFromInput(state.input);
    saveDraft();
    renderPromptUnderstood(intent, text);
    if (aiUsed) {
      appendPromptNote(`Brief interpreted by ${aiProviderLabel()}`);
    } else if (aiTried) {
      appendPromptNote(`${aiProviderLabel()} was unavailable — used the built-in parser`);
    }
    if (!(intent.understood || []).length && text) {
      appendPromptNote("Refreshed the layout seed — every brief explores a new scheme");
    }
    await runEngine(false);
    await relaxStrictnessIfNoVariants();
    navigateToHash("#plan");
  } finally {
    if (els.promptGenerateBtn) {
      els.promptGenerateBtn.disabled = false;
      els.promptGenerateBtn.textContent = defaultButtonLabel || "Generate from prompt";
    }
  }
}

async function openInputFile() {
  const file = els.inputFile.files && els.inputFile.files[0];
  if (!file) {
    return;
  }

  await loadInputFile(file);
  els.inputFile.value = "";
}

async function openDroppedInputFile(event) {
  event.preventDefault();
  els.inputEditor.classList.remove("dragging");
  const file = event.dataTransfer && event.dataTransfer.files && event.dataTransfer.files[0];
  if (!file) {
    return;
  }

  await loadInputFile(file);
}

async function loadInputFile(file) {
  try {
    const text = await file.text();
    const parsed = JSON.parse(text);
    setInput(parsed);
    saveDraft();
    setStatus(`Opened ${file.name}`);
    await runEngine(false);
  } catch (error) {
    setStatus("Input file could not be opened");
    renderError("input_file_invalid", error.message);
  }
}

function applyJsonFromEditor() {
  try {
    const parsed = JSON.parse(els.inputEditor.value);
    setInput(parsed, { preserveResponse: true });
    saveDraft();
    markInputDirty("Updating plan from JSON", 150);
    renderAll();
  } catch (error) {
    setStatus("Input JSON is invalid");
    renderError("invalid_json", error.message);
  }
}

function markInputDirty(message, autoGenerateDelay) {
  state.inputRevision += 1;
  state.runSerial += 1;
  state.inputDirty = true;
  if (state.busy) {
    state.pendingRunMode = "generate";
  }
  setStatus(message || "Plan edits pending");
  if (autoGenerateDelay !== null && autoGenerateDelay !== undefined && Number.isFinite(Number(autoGenerateDelay))) {
    scheduleAutoGenerate(autoGenerateDelay);
  }
}

function scheduleAutoGenerate(delay) {
  clearAutoGenerate();
  state.autoGenerateTimer = window.setTimeout(() => {
    state.autoGenerateTimer = 0;
    runEngine(false);
  }, Math.max(100, Number(delay) || 650));
}

function clearAutoGenerate() {
  if (state.autoGenerateTimer) {
    window.clearTimeout(state.autoGenerateTimer);
    state.autoGenerateTimer = 0;
  }
}

// Waits for any in-flight engine run to drain so a caller's own run starts
// immediately instead of being queued behind a stale response (runEngine
// returns instantly when busy, which would let callers read the wrong
// state.response right after awaiting it).
async function awaitEngineIdle(maxMs) {
  const deadline = Date.now() + (Number(maxMs) || 8000);
  while (state.busy && Date.now() < deadline) {
    await new Promise((resolve) => window.setTimeout(resolve, 80));
  }
}

async function runEngine(validateOnly) {
  if (state.busy) {
    state.pendingRunMode = state.pendingRunMode === "generate" || !validateOnly ? "generate" : "validate";
    setStatus(validateOnly ? "Queued validation" : "Queued regeneration");
    return;
  }

  clearAutoGenerate();
  try {
    syncInputFromForm();
    syncFormFromInput(state.input);
    setEditorFromInput(state.input);
    saveDraft();
  } catch (error) {
    renderError("setup_invalid", error.message);
    return;
  }

  const variants = state.input.generationSettings.variantCount;
  const seed = state.input.project.seed;
  const requestInputRevision = state.inputRevision;
  const request = {
    input: clone(state.input),
    validateOnly,
    variants,
    seed
  };
  const runId = ++state.runSerial;
  state.busyRunId = runId;

  setBusy(true, validateOnly ? "Checking" : "Generating");
  try {
    const response = await fetchJson("/api/generate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
    if (runId !== state.runSerial || requestInputRevision !== state.inputRevision) {
      state.previewStale = Boolean(state.lastPreviewResponse);
      state.pendingRunMode = state.pendingRunMode || "generate";
      renderAll();
      return;
    }
    const hasPreview = hasGeneratedVariant(response.output);
    state.response = response;
    if (hasPreview) {
      // Identical input (same floorplate, rules, seed) regenerates identical
      // variants, so manual room/wall edits keyed to those variant ids stay
      // valid — even across reloads or duplicate boot runs. Any real input
      // change produces a new layout and retires the old edits with it.
      const signature = JSON.stringify(request.input);
      state.lastRunInputSignature = signature;
      if (hasGeometryOverrides() && state.editsSignature !== signature) {
        state.geometryEdits = {};
      }
      state.lastPreviewResponse = response;
      state.selectedVariantId = response.bestVariantId || firstVariantId(response.output);
    } else if (!state.lastPreviewResponse) {
      state.selectedVariantId = "";
    }
    state.previewStale = !hasPreview && Boolean(state.lastPreviewResponse);
    state.inputDirty = false;
    setStatus(`${friendlyStatus(response.status)} · ${response.validVariantCount}/${response.variantCount} valid variants`);
    renderAll();
  } catch (error) {
    if (runId === state.runSerial) {
      renderError("request_failed", error.message);
      setStatus("Request failed");
    }
  } finally {
    if (state.busyRunId === runId) {
      setBusy(false);
    }
    if (!state.busy && state.pendingRunMode) {
      const nextMode = state.pendingRunMode;
      state.pendingRunMode = "";
      runEngine(nextMode === "validate");
    }
  }
}

function renderAll() {
  const output = state.response ? state.response.output : null;
  const visualOutput = currentVisualOutput();
  syncSelection(visualOutput);
  renderSetupGuide(output);
  renderVariantSelect(visualOutput);
  renderPreview(visualOutput);
  renderPlanTitleBlock(visualOutput);
  renderMetrics(visualOutput);
  renderVariants(visualOutput);
  renderDiagnostics(output);
  renderExportSummary(output);
  renderHypergraphPreview(visualOutput);
  renderSchedule(visualOutput);
  renderValidation(output);
  renderSelectionInspector(visualOutput);
  renderRoomReviewSchedule(visualOutput);
  renderSubtitles(output, visualOutput);
  updateDirtyState();
  updateCanvasUi(visualOutput);

  els.outputJson.textContent = output ? JSON.stringify(output, null, 2) : "";
  els.cliCommand.textContent = buildCliCommand();
  updateExportActions(output);
}

function currentVisualOutput() {
  const output = state.response ? state.response.output : null;
  return previewOutput(output);
}

// The empty canvas explains itself. A failed run is a dead end only if the
// app leaves the user staring at an outline — so it names the problem and
// offers the way back (reset to a known-good template, read the notes).
function renderEmptyPreviewContent() {
  const output = state.response ? state.response.output : null;
  const failedRun = Boolean(output && !hasGeneratedVariant(output));
  if (!failedRun) {
    els.emptyPreview.textContent = "Input outline";
    return;
  }
  const firstIssue = collectDiagnostics(output)
    .find((diagnostic) => /error|fail/i.test(String(diagnostic.severity || "")));
  const reason = firstIssue
    ? friendlyDiagnosticMessage(firstIssue)
    : "The engine could not fit any valid unit layout inside these constraints.";
  els.emptyPreview.innerHTML = `
    <div class="empty-preview-card">
      <strong>No valid variants from this input</strong>
      <p>${escapeHtml(firstLine(reason))}</p>
      <div class="empty-preview-actions">
        <button type="button" data-empty-action="reset">Reset to template</button>
        <button type="button" data-empty-action="notes">Open review notes</button>
      </div>
    </div>
  `;
}

function handleEmptyPreviewAction(event) {
  const button = event.target.closest ? event.target.closest("[data-empty-action]") : null;
  if (!button) {
    return;
  }
  if (button.dataset.emptyAction === "reset") {
    // Recover to a template that is known to generate — re-loading the very
    // input that just failed would only reproduce the dead end.
    const fallback = state.samples.find((sample) => sample.name === "rectangular-core");
    if (fallback && els.sampleSelect.value !== fallback.name) {
      els.sampleSelect.value = fallback.name;
    }
    loadSelectedSample(true);
    return;
  }
  if (button.dataset.emptyAction === "notes") {
    const list = els.diagnosticList;
    if (list && typeof list.scrollIntoView === "function") {
      list.scrollIntoView({ behavior: "smooth", block: "center" });
    }
    setStatus("Review notes");
  }
}

function previewOutput(output) {
  if (hasGeneratedVariant(output)) {
    return output;
  }

  return state.lastPreviewResponse ? state.lastPreviewResponse.output : output;
}

function hasGeneratedVariant(output) {
  const variants = output && Array.isArray(output.variants) ? output.variants : [];
  return variants.some(hasUsableVariantGeometry);
}

function hasUsableVariantGeometry(variant) {
  return Boolean(
    variant &&
    (variant.units || []).length > 0 &&
    (variant.rooms || []).length > 0
  );
}

function updateExportActions(output) {
  const exportReady = Boolean(output && !state.inputDirty && !state.busy);
  const hasVariant = Boolean(selectedVariant(output));
  const hasPreview = Boolean(els.planSvg.childElementCount);
  const staleGeneratedPreview = Boolean(state.previewStale);
  const rawOutputActions = new Set([
    "download-json",
    "copy-json",
    "save-svg"
  ]);
  const variantRequiredActions = new Set([
    "copy-rhino",
    "copy-ifc",
    "copy-hypergraph",
    "download-hypergraph"
  ]);
  document.querySelectorAll("[data-export-action]").forEach((button) => {
    const action = button.getAttribute("data-export-action");
    if (action === "copy-cli" || action === "copy-api") {
      button.disabled = state.busy;
      return;
    }

    if (rawOutputActions.has(action)) {
      const needsCurrentPreview = action === "save-svg";
      button.disabled = !exportReady || (needsCurrentPreview && (!hasPreview || staleGeneratedPreview));
      return;
    }

    if (variantRequiredActions.has(action)) {
      button.disabled = !exportReady || !hasVariant;
    }
  });
  els.saveSvgBtn.disabled = !exportReady || !hasPreview || staleGeneratedPreview || state.viewMode === "model3d";
}

// The setup panel is one honest scrolling form (the step-wizard chrome was
// retired — it only re-highlighted buttons without changing the panel). This
// keeps the primary action state and the Run Review card current.
function renderSetupGuide(output) {
  if (els.setupGenerateBtn) {
    els.setupGenerateBtn.disabled = state.busy;
  }
  els.setupReview.innerHTML = buildSetupReview(output);
}

function buildSetupReview(output) {
  const input = state.input ? ensureInputShape(state.input) : null;
  if (!input) {
    return `<div class="empty-list">Load a template or enter project basics to prepare generation.</div>`;
  }

  const floorBounds = input.floorplate && input.floorplate.outer ? boundsOfPoints(input.floorplate.outer.points) : null;
  const core = firstCore(input);
  const coreBounds = core && core.polygon ? boundsOfPoints(core.polygon.points) : null;
  const rules = input.rules || {};
  const targets = input.program && Array.isArray(input.program.targetUnitTypes) ? input.program.targetUnitTypes : [];
  const mix = targets
    .filter((target) => Number(target.targetRatio) > 0)
    .map((target) => `${shortUnitType(target.type)} ${Math.round(Number(target.targetRatio) * 100)}%`)
    .join(", ");
  const variant = selectedVariant(output);
  const checks = variant && variant.validation && Array.isArray(variant.validation.checks) ? variant.validation.checks : [];
  const failedChecks = checks.filter((check) => !check.passed);
  const reviewText = failedChecks.length
    ? `${failedChecks.length} review item${failedChecks.length === 1 ? "" : "s"}`
    : "Inputs are ready for a ranked variant run";
  const readiness = state.inputDirty
    ? "Edits pending"
    : output && variant
      ? `${friendlyStatus(output.status)} · ${variant.units.length} ${variant.units.length === 1 ? "unit" : "units"}`
      : "Ready to generate";

  const rows = [
    ["Project", input.project ? input.project.name : "Floor Plan Project"],
    [
      "Floorplate",
      floorBounds
        ? `${formatNumber(floorBounds.width, 1)} × ${formatNumber(floorBounds.height, 1)} m`
        : "Not set"
    ],
    [
      "Core",
      coreBounds
        ? `${formatNumber(coreBounds.width, 1)} × ${formatNumber(coreBounds.height, 1)} m at ` +
          `${formatNumber(coreBounds.minX, 1)}, ${formatNumber(coreBounds.minY, 1)}`
        : "No core"
    ],
    ["Rules", `Corridor ${formatNumber(rules.minCorridorWidth, 1)} m, min unit ${formatNumber(rules.minUnitArea, 0)} m²`],
    ["Unit mix", mix || "No target mix"],
    ["Readiness", readiness]
  ];

  return `
    <div class="setup-review-status ${failedChecks.length ? "warning" : "good"}">
      <strong>${escapeHtml(readiness)}</strong>
      <span>${reviewText}</span>
    </div>
    <div class="setup-review-grid">
      ${rows.map(([label, value]) => `
        <div>
          <span>${escapeHtml(label)}</span>
          <strong>${escapeHtml(value)}</strong>
        </div>
      `).join("")}
    </div>
  `;
}

function updateDirtyState() {
  const stalePreview = Boolean((state.inputDirty && state.response) || state.previewStale);
  els.previewFrame.classList.toggle("is-stale", stalePreview);
  els.previewFrame.classList.toggle("is-dragging", Boolean(state.dragEdit));
  els.previewFrame.classList.toggle("is-edit-mode", Boolean(state.editMode && state.viewMode !== "axon"));
  els.planSvg.classList.toggle("stale-preview", stalePreview);

  if (!state.inputDirty && !state.previewStale) {
    return;
  }

  if (state.inputDirty && state.response) {
    els.planSubtitle.textContent = `${els.planSubtitle.textContent} · regenerating from edits`;
    els.resultSubtitle.textContent = "Edits pending - last generated plan stays visible until the engine refreshes it";
  } else if (state.previewStale) {
    els.planSubtitle.textContent = `${els.planSubtitle.textContent} · previous generated plan shown`;
    els.resultSubtitle.textContent = `${els.resultSubtitle.textContent} · previous plan kept visible`;
  } else {
    els.resultSubtitle.textContent = "Edits pending - generate to produce variants";
  }
}

function renderSubtitles(output, visualOutput = output) {
  const projectName = state.input && state.input.project ? state.input.project.name : "Project";
  const floorBounds = state.input && state.input.floorplate && state.input.floorplate.outer
    ? boundsOfPoints(state.input.floorplate.outer.points)
    : null;
  const targets = state.input && state.input.program && Array.isArray(state.input.program.targetUnitTypes)
    ? state.input.program.targetUnitTypes
    : [];
  const targetMix = targets
    .filter((target) => Number(target.targetRatio) > 0)
    .map((target) => `${shortUnitType(target.type)} ${Math.round(Number(target.targetRatio) * 100)}%`)
    .join(", ");
  els.setupSubtitle.textContent = state.input && state.input.project
    ? (state.input.project.name || titleCase(state.input.project.id || "project"))
    : "Project";

  if (!output) {
    els.resultSubtitle.textContent = `${projectName} ready for generation`;
    els.planSubtitle.textContent = floorBounds
      ? `Input outline ${formatNumber(floorBounds.width, 1)} × ${formatNumber(floorBounds.height, 1)} m`
      : "Input outline";
    els.scheduleSubtitle.textContent = targetMix ? `Target mix: ${targetMix}` : "Generate to populate schedule";
    return;
  }

  const variant = selectedVariant(visualOutput);
  const valid = output.variants ? output.variants.filter((v) => v.validation && v.validation.passed).length : 0;
  const summary = diagnosticSummary(output);
  const diagnosticText = diagnosticSubtitleText(summary);
  const totalVariants = output.variants ? output.variants.length : 0;
  const resultStatus = `${friendlyStatus(output.status)} · ${valid}/${totalVariants} valid`;
  els.resultSubtitle.textContent = diagnosticText
    ? `${resultStatus}, ${diagnosticText}`
    : resultStatus;
  if (!variant) {
    els.planSubtitle.textContent = output.status === "validated" ? "Input passed validation. Generate variants when ready." : "No generated variant";
    els.scheduleSubtitle.textContent = targetMix ? `Target mix: ${targetMix}` : "No generated units";
    return;
  }

  const metrics = variant.metrics || {};
  const mix = unitMixSummary(variant.units || []);
  els.planSubtitle.textContent = `${variant.variantId} · score ${formatNumber(metrics.score, 3)} · net/gross ${formatNumber(metrics.netGrossRatio, 3)}`;
  const unitSummary = `${variant.units.length} units${mix ? ` (${mix})` : ""}`;
  els.scheduleSubtitle.textContent = `${unitSummary}, ${variant.rooms.length} rooms, ${variant.doorsOpenings.length} doors`;
}

function renderVariantSelect(output) {
  const variants = output && Array.isArray(output.variants) ? output.variants : [];
  if (variants.length === 0) {
    els.variantSelect.innerHTML = '<option value="">No variants</option>';
    els.variantSelect.disabled = true;
    return;
  }

  els.variantSelect.disabled = false;
  els.variantSelect.innerHTML = variants
    .map((variant, index) => {
      const metrics = variant.metrics || {};
      const status = friendlyStatus(variant.status);
      const score = Number.isFinite(Number(metrics.score)) ? ` · ${formatNumber(metrics.score, 3)}` : "";
      return `<option value="${escapeHtml(variant.variantId)}">#${index + 1} ${escapeHtml(variant.variantId)} · ${escapeHtml(status)}${score}</option>`;
    })
    .join("");
  els.variantSelect.value = state.selectedVariantId || firstVariantId(output);
}

function renderPreview(output) {
  clearSvg();
  renderSvgDefs();
  updateModeButtons();
  const variant = selectedVariant(output);
  const input = state.input;
  const metadata = output ? output.metadata : null;
  const bounds = metadata && metadata.floorplate ? metadata.floorplate.bounds : collectBounds(output, variant, input);

  els.previewFrame.dataset.viewMode = state.viewMode;
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
    els.planSvg.removeAttribute("viewBox");
    els.planSvg.dataset.viewMode = state.viewMode;
    hideModelViewport();
    els.emptyPreview.hidden = false;
    els.emptyPreview.textContent = "No plan available";
    renderLegend(false);
    return;
  }

  els.emptyPreview.hidden = Boolean(variant);
  renderEmptyPreviewContent();
  els.planSvg.dataset.viewMode = state.viewMode;

  // The 3D model tab swaps the SVG sheet for a WebGL viewport: the scene is
  // computed here as data and handed to the lazily loaded three.js module.
  if (state.viewMode === "model3d") {
    renderModelView(variant, input, bounds);
    renderLegend(Boolean(variant));
    return;
  }
  hideModelViewport();

  // The 3D axon is a real projected scene, not a sheared plan: it owns its
  // geometry, depth sorting and view box, so it branches off here entirely.
  if (state.viewMode === "axon") {
    renderAxonView(variant, input, bounds);
    renderLegend(Boolean(variant));
    return;
  }

  const viewBox = previewViewBox(bounds, state.zoom);
  els.planSvg.setAttribute("viewBox", viewBox);
  els.planSvg.setAttribute("preserveAspectRatio", "xMidYMid meet");

  if (state.viewMode !== "axon") {
    renderDimensionGuides(bounds, metadata && metadata.projectUnits);
  }
  if (state.viewMode === "plan") {
    renderNorthArrow(bounds);
  }

  const group = svgEl("g", { transform: previewTransform() });
  els.planSvg.appendChild(group);

  // The drawing sits on a white sheet floating over the studio surface — the
  // presentation frame of a printed plan, and the contrast base every other
  // ink weight is tuned against. Its soft shadow is three stacked low-alpha
  // rects: blur filters at sheet scale freeze the SVG rasterizer.
  if (state.viewMode !== "axon") {
    const sheetMargin = clamp(Math.max(bounds.width, bounds.height) * 0.055, 1.4, 3.2);
    const sheet = {
      x: bounds.minX - sheetMargin,
      y: bounds.minY - sheetMargin,
      w: bounds.width + sheetMargin * 2,
      h: bounds.height + sheetMargin * 2
    };
    [0.34, 0.2, 0.09].forEach((spread) => {
      group.appendChild(svgEl("rect", {
        class: "plan-sheet-shadow",
        x: round(sheet.x - spread),
        y: round(sheet.y - spread - 0.1),
        width: round(sheet.w + spread * 2),
        height: round(sheet.h + spread * 2),
        rx: round(0.2 + spread),
        ry: round(0.2 + spread)
      }));
    });
    group.appendChild(svgEl("rect", {
      class: "plan-sheet",
      x: round(sheet.x),
      y: round(sheet.y),
      width: round(sheet.w),
      height: round(sheet.h),
      rx: 0.18,
      ry: 0.18
    }));
  }
  renderPlanGrid(group, bounds);

  if (input && input.floorplate && input.floorplate.outer) {
    group.appendChild(polygonEl(input.floorplate.outer.points, selectableClass("boundary", "floorplate", "floorplate"), {
      ...selectableAttributes("floorplate", "floorplate"),
      "data-edit-target": "floorplate"
    }));
  }
  if (input && Array.isArray(input.fixedElements)) {
    input.fixedElements.forEach((fixed) => {
      if (fixed.polygon && fixed.polygon.points) {
        const kind = String(fixed.type || "").toLowerCase() === "core" ? "core" : "fixed";
        const attributes = {
          ...selectableAttributes(kind, fixed.id || "fixed"),
          "data-edit-target": fixed.id || "fixed"
        };
        group.appendChild(polygonEl(fixed.polygon.points, selectableClass("fixed", kind, fixed.id || "fixed"), attributes));
        // Name the solid block so it reads as the service core, not a void.
        // Text lives on the un-flipped SVG root (like room labels) with a
        // negated y, and paints above the walls by document order.
        const coreBounds = kind === "core" ? boundsOfPoints(fixed.polygon.points) : null;
        if (coreBounds && coreBounds.width >= 2 && coreBounds.height >= 1.2 && state.viewMode === "plan") {
          const coreFont = clamp(Math.min(coreBounds.width, coreBounds.height) * 0.16, 0.3, 0.55);
          const coreLabel = svgEl("text", {
            class: "core-label",
            x: round(coreBounds.minX + coreBounds.width / 2),
            y: round(-(coreBounds.minY + coreBounds.height / 2) + coreFont * 0.34),
            "text-anchor": "middle",
            "font-size": formatNumber(coreFont, 2)
          });
          coreLabel.textContent = "CORE";
          els.planSvg.appendChild(coreLabel);
        }
      }
    });
  }
  renderInputAccessMarkers(group, input, bounds);

  if (variant) {
    (variant.units || []).forEach((unit) => {
      const className = selectableClass(`unit unit-${unit.type || "standard"}`, "unit", unit.id);
      group.appendChild(polygonEl(unit.polygon.points, className, selectableAttributes("unit", unit.id)));
    });
    (variant.rooms || []).forEach((room) => {
      const className = selectableClass(`room ${roomCategoryClass(room)}`, "room", room.id);
      group.appendChild(polygonEl(room.polygon.points, className, selectableAttributes("room", room.id)));
    });
    (variant.corridors || []).forEach((corridor) => {
      const className = selectableClass("corridor", "corridor", corridor.id);
      const attributes = selectableAttributes("corridor", corridor.id);
      group.appendChild(polygonEl(corridor.polygon.points, className, attributes));
    });
    renderPlanGlyphLayer(group, variant, bounds);
    const wallById = new Map((variant.walls || []).map((wall) => [wall.id, wall]));
    (variant.walls || []).forEach((wall) => {
      renderWallSegment(group, wall);
    });
    (variant.doorsOpenings || []).forEach((door) => {
      renderDoorOpening(group, door, wallById.get(door.hostWall), bounds);
    });
    renderWindowLayer(group, variant, bounds);
    if (state.viewMode === "circulation") {
      renderCirculationOverlay(group, variant, bounds);
    }
    renderSelectedRoomHalo(group, variant);

    const unitById = new Map((variant.units || []).map((unit) => [unit.id, unit]));
    const labels = variant.labels || [];
    if (state.viewMode === "circulation") {
      labels
        .filter((label) => label.targetId && unitById.has(label.targetId))
        .forEach((label) => {
          const unit = unitById.get(label.targetId);
          const text = svgEl("text", {
            class: "svg-label unit-label",
            x: label.location.x,
            y: -label.location.y,
            "text-anchor": "middle",
            "font-size": "0.58"
          });
          text.textContent = shortUnitType(unit.type);
          els.planSvg.appendChild(text);
        });
    }
    renderRoomLabels(variant);
  }

  renderInputEditHandles(group, input);
  renderSelectionConstraintHandles(group, output);
  renderDragReadout(bounds);
  renderLegend(Boolean(variant));
}

// ---------------------------------------------------------------------------
// CIRCULATION — a movement diagram derived from the model's door network:
// every door connects two spaces, so the flow lines run space → door → space.
// Buildings read as a corridor spine with arrowed travel directions and unit
// spurs; dwellings read as the entry arrow plus the room-to-room door graph.
// ---------------------------------------------------------------------------
function nearestPointOnSegment(point, start, end) {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const lengthSq = dx * dx + dy * dy;
  if (lengthSq < 1e-12) {
    return { x: start.x, y: start.y };
  }
  const t = clamp(((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSq, 0, 1);
  return { x: start.x + dx * t, y: start.y + dy * t };
}

function circulationAnchor(spaceId, door, spaces) {
  const space = spaces.get(String(spaceId || ""));
  if (!space) {
    return null;
  }
  if (space.kind === "corridor" && space.centerline) {
    return nearestPointOnSegment(door.location, space.centerline.start, space.centerline.end);
  }
  return space.center;
}

function circulationFlowPath(group, points, className) {
  const attr = points
    .filter(Boolean)
    .map((p) => `${formatNumber(p.x, 3)},${formatNumber(p.y, 3)}`)
    .join(" ");
  group.appendChild(svgEl("polyline", {
    class: className,
    points: attr,
    "marker-end": "url(#circ-arrow)"
  }));
}

function renderCirculationOverlay(group, variant, bounds) {
  const overlay = svgEl("g", { class: "circulation-overlay", "aria-hidden": "true" });
  const spaces = new Map();
  const register = (items, kind) => (items || []).forEach((item) => {
    const itemBounds = item.polygon ? boundsOfPoints(item.polygon.points) : null;
    if (!itemBounds) {
      return;
    }
    spaces.set(String(item.id || ""), {
      kind,
      center: { x: itemBounds.minX + itemBounds.width / 2, y: itemBounds.minY + itemBounds.height / 2 },
      centerline: kind === "corridor" && item.centerline ? item.centerline : null
    });
  });
  register(variant.units, "unit");
  register(variant.rooms, "room");
  register(variant.corridors, "corridor");

  // The corridor spine: dashed centerline with travel arrows both ways.
  (variant.corridors || []).forEach((corridor) => {
    const line = corridor.centerline;
    if (!line || !line.start || !line.end) {
      return;
    }
    overlay.appendChild(svgEl("line", {
      class: "circ-spine",
      x1: round(line.start.x), y1: round(line.start.y),
      x2: round(line.end.x), y2: round(line.end.y)
    }));
    const inset = 0.18;
    const stepX = Math.sign(line.end.x - line.start.x) * inset;
    const stepY = Math.sign(line.end.y - line.start.y) * inset;
    const mid = { x: (line.start.x + line.end.x) / 2, y: (line.start.y + line.end.y) / 2 };
    const toEnd = { x: line.end.x - stepX, y: line.end.y - stepY };
    const toStart = { x: line.start.x + stepX, y: line.start.y + stepY };
    circulationFlowPath(overlay, [mid, toEnd], "circ-flow circ-spine-arrow");
    circulationFlowPath(overlay, [mid, toStart], "circ-flow circ-spine-arrow");
  });

  const plateCenter = bounds
    ? { x: bounds.minX + bounds.width / 2, y: bounds.minY + bounds.height / 2 }
    : null;
  (variant.doorsOpenings || []).forEach((door) => {
    if (!door || !door.location) {
      return;
    }
    const connects = door.connectsSpaces || [];
    const isEntry = String(door.hostWall || "").indexOf("wall-entry-") === 0;
    const dwellingEntry = isEntry && connects.some((id) => spaces.get(String(id)) && spaces.get(String(id)).kind === "unit")
      && (variant.corridors || []).length === 0;

    if (dwellingEntry && plateCenter) {
      // The way in: an arrow from outside the facade through the entry door
      // to the hub room the door opens into.
      const hubId = connects.map(String).find((id) => spaces.get(id) && spaces.get(id).kind === "room");
      const hub = hubId ? spaces.get(hubId) : null;
      const dx = door.location.x - plateCenter.x;
      const dy = door.location.y - plateCenter.y;
      const horizontal = Math.abs(dx) >= Math.abs(dy);
      const outside = {
        x: door.location.x + (horizontal ? Math.sign(dx || 1) * 1.4 : 0),
        y: door.location.y + (horizontal ? 0 : Math.sign(dy || 1) * 1.4)
      };
      circulationFlowPath(overlay, [outside, door.location, hub ? hub.center : null], "circ-flow circ-entry-flow");
    } else if (connects.length >= 2) {
      const from = circulationAnchor(connects[0], door, spaces);
      const to = circulationAnchor(connects[1], door, spaces);
      if (from && to) {
        const corridorFirst = spaces.get(String(connects[0])) && spaces.get(String(connects[0])).kind === "corridor";
        const points = corridorFirst ? [from, door.location, to] : [to, door.location, from];
        circulationFlowPath(overlay, points, "circ-flow");
      }
    }

    overlay.appendChild(svgEl("circle", {
      class: isEntry ? "circ-door-dot circ-entry-dot" : "circ-door-dot",
      cx: round(door.location.x),
      cy: round(door.location.y),
      r: isEntry ? 0.24 : 0.15
    }));
  });

  group.appendChild(overlay);
}

// Live dimension badge that follows the cursor during a resize/move drag, so the
// editor reads its result in place (like CAD/Figma) instead of only in the sidebar.
// Purely presentational — it reads the already-edited input, never mutates geometry.
function renderDragReadout(bounds) {
  const edit = state.dragEdit;
  if (!edit || !edit.lastPoint || state.viewMode !== "plan" || !bounds) {
    return;
  }

  const text = dragReadoutText(edit, state.input);
  if (!text) {
    return;
  }

  const maxDim = Math.max(bounds.width, bounds.height);
  const font = clamp(maxDim * 0.022, 0.46, 1.05);
  const padX = font * 0.55;
  const padY = font * 0.4;
  const boxWidth = text.length * font * 0.6 + padX * 2;
  const boxHeight = font + padY * 2;
  const anchorX = edit.lastPoint.x + font * 1.1;
  // planSvg space is y-down (labels are appended here, not into the flipped group),
  // so negate the model y and lift the badge above the cursor.
  const topY = -edit.lastPoint.y - boxHeight - font * 0.5;
  const badge = svgEl("g", { class: "drag-readout", "aria-hidden": "true" });
  badge.appendChild(svgEl("rect", {
    class: "drag-readout-bg",
    x: round(anchorX),
    y: round(topY),
    width: round(boxWidth),
    height: round(boxHeight),
    rx: round(font * 0.35),
    ry: round(font * 0.35)
  }));
  const label = svgEl("text", {
    class: "drag-readout-text",
    x: round(anchorX + padX),
    y: round(topY + padY + font * 0.82),
    "font-size": formatNumber(font, 2)
  });
  label.textContent = text;
  badge.appendChild(label);
  els.planSvg.appendChild(badge);
}

function dragReadoutText(edit, input) {
  if (!edit || !input) {
    return "";
  }

  const floorBounds = input.floorplate && input.floorplate.outer
    ? boundsOfPoints(input.floorplate.outer.points)
    : null;
  const core = firstCore(input);
  const coreBounds = core && core.polygon ? boundsOfPoints(core.polygon.points) : null;
  switch (edit.action) {
    case "floor-width":
      return floorBounds ? `${formatNumber(floorBounds.width, 1)} m wide` : "";
    case "floor-depth":
      return floorBounds ? `${formatNumber(floorBounds.height, 1)} m deep` : "";
    case "floor-size":
      return floorBounds
        ? `${formatNumber(floorBounds.width, 1)} × ${formatNumber(floorBounds.height, 1)} m`
        : "";
    case "core-size":
      return coreBounds
        ? `Core ${formatNumber(coreBounds.width, 1)} × ${formatNumber(coreBounds.height, 1)} m`
        : "";
    case "core-move":
      return coreBounds
        ? `Core @ ${formatNumber(coreBounds.minX, 1)}, ${formatNumber(coreBounds.minY, 1)} m`
        : "";
    default:
      return state.editReadout || "";
  }
}

function previewViewBox(bounds, zoom) {
  const maxDim = Math.max(bounds.width, bounds.height);
  // Reserve a margin big enough for the dimension strings + scale bar drawn by
  // renderDimensionGuides so they live inside the viewBox instead of being
  // clipped off into the letterbox gutters.
  const offset = Math.max(maxDim * 0.075, 1.6);
  const labelGap = Math.max(maxDim * 0.024, 0.58);
  const margin = offset + labelGap + 0.85;
  // SVG space is Y-flipped by the group transform, so the plan spans
  // [-maxY, -minY]; pad symmetrically around that.
  let minX = bounds.minX - margin;
  let minY = -bounds.maxY - margin;
  let width = bounds.width + margin * 2;
  let height = bounds.height + margin * 2;

  // Expand the short axis so the viewBox aspect matches the canvas element.
  // With preserveAspectRatio "meet" the drawing then fills the frame edge to
  // edge and stays centred, removing the dead band above and below the plan.
  const frameAspect = previewFrameAspect();
  const contentAspect = height > 0 ? width / height : frameAspect;
  if (frameAspect > 0 && contentAspect > frameAspect) {
    const target = width / frameAspect;
    minY -= (target - height) / 2;
    height = target;
  } else if (frameAspect > 0) {
    const target = height * frameAspect;
    minX -= (target - width) / 2;
    width = target;
  }

  const safeZoom = clamp(Number(zoom) || 1, 1, maxZoom);
  const fullCenterX = minX + width / 2;
  const fullCenterY = minY + height / 2;
  // Record the fit-frame (zoom = 1) so pan/wheel handlers can map screen points
  // to model space and convert a desired centre back into a pan offset.
  state.viewFrame = { centerX: fullCenterX, centerY: fullCenterY, width, height };
  const viewWidth = width / safeZoom;
  const viewHeight = height / safeZoom;
  // Pan is stored relative to the fit centre; clamp it so the plan can never be
  // dragged entirely out of view (limit shrinks to zero as zoom returns to 1).
  const panLimitX = Math.max((width - viewWidth) / 2, 0);
  const panLimitY = Math.max((height - viewHeight) / 2, 0);
  const panX = clamp(Number(state.panX) || 0, -panLimitX, panLimitX);
  const panY = clamp(Number(state.panY) || 0, -panLimitY, panLimitY);
  state.panX = panX;
  state.panY = panY;
  const centerX = fullCenterX + panX;
  const centerY = fullCenterY + panY;
  return [centerX - viewWidth / 2, centerY - viewHeight / 2, viewWidth, viewHeight]
    .map((value) => formatNumber(value, 3))
    .join(" ");
}

function previewFrameAspect() {
  const el = els.planSvg;
  const width = el ? el.clientWidth : 0;
  const height = el ? el.clientHeight : 0;
  if (width > 0 && height > 0) {
    return width / height;
  }
  return 1.6;
}

function previewTransform() {
  return "scale(1,-1)";
}

// ---------------------------------------------------------------------------
// 3D AXON — a real plan-oblique axonometric built from the model: the plan is
// rotated 45° with foreshortened depth, verticals rise true, walls and the
// core are extruded volumes whose faces are painter-sorted back to front.
// Pure SVG polygons — deterministic, zoomable, and export-safe (no filters).
// ---------------------------------------------------------------------------
const axonSettings = {
  angle: Math.PI / 4,
  depthScale: 0.62,
  wallHeight: 2.7,
  partitionHeight: 2.35,
  coreHeight: 3.3,
  slabDepth: 0.35
};

function axonProjector(bounds) {
  const cos = Math.cos(axonSettings.angle);
  const sin = Math.sin(axonSettings.angle);
  const cx = bounds.minX + bounds.width / 2;
  const cy = bounds.minY + bounds.height / 2;
  return {
    // Returns model-up coordinates so the standard scale(1,-1) group renders
    // the scene with +z pointing up on screen.
    point(x, y, z) {
      const dx = Number(x) - cx;
      const dy = Number(y) - cy;
      return {
        x: cx + (dx * cos - dy * sin),
        y: cy + (dx * sin + dy * cos) * axonSettings.depthScale + (Number(z) || 0)
      };
    },
    depth(x, y) {
      const dx = Number(x) - cx;
      const dy = Number(y) - cy;
      return dx * sin + dy * cos;
    }
  };
}

function axonPolygonAttr(projected) {
  return projected.map((p) => `${formatNumber(p.x, 3)},${formatNumber(p.y, 3)}`).join(" ");
}

function axonFace(proj, points, z, className, extend) {
  const projected = points.map((p) => proj.point(p.x, p.y, z));
  extend(projected);
  const depth = points.reduce((sum, p) => sum + proj.depth(p.x, p.y), 0) / Math.max(1, points.length);
  return { className, projected, depth };
}

// One extruded volume for an axis-aligned footprint: side quads drawn back to
// front, then the top. Side tone follows the face normal so solids read.
function axonBox(proj, points, zBottom, zTop, tone, extend) {
  const faces = [];
  const ring = points.slice();
  if (ring.length > 1 && Math.abs(ring[0].x - ring[ring.length - 1].x) < 1e-9
    && Math.abs(ring[0].y - ring[ring.length - 1].y) < 1e-9) {
    ring.pop();
  }
  let nearestDepth = Infinity;
  for (let i = 0; i < ring.length; i += 1) {
    const a = ring[i];
    const b = ring[(i + 1) % ring.length];
    const projected = [
      proj.point(a.x, a.y, zBottom),
      proj.point(b.x, b.y, zBottom),
      proj.point(b.x, b.y, zTop),
      proj.point(a.x, a.y, zTop)
    ];
    extend(projected);
    const horizontal = Math.abs(a.y - b.y) <= Math.abs(a.x - b.x);
    const depth = (proj.depth(a.x, a.y) + proj.depth(b.x, b.y)) / 2;
    nearestDepth = Math.min(nearestDepth, proj.depth(a.x, a.y));
    faces.push({
      className: `axon-side-${horizontal ? "x" : "y"} ${tone}`,
      projected,
      depth
    });
  }
  faces.sort((a, b) => b.depth - a.depth);
  faces.push(axonFace(proj, ring, zTop, `axon-top ${tone}`, extend));
  // A box's paint order is keyed by its NEAREST footprint corner: a centroid
  // key let thin walls beside the large core draw before it and slice through
  // its mass — the closest point decides who is really in front.
  return { depth: Number.isFinite(nearestDepth) ? nearestDepth : 0, faces };
}

// Door and window openings to carve out of each wall volume, keyed by wall id.
// Doors come straight from the model (hostWall); window spans reuse the exact
// 2D glazing geometry and attach to the facade wall they sit on.
const axonOpenings = {
  doorHead: 2.08,
  sillTop: 0.95,
  glassTop: 2.25
};

function axonWallCuts(variant, floorBounds) {
  const cuts = new Map();
  const add = (wallId, cut) => {
    if (!cuts.has(wallId)) {
      cuts.set(wallId, []);
    }
    cuts.get(wallId).push(cut);
  };

  const wallGeometry = new Map();
  (variant.walls || []).forEach((wall) => {
    const line = wall && wall.centerline;
    if (!line || !line.start || !line.end) {
      return;
    }
    const sx = Number(line.start.x);
    const sy = Number(line.start.y);
    const ex = Number(line.end.x);
    const ey = Number(line.end.y);
    const horizontal = Math.abs(ex - sx) >= Math.abs(ey - sy);
    wallGeometry.set(String(wall.id || ""), {
      horizontal,
      axisLo: horizontal ? Math.min(sx, ex) : Math.min(sy, ey),
      axisHi: horizontal ? Math.max(sx, ex) : Math.max(sy, ey),
      cross: horizontal ? (sy + ey) / 2 : (sx + ex) / 2
    });
  });

  (variant.doorsOpenings || []).forEach((door) => {
    const geometry = wallGeometry.get(String(door && door.hostWall || ""));
    if (!geometry || !door.location) {
      return;
    }
    const along = geometry.horizontal ? Number(door.location.x) : Number(door.location.y);
    const half = Math.max(Number(door.width) || 0.9, 0.6) / 2;
    add(String(door.hostWall), { lo: along - half, hi: along + half, kind: "door" });
  });

  (variant.rooms || []).forEach((room) => {
    if (!room || !room.daylight) {
      return;
    }
    const roomBounds = roomVisualBounds(room);
    if (!roomBounds) {
      return;
    }
    facadeSidesFor(roomBounds, floorBounds).forEach((side) => {
      const span = windowSpanForSide(roomBounds, side);
      if (!span) {
        return;
      }
      wallGeometry.forEach((geometry, wallId) => {
        if (geometry.horizontal !== span.horizontal || Math.abs(geometry.cross - span.edge) > 0.12) {
          return;
        }
        const lo = Math.max(span.a, geometry.axisLo + 0.08);
        const hi = Math.min(span.b, geometry.axisHi - 0.08);
        if (hi - lo > 0.35) {
          add(wallId, { lo, hi, kind: "window" });
        }
      });
    });
  });
  return cuts;
}

// Emits the volume set for one wall: solid segments between openings, a
// lintel bridging each door, and sill + glass + header stacks at windows.
// Solid runs additionally split at the core's boundary lines: a long wall
// that passes behind the core while extending toward the viewer cannot be
// painter-ordered as one box — no single depth key is correct for it.
function axonWallVolumes(proj, wall, cuts, extend, sortSplits) {
  const thickness = Math.max(Number(wall.thickness) || 0.1, 0.06);
  const footprint = wallFootprint(wall, thickness);
  if (!footprint) {
    return [];
  }
  const partition = String(wall.layerType || "") === "room_partition";
  const height = partition ? axonSettings.partitionHeight : axonSettings.wallHeight;
  const tone = partition ? "axon-wall-partition" : "axon-wall";

  const frame = boundsOfPoints(footprint);
  const horizontal = frame.width >= frame.height;
  const axisLo = horizontal ? frame.minX : frame.minY;
  const axisHi = horizontal ? frame.maxX : frame.maxY;
  const crossLo = horizontal ? frame.minY : frame.minX;
  const crossHi = horizontal ? frame.maxY : frame.maxX;
  const segmentRect = (lo, hi) => (horizontal
    ? [{ x: lo, y: crossLo }, { x: hi, y: crossLo }, { x: hi, y: crossHi }, { x: lo, y: crossHi }]
    : [{ x: crossLo, y: lo }, { x: crossHi, y: lo }, { x: crossHi, y: hi }, { x: crossLo, y: hi }]);

  const volumes = [];
  const splits = (sortSplits && (horizontal ? sortSplits.x : sortSplits.y)) || [];
  const emitSolid = (lo, hi) => {
    const stops = [lo];
    splits.forEach((value) => {
      if (value > lo + 0.05 && value < hi - 0.05) {
        stops.push(value);
      }
    });
    stops.push(hi);
    stops.sort((a, b) => a - b);
    for (let i = 0; i < stops.length - 1; i += 1) {
      if (stops[i + 1] - stops[i] > 0.04) {
        volumes.push(axonBox(proj, segmentRect(stops[i], stops[i + 1]), 0, height, tone, extend));
      }
    }
  };

  const ordered = wallCutIntervals(cuts, axisLo, axisHi);

  let cursor = axisLo;
  ordered.forEach((cut) => {
    const lo = Math.max(cut.lo, cursor);
    const hi = Math.max(cut.hi, lo);
    if (lo - cursor > 0.04) {
      emitSolid(cursor, lo);
    }
    if (hi - lo > 0.04) {
      const rect = segmentRect(lo, hi);
      if (cut.kind === "door") {
        volumes.push(axonBox(proj, rect, axonOpenings.doorHead, height, tone, extend));
      } else {
        volumes.push(axonBox(proj, rect, 0, axonOpenings.sillTop, tone, extend));
        volumes.push(axonBox(proj, rect, axonOpenings.sillTop, axonOpenings.glassTop, "axon-glass", extend));
        volumes.push(axonBox(proj, rect, axonOpenings.glassTop, height, tone, extend));
      }
    }
    cursor = Math.max(cursor, hi);
  });
  if (axisHi - cursor > 0.04) {
    emitSolid(cursor, axisHi);
  }
  return volumes;
}

function renderAxonView(variant, input, bounds) {
  const proj = axonProjector(bounds);
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  const extend = (projected) => {
    projected.forEach((p) => {
      if (p.x < minX) { minX = p.x; }
      if (p.x > maxX) { maxX = p.x; }
      if (p.y < minY) { minY = p.y; }
      if (p.y > maxY) { maxY = p.y; }
    });
  };

  const floors = [];
  const boxes = [];
  const plate = input && input.floorplate && input.floorplate.outer ? input.floorplate.outer.points : null;

  if (plate) {
    // Ground shadow anchors the model; a flat offset polygon, never a filter.
    const shadow = axonFace(proj, plate, 0, "axon-shadow", extend);
    shadow.projected = shadow.projected.map((p) => ({ x: p.x + 0.55, y: p.y - 0.4 }));
    extend(shadow.projected);
    floors.push(shadow);
    boxes.push({ ...axonBox(proj, plate, -axonSettings.slabDepth, 0, "axon-slab", extend), depth: Infinity });
  }

  if (variant) {
    (variant.rooms || []).forEach((room) => {
      floors.push(axonFace(proj, room.polygon.points, 0.012, `axon-floor ${roomCategoryClass(room)}`, extend));
    });
    (variant.corridors || []).forEach((corridor) => {
      floors.push(axonFace(proj, corridor.polygon.points, 0.012, "axon-floor axon-floor-corridor", extend));
    });
    // Walls carry their openings: door gaps bridged by lintels and facade
    // windows glazed as sill + glass + header bands, carved per wall. The
    // core's boundary lines also split solid runs so painter order stays
    // correct beside the one genuinely large volume in the scene.
    const sortSplits = { x: [], y: [] };
    ((input && input.fixedElements) || []).forEach((fixed) => {
      const fixedBounds = fixed && fixed.polygon ? boundsOfPoints(fixed.polygon.points) : null;
      if (fixedBounds) {
        sortSplits.x.push(fixedBounds.minX, fixedBounds.maxX);
        sortSplits.y.push(fixedBounds.minY, fixedBounds.maxY);
      }
    });
    const cutsByWall = axonWallCuts(variant, bounds);
    (variant.walls || []).forEach((wall) => {
      boxes.push(...axonWallVolumes(proj, wall, cutsByWall.get(String(wall.id || "")), extend, sortSplits));
    });
  }

  if (input && Array.isArray(input.fixedElements)) {
    input.fixedElements.forEach((fixed) => {
      if (fixed.polygon && fixed.polygon.points) {
        boxes.push(axonBox(proj, fixed.polygon.points, 0, axonSettings.coreHeight, "axon-core", extend));
      }
    });
  }

  if (!Number.isFinite(minX)) {
    els.planSvg.removeAttribute("viewBox");
    return;
  }

  const projBounds = { minX, minY, maxX, maxY, width: maxX - minX, height: maxY - minY };
  els.planSvg.setAttribute("viewBox", previewViewBox(projBounds, state.zoom));
  els.planSvg.setAttribute("preserveAspectRatio", "xMidYMid meet");
  const group = svgEl("g", { transform: previewTransform() });
  els.planSvg.appendChild(group);

  // Painter's algorithm: the slab first (depth Infinity), flat floors on it,
  // then whole volumes from the back of the scene forward — sorting complete
  // boxes (not loose faces) keeps wall junctions free of interleave artifacts.
  const emit = (face) => group.appendChild(svgEl("polygon", { class: face.className, points: axonPolygonAttr(face.projected) }));
  boxes.sort((a, b) => b.depth - a.depth);
  const slabAndBack = boxes.filter((box) => box.depth === Infinity);
  slabAndBack.forEach((box) => box.faces.forEach(emit));
  floors.forEach(emit);
  boxes.filter((box) => box.depth !== Infinity).forEach((box) => box.faces.forEach(emit));

  // Billboard room tags: small upright labels anchored on each floor, so the
  // model stays readable without turning back to the 2D plan.
  if (variant && state.labelsVisible) {
    (variant.rooms || []).forEach((room) => {
      const roomBounds = roomVisualBounds(room);
      if (!roomBounds || Math.min(roomBounds.width, roomBounds.height) < 1.6) {
        return;
      }
      const anchor = proj.point(roomBounds.minX + roomBounds.width / 2, roomBounds.minY + roomBounds.height / 2, 0.02);
      const tag = svgEl("text", {
        class: "axon-room-tag",
        x: round(anchor.x),
        y: round(-anchor.y),
        "text-anchor": "middle",
        "font-size": "0.42"
      });
      tag.textContent = compactRoomLabel(room);
      els.planSvg.appendChild(tag);
    });
  }
}

// ---------------------------------------------------------------------------
// 3D MODEL — a real-time three.js walkthrough of the selected variant. The
// scene is computed HERE as plain data (axis-aligned boxes, polygon prisms,
// floor plates and label anchors in model metres, z up) so it shares the
// axon's exact opening math; every WebGL concern lives in viewer3d.js, which
// is lazy-loaded from local vendored modules the first time the tab opens.
// ---------------------------------------------------------------------------
const viewer3dModuleUrl = "./viewer3d.js?v=20260612-1";
let viewer3dPromise = null;
let viewer3dApi = null;

function ensureViewer3d() {
  if (!viewer3dPromise) {
    viewer3dPromise = import(viewer3dModuleUrl)
      .then((module) => {
        viewer3dApi = module.createViewer(els.viewerCanvasHost, {
          onPickRoom: handleViewerPick,
          onModeChange: updateViewerToolbar,
          onStatus: setStatus
        });
        return viewer3dApi;
      })
      .catch((error) => {
        viewer3dPromise = null;
        setStatus("3D viewer failed to load");
        console.error("viewer3d load failed", error);
        return null;
      });
  }
  return viewer3dPromise;
}

// Runs synchronously once the module is up so camera-mode clicks keep their
// user-gesture context (pointer lock is refused outside of one).
function withViewer3d(fn) {
  if (viewer3dApi) {
    fn(viewer3dApi);
    return;
  }
  ensureViewer3d().then((viewer) => {
    if (viewer) {
      fn(viewer);
    }
  });
}

function hideModelViewport() {
  if (els.modelViewport) {
    els.modelViewport.hidden = true;
  }
  if (viewer3dApi) {
    viewer3dApi.setActive(false);
  }
}

function renderModelView(variant, input, bounds) {
  els.emptyPreview.hidden = Boolean(variant);
  renderEmptyPreviewContent();
  if (!variant || !els.modelViewport) {
    hideModelViewport();
    return;
  }
  els.modelViewport.hidden = false;
  const sceneData = buildModelScene(variant, input, bounds);
  ensureViewer3d().then((viewer) => {
    if (viewer && state.viewMode === "model3d") {
      viewer.setScene(sceneData);
      viewer.setActive(true);
      updateViewerToolbar(viewer.cameraMode());
    }
  });
}

function handleViewerPick(roomId) {
  if (!roomId) {
    state.selection = null;
    renderAll();
    return;
  }
  state.selection = { kind: "room", id: String(roomId) };
  setStatus("Room selected");
  renderAll();
}

function updateViewerToolbar(mode) {
  const walking = mode === "walk";
  if (els.viewerOrbitBtn) {
    els.viewerOrbitBtn.classList.toggle("active", !walking);
    els.viewerOrbitBtn.setAttribute("aria-pressed", walking ? "false" : "true");
  }
  if (els.viewerWalkBtn) {
    els.viewerWalkBtn.classList.toggle("active", walking);
    els.viewerWalkBtn.setAttribute("aria-pressed", walking ? "true" : "false");
  }
  if (els.viewerHint) {
    els.viewerHint.textContent = walking
      ? "W A S D to move · mouse to look · Esc to exit"
      : "Drag to orbit · scroll to zoom · click a room to select it";
  }
}

// Shared by the SVG axon and the 3D model: openings clamped into the wall,
// doors winning where a window overlaps one, ordered along the wall axis.
// One implementation so the two views can never disagree about an opening.
function wallCutIntervals(cuts, axisLo, axisHi) {
  const doors = (cuts || []).filter((cut) => cut.kind === "door");
  return (cuts || [])
    .map((cut) => ({ ...cut, lo: Math.max(cut.lo, axisLo + 0.02), hi: Math.min(cut.hi, axisHi - 0.02) }))
    .filter((cut) => cut.hi - cut.lo > 0.2)
    .filter((cut) => cut.kind === "door"
      || !doors.some((door) => intervalOverlap(cut.lo, cut.hi, door.lo, door.hi) > 0.01))
    .sort((a, b) => a.lo - b.lo);
}

// Model-space volume decomposition of one wall: solid runs between openings,
// a lintel above each door gap, and sill + glass + header stacks at windows —
// the same bands the axon extrudes, emitted as data boxes for the viewer.
function wallVolumeParts(wall, cuts) {
  const thickness = Math.max(Number(wall.thickness) || 0.1, 0.06);
  const footprint = wallFootprint(wall, thickness);
  if (!footprint) {
    return [];
  }
  const partition = String(wall.layerType || "") === "room_partition";
  const height = partition ? axonSettings.partitionHeight : axonSettings.wallHeight;
  const wallKind = partition ? "wall-partition" : "wall";
  const frame = boundsOfPoints(footprint);
  const horizontal = frame.width >= frame.height;
  const axisLo = horizontal ? frame.minX : frame.minY;
  const axisHi = horizontal ? frame.maxX : frame.maxY;
  const crossLo = horizontal ? frame.minY : frame.minX;
  const crossHi = horizontal ? frame.maxY : frame.maxX;

  const parts = [];
  const emit = (lo, hi, z0, z1, kind) => {
    if (hi - lo <= 0.04 || z1 - z0 <= 0.01) {
      return;
    }
    parts.push(horizontal
      ? { x0: lo, y0: crossLo, x1: hi, y1: crossHi, z0, z1, kind }
      : { x0: crossLo, y0: lo, x1: crossHi, y1: hi, z0, z1, kind });
  };

  let cursor = axisLo;
  wallCutIntervals(cuts, axisLo, axisHi).forEach((cut) => {
    const lo = Math.max(cut.lo, cursor);
    const hi = Math.max(cut.hi, lo);
    if (lo - cursor > 0.04) {
      emit(cursor, lo, 0, height, wallKind);
    }
    if (cut.kind === "door") {
      emit(lo, hi, axonOpenings.doorHead, height, wallKind);
    } else {
      emit(lo, hi, 0, axonOpenings.sillTop, wallKind);
      emit(lo, hi, axonOpenings.sillTop, axonOpenings.glassTop, "glass");
      emit(lo, hi, axonOpenings.glassTop, height, wallKind);
    }
    cursor = Math.max(cursor, hi);
  });
  if (axisHi - cursor > 0.04) {
    emit(cursor, axisHi, 0, height, wallKind);
  }
  return parts;
}

function ringPoints(points) {
  const ring = (points || []).map((p) => ({ x: Number(p.x), y: Number(p.y) }));
  if (ring.length > 1) {
    const first = ring[0];
    const last = ring[ring.length - 1];
    if (Math.abs(first.x - last.x) < 1e-9 && Math.abs(first.y - last.y) < 1e-9) {
      ring.pop();
    }
  }
  return ring;
}

// White-model furniture: a few honest volumes at real heights so rooms read
// at eye level during the walkthrough — never decoration the plan doesn't claim.
function modelFurnitureFor(room) {
  const bounds = roomVisualBounds(room);
  if (!bounds) {
    return [];
  }
  const inner = insetBounds(bounds, 0.24);
  if (inner.width < 1.15 || inner.height < 1.15) {
    return [];
  }
  const type = roomCategoryClass(room);
  const boxes = [];
  const push = (x0, y0, x1, y1, z1) => {
    const cx0 = Math.max(inner.minX, Math.min(x0, x1));
    const cy0 = Math.max(inner.minY, Math.min(y0, y1));
    const cx1 = Math.min(inner.maxX, Math.max(x0, x1));
    const cy1 = Math.min(inner.maxY, Math.max(y0, y1));
    if (cx1 - cx0 > 0.18 && cy1 - cy0 > 0.18) {
      boxes.push({ x0: cx0, y0: cy0, x1: cx1, y1: cy1, z0: 0.06, z1, kind: "furniture" });
    }
  };
  const cx = inner.minX + inner.width / 2;
  if (type === "room-bedroom") {
    const width = Math.min(1.7, inner.width * 0.55);
    push(cx - width / 2, inner.minY, cx + width / 2, inner.minY + Math.min(2.05, inner.height * 0.6), 0.52);
    push(cx - width / 2 - 0.55, inner.minY, cx - width / 2 - 0.12, inner.minY + 0.45, 0.5);
    push(cx + width / 2 + 0.12, inner.minY, cx + width / 2 + 0.55, inner.minY + 0.45, 0.5);
  } else if (type === "room-living") {
    const width = Math.min(2.3, inner.width * 0.62);
    push(cx - width / 2, inner.maxY - 0.92, cx + width / 2, inner.maxY, 0.74);
    push(cx - 0.55, inner.maxY - 2.05, cx + 0.55, inner.maxY - 1.45, 0.38);
    push(cx - Math.min(0.9, width / 2), inner.minY, cx + Math.min(0.9, width / 2), inner.minY + 0.42, 0.5);
  } else if (type === "room-kitchen") {
    if (inner.width >= inner.height) {
      push(inner.minX, inner.minY, inner.maxX, inner.minY + 0.62, 0.92);
    } else {
      push(inner.minX, inner.minY, inner.minX + 0.62, inner.maxY, 0.92);
    }
  } else if (type === "room-bathroom") {
    push(inner.minX, inner.minY, inner.minX + Math.min(1.05, inner.width * 0.5), inner.minY + 0.52, 0.86);
    push(inner.maxX - 0.42, inner.minY, inner.maxX, inner.minY + 0.66, 0.42);
  } else if (type === "room-service") {
    push(inner.minX, inner.minY, inner.minX + 0.5, inner.maxY, 1.9);
  }
  return boxes;
}

function buildModelScene(variant, input, bounds) {
  const sceneData = {
    name: variant && variant.variantId ? String(variant.variantId) : "floor-plan",
    bounds: { minX: bounds.minX, minY: bounds.minY, width: bounds.width, height: bounds.height },
    slabDepth: axonSettings.slabDepth,
    boxes: [],
    prisms: [],
    floors: [],
    labels: [],
    selectedRoomId: state.selection && state.selection.kind === "room" ? String(state.selection.id) : ""
  };

  const plate = input && input.floorplate && input.floorplate.outer ? input.floorplate.outer.points : null;
  if (plate) {
    sceneData.prisms.push({ points: ringPoints(plate), z0: -axonSettings.slabDepth, z1: 0, kind: "slab" });
  }
  ((input && input.fixedElements) || []).forEach((fixed) => {
    if (fixed.polygon && fixed.polygon.points) {
      sceneData.prisms.push({ points: ringPoints(fixed.polygon.points), z0: 0, z1: axonSettings.coreHeight, kind: "core" });
    }
  });

  if (variant) {
    (variant.rooms || []).forEach((room) => {
      sceneData.floors.push({
        points: ringPoints(room.polygon.points),
        kind: roomCategoryClass(room).replace("room-", "floor-"),
        roomId: String(room.id || ""),
        name: planRoomLabelName(room)
      });
      modelFurnitureFor(room).forEach((box) => sceneData.boxes.push(box));
      const roomBounds = roomVisualBounds(room);
      if (roomBounds && Math.min(roomBounds.width, roomBounds.height) >= 1.6) {
        sceneData.labels.push({
          x: roomBounds.minX + roomBounds.width / 2,
          y: roomBounds.minY + roomBounds.height / 2,
          z: 1.55,
          text: compactRoomLabel(room)
        });
      }
    });
    (variant.corridors || []).forEach((corridor) => {
      sceneData.floors.push({ points: ringPoints(corridor.polygon.points), kind: "floor-corridor", roomId: "", name: "" });
    });
    const cutsByWall = axonWallCuts(variant, bounds);
    (variant.walls || []).forEach((wall) => {
      wallVolumeParts(wall, cutsByWall.get(String(wall.id || ""))).forEach((box) => sceneData.boxes.push(box));
    });
  }
  return sceneData;
}

function renderDimensionGuides(bounds, units = "m") {
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
    return;
  }

  const maxDim = Math.max(bounds.width, bounds.height);
  const offset = Math.max(maxDim * 0.075, 1.6);
  const labelGap = Math.max(maxDim * 0.024, 0.58);
  const tick = Math.max(maxDim * 0.012, 0.28);
  // Dimension lettering scales with the plan so a 12 m cottage doesn't get
  // billboard-sized text while a 60 m slab keeps legible labels.
  const font = clamp(maxDim * 0.016, 0.3, 0.78);
  // Witness (extension) lines start with a small gap off the object and run just
  // past the dimension line, with 45-degree oblique tick slashes at each station —
  // the architectural convention (NKBA) instead of perpendicular crossbars.
  const witnessGap = Math.max(maxDim * 0.009, 0.16);
  const over = tick * 0.6;
  const centerX = bounds.minX + bounds.width / 2;
  const centerY = bounds.minY + bounds.height / 2;
  const group = svgEl("g", { class: "dimension-guides", "aria-hidden": "true" });

  const topY = bounds.maxY + offset;
  appendDimensionLine(group, bounds.minX, bounds.maxY + witnessGap, bounds.minX, topY + over, "dimension-witness");
  appendDimensionLine(group, bounds.maxX, bounds.maxY + witnessGap, bounds.maxX, topY + over, "dimension-witness");
  appendDimensionLine(group, bounds.minX, topY, bounds.maxX, topY);
  appendObliqueTick(group, bounds.minX, topY, tick);
  appendObliqueTick(group, bounds.maxX, topY, tick);
  appendDimensionText(group, `${formatNumber(bounds.width, 1)} ${units || "m"}`, centerX, topY + labelGap, "horizontal", font);

  const bottomY = bounds.minY - offset;
  appendDimensionLine(group, bounds.minX, bounds.minY - witnessGap, bounds.minX, bottomY - over, "dimension-witness");
  appendDimensionLine(group, bounds.maxX, bounds.minY - witnessGap, bounds.maxX, bottomY - over, "dimension-witness");
  appendDimensionLine(group, bounds.minX, bottomY, bounds.maxX, bottomY);
  appendObliqueTick(group, bounds.minX, bottomY, tick);
  appendObliqueTick(group, bounds.maxX, bottomY, tick);

  const rightX = bounds.maxX + offset;
  appendDimensionLine(group, bounds.maxX + witnessGap, bounds.minY, rightX + over, bounds.minY, "dimension-witness");
  appendDimensionLine(group, bounds.maxX + witnessGap, bounds.maxY, rightX + over, bounds.maxY, "dimension-witness");
  appendDimensionLine(group, rightX, bounds.minY, rightX, bounds.maxY);
  appendObliqueTick(group, rightX, bounds.minY, tick);
  appendObliqueTick(group, rightX, bounds.maxY, tick);
  appendDimensionText(group, `${formatNumber(bounds.height, 1)} ${units || "m"}`, rightX + labelGap, centerY, "vertical", font);

  const leftX = bounds.minX - offset;
  appendDimensionLine(group, bounds.minX - witnessGap, bounds.minY, leftX - over, bounds.minY, "dimension-witness");
  appendDimensionLine(group, bounds.minX - witnessGap, bounds.maxY, leftX - over, bounds.maxY, "dimension-witness");
  appendDimensionLine(group, leftX, bounds.minY, leftX, bounds.maxY);
  appendObliqueTick(group, leftX, bounds.minY, tick);
  appendObliqueTick(group, leftX, bounds.maxY, tick);
  renderScaleBar(group, bounds, units || "m", offset);

  els.planSvg.appendChild(group);
}

function appendDimensionLine(group, x1, y1, x2, y2, className = "dimension-line") {
  group.appendChild(svgEl("line", {
    class: className,
    x1: round(x1),
    y1: round(-y1),
    x2: round(x2),
    y2: round(-y2)
  }));
}

function appendObliqueTick(group, x, y, size) {
  // 45-degree slash centred on the witness station (architectural dimension tick).
  const half = size * 0.7;
  group.appendChild(svgEl("line", {
    class: "dimension-tick",
    x1: round(x - half),
    y1: round(-(y - half)),
    x2: round(x + half),
    y2: round(-(y + half))
  }));
}

function appendDimensionText(group, label, x, y, orientation = "horizontal", fontSize = 0.68) {
  const renderedY = round(-y);
  const text = svgEl("text", {
    class: `dimension-label dimension-label-${orientation}`,
    x: round(x),
    y: renderedY,
    "text-anchor": "middle",
    "font-size": formatNumber(fontSize, 2)
  });
  if (orientation === "vertical") {
    text.setAttribute("transform", `rotate(-90 ${round(x)} ${renderedY})`);
  }
  text.textContent = label;
  group.appendChild(text);
}

function renderScaleBar(group, bounds, units = "m", offset = 1.6) {
  const length = niceScaleBarLength(bounds.width * 0.18);
  if (!Number.isFinite(length) || length <= 0) {
    return;
  }

  const inset = Math.max(offset * 0.35, 0.8);
  const x1 = bounds.maxX - inset - length;
  const x2 = bounds.maxX - inset;
  const y = bounds.minY - Math.max(offset * 0.5, 1.05);
  const tick = Math.max(Math.max(bounds.width, bounds.height) * 0.008, 0.2);
  const scaleGroup = svgEl("g", { class: "scale-bar", "aria-hidden": "true" });

  const font = clamp(Math.max(bounds.width, bounds.height) * 0.016, 0.3, 0.72);
  appendScaleBarLine(scaleGroup, x1, y, x2, y, "scale-bar-line");
  appendScaleBarLine(scaleGroup, x1, y - tick, x1, y + tick, "scale-bar-tick");
  appendScaleBarLine(scaleGroup, x2, y - tick, x2, y + tick, "scale-bar-tick");
  appendScaleBarText(scaleGroup, "0", x1, y - tick * 1.7, "start", font);
  appendScaleBarText(scaleGroup, `${formatNumber(length, length >= 10 ? 0 : 1)} ${units || "m"}`, x2, y - tick * 1.7, "end", font);
  group.appendChild(scaleGroup);
}

function niceScaleBarLength(target) {
  if (!Number.isFinite(target) || target <= 0) {
    return 1;
  }

  const power = Math.pow(10, Math.floor(Math.log10(target)));
  const normalized = target / power;
  const factor = normalized >= 5 ? 5 : normalized >= 2 ? 2 : 1;
  return factor * power;
}

function appendScaleBarLine(group, x1, y1, x2, y2, className) {
  group.appendChild(svgEl("line", {
    class: className,
    x1: round(x1),
    y1: round(-y1),
    x2: round(x2),
    y2: round(-y2)
  }));
}

function appendScaleBarText(group, label, x, y, anchor, fontSize = 0.72) {
  const text = svgEl("text", {
    class: "scale-bar-label",
    x: round(x),
    y: round(-y),
    "text-anchor": anchor,
    "font-size": formatNumber(fontSize, 2)
  });
  text.textContent = label;
  group.appendChild(text);
}

function renderNorthArrow(bounds) {
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
    return;
  }

  const maxDim = Math.max(bounds.width, bounds.height);
  const size = clamp(maxDim * 0.03, 0.85, 1.7);
  const offset = Math.max(maxDim * 0.075, 1.6);
  const cx = bounds.maxX + offset * 0.58;
  const baseY = bounds.maxY + offset * 0.18;
  const group = svgEl("g", { class: "north-arrow", "aria-hidden": "true" });
  group.appendChild(polygonEl([
    { x: cx, y: -(baseY + size) },
    { x: cx - size * 0.32, y: -baseY },
    { x: cx, y: -(baseY + size * 0.3) },
    { x: cx + size * 0.32, y: -baseY }
  ], "north-arrow-needle"));
  const label = svgEl("text", {
    class: "north-arrow-label",
    x: round(cx),
    y: round(-(baseY + size + size * 0.52)),
    "text-anchor": "middle",
    "font-size": formatNumber(size * 0.6, 2)
  });
  label.textContent = "N";
  group.appendChild(label);
  els.planSvg.appendChild(group);
}

function renderInputAccessMarkers(group, input, bounds) {
  const entries = input && input.access && Array.isArray(input.access.entryPoints)
    ? input.access.entryPoints
    : [];
  if (entries.length === 0 || !bounds) {
    return;
  }

  const markerGroup = svgEl("g", { class: "entry-markers", "aria-hidden": "true" });
  entries.slice(0, 4).forEach((point) => {
    if (!Number.isFinite(Number(point.x)) || !Number.isFinite(Number(point.y))) {
      return;
    }
    markerGroup.appendChild(polygonEl(entryMarkerPoints(point, bounds), "entry-marker"));
  });
  if (markerGroup.childElementCount > 0) {
    group.appendChild(markerGroup);
  }
}

function entryMarkerPoints(point, bounds) {
  const x = Number(point.x);
  const y = Number(point.y);
  const size = clamp(Math.max(bounds.width, bounds.height) * 0.018, 0.42, 0.9);
  const side = closestPointSide({ x, y }, bounds);
  if (side === "top") {
    return [
      { x, y: y - size * 0.12 },
      { x: x - size * 0.42, y: y + size },
      { x: x + size * 0.42, y: y + size }
    ];
  }
  if (side === "left") {
    return [
      { x: x + size * 0.12, y },
      { x: x - size, y: y - size * 0.42 },
      { x: x - size, y: y + size * 0.42 }
    ];
  }
  if (side === "right") {
    return [
      { x: x - size * 0.12, y },
      { x: x + size, y: y - size * 0.42 },
      { x: x + size, y: y + size * 0.42 }
    ];
  }
  return [
    { x, y: y + size * 0.12 },
    { x: x - size * 0.42, y: y - size },
    { x: x + size * 0.42, y: y - size }
  ];
}

function closestPointSide(point, bounds) {
  const candidates = [
    ["left", Math.abs(point.x - bounds.minX)],
    ["right", Math.abs(point.x - bounds.maxX)],
    ["bottom", Math.abs(point.y - bounds.minY)],
    ["top", Math.abs(point.y - bounds.maxY)]
  ].filter(([, distance]) => Number.isFinite(distance));
  candidates.sort((a, b) => a[1] - b[1]);
  return candidates.length ? candidates[0][0] : "bottom";
}

function renderPlanGlyphLayer(group, variant, bounds) {
  if (!variant) {
    return;
  }

  const layer = svgEl("g", { class: "plan-visual-layer", "aria-hidden": "true" });
  if (state.editMode || state.viewMode === "circulation") {
    renderCorridorCenterlines(layer, variant);
  }
  renderWetRoomFloors(layer, variant);
  renderRoomFixtures(layer, variant);
  if (layer.childElementCount > 0) {
    group.appendChild(layer);
  }
}

// Bathrooms and kitchens read as tiled floors — under the fixtures, so the
// material hint never competes with the line work above it.
function renderWetRoomFloors(layer, variant) {
  (variant.rooms || []).forEach((room) => {
    const type = String(room && (room.roomType || "")).toLowerCase();
    if (!type.includes("bath") && !type.includes("kitchen") && !type.includes("wc")) {
      return;
    }
    // roomVisualBounds applies any manual edits, so the tiling follows the
    // room when a wall is dragged (rooms are rectangles by construction).
    const roomBounds = roomVisualBounds(room);
    if (roomBounds && roomBounds.width > 0.4 && roomBounds.height > 0.4) {
      const tile = rectEl(roomBounds.minX, roomBounds.minY, roomBounds.width, roomBounds.height, "floor-tile");
      tile.setAttribute("data-room-ref", String(room.id || ""));
      layer.appendChild(tile);
    }
  });
}

// Windows are openings punched through the wall poché, so this layer MUST
// paint after the walls — rendered earlier it disappears under the ink, which
// is exactly the regression that once made every window invisible.
function renderWindowLayer(group, variant, floorBounds) {
  if (!variant) {
    return;
  }
  const layer = svgEl("g", { class: "plan-window-layer", "aria-hidden": "true" });
  (variant.rooms || []).forEach((room) => {
    if (!room || !room.daylight) {
      return;
    }

    const bounds = roomVisualBounds(room);
    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
      return;
    }

    const wrap = svgEl("g", { "data-room-ref": String(room.id || "") });
    facadeSidesFor(bounds, floorBounds).forEach((side) => appendWindowSymbol(wrap, bounds, side));
    if (wrap.childElementCount > 0) {
      layer.appendChild(wrap);
    }
  });
  if (layer.childElementCount > 0) {
    group.appendChild(layer);
  }
}

// Every side of the room that genuinely lies on the floorplate boundary gets
// glazing (a corner room reads with two windows, like a real plan); rooms
// whose facade the bounding box cannot prove fall back to the nearest side.
function facadeSidesFor(roomBounds, floorBounds) {
  if (!floorBounds) {
    return [roomBounds.width >= roomBounds.height ? "top" : "right"];
  }
  const tol = 0.05;
  const sides = [];
  if (Math.abs(roomBounds.minX - floorBounds.minX) <= tol) { sides.push(["left", roomBounds.height]); }
  if (Math.abs(roomBounds.maxX - floorBounds.maxX) <= tol) { sides.push(["right", roomBounds.height]); }
  if (Math.abs(roomBounds.minY - floorBounds.minY) <= tol) { sides.push(["bottom", roomBounds.width]); }
  if (Math.abs(roomBounds.maxY - floorBounds.maxY) <= tol) { sides.push(["top", roomBounds.width]); }
  if (!sides.length) {
    return [closestFacadeSide(roomBounds, floorBounds)];
  }
  return sides.sort((a, b) => b[1] - a[1]).slice(0, 2).map((entry) => entry[0]);
}

// The glazed span a daylit room cuts into its facade: shared by the 2D window
// symbol and the 3D axon glass band so both views open the same wall segment.
function windowSpanForSide(bounds, side) {
  const horizontal = side === "top" || side === "bottom";
  const span = horizontal ? bounds.width : bounds.height;
  if (span < 0.85) {
    return null;
  }
  const inset = clamp(span * 0.16, 0.16, 0.6);
  const a = (horizontal ? bounds.minX : bounds.minY) + inset;
  const b = (horizontal ? bounds.maxX : bounds.maxY) - inset;
  if (b - a < 0.4) {
    return null;
  }
  let edge;
  if (side === "top") {
    edge = bounds.maxY;
  } else if (side === "bottom") {
    edge = bounds.minY;
  } else if (side === "left") {
    edge = bounds.minX;
  } else {
    edge = bounds.maxX;
  }
  return { horizontal, a, b, edge };
}

// Draws a glazed window symbol (sill + glass pane + mullion + jambs) set into
// the facade wall of a daylit room, the way a real plan shows openings.
function appendWindowSymbol(layer, bounds, side) {
  const spanInfo = windowSpanForSide(bounds, side);
  if (!spanInfo) {
    return;
  }
  const horizontal = spanInfo.horizontal;
  const a = spanInfo.a;
  const b = spanInfo.b;
  const edge = spanInfo.edge;
  const t = 0.12;

  const lo = horizontal ? { x: a, y: edge } : { x: edge, y: a };
  const hi = horizontal ? { x: b, y: edge } : { x: edge, y: b };
  // A window is a white break punched through the wall poché with glazing
  // lines across it — the print convention — not a colored highlight band.
  if (horizontal) {
    layer.appendChild(rectEl(a, edge - t, b - a, t * 2, "window-break"));
  } else {
    layer.appendChild(rectEl(edge - t, a, t * 2, b - a, "window-break"));
  }
  layer.appendChild(lineEl(offsetAcross(lo, side, -t), offsetAcross(hi, side, -t), "window-frame"));
  layer.appendChild(lineEl(offsetAcross(lo, side, t), offsetAcross(hi, side, t), "window-frame"));
  layer.appendChild(lineEl(lo, hi, "window-mullion"));
  layer.appendChild(lineEl(offsetAcross(lo, side, -t), offsetAcross(lo, side, t), "window-jamb"));
  layer.appendChild(lineEl(offsetAcross(hi, side, -t), offsetAcross(hi, side, t), "window-jamb"));
}

function offsetAcross(point, side, amount) {
  const horizontal = side === "top" || side === "bottom";
  return horizontal
    ? { x: point.x, y: point.y + amount }
    : { x: point.x + amount, y: point.y };
}

function closestFacadeSide(roomBounds, floorBounds) {
  if (!floorBounds) {
    return roomBounds.width >= roomBounds.height ? "top" : "right";
  }

  const candidates = [
    ["left", Math.abs(roomBounds.minX - floorBounds.minX)],
    ["right", Math.abs(roomBounds.maxX - floorBounds.maxX)],
    ["bottom", Math.abs(roomBounds.minY - floorBounds.minY)],
    ["top", Math.abs(roomBounds.maxY - floorBounds.maxY)]
  ].filter(([, distance]) => Number.isFinite(distance));
  candidates.sort((a, b) => a[1] - b[1]);
  return candidates.length ? candidates[0][0] : "top";
}

function renderCorridorCenterlines(layer, variant) {
  (variant.corridors || []).forEach((corridor) => {
    if (corridor && corridor.centerline && corridor.centerline.start && corridor.centerline.end) {
      layer.appendChild(lineEl(corridor.centerline.start, corridor.centerline.end, "corridor-centerline"));
    }
  });
}

function renderRoomFixtures(layer, variant) {
  const fixtures = svgEl("g", { class: "room-fixtures", "aria-hidden": "true" });
  (variant.rooms || []).forEach((room) => {
    // Per-room wrapper so a live drag can fade exactly this room's furniture.
    const wrap = svgEl("g", { "data-room-ref": String((room && room.id) || "") });
    appendRoomFixture(wrap, room);
    if (wrap.childElementCount > 0) {
      fixtures.appendChild(wrap);
    }
  });
  if (fixtures.childElementCount > 0) {
    layer.appendChild(fixtures);
  }
}

function appendRoomFixture(group, room) {
  const bounds = roomVisualBounds(room);
  if (!bounds || bounds.width < 0.75 || bounds.height < 0.75) {
    return;
  }

  const type = String(room && (room.roomType || room.type || "")).toLowerCase();
  if (type.includes("bed")) {
    appendBedroomFixture(group, bounds);
  } else if (type.includes("bath") || type.includes("wc")) {
    appendBathroomFixture(group, bounds);
  } else if (type.includes("kitchen")) {
    appendKitchenFixture(group, bounds);
  } else if (type.includes("living") || type.includes("dining")) {
    appendLivingFixture(group, bounds);
  } else if (type.includes("balcony") || type.includes("terrace")) {
    appendBalconyFixture(group, bounds);
  } else if (type.includes("utility") || type.includes("storage") || type.includes("laundry")) {
    appendUtilityFixture(group, bounds);
  } else if (bounds.width * bounds.height >= 8) {
    appendGeneralRoomFixture(group, bounds);
  }
}

function appendBedroomFixture(group, bounds) {
  const inner = insetBounds(bounds, fixturePadding(bounds));
  if (inner.width < 0.6 || inner.height < 0.6) {
    return;
  }
  const horizontal = inner.width >= inner.height;
  const x0 = inner.minX;
  const y0 = inner.minY;
  const w = inner.width;
  const h = inner.height;
  const roomy = w * h >= 9;

  if (roomy) {
    const r = Math.min(w, h) * 0.08;
    appendFixtureRect(group, x0 + r, y0 + r, w - r * 2, h - r * 2, "fixture-rug", roundedAttr(0.22));
  }

  // Reserve a wall band for the wardrobe so the bed never collides with it.
  const bandW = horizontal ? w * 0.82 : w;
  const bandH = horizontal ? h : h * 0.82;
  const bedW = horizontal ? bandW * 0.5 : bandW * 0.6;
  const bedH = horizontal ? bandH * 0.66 : bandH * 0.5;
  const bedX = horizontal ? x0 : x0 + (bandW - bedW) / 2;
  const bedY = horizontal ? y0 + (bandH - bedH) / 2 : y0;
  appendFixtureRect(group, bedX, bedY, bedW, bedH, "fixture-bed", roundedAttr(0.09));

  // Headboard strip, a turned-down duvet fold line, and two pillows at the head.
  const head = Math.max(Math.min(bedW, bedH) * 0.14, 0.07);
  if (horizontal) {
    appendFixtureRect(group, bedX, bedY, head, bedH, "fixture-headboard");
    appendFixtureLine(group, bedX + bedW * 0.68, bedY, bedX + bedW * 0.68, bedY + bedH);
    appendFixtureRect(group, bedX + head * 1.3, bedY + bedH * 0.1, bedW * 0.17, bedH * 0.33, "fixture-pillow", roundedAttr(0.05));
    appendFixtureRect(group, bedX + head * 1.3, bedY + bedH * 0.57, bedW * 0.17, bedH * 0.33, "fixture-pillow", roundedAttr(0.05));
  } else {
    appendFixtureRect(group, bedX, bedY, bedW, head, "fixture-headboard");
    appendFixtureLine(group, bedX, bedY + bedH * 0.68, bedX + bedW, bedY + bedH * 0.68);
    appendFixtureRect(group, bedX + bedW * 0.1, bedY + head * 1.3, bedW * 0.33, bedH * 0.17, "fixture-pillow", roundedAttr(0.05));
    appendFixtureRect(group, bedX + bedW * 0.57, bedY + head * 1.3, bedW * 0.33, bedH * 0.17, "fixture-pillow", roundedAttr(0.05));
  }

  // Nightstands beside the head, sized to the gap so they stay inside the room.
  if (roomy) {
    if (horizontal) {
      const gap = (bandH - bedH) / 2;
      const ns = Math.min(gap * 0.8, bedW * 0.22);
      if (ns > 0.16) {
        appendFixtureRect(group, bedX, bedY - gap / 2 - ns / 2, ns, ns, "fixture-nightstand");
        appendFixtureRect(group, bedX, bedY + bedH + gap / 2 - ns / 2, ns, ns, "fixture-nightstand");
      }
    } else {
      const gap = (bandW - bedW) / 2;
      const ns = Math.min(gap * 0.8, bedH * 0.22);
      if (ns > 0.16) {
        appendFixtureRect(group, bedX - gap / 2 - ns / 2, bedY, ns, ns, "fixture-nightstand");
        appendFixtureRect(group, bedX + bedW + gap / 2 - ns / 2, bedY, ns, ns, "fixture-nightstand");
      }
    }
  }

  // Wardrobe filling the reserved wall band, with sliding-door split lines.
  if (horizontal) {
    const wx = x0 + w * 0.86;
    appendFixtureRect(group, wx, y0 + h * 0.06, w * 0.12, h * 0.88, "fixture-wardrobe");
    appendDoorSplits(group, wx, y0 + h * 0.06, w * 0.12, h * 0.88, false, 3);
  } else {
    const wy = y0 + h * 0.86;
    appendFixtureRect(group, x0 + w * 0.06, wy, w * 0.88, h * 0.12, "fixture-wardrobe");
    appendDoorSplits(group, x0 + w * 0.06, wy, w * 0.88, h * 0.12, true, 3);
  }
}

function appendBathroomFixture(group, bounds) {
  const inner = insetBounds(bounds, fixturePadding(bounds) * 0.8);
  if (inner.width < 0.5 || inner.height < 0.5) {
    return;
  }
  const horizontal = inner.width >= inner.height;
  const x0 = inner.minX;
  const y0 = inner.minY;
  const w = inner.width;
  const h = inner.height;
  const fit = Math.min(w, h);
  const big = w * h >= 4.5;

  // Wet wall hosts a tub (roomy) or a shower (tight); WC + vanity basin opposite.
  if (horizontal) {
    if (big) {
      const tubH = h * 0.42;
      appendFixtureRect(group, x0, y0, w * 0.5, tubH, "fixture-bath", roundedAttr(0.16));
      appendFixtureEllipse(group, x0 + w * 0.14, y0 + tubH * 0.5, w * 0.045, tubH * 0.2, "fixture-detail");
    } else {
      appendShower(group, x0, y0, Math.min(w * 0.42, fit * 0.7), Math.min(h * 0.5, fit * 0.7));
    }
    appendToilet(group, x0 + w * 0.04, y0 + h - fit * 0.46, fit * 0.42);
    appendBasin(group, x0 + w - w * 0.34, y0 + h - h * 0.34, w * 0.3, h * 0.24);
  } else {
    if (big) {
      const tubW = w * 0.42;
      appendFixtureRect(group, x0, y0, tubW, h * 0.5, "fixture-bath", roundedAttr(0.16));
      appendFixtureEllipse(group, x0 + tubW * 0.5, y0 + h * 0.14, tubW * 0.2, h * 0.045, "fixture-detail");
    } else {
      appendShower(group, x0, y0, Math.min(w * 0.5, fit * 0.7), Math.min(h * 0.42, fit * 0.7));
    }
    appendToilet(group, x0 + w - fit * 0.46, y0 + h * 0.04, fit * 0.42);
    appendBasin(group, x0 + w - w * 0.34, y0 + h - h * 0.3, w * 0.3, h * 0.24);
  }
}

function appendKitchenFixture(group, bounds) {
  const inner = insetBounds(bounds, fixturePadding(bounds) * 0.7);
  if (inner.width < 0.6 || inner.height < 0.6) {
    return;
  }
  const horizontal = inner.width >= inner.height;
  const x0 = inner.minX;
  const y0 = inner.minY;
  const w = inner.width;
  const h = inner.height;
  const depth = Math.min(Math.min(w, h) * 0.32, 0.85);

  // A run of counter along one wall carries a double sink, a hob, and a fridge.
  if (horizontal) {
    appendFixtureRect(group, x0, y0, w, depth, "fixture-counter");
    appendDoubleSink(group, x0 + w * 0.08, y0 + depth * 0.2, w * 0.17, depth * 0.6, true);
    appendHob(group, x0 + w * 0.4, y0 + depth * 0.16, Math.min(w * 0.2, depth * 1.5), depth * 0.68);
    appendFixtureRect(group, x0 + w - depth * 1.05, y0, depth, depth * 1.2, "fixture-fridge");
    appendFixtureLine(group, x0 + w - depth * 1.05, y0 + depth * 0.55, x0 + w - depth * 0.05, y0 + depth * 0.55);
  } else {
    appendFixtureRect(group, x0, y0, depth, h, "fixture-counter");
    appendDoubleSink(group, x0 + depth * 0.2, y0 + h * 0.08, depth * 0.6, h * 0.17, false);
    appendHob(group, x0 + depth * 0.16, y0 + h * 0.4, depth * 0.68, Math.min(h * 0.2, depth * 1.5));
    appendFixtureRect(group, x0, y0 + h - depth * 1.05, depth * 1.2, depth, "fixture-fridge");
    appendFixtureLine(group, x0 + depth * 0.55, y0 + h - depth * 1.05, x0 + depth * 0.55, y0 + h - depth * 0.05);
  }
}

function appendLivingFixture(group, bounds) {
  const inner = insetBounds(bounds, fixturePadding(bounds));
  if (inner.width < 0.7 || inner.height < 0.7) {
    return;
  }
  const horizontal = inner.width >= inner.height;
  const x0 = inner.minX;
  const y0 = inner.minY;
  const w = inner.width;
  const h = inner.height;

  // Anchor rug under the seating zone, then a sofa facing a media unit across a table.
  if (w * h >= 10) {
    const r = Math.min(w, h) * 0.06;
    const rw = (horizontal ? w * 0.62 : w) - r * 2;
    const rh = (horizontal ? h : h * 0.62) - r * 2;
    appendFixtureRect(group, x0 + r, y0 + r, rw, rh, "fixture-rug", roundedAttr(0.18));
  }

  // The media wall sits across the room from the sofa, which also keeps the
  // central label band clear — a fixture line through "LIVING" reads as a
  // strikethrough, not as furniture.
  if (horizontal) {
    appendSofa(group, x0 + w * 0.03, y0 + h * 0.16, w * 0.13, h * 0.66, false);
    appendFixtureRect(group, x0 + w * 0.24, y0 + h * 0.36, w * 0.12, h * 0.28, "fixture-table", roundedAttr(0.05));
    appendFixtureRect(group, x0 + w * 0.92, y0 + h * 0.2, w * 0.05, h * 0.6, "fixture-tv");
  } else {
    appendSofa(group, x0 + w * 0.16, y0 + h * 0.03, w * 0.66, h * 0.13, true);
    appendFixtureRect(group, x0 + w * 0.36, y0 + h * 0.17, w * 0.28, h * 0.12, "fixture-table", roundedAttr(0.05));
    appendFixtureRect(group, x0 + w * 0.2, y0 + h * 0.92, w * 0.6, h * 0.05, "fixture-tv");
  }
}

function appendBalconyFixture(group, bounds) {
  const inner = insetBounds(bounds, fixturePadding(bounds) * 0.7);
  const railThickness = Math.max(Math.min(inner.width, inner.height) * 0.08, 0.08);
  appendFixtureRect(group, inner.minX, inner.maxY - railThickness, inner.width, railThickness, "fixture-counter");
  appendFixtureCircle(group, inner.minX + inner.width * 0.22, inner.minY + inner.height * 0.42, Math.min(inner.width, inner.height) * 0.11, "fixture-plant");
  appendFixtureCircle(group, inner.maxX - inner.width * 0.2, inner.minY + inner.height * 0.35, Math.min(inner.width, inner.height) * 0.09, "fixture-plant");
}

function appendUtilityFixture(group, bounds) {
  const inner = insetBounds(bounds, fixturePadding(bounds));
  const size = Math.min(inner.width, inner.height) * 0.32;
  appendFixtureRect(group, inner.minX + inner.width * 0.1, inner.minY + inner.height * 0.14, size, size, "fixture-appliance");
  appendFixtureRect(group, inner.minX + inner.width * 0.1 + size * 1.18, inner.minY + inner.height * 0.14, size, size, "fixture-appliance");
  appendFixtureRect(group, inner.maxX - inner.width * 0.22, inner.minY + inner.height * 0.12, inner.width * 0.12, inner.height * 0.72, "fixture-wardrobe");
}

function appendGeneralRoomFixture(group, bounds) {
  const inner = insetBounds(bounds, fixturePadding(bounds));
  if (inner.width < 0.7 || inner.height < 0.7) {
    return;
  }
  const cx = inner.minX + inner.width * 0.5;
  const cy = inner.minY + inner.height * 0.5;
  const tw = inner.width * 0.34;
  const th = inner.height * 0.26;
  appendFixtureRect(group, cx - tw / 2, cy - th / 2, tw, th, "fixture-table", roundedAttr(0.05));
  const chairW = Math.min(tw * 0.26, 0.5);
  const chairH = Math.min(th * 0.34, 0.5);
  if (chairW > 0.16 && chairH > 0.16) {
    appendFixtureRect(group, cx - tw * 0.28, cy - th / 2 - chairH * 1.1, chairW, chairH, "fixture-chair");
    appendFixtureRect(group, cx + tw * 0.28 - chairW, cy - th / 2 - chairH * 1.1, chairW, chairH, "fixture-chair");
    appendFixtureRect(group, cx - tw * 0.28, cy + th / 2 + chairH * 0.1, chairW, chairH, "fixture-chair");
    appendFixtureRect(group, cx + tw * 0.28 - chairW, cy + th / 2 + chairH * 0.1, chairW, chairH, "fixture-chair");
  }
}

// --- Furniture sub-helpers: small composable pieces the room fixtures assemble. ---

function roundedAttr(radius) {
  return { rx: round(radius), ry: round(radius) };
}

function appendFixtureLine(group, x1, y1, x2, y2, className = "fixture-detail") {
  group.appendChild(lineEl({ x: x1, y: y1 }, { x: x2, y: y2 }, className));
}

function appendDoorSplits(group, x, y, width, height, horizontal, count) {
  for (let i = 1; i < count; i += 1) {
    if (horizontal) {
      const ly = y + (height / count) * i;
      appendFixtureLine(group, x, ly, x + width, ly);
    } else {
      const lx = x + (width / count) * i;
      appendFixtureLine(group, lx, y, lx, y + height);
    }
  }
}

function appendToilet(group, x, y, size) {
  if (size < 0.18) {
    return;
  }
  const w = size * 0.62;
  appendFixtureRect(group, x + (size - w) / 2, y, w, size * 0.3, "fixture-wc", roundedAttr(0.03));
  appendFixtureEllipse(group, x + size / 2, y + size * 0.62, size * 0.32, size * 0.34, "fixture-wc");
}

function appendBasin(group, x, y, width, height) {
  if (width < 0.2 || height < 0.16) {
    return;
  }
  appendFixtureRect(group, x, y, width, height, "fixture-counter", roundedAttr(0.04));
  appendFixtureEllipse(group, x + width / 2, y + height / 2, width * 0.32, height * 0.34, "fixture-sink");
}

function appendShower(group, x, y, width, height) {
  if (width < 0.24 || height < 0.24) {
    return;
  }
  appendFixtureRect(group, x, y, width, height, "fixture-shower", roundedAttr(0.04));
  appendFixtureLine(group, x, y, x + width, y + height);
  appendFixtureLine(group, x + width, y, x, y + height);
  appendFixtureCircle(group, x + width / 2, y + height / 2, Math.min(width, height) * 0.08, "fixture-detail");
}

function appendHob(group, x, y, width, height) {
  appendFixtureRect(group, x, y, width, height, "fixture-stove", roundedAttr(0.03));
  if (Math.min(width, height) > 0.3) {
    [0.3, 0.7].forEach((fx) => [0.3, 0.7].forEach((fy) =>
      appendFixtureCircle(group, x + width * fx, y + height * fy, Math.min(width, height) * 0.13, "fixture-burner")));
  }
}

function appendDoubleSink(group, x, y, width, height, horizontal) {
  appendFixtureRect(group, x, y, width, height, "fixture-sink", roundedAttr(0.03));
  if (horizontal && width > 0.3) {
    appendFixtureLine(group, x + width / 2, y, x + width / 2, y + height);
  } else if (!horizontal && height > 0.3) {
    appendFixtureLine(group, x, y + height / 2, x + width, y + height / 2);
  }
}

function appendSofa(group, x, y, width, height, horizontal) {
  appendFixtureRect(group, x, y, width, height, "fixture-sofa", roundedAttr(0.06));
  if (horizontal) {
    appendFixtureLine(group, x, y, x + width, y);
    appendFixtureLine(group, x + width / 3, y + height * 0.34, x + width / 3, y + height);
    appendFixtureLine(group, x + (width * 2) / 3, y + height * 0.34, x + (width * 2) / 3, y + height);
  } else {
    appendFixtureLine(group, x, y, x, y + height);
    appendFixtureLine(group, x + width * 0.34, y + height / 3, x + width, y + height / 3);
    appendFixtureLine(group, x + width * 0.34, y + (height * 2) / 3, x + width, y + (height * 2) / 3);
  }
}

function fixturePadding(bounds) {
  return Math.min(Math.max(Math.min(bounds.width, bounds.height) * 0.12, 0.14), 0.65);
}

function appendFixtureRect(group, x, y, width, height, className, attributes = {}) {
  if (width <= 0.04 || height <= 0.04) {
    return;
  }
  group.appendChild(rectEl(x, y, width, height, `fixture ${className}`, attributes));
}

function appendFixtureCircle(group, cx, cy, radius, className) {
  if (radius <= 0.03) {
    return;
  }
  group.appendChild(svgEl("circle", {
    class: `fixture ${className}`,
    cx: round(cx),
    cy: round(cy),
    r: round(radius)
  }));
}

function appendFixtureEllipse(group, cx, cy, rx, ry, className) {
  if (rx <= 0.04 || ry <= 0.04) {
    return;
  }
  group.appendChild(svgEl("ellipse", {
    class: `fixture ${className}`,
    cx: round(cx),
    cy: round(cy),
    rx: round(rx),
    ry: round(ry)
  }));
}

function renderSelectedRoomHalo(group, variant) {
  if (!state.selection || state.selection.kind !== "room" || !variant) {
    return;
  }

  const room = (variant.rooms || []).find((item) => String(item.id || "") === state.selection.id);
  const points = room && room.polygon ? room.polygon.points || [] : [];
  const bounds = roomVisualBounds(room);
  if (!room || !bounds || bounds.width <= 0 || bounds.height <= 0) {
    return;
  }

  const halo = svgEl("g", { class: "room-selected-halo-group", "aria-hidden": "true" });
  if (points.length >= 3) {
    halo.appendChild(polygonEl(points, "room-selected-halo"));
  } else {
    halo.appendChild(rectEl(bounds.minX, bounds.minY, bounds.width, bounds.height, "room-selected-halo"));
  }

  // The real, draggable resize grips are drawn by appendGeomResizeHandles in
  // edit mode; the halo stays a pure selection glow so there is exactly one
  // set of handles on screen and every visible grip actually works.
  group.appendChild(halo);
}

function roomVisualBounds(room) {
  if (!room) {
    return null;
  }

  const bounds = room.bounds || {};
  const minX = Number(bounds.minX);
  const minY = Number(bounds.minY);
  const width = Number(bounds.width);
  const height = Number(bounds.height);
  if (Number.isFinite(minX) && Number.isFinite(minY) && width > 0 && height > 0) {
    return { minX, minY, maxX: minX + width, maxY: minY + height, width, height };
  }

  return boundsOfPoints(room.polygon ? room.polygon.points || [] : []);
}

function insetBounds(bounds, amount) {
  const inset = Math.max(0, Math.min(amount, bounds.width * 0.42, bounds.height * 0.42));
  return {
    minX: bounds.minX + inset,
    minY: bounds.minY + inset,
    maxX: bounds.maxX - inset,
    maxY: bounds.maxY - inset,
    width: Math.max(bounds.width - inset * 2, 0),
    height: Math.max(bounds.height - inset * 2, 0)
  };
}

function renderWallSegment(group, wall) {
  if (!wall || !wall.centerline || !wall.centerline.start || !wall.centerline.end) {
    return;
  }

  const attributes = selectableAttributes("wall", wall.id);
  const thickness = wallStrokeWidth(wall);
  const wallClass = selectableClass(`wall wall-${safeClassToken(wall.layerType || "partition")}`, "wall", wall.id);

  // A white halo keeps the wall crisp where it abuts a dark fixture or another wall.
  group.appendChild(lineEl(wall.centerline.start, wall.centerline.end, "wall-backdrop", {
    "stroke-width": formatNumber(thickness + 0.07, 3),
    "data-wall-ref": wall.id
  }));

  // Construction-document poché: each wall is drawn as its real footprint polygon
  // (centerline offset by half its data-driven thickness, in real model metres) so it
  // carries a crisp double-line outline with a hatch-pattern fill between the faces,
  // rather than a single flat stroke. Ends are extended by half-thickness so abutting
  // walls overlap and corners read solid. Degenerate walls fall back to a stroke.
  const footprint = wallFootprint(wall, thickness);
  if (footprint) {
    group.appendChild(polygonEl(footprint, wallClass, { ...attributes, "data-wall-ref": wall.id }));
  } else {
    group.appendChild(lineEl(wall.centerline.start, wall.centerline.end, wallClass, {
      ...attributes,
      "stroke-width": formatNumber(thickness, 3),
      "data-wall-ref": wall.id
    }));
  }
  // Orientation class so edit mode can show the matching resize cursor when
  // the wall itself is grabbed as a slide handle.
  const dirX = Math.abs(Number(wall.centerline.start.x) - Number(wall.centerline.end.x));
  const dirY = Math.abs(Number(wall.centerline.start.y) - Number(wall.centerline.end.y));
  const orientation = dirX <= 0.05 && dirY > 0.05 ? "wall-hit-v" : (dirY <= 0.05 && dirX > 0.05 ? "wall-hit-h" : "");
  group.appendChild(lineEl(wall.centerline.start, wall.centerline.end, `wall-hit${orientation ? ` ${orientation}` : ""}`,
    { ...attributes, "data-wall-ref": wall.id }));
}

function wallFootprint(wall, thickness) {
  const direction = wallDirection(wall);
  if (!direction) {
    return null;
  }

  const start = wall.centerline.start;
  const end = wall.centerline.end;
  const half = thickness / 2;
  const ext = thickness / 2;
  const sx = Number(start.x) - direction.ux * ext;
  const sy = Number(start.y) - direction.uy * ext;
  const ex = Number(end.x) + direction.ux * ext;
  const ey = Number(end.y) + direction.uy * ext;
  const nx = direction.nx * half;
  const ny = direction.ny * half;
  return [
    { x: sx + nx, y: sy + ny },
    { x: ex + nx, y: ey + ny },
    { x: ex - nx, y: ey - ny },
    { x: sx - nx, y: sy - ny }
  ];
}

function renderSvgDefs() {
  const defs = svgEl("defs");
  // 45-degree poché hatch tiled in real model metres so it scales with the plan.
  // Heavy tile (denser) for demising/structure, light tile for partitions.
  defs.appendChild(buildHatchPattern("wallPocheHeavy", 0.05, 0.026));
  defs.appendChild(buildHatchPattern("wallPocheLight", 0.058, 0.018));
  // Arrowhead for circulation flow lines, sized off the stroke so it stays
  // proportionate at every zoom level.
  const arrow = svgEl("marker", {
    id: "circ-arrow",
    viewBox: "0 0 10 10",
    refX: "8",
    refY: "5",
    markerWidth: "4.2",
    markerHeight: "4.2",
    markerUnits: "strokeWidth",
    orient: "auto-start-reverse"
  });
  arrow.appendChild(svgEl("path", { d: "M 0 1 L 8 5 L 0 9 z", class: "circ-arrow-head" }));
  defs.appendChild(arrow);
  // Wet-room flooring: a quiet 45 cm tile grid in real metres, the material
  // hint a drawn plan gives bathrooms and kitchens.
  const tile = svgEl("pattern", {
    id: "wetTile",
    patternUnits: "userSpaceOnUse",
    width: "0.45",
    height: "0.45"
  });
  tile.appendChild(svgEl("path", { d: "M 0.45 0 L 0 0 L 0 0.45", class: "wet-tile-line" }));
  defs.appendChild(tile);
  els.planSvg.appendChild(defs);
}

function buildHatchPattern(id, gap, weight) {
  const pattern = svgEl("pattern", {
    id,
    patternUnits: "userSpaceOnUse",
    width: formatNumber(gap, 3),
    height: formatNumber(gap, 3),
    patternTransform: "rotate(45)"
  });
  pattern.appendChild(svgEl("line", {
    x1: 0,
    y1: 0,
    x2: 0,
    y2: formatNumber(gap, 3),
    stroke: "#1b2026",
    "stroke-width": formatNumber(weight, 3)
  }));
  return pattern;
}

function renderDoorOpening(group, door, wall, bounds) {
  if (!door || !door.location) {
    return;
  }

  const location = door.location;
  const direction = wallDirection(wall);
  const doorGroup = svgEl("g", {
    class: selectableClass("door door-opening-group", "door", door.id),
    ...selectableAttributes("door", door.id)
  });
  const tooltip = svgEl("title");
  tooltip.textContent = [
    door.id || "Door opening",
    door.hostWall ? `wall ${door.hostWall}` : "",
    Array.isArray(door.connectsSpaces) && door.connectsSpaces.length ? `connects ${door.connectsSpaces.join(", ")}` : ""
  ].filter(Boolean).join(" · ");
  doorGroup.appendChild(tooltip);

  if (!direction) {
    doorGroup.appendChild(svgEl("circle", {
      class: "door-marker",
      cx: location.x,
      cy: location.y,
      r: Math.max(Math.max(bounds.width, bounds.height) * 0.006, 0.18)
    }));
    group.appendChild(doorGroup);
    return;
  }

  const width = clamp(Number(door.width) || 0.9, 0.45, 2.4);
  const half = width / 2;
  const swing = clamp(width, 0.6, 1.1);
  const swingSign = doorSwingSign(door, direction, bounds);
  const p1 = { x: location.x - direction.ux * half, y: location.y - direction.uy * half };
  const p2 = { x: location.x + direction.ux * half, y: location.y + direction.uy * half };
  const open = {
    x: p1.x + direction.nx * swing * swingSign,
    y: p1.y + direction.ny * swing * swingSign
  };
  const gapWidth = Math.max(wallStrokeWidth(wall) * 1.85, 0.22);

  doorGroup.appendChild(lineEl(p1, p2, "door-gap", { "stroke-width": formatNumber(gapWidth, 3) }));
  doorGroup.appendChild(lineEl(p1, open, "door-leaf"));
  doorGroup.appendChild(svgEl("path", {
    class: "door-swing",
    d: [
      `M ${formatNumber(p2.x, 3)} ${formatNumber(p2.y, 3)}`,
      `A ${formatNumber(swing, 3)} ${formatNumber(swing, 3)} 0 0 ${swingSign > 0 ? 1 : 0}`,
      `${formatNumber(open.x, 3)} ${formatNumber(open.y, 3)}`
    ].join(" ")
  }));
  group.appendChild(doorGroup);
}

function wallDirection(wall) {
  if (!wall || !wall.centerline || !wall.centerline.start || !wall.centerline.end) {
    return null;
  }

  const start = wall.centerline.start;
  const end = wall.centerline.end;
  const dx = Number(end.x) - Number(start.x);
  const dy = Number(end.y) - Number(start.y);
  const length = Math.sqrt(dx * dx + dy * dy);
  if (!Number.isFinite(length) || length <= 0) {
    return null;
  }

  const ux = dx / length;
  const uy = dy / length;
  return { ux, uy, nx: -uy, ny: ux };
}

function doorSwingSign(door, direction, bounds) {
  if (!bounds || !door || !door.location || !direction) {
    return 1;
  }

  const centerX = bounds.minX + bounds.width / 2;
  const centerY = bounds.minY + bounds.height / 2;
  const dot = (centerX - door.location.x) * direction.nx + (centerY - door.location.y) * direction.ny;
  return dot >= 0 ? 1 : -1;
}

function wallStrokeWidth(wall) {
  const thickness = Number(wall && wall.thickness);
  return clamp(Number.isFinite(thickness) ? thickness : 0.12, 0.08, 0.34);
}

function renderRoomLabels(variant) {
  if (!state.labelsVisible || state.viewMode !== "plan") {
    return;
  }

  const densePlan = countOf(variant.rooms) > 14;
  (variant.rooms || []).forEach((room) => {
    const points = room.polygon ? room.polygon.points || [] : [];
    const bounds = roomVisualBounds(room) || boundsOfPoints(points);
    if (!shouldShowRoomLabel(room, bounds, densePlan)) {
      return;
    }

    const centerX = bounds.minX + bounds.width / 2;
    const centerY = bounds.minY + bounds.height / 2;
    const fontSize = roomLabelFontSize(bounds, densePlan);
    const text = svgEl("text", {
      class: "svg-label room-label",
      "data-room-ref": String(room.id || ""),
      x: round(centerX),
      y: round(-centerY + fontSize * 0.34),
      "text-anchor": "middle",
      "font-size": formatNumber(fontSize, 2)
    });
    const title = svgEl("tspan", { x: round(centerX), dy: 0 });
    title.textContent = densePlan ? compactRoomLabel(room) : planRoomLabelName(room);
    text.appendChild(title);
    const minSpan = Math.min(bounds.width, bounds.height);
    // Small wet rooms keep only their name: a dimension line under "KITCHEN"
    // in a 2.7 m room collides with the counter fixtures and reads as noise.
    const showMeta = densePlan ? minSpan >= 2.1 : minSpan >= 2.8;
    if (showMeta) {
      const meta = svgEl("tspan", {
        class: "room-label-meta",
        x: round(centerX),
        dy: formatNumber(fontSize * 1.3, 2)
      });
      const areaValue = Number(room && room.area);
      // Dense plans get a compact area line (e.g. "6.5 m2"); sparse plans get the
      // fuller W x D dimension string, which has room to breathe.
      meta.textContent = densePlan && Number.isFinite(areaValue) && areaValue > 0
        ? `${formatNumber(areaValue, 1)} m²`
        : roomDimensionText(room, bounds, 2);
      text.appendChild(meta);
    }
    els.planSvg.appendChild(text);
  });
}

function shouldShowRoomLabel(room, bounds, densePlan) {
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
    return false;
  }

  const minDimension = Math.min(bounds.width, bounds.height);
  const area = Number(room && room.area) || bounds.width * bounds.height;
  if (densePlan) {
    // A construction document labels every habitable room; only skip closets and
    // slivers too small to seat even a compact tag without colliding with walls.
    return minDimension >= 1.7 && area >= 3.2;
  }

  return minDimension >= 1.9 && area >= 4.5;
}

function roomLabelFontSize(bounds, densePlan) {
  const minDimension = Math.min(bounds.width, bounds.height);
  const max = Math.min(densePlan ? 0.46 : 0.6, maxPlanLabelFontSize);
  const min = densePlan ? 0.32 : 0.38;
  return clamp(minDimension * 0.15, min, max);
}

function compactRoomLabel(room) {
  const type = String(room && (room.roomType || room.type || "")).toLowerCase();
  if (type.includes("bed")) {
    return "BED";
  }
  if (type.includes("bath") || type.includes("wc")) {
    return "BATH";
  }
  if (type.includes("kitchen")) {
    return "KIT";
  }
  if (type.includes("living") || type.includes("dining")) {
    return "LIV";
  }
  if (type.includes("balcony") || type.includes("terrace")) {
    return "BAL";
  }
  if (type.includes("utility") || type.includes("laundry")) {
    return "UTIL";
  }
  if (type.includes("storage")) {
    return "STO";
  }
  return "ROOM";
}

function displayRoomType(room) {
  return displayUnitType(room && (room.roomType || room.type || "room"));
}

function planRoomLabelName(room) {
  const type = displayRoomType(room);
  return String(type || compactRoomLabel(room)).toUpperCase();
}

function roomDimensionText(room, bounds, digits = 1) {
  const dimensions = room && room.dimensions ? room.dimensions : {};
  const width = Number(dimensions.width) || (bounds ? bounds.width : 0);
  const depth = Number(dimensions.depth) || (bounds ? bounds.height : 0);
  if (width > 0 && depth > 0) {
    return `${formatNumber(width, digits)} × ${formatNumber(depth, digits)}`;
  }

  const area = Number(room && room.area);
  return Number.isFinite(area) ? `${formatNumber(area, 1)} m²` : "";
}

function roomCategoryClass(room) {
  const type = String(room && (room.roomType || room.type || "")).toLowerCase();
  if (type.includes("bed")) {
    return "room-bedroom";
  }
  if (type.includes("bath") || type.includes("wc")) {
    return "room-bathroom";
  }
  if (type.includes("kitchen")) {
    return "room-kitchen";
  }
  if (type.includes("living") || type.includes("dining")) {
    return "room-living";
  }
  if (type.includes("balcony") || type.includes("terrace")) {
    return "room-balcony";
  }
  if (type.includes("utility") || type.includes("storage") || type.includes("store") ||
    type.includes("laundry") || type.includes("pooja") || type.includes("puja") ||
    type.includes("prayer") || type.includes("foyer")) {
    return "room-service";
  }
  return "room-general";
}

function safeClassToken(value) {
  return String(value || "item").toLowerCase().replace(/[^a-z0-9_-]+/g, "-");
}

function setViewMode(mode) {
  if (!["plan", "axon", "circulation", "model3d"].includes(mode)) {
    return;
  }

  state.viewMode = mode;
  state.zoom = 1;
  state.panX = 0;
  state.panY = 0;
  if (mode === "axon" || mode === "model3d") {
    state.editMode = false;
    state.dragEdit = null;
  }
  state.editReadout = "";
  renderAll();
  setStatus(`${viewModeLabel(mode)} view`);
}

function handleCanvasAction(action) {
  if (action === "edit-toggle") {
    if (state.viewMode === "axon" || state.viewMode === "model3d") {
      state.viewMode = "plan";
    }
    state.editMode = !state.editMode;
    state.dragEdit = null;
    // The user's selection survives the mode switch — entering edit mode to
    // resize the room you just picked must not yank focus to the floorplate.
    state.editReadout = state.editMode ? editSummary(state.input) : "";
    renderAll();
    setStatus(state.editMode
      ? "Edit mode — drag rooms, walls, or grips"
      : "Plan review mode");
    return;
  }

  if (action === "labels-toggle") {
    state.labelsVisible = !state.labelsVisible;
    renderAll();
    setStatus(state.labelsVisible ? "Room labels on" : "Room labels off");
    return;
  }

  if (action === "grid-toggle") {
    state.gridVisible = !state.gridVisible;
    renderAll();
    setStatus(state.gridVisible ? "Reference grid on" : "Reference grid off");
    return;
  }

  if (action === "fullscreen") {
    toggleFullscreen();
    return;
  }

  if (action === "undo") {
    undoEdit();
    return;
  }

  if (action === "redo") {
    redoEdit();
    return;
  }

  if (!els.planSvg.getAttribute("viewBox")) {
    setStatus("Generate a plan before using canvas tools");
    return;
  }

  if (action === "select") {
    state.canvasTool = "select";
    state.editMode = false;
    state.dragEdit = null;
    state.editReadout = "";
    renderAll();
    setStatus("Select tool");
    return;
  }

  if (action === "pan") {
    state.canvasTool = "pan";
    state.editMode = false;
    state.dragEdit = null;
    state.editReadout = "";
    renderAll();
    setStatus("Pan tool — drag to move the view");
    return;
  }

  if (action === "zoom-in") {
    state.zoom = clamp(state.zoom * 1.25, 1, maxZoom);
  } else if (action === "zoom-out") {
    state.zoom = clamp(state.zoom / 1.25, 1, maxZoom);
  } else if (action === "fit") {
    state.zoom = 1;
    state.panX = 0;
    state.panY = 0;
  } else {
    return;
  }

  renderAll();
  setStatus(action === "fit" ? "Fit view" : `Zoom ${formatNumber(state.zoom, 2)}x`);
}

function updateModeButtons() {
  els.modeButtons.forEach((button) => {
    const active = button.dataset.viewMode === state.viewMode;
    button.classList.toggle("active", active);
    button.setAttribute("aria-pressed", active ? "true" : "false");
  });
}

function selectableAttributes(kind, id) {
  return {
    "data-select-kind": kind,
    "data-select-id": id == null ? "" : String(id)
  };
}

function selectableClass(baseClass, kind, id) {
  return `${baseClass}${isSelection(kind, id) ? " selected-element" : ""}`;
}

function isSelection(kind, id) {
  return Boolean(state.selection && state.selection.kind === kind && state.selection.id === String(id || ""));
}

function handlePlanClick(event) {
  if (state.suppressNextPlanClick) {
    state.suppressNextPlanClick = false;
    return;
  }
  const planAction = event.target.closest ? event.target.closest("[data-plan-action]") : null;
  if (planAction) {
    event.preventDefault();
    event.stopPropagation();
    runSelectedPlanAction(planAction.dataset.planAction);
    return;
  }

  // Pure handles swallow clicks, but elements that are BOTH a handle and a
  // selectable (the core polygon in edit mode) still select on a plain click.
  if (event.target.closest && event.target.closest("[data-edit-action]")
    && !event.target.closest("[data-select-kind]")) {
    return;
  }

  const target = event.target.closest ? event.target.closest("[data-select-kind]") : null;
  if (!target) {
    if (event.target === els.planSvg) {
      state.selection = null;
      renderAll();
    }
    return;
  }

  state.selection = {
    kind: target.dataset.selectKind,
    id: target.dataset.selectId || ""
  };
  setStatus(`${selectionKindLabel(state.selection.kind)} selected`);
  renderAll();
}

// Double-click an element to zoom to it; double-click empty canvas to fit all.
function handlePlanDoubleClick(event) {
  if (state.viewMode === "axon" || !state.viewFrame) {
    return;
  }
  const target = event.target.closest ? event.target.closest("[data-select-kind]") : null;
  if (!target) {
    state.zoom = 1;
    state.panX = 0;
    state.panY = 0;
    renderAll();
    setStatus("Fit view");
    return;
  }
  state.selection = { kind: target.dataset.selectKind, id: target.dataset.selectId || "" };
  const detail = selectedElementDetails(currentVisualOutput());
  if (detail && detail.bounds && detail.bounds.width > 0 && detail.bounds.height > 0) {
    zoomToBounds(detail.bounds);
    setStatus(`Zoomed to ${selectionKindLabel(state.selection.kind).toLowerCase()}`);
  }
}

function zoomToBounds(bounds) {
  const frame = state.viewFrame;
  if (!frame || !bounds) {
    return;
  }
  state.zoom = clamp(
    Math.min(frame.width / (bounds.width * 3.2), frame.height / (bounds.height * 3.2)),
    1,
    maxZoom);
  // viewFrame lives in flipped (SVG) space, so the target centre's Y negates.
  state.panX = (bounds.minX + bounds.width / 2) - frame.centerX;
  state.panY = -(bounds.minY + bounds.height / 2) - frame.centerY;
  renderAll();
}

function handlePlanActionKeyDown(event) {
  if (event.key !== "Enter" && event.key !== " ") {
    return;
  }

  const planAction = event.target.closest ? event.target.closest("[data-plan-action]") : null;
  if (!planAction) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  runSelectedPlanAction(planAction.dataset.planAction);
}

function setCanvasButtonState(action, active) {
  const button = els.canvasButtons.find((candidate) => candidate.dataset.canvasAction === action);
  if (!button) {
    return;
  }
  button.classList.toggle("active", Boolean(active));
  button.setAttribute("aria-pressed", active ? "true" : "false");
}

function setCanvasButtonDisabled(action, disabled) {
  const button = els.canvasButtons.find((candidate) => candidate.dataset.canvasAction === action);
  if (!button) {
    return;
  }
  // Moving focus off a button before disabling it keeps a keyboard user anchored in
  // the toolbar instead of dropping focus to <body> (e.g. the last Undo emptying the stack).
  if (disabled && document.activeElement === button) {
    const fallback = els.canvasButtons.find((candidate) => candidate !== button && !candidate.disabled);
    if (fallback) {
      fallback.focus();
    } else {
      button.blur();
    }
  }
  button.disabled = Boolean(disabled);
  button.classList.toggle("is-disabled", Boolean(disabled));
}

function toggleFullscreen() {
  const el = els.previewFrame;
  if (!el) {
    return;
  }
  const current = document.fullscreenElement || document.webkitFullscreenElement;
  if (current) {
    const exit = document.exitFullscreen || document.webkitExitFullscreen;
    if (exit) {
      exit.call(document);
    }
    return;
  }
  const request = el.requestFullscreen || el.webkitRequestFullscreen;
  if (!request) {
    setStatus("Full screen is not supported in this browser");
    return;
  }
  const result = request.call(el);
  if (result && typeof result.catch === "function") {
    result.catch(() => setStatus("Full screen request was blocked by the browser"));
  }
}

function handleFullscreenChange() {
  const current = document.fullscreenElement || document.webkitFullscreenElement;
  state.isFullscreen = Boolean(current && current === els.previewFrame);
  // The canvas just changed size; recompute the aspect-matched viewBox and chrome.
  renderAll();
  setStatus(state.isFullscreen ? "Full screen editor" : "Exited full screen");
}

function zoomAround(factor, clientX, clientY) {
  if (!els.planSvg.getAttribute("viewBox") || !state.viewFrame) {
    return;
  }
  const rect = els.planSvg.getBoundingClientRect();
  if (!rect.width || !rect.height) {
    return;
  }
  const newZoom = clamp(state.zoom * factor, 1, maxZoom);
  if (Math.abs(newZoom - state.zoom) < 1e-4) {
    return;
  }
  const frame = state.viewFrame;
  const curW = frame.width / state.zoom;
  const curH = frame.height / state.zoom;
  const curX = frame.centerX + (Number(state.panX) || 0) - curW / 2;
  const curY = frame.centerY + (Number(state.panY) || 0) - curH / 2;
  const fx = clamp((clientX - rect.left) / rect.width, 0, 1);
  const fy = clamp((clientY - rect.top) / rect.height, 0, 1);
  const anchorX = curX + fx * curW;
  const anchorY = curY + fy * curH;
  const newW = frame.width / newZoom;
  const newH = frame.height / newZoom;
  state.zoom = newZoom;
  state.panX = (anchorX - fx * newW) + newW / 2 - frame.centerX;
  state.panY = (anchorY - fy * newH) + newH / 2 - frame.centerY;
  renderAll();
}

function handleCanvasWheel(event) {
  if (state.viewMode === "axon" || !els.planSvg.getAttribute("viewBox")) {
    return;
  }
  event.preventDefault();
  const factor = event.deltaY < 0 ? 1.12 : 1 / 1.12;
  zoomAround(factor, event.clientX, event.clientY);
}

function canvasPanAllowed(event) {
  if (state.viewMode === "axon" || !els.planSvg.getAttribute("viewBox")) {
    return false;
  }
  if (event.target.closest && event.target.closest("[data-edit-action]")) {
    return false;
  }
  // In edit mode, pressing on a room/unit or wall grabs it to edit — never pan
  // from there.
  if (state.editMode && event.target.closest
    && event.target.closest('[data-select-kind="room"],[data-select-kind="unit"],[data-wall-ref]')) {
    return false;
  }
  const toolPan = state.canvasTool === "pan" && !state.editMode;
  // Dragging truly empty canvas (the SVG itself, no element under the pointer)
  // pans in any tool — the universal "grab the paper" gesture. A plain click
  // still lands as deselect because zero-movement pans don't swallow clicks.
  const emptyDrag = event.target === els.planSvg && (!event.button || event.button === 0);
  return toolPan || emptyDrag || state.spaceHeld || event.button === 1;
}

function handleCanvasPanDown(event) {
  if (!canvasPanAllowed(event)) {
    return;
  }
  const rect = els.planSvg.getBoundingClientRect();
  const vb = (els.planSvg.getAttribute("viewBox") || "").split(/\s+/).map(Number);
  if (vb.length !== 4 || !rect.width || !rect.height) {
    return;
  }
  event.preventDefault();
  state.panDrag = {
    clientX: event.clientX,
    clientY: event.clientY,
    panX: Number(state.panX) || 0,
    panY: Number(state.panY) || 0,
    scaleX: vb[2] / rect.width,
    scaleY: vb[3] / rect.height,
    viewW: vb[2],
    viewH: vb[3]
  };
  try {
    els.planSvg.setPointerCapture(event.pointerId);
  } catch (_) {
    // Pointer capture is a smoothness nicety, not a requirement.
  }
  els.previewFrame.classList.add("is-panning");
}

function handleCanvasPanMove(event) {
  const drag = state.panDrag;
  if (!drag || !state.viewFrame) {
    return;
  }
  event.preventDefault();
  if (Math.abs(event.clientX - drag.clientX) > 3 || Math.abs(event.clientY - drag.clientY) > 3) {
    drag.moved = true;
  }
  const frame = state.viewFrame;
  const limitX = Math.max((frame.width - drag.viewW) / 2, 0);
  const limitY = Math.max((frame.height - drag.viewH) / 2, 0);
  const panX = clamp(drag.panX - (event.clientX - drag.clientX) * drag.scaleX, -limitX, limitX);
  const panY = clamp(drag.panY - (event.clientY - drag.clientY) * drag.scaleY, -limitY, limitY);
  state.panX = panX;
  state.panY = panY;
  // Re-aim the viewBox directly for a smooth drag without a full canvas re-render.
  const vx = frame.centerX + panX - drag.viewW / 2;
  const vy = frame.centerY + panY - drag.viewH / 2;
  els.planSvg.setAttribute("viewBox", `${formatNumber(vx, 3)} ${formatNumber(vy, 3)} `
    + `${formatNumber(drag.viewW, 3)} ${formatNumber(drag.viewH, 3)}`);
}

function handleCanvasPanUp() {
  if (!state.panDrag) {
    return;
  }
  // An actual pan must not double as a click — releasing after a drag on empty
  // canvas should keep the current selection, not clear it.
  if (state.panDrag.moved) {
    state.suppressNextPlanClick = true;
  }
  state.panDrag = null;
  els.previewFrame.classList.remove("is-panning");
  renderAll();
}

function isTextEntryTarget(target) {
  if (!target || !target.tagName) {
    return false;
  }
  const tag = target.tagName.toLowerCase();
  return tag === "input" || tag === "textarea" || tag === "select" || Boolean(target.isContentEditable);
}

function isInteractiveTarget(target) {
  if (isTextEntryTarget(target)) {
    return true;
  }
  const tag = target && target.tagName ? target.tagName.toLowerCase() : "";
  return tag === "button" || tag === "a";
}

const canvasKeyShortcuts = {
  "+": "zoom-in",
  "=": "zoom-in",
  "-": "zoom-out",
  _: "zoom-out",
  "0": "fit",
  f: "fullscreen",
  g: "grid-toggle",
  e: "edit-toggle",
  l: "labels-toggle",
  v: "select",
  h: "pan"
};

function handleEditorKeyDown(event) {
  // The 3D model viewport owns the keyboard while it is open (WASD walking,
  // Esc) — the SVG canvas shortcuts would fight the walkthrough controls.
  if (state.viewMode === "model3d") {
    return;
  }
  // The plan IS the default view, so canvas shortcuts must work for an empty
  // or foreign hash too — normalize instead of demanding exactly "#plan".
  const onPlan = normalizeNavHash(window.location.hash) === "#plan";
  if (event.code === "Space") {
    if (onPlan && !isInteractiveTarget(event.target)) {
      if (!event.repeat) {
        state.spaceHeld = true;
        els.previewFrame.classList.add("space-pan");
      }
      event.preventDefault();
    }
    return;
  }

  if (isTextEntryTarget(event.target) || !onPlan) {
    return;
  }

  if (event.ctrlKey || event.metaKey) {
    const key = event.key.toLowerCase();
    if (key === "z") {
      event.preventDefault();
      if (event.shiftKey) {
        redoEdit();
      } else {
        undoEdit();
      }
    } else if (key === "y") {
      event.preventDefault();
      redoEdit();
    }
    return;
  }

  const action = canvasKeyShortcuts[event.key.toLowerCase()] || canvasKeyShortcuts[event.key];
  if (action) {
    event.preventDefault();
    handleCanvasAction(action);
    return;
  }

  if (event.key === "Escape") {
    // Escape first abandons an in-flight drag (no commit, geometry restored),
    // then — on a second press — clears the selection. CAD muscle memory.
    if (state.geomDrag || state.wallDrag) {
      event.preventDefault();
      cancelActiveDrag();
      return;
    }
    if (state.selection) {
      state.selection = null;
      renderAll();
      return;
    }
    return;
  }

  const arrows = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, 1], ArrowDown: [0, -1] };
  // In edit mode, arrows nudge the selected room/unit on the snap grid
  // (Shift = 0.5 m steps); otherwise they pan a zoomed view.
  if (arrows[event.key] && state.editMode && state.selection
    && (state.selection.kind === "room" || state.selection.kind === "unit")) {
    event.preventDefault();
    nudgeSelection(arrows[event.key], event.shiftKey ? 0.5 : geomSnapStep, !event.repeat);
    return;
  }
  if (arrows[event.key] && state.zoom > 1 && state.viewFrame) {
    event.preventDefault();
    const step = (state.viewFrame.width / state.zoom) * 0.12;
    // Screen-down is model-up: panY is stored in flipped (SVG) space.
    state.panX = (Number(state.panX) || 0) + arrows[event.key][0] * step;
    state.panY = (Number(state.panY) || 0) - arrows[event.key][1] * step;
    renderAll();
  }
}

function nudgeSelection(direction, step, recordUndo) {
  // Arrow nudges run through the same boundary engine as pointer drags: the
  // nudged element pushes and pulls its wall planes, neighbours follow, and
  // nothing can overlap or tear.
  const variant = selectedVariant(currentVisualOutput());
  if (!variant || !variant.variantId || !state.selection) {
    return;
  }
  const kind = state.selection.kind;
  const id = String(state.selection.id || "");
  const list = kind === "unit" ? variant.units : variant.rooms;
  const item = (list || []).find((entry) => String(entry.id || "") === id);
  const points = (item && item.polygon ? item.polygon.points || [] : [])
    .map((p) => ({ x: Number(p.x), y: Number(p.y) }));
  const bounds = boundsOfPoints(points);
  if (points.length < 3 || !bounds) {
    return;
  }
  const floorBounds = state.input && state.input.floorplate && state.input.floorplate.outer
    ? boundsOfPoints(state.input.floorplate.outer.points)
    : null;
  const childRooms = kind === "unit"
    ? (variant.rooms || [])
        .filter((room) => String(room.unitId || "") === id)
        .map((room) => ({
          id: String(room.id || ""),
          points: (room.polygon ? room.polygon.points || [] : []).map((p) => ({ x: Number(p.x), y: Number(p.y) }))
        }))
    : [];
  const edges = collectBoundaryEdges(variant, kind, id, bounds, childRooms, floorBounds);
  const raw = translateBoundsWithin(bounds, direction[0] * step, direction[1] * step, floorBounds);
  const next = clampBoundsToEdges(raw, bounds, edges, "move");
  const edgeDeltas = boundaryEdgeDeltas(edges, bounds, next);
  if (!edgeDeltas.length) {
    return;
  }
  if (recordUndo) {
    pushUndoSnapshot(captureSnapshot());
  }
  setGeometryOverride(variant.variantId, kind, id, remapPolygon(points, bounds, next));
  childRooms.forEach((child) => {
    if (child.points.length >= 3) {
      setGeometryOverride(variant.variantId, "room", child.id, remapPolygon(child.points, bounds, next));
    }
  });
  followersForDeltas(edgeDeltas).forEach((follower) => {
    setGeometryOverride(variant.variantId, follower.kind, follower.id, shiftFollowerPoints(follower, edgeDeltas));
  });
  // Walls and doors ride the nudge through the same committed-override path
  // as pointer drags.
  commitWallDoorOverrides(
    variant.variantId,
    collectLiveWallsForEdges(variant, bounds, edges),
    variant.doorsOpenings || [],
    buildEditPointMapper(bounds, next, edgeDeltas));
  state.editsSignature = state.lastRunInputSignature;
  saveDraft();
  renderAll();
  setStatus(`${selectionKindLabel(kind)} @ ${formatNumber(next.minX, 1)}, ${formatNumber(next.minY, 1)} m`);
}

function handleEditorKeyUp(event) {
  if (event.code === "Space") {
    state.spaceHeld = false;
    els.previewFrame.classList.remove("space-pan");
  }
}

// A history entry captures both worlds the user can edit: the engine input
// (floorplate/core/rules) and the manual geometry overlay (resized rooms/units),
// so a single Undo restores whichever one the last action touched.
function captureSnapshot() {
  return { input: clone(state.input), geometry: clone(state.geometryEdits) };
}

function pushUndoSnapshot(snapshot) {
  if (!snapshot || !snapshot.input) {
    return;
  }
  state.undoStack.push(snapshot);
  if (state.undoStack.length > undoStackLimit) {
    state.undoStack.shift();
  }
  state.redoStack = [];
}

function undoEdit() {
  if (!state.undoStack.length) {
    setStatus("Nothing to undo");
    return;
  }
  state.redoStack.push(captureSnapshot());
  applySnapshot(state.undoStack.pop(), "Undid last edit");
}

function redoEdit() {
  if (!state.redoStack.length) {
    setStatus("Nothing to redo");
    return;
  }
  state.undoStack.push(captureSnapshot());
  applySnapshot(state.redoStack.pop(), "Redid edit");
}

function applySnapshot(snapshot, label) {
  if (!snapshot || !snapshot.input) {
    return;
  }
  // Only re-run the engine when the restored state changes the input; a pure
  // geometry-overlay undo must NOT regenerate, or it would wipe the very
  // overrides it just restored.
  const inputChanged = JSON.stringify(state.input) !== JSON.stringify(snapshot.input);
  state.input = clone(snapshot.input);
  state.geometryEdits = clone(snapshot.geometry || {});
  syncFormFromInput(state.input);
  setEditorFromInput(state.input);
  saveDraft();
  state.editReadout = state.editMode ? editSummary(state.input) : "";
  if (inputChanged) {
    markInputDirty(label, 120);
  }
  renderAll();
  setStatus(label);
}

function renderPlanGrid(group, bounds) {
  if (!state.gridVisible || state.viewMode === "axon" || !bounds) {
    return;
  }
  const minor = 1;
  const major = 5;
  const layer = svgEl("g", { class: "plan-grid" });
  const startX = Math.floor(bounds.minX / minor) * minor;
  const endX = Math.ceil(bounds.maxX / minor) * minor;
  const startY = Math.floor(bounds.minY / minor) * minor;
  const endY = Math.ceil(bounds.maxY / minor) * minor;
  for (let x = startX; x <= endX + 1e-6; x += minor) {
    const isMajor = Math.abs(x / major - Math.round(x / major)) < 1e-6;
    layer.appendChild(lineEl({ x, y: bounds.minY }, { x, y: bounds.maxY }, isMajor ? "grid-line grid-line-major" : "grid-line"));
  }
  for (let y = startY; y <= endY + 1e-6; y += minor) {
    const isMajor = Math.abs(y / major - Math.round(y / major)) < 1e-6;
    layer.appendChild(lineEl({ x: bounds.minX, y }, { x: bounds.maxX, y }, isMajor ? "grid-line grid-line-major" : "grid-line"));
  }
  group.appendChild(layer);
}

function updateCanvasUi(output) {
  const editActive = Boolean(state.editMode && state.viewMode !== "axon");
  const editToggle = els.canvasButtons.find((button) => button.dataset.canvasAction === "edit-toggle");
  if (editToggle) {
    editToggle.classList.toggle("active", editActive);
    editToggle.setAttribute("aria-pressed", editActive ? "true" : "false");
  }

  const labelsActive = Boolean(state.labelsVisible);
  const labelsToggle = els.canvasButtons.find((button) => button.dataset.canvasAction === "labels-toggle");
  if (labelsToggle) {
    labelsToggle.classList.toggle("active", labelsActive);
    labelsToggle.setAttribute("aria-pressed", labelsActive ? "true" : "false");
  }

  setCanvasButtonState("grid-toggle", state.gridVisible);
  setCanvasButtonState("fullscreen", state.isFullscreen);
  setCanvasButtonState("select", state.canvasTool === "select" && !editActive);
  setCanvasButtonState("pan", state.canvasTool === "pan" && !editActive);
  setCanvasButtonDisabled("undo", state.undoStack.length === 0);
  setCanvasButtonDisabled("redo", state.redoStack.length === 0);

  els.previewFrame.classList.toggle("is-edit-mode", editActive);
  els.previewFrame.classList.toggle("labels-on", labelsActive);
  els.previewFrame.classList.toggle("grid-on", Boolean(state.gridVisible));
  els.previewFrame.classList.toggle("is-fullscreen", Boolean(state.isFullscreen));
  els.previewFrame.dataset.canvasTool = state.canvasTool;
  if (els.zoomLevel) {
    els.zoomLevel.textContent = `${Math.round(clamp(state.zoom, 1, maxZoom) * 100)}%`;
  }
  const detail = selectedElementDetails(output);
  const selectedSummary = selectionInlineSummary(detail);
  // Outside edit mode a selected room advertises the next step, so the path
  // from "I picked a bedroom" to "I'm resizing it" is written on screen. In
  // edit mode the active selection outranks the floor/core summary.
  const editHint = !editActive && detail && (detail.kind === "room" || detail.kind === "unit")
    ? " — press E to edit"
    : "";
  const text = editActive
    ? (selectedSummary || state.editReadout || editSummary(state.input))
    : selectedSummary + (selectedSummary ? editHint : "");
  els.editReadout.textContent = text || "";
  els.editReadout.hidden = !text;
}

function selectionKindLabel(kind) {
  switch (kind) {
    case "floorplate":
      return "Floorplate";
    case "core":
      return "Core";
    case "fixed":
      return "Fixed element";
    case "unit":
      return "Unit";
    case "room":
      return "Room";
    case "corridor":
      return "Corridor";
    case "wall":
      return "Wall";
    case "door":
      return "Door";
    default:
      return "Plan element";
  }
}

function renderSelectionInspector(output) {
  const detail = selectedElementDetails(output);
  if (!detail) {
    els.selectionInspector.innerHTML = `
      <div class="empty-list">Select a plan element to inspect the inputs that drive it.</div>
    `;
    return;
  }

  els.selectionInspector.innerHTML = inspectorMarkup(detail);
}

function selectedElementDetails(output) {
  if (!state.selection) {
    return null;
  }

  const selection = state.selection;
  const input = state.input ? ensureInputShape(state.input) : null;
  const variant = selectedVariant(output);

  if (selection.kind === "floorplate" && input && input.floorplate && input.floorplate.outer) {
    const points = input.floorplate.outer.points || [];
    const bounds = boundsOfPoints(points);
    return {
      kind: "floorplate",
      id: "floorplate",
      title: "Floorplate",
      item: input.floorplate.outer,
      points,
      bounds,
      area: polygonArea(points),
      source: "input",
      diagnostics: diagnosticsForElement(output, ["floorplate"])
    };
  }

  if ((selection.kind === "core" || selection.kind === "fixed") && input && Array.isArray(input.fixedElements)) {
    const fixed = input.fixedElements.find((item) => String(item.id || "fixed") === selection.id)
      || input.fixedElements.find((item) => String(item.type || "").toLowerCase() === selection.kind);
    if (fixed && fixed.polygon) {
      const points = fixed.polygon.points || [];
      return {
        kind: String(fixed.type || selection.kind).toLowerCase() === "core" ? "core" : "fixed",
        id: fixed.id || "fixed",
        title: fixed.type || "Fixed element",
        item: fixed,
        points,
        bounds: boundsOfPoints(points),
        area: polygonArea(points),
        source: "input",
        diagnostics: diagnosticsForElement(output, [fixed.id || selection.id || "fixed"])
      };
    }
  }

  if (!variant) {
    return null;
  }

  if (selection.kind === "unit") {
    const unit = (variant.units || []).find((item) => String(item.id || "") === selection.id);
    if (unit) {
      const points = unit.polygon ? unit.polygon.points || [] : [];
      return {
        kind: "unit",
        id: unit.id,
        title: `${displayUnitType(unit.type)} unit`,
        item: unit,
        points,
        bounds: boundsOfPoints(points),
        area: Number(unit.area) || polygonArea(points),
        source: "generated",
        relationships: relationshipsForElement(variant, "unit", unit.id, unit),
        diagnostics: diagnosticsForElement(output, [unit.id, unit.externalId])
      };
    }
  }

  if (selection.kind === "room") {
    const room = (variant.rooms || []).find((item) => String(item.id || "") === selection.id);
    if (room) {
      const points = room.polygon ? room.polygon.points || [] : [];
      const parentUnit = unitForRoom(variant, room);
      return {
        kind: "room",
        id: room.id,
        title: displayRoomType(room),
        item: room,
        unit: parentUnit,
        points,
        bounds: boundsOfPoints(points),
        area: Number(room.area) || polygonArea(points),
        source: "generated",
        relationships: relationshipsForElement(variant, "room", room.id, room),
        diagnostics: diagnosticsForElement(output, [room.id, room.unitId, room.externalId])
      };
    }
  }

  if (selection.kind === "corridor") {
    const corridor = (variant.corridors || []).find((item) => String(item.id || "") === selection.id);
    if (corridor) {
      const points = corridor.polygon ? corridor.polygon.points || [] : [];
      const bounds = boundsOfPoints(points);
      return {
        kind: "corridor",
        id: corridor.id,
        title: "Corridor",
        item: corridor,
        points,
        bounds,
        area: Number(corridor.area) || polygonArea(points),
        source: "generated",
        relationships: relationshipsForElement(variant, "corridor", corridor.id, corridor),
        diagnostics: diagnosticsForElement(output, [corridor.id, corridor.externalId])
      };
    }
  }

  if (selection.kind === "wall") {
    const wall = (variant.walls || []).find((item) => String(item.id || "") === selection.id);
    if (wall) {
      return {
        kind: "wall",
        id: wall.id,
        title: `${displayUnitType(wall.layerType || "wall")} ${wall.id}`,
        item: wall,
        length: lineLength(wall.centerline),
        source: "generated",
        relationships: relationshipsForElement(variant, "wall", wall.id, wall),
        diagnostics: diagnosticsForElement(output, [wall.id, wall.externalId])
      };
    }
  }

  if (selection.kind === "door") {
    const door = (variant.doorsOpenings || []).find((item) => String(item.id || "") === selection.id);
    if (door) {
      return {
        kind: "door",
        id: door.id,
        title: `${displayUnitType(door.type || "door")} ${door.id}`,
        item: door,
        source: "generated",
        relationships: relationshipsForElement(variant, "door", door.id, door),
        diagnostics: diagnosticsForElement(output, [door.id, door.hostWall, door.externalId])
      };
    }
  }

  return null;
}

function unitForRoom(variant, room) {
  if (!variant || !room) {
    return null;
  }

  const roomId = String(room.id || "");
  const roomUnitId = String(room.unitId || "");
  return (variant.units || []).find((unit) => {
    if (roomUnitId && String(unit.id || "") === roomUnitId) {
      return true;
    }

  return (unit.rooms || []).some((unitRoom) => String(unitRoom.id || unitRoom) === roomId);
  }) || null;
}

function relationshipsForElement(variant, kind, id, item) {
  if (!variant) {
    return null;
  }

  const topology = variant.topology || {};
  const hypergraph = topology.hypergraph || {};
  const node = graphNodeForElement(variant, kind, id, item);
  const selectedIds = relationshipLookupIds(node, id, item);
  const topologyEdges = (topology.edges || []).filter((edge) => edgeTouchesIds(edge, selectedIds));
  const hyperedges = (hypergraph.hyperedges || []).filter((edge) => hyperedgeTouchesIds(edge, selectedIds));
  const adjacent = relatedNodesFromHyperedges(variant, hyperedges, selectedIds, "adjacency", 8);
  const access = relatedNodesFromHyperedges(variant, hyperedges, selectedIds, "circulation_access", 6)
    .concat(relatedNodesFromHyperedges(variant, hyperedges, selectedIds, "door", 4));
  const facade = hyperedges.filter((edge) => String(edge.kind || "").toLowerCase() === "facade");
  const constraints = hyperedges.filter((edge) => String(edge.kind || "").toLowerCase() === "constraint");
  const doorLinks = doorsForElement(variant, selectedIds, item);

  return {
    node,
    nodeId: node ? node.id : String(id || ""),
    parent: node && node.parentId ? graphNodeById(variant, node.parentId) : null,
    children: childNodesForNode(variant, node).slice(0, 8),
    adjacent,
    access: uniqueRelationshipItems(access, "id").slice(0, 8),
    facadeCount: facade.length,
    constraintCount: constraints.length,
    edgeCount: topologyEdges.length,
    hyperedgeCount: hyperedges.length,
    doorLinks
  };
}

function relationshipLookupIds(node, id, item) {
  const ids = new Set();
  [id, node && node.id, node && node.referenceId, item && item.id, item && item.hostWall]
    .filter(Boolean)
    .forEach((value) => ids.add(String(value)));
  (item && item.connectsSpaces || []).forEach((value) => ids.add(String(value)));
  return ids;
}

function graphNodeForElement(variant, kind, id, item) {
  const nodes = graphNodes(variant);
  const idText = String(id || "");
  const externalId = String(item && item.externalId || "");
  return nodes.find((node) =>
    String(node.id || "") === idText ||
    String(node.referenceId || "") === idText ||
    Boolean(externalId && String(node.externalId || "") === externalId)) ||
    nodes.find((node) => nodeKindMatchesSelection(node, kind, idText)) ||
    null;
}

function graphNodeById(variant, id) {
  return graphNodes(variant).find((node) => String(node.id || "") === String(id || "")) || null;
}

function graphNodes(variant) {
  const topologyNodes = variant && variant.topology && Array.isArray(variant.topology.nodes)
    ? variant.topology.nodes
    : [];
  const hypergraphNodes = variant && variant.topology && variant.topology.hypergraph &&
    Array.isArray(variant.topology.hypergraph.nodes)
    ? variant.topology.hypergraph.nodes
    : [];
  return topologyNodes.length ? topologyNodes : hypergraphNodes;
}

function nodeKindMatchesSelection(node, kind, id) {
  const nodeKind = String(node.kind || "").toLowerCase();
  return nodeKind === String(kind || "").toLowerCase() && String(node.referenceId || "") === id;
}

function childNodesForNode(variant, node) {
  if (!node || !node.id) {
    return [];
  }
  return graphNodes(variant).filter((candidate) => String(candidate.parentId || "") === String(node.id));
}

function edgeTouchesIds(edge, ids) {
  return ids.has(String(edge && edge.from || "")) || ids.has(String(edge && edge.to || ""));
}

function hyperedgeTouchesIds(edge, ids) {
  return (edge && edge.members || []).some((member) => ids.has(String(member.nodeId || "")));
}

function relatedNodesFromHyperedges(variant, hyperedges, selectedIds, kind, limit) {
  return uniqueRelationshipItems(
    hyperedges
      .filter((edge) => String(edge.kind || "").toLowerCase() === kind)
      .flatMap((edge) => (edge.members || [])
        .filter((member) => !selectedIds.has(String(member.nodeId || "")))
        .map((member) => relationshipItemForNode(variant, member.nodeId, edge))),
    "id")
    .slice(0, limit);
}

function relationshipItemForNode(variant, nodeId, edge, knownNode = null) {
  const node = knownNode || graphNodeById(variant, nodeId) || { id: nodeId };
  return {
    id: String(node.id || nodeId),
    kind: selectionKindForGraphNode(node),
    label: relationshipLabelForNode(node),
    edgeKind: edge ? edge.kind || "" : ""
  };
}

function selectionKindForGraphNode(node) {
  const kind = String(node && node.kind || "").toLowerCase();
  if (kind.startsWith("room")) {
    return "room";
  }
  if (kind.includes("corridor")) {
    return "corridor";
  }
  if (kind.includes("wall")) {
    return "wall";
  }
  if (kind.includes("core")) {
    return "core";
  }
  if (kind.includes("unit")) {
    return "unit";
  }
  if (kind.includes("floorplate")) {
    return "floorplate";
  }
  return "";
}

function relationshipLabelForNode(node) {
  const id = String(node && (node.referenceId || node.id) || "");
  const kind = String(node && node.kind || "").replace("room:", "");
  return `${displayUnitType(kind || "node")} ${shortRelationshipId(id)}`.trim();
}

function shortRelationshipId(value) {
  const text = String(value || "");
  if (text.length <= 28) {
    return text;
  }
  return `${text.slice(0, 13)}...${text.slice(-10)}`;
}

function doorsForElement(variant, ids, item) {
  const doors = Array.isArray(variant && variant.doorsOpenings) ? variant.doorsOpenings : [];
  return doors
    .filter((door) =>
      ids.has(String(door.id || "")) ||
      ids.has(String(door.hostWall || "")) ||
      (door.connectsSpaces || []).some((space) => ids.has(String(space))))
    .map((door) => ({
      id: door.id,
      kind: "door",
      label: `Door ${shortRelationshipId(door.id)}`,
      connects: (door.connectsSpaces || []).join(" / ")
    }))
    .filter((door) => !item || String(door.id || "") !== String(item.id || ""))
    .slice(0, 6);
}

function uniqueRelationshipItems(items, key) {
  const seen = new Set();
  return (items || []).filter((item) => {
    const value = String(item && item[key] || "");
    if (!value || seen.has(value)) {
      return false;
    }
    seen.add(value);
    return true;
  });
}

function diagnosticsForElement(output, ids) {
  const lookup = new Set((ids || []).filter(Boolean).map((value) => String(value)));
  if (lookup.size === 0) {
    return [];
  }

  return collectDiagnostics(output)
    .filter((diagnostic) => {
      const source = String(diagnostic.sourceId || "");
      return lookup.has(source) || [...lookup].some((id) => source.includes(id));
    })
    .slice(0, 5);
}

function selectionInlineSummary(detail) {
  if (!detail) {
    return "";
  }

  if (detail.kind === "unit" || detail.kind === "room" || detail.kind === "corridor") {
    // Humans read "Bathroom · 8.7 m²", not engine ids — those stay in exports.
    const name = detail.kind === "room"
      ? displayRoomType(detail.item)
      : detail.kind === "unit"
        ? `${displayUnitType(detail.item && detail.item.type)} unit`
        : "Corridor";
    return `${name} · ${formatNumber(detail.area, 1)} m²`;
  }

  if (detail.bounds) {
    return `${selectionKindLabel(detail.kind)} · ${formatNumber(detail.bounds.width, 1)} × ${formatNumber(detail.bounds.height, 1)} m`;
  }

  return `${selectionKindLabel(detail.kind)} ${detail.id || ""}`.trim();
}

function planActionsForDetail(detail) {
  if (!detail) {
    return [];
  }

  if (detail.kind === "unit") {
    return [["unit-more", "More like this"], ["unit-less", "Fewer like this"], ["unit-fit-area", "Fit target area"]];
  }
  if (detail.kind === "floorplate") {
    return [["floor-wider", "Wider"], ["floor-narrower", "Narrower"], ["floor-deeper", "Deeper"], ["floor-shallower", "Shallower"]];
  }
  if (detail.kind === "core") {
    return [["core-left", "Left"], ["core-right", "Right"], ["core-up", "Up"], ["core-down", "Down"], ["core-grow", "Grow"], ["core-shrink", "Shrink"]];
  }
  if (detail.kind === "room") {
    const actions = [["room-use-dimensions", "Use as room minimum"]];
    if (detail.unit) {
      actions.push(["unit-more", "More like this"], ["unit-less", "Fewer like this"], ["unit-fit-area", "Fit target area"]);
    }
    return actions;
  }
  if (detail.kind === "corridor") {
    return [["corridor-use-width", "Use as corridor width"]];
  }

  return [];
}

function inspectorMarkup(detail) {
  const rows = [];
  const actions = planActionsForDetail(detail);
  const relationships = relationshipSectionsMarkup(detail.relationships);
  const localNotes = diagnosticSectionsMarkup(detail.diagnostics);
  rows.push(["Source", detail.source === "input" ? "Input constraint" : "Generated output"]);
  rows.push(["Id", detail.id || "-"]);

  if (detail.bounds) {
    rows.push(["Size", `${formatNumber(detail.bounds.width, 1)} × ${formatNumber(detail.bounds.height, 1)} m`]);
  }
  if (Number.isFinite(Number(detail.area))) {
    rows.push(["Area", `${formatNumber(detail.area, 1)} m²`]);
  }

  if (detail.kind === "unit") {
    rows.push(["Type", displayUnitType(detail.item.type)]);
    rows.push(["Rooms", String(Array.isArray(detail.item.rooms) ? detail.item.rooms.length : 0)]);
    rows.push(["Facade", `${formatNumber(detail.item.facadeLength || 0, 1)} m`]);
    rows.push(["Score", formatNumber(detail.item.score, 3)]);
    rows.push(["External", compactExternalId(detail.item.externalId || "")]);
  } else if (detail.kind === "floorplate") {
  } else if (detail.kind === "core") {
    rows.push(["Blocks units", detail.item.blocksGeneration === false ? "No" : "Yes"]);
  } else if (detail.kind === "room") {
    rows.push(["Type", displayRoomType(detail.item)]);
    rows.push(["Unit", detail.item.unitId || "-"]);
    if (detail.unit) {
      rows.push(["Unit type", displayUnitType(detail.unit.type)]);
    }
    if (detail.item.dimensions && !detail.item.edited) {
      // Engine-reported dimensions go stale once the room is manually edited;
      // the Size row above (from live bounds) is then the single source.
      rows.push([
        "Dimensions",
        `${formatNumber(detail.item.dimensions.width, 1)} × ${formatNumber(detail.item.dimensions.depth, 1)} m`
      ]);
    }
    rows.push(["Daylight", detail.item.daylight ? "Yes" : "No"]);
    rows.push(["External", compactExternalId(detail.item.externalId || "")]);
  } else if (detail.kind === "corridor") {
    rows.push(["Width", `${formatNumber(corridorWidth(detail), 2)} m`]);
    rows.push(["Connections", String(Array.isArray(detail.item.connections) ? detail.item.connections.length : 0)]);
    rows.push(["External", compactExternalId(detail.item.externalId || "")]);
  } else if (detail.kind === "wall") {
    rows.push(["Layer", displayUnitType(detail.item.layerType || "partition")]);
    rows.push(["Thickness", `${formatNumber(detail.item.thickness || 0.12, 2)} m`]);
    rows.push(["Length", `${formatNumber(detail.length, 1)} m`]);
    rows.push(["External", compactExternalId(detail.item.externalId || "")]);
  } else if (detail.kind === "door") {
    rows.push(["Kind", displayUnitType(detail.item.type || "door")]);
    rows.push(["Width", `${formatNumber(detail.item.width || 0.9, 2)} m`]);
    rows.push(["Host wall", detail.item.hostWall || "-"]);
    rows.push(["Connects", (detail.item.connectsSpaces || []).join(", ") || "-"]);
    if (detail.item.location) {
      rows.push(["Location", `${formatNumber(detail.item.location.x, 1)}, ${formatNumber(detail.item.location.y, 1)}`]);
    }
    rows.push(["External", compactExternalId(detail.item.externalId || "")]);
  }

  return `
    <div class="inspector-card">
      <div class="inspector-kicker">${escapeHtml(selectionKindLabel(detail.kind))}</div>
      <div class="inspector-title">${escapeHtml(detail.title || detail.id || "Selected element")}</div>
      <div class="inspector-grid">
        ${rows.map(([label, value]) => `
          <div class="inspector-row">
            <span>${escapeHtml(label)}</span>
            <strong>${escapeHtml(value)}</strong>
          </div>
        `).join("")}
      </div>
      ${relationships}
      ${localNotes}
      ${actions.length ? `
        <div class="inspector-actions">
          ${actions.map(([action, label]) => `<button type="button" data-inspector-action="${escapeHtml(action)}">${escapeHtml(label)}</button>`).join("")}
        </div>
      ` : ""}
      <button class="inspector-clear" type="button" data-inspector-action="clear-selection">Clear selection</button>
    </div>
  `;
}

function relationshipSectionsMarkup(relationships) {
  if (!relationships) {
    return "";
  }

  const chips = [];
  if (relationships.parent) {
    chips.push(["Parent", [relationshipItemForNode(null, relationships.parent.id, null, relationships.parent)]]);
  }
  if (relationships.children && relationships.children.length) {
    chips.push(["Contains", relationships.children.map((node) => relationshipItemForNode(null, node.id, null, node))]);
  }
  if (relationships.adjacent && relationships.adjacent.length) {
    chips.push(["Adjacent", relationships.adjacent]);
  }
  if (relationships.access && relationships.access.length) {
    chips.push(["Access", relationships.access]);
  }
  if (relationships.doorLinks && relationships.doorLinks.length) {
    chips.push(["Doors", relationships.doorLinks]);
  }

  const stats = [
    ["Graph node", relationships.nodeId || "-"],
    ["Hyperedges", String(relationships.hyperedgeCount || 0)],
    ["Facade", relationships.facadeCount ? `${relationships.facadeCount} edge${relationships.facadeCount === 1 ? "" : "s"}` : "No"],
    ["Constraints", relationships.constraintCount ? `${relationships.constraintCount} daylight/facade` : "None"]
  ];

  return `
    <div class="inspector-section graph-section">
      <div class="inspector-section-title">Graph Relations</div>
      <div class="relationship-stats">
        ${stats.map(([label, value]) => `
          <div>
            <span>${escapeHtml(label)}</span>
            <strong>${escapeHtml(value)}</strong>
          </div>
        `).join("")}
      </div>
      ${chips.map(([label, items]) => relationshipChipGroup(label, items)).join("")}
    </div>
  `;
}

function relationshipChipGroup(label, items) {
  const usableItems = (items || []).filter((item) => item && item.id);
  if (usableItems.length === 0) {
    return "";
  }

  return `
    <div class="relationship-group">
      <span>${escapeHtml(label)}</span>
      <div class="relationship-chips">
        ${usableItems.map((item) => `
          <button type="button"
              data-inspector-select-kind="${escapeHtml(item.kind || "")}"
              data-inspector-select-id="${escapeHtml(item.id)}"
              ${item.kind ? "" : "disabled"}>
            ${escapeHtml(item.label || item.id)}
            ${item.connects ? `<small>${escapeHtml(item.connects)}</small>` : ""}
          </button>
        `).join("")}
      </div>
    </div>
  `;
}

function diagnosticSectionsMarkup(diagnostics) {
  const items = diagnostics || [];
  if (items.length === 0) {
    return "";
  }

  return `
    <div class="inspector-section local-notes-section">
      <div class="inspector-section-title">Review Notes</div>
      ${items.map((diagnostic) => `
        <div class="inspector-note ${escapeHtml(diagnostic.severity || "warning")}">
          <strong>${escapeHtml(humanizeCode(diagnostic.code || diagnostic.name || "note"))}</strong>
          <span>${escapeHtml(friendlyDiagnosticMessage(diagnostic))}</span>
        </div>
      `).join("")}
    </div>
  `;
}

function handleInspectorAction(event) {
  const selectButton = event.target.closest("[data-inspector-select-id]");
  if (selectButton) {
    const kind = selectButton.dataset.inspectorSelectKind || "";
    const id = selectButton.dataset.inspectorSelectId || "";
    if (kind && id) {
      state.selection = { kind, id };
      renderAll();
      setStatus(`${selectionKindLabel(kind)} selected`);
    }
    return;
  }

  const button = event.target.closest("[data-inspector-action]");
  if (!button) {
    return;
  }

  const action = button.dataset.inspectorAction;
  if (!action) {
    return;
  }

  if (action === "clear-selection") {
    state.selection = null;
    renderAll();
    setStatus("Selection cleared");
    return;
  }

  runSelectedPlanAction(action);
}

function handleRoomScheduleClick(event) {
  const unitButton = event.target.closest("[data-schedule-unit-id]");
  if (unitButton) {
    state.selection = { kind: "unit", id: unitButton.dataset.scheduleUnitId || "" };
    renderAll();
    setStatus(`${unitButton.dataset.scheduleUnitName || "Unit"} selected`);
    return;
  }

  const button = event.target.closest("[data-schedule-room-id]");
  if (!button) {
    return;
  }

  state.selection = { kind: "room", id: button.dataset.scheduleRoomId || "" };
  renderAll();
  setStatus(`${button.dataset.scheduleRoomName || "Room"} selected`);
}

function runSelectedPlanAction(action) {
  const detail = selectedElementDetails(currentVisualOutput());
  if (!detail) {
    setStatus("Select a plan element first");
    return;
  }

  const actions = new Set(planActionsForDetail(detail).map(([availableAction]) => availableAction));
  if (!actions.has(action)) {
    setStatus("No canvas edit available for this element");
    return;
  }

  if (action.startsWith("floor-")) {
    adjustFloorplateFromInspector(action);
  } else if (action.startsWith("core-")) {
    adjustCoreFromInspector(action);
  } else if (action.startsWith("unit-")) {
    adjustUnitTargetFromInspector(action, detail);
  } else if (action === "room-use-dimensions") {
    applyRoomMinimumFromInspector(detail);
  } else if (action === "corridor-use-width") {
    applyCorridorWidthFromInspector(detail);
  }
}

function syncSelection(output) {
  if (state.selection && !selectedElementDetails(output)) {
    state.selection = null;
  }
}

function adjustFloorplateFromInspector(action) {
  const step = 1;
  applyInputMutation("Resizing floorplate", 180, (input) => {
    const source = clone(input);
    const bounds = boundsOfPoints(source.floorplate.outer.points) || { width: 42, height: 22 };
    const width = clamp(bounds.width + (action === "floor-wider" ? step : action === "floor-narrower" ? -step : 0), 8, 300);
    const depth = clamp(bounds.height + (action === "floor-deeper" ? step : action === "floor-shallower" ? -step : 0), 8, 300);
    resizeFloorplateInput(input, source, width, depth);
    clampCoreIntoFloorplate(input);
  });
}

function adjustCoreFromInspector(action) {
  const step = 1;
  applyInputMutation("Adjusting core", 180, (input) => {
    const core = ensureCore(input);
    const floorBounds = boundsOfPoints(input.floorplate.outer.points) || { minX: 0, minY: 0, maxX: 42, maxY: 22, width: 42, height: 22 };
    const coreBounds = boundsOfPoints(core.polygon.points) || { minX: 18, minY: 8, width: 6, height: 6 };
    const nextWidth = clamp(coreBounds.width + (action === "core-grow" ? step : action === "core-shrink" ? -step : 0), 1, floorBounds.width);
    const nextDepth = clamp(coreBounds.height + (action === "core-grow" ? step : action === "core-shrink" ? -step : 0), 1, floorBounds.height);
    const dx = action === "core-left" ? -step : action === "core-right" ? step : 0;
    const dy = action === "core-down" ? -step : action === "core-up" ? step : 0;
    const x = clamp(coreBounds.minX + dx, floorBounds.minX, floorBounds.maxX - nextWidth);
    const y = clamp(coreBounds.minY + dy, floorBounds.minY, floorBounds.maxY - nextDepth);
    core.polygon.points = rectPoints(x, y, nextWidth, nextDepth);
    refreshAccessFromCore(input);
  });
}

function adjustUnitTargetFromInspector(action, detail) {
  const unit = detail.kind === "room" && detail.unit ? detail.unit : detail.item || {};
  const type = unit.type || "studio";
  applyInputMutation(action === "unit-fit-area" ? "Updating target unit area" : "Updating target unit mix", 220, (input) => {
    const target = ensureUnitTarget(input, type);
    if (action === "unit-fit-area") {
      const area = Number(unit.area) || detail.area || polygonArea(detail.points);
      target.minArea = Math.max(10, round(area * 0.9));
      target.maxArea = Math.max(target.minArea, round(area * 1.1));
      return;
    }

    const delta = action === "unit-more" ? 0.05 : -0.05;
    target.targetRatio = clamp(round((Number(target.targetRatio) || 0) + delta), 0, 1);
    normalizeUnitTargetRatios(input.program.targetUnitTypes);
  });
}

function applyRoomMinimumFromInspector(detail) {
  applyInputMutation("Updating room sizing rules", 220, (input) => {
    const bounds = detail.bounds || boundsOfPoints(detail.points);
    if (!bounds) {
      return;
    }
    input.rules.minRoomWidth = clamp(round(Math.min(bounds.width, bounds.height)), 1.2, 20);
    input.rules.minRoomDepth = clamp(round(Math.max(bounds.width, bounds.height)), 1.2, 25);
  });
}

function applyCorridorWidthFromInspector(detail) {
  applyInputMutation("Updating corridor width", 220, (input) => {
    input.rules.minCorridorWidth = clamp(round(corridorWidth(detail)), 0.9, 12);
  });
}

function applyInputMutation(message, autoGenerateDelay, mutate) {
  const input = ensureInputShape(state.input || {});
  mutate(input);
  state.input = input;
  syncFormFromInput(state.input);
  setEditorFromInput(state.input);
  saveDraft();
  state.editReadout = editSummary(state.input);
  markInputDirty(message, autoGenerateDelay);
  renderAll();
}

function selectionEditSnapshot(detail) {
  if (!detail || !detail.bounds) {
    return null;
  }

  const snapshot = {
    kind: detail.kind,
    id: detail.id,
    bounds: { ...detail.bounds },
    area: Number(detail.area) || 0
  };
  if (detail.kind === "unit") {
    snapshot.unitType = detail.item && detail.item.type ? detail.item.type : "studio";
  }
  if (detail.kind === "room") {
    snapshot.roomType = detail.item && (detail.item.roomType || detail.item.type) ? detail.item.roomType || detail.item.type : "room";
  }
  if (detail.kind === "corridor") {
    snapshot.corridorWidth = corridorWidth(detail);
  }
  return snapshot;
}

function handlePlanPointerDown(event) {
  if (!state.editMode || !state.input || state.viewMode === "axon") {
    return;
  }
  if (event.button && event.button !== 0) {
    return;
  }

  const point = clientToModelPoint(event);
  if (!point) {
    return;
  }

  // 1) Direct geometry resize handle on a selected room/unit.
  const geomHandle = event.target.closest ? event.target.closest('[data-edit-action="geom-resize"]') : null;
  if (geomHandle) {
    event.preventDefault();
    beginGeomDrag(event, point, {
      mode: "resize",
      handle: geomHandle.dataset.geomHandle,
      kind: geomHandle.dataset.geomKind,
      id: geomHandle.dataset.geomId
    });
    return;
  }

  // 2) Existing input-constraint handles (floorplate, core, corridor width, ...).
  const handle = event.target.closest ? event.target.closest("[data-edit-action]") : null;
  if (handle) {
    event.preventDefault();
    const detail = selectedElementDetails(currentVisualOutput());
    state.dragEdit = {
      action: handle.dataset.editAction,
      startPoint: point,
      startInput: clone(state.input),
      startGeometry: clone(state.geometryEdits),
      selection: selectionEditSnapshot(detail)
    };
    try {
      handle.setPointerCapture(event.pointerId);
    } catch (_) {
      // Pointer capture is helpful but not required for SVG editing.
    }
    els.previewFrame.classList.add("is-dragging");
    setStatus("Editing plan");
    return;
  }

  // 3) Not while panning.
  if (state.spaceHeld || state.canvasTool === "pan") {
    return;
  }

  // 4) A straight wall is itself a handle: grab it to slide the whole wall
  //    plane, resizing the rooms on both sides in lockstep.
  const wallHit = event.target.closest ? event.target.closest("[data-wall-ref]") : null;
  if (wallHit && beginWallDrag(event, point, wallHit.dataset.wallRef)) {
    event.preventDefault();
    return;
  }

  // 5) Grab the body of a room/unit to move it directly.
  const body = event.target.closest ? event.target.closest('[data-select-kind="room"],[data-select-kind="unit"]') : null;
  if (body) {
    event.preventDefault();
    state.selection = { kind: body.dataset.selectKind, id: body.dataset.selectId || "" };
    beginGeomDrag(event, point, { mode: "move", kind: state.selection.kind, id: state.selection.id });
  }
}

function handlePlanPointerMove(event) {
  if (state.wallDrag) {
    updateWallDrag(event);
    return;
  }
  if (state.geomDrag) {
    updateGeomDrag(event);
    return;
  }
  if (!state.dragEdit) {
    return;
  }

  const point = clientToModelPoint(event);
  if (!point) {
    return;
  }

  event.preventDefault();
  const edited = applyCanvasEdit(state.dragEdit, point);
  if (!edited) {
    return;
  }

  state.dragEdit.lastPoint = point;
  state.input = edited;
  syncFormFromInput(state.input);
  setEditorFromInput(state.input);
  saveDraft();
  state.editReadout = editSummary(state.input);
  markInputDirty("Editing plan", null);
  renderAll();
}

function finishPlanPointerEdit() {
  if (state.wallDrag) {
    finishWallDrag();
    return;
  }
  if (state.geomDrag) {
    finishGeomDrag();
    return;
  }
  if (!state.dragEdit) {
    return;
  }

  // Capture the pre-drag input so this edit can be undone, but only when the
  // pointer actually moved (a bare click on a handle should not stack history).
  if (state.dragEdit.startInput && state.dragEdit.lastPoint) {
    pushUndoSnapshot({ input: state.dragEdit.startInput, geometry: state.dragEdit.startGeometry });
    // The pointerup may also arrive as a click on the handle's host element —
    // swallow it so finishing a core drag does not re-route the selection.
    state.suppressNextPlanClick = true;
  }

  state.dragEdit = null;
  els.previewFrame.classList.remove("is-dragging");
  state.editReadout = editSummary(state.input);
  markInputDirty("Regenerating edited plan", 120);
  renderAll();
}

function clientToModelPoint(event) {
  const matrix = els.planSvg.getScreenCTM();
  if (!matrix) {
    return null;
  }

  const point = els.planSvg.createSVGPoint();
  point.x = event.clientX;
  point.y = event.clientY;
  const svgPoint = point.matrixTransform(matrix.inverse());
  return { x: round(svgPoint.x), y: round(-svgPoint.y) };
}

// --- Direct geometry editing (live room/unit resize + move) -----------------
// Smallest room/unit dimension and the grid the drag snaps to, in metres.
const geomMinDimension = 0.8;
const geomSnapStep = 0.1;

function geomSnap(value) {
  // Shift held = free movement (the CAD convention for temporarily
  // suspending the grid); drags set this flag from the live pointer event.
  if (state.snapSuspended) {
    return round(value);
  }
  return round(Math.round(Number(value) / geomSnapStep) * geomSnapStep);
}

function pointsToAttr(points) {
  return (points || []).map((p) => `${p.x},${p.y}`).join(" ");
}

// Find the live <polygon> for a room/unit so a drag can patch its points
// in place at 60fps, the way the pan handler re-aims the viewBox directly.
function planPolygonFor(kind, id) {
  const nodes = els.planSvg.querySelectorAll(`[data-select-kind="${kind}"]`);
  for (let i = 0; i < nodes.length; i += 1) {
    if ((nodes[i].getAttribute("data-select-id") || "") === String(id)) {
      return nodes[i];
    }
  }
  return null;
}

function beginGeomDrag(event, point, opts) {
  // The effective variant already has earlier overrides (and warped walls)
  // applied, so every drag starts from exactly what is on screen.
  const variant = selectedVariant(currentVisualOutput());
  if (!variant || !variant.variantId) {
    return;
  }
  const kind = opts.kind;
  const id = String(opts.id || "");
  const list = kind === "unit" ? variant.units : variant.rooms;
  const item = (list || []).find((entry) => String(entry.id || "") === id);
  const startPoints = (item && item.polygon ? item.polygon.points || [] : [])
    .map((p) => ({ x: Number(p.x), y: Number(p.y) }));
  const startBounds = boundsOfPoints(startPoints);
  if (startPoints.length < 3 || !startBounds || startBounds.width <= 0 || startBounds.height <= 0) {
    return;
  }

  const floorBounds = state.input && state.input.floorplate && state.input.floorplate.outer
    ? boundsOfPoints(state.input.floorplate.outer.points)
    : null;
  // Resizing a unit must carry its rooms along, live and on commit.
  const childRooms = kind === "unit"
    ? (variant.rooms || [])
        .filter((room) => String(room.unitId || "") === id)
        .map((room) => ({
          id: String(room.id || ""),
          points: (room.polygon ? room.polygon.points || [] : []).map((p) => ({ x: Number(p.x), y: Number(p.y) })),
          node: planPolygonFor("room", String(room.id || ""))
        }))
    : [];
  // The dragged edge is a shared wall plane: whatever borders it must follow,
  // so resolve neighbours and travel limits once, up front.
  const edges = collectBoundaryEdges(variant, kind, id, startBounds, childRooms, floorBounds);
  const fadeNodes = collectFadeNodes(variant, kind, id, childRooms, startBounds);
  followersForDeltas([edges.w, edges.e, edges.s, edges.n].map((edge) => ({ followers: edge.followers })))
    .forEach((follower) => {
      if (follower.kind === "room") {
        els.planSvg.querySelectorAll(`[data-room-ref="${follower.id}"]`).forEach((node) => fadeNodes.push(node));
      }
    });
  state.geomDrag = {
    mode: opts.mode,
    handle: opts.handle || null,
    kind,
    id,
    variantId: variant.variantId,
    startPoint: point,
    startPoints,
    startBounds,
    floorBounds,
    edges,
    edgeDeltas: [],
    current: startPoints,
    currentBounds: startBounds,
    moved: false,
    snapshot: captureSnapshot(),
    polygon: planPolygonFor(kind, id),
    childRooms,
    liveWalls: collectLiveWallsForEdges(variant, startBounds, edges),
    doors: (variant.doorsOpenings || []).slice(),
    fadeNodes,
    statusBefore: els.runStatus ? els.runStatus.textContent : ""
  };

  try {
    els.planSvg.setPointerCapture(event.pointerId);
  } catch (_) {
    // Pointer capture only smooths the drag; editing still works without it.
  }
  els.previewFrame.classList.add("is-dragging");
  // Fixtures and doors re-place on commit; fade them so mid-drag they read as
  // "pending" instead of stuck.
  fadeNodes.forEach((node) => node.classList.add("geom-stale"));
  // Swap the static selection chrome for a live overlay we repaint per frame.
  removeGeomChrome();
  renderGeomOverlay(startBounds, kind, id, startPoints);
}

// Walls touching the dragged element's footprint get their endpoints re-aimed
// every frame, so the wall fabric stretches with the room like in a CAD tool.
function collectLiveWalls(variant, bounds) {
  const inBounds = (p) => p
    && Number(p.x) >= bounds.minX - warpEpsilon && Number(p.x) <= bounds.maxX + warpEpsilon
    && Number(p.y) >= bounds.minY - warpEpsilon && Number(p.y) <= bounds.maxY + warpEpsilon;
  const refs = [];
  (variant.walls || []).forEach((wall) => {
    if (!wall || !wall.centerline || !wall.centerline.start || !wall.centerline.end) {
      return;
    }
    const startIn = inBounds(wall.centerline.start);
    const endIn = inBounds(wall.centerline.end);
    if (!startIn && !endIn) {
      return;
    }
    const parts = Array.from(els.planSvg.querySelectorAll(`[data-wall-ref="${wall.id}"]`));
    if (parts.length) {
      refs.push({ wall, startIn, endIn, parts, thickness: wallStrokeWidth(wall) });
    }
  });
  return refs;
}

// Walls inside the dragged element's own box PLUS walls riding any of its
// four (possibly span-widened) boundary planes — so a party wall that extends
// past the dragged room still slides live instead of snapping on commit.
function collectLiveWallsForEdges(variant, bounds, edges) {
  const byId = new Map();
  collectLiveWalls(variant, bounds).forEach((ref) => byId.set(ref.wall.id, ref));
  ["w", "e", "s", "n"].forEach((dir) => {
    const edge = edges[dir];
    collectLiveWallsOnLine(variant, { axis: edge.axis, line: edge.line, spanLo: edge.spanLo, spanHi: edge.spanHi })
      .forEach((ref) => {
        if (!byId.has(ref.wall.id)) {
          byId.set(ref.wall.id, ref);
        }
      });
  });
  return Array.from(byId.values());
}

function collectFadeNodes(variant, kind, id, childRooms, bounds) {
  const nodes = [];
  const roomIds = kind === "room" ? [id] : childRooms.map((child) => child.id);
  roomIds.forEach((roomId) => {
    els.planSvg.querySelectorAll(`[data-room-ref="${roomId}"]`).forEach((node) => nodes.push(node));
  });
  (variant.doorsOpenings || []).forEach((door) => {
    const p = door && door.location;
    if (!p
      || Number(p.x) < bounds.minX - warpEpsilon || Number(p.x) > bounds.maxX + warpEpsilon
      || Number(p.y) < bounds.minY - warpEpsilon || Number(p.y) > bounds.maxY + warpEpsilon) {
      return;
    }
    els.planSvg.querySelectorAll(`[data-select-kind="door"][data-select-id="${door.id}"]`)
      .forEach((node) => nodes.push(node));
  });
  return nodes;
}

function updateGeomDrag(event) {
  const drag = state.geomDrag;
  if (!drag) {
    return;
  }
  event.preventDefault();
  const point = clientToModelPoint(event);
  if (!point) {
    return;
  }

  state.snapSuspended = Boolean(event.shiftKey);
  const dx = point.x - drag.startPoint.x;
  const dy = point.y - drag.startPoint.y;
  const rawBounds = drag.mode === "move"
    ? translateBoundsWithin(drag.startBounds, dx, dy, drag.floorBounds)
    : resizeBounds(drag.startBounds, drag.handle, dx, dy, drag.floorBounds);
  // Boundary planes stop where a neighbour would drop below minimum size.
  const bounds = clampBoundsToEdges(rawBounds, drag.startBounds, drag.edges, drag.mode);
  const points = remapPolygon(drag.startPoints, drag.startBounds, bounds);

  drag.current = points;
  drag.currentBounds = bounds;
  drag.edgeDeltas = boundaryEdgeDeltas(drag.edges, drag.startBounds, bounds);
  // "Moved" means the snapped geometry actually changed — a 1-px click wiggle
  // must not announce a drag, stack undo history, or store a no-op override.
  if (!drag.moved && drag.edgeDeltas.length > 0) {
    drag.moved = true;
    setStatus(`${drag.mode === "move" ? "Moving" : "Resizing"} ${selectionKindLabel(drag.kind).toLowerCase()}`);
  }
  if (drag.polygon) {
    drag.polygon.setAttribute("points", pointsToAttr(points));
    drag.polygon.classList.add("geom-editing");
  }

  // Live-follow: child rooms (when dragging a unit), neighbours sharing the
  // dragged wall plane, and abutting walls are re-aimed in place each frame —
  // no full re-render, CAD-smooth.
  const mapPoint = (p) => ({
    x: round(bounds.minX + ((Number(p.x) - drag.startBounds.minX) / drag.startBounds.width) * bounds.width),
    y: round(bounds.minY + ((Number(p.y) - drag.startBounds.minY) / drag.startBounds.height) * bounds.height)
  });
  (drag.childRooms || []).forEach((child) => {
    if (child.node && child.points.length >= 3) {
      child.node.setAttribute("points", pointsToAttr(child.points.map(mapPoint)));
    }
  });
  const allEdges = [drag.edges.w, drag.edges.e, drag.edges.s, drag.edges.n];
  followersForDeltas(allEdges.map((edge) => ({ followers: edge.followers }))).forEach((follower) => {
    // Shifts are applied from the follower's drag-start points, so a reversed
    // drag (delta back to zero) restores the original outline exactly.
    if (follower.node) {
      follower.node.setAttribute("points", pointsToAttr(shiftFollowerPoints(follower, drag.edgeDeltas)));
    }
  });
  // Live walls use the same hybrid mapper the commit writes, so what the drag
  // shows is exactly what releasing the pointer keeps.
  const liveMapper = buildEditPointMapper(drag.startBounds, bounds, drag.edgeDeltas);
  (drag.liveWalls || []).forEach((ref) => updateLiveWall(ref, liveMapper));

  renderGeomOverlay(bounds, drag.kind, drag.id, points);
  showGeomReadout(bounds, points, drag.kind);
}

function updateLiveWall(ref, mapper) {
  const next = mapper.wall(ref.wall.centerline.start, ref.wall.centerline.end);
  const start = next.start;
  const end = next.end;
  const footprint = wallFootprint({ centerline: { start, end } }, ref.thickness);
  ref.parts.forEach((part) => {
    if (part.tagName === "polygon") {
      if (footprint) {
        part.setAttribute("points", pointsToAttr(footprint.map((p) => ({ x: round(p.x), y: round(p.y) }))));
      }
    } else {
      part.setAttribute("x1", round(start.x));
      part.setAttribute("y1", round(start.y));
      part.setAttribute("x2", round(end.x));
      part.setAttribute("y2", round(end.y));
    }
  });
}

// One mapping for everything a manual edit drags along. A point strictly
// inside the dragged element's old box follows the box's old→new transform
// (interior partitions of a resized unit scale with it); any other point
// moves only by the plane deltas whose line AND span it actually sits on.
// Mapping per-axis through the right rule is what keeps a wall endpoint from
// inheriting a translation on an axis whose plane never moved — the bug that
// used to shear far walls into diagonals.
function buildEditPointMapper(oldBounds, newBounds, edgeDeltas) {
  const deltas = edgeDeltas || [];
  const inOld = (p) => oldBounds
    && p.x >= oldBounds.minX - warpEpsilon && p.x <= oldBounds.maxX + warpEpsilon
    && p.y >= oldBounds.minY - warpEpsilon && p.y <= oldBounds.maxY + warpEpsilon;
  const boundsMap = (p) => ({
    x: round(newBounds.minX + ((p.x - oldBounds.minX) / oldBounds.width) * newBounds.width),
    y: round(newBounds.minY + ((p.y - oldBounds.minY) / oldBounds.height) * newBounds.height)
  });
  const planeShift = (p) => {
    let x = p.x;
    let y = p.y;
    deltas.forEach((edge) => {
      if (edge.axis === "x") {
        if (Math.abs(p.x - edge.line) <= boundaryEps * 4
          && p.y >= edge.spanLo - boundaryEps && p.y <= edge.spanHi + boundaryEps) {
          x = round(p.x + edge.delta);
        }
      } else if (Math.abs(p.y - edge.line) <= boundaryEps * 4
        && p.x >= edge.spanLo - boundaryEps && p.x <= edge.spanHi + boundaryEps) {
        y = round(p.y + edge.delta);
      }
    });
    return { x, y };
  };
  const point = (raw) => {
    const p = { x: Number(raw.x), y: Number(raw.y) };
    return oldBounds && newBounds && inOld(p) ? boundsMap(p) : planeShift(p);
  };
  // Strictly interior to the old box — on-edge points belong to the planes.
  const strictlyInOld = (p) => oldBounds
    && p.x > oldBounds.minX + boundaryEps && p.x < oldBounds.maxX - boundaryEps
    && p.y > oldBounds.minY + boundaryEps && p.y < oldBounds.maxY - boundaryEps;
  // Wall mapping is plane-first. The wall's CONSTANT axis is decided once for
  // the whole wall: if the wall lies on a moved plane it travels with it iff
  // their spans genuinely overlap (junction overhangs ride along; a wall
  // merely touching the span's end does not). The running axis maps per
  // endpoint, so perpendicular walls stretch into the moved plane. Interior
  // endpoints of a resized unit keep the proportional box map.
  const wall = (startRaw, endRaw) => {
    const s0 = { x: Number(startRaw.x), y: Number(startRaw.y) };
    const e0 = { x: Number(endRaw.x), y: Number(endRaw.y) };
    if (strictlyInOld(s0) && strictlyInOld(e0)) {
      return { start: boundsMap(s0), end: boundsMap(e0) };
    }
    const s = { ...s0 };
    const e = { ...e0 };
    ["x", "y"].forEach((axis) => {
      const perp = axis === "x" ? "y" : "x";
      const isConstantAxis = Math.abs(s0[axis] - e0[axis]) <= boundaryEps;
      if (isConstantAxis) {
        // Whole-wall decision on its constant axis.
        deltas.forEach((edge) => {
          if (edge.axis !== axis || Math.abs(s0[axis] - edge.line) > boundaryEps * 4) {
            return;
          }
          const lo = Math.min(s0[perp], e0[perp]);
          const hi = Math.max(s0[perp], e0[perp]);
          if (intervalOverlap(lo, hi, edge.spanLo, edge.spanHi) > boundaryEps) {
            const movedTo = round(s0[axis] + edge.delta);
            s[axis] = movedTo;
            e[axis] = movedTo;
          }
        });
      } else {
        // Running axis: each endpoint follows the planes it sits on.
        [[s0, s], [e0, e]].forEach(([orig, target]) => {
          if (strictlyInOld(orig)) {
            target[axis] = boundsMap(orig)[axis];
            return;
          }
          deltas.forEach((edge) => {
            if (edge.axis !== axis) {
              return;
            }
            if (Math.abs(orig[axis] - edge.line) <= boundaryEps * 4
              && orig[perp] >= edge.spanLo - boundaryEps && orig[perp] <= edge.spanHi + boundaryEps) {
              target[axis] = round(orig[axis] + edge.delta);
            }
          });
        });
      }
    });
    return { start: s, end: e };
  };
  return { point, wall };
}

// Persist the final wall centerlines and door locations of an edit as
// explicit overrides — exactly what the live drag drew, so commit == preview.
function commitWallDoorOverrides(variantId, wallRefs, doors, mapper) {
  (wallRefs || []).forEach((ref) => {
    const next = mapper.wall(ref.wall.centerline.start, ref.wall.centerline.end);
    const old = ref.wall.centerline;
    if (next.start.x !== Number(old.start.x) || next.start.y !== Number(old.start.y)
      || next.end.x !== Number(old.end.x) || next.end.y !== Number(old.end.y)) {
      setGeometryOverride(variantId, "wall", ref.wall.id, next);
    }
  });

  (doors || []).forEach((door) => {
    if (!door || !door.location) {
      return;
    }
    const next = mapper.point(door.location);
    if (next.x !== Number(door.location.x) || next.y !== Number(door.location.y)) {
      setGeometryOverride(variantId, "door", door.id, next);
    }
  });
}

function finishGeomDrag() {
  const drag = state.geomDrag;
  if (!drag) {
    return;
  }
  state.geomDrag = null;
  state.snapSuspended = false;
  els.previewFrame.classList.remove("is-dragging");
  removeGeomOverlay();
  if (drag.polygon) {
    drag.polygon.classList.remove("geom-editing");
  }
  (drag.fadeNodes || []).forEach((node) => node.classList.remove("geom-stale"));

  if (drag.moved && drag.edgeDeltas.length > 0) {
    // Record the pre-edit state first so a single Undo reverts this resize,
    // then store the override. Manual geometry edits never re-run the engine.
    pushUndoSnapshot(drag.snapshot);
    setGeometryOverride(drag.variantId, drag.kind, drag.id, drag.current);
    // A unit edit rebases its rooms as explicit overrides with the same
    // transform, so rooms travel with their unit and stay individually editable.
    (drag.childRooms || []).forEach((child) => {
      if (child.points.length >= 3) {
        setGeometryOverride(
          drag.variantId,
          "room",
          child.id,
          remapPolygon(child.points, drag.startBounds, drag.currentBounds));
      }
    });
    // Neighbours that shared the dragged wall plane keep the plan watertight:
    // their adjusted outlines are committed as overrides of their own.
    followersForDeltas(drag.edgeDeltas).forEach((follower) => {
      setGeometryOverride(
        drag.variantId,
        follower.kind,
        follower.id,
        shiftFollowerPoints(follower, drag.edgeDeltas));
    });
    // Walls and doors commit exactly as the live drag drew them.
    commitWallDoorOverrides(
      drag.variantId,
      drag.liveWalls,
      drag.doors,
      buildEditPointMapper(drag.startBounds, drag.currentBounds, drag.edgeDeltas));
    state.editsSignature = state.lastRunInputSignature;
    saveDraft();
    const b = drag.currentBounds;
    setStatus(`${selectionKindLabel(drag.kind)} set to ${formatNumber(b.width, 1)} × ${formatNumber(b.height, 1)} m`);
    // A move/resize ends with a pointerup that the browser may also deliver as a
    // click; swallow that one so it does not reset the selection we just edited.
    state.suppressNextPlanClick = true;
  } else if (drag.statusBefore) {
    // A bare click (no movement) should not leave a stale "Moving room" status.
    setStatus(drag.statusBefore);
  }
  renderAll();
}

function resizeBounds(start, handle, dx, dy, floor) {
  const h = handle || "";
  let minX = start.minX;
  let minY = start.minY;
  let maxX = start.maxX;
  let maxY = start.maxY;
  // Model space is Y-up, so the "north"/top handle moves the max-Y edge.
  if (h.includes("e")) maxX = geomSnap(start.maxX + dx);
  if (h.includes("w")) minX = geomSnap(start.minX + dx);
  if (h.includes("n")) maxY = geomSnap(start.maxY + dy);
  if (h.includes("s")) minY = geomSnap(start.minY + dy);

  // Keep the opposite (anchored) edge fixed while enforcing a minimum size.
  if (h.includes("w")) {
    minX = Math.min(minX, maxX - geomMinDimension);
  } else if (h.includes("e")) {
    maxX = Math.max(maxX, minX + geomMinDimension);
  }
  if (h.includes("s")) {
    minY = Math.min(minY, maxY - geomMinDimension);
  } else if (h.includes("n")) {
    maxY = Math.max(maxY, minY + geomMinDimension);
  }

  // Never let a dragged edge leave the floorplate.
  if (floor) {
    if (h.includes("w")) minX = clamp(minX, floor.minX, maxX - geomMinDimension);
    if (h.includes("e")) maxX = clamp(maxX, minX + geomMinDimension, floor.maxX);
    if (h.includes("s")) minY = clamp(minY, floor.minY, maxY - geomMinDimension);
    if (h.includes("n")) maxY = clamp(maxY, minY + geomMinDimension, floor.maxY);
  }

  return { minX, minY, maxX, maxY, width: round(maxX - minX), height: round(maxY - minY) };
}

function translateBoundsWithin(start, dx, dy, floor) {
  let minX = geomSnap(start.minX + dx);
  let minY = geomSnap(start.minY + dy);
  if (floor && floor.width >= start.width && floor.height >= start.height) {
    minX = clamp(minX, floor.minX, floor.maxX - start.width);
    minY = clamp(minY, floor.minY, floor.maxY - start.height);
  }
  return {
    minX,
    minY,
    maxX: round(minX + start.width),
    maxY: round(minY + start.height),
    width: start.width,
    height: start.height
  };
}

// Proportionally remap a polygon from its old bounding box into a new one. For
// the common rectangular room this is an exact resize; for an L-shaped room it
// scales the outline sensibly so non-rectangular rooms still drag predictably.
function remapPolygon(points, oldBounds, newBounds) {
  const sx = oldBounds.width > 1e-6 ? newBounds.width / oldBounds.width : 1;
  const sy = oldBounds.height > 1e-6 ? newBounds.height / oldBounds.height : 1;
  return points.map((p) => ({
    x: round(newBounds.minX + (Number(p.x) - oldBounds.minX) * sx),
    y: round(newBounds.minY + (Number(p.y) - oldBounds.minY) * sy)
  }));
}

function showGeomReadout(bounds, points, kind) {
  if (!els.editReadout) {
    return;
  }
  const area = polygonArea(points);
  els.editReadout.textContent =
    `${selectionKindLabel(kind)} ${formatNumber(bounds.width, 1)} × ${formatNumber(bounds.height, 1)} m · ${formatNumber(area, 1)} m²`;
  els.editReadout.hidden = false;
}

function removeGeomChrome() {
  els.planSvg.querySelectorAll(".edit-selection-handles, .room-selected-halo-group")
    .forEach((node) => node.remove());
}

function removeGeomOverlay() {
  const existing = els.planSvg.querySelector(".geom-drag-overlay");
  if (existing) {
    existing.remove();
  }
}

function renderGeomOverlay(bounds, kind, id, points) {
  removeGeomOverlay();
  const overlay = svgEl("g", { class: "edit-selection-handles geom-drag-overlay", transform: "scale(1,-1)" });
  appendGeomResizeHandles(overlay, bounds, kind, id);
  if (points) {
    appendGeomBadge(overlay, bounds, points);
  }
  els.planSvg.appendChild(overlay);
}

// Grip size in model metres that stays a constant ~10px on screen regardless of
// zoom, so handles never balloon when zoomed in or vanish when zoomed out.
function geomHandleRadius() {
  const view = state.viewFrame
    ? state.viewFrame.width / clamp(state.zoom, 1, maxZoom)
    : 40;
  return clamp(view * 0.011, 0.12, 0.6);
}

// Selection box plus eight resize grips — square corners, round edge midpoints
// (the Figma/CAD convention). Used for the resting selection and, repainted
// each frame, during a drag.
function appendGeomResizeHandles(parent, bounds, kind, id) {
  parent.appendChild(svgEl("rect", {
    class: "edit-selection-box geom-edit-box",
    x: round(bounds.minX),
    y: round(bounds.minY),
    width: round(Math.max(bounds.width, 0)),
    height: round(Math.max(bounds.height, 0))
  }));

  const radius = geomHandleRadius();
  geomHandlePositions(bounds).forEach((pos) => {
    const handleAttrs = {
      "data-edit-action": "geom-resize",
      "data-geom-handle": pos.dir,
      "data-geom-kind": kind,
      "data-geom-id": String(id)
    };
    const group = svgEl("g", { class: `edit-handle-group geom-handle-group geom-handle-${pos.dir}`, ...handleAttrs });
    group.appendChild(svgEl("circle", {
      class: "edit-handle-hit",
      cx: round(pos.x),
      cy: round(pos.y),
      r: round(Math.max(radius * 2.1, 0.55))
    }));
    if (pos.dir.length === 2) {
      const side = radius * 1.8;
      group.appendChild(svgEl("rect", {
        class: "edit-handle geom-handle geom-handle-corner",
        ...handleAttrs,
        x: round(pos.x - side / 2),
        y: round(pos.y - side / 2),
        width: round(side),
        height: round(side),
        rx: round(side * 0.18),
        ry: round(side * 0.18)
      }));
    } else {
      group.appendChild(svgEl("circle", {
        class: "edit-handle geom-handle",
        ...handleAttrs,
        cx: round(pos.x),
        cy: round(pos.y),
        r: round(radius)
      }));
    }
    parent.appendChild(group);
  });
}

// Floating dimension pill above the dragged element: width × depth · area,
// sized for the current zoom so it reads the same at any magnification.
function appendGeomBadge(parent, bounds, points) {
  const view = state.viewFrame ? state.viewFrame.width / clamp(state.zoom, 1, maxZoom) : 40;
  const font = clamp(view * 0.021, 0.28, 1.05);
  const text = `${formatNumber(bounds.width, 1)} × ${formatNumber(bounds.height, 1)} m · ${formatNumber(polygonArea(points), 1)} m²`;
  const padX = font * 0.7;
  const boxH = font * 1.75;
  const boxW = text.length * font * 0.56 + padX * 2;
  const cx = bounds.minX + bounds.width / 2;
  const y0 = bounds.maxY + font * 0.9;
  parent.appendChild(svgEl("rect", {
    class: "geom-badge-bg",
    x: round(cx - boxW / 2),
    y: round(y0),
    width: round(boxW),
    height: round(boxH),
    rx: round(boxH / 2),
    ry: round(boxH / 2)
  }));
  const label = svgEl("text", {
    class: "geom-badge-text",
    x: round(cx),
    y: round(-(y0 + boxH * 0.7)),
    transform: "scale(1,-1)",
    "text-anchor": "middle",
    "font-size": formatNumber(font, 2)
  });
  label.textContent = text;
  parent.appendChild(label);
}

function geomHandlePositions(bounds) {
  const midX = bounds.minX + bounds.width / 2;
  const midY = bounds.minY + bounds.height / 2;
  return [
    { dir: "sw", x: bounds.minX, y: bounds.minY },
    { dir: "s", x: midX, y: bounds.minY },
    { dir: "se", x: bounds.maxX, y: bounds.minY },
    { dir: "e", x: bounds.maxX, y: midY },
    { dir: "ne", x: bounds.maxX, y: bounds.maxY },
    { dir: "n", x: midX, y: bounds.maxY },
    { dir: "nw", x: bounds.minX, y: bounds.maxY },
    { dir: "w", x: bounds.minX, y: midY }
  ];
}

// --- Boundary followers ------------------------------------------------------
// A dragged room edge is a wall plane shared with whatever sits across it. For
// the plan to stay a plan (no overlaps, no gaps) every polygon with an edge on
// that plane must follow the drag: the neighbour room shrinks as this one
// grows, the parent unit stretches with its room, the corridor gives way.
// These helpers find those polygons per edge and compute how far the plane may
// travel before something is squeezed below its minimum dimension.

const boundaryEps = 0.05;
const boundaryMinOverlap = 0.15;

// Intervals of the polygon's outline that run along the given axis line, e.g.
// the vertical edge segments at x == lineCoord. Used to decide participation.
function polygonEdgeIntervalsOnLine(points, axis, lineCoord) {
  const perp = axis === "x" ? "y" : "x";
  const intervals = [];
  for (let i = 0; i < points.length; i += 1) {
    const a = points[i];
    const b = points[(i + 1) % points.length];
    if (Math.abs(Number(a[axis]) - lineCoord) <= boundaryEps
      && Math.abs(Number(b[axis]) - lineCoord) <= boundaryEps) {
      const lo = Math.min(Number(a[perp]), Number(b[perp]));
      const hi = Math.max(Number(a[perp]), Number(b[perp]));
      if (hi - lo > 1e-6) {
        intervals.push([lo, hi]);
      }
    }
  }
  return intervals;
}

function intervalOverlap(aLo, aHi, bLo, bHi) {
  return Math.min(aHi, bHi) - Math.max(aLo, bLo);
}

// Widens [lo, hi] to the transitive closure of every polygon edge interval on
// the plane that genuinely shares wall with the span. The follower threshold
// (boundaryMinOverlap, 0.15 m) is deliberately coarse so corner-touching
// neighbours stay out — but it is far too coarse for SPAN GROWTH: an 8 cm
// sliver of shared wall still ties its room to the plane, and skipping it
// tears the plane into a partial move (half the wall travels, half stays —
// overlapping rooms and detached doors). Growth therefore uses a tiny
// positive threshold and scans EVERY polygon, not just current followers.
function absorbSpanOnLine(variant, excludeIds, axis, line, spanLo, spanHi) {
  let lo = spanLo;
  let hi = spanHi;
  const polygons = [];
  const visit = (items, kind) => {
    (items || []).forEach((item) => {
      const id = String(item && item.id || "");
      if (!item || !item.polygon || excludeIds.has(`${kind}:${id}`)) {
        return;
      }
      const points = (item.polygon.points || []).map((p) => ({ x: Number(p.x), y: Number(p.y) }));
      if (points.length >= 3) {
        polygons.push(points);
      }
    });
  };
  visit(variant.units, "unit");
  visit(variant.rooms, "room");
  visit(variant.corridors, "corridor");
  for (let pass = 0; pass < 12; pass += 1) {
    let grew = false;
    polygons.forEach((points) => {
      polygonEdgeIntervalsOnLine(points, axis, line).forEach(([a, b]) => {
        if (intervalOverlap(a, b, lo, hi) > 1e-4) {
          if (a < lo - 1e-6) { lo = a; grew = true; }
          if (b > hi + 1e-6) { hi = b; grew = true; }
        }
      });
    });
    if (!grew) {
      break;
    }
  }
  return [lo, hi];
}

// Every room/unit/corridor (except the dragged element and, for unit drags,
// its own rooms, which scale with the unit instead) that shares the boundary
// line. side = +1 when the polygon's body lies on the positive-axis side.
function collectEdgeFollowers(variant, excludeIds, axis, lineCoord, spanLo, spanHi) {
  const followers = [];
  const visit = (items, kind) => {
    (items || []).forEach((item) => {
      const id = String(item.id || "");
      if (!item.polygon || excludeIds.has(`${kind}:${id}`)) {
        return;
      }
      const points = (item.polygon.points || []).map((p) => ({ x: Number(p.x), y: Number(p.y) }));
      if (points.length < 3) {
        return;
      }
      const intervals = polygonEdgeIntervalsOnLine(points, axis, lineCoord);
      const touches = intervals.some(([lo, hi]) => intervalOverlap(lo, hi, spanLo, spanHi) > boundaryMinOverlap);
      if (!touches) {
        return;
      }
      const bounds = boundsOfPoints(points);
      if (!bounds) {
        return;
      }
      const center = axis === "x" ? bounds.minX + bounds.width / 2 : bounds.minY + bounds.height / 2;
      const side = center >= lineCoord ? 1 : -1;
      const farCoord = axis === "x"
        ? (side > 0 ? bounds.maxX : bounds.minX)
        : (side > 0 ? bounds.maxY : bounds.minY);
      followers.push({
        kind,
        id,
        points,
        side,
        farCoord,
        node: planPolygonFor(kind, id)
      });
    });
  };
  visit(variant.units, "unit");
  visit(variant.rooms, "room");
  visit(variant.corridors, "corridor");
  return followers;
}

// How far the boundary plane may travel: never past the floorplate, never so
// far that the dragged element or any follower drops below the minimum room
// dimension, and never into a fixed element (the core is a rigid obstacle —
// rooms butt against its face, they cannot swallow it).
function boundaryLineRange(followers, axis, floorBounds, line, spanLo, spanHi) {
  let min = floorBounds ? (axis === "x" ? floorBounds.minX : floorBounds.minY) : -Infinity;
  let max = floorBounds ? (axis === "x" ? floorBounds.maxX : floorBounds.maxY) : Infinity;
  followers.forEach((follower) => {
    if (follower.side > 0) {
      max = Math.min(max, follower.farCoord - geomMinDimension);
    } else {
      min = Math.max(min, follower.farCoord + geomMinDimension);
    }
  });
  fixedObstacleBounds().forEach((obstacle) => {
    const perpLo = axis === "x" ? obstacle.minY : obstacle.minX;
    const perpHi = axis === "x" ? obstacle.maxY : obstacle.maxX;
    if (intervalOverlap(perpLo, perpHi, spanLo, spanHi) <= boundaryEps) {
      return;
    }
    const near = axis === "x" ? obstacle.minX : obstacle.minY;
    const far = axis === "x" ? obstacle.maxX : obstacle.maxY;
    if (near >= line - boundaryEps) {
      max = Math.min(max, near);
    } else if (far <= line + boundaryEps) {
      min = Math.max(min, far);
    } else {
      // The plane starts inside the obstacle footprint (shouldn't happen for
      // engine output) — freeze rather than allow any travel through it.
      min = Math.max(min, line);
      max = Math.min(max, line);
    }
  });
  return { min, max };
}

// Footprints of input elements the generator treats as immovable (the core).
function fixedObstacleBounds() {
  return ((state.input && state.input.fixedElements) || [])
    .filter((fixed) => fixed && fixed.blocksGeneration !== false && fixed.polygon)
    .map((fixed) => boundsOfPoints(fixed.polygon.points))
    .filter(Boolean);
}

// Move follower vertices with their PARTICIPATING EDGES, not by raw point
// proximity. A vertex shifts for a plane delta only when one of its adjacent
// polygon edges lies on that plane with real span overlap — a corner that
// merely touches the end of someone else's span must not be dragged along
// (that phantom used to shear rectangles into trapezoids at span ends).
function shiftFollowerPoints(follower, edgeDeltas) {
  const pts = follower.points;
  // Engine rings close explicitly (last point repeats the first). Work on the
  // open ring and mirror the first vertex onto the duplicate at the end, or
  // the closing point gets left behind and the rectangle grows a hairline spur.
  const closed = pts.length > 3
    && Math.abs(Number(pts[0].x) - Number(pts[pts.length - 1].x)) <= 1e-9
    && Math.abs(Number(pts[0].y) - Number(pts[pts.length - 1].y)) <= 1e-9;
  const count = closed ? pts.length - 1 : pts.length;
  const moved = pts.map((p) => ({ x: Number(p.x), y: Number(p.y) }));
  edgeDeltas.forEach((edge) => {
    const axis = edge.axis;
    const perp = axis === "x" ? "y" : "x";
    const mask = new Array(count).fill(false);
    for (let i = 0; i < count; i += 1) {
      const a = pts[i];
      const b = pts[(i + 1) % count];
      if (Math.abs(Number(a[axis]) - edge.line) <= boundaryEps
        && Math.abs(Number(b[axis]) - edge.line) <= boundaryEps) {
        const lo = Math.min(Number(a[perp]), Number(b[perp]));
        const hi = Math.max(Number(a[perp]), Number(b[perp]));
        if (intervalOverlap(lo, hi, edge.spanLo, edge.spanHi) > boundaryEps) {
          mask[i] = true;
          mask[(i + 1) % count] = true;
        }
      }
    }
    for (let i = 0; i < count; i += 1) {
      if (mask[i]) {
        moved[i][axis] = round(moved[i][axis] + edge.delta);
      }
    }
  });
  if (closed) {
    moved[moved.length - 1] = { ...moved[0] };
  }
  return moved;
}

// The four boundary planes of the dragged element, with followers and travel
// limits resolved once at drag start.
function collectBoundaryEdges(variant, kind, id, startBounds, childRooms, floorBounds) {
  const excludeIds = new Set([`${kind}:${id}`]);
  (childRooms || []).forEach((child) => excludeIds.add(`room:${child.id}`));
  const make = (dir, axis, line, spanLo, spanHi) => {
    // Span absorption: when a neighbour's collinear edge only PARTLY overlaps
    // the dragged edge, moving just the in-span vertices would jog the wall
    // mid-edge and strand a sliver of floor area that belongs to no room. A
    // straight wall plane moves as one — so the span grows to swallow every
    // overlapping edge (and any edges THOSE newly expose) before followers
    // are final.
    const [lo, hi] = absorbSpanOnLine(variant, excludeIds, axis, line, spanLo, spanHi);
    const followers = collectEdgeFollowers(variant, excludeIds, axis, line, lo, hi);
    const range = boundaryLineRange(followers, axis, floorBounds, line, lo, hi);
    // The plane's current position is always legal, even when the engine
    // produced a neighbour thinner than the minimum — never force a jump.
    range.min = Math.min(range.min, line);
    range.max = Math.max(range.max, line);
    return { dir, axis, line, spanLo: lo, spanHi: hi, followers, range };
  };
  return {
    w: make("w", "x", startBounds.minX, startBounds.minY, startBounds.maxY),
    e: make("e", "x", startBounds.maxX, startBounds.minY, startBounds.maxY),
    s: make("s", "y", startBounds.minY, startBounds.minX, startBounds.maxX),
    n: make("n", "y", startBounds.maxY, startBounds.minX, startBounds.maxX)
  };
}

// Clamp the freshly dragged bounds so no boundary plane leaves its legal
// range. A resize clamps each edge independently; a move must clamp the
// translation as a whole so the element stops at an obstacle instead of
// squashing against it.
function clampBoundsToEdges(bounds, start, edges, mode) {
  if (mode === "move") {
    const dxLo = Math.max(edges.w.range.min - start.minX, edges.e.range.min - start.maxX);
    const dxHi = Math.min(edges.w.range.max - start.minX, edges.e.range.max - start.maxX);
    const dyLo = Math.max(edges.s.range.min - start.minY, edges.n.range.min - start.maxY);
    const dyHi = Math.min(edges.s.range.max - start.minY, edges.n.range.max - start.maxY);
    const dx = clamp(bounds.minX - start.minX, Math.min(dxLo, 0), Math.max(dxHi, 0));
    const dy = clamp(bounds.minY - start.minY, Math.min(dyLo, 0), Math.max(dyHi, 0));
    return {
      minX: round(start.minX + dx),
      minY: round(start.minY + dy),
      maxX: round(start.maxX + dx),
      maxY: round(start.maxY + dy),
      width: start.width,
      height: start.height
    };
  }
  const minX = clamp(bounds.minX, edges.w.range.min, edges.w.range.max);
  const maxX = clamp(bounds.maxX, edges.e.range.min, edges.e.range.max);
  const minY = clamp(bounds.minY, edges.s.range.min, edges.s.range.max);
  const maxY = clamp(bounds.maxY, edges.n.range.min, edges.n.range.max);
  return {
    minX,
    minY,
    maxX: Math.max(maxX, minX + geomMinDimension),
    maxY: Math.max(maxY, minY + geomMinDimension),
    width: round(Math.max(maxX - minX, geomMinDimension)),
    height: round(Math.max(maxY - minY, geomMinDimension))
  };
}

// Per-edge plane deltas implied by the new bounds. Only edges that actually
// moved are returned, each carrying its original line and span for matching.
function boundaryEdgeDeltas(edges, startBounds, bounds) {
  const moves = [
    { dir: "w", value: bounds.minX - startBounds.minX },
    { dir: "e", value: bounds.maxX - startBounds.maxX },
    { dir: "s", value: bounds.minY - startBounds.minY },
    { dir: "n", value: bounds.maxY - startBounds.maxY }
  ];
  return moves
    .filter((move) => Math.abs(move.value) > 1e-6)
    .map((move) => {
      const edge = edges[move.dir];
      return {
        dir: move.dir,
        axis: edge.axis,
        line: edge.line,
        spanLo: edge.spanLo,
        spanHi: edge.spanHi,
        delta: round(move.value),
        followers: edge.followers
      };
    });
}

// Distinct followers across the supplied deltas; a corner drag can touch the
// same neighbour on two planes, which must resolve to one combined shift.
function followersForDeltas(edgeDeltas) {
  const byKey = new Map();
  edgeDeltas.forEach((edge) => {
    edge.followers.forEach((follower) => {
      const key = `${follower.kind}:${follower.id}`;
      if (!byKey.has(key)) {
        byKey.set(key, follower);
      }
    });
  });
  return Array.from(byKey.values());
}

// --- Direct wall dragging ----------------------------------------------------
// In edit mode any straight interior wall is itself a handle: grab it and the
// whole wall plane slides, with the rooms on both sides resizing in lockstep.
// This is the move architects actually make — push a partition, not "scale a
// room rectangle" — so it gets first-class treatment.

function wallDragCandidate(variant, wallId) {
  const wall = (variant.walls || []).find((item) => String(item.id || "") === String(wallId || ""));
  if (!wall || !wall.centerline || !wall.centerline.start || !wall.centerline.end) {
    return null;
  }
  const sx = Number(wall.centerline.start.x);
  const sy = Number(wall.centerline.start.y);
  const ex = Number(wall.centerline.end.x);
  const ey = Number(wall.centerline.end.y);
  let axis = null;
  if (Math.abs(sx - ex) <= boundaryEps && Math.abs(sy - ey) > boundaryEps) {
    axis = "x";
  } else if (Math.abs(sy - ey) <= boundaryEps && Math.abs(sx - ex) > boundaryEps) {
    axis = "y";
  }
  if (!axis) {
    return null;
  }
  const line = axis === "x" ? round((sx + ex) / 2) : round((sy + ey) / 2);
  const spanLo = axis === "x" ? Math.min(sy, ey) : Math.min(sx, ex);
  const spanHi = axis === "x" ? Math.max(sy, ey) : Math.max(sx, ex);
  return { wall, axis, line, spanLo, spanHi };
}

function beginWallDrag(event, point, wallId) {
  const variant = selectedVariant(currentVisualOutput());
  if (!variant || !variant.variantId) {
    return false;
  }
  const candidate = wallDragCandidate(variant, wallId);
  if (!candidate) {
    return false;
  }
  const floorBounds = state.input && state.input.floorplate && state.input.floorplate.outer
    ? boundsOfPoints(state.input.floorplate.outer.points)
    : null;
  // Facade walls trace the floorplate itself; that contour is an input
  // constraint, edited from the brief, not by dragging the drawing.
  if (floorBounds) {
    const onFloorEdge = candidate.axis === "x"
      ? Math.abs(candidate.line - floorBounds.minX) <= boundaryEps || Math.abs(candidate.line - floorBounds.maxX) <= boundaryEps
      : Math.abs(candidate.line - floorBounds.minY) <= boundaryEps || Math.abs(candidate.line - floorBounds.maxY) <= boundaryEps;
    if (onFloorEdge) {
      setStatus("Facade wall — resize the floorplate from the Design Brief");
      return false;
    }
  }
  // Span absorption (see absorbSpanOnLine): the plane a wall lives on moves
  // as one, so the grabbed segment's span grows to cover every overlapping
  // room/unit edge — sliver overlaps included — before followers are final.
  const [absorbedLo, absorbedHi] = absorbSpanOnLine(
    variant, new Set(), candidate.axis, candidate.line, candidate.spanLo, candidate.spanHi);
  candidate.spanLo = absorbedLo;
  candidate.spanHi = absorbedHi;
  const followers = collectEdgeFollowers(
    variant, new Set(), candidate.axis, candidate.line, candidate.spanLo, candidate.spanHi);
  if (!followers.length) {
    return false;
  }
  const range = boundaryLineRange(
    followers, candidate.axis, floorBounds, candidate.line, candidate.spanLo, candidate.spanHi);
  range.min = Math.min(range.min, candidate.line);
  range.max = Math.max(range.max, candidate.line);

  const fadeNodes = [];
  followers.forEach((follower) => {
    if (follower.kind === "room") {
      els.planSvg.querySelectorAll(`[data-room-ref="${follower.id}"]`).forEach((node) => fadeNodes.push(node));
    }
  });
  (variant.doorsOpenings || []).forEach((door) => {
    const p = door && door.location;
    if (!p) {
      return;
    }
    const onLine = candidate.axis === "x"
      ? Math.abs(Number(p.x) - candidate.line) <= boundaryEps * 4
      : Math.abs(Number(p.y) - candidate.line) <= boundaryEps * 4;
    if (onLine) {
      els.planSvg.querySelectorAll(`[data-select-kind="door"][data-select-id="${door.id}"]`)
        .forEach((node) => fadeNodes.push(node));
    }
  });

  state.wallDrag = {
    variantId: variant.variantId,
    axis: candidate.axis,
    line: candidate.line,
    spanLo: candidate.spanLo,
    spanHi: candidate.spanHi,
    followers,
    range,
    delta: 0,
    moved: false,
    startPoint: point,
    snapshot: captureSnapshot(),
    liveWalls: collectLiveWallsOnLine(variant, candidate),
    doors: (variant.doorsOpenings || []).slice(),
    fadeNodes,
    statusBefore: els.runStatus ? els.runStatus.textContent : ""
  };
  try {
    els.planSvg.setPointerCapture(event.pointerId);
  } catch (_) {
    // Pointer capture only smooths the drag.
  }
  els.previewFrame.classList.add("is-dragging");
  fadeNodes.forEach((node) => node.classList.add("geom-stale"));
  removeGeomChrome();
  return true;
}

// Walls with an endpoint resting on the dragged plane: the collinear wall
// itself plus every perpendicular wall that tees into it. A wall LYING on the
// plane whose segment overlaps the moved span travels as a whole, even when
// one endpoint pokes past the span — moving only the in-span end is what used
// to bend straight walls into diagonals.
function collectLiveWallsOnLine(variant, candidate) {
  const refs = [];
  const main = (p) => (candidate.axis === "x" ? Number(p.x) : Number(p.y));
  const perp = (p) => (candidate.axis === "x" ? Number(p.y) : Number(p.x));
  (variant.walls || []).forEach((wall) => {
    if (!wall || !wall.centerline || !wall.centerline.start || !wall.centerline.end) {
      return;
    }
    const start = wall.centerline.start;
    const end = wall.centerline.end;
    const within = (p) => Math.abs(main(p) - candidate.line) <= boundaryEps
      && perp(p) >= candidate.spanLo - boundaryEps && perp(p) <= candidate.spanHi + boundaryEps;
    let startIn = within(start);
    let endIn = within(end);
    const collinear = Math.abs(main(start) - candidate.line) <= boundaryEps
      && Math.abs(main(end) - candidate.line) <= boundaryEps;
    if (collinear) {
      const lo = Math.min(perp(start), perp(end));
      const hi = Math.max(perp(start), perp(end));
      if (intervalOverlap(lo, hi, candidate.spanLo, candidate.spanHi) > boundaryEps) {
        startIn = true;
        endIn = true;
      }
    }
    if (!startIn && !endIn) {
      return;
    }
    const parts = Array.from(els.planSvg.querySelectorAll(`[data-wall-ref="${wall.id}"]`));
    refs.push({ wall, startIn, endIn, parts, thickness: wallStrokeWidth(wall) });
  });
  return refs;
}

function updateWallDrag(event) {
  const drag = state.wallDrag;
  if (!drag) {
    return;
  }
  event.preventDefault();
  const point = clientToModelPoint(event);
  if (!point) {
    return;
  }
  state.snapSuspended = Boolean(event.shiftKey);
  const raw = (drag.axis === "x" ? point.x : point.y) - drag.line;
  const target = clamp(geomSnap(drag.line + raw), drag.range.min, drag.range.max);
  drag.delta = round(target - drag.line);
  if (!drag.moved && Math.abs(drag.delta) > 1e-6) {
    drag.moved = true;
    setStatus("Moving wall");
  }

  const edgeDeltas = [{
    axis: drag.axis,
    line: drag.line,
    spanLo: drag.spanLo,
    spanHi: drag.spanHi,
    delta: drag.delta,
    followers: drag.followers
  }];
  drag.followers.forEach((follower) => {
    if (follower.node) {
      follower.node.setAttribute("points", pointsToAttr(shiftFollowerPoints(follower, edgeDeltas)));
    }
  });
  const liveMapper = buildEditPointMapper(null, null, edgeDeltas);
  (drag.liveWalls || []).forEach((ref) => updateLiveWall(ref, liveMapper));

  renderWallDragGuide(drag);
  if (els.editReadout) {
    const sides = wallDragSideReadout(drag);
    els.editReadout.textContent = `Wall ${drag.axis === "x" ? "x" : "y"} ${formatNumber(drag.line + drag.delta, 2)} m`
      + (sides ? ` · ${sides}` : "")
      + ` · Δ ${drag.delta >= 0 ? "+" : ""}${formatNumber(drag.delta, 2)} m`;
    els.editReadout.hidden = false;
  }
}

// "3.1 m | 4.7 m" — the resulting clear dimensions either side of the plane,
// taken from the tightest follower on each side.
function wallDragSideReadout(drag) {
  let below = null;
  let above = null;
  drag.followers.forEach((follower) => {
    const depth = Math.abs(follower.farCoord - (drag.line + drag.delta));
    if (follower.side > 0) {
      above = above == null ? depth : Math.min(above, depth);
    } else {
      below = below == null ? depth : Math.min(below, depth);
    }
  });
  if (below == null && above == null) {
    return "";
  }
  const fmt = (value) => (value == null ? "—" : `${formatNumber(value, 1)} m`);
  return `${fmt(below)} | ${fmt(above)}`;
}

function renderWallDragGuide(drag) {
  removeWallDragGuide();
  const overlay = svgEl("g", { class: "wall-drag-guide-group geom-drag-overlay", transform: "scale(1,-1)" });
  const linePos = drag.line + drag.delta;
  const guide = drag.axis === "x"
    ? lineEl({ x: linePos, y: drag.spanLo }, { x: linePos, y: drag.spanHi }, "wall-drag-guide")
    : lineEl({ x: drag.spanLo, y: linePos }, { x: drag.spanHi, y: linePos }, "wall-drag-guide");
  overlay.appendChild(guide);
  els.planSvg.appendChild(overlay);
}

function removeWallDragGuide() {
  const existing = els.planSvg.querySelector(".wall-drag-guide-group");
  if (existing) {
    existing.remove();
  }
}

// Abort whichever direct-manipulation drag is in flight without committing:
// the next renderAll repaints every polygon and wall from unchanged state.
function cancelActiveDrag() {
  const geom = state.geomDrag;
  const wall = state.wallDrag;
  state.geomDrag = null;
  state.wallDrag = null;
  state.snapSuspended = false;
  els.previewFrame.classList.remove("is-dragging");
  removeGeomOverlay();
  removeWallDragGuide();
  [geom, wall].forEach((drag) => {
    if (!drag) {
      return;
    }
    (drag.fadeNodes || []).forEach((node) => node.classList.remove("geom-stale"));
    if (drag.statusBefore) {
      setStatus(drag.statusBefore);
    }
  });
  if (geom && geom.polygon) {
    geom.polygon.classList.remove("geom-editing");
  }
  state.suppressNextPlanClick = true;
  renderAll();
}

function finishWallDrag() {
  const drag = state.wallDrag;
  if (!drag) {
    return;
  }
  state.wallDrag = null;
  state.snapSuspended = false;
  els.previewFrame.classList.remove("is-dragging");
  removeWallDragGuide();
  (drag.fadeNodes || []).forEach((node) => node.classList.remove("geom-stale"));

  if (drag.moved && Math.abs(drag.delta) > 1e-6) {
    pushUndoSnapshot(drag.snapshot);
    const edgeDeltas = [{
      axis: drag.axis,
      line: drag.line,
      spanLo: drag.spanLo,
      spanHi: drag.spanHi,
      delta: drag.delta,
      followers: drag.followers
    }];
    drag.followers.forEach((follower) => {
      setGeometryOverride(drag.variantId, follower.kind, follower.id, shiftFollowerPoints(follower, edgeDeltas));
    });
    commitWallDoorOverrides(
      drag.variantId, drag.liveWalls, drag.doors, buildEditPointMapper(null, null, edgeDeltas));
    state.editsSignature = state.lastRunInputSignature;
    saveDraft();
    setStatus(`Wall moved ${drag.delta >= 0 ? "+" : ""}${formatNumber(drag.delta, 2)} m`);
    state.suppressNextPlanClick = true;
  } else if (drag.statusBefore) {
    setStatus(drag.statusBefore);
  }
  renderAll();
}

function applyCanvasEdit(edit, point) {
  const input = ensureInputShape(edit.startInput);
  const floorBounds = boundsOfPoints(edit.startInput.floorplate.outer.points);
  if (!floorBounds) {
    return null;
  }

  if (edit.action === "floor-width" || edit.action === "floor-depth" || edit.action === "floor-size") {
    const dx = point.x - edit.startPoint.x;
    const dy = point.y - edit.startPoint.y;
    // The floorplate snaps to the same 0.5 m grid the form steppers use, so a
    // drag never produces "39.53 m" inputs that read like noise.
    const snapHalf = (value) => Math.round(value * 2) / 2;
    const width = edit.action === "floor-depth"
      ? floorBounds.width
      : clamp(snapHalf(floorBounds.width + dx), 8, 300);
    const depth = edit.action === "floor-width"
      ? floorBounds.height
      : clamp(snapHalf(floorBounds.height + dy), 8, 300);
    resizeFloorplateInput(input, edit.startInput, width, depth);
    clampCoreIntoFloorplate(input);
    return input;
  }

  if (edit.action === "unit-target-area" && edit.selection && edit.selection.kind === "unit") {
    const target = ensureUnitTarget(input, edit.selection.unitType || "studio");
    const bounds = edit.selection.bounds;
    const width = clamp(round(point.x - bounds.minX), 2, 60);
    const depth = clamp(round(point.y - bounds.minY), 2, 60);
    const area = clamp(round(width * depth), 10, 400);
    target.minArea = Math.max(10, round(area * 0.9));
    target.maxArea = Math.max(target.minArea, round(area * 1.1));
    return input;
  }

  if (edit.action === "room-min-size" && edit.selection && edit.selection.kind === "room") {
    const bounds = edit.selection.bounds;
    const width = clamp(round(point.x - bounds.minX), 1.2, 25);
    const depth = clamp(round(point.y - bounds.minY), 1.2, 25);
    input.rules.minRoomWidth = clamp(round(Math.min(width, depth)), 1.2, 20);
    input.rules.minRoomDepth = clamp(round(Math.max(width, depth)), 1.2, 25);
    return input;
  }

  if (edit.action === "corridor-width" && edit.selection && edit.selection.kind === "corridor") {
    const bounds = edit.selection.bounds;
    const centerX = bounds.minX + bounds.width / 2;
    const centerY = bounds.minY + bounds.height / 2;
    const horizontal = bounds.width >= bounds.height;
    const width = horizontal
      ? Math.abs(point.y - centerY) * 2
      : Math.abs(point.x - centerX) * 2;
    input.rules.minCorridorWidth = clamp(round(width), 0.9, 12);
    return input;
  }

  const core = firstCore(input);
  const startCore = firstCore(edit.startInput);
  const startCoreBounds = startCore && startCore.polygon ? boundsOfPoints(startCore.polygon.points) : null;
  if (!core || !startCoreBounds) {
    return null;
  }

  const currentFloorBounds = boundsOfPoints(input.floorplate.outer.points) || floorBounds;
  if (edit.action === "core-move") {
    const dx = point.x - edit.startPoint.x;
    const dy = point.y - edit.startPoint.y;
    // Snap to the same 0.1 m grid as room edits so the form reads clean numbers.
    const x = clamp(geomSnap(startCoreBounds.minX + dx), currentFloorBounds.minX, currentFloorBounds.maxX - startCoreBounds.width);
    const y = clamp(geomSnap(startCoreBounds.minY + dy), currentFloorBounds.minY, currentFloorBounds.maxY - startCoreBounds.height);
    core.polygon.points = rectPoints(x, y, startCoreBounds.width, startCoreBounds.height);
    refreshAccessFromCore(input);
    return input;
  }

  if (edit.action === "core-size") {
    const width = clamp(geomSnap(point.x - startCoreBounds.minX), 1, currentFloorBounds.maxX - startCoreBounds.minX);
    const depth = clamp(geomSnap(point.y - startCoreBounds.minY), 1, currentFloorBounds.maxY - startCoreBounds.minY);
    core.polygon.points = rectPoints(startCoreBounds.minX, startCoreBounds.minY, width, depth);
    refreshAccessFromCore(input);
    return input;
  }

  return null;
}

function resizeFloorplateInput(input, sourceInput, width, depth) {
  const sourceBounds = boundsOfPoints(sourceInput.floorplate.outer.points);
  input.floorplate.outer.points = scalePointsToBox(sourceInput.floorplate.outer.points, sourceBounds, width, depth);
  input.floorplate.holes = (sourceInput.floorplate.holes || []).map((hole) => ({
    ...hole,
    points: Array.isArray(hole.points) && hole.points.length
      ? scalePointsToBox(hole.points, sourceBounds, width, depth)
      : []
  }));
}

function clampCoreIntoFloorplate(input) {
  const floorBounds = boundsOfPoints(input.floorplate.outer.points);
  const core = firstCore(input);
  const coreBounds = core && core.polygon ? boundsOfPoints(core.polygon.points) : null;
  if (!floorBounds || !core || !coreBounds) {
    return;
  }

  const width = Math.min(coreBounds.width, floorBounds.width);
  const depth = Math.min(coreBounds.height, floorBounds.height);
  const x = clamp(coreBounds.minX, floorBounds.minX, floorBounds.maxX - width);
  const y = clamp(coreBounds.minY, floorBounds.minY, floorBounds.maxY - depth);
  core.polygon.points = rectPoints(x, y, width, depth);
  refreshAccessFromCore(input);
}

function ensureCore(input) {
  let core = firstCore(input);
  if (!core) {
    core = {
      id: "core-01",
      type: "core",
      blocksGeneration: true,
      polygon: { id: "core-01", points: rectPoints(18, 8, 6, 6) }
    };
    input.fixedElements.push(core);
  }

  core.id = core.id || "core-01";
  core.type = core.type || "core";
  core.blocksGeneration = core.blocksGeneration !== false;
  core.polygon = core.polygon || { id: core.id, points: rectPoints(18, 8, 6, 6) };
  core.polygon.id = core.polygon.id || core.id;
  core.polygon.points = Array.isArray(core.polygon.points) && core.polygon.points.length >= 4
    ? core.polygon.points
    : rectPoints(18, 8, 6, 6);
  return core;
}

function refreshAccessFromCore(input) {
  const core = firstCore(input);
  const bounds = core && core.polygon ? boundsOfPoints(core.polygon.points) : null;
  if (!bounds) {
    return;
  }

  const coreCenterX = round(bounds.minX + bounds.width / 2);
  input.access = input.access || {};
  input.access.entryPoints = [{ x: coreCenterX, y: 0 }];
  input.access.verticalCoreAccess = [{ x: coreCenterX, y: round(bounds.maxY) }];
  input.access.corridorStartPoints = input.access.corridorStartPoints || [];
  input.access.corridorEndPoints = input.access.corridorEndPoints || [];
  input.access.corridorCenterlines = input.access.corridorCenterlines || [];
}

function ensureUnitTarget(input, type) {
  input.program = input.program || {};
  input.program.targetUnitTypes = Array.isArray(input.program.targetUnitTypes)
    ? input.program.targetUnitTypes
    : [];
  let target = input.program.targetUnitTypes.find((item) => item.type === type);
  if (!target) {
    target = defaultUnitTarget(type);
    input.program.targetUnitTypes.push(target);
  }

  return target;
}

function normalizeUnitTargetRatios(targets) {
  const usableTargets = (targets || []).filter((target) => Number(target.targetRatio) > 0);
  const total = usableTargets.reduce((sum, target) => sum + Number(target.targetRatio), 0);
  if (total <= 0) {
    return;
  }

  usableTargets.forEach((target) => {
    target.targetRatio = round(Number(target.targetRatio) / total);
  });
}

function editSummary(input) {
  if (!input || !input.floorplate || !input.floorplate.outer) {
    return "";
  }

  const floorBounds = boundsOfPoints(input.floorplate.outer.points);
  const core = firstCore(input);
  const coreBounds = core && core.polygon ? boundsOfPoints(core.polygon.points) : null;
  const floorText = floorBounds
    ? `Floor ${formatNumber(floorBounds.width, 1)} × ${formatNumber(floorBounds.height, 1)} m`
    : "Floor outline";
  const coreText = coreBounds
    ? `Core ${formatNumber(coreBounds.width, 1)} × ${formatNumber(coreBounds.height, 1)} m at ` +
      `${formatNumber(coreBounds.minX, 1)}, ${formatNumber(coreBounds.minY, 1)}`
    : "No core";
  return `${floorText} · ${coreText}`;
}

function renderInputEditHandles(group, input) {
  if (!state.editMode || !input || state.viewMode === "axon") {
    return;
  }

  const floorBounds = input.floorplate && input.floorplate.outer
    ? boundsOfPoints(input.floorplate.outer.points)
    : null;
  if (!floorBounds) {
    return;
  }

  // Quiet, label-free grips that sit exactly on the constraint geometry: three
  // on the floorplate boundary (E width, N depth, NE corner) and two on the
  // core (centre move, corner resize). Everything else on the canvas is edited
  // by grabbing the drawing itself, so the input chrome stays whisper-thin.
  const handleRadius = geomHandleRadius();
  const floorGroup = svgEl("g", { class: "edit-handles" });
  floorGroup.appendChild(editHandle(
    "floor-width", floorBounds.maxX, floorBounds.minY + floorBounds.height / 2, handleRadius,
    "Drag to set floorplate width", "ew"));
  floorGroup.appendChild(editHandle(
    "floor-depth", floorBounds.minX + floorBounds.width / 2, floorBounds.maxY, handleRadius,
    "Drag to set floorplate depth", "ns"));
  floorGroup.appendChild(editHandle(
    "floor-size", floorBounds.maxX, floorBounds.maxY, handleRadius * 1.05,
    "Drag to resize the floorplate", "nesw"));

  const core = firstCore(input);
  const coreBounds = core && core.polygon ? boundsOfPoints(core.polygon.points) : null;
  if (coreBounds) {
    // The whole core is its own move handle. The grab surface is an invisible
    // overlay appended here — above the walls — so a press inside the core
    // can never be stolen by a wall's hit zone.
    const grab = polygonEl(core.polygon.points, "core-grab-overlay", {
      "data-edit-action": "core-move",
      ...selectableAttributes("core", core.id || "core")
    });
    const grabTitle = svgEl("title");
    grabTitle.textContent = "Drag to move the core";
    grab.appendChild(grabTitle);
    floorGroup.appendChild(grab);
    floorGroup.appendChild(editHandle(
      "core-size", coreBounds.maxX, coreBounds.maxY, handleRadius,
      "Drag to resize the core", "nesw"));
  }

  group.appendChild(floorGroup);
}

function renderSelectionConstraintHandles(group, output) {
  if (!state.editMode || state.viewMode === "axon") {
    return;
  }

  const detail = selectedElementDetails(output);
  if (!detail || !detail.bounds || detail.source !== "generated") {
    return;
  }

  const bounds = detail.bounds;
  const editGroup = svgEl("g", { class: "edit-selection-handles" });

  if (detail.kind === "unit" || detail.kind === "room") {
    // Rooms and units are resized directly: eight live grips + body drag-to-move.
    appendGeomResizeHandles(editGroup, bounds, detail.kind, detail.id);
  } else {
    // Corridors and other generated shapes show a plain selection box; their
    // long edges slide via direct wall dragging like everything else.
    editGroup.appendChild(svgEl("rect", {
      class: "edit-selection-box",
      x: round(bounds.minX),
      y: round(bounds.minY),
      width: round(bounds.width),
      height: round(bounds.height)
    }));
  }

  group.appendChild(editGroup);
}

function editHandle(action, x, y, radius, label, cursor = "") {
  const visible = Math.max(radius, 0.16);
  // The visible dot is small for a clean drawing, so wrap it in a generous invisible
  // grab halo: pointers landing anywhere in the halo still resolve to this handle via
  // closest("[data-edit-action]"), making resize/move drags forgiving to seize.
  const group = svgEl("g", {
    class: `edit-handle-group edit-${action}${cursor ? ` cursor-${cursor}` : ""}`,
    "data-edit-action": action
  });
  group.appendChild(svgEl("circle", {
    class: "edit-handle-hit",
    cx: round(x),
    cy: round(y),
    r: round(Math.max(visible * 2.1, 0.6))
  }));
  const handle = svgEl("circle", {
    class: `edit-handle edit-${action}`,
    "data-edit-action": action,
    cx: round(x),
    cy: round(y),
    r: round(visible)
  });
  group.appendChild(handle);
  const title = svgEl("title");
  title.textContent = label;
  group.appendChild(title);
  return group;
}

// Canvas quick-action chips were retired deliberately: floating buttons over
// the drawing hid the very rooms being edited. The same actions live in the
// Selection inspector, and the drawing itself is now the manipulation surface.

function renderLegend(hasVariant) {
  // The legend only lists what this plan actually contains: a single dwelling
  // has no core and no corridor, so those chips would just be noise.
  const variant = hasVariant ? selectedVariant(currentVisualOutput()) : null;
  const hasCore = (((state.input || {}).fixedElements) || []).length > 0;
  const hasCorridor = Boolean(variant && (variant.corridors || []).length);
  const items = [["Boundary", "boundary-swatch"]];
  if (hasCore) {
    items.push(["Core", "core-swatch"]);
  }
  if (hasVariant) {
    items.push(["Units", "unit-swatch"], ["Rooms", "room-swatch"]);
    if (hasCorridor) {
      items.push(["Corridor", "corridor-swatch"]);
    }
    items.push(["Doors", "door-swatch"]);
  }

  els.legendRow.innerHTML = items
    .map(([label, className]) => `<span><i class="${className}"></i>${escapeHtml(label)}</span>`)
    .join("");
}

function renderMetrics(output) {
  const variant = selectedVariant(output);
  const metadata = output ? output.metadata : null;
  const metrics = variant ? variant.metrics : null;
  const floorplate = metadata ? metadata.floorplate : null;
  const checks = variant && variant.validation && Array.isArray(variant.validation.checks) ? variant.validation.checks : [];
  const failed = checks.filter((check) => !check.passed);
  const netGross = metrics
    ? formatNumber(metrics.netGrossRatio, 3)
    : floorplate
      ? formatNumber(floorplate.usableArea / Math.max(1, floorplate.grossArea), 3)
      : "-";
  const rows = [
    ["Status", output ? friendlyStatus(output.status) : "Ready"],
    ["Units", variant ? String(variant.units.length) : "-"],
    ["Sellable", metrics ? `${formatNumber(metrics.sellableArea, 1)} m²` : "-"],
    ["Circulation", metrics ? `${formatNumber(metrics.corridorArea, 1)} m²` : "-"],
    ["Net/Gross", netGross],
    ["Checks", checks.length ? (failed.length ? `${failed.length} open` : `${checks.length} passed`) : "-"]
  ];

  els.metricsRow.innerHTML = rows.map(([label, value]) => `
    <div>
      <span>${escapeHtml(label)}</span>
      <strong>${escapeHtml(value)}</strong>
    </div>
  `).join("");
}

function renderPlanTitleBlock(output) {
  const block = els.planTitleBlock;
  if (!block) {
    return;
  }

  const variant = selectedVariant(output);
  if (!variant || state.viewMode !== "plan") {
    block.hidden = true;
    block.innerHTML = "";
    return;
  }

  const metadata = output ? output.metadata : null;
  const floorplate = metadata ? metadata.floorplate : null;
  const metrics = variant.metrics || {};
  const net = Number(metrics.sellableArea);
  const gross = floorplate ? Number(floorplate.grossArea) : NaN;
  const ratio = Number(metrics.netGrossRatio);
  const efficiency = Number.isFinite(ratio)
    ? ratio
    : (Number.isFinite(net) && Number.isFinite(gross) && gross > 0 ? net / gross : NaN);
  const projectName = formatProjectName(output ? output.projectId : "");
  const rows = [
    ["Units", String((variant.units || []).length)],
    ["Rooms", String((variant.rooms || []).length)],
    ["Net area", Number.isFinite(net) ? `${formatNumber(net, 0)} m²` : "-"],
    ["Gross area", Number.isFinite(gross) ? `${formatNumber(gross, 0)} m²` : "-"],
    ["Efficiency", Number.isFinite(efficiency) ? `${formatNumber(efficiency * 100, 0)}%` : "-"]
  ];
  const schedule = rows
    .map(([key, value]) => `<div><dt>${escapeHtml(key)}</dt><dd>${escapeHtml(value)}</dd></div>`)
    .join("");
  const subtitle = projectName ? `Main Level &middot; ${escapeHtml(projectName)}` : "Main Level";
  block.innerHTML = `
    <div class="title-block-head">
      <strong>FLOOR PLAN</strong>
      <span>${subtitle}</span>
    </div>
    <dl class="title-block-schedule">${schedule}</dl>
  `;
  block.hidden = false;
}

function formatProjectName(id) {
  const text = String(id || "").trim();
  if (!text) {
    return "";
  }
  return text.replace(/[-_]+/g, " ").replace(/\b\w/g, (char) => char.toUpperCase());
}

function renderVariants(output) {
  const variants = output && Array.isArray(output.variants) ? output.variants : [];
  els.variantCountLabel.textContent = String(variants.length);
  if (variants.length === 0) {
    els.variantList.innerHTML = `<div class="empty-list">Generate variants to compare score, unit mix, validation, and export readiness.</div>`;
    return;
  }

  els.variantList.innerHTML = "";
  variants.forEach((variant, index) => {
    const metrics = variant.metrics || {};
    const units = Array.isArray(variant.units) ? variant.units : [];
    const checks = variant.validation && Array.isArray(variant.validation.checks) ? variant.validation.checks : [];
    const failedChecks = checks.filter((check) => !check.passed);
    const warnings = failedChecks.filter((check) => String(check.severity || "").toLowerCase() !== "error");
    const errors = failedChecks.length - warnings.length;
    const hypergraph = variant.topology ? variant.topology.hypergraph : null;
    const scoreWidth = Math.round(clamp(metrics.score || 0, 0, 1) * 100);
    const mix = unitMixSummary(units);
    const errorText = errors ? `${errors} fail${errors === 1 ? "" : "s"}` : "";
    const warningText = warnings.length
      ? `${warnings.length} warning${warnings.length === 1 ? "" : "s"}`
      : "";
    const checkText = checks.length === 0
      ? "No checks"
      : failedChecks.length === 0
        ? `${checks.length} checks passed`
        : `${errorText}${errors && warnings.length ? ", " : ""}${warningText}`;
    const edits = state.geometryEdits[variant.variantId];
    const hasEdits = Boolean(edits && Object.keys(edits).length);
    const sellable = Number(metrics.sellableArea);
    const item = document.createElement("button");
    item.type = "button";
    item.className = `variant-item${variant.variantId === state.selectedVariantId ? " active" : ""}`;
    item.setAttribute("aria-pressed", variant.variantId === state.selectedVariantId ? "true" : "false");
    item.innerHTML = `
      <div class="variant-title">
        <span>#${index + 1} ${escapeHtml(variant.variantId)}</span>
        <span class="variant-title-pills">
          ${hasEdits ? '<span class="pill edited" title="This variant carries manual studio edits">Edited</span>' : ""}
          <span class="pill ${escapeHtml(variant.status)}">${escapeHtml(friendlyStatus(variant.status))}</span>
        </span>
      </div>
      <div class="variant-card-body">
        ${variantThumbnailSvg(variant, state.input)}
        <div>
          <div class="variant-meta">
            Score ${formatNumber(metrics.score, 3)} · Net/gross ${formatNumber(metrics.netGrossRatio, 3)} · ${units.length} units
          </div>
          <div class="variant-meta">
            ${escapeHtml(mix || "No unit mix")} · ${escapeHtml(checkText)}
          </div>
          <div class="variant-meta">
            ${Number.isFinite(sellable) ? `Sellable ${formatNumber(sellable, 1)} m²` : "Sellable —"}
            ${hypergraph ? ` · ${countOf(hypergraph.nodes)} graph nodes` : ""}
          </div>
        </div>
      </div>
      <progress class="score-bar" value="${scoreWidth}" max="100" aria-label="Variant score ${scoreWidth}%"></progress>
    `;
    item.addEventListener("click", () => {
      state.selectedVariantId = variant.variantId;
      // Same reset the dropdown applies: a fresh variant starts at fit view.
      state.zoom = 1;
      state.panX = 0;
      state.panY = 0;
      renderAll();
    });
    els.variantList.appendChild(item);
  });
}

function renderDiagnostics(output) {
  const summary = diagnosticSummary(output);
  const diagnostics = summary.diagnostics;
  els.issueCountLabel.textContent = diagnostics.length === 0
    ? "0"
    : summary.errorCount
      ? `${summary.errorCount} fail${summary.errorCount === 1 ? "" : "s"}`
      : `${summary.reviewNoteCount} note${summary.reviewNoteCount === 1 ? "" : "s"}`;
  if (diagnostics.length === 0) {
    els.diagnosticList.innerHTML = `<div class="empty-list good">No blocking issues found. This variant is ready to review or export.</div>`;
    return;
  }

  const visibleDiagnostics = 5;
  const diagnosticMarkup = diagnostics.slice(0, visibleDiagnostics).map((diagnostic) => `
    <div class="diagnostic-item ${escapeHtml(diagnostic.severity || "info")}">
      <div class="diagnostic-title">
        <span>${escapeHtml(humanizeCode(diagnostic.code || diagnostic.name || "diagnostic"))}</span>
        <span class="pill ${escapeHtml(diagnostic.severity || "info")}">${escapeHtml(friendlySeverity(diagnostic.severity))}</span>
      </div>
      <div class="diagnostic-message">${escapeHtml(friendlyDiagnosticMessage(diagnostic))}</div>
      ${diagnostic.sourceId ? `<div class="diagnostic-source">${escapeHtml(diagnostic.sourceId)}</div>` : ""}
    </div>
  `).join("");

  const hiddenDiagnosticCount = diagnostics.length - visibleDiagnostics;
  const hiddenDiagnosticMarkup = hiddenDiagnosticCount > 0
    ? `
      <div class="empty-list">
        ${hiddenDiagnosticCount} more review note${hiddenDiagnosticCount === 1 ? "" : "s"} in Output JSON
      </div>
    `
    : "";
  els.diagnosticList.innerHTML = diagnosticMarkup + hiddenDiagnosticMarkup;
}

function renderSchedule(output) {
  const variant = selectedVariant(output);
  const units = variant && Array.isArray(variant.units) ? variant.units : [];
  els.unitCountLabel.textContent = String(units.length);
  if (units.length === 0) {
    els.unitSchedule.innerHTML = `<div class="empty-list">No generated units yet. Generate a layout to see the aggregated unit schedule.</div>`;
    return;
  }

  const summaryRows = aggregateUnits(units);
  const totalArea = summaryRows.reduce((sum, row) => sum + row.totalArea, 0);
  els.unitSchedule.innerHTML = `
    <table>
      <thead>
        <tr>
          <th>Type</th>
          <th>Count</th>
          <th>% Total</th>
          <th>Avg Area</th>
          <th>Min</th>
          <th>Max</th>
          <th>Total Area</th>
        </tr>
      </thead>
      <tbody>
        ${summaryRows.map((row) => `
          <tr>
            <td>${escapeHtml(displayUnitType(row.type))}</td>
            <td>${row.count}</td>
            <td>${formatPercent(row.count / Math.max(1, units.length), 0)}</td>
            <td>${formatNumber(row.averageArea, 1)} m²</td>
            <td>${formatNumber(row.minArea, 1)} m²</td>
            <td>${formatNumber(row.maxArea, 1)} m²</td>
            <td>${formatNumber(row.totalArea, 1)} m²</td>
          </tr>
        `).join("")}
        <tr>
          <td><strong>Total</strong></td>
          <td><strong>${units.length}</strong></td>
          <td><strong>100%</strong></td>
          <td>-</td>
          <td>-</td>
          <td>-</td>
          <td><strong>${formatNumber(totalArea, 1)} m²</strong></td>
        </tr>
      </tbody>
    </table>
    <table>
      <thead>
        <tr>
          <th>Unit</th>
          <th>Type</th>
          <th>Area</th>
          <th>Rooms</th>
          <th>Score</th>
          <th>External Id</th>
        </tr>
      </thead>
      <tbody>
        ${units.map((unit) => `
          <tr>
            <td>${escapeHtml(unit.id)}</td>
            <td>${escapeHtml(displayUnitType(unit.type))}</td>
            <td>${formatNumber(unit.area, 1)} m²</td>
            <td>${unit.rooms ? unit.rooms.length : 0}</td>
            <td>${formatNumber(unit.score, 3)}</td>
            <td>${escapeHtml(compactExternalId(unit.externalId || ""))}</td>
          </tr>
        `).join("")}
      </tbody>
    </table>
  `;
}

function renderRoomReviewSchedule(output) {
  const variant = selectedVariant(output);
  const rooms = variant && Array.isArray(variant.rooms)
    ? variant.rooms.slice().sort(sortRoomsForReview)
    : [];
  els.roomScheduleCountLabel.textContent = String(rooms.length);
  if (rooms.length === 0) {
    els.roomScheduleList.innerHTML = `<div class="empty-list">Generate a layout to review rooms, areas, daylight, and unit assignment.</div>`;
    return;
  }

  const totalArea = rooms.reduce((sum, room) => sum + (Number(room.area) || 0), 0);
  const roomsByUnit = groupRoomsByUnit(rooms);
  const units = variant && Array.isArray(variant.units)
    ? variant.units.slice().sort((a, b) => String(a.id || "").localeCompare(String(b.id || "")))
    : [];
  const renderedUnitIds = new Set();
  const unitGroups = units.map((unit) => {
    renderedUnitIds.add(String(unit.id || ""));
    return unitRoomScheduleGroup(unit, roomsByUnit.get(String(unit.id || "")) || [], output);
  });
  const orphanRooms = rooms.filter((room) => !renderedUnitIds.has(String(room.unitId || "")));
  els.roomScheduleList.innerHTML = `
    ${unitGroups.join("")}
    ${orphanRooms.length ? `
      <div class="room-schedule-group">
        <div class="room-schedule-unit orphan">
          <span>
            <strong>Unassigned rooms</strong>
            <small>${orphanRooms.length} rooms outside a unit group</small>
          </span>
        </div>
        ${orphanRooms.map((room) => roomScheduleRow(room, output)).join("")}
      </div>
    ` : ""}
    <div class="room-schedule-total">
      <span>Total rooms</span>
      <strong>${rooms.length} / ${formatNumber(totalArea, 1)} m²</strong>
    </div>
  `;
}

function groupRoomsByUnit(rooms) {
  const groups = new Map();
  (rooms || []).forEach((room) => {
    const unitId = String(room.unitId || "");
    if (!groups.has(unitId)) {
      groups.set(unitId, []);
    }
    groups.get(unitId).push(room);
  });
  return groups;
}

function unitRoomScheduleGroup(unit, rooms, output) {
  const active = isSelection("unit", unit.id);
  const noteCount = diagnosticsForElement(output, [unit.id, unit.externalId]).length;
  const roomArea = (rooms || []).reduce((sum, room) => sum + (Number(room.area) || 0), 0);
  return `
    <div class="room-schedule-group">
      <button class="room-schedule-unit${active ? " active" : ""}" type="button"
          data-schedule-unit-id="${escapeHtml(unit.id)}"
          data-schedule-unit-name="${escapeHtml(displayUnitType(unit.type))}"
          aria-pressed="${active ? "true" : "false"}">
        <span>
          <strong>${escapeHtml(displayUnitType(unit.type))}</strong>
          <small>${escapeHtml(unit.id)} · ${rooms.length} rooms · ${formatNumber(roomArea || unit.area || 0, 1)} m²</small>
        </span>
        ${noteCount ? `<em>${noteCount} note${noteCount === 1 ? "" : "s"}</em>` : `<em>${formatNumber(unit.facadeLength || 0, 1)} m facade</em>`}
      </button>
      ${(rooms || []).map((room) => roomScheduleRow(room, output)).join("")}
    </div>
  `;
}

function roomScheduleRow(room, output) {
  const active = isSelection("room", room.id);
  const roomName = displayRoomType(room);
  const dimensions = room.dimensions
    ? `${formatNumber(room.dimensions.width, 1)} × ${formatNumber(room.dimensions.depth, 1)}`
    : roomDimensionText(room, boundsOfPoints(room.polygon ? room.polygon.points : []));
  const notes = diagnosticsForElement(output, [room.id, room.unitId, room.externalId]);
  return `
    <button class="room-schedule-row${active ? " active" : ""}${notes.length ? " has-notes" : ""}" type="button"
        data-schedule-room-id="${escapeHtml(room.id)}"
        data-schedule-room-name="${escapeHtml(roomName)}"
        aria-pressed="${active ? "true" : "false"}">
      <span>
        <strong>${escapeHtml(roomName)}</strong>
        <small>${escapeHtml(dimensions || "-")} · ${room.daylight ? "Daylight" : "Interior"}</small>
      </span>
      <em>${notes.length ? `${notes.length} note${notes.length === 1 ? "" : "s"}` : `${formatNumber(room.area || 0, 1)} m²`}</em>
    </button>
  `;
}

function sortRoomsForReview(a, b) {
  return String(a.unitId || "").localeCompare(String(b.unitId || ""))
    || displayRoomType(a).localeCompare(displayRoomType(b))
    || String(a.id || "").localeCompare(String(b.id || ""));
}

function renderValidation(output) {
  const variant = selectedVariant(output);
  const checks = variant && variant.validation && Array.isArray(variant.validation.checks)
    ? variant.validation.checks
    : [];
  const notPassed = checks.filter((check) => !check.passed);
  const errors = notPassed.filter((check) => String(check.severity || "").toLowerCase() === "error");
  const warnings = notPassed.filter((check) => String(check.severity || "").toLowerCase() !== "error");
  els.checkCountLabel.textContent = checks.length === 0
    ? "0"
    : notPassed.length === 0
      ? `${checks.length} passed`
      : errors.length > 0
        ? `${errors.length} failed`
        : `${warnings.length} warnings`;
  if (checks.length === 0) {
    els.validationList.innerHTML = `<div class="empty-list">No checks to show yet. Use Check for validation only, or Generate for ranked variants.</div>`;
    return;
  }

  const passed = checks.length - notPassed.length;
  const validationMessage = errors.length
    ? "Resolve blocking checks before export."
    : warnings.length
      ? "Warnings are review items; the generated plan is still inspectable."
      : "Validation is clear for the selected variant.";
  const summary = `
    <div class="check-item ${errors.length ? "failed" : warnings.length ? "warning" : "passed"}">
      <span>${errors.length ? "Fail" : warnings.length ? "Warn" : "Pass"}</span>
      <strong>${passed}/${checks.length} checks passed</strong>
      <em>${validationMessage}</em>
    </div>
  `;

  if (notPassed.length === 0) {
    els.validationList.innerHTML = `${summary}<div class="empty-list good">${checks.length} validation checks passed</div>`;
    return;
  }

  els.validationList.innerHTML = summary + notPassed.slice(0, 12).map((check) => {
    const severity = String(check.severity || "warning").toLowerCase();
    const label = severity === "error" ? "Fail" : "Warn";
    return `
    <div class="check-item ${severity === "error" ? "failed" : "warning"}">
      <span>${label}</span>
      <strong>${escapeHtml(humanizeCode(check.name))}</strong>
      ${check.reason ? `<em>${escapeHtml(friendlyCheckReason(check))}</em>` : ""}
    </div>
  `;
  }).join("") + (notPassed.length > 12 ? `<div class="empty-list">${notPassed.length - 12} more checks in Output JSON</div>` : "");
}

function renderExportSummary(output) {
  if (!output) {
    const cli = buildCliCommand();
    const apiText = buildApiCopyText();
    els.exportSummary.innerHTML = `
      <div>
        <span>CLI Access</span>
        <strong>${escapeHtml(firstLine(cli))}</strong>
        <button type="button" data-export-action="copy-cli">Copy CLI</button>
      </div>
      <div>
        <span>API Endpoint</span>
        <strong>${escapeHtml(apiText)}</strong>
        <button type="button" data-export-action="copy-api">Copy API</button>
      </div>
      <div><span>Raw JSON</span><strong>Hidden until Output JSON is opened</strong></div>
    `;
    return;
  }

  const variant = selectedVariant(output);
  const hypergraph = variant && variant.topology ? variant.topology.hypergraph : null;
  const hypergraphSummary = summarizeHypergraph(hypergraph);
  const hypergraphValidation = hypergraphValidationSummary(variant);
  const validation = summarizeValidation(variant);
  const schema = output.metadata ? output.metadata.schemaVersion : "-";
  const layers = output.metadata && output.metadata.layers ? Object.keys(output.metadata.layers).length : 0;
  const rhinoSummary = variant
    ? `${countOf(variant.units)} units, ${countOf(variant.rooms)} rooms, ${countOf(variant.walls)} walls`
    : "Generate a variant first";

  els.exportSummary.innerHTML = `
    <div>
      <span>Export Ready</span>
      <strong>${escapeHtml(variant ? `${variant.variantId} · ${validation.label}` : "Generate a variant first")}</strong>
    </div>
    <div>
      <span>Rhino / Grasshopper</span>
      <strong>${escapeHtml(rhinoSummary)}</strong>
      <button type="button" data-export-action="copy-rhino">Copy Rhino</button>
    </div>
    <div>
      <span>IFC / BIM Ready</span>
      <strong>${escapeHtml(variant ? `${countExternalIds(variant)} stable external ids` : "No element ids yet")}</strong>
      <button type="button" data-export-action="copy-ifc">Copy IFC</button>
    </div>
    <div>
      <span>SVG / Report</span>
      <strong>${els.planSvg.childElementCount ? "Preview SVG can be saved" : "No preview to save"}</strong>
      <button type="button" data-export-action="save-svg">Save SVG</button>
    </div>
    <div>
      <span>Engine JSON</span>
      <strong>${escapeHtml(hypergraph ? "Machine-readable result available" : "Validation-only output")}</strong>
      <button type="button" data-export-action="copy-json">Copy JSON</button>
    </div>
    <details class="advanced-details export-technical">
      <summary>Technical contract</summary>
      <div>
        <span>Schema and Layers</span>
        <strong>${escapeHtml(schema)}${layers ? ` · ${layers} layer keys` : ""}</strong>
      </div>
      <div>
        <span>Hypergraph Contract</span>
        <strong>${escapeHtml(hypergraphValidation.label)}</strong>
        <div class="variant-meta">${escapeHtml(hypergraphValidation.detail)}</div>
      </div>
      <div>
        <span>Hypergraph Summary</span>
        <strong>${escapeHtml(hypergraphSummary.headline)}</strong>
        <div class="variant-meta">${escapeHtml(hypergraphSummary.detail)}</div>
        <button type="button" data-export-action="copy-hypergraph">Copy Hypergraph</button>
      </div>
      <div>
        <span>Hypergraph Preview</span>
        <strong>${escapeHtml(hypergraphSummary.preview)}</strong>
        <div class="variant-meta">${escapeHtml(hypergraphSummary.matrices)}</div>
        <button type="button" data-export-action="download-hypergraph">Download Hypergraph</button>
      </div>
    </details>
  `;
}

function renderHypergraphPreview(output) {
  if (!els.hypergraphPreview) {
    return;
  }

  const variant = selectedVariant(output);
  const hypergraph = variant && variant.topology ? variant.topology.hypergraph : null;
  if (!hypergraph) {
    els.hypergraphPreview.innerHTML = `
      <div class="empty-list">Generate a variant to inspect the DataNode tree, hyperedges, and incidence matrix.</div>
    `;
    return;
  }

  const summary = summarizeHypergraph(hypergraph);
  const validation = hypergraphValidationSummary(variant);
  const treeRows = dataNodeRows(hypergraph.root, 0, [], 24);
  const totalTreeRows = countDataNodes(hypergraph.root);
  const edgeKinds = Object.entries(countBy(hypergraph.hyperedges || [], (edge) => edge.kind || "edge"))
    .sort((a, b) => b[1] - a[1])
    .slice(0, 7);

  els.hypergraphPreview.innerHTML = `
    <div class="hypergraph-card hypergraph-summary-card">
      <span>Portable contract</span>
      <strong>${escapeHtml(summary.headline)}</strong>
      <div class="contract-status ${validation.label.includes("failed") ? "warning" : "good"}">
        <b>${escapeHtml(validation.label)}</b>
        <em>${escapeHtml(validation.detail)}</em>
      </div>
      <div class="hypergraph-contract-grid">
        ${hypergraphContractRows(hypergraph).map(([label, value]) => `
          <div>
            <b>${escapeHtml(label)}</b>
            <em>${escapeHtml(value)}</em>
          </div>
        `).join("")}
      </div>
    </div>
    <div class="hypergraph-card hypergraph-tree-card">
      <span>DataNode tree</span>
      <strong>${escapeHtml(summary.preview)}</strong>
      <div class="matrix-caption">Showing ${treeRows.length} of ${totalTreeRows} DataNode rows. Full tree is in Output JSON.</div>
      <ul class="node-tree">
        ${treeRows.map((row) => `
          <li class="${depthClass(row.depth)}">
            <b>${escapeHtml(row.name)}</b>
            <em>${row.final ? "final" : "branch"}${row.area ? ` · ${formatNumber(row.area, 1)} m²` : ""}</em>
          </li>
        `).join("")}
      </ul>
    </div>
    <div class="hypergraph-card hypergraph-diagram-card">
      <span>Node-edge diagram</span>
      <strong>${escapeHtml(summary.detail)}</strong>
      <div class="matrix-caption">Sampled diagram: first 10 related nodes and 7 priority hyperedges.</div>
      ${hypergraphDiagramSvg(hypergraph)}
      <div class="edge-cloud">
        ${edgeKinds.map(([kind, count]) => `<i>${escapeHtml(humanizeCode(kind))}<b>${count}</b></i>`).join("")}
      </div>
    </div>
    <div class="hypergraph-card hypergraph-matrix-card">
      <span>Incidence matrix</span>
      <strong>${escapeHtml(summary.matrices)}</strong>
      ${incidencePreview(hypergraph)}
    </div>
  `;
}

function depthClass(depth) {
  return `node-depth-${clamp(Math.round(Number(depth) || 0), 0, 12)}`;
}

function handleExportAction(event) {
  const button = event.target.closest("[data-export-action]");
  if (!button) {
    return;
  }

  if (button.disabled) {
    return;
  }

  const action = button.getAttribute("data-export-action");
  const output = state.response ? state.response.output : null;
  const rawOutputActions = new Set(["download-json", "copy-json", "save-svg"]);
  const variantRequiredActions = new Set(["copy-rhino", "copy-ifc", "copy-hypergraph", "download-hypergraph"]);
  const generatedAction = rawOutputActions.has(action) || variantRequiredActions.has(action);
  if (generatedAction && (!state.response || state.inputDirty || (action === "save-svg" && state.previewStale))) {
    setStatus("Regenerate before exporting generated output");
    return;
  }
  if (variantRequiredActions.has(action) && !selectedVariant(output)) {
    setStatus("Generate a variant before exporting adapter payloads");
    return;
  }

  if (action === "copy-cli") {
    copyText(buildCliCommand(), "CLI command copied");
  } else if (action === "copy-api") {
    copyText(buildApiCopyText(), "API request text copied");
  } else if (action === "copy-rhino") {
    copyText(buildRhinoHandoffText(), "Rhino handoff payload copied");
  } else if (action === "copy-ifc") {
    copyText(buildBimHandoffText(), "BIM handoff payload copied");
  } else if (action === "download-json") {
    downloadText("floor-plan-output.json", els.outputJson.textContent || "{}");
    setStatus("Output JSON downloaded");
  } else if (action === "copy-json") {
    copyOutput();
  } else if (action === "copy-hypergraph") {
    copyText(buildHypergraphText(), "Hypergraph JSON copied");
  } else if (action === "download-hypergraph") {
    downloadText("floor-plan-hypergraph.json", buildHypergraphText());
    setStatus("Hypergraph JSON downloaded");
  } else if (action === "save-svg") {
    saveSvg();
  }
}

function summarizeValidation(variant) {
  if (!variant || !variant.validation || !Array.isArray(variant.validation.checks)) {
    return { label: "No checks", passed: 0, failed: 0, warnings: 0, total: 0 };
  }

  const checks = variant.validation.checks;
  const failed = checks.filter((check) => !check.passed);
  const warnings = failed.filter((check) => String(check.severity || "").toLowerCase() !== "error");
  const errors = failed.length - warnings.length;
  const label = failed.length === 0
    ? `${checks.length} checks passed`
    : errors
      ? `${errors} blocking issue${errors === 1 ? "" : "s"}`
      : `${warnings.length} warning${warnings.length === 1 ? "" : "s"}`;
  return {
    label,
    passed: checks.length - failed.length,
    failed: errors,
    warnings: warnings.length,
    total: checks.length
  };
}

function hypergraphValidationSummary(variant) {
  const checks = variant && variant.validation && Array.isArray(variant.validation.checks)
    ? variant.validation.checks
    : [];
  const check = checks.find((item) => item.name === "hypergraph_contract");
  if (!check) {
    return {
      label: "Not checked",
      detail: "Generate a full variant to run the backend hypergraph contract validation."
    };
  }

  return {
    label: check.passed ? "Backend validation passed" : "Backend validation failed",
    detail: check.passed
      ? "The selected variant passed the hypergraph_contract validation check."
      : (check.reason || "The selected variant did not pass hypergraph_contract validation.")
  };
}

function summarizeHypergraph(hypergraph) {
  if (!hypergraph) {
    return {
      headline: "No hypergraph for validation-only output",
      detail: "Generate variants to inspect DataNode, hyperedges, incidence, and matrices.",
      preview: "DataNode preview unavailable",
      matrices: "Matrix dimensions unavailable"
    };
  }

  const nodes = Array.isArray(hypergraph.nodes) ? hypergraph.nodes : [];
  const hyperedges = Array.isArray(hypergraph.hyperedges) ? hypergraph.hyperedges : [];
  const incidence = Array.isArray(hypergraph.incidence) ? hypergraph.incidence : [];
  const kinds = countBy(hyperedges, (edge) => edge.kind || "edge");
  const kindText = Object.entries(kinds)
    .sort((a, b) => b[1] - a[1])
    .slice(0, 4)
    .map(([kind, count]) => `${humanizeCode(kind)} ${count}`)
    .join(", ");
  const preview = previewDataNode(hypergraph.root);
  const matrices = hypergraph.matrices || {};
  const nodeOrder = countOf(matrices.nodeOrder);
  const edgeOrder = countOf(matrices.hyperedgeOrder);

  return {
    headline: `${nodes.length} nodes, ${hyperedges.length} hyperedges, ${incidence.length} incidence records`,
    detail: kindText || "No hyperedge kind breakdown available",
    preview,
    matrices: `${nodeOrder} node order x ${edgeOrder} hyperedge order`
  };
}

function previewDataNode(node) {
  if (!node) {
    return "DataNode tree unavailable";
  }

  const children = Array.isArray(node.children) ? node.children : [];
  const childNames = children.slice(0, 4).map((child) => child.name || child.id || "node").join(", ");
  const more = children.length > 4 ? `, +${children.length - 4} more` : "";
  return `${node.name || "root"} -> ${childNames || "no children"}${more}`;
}

function dataNodeRows(node, depth = 0, rows = [], maxRows = 12) {
  if (!node || rows.length >= maxRows) {
    return rows;
  }

  rows.push({
    depth,
    name: node.name || "node",
    final: Boolean(node.final),
    area: node.area || (node.treeNodeMesh && node.treeNodeMesh.Area) || 0
  });
  (node.children || []).forEach((child) => dataNodeRows(child, depth + 1, rows, maxRows));
  return rows;
}

function countDataNodes(node) {
  if (!node) {
    return 0;
  }

  return 1 + (node.children || []).reduce((total, child) => total + countDataNodes(child), 0);
}

function hypergraphContractRows(hypergraph) {
  const matrices = hypergraph.matrices || {};
  const root = hypergraph.root || {};
  return [
    ["Schema", hypergraph.schemaVersion || "hypergraph-floorplan-1.0"],
    ["Source", hypergraph.source || "BhaveshY/hypergraph portable DataNode contract"],
    ["Root", root.name || "root"],
    ["DataNode keys", "name, area, angle, mergeid, final, children, connected, treeNodeMesh"],
    ["Node order", `${countOf(matrices.nodeOrder)} ids`],
    ["Hyperedge order", `${countOf(matrices.hyperedgeOrder)} ids`]
  ];
}

function hypergraphDiagramSvg(hypergraph) {
  const nodes = Array.isArray(hypergraph.nodes) ? hypergraph.nodes : [];
  const edges = Array.isArray(hypergraph.hyperedges) ? hypergraph.hyperedges : [];
  if (nodes.length === 0 || edges.length === 0) {
    return `<div class="empty-list">No node-edge diagram available</div>`;
  }

  const nodeById = new Map(nodes.map((node) => [node.id, node]));
  const priority = { subdivision: 1, adjacency: 2, containment: 3, circulation_access: 4, door: 5, facade: 6, constraint: 7 };
  const visibleEdges = edges
    .filter((edge) => Array.isArray(edge.members) && edge.members.length > 0)
    .sort((a, b) => (priority[a.kind] || 99) - (priority[b.kind] || 99) || b.members.length - a.members.length)
    .slice(0, 7);
  const visibleNodeIds = [];
  visibleEdges.forEach((edge) => {
    edge.members.forEach((member) => {
      if (nodeById.has(member.nodeId) && !visibleNodeIds.includes(member.nodeId) && visibleNodeIds.length < 10) {
        visibleNodeIds.push(member.nodeId);
      }
    });
  });

  const nodeY = (index, count) => 32 + index * (236 / Math.max(1, count - 1));
  const edgeY = (index, count) => 32 + index * (236 / Math.max(1, count - 1));
  const nodePositions = new Map(visibleNodeIds.map((id, index) => [id, { x: 120, y: nodeY(index, visibleNodeIds.length) }]));
  const edgePositions = new Map(visibleEdges.map((edge, index) => [edge.id, { x: 500, y: edgeY(index, visibleEdges.length) }]));
  const lines = [];
  visibleEdges.forEach((edge) => {
    const edgePosition = edgePositions.get(edge.id);
    edge.members.forEach((member) => {
      const nodePosition = nodePositions.get(member.nodeId);
      if (nodePosition && edgePosition) {
        lines.push(
          `<line class="hypergraph-link" x1="${nodePosition.x + 86}" y1="${nodePosition.y}" ` +
          `x2="${edgePosition.x - 86}" y2="${edgePosition.y}"></line>`);
      }
    });
  });

  return `
    <svg class="hypergraph-diagram" viewBox="0 0 620 300" role="img" aria-label="Hypergraph node to hyperedge incidence diagram">
      <g class="hypergraph-links">${lines.join("")}</g>
      <g class="hypergraph-nodes">
        ${visibleNodeIds.map((id) => {
          const position = nodePositions.get(id);
          const node = nodeById.get(id);
          return `
            <g class="hypergraph-node" transform="translate(${position.x},${position.y})">
              <rect x="-86" y="-13" width="172" height="26" rx="8"></rect>
              <text text-anchor="middle" y="4">${escapeSvgText(shortHypergraphLabel(node ? node.label || node.name || node.id : id))}</text>
            </g>
          `;
        }).join("")}
      </g>
      <g class="hypergraph-edges">
        ${visibleEdges.map((edge) => {
          const position = edgePositions.get(edge.id);
          return `
            <g class="hypergraph-edge" transform="translate(${position.x},${position.y})">
              <rect x="-86" y="-14" width="172" height="28" rx="8"></rect>
              <text text-anchor="middle" y="-1">${escapeSvgText(shortHypergraphLabel(edge.kind || "edge"))}</text>
              <text class="hypergraph-edge-id" text-anchor="middle" y="10">${escapeSvgText(shortHypergraphLabel(edge.id))}</text>
            </g>
          `;
        }).join("")}
      </g>
    </svg>
  `;
}

function incidencePreview(hypergraph) {
  const matrices = hypergraph && hypergraph.matrices ? hypergraph.matrices : {};
  const rows = Array.isArray(matrices.incidence) ? matrices.incidence.slice(0, 8) : [];
  const nodeOrder = Array.isArray(matrices.nodeOrder) ? matrices.nodeOrder.slice(0, rows.length) : [];
  const edgeOrder = Array.isArray(matrices.hyperedgeOrder) ? matrices.hyperedgeOrder.slice(0, 7) : [];
  if (rows.length === 0 || edgeOrder.length === 0) {
    return `<div class="empty-list">Matrix preview unavailable</div>`;
  }

  const caption =
    `Showing ${rows.length} of ${countOf(matrices.nodeOrder)} nodes and ` +
    `${edgeOrder.length} of ${countOf(matrices.hyperedgeOrder)} hyperedges. ` +
    "Nonzero cells show weighted incidence values.";

  return `
    <table class="matrix-preview">
      <thead>
        <tr>
          <th>Node</th>
          ${edgeOrder.map((edgeId, index) => `<th title="${escapeHtml(edgeId)}">E${index + 1}</th>`).join("")}
        </tr>
      </thead>
      <tbody>
        ${rows.map((row, rowIndex) => `
          <tr>
            <td title="${escapeHtml(nodeOrder[rowIndex] || "")}">V${rowIndex + 1}</td>
            ${row.slice(0, edgeOrder.length).map((value) => {
              const numeric = Number(value) || 0;
              const label = numeric ? formatMatrixValue(numeric) : "-";
              return `<td><span class="matrix-dot ${numeric ? "is-connected" : "is-empty"}" title="${escapeHtml(label)}">${escapeHtml(label)}</span></td>`;
            }).join("")}
          </tr>
        `).join("")}
      </tbody>
    </table>
    <div class="matrix-caption">${caption}</div>
  `;
}

function formatMatrixValue(value) {
  if (!Number.isFinite(Number(value))) {
    return "-";
  }
  return Number(value).toFixed(2).replace(/\.?0+$/, "");
}

function shortHypergraphLabel(value) {
  const text = String(value || "node").replace(/^node-/, "").replace(/^edge-/, "").replaceAll("_", " ");
  return text.length > 24 ? `${text.slice(0, 21)}...` : text;
}

function escapeSvgText(value) {
  return escapeHtml(value).replaceAll("'", "&apos;");
}

function collectDiagnostics(output) {
  if (!output) {
    return [];
  }

  const top = Array.isArray(output.diagnostics) ? output.diagnostics : [];
  const variant = selectedVariant(output);
  const variantDiagnostics = variant && Array.isArray(variant.diagnostics) ? variant.diagnostics : [];
  const failedChecks = variant && variant.validation && Array.isArray(variant.validation.checks)
    ? variant.validation.checks
        .filter((check) => !check.passed)
        .map((check) => ({
          severity: check.severity || "warning",
          code: check.name,
          message: check.reason || "Validation check did not pass.",
          sourceId: check.sourceId
        }))
    : [];

  return [...top, ...variantDiagnostics, ...failedChecks]
    .filter((diagnostic) => diagnostic && diagnostic.severity !== "info");
}

function diagnosticSummary(output) {
  const diagnostics = collectDiagnostics(output);
  const errorCount = diagnostics
    .filter((item) => String(item.severity || "").toLowerCase() === "error")
    .length;

  return {
    diagnostics,
    errorCount,
    reviewNoteCount: diagnostics.length - errorCount
  };
}

function diagnosticSubtitleText(summary) {
  if (!summary || summary.diagnostics.length === 0) {
    return "";
  }

  if (summary.errorCount) {
    return `${summary.errorCount} blocking issue${summary.errorCount === 1 ? "" : "s"}`;
  }

  return `${summary.reviewNoteCount} review note${summary.reviewNoteCount === 1 ? "" : "s"}`;
}

function renderError(code, message) {
  const output = {
    status: "failed",
    variants: [],
    diagnostics: [{ severity: "error", code, message, sourceId: "web" }],
    metadata: {
      schemaVersion: "1.2",
      floorplate: null
    }
  };
  state.response = { output, status: "failed", variantCount: 0, validVariantCount: 0 };
  if (!state.lastPreviewResponse) {
    state.selectedVariantId = "";
  }
  state.previewStale = Boolean(state.lastPreviewResponse);
  renderAll();
}

function selectedVariant(output) {
  return applyGeometryOverrides(selectedVariantRaw(output));
}

function selectedVariantRaw(output) {
  const variants = output && Array.isArray(output.variants) ? output.variants : [];
  return variants.find((variant) => variant.variantId === state.selectedVariantId) || variants[0] || null;
}

// Overlay the user's manual room/unit resizes onto the engine output so every
// downstream renderer (canvas, inspector, schedule, halo, exports) reads the
// edited geometry from one place. Returns the variant untouched when there are
// no edits, so the common path allocates nothing.
function applyGeometryOverrides(variant) {
  if (!variant || !variant.variantId) {
    return variant;
  }
  const overrides = state.geometryEdits[variant.variantId];
  if (!overrides
    || (!overrides.room && !overrides.unit && !overrides.corridor && !overrides.wall && !overrides.door)) {
    return variant;
  }

  const overrideItems = (items, kind) => {
    const byId = overrides[kind];
    if (!byId || !Array.isArray(items)) {
      return items;
    }
    return items.map((item) => {
      const points = byId[String(item.id || "")];
      if (!points) {
        return item;
      }
      const bounds = boundsOfPoints(points);
      return {
        ...item,
        polygon: { ...(item.polygon || {}), points },
        area: round(polygonArea(points)),
        bounds: bounds
          ? { minX: bounds.minX, minY: bounds.minY, width: bounds.width, height: bounds.height }
          : item.bounds,
        edited: true
      };
    });
  };

  const result = {
    ...variant,
    units: overrideItems(variant.units, "unit"),
    rooms: overrideItems(variant.rooms, "room"),
    corridors: (overrideItems(variant.corridors, "corridor") || []).map((corridor) => {
      // An edited corridor's centerline is re-derived from its new outline so
      // the dashed circulation line keeps tracking the polygon.
      if (!corridor || !corridor.edited || !corridor.centerline) {
        return corridor;
      }
      const bounds = boundsOfPoints(corridor.polygon ? corridor.polygon.points : null);
      if (!bounds) {
        return corridor;
      }
      const horizontal = bounds.width >= bounds.height;
      const midY = round(bounds.minY + bounds.height / 2);
      const midX = round(bounds.minX + bounds.width / 2);
      return {
        ...corridor,
        width: round(horizontal ? bounds.height : bounds.width),
        centerline: horizontal
          ? { start: { x: round(bounds.minX), y: midY }, end: { x: round(bounds.maxX), y: midY } }
          : { start: { x: midX, y: round(bounds.minY) }, end: { x: midX, y: round(bounds.maxY) } }
      };
    })
  };

  // Walls and doors move ONLY by explicit overrides written at commit time —
  // exactly the geometry the live drag drew. A wall the edit didn't touch
  // stays verbatim; guessing at untouched fabric through bounding-box warps
  // is what used to shear walls into diagonals. Labels are render hints, not
  // construction geometry, so they still follow the soft warp.
  const warps = collectGeometryWarps(variant, overrides);
  const wallOverrides = overrides.wall || {};
  const doorOverrides = overrides.door || {};
  if (warps.length || Object.keys(wallOverrides).length || Object.keys(doorOverrides).length) {
    result.walls = (variant.walls || []).map((wall) => {
      const centerline = wallOverrides[String(wall.id || "")];
      if (centerline) {
        return { ...wall, centerline: { start: { ...centerline.start }, end: { ...centerline.end } } };
      }
      return wall;
    });
    result.doorsOpenings = (variant.doorsOpenings || []).map((door) => {
      const location = doorOverrides[String(door.id || "")];
      if (location) {
        return { ...door, location: { ...location } };
      }
      return door;
    });
    result.labels = (variant.labels || []).map((label) => (
      label && label.location ? { ...label, location: warpPoint(label.location, warps) } : label
    ));
    result.metrics = recomputeEditedMetrics(variant, result);
  }
  return result;
}

function collectGeometryWarps(variant, overrides) {
  const warps = [];
  const push = (items, kind) => {
    const byId = overrides[kind];
    if (!byId) {
      return;
    }
    (items || []).forEach((item) => {
      const points = byId[String(item.id || "")];
      if (!points || !item.polygon) {
        return;
      }
      const from = boundsOfPoints(item.polygon.points);
      const to = boundsOfPoints(points);
      if (from && to && from.width > 1e-6 && from.height > 1e-6) {
        warps.push({ from, to, area: from.width * from.height });
      }
    });
  };
  push(variant.rooms, "room");
  push(variant.units, "unit");
  push(variant.corridors, "corridor");
  // Most specific (smallest) region wins, so a wall inside an edited room moves
  // with the room even when its parent unit was edited too.
  warps.sort((a, b) => a.area - b.area);
  return warps;
}

const warpEpsilon = 0.02;

function warpPoint(point, warps) {
  const x = Number(point.x);
  const y = Number(point.y);
  for (let i = 0; i < warps.length; i += 1) {
    const w = warps[i];
    if (x >= w.from.minX - warpEpsilon && x <= w.from.maxX + warpEpsilon
      && y >= w.from.minY - warpEpsilon && y <= w.from.maxY + warpEpsilon) {
      return {
        x: round(w.to.minX + ((x - w.from.minX) / w.from.width) * w.to.width),
        y: round(w.to.minY + ((y - w.from.minY) / w.from.height) * w.to.height)
      };
    }
  }
  return point;
}

// Keep the headline metrics honest after manual edits: sellable area is the sum
// of the (possibly resized) units, and net/gross scales with it.
function recomputeEditedMetrics(variant, edited) {
  const metrics = variant.metrics || {};
  const oldSellable = Number(metrics.sellableArea);
  const newSellable = (edited.units || []).reduce((sum, unit) => sum + (Number(unit.area) || 0), 0);
  if (!Number.isFinite(oldSellable) || oldSellable <= 0 || newSellable <= 0) {
    return metrics;
  }
  const factor = newSellable / oldSellable;
  const next = { ...metrics, sellableArea: round(newSellable) };
  if (Number.isFinite(Number(metrics.netGrossRatio))) {
    next.netGrossRatio = round(Number(metrics.netGrossRatio) * factor);
  }
  return next;
}

function geometryOverrideFor(variantId, kind, id) {
  const variantEdits = state.geometryEdits[variantId];
  const byKind = variantEdits ? variantEdits[kind] : null;
  return byKind ? byKind[String(id)] || null : null;
}

function setGeometryOverride(variantId, kind, id, points) {
  if (!variantId) {
    return;
  }
  if (!state.geometryEdits[variantId]) {
    state.geometryEdits[variantId] = {};
  }
  if (!state.geometryEdits[variantId][kind]) {
    state.geometryEdits[variantId][kind] = {};
  }
  state.geometryEdits[variantId][kind][String(id)] = points;
}

function hasGeometryOverrides() {
  return Object.keys(state.geometryEdits || {}).length > 0;
}

function firstVariantId(output) {
  return output && output.variants && output.variants[0] ? output.variants[0].variantId : "";
}

async function fetchJson(url, options) {
  const response = await fetch(url, options);
  const text = await response.text();
  if (!response.ok) {
    throw new Error(httpErrorMessage(response, text));
  }
  return text ? JSON.parse(text) : null;
}

function httpErrorMessage(response, text) {
  try {
    const parsed = JSON.parse(text);
    if (parsed && typeof parsed.message === "string" && parsed.message.trim()) {
      return parsed.message.trim();
    }
  } catch (_) {
    // Non-JSON responses can include framework HTML or server internals.
  }

  return `Request failed (${response.status || "network"}). Try again or check the app logs.`;
}

function clearSvg() {
  while (els.planSvg.firstChild) {
    els.planSvg.removeChild(els.planSvg.firstChild);
  }
}

function polygonEl(points, className, attributes = {}) {
  return svgEl("polygon", {
    class: className,
    points: (points || []).map((p) => `${p.x},${p.y}`).join(" "),
    ...attributes
  });
}

function rectEl(x, y, width, height, className, attributes = {}) {
  return svgEl("rect", {
    class: className,
    x: round(x),
    y: round(y),
    width: round(width),
    height: round(height),
    ...attributes
  });
}

function lineEl(start, end, className, attributes = {}) {
  return svgEl("line", {
    class: className,
    x1: round(start.x),
    y1: round(start.y),
    x2: round(end.x),
    y2: round(end.y),
    ...attributes
  });
}

function svgEl(name, attributes) {
  const element = document.createElementNS("http://www.w3.org/2000/svg", name);
  Object.entries(attributes || {}).forEach(([key, value]) => element.setAttribute(key, value));
  return element;
}

function collectBounds(output, variant, input) {
  const points = [];
  if (input && input.floorplate && input.floorplate.outer && Array.isArray(input.floorplate.outer.points)) {
    points.push(...input.floorplate.outer.points);
  }
  if (input && Array.isArray(input.fixedElements)) {
    input.fixedElements.forEach((fixed) => {
      if (fixed.polygon && Array.isArray(fixed.polygon.points)) {
        points.push(...fixed.polygon.points);
      }
    });
  }
  if (variant) {
    ["units", "rooms", "corridors"].forEach((key) => {
      (variant[key] || []).forEach((item) => {
        if (item.polygon && Array.isArray(item.polygon.points)) {
          points.push(...item.polygon.points);
        }
      });
    });
  }
  if (output && output.metadata && output.metadata.floorplate && output.metadata.floorplate.bounds) {
    const b = output.metadata.floorplate.bounds;
    points.push({ x: b.minX, y: b.minY }, { x: b.maxX, y: b.maxY });
  }
  return boundsOfPoints(points);
}

function variantThumbnailSvg(variant, input) {
  const bounds = collectBounds(null, variant, input);
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
    return `<div class="variant-thumb empty">No preview</div>`;
  }

  const pad = Math.max(bounds.width, bounds.height) * 0.05;
  const viewBox = [bounds.minX - pad, -bounds.maxY - pad, bounds.width + pad * 2, bounds.height + pad * 2].join(" ");
  const polygons = [];
  if (input && input.floorplate && input.floorplate.outer) {
    polygons.push(thumbPolygon(input.floorplate.outer.points, "thumb-boundary"));
  }
  (variant.units || []).forEach((unit) => polygons.push(thumbPolygon(unit.polygon.points, `thumb-unit thumb-${unit.type || "unit"}`)));
  (variant.corridors || []).forEach((corridor) => polygons.push(thumbPolygon(corridor.polygon.points, "thumb-corridor")));
  if (input && Array.isArray(input.fixedElements)) {
    input.fixedElements.forEach((fixed) => {
      if (fixed.polygon && fixed.polygon.points) {
        polygons.push(thumbPolygon(fixed.polygon.points, "thumb-core"));
      }
    });
  }

  return `
    <svg class="variant-thumb" viewBox="${escapeHtml(viewBox)}" preserveAspectRatio="xMidYMid meet" aria-hidden="true">
      <g transform="scale(1,-1)">${polygons.join("")}</g>
    </svg>
  `;
}

function thumbPolygon(points, className) {
  return `<polygon class="${escapeHtml(className)}" points="${(points || []).map((point) => `${point.x},${point.y}`).join(" ")}"></polygon>`;
}

function aggregateUnits(units) {
  const buckets = new Map();
  (units || []).forEach((unit) => {
    const type = unit.type || "unit";
    if (!buckets.has(type)) {
      buckets.set(type, { type, count: 0, totalArea: 0, minArea: Number.POSITIVE_INFINITY, maxArea: 0 });
    }
    const row = buckets.get(type);
    const area = Number(unit.area);
    row.count += 1;
    if (Number.isFinite(area)) {
      row.totalArea += area;
      row.minArea = Math.min(row.minArea, area);
      row.maxArea = Math.max(row.maxArea, area);
    }
  });

  return Array.from(buckets.values())
    .map((row) => ({
      ...row,
      minArea: Number.isFinite(row.minArea) ? row.minArea : 0,
      averageArea: row.count ? row.totalArea / row.count : 0
    }))
    .sort((a, b) => unitTypeSort(a.type) - unitTypeSort(b.type) || a.type.localeCompare(b.type));
}

function unitMixSummary(units) {
  const rows = aggregateUnits(units);
  return rows.map((row) => `${row.count} ${shortUnitType(row.type)}`).join(", ");
}

function countBy(items, selector) {
  return (items || []).reduce((result, item) => {
    const key = selector(item);
    result[key] = (result[key] || 0) + 1;
    return result;
  }, {});
}

function countOf(value) {
  return Array.isArray(value) ? value.length : 0;
}

function countExternalIds(variant) {
  if (!variant) {
    return 0;
  }

  return ["units", "rooms", "corridors", "walls", "doorsOpenings", "labels"]
    .reduce((total, key) => total + (Array.isArray(variant[key]) ? variant[key].filter((item) => item.externalId).length : 0), variant.externalId ? 1 : 0);
}

function compactExternalId(value) {
  if (!value) {
    return "-";
  }

  const parts = String(value).split("/");
  return parts.length > 4 ? parts.slice(-3).join("/") : String(value);
}

function unitTypeSort(type) {
  const order = { studio: 1, one_bed: 2, two_bed: 3 };
  return order[String(type || "").toLowerCase()] || 99;
}

function friendlyStatus(value) {
  switch (String(value || "").toLowerCase()) {
    case "succeeded":
      return "Succeeded";
    case "validated":
      return "Validated";
    case "partial":
      return "Needs attention";
    case "failed":
      return "Failed";
    case "ready":
      return "Ready";
    default:
      return titleCase(value || "ready");
  }
}

function friendlySeverity(value) {
  const severity = String(value || "info").toLowerCase();
  if (severity === "error") {
    return "Blocking";
  }
  if (severity === "warning") {
    return "Review";
  }
  return titleCase(severity);
}

function friendlyDiagnosticMessage(diagnostic) {
  const message = diagnostic.message || diagnostic.reason || "Review this item before export.";
  const severity = String(diagnostic.severity || "").toLowerCase();
  if (severity === "error") {
    return `${message} This is blocking export-ready confidence.`;
  }
  if (severity === "warning") {
    return `${message} Review it, but the plan can still be inspected.`;
  }
  return message;
}

function friendlyCheckReason(check) {
  const reason = check.reason || "Validation check did not pass.";
  const severity = String(check.severity || "").toLowerCase();
  return severity === "error"
    ? `${reason} Fix this before relying on the variant.`
    : `${reason} Treat this as a review note.`;
}

function formatInput() {
  try {
    const parsed = JSON.parse(els.inputEditor.value);
    setInput(parsed, { preserveResponse: true });
    saveDraft();
    markInputDirty("Updating plan from JSON", 150);
    renderAll();
  } catch (error) {
    setStatus("Input JSON is invalid");
    renderError("invalid_json", error.message);
  }
}

async function copyOutput() {
  const text = els.outputJson.textContent || "{}";
  await copyText(text, "Output JSON copied");
}

function buildHypergraphText() {
  const output = state.response ? state.response.output : null;
  const variant = selectedVariant(output);
  const hypergraph = variant && variant.topology ? variant.topology.hypergraph : null;
  return JSON.stringify(hypergraph || {}, null, 2);
}

async function copyText(text, successMessage) {
  try {
    await navigator.clipboard.writeText(text);
    setStatus(successMessage || "Copied");
  } catch (_) {
    const scratch = document.createElement("textarea");
    scratch.value = text;
    scratch.setAttribute("readonly", "readonly");
    scratch.className = "clipboard-scratch";
    document.body.appendChild(scratch);
    scratch.select();
    document.execCommand("copy");
    scratch.remove();
    setStatus(successMessage || "Copied");
  }
}

function saveSvg() {
  if (!els.planSvg.childElementCount) {
    setStatus("No SVG to save");
    return;
  }

  const cloneSvg = els.planSvg.cloneNode(true);
  cloneSvg.setAttribute("xmlns", "http://www.w3.org/2000/svg");
  // Editor chrome is for the live canvas only — a saved drawing carries the
  // plan, not the selection halo, grips, hit zones, or drag overlays.
  cloneSvg.querySelectorAll(
    ".edit-handles, .edit-selection-handles, .room-selected-halo-group, " +
    ".geom-drag-overlay, .wall-drag-guide-group, .wall-hit, .core-grab-overlay, .plan-grid")
    .forEach((node) => node.remove());
  cloneSvg.insertBefore(svgStyleElement(), cloneSvg.firstChild);
  const xml = new XMLSerializer().serializeToString(cloneSvg);
  downloadText("floor-plan-preview.svg", xml, "image/svg+xml");
  setStatus("SVG saved");
}

function svgStyleElement() {
  const style = document.createElementNS("http://www.w3.org/2000/svg", "style");
  // Self-contained styles so the exported drawing is byte-for-byte the live
  // ink rendering: one warm-graphite hue at four strengths, solid wall poché,
  // line-work fixtures, white window breaks. Custom properties are expanded
  // to literals because a standalone SVG has no app stylesheet to resolve
  // var() against. Strokes stay in model metres — correct for vector reuse.
  const ink900 = "#21262b";
  const ink700 = "rgba(33,38,43,0.72)";
  const ink500 = "rgba(33,38,43,0.5)";
  const ink300 = "rgba(33,38,43,0.3)";
  const ink150 = "rgba(33,38,43,0.15)";
  style.textContent = `
    text{font-family:ui-sans-serif,system-ui,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif}
    .plan-sheet{fill:#ffffff;stroke:rgba(33,38,43,0.07);stroke-width:0.02}
    .plan-sheet-shadow{fill:rgba(26,34,42,0.035);stroke:none}
    .boundary{fill:#ffffff;stroke:${ink900};stroke-width:0.3;paint-order:stroke fill}
    .fixed{fill:#2c3338;stroke:none}
    .core-label{fill:rgba(255,255,255,0.85);font-weight:750;letter-spacing:0.22em}
    .corridor{fill:#f0f0ed;stroke:none}
    .unit,.unit-one_bed,.unit-two_bed{fill:none;stroke:${ink150};stroke-width:0.05}
    .room{fill:#ffffff;stroke:${ink150};stroke-width:0.04}
    .room-bedroom{fill:#f8f5ef}
    .room-bathroom{fill:#f0f4f5}
    .room-kitchen{fill:#f3f5f0}
    .room-living{fill:#faf6ee}
    .room-balcony{fill:#f3f6f1}
    .room-service,.room-general{fill:#f4f4f2}
    .wall{fill:${ink900};stroke:none}
    .wall-exterior,.wall-unit_boundary,.wall-unit_demising{fill:#1a1f24}
    .wall-backdrop{fill:none;stroke:#ffffff}
    .window-break{fill:#ffffff;stroke:none}
    .window-frame{fill:none;stroke:${ink900};stroke-width:0.04}
    .window-mullion{fill:none;stroke:${ink500};stroke-width:0.025}
    .window-jamb{fill:none;stroke:${ink900};stroke-width:0.055}
    .fixture{fill:#ffffff;stroke:${ink500};stroke-width:0.035;stroke-linecap:round;stroke-linejoin:round}
    .fixture-rug{fill:rgba(33,38,43,0.04);stroke:none}
    .fixture-bed{fill:#ffffff;stroke:${ink700};stroke-width:0.04}
    .fixture-headboard{fill:#f2f1ee;stroke:${ink500}}
    .fixture-pillow{fill:#ffffff;stroke:${ink300};stroke-width:0.028}
    .fixture-sink,.fixture-toilet,.fixture-wc{fill:#ffffff;stroke:${ink700};stroke-width:0.038}
    .fixture-nightstand,.fixture-wardrobe,.fixture-counter{fill:#fafaf8;stroke:${ink500}}
    .fixture-appliance,.fixture-stove,.fixture-fridge{fill:#ffffff;stroke:${ink700};stroke-width:0.038}
    .fixture-table{fill:#ffffff;stroke:${ink500}}
    .fixture-chair,.fixture-sofa{fill:#ffffff;stroke:${ink700};stroke-width:0.04}
    .fixture-tv{fill:#f2f1ee;stroke:${ink500}}
    .fixture-bath,.fixture-shower{fill:#ffffff;stroke:${ink700};stroke-width:0.04}
    .fixture-plant{fill:none;stroke:${ink300};stroke-width:0.03}
    .fixture-burner{fill:none;stroke:${ink500};stroke-width:0.03}
    .fixture-detail{fill:none;stroke:${ink300};stroke-width:0.03;stroke-linecap:round}
    .door-gap{stroke:#ffffff;stroke-linecap:butt}
    .door-leaf{fill:none;stroke:${ink900};stroke-width:0.045;stroke-linecap:square}
    .door-swing{fill:none;stroke:${ink300};stroke-width:0.028}
    .door-marker{fill:${ink500};stroke:#fff;stroke-width:0.02}
    .entry-marker{fill:${ink700};stroke:rgba(255,255,255,0.86);stroke-width:0.02}
    .corridor-centerline{fill:none;stroke:${ink300};stroke-width:0.045;stroke-dasharray:0.42 0.28;stroke-linecap:round}
    .svg-label{fill:${ink900};paint-order:stroke;stroke:rgba(255,255,255,0.85);stroke-width:0.09;stroke-linejoin:round}
    .unit-label{fill:${ink300};stroke:rgba(255,255,255,0.6);stroke-width:0.06;font-weight:800}
    .room-label{fill:${ink900};stroke:rgba(255,255,255,0.92);stroke-width:0.1;font-weight:650;letter-spacing:0.07em}
    .room-label-meta{fill:${ink500};font-weight:560;letter-spacing:0.02em}
    .dimension-line,.dimension-tick{fill:none;stroke:${ink300};stroke-width:0.04}
    .dimension-tick{stroke:${ink500}}
    .dimension-witness{fill:none;stroke:${ink150};stroke-width:0.03}
    .dimension-label{fill:${ink700};paint-order:stroke;stroke:rgba(244,247,248,0.94);stroke-width:0.14}
    .dimension-label{stroke-linejoin:round;font-weight:680;letter-spacing:0.04em}
    .scale-bar-line,.scale-bar-tick{stroke:${ink500};stroke-width:0.05;stroke-linecap:square}
    .scale-bar-label{fill:#26313b;paint-order:stroke;stroke:rgba(248,250,251,0.92);stroke-width:0.14;stroke-linejoin:round;font-weight:800}
    .north-arrow-needle{fill:rgba(27,36,48,0.82);stroke:rgba(255,255,255,0.85);stroke-width:0.02}
    .north-arrow-label{fill:rgba(27,36,48,0.78);paint-order:stroke;stroke:rgba(244,247,248,0.9);stroke-width:0.12;stroke-linejoin:round;font-weight:850}
    .wet-tile-line{fill:none;stroke:${ink150};stroke-width:0.014}
    .floor-tile{fill:url(#wetTile);stroke:none}
    .axon-shadow{fill:rgba(33,38,43,0.10);stroke:none}
    .axon-top.axon-slab{fill:#eef0ee;stroke:${ink900};stroke-width:0.03}
    .axon-side-x.axon-slab,.axon-side-y.axon-slab{fill:#d8dcda;stroke:${ink900};stroke-width:0.025}
    .axon-top.axon-wall{fill:#fdfdfc;stroke:${ink900};stroke-width:0.022}
    .axon-side-x.axon-wall{fill:#e7e9e7;stroke:${ink900};stroke-width:0.018}
    .axon-side-y.axon-wall{fill:#d3d7d5;stroke:${ink900};stroke-width:0.018}
    .axon-top.axon-wall-partition{fill:#fbfbfa;stroke:${ink900};stroke-width:0.016}
    .axon-side-x.axon-wall-partition{fill:#ecedeb;stroke:${ink900};stroke-width:0.014}
    .axon-side-y.axon-wall-partition{fill:#dde0de;stroke:${ink900};stroke-width:0.014}
    .axon-top.axon-core{fill:#5d6a70;stroke:${ink900};stroke-width:0.03}
    .axon-side-x.axon-core{fill:#4a565c;stroke:${ink900};stroke-width:0.025}
    .axon-side-y.axon-core{fill:#3d484d;stroke:${ink900};stroke-width:0.025}
    .axon-top.axon-glass{fill:#cfe2e0;stroke:${ink700};stroke-width:0.016}
    .axon-side-x.axon-glass{fill:rgba(151,196,192,0.62);stroke:${ink700};stroke-width:0.014}
    .axon-side-y.axon-glass{fill:rgba(122,172,168,0.62);stroke:${ink700};stroke-width:0.014}
    .axon-floor{stroke:none;fill:#faf6ee}
    .axon-floor.room-bedroom{fill:#f8f5ef}
    .axon-floor.room-bathroom{fill:#eef3f4}
    .axon-floor.room-kitchen{fill:#f3f1e9}
    .axon-floor.room-living{fill:#faf6ee}
    .axon-floor.room-balcony{fill:#f0f3ef}
    .axon-floor.room-service{fill:#f1f0ec}
    .axon-floor-corridor{fill:#e9eef0}
    .axon-room-tag{fill:${ink500};font-weight:640;letter-spacing:0.1em;paint-order:stroke;stroke:rgba(255,255,255,0.85);stroke-width:0.08;stroke-linejoin:round}
    .circ-spine{stroke:#007d78;stroke-width:0.16;stroke-dasharray:0.5 0.32;stroke-linecap:round;fill:none;opacity:0.9}
    .circ-flow{stroke:#007d78;stroke-width:0.085;stroke-dasharray:0.3 0.22;stroke-linecap:round;stroke-linejoin:round;fill:none;opacity:0.95}
    .circ-entry-flow{stroke-width:0.14;stroke-dasharray:none}
    .circ-spine-arrow{stroke-dasharray:none;stroke-width:0.12}
    .circ-arrow-head{fill:#007d78}
    .circ-door-dot{fill:#fff;stroke:#007d78;stroke-width:0.06}
    .circ-entry-dot{fill:#007d78;stroke:#fff;stroke-width:0.06}
  `;
  return style;
}

function downloadText(fileName, text, mimeType = "application/json") {
  const blob = new Blob([text], { type: mimeType });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(link.href);
}

function setBusy(busy, label) {
  state.busy = busy;
  if (!busy) {
    state.busyRunId = 0;
  }
  [
    els.generateBtn,
    els.validateBtn,
    els.loadSampleBtn,
    els.sampleSelect,
    els.setupGenerateBtn,
    els.openInputBtn,
    els.applyJsonBtn,
    els.formatBtn,
    els.downloadInputBtn
  ].forEach((button) => {
    if (button) {
      button.disabled = busy;
    }
  });
  if (els.previewFrame) {
    els.previewFrame.classList.toggle("is-busy", Boolean(busy));
  }
  renderSetupGuide(state.response ? state.response.output : null);
  updateExportActions(state.response ? state.response.output : null);
  if (busy) {
    setStatus(label || "Working");
  }
}

function setStatus(text) {
  els.runStatus.textContent = text;
}

function setEditorFromInput(input) {
  els.inputEditor.value = JSON.stringify(input, null, 2);
}

function ensureInputShape(input) {
  const next = clone(input || {});
  next.project = {
    id: "project",
    name: "Floor Plan Project",
    units: "m",
    tolerance: 0.01,
    seed: 1,
    ...(next.project || {})
  };
  next.floorplate = next.floorplate || {};
  next.floorplate.outer = next.floorplate.outer || { id: "floorplate-01", points: rectPoints(0, 0, 42, 22) };
  next.floorplate.outer.id = next.floorplate.outer.id || "floorplate-01";
  next.floorplate.outer.points = Array.isArray(next.floorplate.outer.points) && next.floorplate.outer.points.length >= 4
    ? next.floorplate.outer.points
    : rectPoints(0, 0, 42, 22);
  next.floorplate.holes = Array.isArray(next.floorplate.holes) ? next.floorplate.holes : [];
  next.fixedElements = Array.isArray(next.fixedElements) ? next.fixedElements : [];
  next.access = {
    entryPoints: [],
    verticalCoreAccess: [],
    corridorStartPoints: [],
    corridorEndPoints: [],
    corridorCenterlines: [],
    ...(next.access || {})
  };
  next.facade = {
    segments: [],
    daylightCapableEdges: [],
    nonDaylightEdges: [],
    ...(next.facade || {})
  };
  next.program = next.program || {};
  next.program.targetUnitTypes = Array.isArray(next.program.targetUnitTypes) && next.program.targetUnitTypes.length
    ? next.program.targetUnitTypes
    : unitTypes.map(defaultUnitTarget);
  next.program.roomTypes = Array.isArray(next.program.roomTypes) ? next.program.roomTypes : defaultRoomTypes();
  next.rules = {
    minCorridorWidth: 1.8,
    minRoomWidth: 2.4,
    minRoomDepth: 2.4,
    doorWidth: 0.9,
    wetRoomAdjacencyPreferred: true,
    requireDaylightForBedrooms: true,
    requireDaylightForLiving: true,
    minUnitArea: 25,
    ...(next.rules || {})
  };
  next.generationSettings = {
    variantCount: 4,
    timeLimitMilliseconds: 1000,
    strictness: "balanced",
    weightedVariation: true,
    scoringWeights: defaultScoringWeights(),
    ...(next.generationSettings || {})
  };
  return next;
}

function defaultUnitTarget(type) {
  const defaults = {
    studio: { minArea: 30, maxArea: 55, targetRatio: 0.35, weight: 1 },
    one_bed: { minArea: 50, maxArea: 78, targetRatio: 0.45, weight: 1 },
    two_bed: { minArea: 72, maxArea: 108, targetRatio: 0.2, weight: 0.8 }
  };
  return {
    type,
    targetCount: 0,
    ...(defaults[type] || defaults.studio)
  };
}

function defaultRoomTypes() {
  return [
    { type: "bedroom", minArea: 10, minWidth: 2.7, minDepth: 2.7, requiresDaylight: true },
    { type: "living", minArea: 14, minWidth: 3, minDepth: 3, requiresDaylight: true },
    { type: "bathroom", minArea: 3.5, minWidth: 1.6, minDepth: 2, isWet: true }
  ];
}

function defaultScoringWeights() {
  return {
    efficiency: 0.3,
    netGrossRatio: 0.2,
    unitMixMatch: 0.25,
    unitQuality: 0.15,
    daylight: 0.1
  };
}

function firstCore(input) {
  return (input.fixedElements || []).find((fixed) => String(fixed.type || "").toLowerCase() === "core") || input.fixedElements[0] || null;
}

function findUnitTarget(input, type) {
  return (input.program.targetUnitTypes || []).find((target) => target.type === type) || defaultUnitTarget(type);
}

function boundsOfPoints(points) {
  const valid = (points || []).filter((p) => Number.isFinite(Number(p.x)) && Number.isFinite(Number(p.y)));
  if (valid.length === 0) {
    return null;
  }
  const xs = valid.map((p) => Number(p.x));
  const ys = valid.map((p) => Number(p.y));
  const minX = Math.min(...xs);
  const maxX = Math.max(...xs);
  const minY = Math.min(...ys);
  const maxY = Math.max(...ys);
  return { minX, minY, maxX, maxY, width: maxX - minX, height: maxY - minY };
}

function polygonArea(points) {
  const valid = (points || []).filter((p) => Number.isFinite(Number(p.x)) && Number.isFinite(Number(p.y)));
  if (valid.length < 3) {
    return 0;
  }

  const sum = valid.reduce((total, point, index) => {
    const next = valid[(index + 1) % valid.length];
    return total + Number(point.x) * Number(next.y) - Number(next.x) * Number(point.y);
  }, 0);
  return Math.abs(sum) / 2;
}

function lineLength(centerline) {
  if (!centerline || !centerline.start || !centerline.end) {
    return 0;
  }

  const dx = Number(centerline.end.x) - Number(centerline.start.x);
  const dy = Number(centerline.end.y) - Number(centerline.start.y);
  return Math.sqrt(dx * dx + dy * dy);
}

function corridorWidth(detail) {
  const corridor = detail && detail.item ? detail.item : {};
  if (Number.isFinite(Number(corridor.width)) && Number(corridor.width) > 0) {
    return Number(corridor.width);
  }

  const bounds = detail ? detail.bounds : null;
  if (bounds) {
    return Math.min(bounds.width, bounds.height);
  }

  return state.input && state.input.rules ? Number(state.input.rules.minCorridorWidth) || 1.8 : 1.8;
}

function scalePointsToBox(points, bounds, width, depth) {
  if (!Array.isArray(points) || points.length === 0 || !bounds || bounds.width <= 0 || bounds.height <= 0) {
    return rectPoints(0, 0, width, depth);
  }
  const sx = width / bounds.width;
  const sy = depth / bounds.height;
  return points.map((point) => ({
    x: round((Number(point.x) - bounds.minX) * sx),
    y: round((Number(point.y) - bounds.minY) * sy)
  }));
}

function rectPoints(x, y, width, depth) {
  return [
    { x: round(x), y: round(y) },
    { x: round(x + width), y: round(y) },
    { x: round(x + width), y: round(y + depth) },
    { x: round(x), y: round(y + depth) }
  ];
}

function readNumber(input, fallback) {
  const value = Number.parseFloat(input.value);
  return Number.isFinite(value) ? value : fallback;
}

function readPositive(input, fallback) {
  const value = readNumber(input, fallback);
  return value > 0 ? value : fallback;
}

function fieldNumber(value) {
  if (!Number.isFinite(Number(value))) {
    return "";
  }
  const rounded = round(Number(value));
  return Number.isInteger(rounded) ? String(rounded) : String(rounded);
}

function round(value) {
  return Math.round(Number(value) * 1000) / 1000;
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function clone(value) {
  return JSON.parse(JSON.stringify(value || {}));
}

function formatNumber(value, digits) {
  if (!Number.isFinite(Number(value))) {
    return "-";
  }
  return Number(value).toFixed(digits);
}

function formatPercent(value, digits) {
  if (!Number.isFinite(Number(value))) {
    return "-";
  }
  return `${(Number(value) * 100).toFixed(digits)}%`;
}

function firstLine(value) {
  return String(value || "").split(/\r?\n/)[0];
}

function labelText(text) {
  return String(text || "").replace(/\s+\d+(\.\d+)?\s*m(?:2|²)$/i, "");
}

function humanizeCode(value) {
  return String(value || "")
    .replace(/^input\./, "")
    .replace(/^validation\./, "")
    .replace(/^geometry\./, "")
    .replaceAll("_", " ")
    .replaceAll(".", " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function displayUnitType(value) {
  return humanizeCode(value || "unit");
}

function shortUnitType(value) {
  switch (String(value || "").toLowerCase()) {
    case "studio":
      return "Studio";
    case "one_bed":
      return "1BR";
    case "two_bed":
      return "2BR";
    default:
      return displayUnitType(value);
  }
}

function titleCase(value) {
  return humanizeCode(String(value || "").replaceAll("-", " "));
}

function viewModeLabel(mode) {
  switch (mode) {
    case "axon":
      return "3D axon";
    case "model3d":
      return "3D model";
    case "circulation":
      return "Circulation";
    default:
      return "2D plan";
  }
}

function updateActiveNav(nextHash) {
  const hash = normalizeNavHash(nextHash);
  els.topNavLinks.forEach((link) => {
    link.classList.toggle("active", link.getAttribute("href") === hash);
  });
}

function navigateToHash(nextHash) {
  const hash = normalizeNavHash(nextHash);
  if (window.location.hash === hash) {
    updateActiveNav(hash);
    scrollToHashTarget(hash);
    return;
  }

  window.location.hash = hash;
}

function handleTopNavAction(action) {
  switch (action) {
    case "edit":
      navigateToHash("#plan");
      state.editMode = true;
      state.canvasTool = "select";
      renderAll();
      setStatus("Edit mode on — drag a room to resize or move it");
      break;
    case "variants": {
      const details = els.variantList ? els.variantList.closest("details") : null;
      if (details) {
        details.open = true;
        details.scrollIntoView({ behavior: "smooth", block: "center" });
      }
      setStatus("Variants");
      break;
    }
    case "export":
      navigateToHash("#exports");
      setStatus("Export");
      break;
    case "rhino":
      navigateToHash("#exports");
      flashExportCard("copy-rhino");
      setStatus("Rhino / Grasshopper export");
      break;
    default:
      break;
  }
}

function flashExportCard(action) {
  const card = document.querySelector(`[data-export-action="${action}"]`);
  if (!card) {
    return;
  }
  card.classList.add("nav-flash");
  window.setTimeout(() => card.classList.remove("nav-flash"), 1400);
}

function scrollToHashTarget(nextHash, instant = false) {
  const hash = normalizeNavHash(nextHash);
  const target = document.getElementById(hash.slice(1));
  if (!target) {
    return;
  }

  const topbar = document.querySelector(".topbar");
  const topbarHeight = topbar ? topbar.getBoundingClientRect().height : 70;
  const targetTop = target.getBoundingClientRect().top + window.scrollY;
  window.scrollTo({
    top: Math.max(0, targetTop - topbarHeight - 16),
    behavior: instant ? "auto" : "smooth"
  });
}

function normalizeNavHash(nextHash) {
  const allowed = new Set(["#setup", "#plan", "#schedule", "#exports"]);
  if (allowed.has(nextHash)) {
    return nextHash;
  }
  if (allowed.has(window.location.hash)) {
    return window.location.hash;
  }
  return "#plan";
}

function slugify(value) {
  const slug = String(value || "project")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return slug || "project";
}

function buildCliCommand() {
  const inputName = `${slugify(els.projectName.value || "floor-plan")}.json`;
  return [
    "dotnet run --project FloorPlanGeneration.Cli -- \\",
    `  --input ${inputName} \\`,
    "  --output floor-plan-output.json \\",
    `  --seed ${escapeCli(els.seedInput.value || "1")} \\`,
    `  --variants ${escapeCli(els.variantInput.value || "4")} \\`,
    "  --summary"
  ].join("\n");
}

function buildApiCopyText() {
  const variants = escapeCli(els.variantInput.value || "4");
  const seed = escapeCli(els.seedInput.value || "1");
  return [
    "POST /api/generate",
    "Content-Type: application/json",
    "",
    JSON.stringify({
      input: state.input || {},
      validateOnly: false,
      variants: Number(variants) || 4,
      seed: Number(seed) || 1
    }, null, 2)
  ].join("\n");
}

function buildRhinoHandoffText() {
  const output = state.response ? state.response.output : null;
  const variant = selectedVariant(output);
  if (!output || !variant) {
    return "Generate a variant before copying the Rhino handoff payload.";
  }

  return JSON.stringify({
    adapter: "rhino-grasshopper",
    schemaVersion: output.metadata ? output.metadata.schemaVersion : null,
    project: state.input ? state.input.project : null,
    selectedVariant: variant.variantId,
    // True when the geometry below includes manual studio edits on top of the
    // engine result (room/wall moves), so downstream consumers can tell a
    // hand-tuned plan from a raw generation.
    manualEdits: hasGeometryOverrides(),
    stableExternalIds: true,
    layers: output.metadata ? output.metadata.layers : {},
    geometryCounts: {
      units: countOf(variant.units),
      rooms: countOf(variant.rooms),
      corridors: countOf(variant.corridors),
      walls: countOf(variant.walls),
      doors: countOf(variant.doorsOpenings),
      labels: countOf(variant.labels)
    },
    geometry: {
      variantId: variant.variantId,
      status: variant.status,
      externalId: variant.externalId,
      metrics: variant.metrics || {},
      units: variant.units || [],
      rooms: variant.rooms || [],
      corridors: variant.corridors || [],
      walls: variant.walls || [],
      doorsOpenings: variant.doorsOpenings || [],
      labels: variant.labels || []
    },
    hypergraph: variant.topology ? variant.topology.hypergraph : null,
    cli: buildCliCommand(),
    note: "Adapter-ready payload with selected variant polygons, walls, doors, labels, layers, external ids, and topology.hypergraph."
  }, null, 2);
}

function buildBimHandoffText() {
  const output = state.response ? state.response.output : null;
  const variant = selectedVariant(output);
  if (!output || !variant) {
    return "Generate a variant before copying the BIM handoff payload.";
  }

  return JSON.stringify({
    adapter: "ifc-bim",
    schemaVersion: output.metadata ? output.metadata.schemaVersion : null,
    project: state.input ? state.input.project : null,
    selectedVariant: variant.variantId,
    manualEdits: hasGeometryOverrides(),
    ifcGuidSource: "externalId",
    spaces: (variant.units || []).map((unit) => ({
      id: unit.id,
      type: unit.type,
      area: unit.area,
      externalId: unit.externalId,
      roomCount: unit.rooms ? unit.rooms.length : 0
    })),
    validation: summarizeValidation(variant),
    note: "This is an adapter-ready BIM payload. A dedicated IFC adapter should translate these stable ids and spaces into IFC entities."
  }, null, 2);
}

function escapeCli(value) {
  return String(value).replace(/[^\w.-]/g, "");
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

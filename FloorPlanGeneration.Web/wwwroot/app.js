const state = {
  samples: [],
  input: null,
  response: null,
  lastPreviewResponse: null,
  selectedVariantId: "",
  viewMode: "plan",
  zoom: 1,
  editMode: false,
  editReadout: "",
  setupStep: "floorplate",
  inputDirty: false,
  previewStale: false,
  autoGenerateTimer: 0,
  runSerial: 0,
  inputRevision: 0,
  pendingRunMode: "",
  busy: false,
  busyRunId: 0,
  dragEdit: null,
  selection: null,
  syncing: false
};

const draftKey = "floor-engine-web-draft-v2";
const unitTypes = ["studio", "one_bed", "two_bed"];
const setupSteps = ["floorplate", "core", "rules", "mix", "generate"];

const els = {
  sampleSelect: document.getElementById("sampleSelect"),
  setupForm: document.getElementById("setupForm"),
  setupSubtitle: document.getElementById("setupSubtitle"),
  setupReview: document.getElementById("setupReview"),
  setupPrevBtn: document.getElementById("setupPrevBtn"),
  setupNextBtn: document.getElementById("setupNextBtn"),
  setupGenerateBtn: document.getElementById("setupGenerateBtn"),
  projectName: document.getElementById("projectName"),
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
  editReadout: document.getElementById("editReadout"),
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
  setupStepButtons: Array.from(document.querySelectorAll("[data-setup-step-button]")),
  setupStepPanels: Array.from(document.querySelectorAll("[data-setup-step]")),
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
  try {
    await loadSamples();
    if (!restoreDraft()) {
      await loadSelectedSample(false);
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
  els.validateBtn.addEventListener("click", () => runEngine(true));
  els.generateBtn.addEventListener("click", () => runEngine(false));
  els.openInputBtn.addEventListener("click", () => els.inputFile.click());
  els.applyJsonBtn.addEventListener("click", applyJsonFromEditor);
  els.inputFile.addEventListener("change", openInputFile);
  els.setupForm.addEventListener("input", handleSetupInput);
  els.setupForm.addEventListener("change", handleSetupInput);
  els.setupStepButtons.forEach((button) => button.addEventListener("click", () => setSetupStep(button.dataset.setupStepButton)));
  els.setupPrevBtn.addEventListener("click", () => moveSetupStep(-1));
  els.setupNextBtn.addEventListener("click", () => moveSetupStep(1));
  els.setupGenerateBtn.addEventListener("click", async () => {
    await runEngine(false);
    navigateToHash("#plan");
  });
  els.inputEditor.addEventListener("input", saveDraft);
  els.formatBtn.addEventListener("click", formatInput);
  els.downloadInputBtn.addEventListener("click", () => downloadText("floor-plan-input.json", els.inputEditor.value));
  els.saveSvgBtn.addEventListener("click", saveSvg);
  els.exportSummary.addEventListener("click", handleExportAction);
  els.exportCardGrid.addEventListener("click", handleExportAction);
  els.modeButtons.forEach((button) => button.addEventListener("click", () => setViewMode(button.dataset.viewMode)));
  els.canvasButtons.forEach((button) => button.addEventListener("click", () => handleCanvasAction(button.dataset.canvasAction)));
  els.topNavLinks.forEach((link) => link.addEventListener("click", (event) => {
    event.preventDefault();
    navigateToHash(link.getAttribute("href"));
  }));
  window.addEventListener("hashchange", () => {
    updateActiveNav();
    scrollToHashTarget();
  });
  els.planSvg.addEventListener("click", handlePlanClick);
  els.planSvg.addEventListener("keydown", handlePlanActionKeyDown);
  els.planSvg.addEventListener("pointerdown", handlePlanPointerDown);
  window.addEventListener("pointermove", handlePlanPointerMove);
  window.addEventListener("pointerup", finishPlanPointerEdit);
  window.addEventListener("pointercancel", finishPlanPointerEdit);
  els.selectionInspector.addEventListener("click", handleInspectorAction);
  els.roomScheduleList.addEventListener("click", handleRoomScheduleClick);
  els.variantSelect.addEventListener("change", () => {
    state.selectedVariantId = els.variantSelect.value;
    state.zoom = 1;
    renderAll();
  });

  els.inputEditor.addEventListener("dragover", (event) => {
    event.preventDefault();
    els.inputEditor.classList.add("dragging");
  });
  els.inputEditor.addEventListener("dragleave", () => els.inputEditor.classList.remove("dragging"));
  els.inputEditor.addEventListener("drop", openDroppedInputFile);
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
      savedAt: new Date().toISOString()
    }));
  } catch (_) {
    // Draft persistence is a convenience; generation should keep working without it.
  }
}

function setInput(input, options = {}) {
  state.input = ensureInputShape(clone(input));
  state.inputRevision += 1;
  state.runSerial += 1;
  state.inputDirty = false;
  clearAutoGenerate();
  state.dragEdit = null;
  state.selection = null;
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

function handleSetupInput() {
  if (state.syncing) {
    return;
  }

  syncInputFromForm();
  setEditorFromInput(state.input);
  saveDraft();
  markInputDirty("Updating plan", 650);
  renderAll();
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

    els.projectName.value = project.name || "";
    els.floorWidth.value = fieldNumber(floorBounds ? floorBounds.width : 42);
    els.floorDepth.value = fieldNumber(floorBounds ? floorBounds.height : 22);
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

    els.setupSubtitle.textContent = project.id || "project";
  } finally {
    state.syncing = false;
  }
}

function syncInputFromForm() {
  const input = ensureInputShape(state.input || {});
  const floorBounds = boundsOfPoints(input.floorplate.outer.points) || { minX: 0, minY: 0, width: 42, height: 22 };
  const width = readPositive(els.floorWidth, floorBounds.width);
  const depth = readPositive(els.floorDepth, floorBounds.height);
  const scaleX = floorBounds.width > 0 ? width / floorBounds.width : 1;
  const scaleY = floorBounds.height > 0 ? depth / floorBounds.height : 1;

  input.project.name = els.projectName.value.trim() || "Floor Plan Project";
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

  input.rules.minCorridorWidth = readPositive(els.minCorridorWidth, input.rules.minCorridorWidth || 1.8);
  input.rules.minUnitArea = readPositive(els.minUnitArea, input.rules.minUnitArea || 25);
  input.rules.minRoomWidth = readPositive(els.minRoomWidth, input.rules.minRoomWidth || 2.4);
  input.rules.minRoomDepth = input.rules.minRoomDepth || input.rules.minRoomWidth;
  input.rules.doorWidth = input.rules.doorWidth || 0.9;
  input.rules.wetRoomAdjacencyPreferred = input.rules.wetRoomAdjacencyPreferred !== false;
  input.rules.requireDaylightForBedrooms = els.daylightBedrooms.checked;
  input.rules.requireDaylightForLiving = els.daylightLiving.checked;

  input.generationSettings.variantCount = clamp(Math.trunc(readNumber(els.variantInput, input.generationSettings.variantCount || 4)), 1, 20);
  input.generationSettings.strictness = els.strictnessInput.value || "balanced";
  input.generationSettings.timeLimitMilliseconds = input.generationSettings.timeLimitMilliseconds || 1000;
  input.generationSettings.weightedVariation = input.generationSettings.weightedVariation !== false;
  input.generationSettings.scoringWeights = input.generationSettings.scoringWeights || defaultScoringWeights();

  applyCoreFromForm(input, width, depth);
  input.program.targetUnitTypes = readUnitMixFromForm(input);
  if (!Array.isArray(input.program.roomTypes) || input.program.roomTypes.length === 0) {
    input.program.roomTypes = defaultRoomTypes();
  }

  state.input = input;
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
      state.lastPreviewResponse = response;
      state.selectedVariantId = response.bestVariantId || firstVariantId(response.output);
    } else if (!state.lastPreviewResponse) {
      state.selectedVariantId = "";
    }
    state.previewStale = !hasPreview && Boolean(state.lastPreviewResponse);
    state.inputDirty = false;
    setStatus(`${friendlyStatus(response.status)} - ${response.validVariantCount}/${response.variantCount} valid variants`);
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
  els.saveSvgBtn.disabled = !exportReady || !hasPreview || staleGeneratedPreview;
}

function setSetupStep(step) {
  if (!setupSteps.includes(step)) {
    return;
  }

  state.setupStep = step;
  renderSetupGuide(state.response ? state.response.output : null);
  setStatus(`${setupStepLabel(step)} setup`);
}

function moveSetupStep(delta) {
  const index = setupSteps.indexOf(state.setupStep);
  const nextIndex = clamp(index + delta, 0, setupSteps.length - 1);
  setSetupStep(setupSteps[nextIndex]);
}

function renderSetupGuide(output) {
  const activeIndex = setupSteps.indexOf(state.setupStep);
  const safeIndex = activeIndex >= 0 ? activeIndex : 0;
  const activeStep = setupSteps[safeIndex];
  state.setupStep = activeStep;

  els.setupStepButtons.forEach((button) => {
    const buttonIndex = setupSteps.indexOf(button.dataset.setupStepButton);
    const active = button.dataset.setupStepButton === activeStep;
    button.classList.toggle("active", active);
    button.classList.toggle("complete", buttonIndex >= 0 && buttonIndex < safeIndex);
    button.setAttribute("aria-pressed", active ? "true" : "false");
  });

  els.setupStepPanels.forEach((panel) => {
    panel.hidden = false;
  });

  els.setupPrevBtn.disabled = state.busy || safeIndex === 0;
  els.setupNextBtn.disabled = state.busy;
  els.setupGenerateBtn.disabled = state.busy;
  els.setupPrevBtn.hidden = true;
  els.setupNextBtn.hidden = true;
  els.setupGenerateBtn.hidden = false;
  els.setupReview.innerHTML = buildSetupReview(output);
}

function setupStepLabel(step) {
  switch (step) {
    case "core":
      return "Core";
    case "rules":
      return "Rules";
    case "mix":
      return "Unit mix";
    case "generate":
      return "Generate";
    default:
      return "Floorplate";
  }
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
      ? `${friendlyStatus(output.status)} - ${variant.units.length} units`
      : "Ready to generate";

  const rows = [
    ["Project", input.project ? input.project.name : "Floor Plan Project"],
    [
      "Floorplate",
      floorBounds
        ? `${formatNumber(floorBounds.width, 1)} x ${formatNumber(floorBounds.height, 1)} m`
        : "Not set"
    ],
    [
      "Core",
      coreBounds
        ? `${formatNumber(coreBounds.width, 1)} x ${formatNumber(coreBounds.height, 1)} m at ` +
          `${formatNumber(coreBounds.minX, 1)}, ${formatNumber(coreBounds.minY, 1)}`
        : "No core"
    ],
    ["Rules", `Corridor ${formatNumber(rules.minCorridorWidth, 1)} m, min unit ${formatNumber(rules.minUnitArea, 0)} m2`],
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
    els.planSubtitle.textContent = `${els.planSubtitle.textContent} - regenerating from edits`;
    els.resultSubtitle.textContent = "Edits pending - last generated plan stays visible until the engine refreshes it";
  } else if (state.previewStale) {
    els.planSubtitle.textContent = `${els.planSubtitle.textContent} - previous generated plan shown`;
    els.resultSubtitle.textContent = `${els.resultSubtitle.textContent} - previous plan kept visible`;
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
  els.setupSubtitle.textContent = state.input && state.input.project ? state.input.project.id : "project";

  if (!output) {
    els.resultSubtitle.textContent = `${projectName} ready for generation`;
    els.planSubtitle.textContent = floorBounds
      ? `Input outline ${formatNumber(floorBounds.width, 1)} x ${formatNumber(floorBounds.height, 1)} m`
      : "Input outline";
    els.scheduleSubtitle.textContent = targetMix ? `Target mix: ${targetMix}` : "Generate to populate schedule";
    return;
  }

  const variant = selectedVariant(visualOutput);
  const valid = output.variants ? output.variants.filter((v) => v.validation && v.validation.passed).length : 0;
  const summary = diagnosticSummary(output);
  const diagnosticText = diagnosticSubtitleText(summary);
  const totalVariants = output.variants ? output.variants.length : 0;
  const resultStatus = `${friendlyStatus(output.status)} - ${valid}/${totalVariants} valid`;
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
  els.planSubtitle.textContent = `${variant.variantId} - score ${formatNumber(metrics.score, 3)} - net/gross ${formatNumber(metrics.netGrossRatio, 3)}`;
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
      const score = Number.isFinite(Number(metrics.score)) ? ` - ${formatNumber(metrics.score, 3)}` : "";
      return `<option value="${escapeHtml(variant.variantId)}">#${index + 1} ${escapeHtml(variant.variantId)} - ${escapeHtml(status)}${score}</option>`;
    })
    .join("");
  els.variantSelect.value = state.selectedVariantId || firstVariantId(output);
}

function renderPreview(output) {
  clearSvg();
  updateModeButtons();
  const variant = selectedVariant(output);
  const input = state.input;
  const metadata = output ? output.metadata : null;
  const bounds = metadata && metadata.floorplate ? metadata.floorplate.bounds : collectBounds(output, variant, input);

  if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
    els.planSvg.removeAttribute("viewBox");
    els.planSvg.dataset.viewMode = state.viewMode;
    els.emptyPreview.hidden = false;
    els.emptyPreview.textContent = "No plan available";
    renderLegend(false);
    return;
  }

  els.emptyPreview.hidden = Boolean(variant);
  els.emptyPreview.textContent = "Input outline";
  els.planSvg.dataset.viewMode = state.viewMode;
  const viewBox = previewViewBox(bounds, state.zoom);
  els.planSvg.setAttribute("viewBox", viewBox);
  els.planSvg.setAttribute("preserveAspectRatio", "xMidYMid meet");

  if (state.viewMode !== "axon") {
    renderDimensionGuides(bounds, metadata && metadata.projectUnits);
  }

  const group = svgEl("g", { transform: previewTransform() });
  els.planSvg.appendChild(group);

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
        group.appendChild(polygonEl(fixed.polygon.points, selectableClass("fixed", kind, fixed.id || "fixed"), {
          ...selectableAttributes(kind, fixed.id || "fixed"),
          "data-edit-target": fixed.id || "fixed"
        }));
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
  renderPlanQuickActions(output, bounds);
  renderLegend(Boolean(variant));
}

function previewViewBox(bounds, zoom) {
  const pad = Math.max(bounds.width, bounds.height) * 0.06;
  const baseWidth = bounds.width + pad * 2;
  const baseHeight = bounds.height + pad * 2;
  const safeZoom = clamp(Number(zoom) || 1, 1, 4);
  const boxWidth = baseWidth / safeZoom;
  const boxHeight = baseHeight / safeZoom;
  const centerX = bounds.minX + bounds.width / 2;
  const centerY = -(bounds.minY + bounds.height / 2);
  return [centerX - boxWidth / 2, centerY - boxHeight / 2, boxWidth, boxHeight]
    .map((value) => formatNumber(value, 3))
    .join(" ");
}

function previewTransform() {
  if (state.viewMode === "axon") {
    return "scale(1,-1) skewX(-12) rotate(-3)";
  }
  return "scale(1,-1)";
}

function renderDimensionGuides(bounds, units = "m") {
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
    return;
  }

  const maxDim = Math.max(bounds.width, bounds.height);
  const offset = Math.max(maxDim * 0.075, 1.6);
  const labelGap = Math.max(maxDim * 0.024, 0.58);
  const tick = Math.max(maxDim * 0.012, 0.28);
  const centerX = bounds.minX + bounds.width / 2;
  const centerY = bounds.minY + bounds.height / 2;
  const group = svgEl("g", { class: "dimension-guides", "aria-hidden": "true" });

  const topY = bounds.maxY + offset;
  appendDimensionLine(group, bounds.minX, topY, bounds.maxX, topY);
  appendDimensionLine(group, bounds.minX, topY - tick, bounds.minX, topY + tick, "dimension-tick");
  appendDimensionLine(group, bounds.maxX, topY - tick, bounds.maxX, topY + tick, "dimension-tick");
  appendDimensionText(group, `${formatNumber(bounds.width, 1)} ${units || "m"}`, centerX, topY + labelGap);

  const bottomY = bounds.minY - offset;
  appendDimensionLine(group, bounds.minX, bottomY, bounds.maxX, bottomY);
  appendDimensionLine(group, bounds.minX, bottomY - tick, bounds.minX, bottomY + tick, "dimension-tick");
  appendDimensionLine(group, bounds.maxX, bottomY - tick, bounds.maxX, bottomY + tick, "dimension-tick");

  const rightX = bounds.maxX + offset;
  appendDimensionLine(group, rightX, bounds.minY, rightX, bounds.maxY);
  appendDimensionLine(group, rightX - tick, bounds.minY, rightX + tick, bounds.minY, "dimension-tick");
  appendDimensionLine(group, rightX - tick, bounds.maxY, rightX + tick, bounds.maxY, "dimension-tick");
  appendDimensionText(group, `${formatNumber(bounds.height, 1)} ${units || "m"}`, rightX + labelGap, centerY, "vertical");
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

function appendDimensionText(group, label, x, y, orientation = "horizontal") {
  const renderedY = round(-y);
  const text = svgEl("text", {
    class: `dimension-label dimension-label-${orientation}`,
    x: round(x),
    y: renderedY,
    "text-anchor": "middle",
    "font-size": "0.68"
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

  appendScaleBarLine(scaleGroup, x1, y, x2, y, "scale-bar-line");
  appendScaleBarLine(scaleGroup, x1, y - tick, x1, y + tick, "scale-bar-tick");
  appendScaleBarLine(scaleGroup, x2, y - tick, x2, y + tick, "scale-bar-tick");
  appendScaleBarText(scaleGroup, "0", x1, y - tick * 1.7, "start");
  appendScaleBarText(scaleGroup, `${formatNumber(length, length >= 10 ? 0 : 1)} ${units || "m"}`, x2, y - tick * 1.7, "end");
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

function appendScaleBarText(group, label, x, y, anchor) {
  const text = svgEl("text", {
    class: "scale-bar-label",
    x: round(x),
    y: round(-y),
    "text-anchor": anchor,
    "font-size": "0.72"
  });
  text.textContent = label;
  group.appendChild(text);
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
  renderDaylightAndWindowBands(layer, variant, bounds);
  if (state.editMode || state.viewMode === "circulation") {
    renderCorridorCenterlines(layer, variant);
  }
  renderRoomFixtures(layer, variant);
  if (layer.childElementCount > 0) {
    group.appendChild(layer);
  }
}

function renderDaylightAndWindowBands(layer, variant, floorBounds) {
  (variant.rooms || []).forEach((room) => {
    if (!room || !room.daylight) {
      return;
    }

    const bounds = roomVisualBounds(room);
    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
      return;
    }

    const side = closestFacadeSide(bounds, floorBounds);
    const inset = Math.min(Math.max(Math.min(bounds.width, bounds.height) * 0.09, 0.12), 0.5);
    const thickness = Math.min(Math.max(Math.min(bounds.width, bounds.height) * 0.12, 0.16), 0.42);
    if (side === "left" || side === "right") {
      const height = bounds.height - inset * 2;
      if (height <= 0.1) {
        return;
      }
      layer.appendChild(rectEl(
        side === "right" ? bounds.maxX - thickness - inset * 0.25 : bounds.minX + inset * 0.25,
        bounds.minY + inset,
        thickness,
        height,
        "daylight-band"));
      return;
    }

    const width = bounds.width - inset * 2;
    if (width <= 0.1) {
      return;
    }
    layer.appendChild(rectEl(
      bounds.minX + inset,
      side === "top" ? bounds.maxY - thickness - inset * 0.25 : bounds.minY + inset * 0.25,
      width,
      thickness,
      "daylight-band"));
  });
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
  (variant.rooms || []).forEach((room) => appendRoomFixture(fixtures, room));
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
  const horizontal = inner.width >= inner.height;
  const bedWidth = horizontal ? inner.width * 0.48 : inner.width * 0.68;
  const bedHeight = horizontal ? inner.height * 0.68 : inner.height * 0.46;
  const bedX = horizontal
    ? inner.minX + inner.width * 0.08
    : inner.minX + (inner.width - bedWidth) / 2;
  const bedY = horizontal
    ? inner.minY + (inner.height - bedHeight) / 2
    : inner.minY + inner.height * 0.08;
  appendFixtureRect(group, bedX, bedY, bedWidth, bedHeight, "fixture-bed");

  if (horizontal) {
    appendFixtureRect(group, bedX + bedWidth * 0.08, bedY + bedHeight * 0.14, bedWidth * 0.22, bedHeight * 0.28, "fixture-pillow");
    appendFixtureRect(group, bedX + bedWidth * 0.08, bedY + bedHeight * 0.58, bedWidth * 0.22, bedHeight * 0.28, "fixture-pillow");
    appendFixtureRect(group, inner.maxX - inner.width * 0.15, inner.minY + inner.height * 0.12, inner.width * 0.1, inner.height * 0.76, "fixture-wardrobe");
  } else {
    appendFixtureRect(group, bedX + bedWidth * 0.15, bedY + bedHeight * 0.68, bedWidth * 0.28, bedHeight * 0.22, "fixture-pillow");
    appendFixtureRect(group, bedX + bedWidth * 0.57, bedY + bedHeight * 0.68, bedWidth * 0.28, bedHeight * 0.22, "fixture-pillow");
    appendFixtureRect(group, inner.minX + inner.width * 0.12, inner.maxY - inner.height * 0.16, inner.width * 0.76, inner.height * 0.1, "fixture-wardrobe");
  }
}

function appendBathroomFixture(group, bounds) {
  const inner = insetBounds(bounds, fixturePadding(bounds) * 0.85);
  const fixtureSize = Math.min(inner.width, inner.height);
  const bathWidth = Math.min(inner.width * 0.48, fixtureSize * 0.9);
  const bathHeight = Math.min(inner.height * 0.34, fixtureSize * 0.62);
  appendFixtureRect(group, inner.minX, inner.maxY - bathHeight, bathWidth, bathHeight, "fixture-bath");
  appendFixtureRect(
    group,
    inner.minX,
    inner.minY,
    Math.min(fixtureSize * 0.38, inner.width * 0.42),
    Math.min(fixtureSize * 0.38, inner.height * 0.42),
    "fixture-shower");
  appendFixtureCircle(group, inner.maxX - inner.width * 0.22, inner.minY + inner.height * 0.26, fixtureSize * 0.09, "fixture-toilet");
  appendFixtureRect(
    group,
    inner.maxX - inner.width * 0.33,
    inner.maxY - inner.height * 0.32,
    inner.width * 0.25,
    inner.height * 0.18,
    "fixture-sink");
}

function appendKitchenFixture(group, bounds) {
  const inner = insetBounds(bounds, fixturePadding(bounds) * 0.75);
  const horizontal = inner.width >= inner.height;
  if (horizontal) {
    const counterHeight = inner.height * 0.25;
    appendFixtureRect(group, inner.minX, inner.minY, inner.width, counterHeight, "fixture-counter");
    appendFixtureRect(
      group,
      inner.minX + inner.width * 0.12,
      inner.minY + counterHeight * 0.18,
      counterHeight * 0.58,
      counterHeight * 0.58,
      "fixture-sink");
    appendFixtureRect(
      group,
      inner.maxX - counterHeight * 0.9,
      inner.minY + counterHeight * 0.16,
      counterHeight * 0.62,
      counterHeight * 0.62,
      "fixture-appliance");
  } else {
    const counterWidth = inner.width * 0.25;
    appendFixtureRect(group, inner.minX, inner.minY, counterWidth, inner.height, "fixture-counter");
    appendFixtureRect(
      group,
      inner.minX + counterWidth * 0.18,
      inner.minY + inner.height * 0.12,
      counterWidth * 0.58,
      counterWidth * 0.58,
      "fixture-sink");
    appendFixtureRect(
      group,
      inner.minX + counterWidth * 0.16,
      inner.maxY - counterWidth * 0.9,
      counterWidth * 0.62,
      counterWidth * 0.62,
      "fixture-appliance");
  }
}

function appendLivingFixture(group, bounds) {
  const inner = insetBounds(bounds, fixturePadding(bounds));
  const sofaHeight = Math.max(inner.height * 0.16, 0.28);
  appendFixtureRect(group, inner.minX + inner.width * 0.08, inner.minY + inner.height * 0.12, inner.width * 0.44, sofaHeight, "fixture-sofa");
  appendFixtureEllipse(group, inner.minX + inner.width * 0.5, inner.minY + inner.height * 0.52, inner.width * 0.16, inner.height * 0.11, "fixture-table");
  if (inner.width * inner.height >= 14) {
    appendFixtureEllipse(group, inner.maxX - inner.width * 0.22, inner.maxY - inner.height * 0.24, inner.width * 0.12, inner.height * 0.1, "fixture-table");
    appendFixtureRect(group, inner.maxX - inner.width * 0.34, inner.maxY - inner.height * 0.25, inner.width * 0.06, inner.height * 0.12, "fixture-chair");
    appendFixtureRect(group, inner.maxX - inner.width * 0.1, inner.maxY - inner.height * 0.25, inner.width * 0.06, inner.height * 0.12, "fixture-chair");
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
  appendFixtureEllipse(group, inner.minX + inner.width * 0.5, inner.minY + inner.height * 0.52, inner.width * 0.15, inner.height * 0.11, "fixture-table");
}

function fixturePadding(bounds) {
  return Math.min(Math.max(Math.min(bounds.width, bounds.height) * 0.12, 0.14), 0.65);
}

function appendFixtureRect(group, x, y, width, height, className) {
  if (width <= 0.04 || height <= 0.04) {
    return;
  }
  group.appendChild(rectEl(x, y, width, height, `fixture ${className}`));
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

  const radius = Math.max(Math.max(bounds.width, bounds.height) * 0.014, 0.12);
  if (state.editMode) {
    selectedHaloGripPoints(bounds).forEach((point) => {
      halo.appendChild(svgEl("circle", {
        class: "room-selected-halo-handle",
        cx: round(point.x),
        cy: round(point.y),
        r: round(radius)
      }));
    });
  }
  group.appendChild(halo);
}

function selectedHaloGripPoints(bounds) {
  const midX = bounds.minX + bounds.width / 2;
  const midY = bounds.minY + bounds.height / 2;
  return [
    { x: bounds.minX, y: bounds.minY },
    { x: midX, y: bounds.minY },
    { x: bounds.maxX, y: bounds.minY },
    { x: bounds.maxX, y: midY },
    { x: bounds.maxX, y: bounds.maxY },
    { x: midX, y: bounds.maxY },
    { x: bounds.minX, y: bounds.maxY },
    { x: bounds.minX, y: midY }
  ];
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
  group.appendChild(lineEl(wall.centerline.start, wall.centerline.end, "wall-backdrop"));
  group.appendChild(lineEl(
    wall.centerline.start,
    wall.centerline.end,
    selectableClass(`wall wall-${safeClassToken(wall.layerType || "partition")}`, "wall", wall.id),
    {
      ...attributes,
      "stroke-width": formatNumber(thickness, 3)
    }));
  group.appendChild(lineEl(wall.centerline.start, wall.centerline.end, "wall-hit", attributes));
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
  ].filter(Boolean).join(" - ");
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
  const swing = Math.max(width * 0.9, 0.62);
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
  if (!state.editMode || state.viewMode !== "circulation") {
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
      x: round(centerX),
      y: round(-centerY + fontSize * 0.34),
      "text-anchor": "middle",
      "font-size": formatNumber(fontSize, 2)
    });
    const title = svgEl("tspan", { x: round(centerX), dy: 0 });
    title.textContent = compactRoomLabel(room);
    text.appendChild(title);
    if (!densePlan && Math.min(bounds.width, bounds.height) >= 3.6) {
      const meta = svgEl("tspan", {
        class: "room-label-meta",
        x: round(centerX),
        dy: formatNumber(fontSize * 1.3, 2)
      });
      meta.textContent = roomDimensionText(room, bounds);
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
    return minDimension >= 2.75 && area >= 7;
  }

  return minDimension >= 1.9 && area >= 4.5;
}

function roomLabelFontSize(bounds, densePlan) {
  const minDimension = Math.min(bounds.width, bounds.height);
  const max = densePlan ? 0.5 : 0.66;
  const min = densePlan ? 0.34 : 0.4;
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

function roomDimensionText(room, bounds) {
  const dimensions = room && room.dimensions ? room.dimensions : {};
  const width = Number(dimensions.width) || (bounds ? bounds.width : 0);
  const depth = Number(dimensions.depth) || (bounds ? bounds.height : 0);
  if (width > 0 && depth > 0) {
    return `${formatNumber(width, 1)} x ${formatNumber(depth, 1)}`;
  }

  const area = Number(room && room.area);
  return Number.isFinite(area) ? `${formatNumber(area, 1)} m2` : "";
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
  if (type.includes("utility") || type.includes("storage") || type.includes("laundry")) {
    return "room-service";
  }
  return "room-general";
}

function safeClassToken(value) {
  return String(value || "item").toLowerCase().replace(/[^a-z0-9_-]+/g, "-");
}

function setViewMode(mode) {
  if (!["plan", "axon", "circulation"].includes(mode)) {
    return;
  }

  state.viewMode = mode;
  state.zoom = 1;
  if (mode === "axon") {
    state.editMode = false;
    state.dragEdit = null;
  }
  state.editReadout = "";
  renderAll();
  setStatus(`${viewModeLabel(mode)} view`);
}

function handleCanvasAction(action) {
  if (action === "edit-toggle") {
    if (state.viewMode === "axon") {
      state.viewMode = "plan";
    }
    state.editMode = !state.editMode;
    state.dragEdit = null;
    if (state.editMode && !state.selection) {
      state.selection = { kind: "floorplate", id: "floorplate" };
    }
    state.editReadout = state.editMode ? editSummary(state.input) : "";
    renderAll();
    setStatus(state.editMode ? "Edit constraints mode" : "Plan review mode");
    return;
  }

  if (!els.planSvg.getAttribute("viewBox")) {
    setStatus("Generate a plan before using canvas tools");
    return;
  }

  if (action === "select") {
    state.editMode = false;
    state.dragEdit = null;
    state.editReadout = "";
    renderAll();
    setStatus("Select tool");
    return;
  }

  if (action === "pan") {
    state.editMode = false;
    state.dragEdit = null;
    state.editReadout = "";
    renderAll();
    setStatus("Pan tool");
    return;
  }

  if (action === "zoom-in") {
    state.zoom = clamp(state.zoom * 1.25, 1, 4);
  } else if (action === "zoom-out") {
    state.zoom = clamp(state.zoom / 1.25, 1, 4);
  } else if (action === "fit") {
    state.zoom = 1;
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
  const planAction = event.target.closest ? event.target.closest("[data-plan-action]") : null;
  if (planAction) {
    event.preventDefault();
    event.stopPropagation();
    runSelectedPlanAction(planAction.dataset.planAction);
    return;
  }

  if (event.target.closest && event.target.closest("[data-edit-action]")) {
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

function updateCanvasUi(output) {
  const editActive = Boolean(state.editMode && state.viewMode !== "axon");
  const editToggle = els.canvasButtons.find((button) => button.dataset.canvasAction === "edit-toggle");
  if (editToggle) {
    editToggle.classList.toggle("active", editActive);
    editToggle.setAttribute("aria-pressed", editActive ? "true" : "false");
  }

  els.previewFrame.classList.toggle("is-edit-mode", editActive);
  const selectedSummary = selectionInlineSummary(selectedElementDetails(output));
  const text = editActive ? (state.editReadout || editSummary(state.input)) : selectedSummary;
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
        title: `${displayUnitType(unit.type)} ${unit.id}`,
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
        title: `${displayRoomType(room)} ${room.id}`,
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
        title: `Corridor ${corridor.id}`,
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
    return `${selectionKindLabel(detail.kind)} ${detail.id} - ${formatNumber(detail.area, 1)} m2`;
  }

  if (detail.bounds) {
    return `${selectionKindLabel(detail.kind)} - ${formatNumber(detail.bounds.width, 1)} x ${formatNumber(detail.bounds.height, 1)} m`;
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
    rows.push(["Size", `${formatNumber(detail.bounds.width, 1)} x ${formatNumber(detail.bounds.height, 1)} m`]);
  }
  if (Number.isFinite(Number(detail.area))) {
    rows.push(["Area", `${formatNumber(detail.area, 1)} m2`]);
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
    if (detail.item.dimensions) {
      rows.push([
        "Dimensions",
        `${formatNumber(detail.item.dimensions.width, 1)} x ${formatNumber(detail.item.dimensions.depth, 1)} m`
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
  const handle = event.target.closest ? event.target.closest("[data-edit-action]") : null;
  if (!handle || !state.editMode || !state.input || state.viewMode === "axon") {
    return;
  }

  const point = clientToModelPoint(event);
  if (!point) {
    return;
  }

  event.preventDefault();
  const detail = selectedElementDetails(currentVisualOutput());
  state.dragEdit = {
    action: handle.dataset.editAction,
    startPoint: point,
    startInput: clone(state.input),
    selection: selectionEditSnapshot(detail)
  };
  try {
    handle.setPointerCapture(event.pointerId);
  } catch (_) {
    // Pointer capture is helpful but not required for SVG editing.
  }
  els.previewFrame.classList.add("is-dragging");
  setStatus("Editing plan");
}

function handlePlanPointerMove(event) {
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

  state.input = edited;
  syncFormFromInput(state.input);
  setEditorFromInput(state.input);
  saveDraft();
  state.editReadout = editSummary(state.input);
  markInputDirty("Editing plan", null);
  renderAll();
}

function finishPlanPointerEdit() {
  if (!state.dragEdit) {
    return;
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

function applyCanvasEdit(edit, point) {
  const input = ensureInputShape(edit.startInput);
  const floorBounds = boundsOfPoints(edit.startInput.floorplate.outer.points);
  if (!floorBounds) {
    return null;
  }

  if (edit.action === "floor-width" || edit.action === "floor-depth" || edit.action === "floor-size") {
    const dx = point.x - edit.startPoint.x;
    const dy = point.y - edit.startPoint.y;
    const width = edit.action === "floor-depth"
      ? floorBounds.width
      : clamp(round(floorBounds.width + dx), 8, 300);
    const depth = edit.action === "floor-width"
      ? floorBounds.height
      : clamp(round(floorBounds.height + dy), 8, 300);
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
    const x = clamp(round(startCoreBounds.minX + dx), currentFloorBounds.minX, currentFloorBounds.maxX - startCoreBounds.width);
    const y = clamp(round(startCoreBounds.minY + dy), currentFloorBounds.minY, currentFloorBounds.maxY - startCoreBounds.height);
    core.polygon.points = rectPoints(x, y, startCoreBounds.width, startCoreBounds.height);
    refreshAccessFromCore(input);
    return input;
  }

  if (edit.action === "core-size") {
    const width = clamp(round(point.x - startCoreBounds.minX), 1, currentFloorBounds.maxX - startCoreBounds.minX);
    const depth = clamp(round(point.y - startCoreBounds.minY), 1, currentFloorBounds.maxY - startCoreBounds.minY);
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
    ? `Floor ${formatNumber(floorBounds.width, 1)} x ${formatNumber(floorBounds.height, 1)} m`
    : "Floor outline";
  const coreText = coreBounds
    ? `Core ${formatNumber(coreBounds.width, 1)} x ${formatNumber(coreBounds.height, 1)} m at ` +
      `${formatNumber(coreBounds.minX, 1)}, ${formatNumber(coreBounds.minY, 1)}`
    : "No core";
  return `${floorText} - ${coreText}`;
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

  const handleRadius = Math.max(Math.max(floorBounds.width, floorBounds.height) * 0.018, 0.42);
  const handleInset = Math.max(handleRadius * 1.9, 0.9);
  const floorGroup = svgEl("g", { class: "edit-handles" });
  const floorWidthPoint = { x: floorBounds.maxX, y: floorBounds.minY + floorBounds.height / 2 };
  const floorDepthPoint = { x: floorBounds.minX + floorBounds.width / 2, y: floorBounds.maxY - handleInset };
  const floorSizePoint = { x: floorBounds.maxX, y: floorBounds.maxY - handleInset };
  floorGroup.appendChild(editHandle(
    "floor-width",
    floorWidthPoint.x,
    floorWidthPoint.y,
    handleRadius,
    "Resize floorplate width"));
  floorGroup.appendChild(editHandleLabel("Width", floorWidthPoint.x, floorWidthPoint.y, handleRadius, "left"));
  floorGroup.appendChild(editHandle(
    "floor-depth",
    floorDepthPoint.x,
    floorDepthPoint.y,
    handleRadius,
    "Resize floorplate depth"));
  floorGroup.appendChild(editHandleLabel("Depth", floorDepthPoint.x, floorDepthPoint.y, handleRadius, "below"));
  floorGroup.appendChild(editHandle(
    "floor-size",
    floorSizePoint.x,
    floorSizePoint.y,
    handleRadius * 1.1,
    "Resize floorplate"));
  floorGroup.appendChild(editHandleLabel("Resize", floorSizePoint.x, floorSizePoint.y, handleRadius * 1.1, "left-below"));

  const core = firstCore(input);
  const coreBounds = core && core.polygon ? boundsOfPoints(core.polygon.points) : null;
  if (coreBounds) {
    const coreMovePoint = { x: coreBounds.minX + coreBounds.width / 2, y: coreBounds.minY + coreBounds.height / 2 };
    const coreSizePoint = { x: coreBounds.maxX, y: coreBounds.maxY };
    floorGroup.appendChild(editHandle(
      "core-move",
      coreMovePoint.x,
      coreMovePoint.y,
      handleRadius * 1.2,
      "Move core"));
    floorGroup.appendChild(editHandleLabel("Move", coreMovePoint.x, coreMovePoint.y, handleRadius * 1.2));
    floorGroup.appendChild(editHandle("core-size", coreSizePoint.x, coreSizePoint.y, handleRadius, "Resize core"));
    floorGroup.appendChild(editHandleLabel("Core size", coreSizePoint.x, coreSizePoint.y, handleRadius));
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
  const handleRadius = Math.max(Math.max(bounds.width, bounds.height) * 0.05, 0.18);
  const editGroup = svgEl("g", { class: "edit-selection-handles" });
  editGroup.appendChild(svgEl("rect", {
    class: "edit-selection-box",
    x: round(bounds.minX),
    y: round(bounds.minY),
    width: round(bounds.width),
    height: round(bounds.height)
  }));

  if (detail.kind === "unit") {
    editGroup.appendChild(editHandle("unit-target-area", bounds.maxX, bounds.maxY, handleRadius, "Drag to fit this unit type target area"));
  } else if (detail.kind === "room") {
    editGroup.appendChild(editHandle("room-min-size", bounds.maxX, bounds.maxY, handleRadius, "Drag to set room minimum size"));
  } else if (detail.kind === "corridor") {
    const horizontal = bounds.width >= bounds.height;
    const x = horizontal ? bounds.minX + bounds.width / 2 : bounds.maxX;
    const y = horizontal ? bounds.maxY : bounds.minY + bounds.height / 2;
    editGroup.appendChild(editHandle("corridor-width", x, y, handleRadius, "Drag to set corridor width"));
  }

  group.appendChild(editGroup);
}

function editHandle(action, x, y, radius, label) {
  const handle = svgEl("circle", {
    class: `edit-handle edit-${action}`,
    "data-edit-action": action,
    cx: round(x),
    cy: round(y),
    r: Math.max(radius, 0.34)
  });
  const title = svgEl("title");
  title.textContent = label;
  handle.appendChild(title);
  return handle;
}

function editHandleLabel(text, x, y, radius, placement = "right") {
  const offsetX = Math.max(radius * 1.55, 0.72);
  const offsetY = Math.max(radius * 0.45, 0.28);
  const placeLeft = placement.includes("left");
  const placeBelow = placement.includes("below");
  const label = svgEl("text", {
    class: "edit-handle-label",
    x: round(x + (placeLeft ? -offsetX : offsetX)),
    y: round(-(y + (placeBelow ? -offsetY : offsetY))),
    transform: "scale(1,-1)",
    "text-anchor": placeLeft ? "end" : "start",
    "font-size": "0.72"
  });
  label.textContent = text;
  return label;
}

function renderPlanQuickActions(output, canvasBounds) {
  if (!state.editMode || state.viewMode === "axon") {
    return;
  }

  const detail = selectedElementDetails(output);
  const actions = planActionsForDetail(detail);
  if (!detail || actions.length === 0 || !detail.bounds || !canvasBounds) {
    return;
  }

  const chips = actions.slice(0, 6).map(([action, label]) => ({
    action,
    label: shortPlanActionLabel(label),
    title: label,
    width: planActionChipWidth(shortPlanActionLabel(label))
  }));
  const maxPerRow = detail.kind === "core" ? 3 : 4;
  const rows = [];
  for (let index = 0; index < chips.length; index += maxPerRow) {
    rows.push(chips.slice(index, index + maxPerRow));
  }

  const chipHeight = Math.max(canvasBounds.height * 0.04, 1.65);
  const gap = Math.max(canvasBounds.width * 0.008, 0.34);
  const rowGap = Math.max(canvasBounds.height * 0.008, 0.32);
  const stripWidth = Math.max(...rows.map((row) => row.reduce((total, chip, index) => total + chip.width + (index ? gap : 0), 0)));
  const centerX = detail.bounds.minX + detail.bounds.width / 2;
  const minX = canvasBounds.minX + gap;
  const maxX = canvasBounds.maxX - stripWidth - gap;
  const x = clamp(centerX - stripWidth / 2, minX, Math.max(minX, maxX));
  let y = -(detail.bounds.maxY + chipHeight + rowGap);
  const topLimit = -canvasBounds.maxY - chipHeight - rowGap;
  if (y < topLimit) {
    y = -(detail.bounds.minY - rowGap);
  }

  const strip = svgEl("g", {
    class: "plan-action-strip",
    "aria-label": "Selected element edit actions"
  });

  let yOffset = 0;
  rows.forEach((row) => {
    let xOffset = 0;
    row.forEach((chip) => {
      strip.appendChild(planActionChip(chip.action, chip.label, chip.title, x + xOffset, y + yOffset, chip.width, chipHeight));
      xOffset += chip.width + gap;
    });
    yOffset += chipHeight + rowGap;
  });

  els.planSvg.appendChild(strip);
}

function planActionChip(action, label, title, x, y, width, height) {
  const group = svgEl("g", {
    class: "plan-action-chip",
    "data-plan-action": action,
    role: "button",
    tabindex: "0",
    transform: `translate(${formatNumber(x, 3)},${formatNumber(y, 3)})`
  });
  group.appendChild(svgEl("rect", {
    x: 0,
    y: 0,
    width: formatNumber(width, 3),
    height: formatNumber(height, 3),
    rx: formatNumber(Math.min(height / 2, 0.7), 3)
  }));
  const text = svgEl("text", {
    x: formatNumber(width / 2, 3),
    y: formatNumber(height / 2 + 0.22, 3),
    "text-anchor": "middle",
    "font-size": "0.68"
  });
  text.textContent = label;
  group.appendChild(text);
  const tooltip = svgEl("title");
  tooltip.textContent = title;
  group.appendChild(tooltip);
  return group;
}

function planActionChipWidth(label) {
  return clamp(2.6 + String(label || "").length * 0.46, 4.2, 10.8);
}

function shortPlanActionLabel(label) {
  switch (label) {
    case "More like this":
      return "More";
    case "Fewer like this":
      return "Less";
    case "Fit target area":
      return "Fit";
    case "Use as room minimum":
      return "Room min";
    case "Use as corridor width":
      return "Corridor";
    default:
      return label;
  }
}

function renderLegend(hasVariant) {
  const items = [
    ["Boundary", "boundary-swatch"],
    ["Core", "core-swatch"]
  ];
  if (hasVariant) {
    items.push(["Units", "unit-swatch"], ["Rooms", "room-swatch"], ["Corridor", "corridor-swatch"], ["Doors", "door-swatch"]);
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
    ["Sellable", metrics ? `${formatNumber(metrics.sellableArea, 1)} m2` : "-"],
    ["Circulation", metrics ? `${formatNumber(metrics.corridorArea, 1)} m2` : "-"],
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
    const item = document.createElement("button");
    item.type = "button";
    item.className = `variant-item${variant.variantId === state.selectedVariantId ? " active" : ""}`;
    item.setAttribute("aria-pressed", variant.variantId === state.selectedVariantId ? "true" : "false");
    item.innerHTML = `
      <div class="variant-title">
        <span>#${index + 1} ${escapeHtml(variant.variantId)}</span>
        <span class="pill ${escapeHtml(variant.status)}">${escapeHtml(friendlyStatus(variant.status))}</span>
      </div>
      <div class="variant-card-body">
        ${variantThumbnailSvg(variant, state.input)}
        <div>
          <div class="variant-meta">
            Score ${formatNumber(metrics.score, 3)} - Net/gross ${formatNumber(metrics.netGrossRatio, 3)} - ${units.length} units
          </div>
          <div class="variant-meta">
            ${escapeHtml(mix || "No unit mix")} - ${escapeHtml(checkText)}
          </div>
          <div class="variant-meta">
            Hypergraph ${hypergraph ? `${countOf(hypergraph.nodes)} nodes, ${countOf(hypergraph.hyperedges)} edges` : "not available"}
          </div>
        </div>
      </div>
      <progress class="score-bar" value="${scoreWidth}" max="100" aria-label="Variant score ${scoreWidth}%"></progress>
    `;
    item.addEventListener("click", () => {
      state.selectedVariantId = variant.variantId;
      state.zoom = 1;
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
            <td>${formatNumber(row.averageArea, 1)} m2</td>
            <td>${formatNumber(row.minArea, 1)} m2</td>
            <td>${formatNumber(row.maxArea, 1)} m2</td>
            <td>${formatNumber(row.totalArea, 1)} m2</td>
          </tr>
        `).join("")}
        <tr>
          <td><strong>Total</strong></td>
          <td><strong>${units.length}</strong></td>
          <td><strong>100%</strong></td>
          <td>-</td>
          <td>-</td>
          <td>-</td>
          <td><strong>${formatNumber(totalArea, 1)} m2</strong></td>
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
            <td>${formatNumber(unit.area, 1)} m2</td>
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
      <strong>${rooms.length} / ${formatNumber(totalArea, 1)} m2</strong>
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
          <small>${escapeHtml(unit.id)} - ${rooms.length} rooms - ${formatNumber(roomArea || unit.area || 0, 1)} m2</small>
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
    ? `${formatNumber(room.dimensions.width, 1)} x ${formatNumber(room.dimensions.depth, 1)}`
    : roomDimensionText(room, boundsOfPoints(room.polygon ? room.polygon.points : []));
  const notes = diagnosticsForElement(output, [room.id, room.unitId, room.externalId]);
  return `
    <button class="room-schedule-row${active ? " active" : ""}${notes.length ? " has-notes" : ""}" type="button"
        data-schedule-room-id="${escapeHtml(room.id)}"
        data-schedule-room-name="${escapeHtml(roomName)}"
        aria-pressed="${active ? "true" : "false"}">
      <span>
        <strong>${escapeHtml(roomName)}</strong>
        <small>${escapeHtml(dimensions || "-")} - ${room.daylight ? "Daylight" : "Interior"}</small>
      </span>
      <em>${notes.length ? `${notes.length} note${notes.length === 1 ? "" : "s"}` : `${formatNumber(room.area || 0, 1)} m2`}</em>
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
      <strong>${escapeHtml(variant ? `${variant.variantId} - ${validation.label}` : "Generate a variant first")}</strong>
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
      <span>AI / JSON</span>
      <strong>${escapeHtml(hypergraph ? "Agent-ready payload available" : "Validation-only output")}</strong>
      <button type="button" data-export-action="copy-json">Copy JSON</button>
    </div>
    <details class="advanced-details export-technical">
      <summary>Technical contract</summary>
      <div>
        <span>Schema and Layers</span>
        <strong>${escapeHtml(schema)}${layers ? ` - ${layers} layer keys` : ""}</strong>
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
            <em>${row.final ? "final" : "branch"}${row.area ? ` - ${formatNumber(row.area, 1)} m2` : ""}</em>
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
  const variants = output && Array.isArray(output.variants) ? output.variants : [];
  return variants.find((variant) => variant.variantId === state.selectedVariantId) || variants[0] || null;
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
    x1: start.x,
    y1: start.y,
    x2: end.x,
    y2: end.y,
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
  cloneSvg.insertBefore(svgStyleElement(), cloneSvg.firstChild);
  const xml = new XMLSerializer().serializeToString(cloneSvg);
  downloadText("floor-plan-preview.svg", xml, "image/svg+xml");
  setStatus("SVG saved");
}

function svgStyleElement() {
  const style = document.createElementNS("http://www.w3.org/2000/svg", "style");
  style.textContent = `
    .boundary{fill:#f9fbfb;stroke:#222831;stroke-width:0.18}
    .fixed{fill:#334155;stroke:#111820;stroke-width:0.12}
    .corridor{fill:#f9d889;stroke:#b7791f;stroke-width:0.12}
    .unit{fill:#d9ebf7;stroke:#3f7ea6;stroke-width:0.12}
    .unit-one_bed{fill:#dff0df;stroke:#4f8d50}
    .unit-two_bed{fill:#eadff6;stroke:#7d64a3}
    .room{fill:rgba(255,255,255,0.42);stroke:rgba(31,36,40,0.38);stroke-width:0.06}
    .svg-label{fill:#1f2428;font-size:1.35px;font-weight:700}
    .edit-handle{fill:#fff;stroke:#007d78;stroke-width:0.42;vector-effect:non-scaling-stroke}
    .edit-core-move{fill:#007d78;stroke:#fff;stroke-width:0.52}
    .edit-handle-label{fill:#0d4f4b;paint-order:stroke;stroke:rgba(255,255,255,0.92)}
    .edit-handle-label{stroke-width:0.2;stroke-linejoin:round;font-size:0.78px;font-weight:900}
    .edit-handle-label{letter-spacing:0;pointer-events:none}
    .door{fill:#3867b7;stroke:#fff;stroke-width:0.08}
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
  return String(text || "").replace(/\s+\d+(\.\d+)?\s*m2$/i, "");
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

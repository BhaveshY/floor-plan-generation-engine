const state = {
  samples: [],
  input: null,
  response: null,
  selectedVariantId: "",
  viewMode: "plan",
  zoom: 1,
  editMode: false,
  editReadout: "",
  inputDirty: false,
  autoGenerateTimer: 0,
  runSerial: 0,
  dragEdit: null,
  selection: null,
  syncing: false
};

const draftKey = "floor-engine-web-draft-v2";
const unitTypes = ["studio", "one_bed", "two_bed"];

const els = {
  sampleSelect: document.getElementById("sampleSelect"),
  setupForm: document.getElementById("setupForm"),
  setupSubtitle: document.getElementById("setupSubtitle"),
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
  els.inputEditor.addEventListener("input", saveDraft);
  els.formatBtn.addEventListener("click", formatInput);
  els.downloadInputBtn.addEventListener("click", () => downloadText("floor-plan-input.json", els.inputEditor.value));
  els.saveSvgBtn.addEventListener("click", saveSvg);
  els.exportSummary.addEventListener("click", handleExportAction);
  els.exportCardGrid.addEventListener("click", handleExportAction);
  els.modeButtons.forEach((button) => button.addEventListener("click", () => setViewMode(button.dataset.viewMode)));
  els.canvasButtons.forEach((button) => button.addEventListener("click", () => handleCanvasAction(button.dataset.canvasAction)));
  els.topNavLinks.forEach((link) => link.addEventListener("click", () => updateActiveNav(link.getAttribute("href"))));
  window.addEventListener("hashchange", () => updateActiveNav());
  els.planSvg.addEventListener("click", handlePlanClick);
  els.planSvg.addEventListener("pointerdown", handlePlanPointerDown);
  window.addEventListener("pointermove", handlePlanPointerMove);
  window.addEventListener("pointerup", finishPlanPointerEdit);
  window.addEventListener("pointercancel", finishPlanPointerEdit);
  els.selectionInspector.addEventListener("click", handleInspectorAction);
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
  state.inputDirty = false;
  clearAutoGenerate();
  if (!options.preserveResponse) {
    state.response = null;
    state.selectedVariantId = "";
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
  if (!state.inputDirty) {
    state.runSerial += 1;
  }
  state.inputDirty = true;
  setStatus(message || "Plan edits pending");
  if (Number.isFinite(Number(autoGenerateDelay))) {
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
  clearAutoGenerate();
  try {
    syncInputFromForm();
    setEditorFromInput(state.input);
    saveDraft();
  } catch (error) {
    renderError("setup_invalid", error.message);
    return;
  }

  const variants = state.input.generationSettings.variantCount;
  const seed = state.input.project.seed;
  const request = {
    input: state.input,
    validateOnly,
    variants,
    seed
  };
  const runId = ++state.runSerial;

  setBusy(true, validateOnly ? "Checking" : "Generating");
  try {
    const response = await fetchJson("/api/generate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
    if (runId !== state.runSerial) {
      return;
    }
    state.response = response;
    state.selectedVariantId = response.bestVariantId || firstVariantId(response.output);
    state.inputDirty = false;
    setStatus(`${friendlyStatus(response.status)} - ${response.validVariantCount}/${response.variantCount} valid variants`);
    renderAll();
  } catch (error) {
    if (runId === state.runSerial) {
      renderError("request_failed", error.message);
      setStatus("Request failed");
    }
  } finally {
    if (runId === state.runSerial) {
      setBusy(false);
    }
  }
}

function renderAll() {
  const output = state.response ? state.response.output : null;
  syncSelection(output);
  renderVariantSelect(output);
  renderPreview(output);
  renderMetrics(output);
  renderVariants(output);
  renderDiagnostics(output);
  renderExportSummary(output);
  renderHypergraphPreview(output);
  renderSchedule(output);
  renderValidation(output);
  renderSelectionInspector(output);
  renderSubtitles(output);
  updateDirtyState();
  updateCanvasUi(output);

  const exportReady = Boolean(output && !state.inputDirty);
  els.outputJson.textContent = output ? JSON.stringify(output, null, 2) : "";
  els.cliCommand.textContent = buildCliCommand();
  els.copyOutputBtn.disabled = !exportReady;
  els.downloadOutputBtn.disabled = !exportReady;
  document.querySelectorAll('[data-export-action="copy-rhino"], [data-export-action="copy-ifc"]').forEach((button) => {
    button.disabled = !exportReady;
  });
  els.saveSvgBtn.disabled = state.inputDirty || !els.planSvg.childElementCount;
}

function updateDirtyState() {
  const stalePreview = Boolean(state.inputDirty && state.response);
  els.previewFrame.classList.toggle("is-stale", stalePreview);
  els.previewFrame.classList.toggle("is-dragging", Boolean(state.dragEdit));
  els.previewFrame.classList.toggle("is-edit-mode", Boolean(state.editMode && state.viewMode !== "axon"));
  els.planSvg.classList.toggle("stale-preview", stalePreview);

  if (!state.inputDirty) {
    return;
  }

  if (state.response) {
    els.planSubtitle.textContent = `${els.planSubtitle.textContent} - regenerating from edits`;
    els.resultSubtitle.textContent = "Edits pending - last generated plan stays visible until the engine refreshes it";
  } else {
    els.resultSubtitle.textContent = "Edits pending - generate to produce variants";
  }
}

function renderSubtitles(output) {
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

  const variant = selectedVariant(output);
  const valid = output.variants ? output.variants.filter((v) => v.validation && v.validation.passed).length : 0;
  const issueCount = collectDiagnostics(output).length;
  els.resultSubtitle.textContent = `${friendlyStatus(output.status)} - ${valid}/${output.variants ? output.variants.length : 0} valid${issueCount ? `, ${issueCount} issue${issueCount === 1 ? "" : "s"}` : ""}`;
  if (!variant) {
    els.planSubtitle.textContent = output.status === "validated" ? "Input passed validation. Generate variants when ready." : "No generated variant";
    els.scheduleSubtitle.textContent = targetMix ? `Target mix: ${targetMix}` : "No generated units";
    return;
  }

  const metrics = variant.metrics || {};
  const mix = unitMixSummary(variant.units || []);
  els.planSubtitle.textContent = `${variant.variantId} - score ${formatNumber(metrics.score, 3)} - net/gross ${formatNumber(metrics.netGrossRatio, 3)}`;
  els.scheduleSubtitle.textContent = `${variant.units.length} units${mix ? ` (${mix})` : ""}, ${variant.rooms.length} rooms, ${variant.doorsOpenings.length} doors`;
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
    els.emptyPreview.style.display = "grid";
    els.emptyPreview.textContent = "No plan available";
    renderLegend(false);
    return;
  }

  els.emptyPreview.style.display = variant ? "none" : "grid";
  els.emptyPreview.textContent = "Input outline";
  els.planSvg.dataset.viewMode = state.viewMode;
  const viewBox = previewViewBox(bounds, state.zoom);
  els.planSvg.setAttribute("viewBox", viewBox);
  els.planSvg.setAttribute("preserveAspectRatio", "xMidYMid meet");

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

  if (variant) {
    (variant.units || []).forEach((unit) => group.appendChild(polygonEl(unit.polygon.points, selectableClass(`unit unit-${unit.type || "standard"}`, "unit", unit.id), selectableAttributes("unit", unit.id))));
    (variant.rooms || []).forEach((room) => group.appendChild(polygonEl(room.polygon.points, selectableClass("room", "room", room.id), selectableAttributes("room", room.id))));
    (variant.corridors || []).forEach((corridor) => group.appendChild(polygonEl(corridor.polygon.points, selectableClass("corridor", "corridor", corridor.id), selectableAttributes("corridor", corridor.id))));
    (variant.walls || []).forEach((wall) => {
      if (wall.centerline && wall.centerline.start && wall.centerline.end) {
        group.appendChild(lineEl(wall.centerline.start, wall.centerline.end, selectableClass(`wall wall-${wall.layerType || "partition"}`, "wall", wall.id), selectableAttributes("wall", wall.id)));
      }
    });
    (variant.doorsOpenings || []).forEach((door) => {
      group.appendChild(svgEl("circle", {
        class: selectableClass("door", "door", door.id),
        cx: door.location.x,
        cy: door.location.y,
        r: Math.max(bounds.width, bounds.height) * 0.006,
        ...selectableAttributes("door", door.id)
      }));
    });

    const unitById = new Map((variant.units || []).map((unit) => [unit.id, unit]));
    const labels = variant.labels || [];
    labels
      .filter((label) => label.targetId && unitById.has(label.targetId))
      .forEach((label) => {
        const unit = unitById.get(label.targetId);
        const text = svgEl("text", {
          class: "svg-label",
          x: label.location.x,
          y: -label.location.y,
          "text-anchor": "middle"
        });
        text.textContent = shortUnitType(unit.type);
        els.planSvg.appendChild(text);
      });
  }

  renderInputEditHandles(group, input);
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
    state.editReadout = state.editMode ? editSummary(state.input) : "";
    renderAll();
    setStatus(state.editMode ? "Edit constraints mode" : "Plan review mode");
    return;
  }

  if (!els.planSvg.getAttribute("viewBox")) {
    setStatus("Generate a plan before using canvas tools");
    return;
  }

  if (action === "zoom-in") {
    state.zoom = clamp(state.zoom * 1.25, 1, 4);
  } else if (action === "zoom-out") {
    state.zoom = clamp(state.zoom / 1.25, 1, 4);
  } else if (action === "fit") {
    state.zoom = 1;
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
      source: "input"
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
        source: "input"
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
        source: "generated"
      };
    }
  }

  if (selection.kind === "room") {
    const room = (variant.rooms || []).find((item) => String(item.id || "") === selection.id);
    if (room) {
      const points = room.polygon ? room.polygon.points || [] : [];
      return {
        kind: "room",
        id: room.id,
        title: `${displayUnitType(room.type)} ${room.id}`,
        item: room,
        points,
        bounds: boundsOfPoints(points),
        area: Number(room.area) || polygonArea(points),
        source: "generated"
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
        source: "generated"
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
        source: "generated"
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
        source: "generated"
      };
    }
  }

  return null;
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

function inspectorMarkup(detail) {
  const rows = [];
  const actions = [];
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
    rows.push(["Score", formatNumber(detail.item.score, 3)]);
    actions.push(["unit-more", "More like this"], ["unit-less", "Fewer like this"], ["unit-fit-area", "Fit target area"]);
  } else if (detail.kind === "floorplate") {
    actions.push(["floor-wider", "Wider"], ["floor-narrower", "Narrower"], ["floor-deeper", "Deeper"], ["floor-shallower", "Shallower"]);
  } else if (detail.kind === "core") {
    rows.push(["Blocks units", detail.item.blocksGeneration === false ? "No" : "Yes"]);
    actions.push(["core-left", "Left"], ["core-right", "Right"], ["core-up", "Up"], ["core-down", "Down"], ["core-grow", "Grow"], ["core-shrink", "Shrink"]);
  } else if (detail.kind === "room") {
    rows.push(["Type", displayUnitType(detail.item.type)]);
    rows.push(["Unit", detail.item.unitId || "-"]);
    rows.push(["Daylight", detail.item.hasDaylight === false ? "No" : "Yes"]);
    actions.push(["room-use-dimensions", "Use as room minimum"]);
  } else if (detail.kind === "corridor") {
    rows.push(["Width", `${formatNumber(corridorWidth(detail), 2)} m`]);
    actions.push(["corridor-use-width", "Use as corridor width"]);
  } else if (detail.kind === "wall") {
    rows.push(["Layer", displayUnitType(detail.item.layerType || "partition")]);
    rows.push(["Length", `${formatNumber(detail.length, 1)} m`]);
  } else if (detail.kind === "door") {
    rows.push(["Kind", displayUnitType(detail.item.type || "door")]);
    rows.push(["Width", `${formatNumber(detail.item.width || 0.9, 2)} m`]);
    if (detail.item.location) {
      rows.push(["Location", `${formatNumber(detail.item.location.x, 1)}, ${formatNumber(detail.item.location.y, 1)}`]);
    }
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
      ${actions.length ? `
        <div class="inspector-actions">
          ${actions.map(([action, label]) => `<button type="button" data-inspector-action="${escapeHtml(action)}">${escapeHtml(label)}</button>`).join("")}
        </div>
      ` : ""}
      <button class="inspector-clear" type="button" data-inspector-action="clear-selection">Clear selection</button>
    </div>
  `;
}

function handleInspectorAction(event) {
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

  const detail = selectedElementDetails(state.response ? state.response.output : null);
  if (!detail) {
    setStatus("Select a plan element first");
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
  const unit = detail.item || {};
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
  state.dragEdit = {
    action: handle.dataset.editAction,
    startPoint: point,
    startInput: clone(state.input)
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
    const width = edit.action === "floor-depth"
      ? floorBounds.width
      : clamp(round(point.x - floorBounds.minX), 8, 300);
    const depth = edit.action === "floor-width"
      ? floorBounds.height
      : clamp(round(point.y - floorBounds.minY), 8, 300);
    resizeFloorplateInput(input, edit.startInput, width, depth);
    clampCoreIntoFloorplate(input);
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
    return input;
  }

  if (edit.action === "core-size") {
    const width = clamp(round(point.x - startCoreBounds.minX), 1, currentFloorBounds.maxX - startCoreBounds.minX);
    const depth = clamp(round(point.y - startCoreBounds.minY), 1, currentFloorBounds.maxY - startCoreBounds.minY);
    core.polygon.points = rectPoints(startCoreBounds.minX, startCoreBounds.minY, width, depth);
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
    ? `Core ${formatNumber(coreBounds.width, 1)} x ${formatNumber(coreBounds.height, 1)} m at ${formatNumber(coreBounds.minX, 1)}, ${formatNumber(coreBounds.minY, 1)}`
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

  const handleRadius = Math.max(floorBounds.width, floorBounds.height) * 0.012;
  const floorGroup = svgEl("g", { class: "edit-handles" });
  floorGroup.appendChild(editHandle("floor-width", floorBounds.maxX, floorBounds.minY + floorBounds.height / 2, handleRadius, "Resize floorplate width"));
  floorGroup.appendChild(editHandle("floor-depth", floorBounds.minX + floorBounds.width / 2, floorBounds.maxY, handleRadius, "Resize floorplate depth"));
  floorGroup.appendChild(editHandle("floor-size", floorBounds.maxX, floorBounds.maxY, handleRadius * 1.1, "Resize floorplate"));

  const core = firstCore(input);
  const coreBounds = core && core.polygon ? boundsOfPoints(core.polygon.points) : null;
  if (coreBounds) {
    floorGroup.appendChild(editHandle("core-move", coreBounds.minX + coreBounds.width / 2, coreBounds.minY + coreBounds.height / 2, handleRadius * 1.2, "Move core"));
    floorGroup.appendChild(editHandle("core-size", coreBounds.maxX, coreBounds.maxY, handleRadius, "Resize core"));
  }

  group.appendChild(floorGroup);
}

function editHandle(action, x, y, radius, label) {
  const handle = svgEl("circle", {
    class: `edit-handle edit-${action}`,
    "data-edit-action": action,
    cx: round(x),
    cy: round(y),
    r: Math.max(radius, 0.18)
  });
  const title = svgEl("title");
  title.textContent = label;
  handle.appendChild(title);
  return handle;
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
  const rows = [
    ["Status", output ? friendlyStatus(output.status) : "Ready"],
    ["Units", variant ? String(variant.units.length) : "-"],
    ["Sellable", metrics ? `${formatNumber(metrics.sellableArea, 1)} m2` : "-"],
    ["Circulation", metrics ? `${formatNumber(metrics.corridorArea, 1)} m2` : "-"],
    ["Net/Gross", metrics ? formatNumber(metrics.netGrossRatio, 3) : floorplate ? formatNumber(floorplate.usableArea / Math.max(1, floorplate.grossArea), 3) : "-"],
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
    const checkText = checks.length === 0
      ? "No checks"
      : failedChecks.length === 0
        ? `${checks.length} checks passed`
        : `${errors ? `${errors} fail${errors === 1 ? "" : "s"}` : ""}${errors && warnings.length ? ", " : ""}${warnings.length ? `${warnings.length} warning${warnings.length === 1 ? "" : "s"}` : ""}`;
    const item = document.createElement("button");
    item.type = "button";
    item.className = `variant-item${variant.variantId === state.selectedVariantId ? " active" : ""}`;
    item.setAttribute("aria-pressed", variant.variantId === state.selectedVariantId ? "true" : "false");
    item.innerHTML = `
      <div class="variant-title">
        <span>#${index + 1} ${escapeHtml(variant.variantId)}</span>
        <span class="pill ${escapeHtml(variant.status)}">${escapeHtml(friendlyStatus(variant.status))}</span>
      </div>
      <div class="variant-card-body" style="display:flex;gap:10px;align-items:center;margin-top:8px;">
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
      <div class="score-bar" aria-label="Variant score ${scoreWidth}%"><i style="width:${scoreWidth}%"></i></div>
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
  const diagnostics = collectDiagnostics(output);
  const warningCount = diagnostics.filter((item) => String(item.severity || "").toLowerCase() !== "error").length;
  const errorCount = diagnostics.length - warningCount;
  els.issueCountLabel.textContent = diagnostics.length === 0
    ? "0"
    : errorCount
      ? `${errorCount} fail${errorCount === 1 ? "" : "s"}`
      : `${warningCount} warning${warningCount === 1 ? "" : "s"}`;
  if (diagnostics.length === 0) {
    els.diagnosticList.innerHTML = `<div class="empty-list good">No blocking issues found. This variant is ready to review or export.</div>`;
    return;
  }

  els.diagnosticList.innerHTML = diagnostics.slice(0, 10).map((diagnostic) => `
    <div class="diagnostic-item ${escapeHtml(diagnostic.severity || "info")}">
      <div class="diagnostic-title">
        <span>${escapeHtml(humanizeCode(diagnostic.code || diagnostic.name || "diagnostic"))}</span>
        <span class="pill ${escapeHtml(diagnostic.severity || "info")}">${escapeHtml(friendlySeverity(diagnostic.severity))}</span>
      </div>
      <div class="diagnostic-message">${escapeHtml(friendlyDiagnosticMessage(diagnostic))}</div>
      ${diagnostic.sourceId ? `<div class="diagnostic-source">${escapeHtml(diagnostic.sourceId)}</div>` : ""}
    </div>
  `).join("") + (diagnostics.length > 10 ? `<div class="empty-list">${diagnostics.length - 10} more issue${diagnostics.length - 10 === 1 ? "" : "s"} in Output JSON</div>` : "");
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
  const summary = `
    <div class="check-item ${errors.length ? "failed" : warnings.length ? "warning" : "passed"}">
      <span>${errors.length ? "Fail" : warnings.length ? "Warn" : "Pass"}</span>
      <strong>${passed}/${checks.length} checks passed</strong>
      <em>${errors.length ? "Resolve blocking checks before export." : warnings.length ? "Warnings are review items; the generated plan is still inspectable." : "Validation is clear for the selected variant."}</em>
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
  const validation = summarizeValidation(variant);
  const schema = output.metadata ? output.metadata.schemaVersion : "-";
  const layers = output.metadata && output.metadata.layers ? Object.keys(output.metadata.layers).length : 0;

  els.exportSummary.innerHTML = `
    <div>
      <span>Selected Variant</span>
      <strong>${escapeHtml(variant ? variant.variantId : "-")} - ${escapeHtml(validation.label)}</strong>
    </div>
    <div>
      <span>Schema and Layers</span>
      <strong>${escapeHtml(schema)}${layers ? ` - ${layers} layer keys` : ""}</strong>
    </div>
    <div>
      <span>Rhino / Grasshopper</span>
      <strong>${escapeHtml(variant ? `${countOf(variant.units)} units, ${countOf(variant.rooms)} rooms, ${countOf(variant.walls)} walls` : "Generate a variant first")}</strong>
      <button type="button" data-export-action="copy-cli">Copy CLI</button>
    </div>
    <div>
      <span>IFC / BIM Ready</span>
      <strong>${escapeHtml(variant ? `${countExternalIds(variant)} stable external ids` : "No element ids yet")}</strong>
      <button type="button" data-export-action="copy-api">Copy API</button>
    </div>
    <div>
      <span>SVG / Report</span>
      <strong>${els.planSvg.childElementCount ? "Preview SVG can be saved" : "No preview to save"}</strong>
      <button type="button" data-export-action="save-svg">Save SVG</button>
    </div>
    <div>
      <span>AI Agent Contract</span>
      <strong>${escapeHtml(hypergraph ? "Output JSON includes topology.hypergraph" : "Validation-only output")}</strong>
      <button type="button" data-export-action="copy-json">Copy JSON</button>
    </div>
    <div>
      <span>Hypergraph Summary</span>
      <strong>${escapeHtml(hypergraphSummary.headline)}</strong>
      <div class="variant-meta">${escapeHtml(hypergraphSummary.detail)}</div>
    </div>
    <div>
      <span>Hypergraph Preview</span>
      <strong>${escapeHtml(hypergraphSummary.preview)}</strong>
      <div class="variant-meta">${escapeHtml(hypergraphSummary.matrices)}</div>
    </div>
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
  const edgeKinds = Object.entries(countBy(hypergraph.hyperedges || [], (edge) => edge.kind || "edge"))
    .sort((a, b) => b[1] - a[1])
    .slice(0, 6);

  els.hypergraphPreview.innerHTML = `
    <div class="hypergraph-card">
      <span>DataNode tree</span>
      <strong>${escapeHtml(summary.preview)}</strong>
      <ul class="node-tree">
        ${dataNodeRows(hypergraph.root).map((row) => `
          <li style="--depth:${row.depth}">
            <b>${escapeHtml(row.name)}</b>
            <em>${row.final ? "final" : "branch"}${row.area ? ` - ${formatNumber(row.area, 1)} m2` : ""}</em>
          </li>
        `).join("")}
      </ul>
    </div>
    <div class="hypergraph-card">
      <span>Hyperedges</span>
      <strong>${escapeHtml(summary.detail)}</strong>
      <div class="edge-cloud">
        ${edgeKinds.map(([kind, count]) => `<i>${escapeHtml(humanizeCode(kind))}<b>${count}</b></i>`).join("")}
      </div>
    </div>
    <div class="hypergraph-card">
      <span>Incidence matrix</span>
      <strong>${escapeHtml(summary.matrices)}</strong>
      ${incidencePreview(hypergraph)}
    </div>
  `;
}

function handleExportAction(event) {
  const button = event.target.closest("[data-export-action]");
  if (!button) {
    return;
  }

  const action = button.getAttribute("data-export-action");
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

function dataNodeRows(node, depth = 0, rows = []) {
  if (!node || rows.length >= 12) {
    return rows;
  }

  rows.push({
    depth,
    name: node.name || "node",
    final: Boolean(node.final),
    area: node.area || (node.treeNodeMesh && node.treeNodeMesh.Area) || 0
  });
  (node.children || []).forEach((child) => dataNodeRows(child, depth + 1, rows));
  return rows;
}

function incidencePreview(hypergraph) {
  const matrices = hypergraph && hypergraph.matrices ? hypergraph.matrices : {};
  const rows = Array.isArray(matrices.incidence) ? matrices.incidence.slice(0, 6) : [];
  const edgeOrder = Array.isArray(matrices.hyperedgeOrder) ? matrices.hyperedgeOrder.slice(0, 5) : [];
  if (rows.length === 0 || edgeOrder.length === 0) {
    return `<div class="empty-list">Matrix preview unavailable</div>`;
  }

  return `
    <table class="matrix-preview">
      <thead>
        <tr>
          <th>Node</th>
          ${edgeOrder.map((_, index) => `<th>E${index + 1}</th>`).join("")}
        </tr>
      </thead>
      <tbody>
        ${rows.map((row, rowIndex) => `
          <tr>
            <td>V${rowIndex + 1}</td>
            ${row.slice(0, edgeOrder.length).map((value) => `<td>${Number(value) ? "1" : "-"}</td>`).join("")}
          </tr>
        `).join("")}
      </tbody>
    </table>
  `;
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
  state.selectedVariantId = "";
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
    let message = text;
    try {
      const parsed = JSON.parse(text);
      message = parsed.message || parsed.error || text;
    } catch (_) {
      message = text || response.statusText;
    }
    throw new Error(message);
  }
  return text ? JSON.parse(text) : null;
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

async function copyText(text, successMessage) {
  try {
    await navigator.clipboard.writeText(text);
    setStatus(successMessage || "Copied");
  } catch (_) {
    const scratch = document.createElement("textarea");
    scratch.value = text;
    scratch.setAttribute("readonly", "readonly");
    scratch.style.position = "fixed";
    scratch.style.left = "-1000px";
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
  [els.generateBtn, els.validateBtn, els.loadSampleBtn].forEach((button) => {
    button.disabled = busy;
  });
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
  const allowed = new Set(["#setup", "#plan", "#schedule", "#exports"]);
  const hash = allowed.has(nextHash) ? nextHash : allowed.has(window.location.hash) ? window.location.hash : "#plan";
  els.topNavLinks.forEach((link) => {
    link.classList.toggle("active", link.getAttribute("href") === hash);
  });
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
    cli: buildCliCommand(),
    note: "Use this payload with a Rhino/Grasshopper adapter to map polygons, walls, doors, labels, layers, and external ids."
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

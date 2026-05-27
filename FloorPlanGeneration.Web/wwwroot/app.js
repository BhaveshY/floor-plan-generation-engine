const state = {
  samples: [],
  input: null,
  response: null,
  selectedVariantId: "",
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
  emptyPreview: document.getElementById("emptyPreview"),
  legendRow: document.getElementById("legendRow"),
  metricsRow: document.getElementById("metricsRow"),
  variantList: document.getElementById("variantList"),
  diagnosticList: document.getElementById("diagnosticList"),
  validationList: document.getElementById("validationList"),
  unitSchedule: document.getElementById("unitSchedule"),
  outputJson: document.getElementById("outputJson"),
  cliCommand: document.getElementById("cliCommand"),
  exportSummary: document.getElementById("exportSummary"),
  hypergraphPreview: document.getElementById("hypergraphPreview"),
  copyOutputBtn: document.getElementById("copyOutputBtn"),
  downloadOutputBtn: document.getElementById("downloadOutputBtn"),
  variantCountLabel: document.getElementById("variantCountLabel"),
  issueCountLabel: document.getElementById("issueCountLabel"),
  unitCountLabel: document.getElementById("unitCountLabel"),
  checkCountLabel: document.getElementById("checkCountLabel")
};

init();

async function init() {
  bindEvents();
  await loadSamples();
  if (!restoreDraft()) {
    await loadSelectedSample(false);
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
  els.copyOutputBtn.addEventListener("click", copyOutput);
  els.downloadOutputBtn.addEventListener("click", () => downloadText("floor-plan-output.json", els.outputJson.textContent || "{}"));
  els.exportSummary.addEventListener("click", handleExportAction);
  els.variantSelect.addEventListener("change", () => {
    state.selectedVariantId = els.variantSelect.value;
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

function setInput(input) {
  state.input = ensureInputShape(clone(input));
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
  state.response = null;
  state.selectedVariantId = "";
  saveDraft();
  renderAll();
  setStatus("Ready");
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
    setInput(parsed);
    saveDraft();
    setStatus("Applied input JSON");
  } catch (error) {
    setStatus("Input JSON is invalid");
    renderError("invalid_json", error.message);
  }
}

async function runEngine(validateOnly) {
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

  setBusy(true, validateOnly ? "Checking" : "Generating");
  try {
    const response = await fetchJson("/api/generate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
    state.response = response;
    state.selectedVariantId = response.bestVariantId || firstVariantId(response.output);
    setStatus(`${friendlyStatus(response.status)} - ${response.validVariantCount}/${response.variantCount} valid variants`);
    renderAll();
  } catch (error) {
    renderError("request_failed", error.message);
    setStatus("Request failed");
  } finally {
    setBusy(false);
  }
}

function renderAll() {
  const output = state.response ? state.response.output : null;
  renderVariantSelect(output);
  renderPreview(output);
  renderMetrics(output);
  renderVariants(output);
  renderDiagnostics(output);
  renderExportSummary(output);
  renderHypergraphPreview(output);
  renderSchedule(output);
  renderValidation(output);
  renderSubtitles(output);

  els.outputJson.textContent = output ? JSON.stringify(output, null, 2) : "";
  els.cliCommand.textContent = buildCliCommand();
  els.copyOutputBtn.disabled = !output;
  els.downloadOutputBtn.disabled = !output;
  els.saveSvgBtn.disabled = !els.planSvg.childElementCount;
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
  const variant = selectedVariant(output);
  const input = state.input;
  const metadata = output ? output.metadata : null;
  const bounds = metadata && metadata.floorplate ? metadata.floorplate.bounds : collectBounds(output, variant, input);

  if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
    els.emptyPreview.style.display = "grid";
    els.emptyPreview.textContent = "No plan available";
    renderLegend(false);
    return;
  }

  els.emptyPreview.style.display = variant ? "none" : "grid";
  els.emptyPreview.textContent = "Input outline";
  const pad = Math.max(bounds.width, bounds.height) * 0.06;
  const viewBox = [bounds.minX - pad, -bounds.maxY - pad, bounds.width + pad * 2, bounds.height + pad * 2].join(" ");
  els.planSvg.setAttribute("viewBox", viewBox);
  els.planSvg.setAttribute("preserveAspectRatio", "xMidYMid meet");

  const group = svgEl("g", { transform: "scale(1,-1)" });
  els.planSvg.appendChild(group);

  if (input && input.floorplate && input.floorplate.outer) {
    group.appendChild(polygonEl(input.floorplate.outer.points, "boundary"));
  }
  if (input && Array.isArray(input.fixedElements)) {
    input.fixedElements.forEach((fixed) => {
      if (fixed.polygon && fixed.polygon.points) {
        group.appendChild(polygonEl(fixed.polygon.points, "fixed"));
      }
    });
  }

  if (variant) {
    (variant.units || []).forEach((unit) => group.appendChild(polygonEl(unit.polygon.points, `unit unit-${unit.type || "standard"}`)));
    (variant.rooms || []).forEach((room) => group.appendChild(polygonEl(room.polygon.points, "room")));
    (variant.corridors || []).forEach((corridor) => group.appendChild(polygonEl(corridor.polygon.points, "corridor")));
    (variant.walls || []).forEach((wall) => {
      if (wall.centerline && wall.centerline.start && wall.centerline.end) {
        group.appendChild(lineEl(wall.centerline.start, wall.centerline.end, `wall wall-${wall.layerType || "partition"}`));
      }
    });
    (variant.doorsOpenings || []).forEach((door) => {
      group.appendChild(svgEl("circle", {
        class: "door",
        cx: door.location.x,
        cy: door.location.y,
        r: Math.max(bounds.width, bounds.height) * 0.006
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

  renderLegend(Boolean(variant));
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
  const summary = summarizeHypergraph(hypergraph);
  els.hypergraphPreview.innerHTML = `
    <div class="diagnostic-item">
      <div class="diagnostic-title">
        <span>${escapeHtml(summary.headline)}</span>
        <span class="pill info">Hypergraph</span>
      </div>
      <div class="diagnostic-message">${escapeHtml(summary.preview)}</div>
      <div class="diagnostic-source">${escapeHtml(summary.matrices)}</div>
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

function variantThumbnailSvg(variant, input) {
  const bounds = collectBounds(null, variant, input);
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
    return `<div class="empty-list">No preview</div>`;
  }

  const pad = Math.max(bounds.width, bounds.height) * 0.04;
  const viewBox = [bounds.minX - pad, -bounds.maxY - pad, bounds.width + pad * 2, bounds.height + pad * 2]
    .map((value) => formatNumber(value, 3))
    .join(" ");
  const boundary = input && input.floorplate && input.floorplate.outer
    ? thumbnailPolygon(input.floorplate.outer.points, "#f9fbfb", "#222831")
    : "";
  const fixed = input && Array.isArray(input.fixedElements)
    ? input.fixedElements.map((item) => item.polygon ? thumbnailPolygon(item.polygon.points, "#334155", "#111820") : "").join("")
    : "";
  const units = (variant.units || [])
    .map((unit) => thumbnailPolygon(unit.polygon ? unit.polygon.points : [], thumbnailUnitFill(unit.type), "#3f7ea6"))
    .join("");
  const corridors = (variant.corridors || [])
    .map((corridor) => thumbnailPolygon(corridor.polygon ? corridor.polygon.points : [], "#f9d889", "#b7791f"))
    .join("");

  return `
    <svg viewBox="${escapeHtml(viewBox)}" preserveAspectRatio="xMidYMid meet" aria-hidden="true" style="width:96px;height:64px;border:1px solid #d8dee5;border-radius:6px;background:#f8fafb;flex:0 0 auto;">
      <g transform="scale(1,-1)">${boundary}${units}${corridors}${fixed}</g>
    </svg>
  `;
}

function thumbnailPolygon(points, fill, stroke) {
  if (!Array.isArray(points) || points.length < 3) {
    return "";
  }

  const pointText = points
    .filter((point) => Number.isFinite(Number(point.x)) && Number.isFinite(Number(point.y)))
    .map((point) => `${formatNumber(point.x, 3)},${formatNumber(point.y, 3)}`)
    .join(" ");
  return `<polygon points="${escapeHtml(pointText)}" fill="${escapeHtml(fill)}" stroke="${escapeHtml(stroke)}" stroke-width="0.18"></polygon>`;
}

function thumbnailUnitFill(type) {
  switch (String(type || "").toLowerCase()) {
    case "one_bed":
      return "#dff0df";
    case "two_bed":
      return "#eadff6";
    default:
      return "#d9ebf7";
  }
}

function polygonEl(points, className) {
  return svgEl("polygon", {
    class: className,
    points: (points || []).map((p) => `${p.x},${p.y}`).join(" ")
  });
}

function lineEl(start, end, className) {
  return svgEl("line", {
    class: className,
    x1: start.x,
    y1: start.y,
    x2: end.x,
    y2: end.y
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
    setInput(parsed);
    saveDraft();
    setStatus("Formatted input");
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

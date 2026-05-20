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
    setStatus(`${response.status} - ${response.validVariantCount}/${response.variantCount} valid`);
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
  els.setupSubtitle.textContent = state.input && state.input.project ? state.input.project.id : "project";

  if (!output) {
    els.resultSubtitle.textContent = projectName;
    els.planSubtitle.textContent = "Input outline";
    els.scheduleSubtitle.textContent = "Generate to populate schedule";
    return;
  }

  const variant = selectedVariant(output);
  const valid = output.variants ? output.variants.filter((v) => v.validation && v.validation.passed).length : 0;
  els.resultSubtitle.textContent = `${output.status} - ${valid}/${output.variants ? output.variants.length : 0} valid`;
  if (!variant) {
    els.planSubtitle.textContent = output.status === "validated" ? "Input passed validation" : "No generated variant";
    els.scheduleSubtitle.textContent = "No generated units";
    return;
  }

  els.planSubtitle.textContent = `${variant.variantId} - ${variant.units.length} units - score ${formatNumber(variant.metrics.score, 3)}`;
  els.scheduleSubtitle.textContent = `${variant.rooms.length} rooms, ${variant.doorsOpenings.length} doors, ${variant.walls.length} walls`;
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
    .map((variant) => `<option value="${escapeHtml(variant.variantId)}">${escapeHtml(variant.variantId)}</option>`)
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
  const rows = [
    ["Status", output ? output.status : "ready"],
    ["Units", variant ? String(variant.units.length) : "-"],
    ["Sellable", metrics ? `${formatNumber(metrics.sellableArea, 1)} m2` : "-"],
    ["Corridor", metrics ? `${formatNumber(metrics.corridorArea, 1)} m2` : "-"],
    ["Net/Gross", metrics ? formatNumber(metrics.netGrossRatio, 3) : floorplate ? formatNumber(floorplate.usableArea / Math.max(1, floorplate.grossArea), 3) : "-"],
    ["Score", metrics ? formatNumber(metrics.score, 3) : "-"]
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
    els.variantList.innerHTML = `<div class="empty-list">No variants generated</div>`;
    return;
  }

  els.variantList.innerHTML = "";
  variants.forEach((variant, index) => {
    const item = document.createElement("button");
    item.type = "button";
    item.className = `variant-item${variant.variantId === state.selectedVariantId ? " active" : ""}`;
    item.innerHTML = `
      <div class="variant-title">
        <span>#${index + 1} ${escapeHtml(variant.variantId)}</span>
        <span class="pill ${escapeHtml(variant.status)}">${escapeHtml(variant.status)}</span>
      </div>
      <div class="variant-meta">
        ${variant.units ? variant.units.length : 0} units, ${variant.rooms ? variant.rooms.length : 0} rooms, net/gross ${formatNumber(variant.metrics ? variant.metrics.netGrossRatio : 0, 3)}
      </div>
      <div class="score-bar"><i style="width:${Math.round(clamp(variant.metrics ? variant.metrics.score : 0, 0, 1) * 100)}%"></i></div>
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
  els.issueCountLabel.textContent = String(diagnostics.filter((item) => item.severity !== "info").length);
  if (diagnostics.length === 0) {
    els.diagnosticList.innerHTML = `<div class="empty-list good">No issues found</div>`;
    return;
  }

  els.diagnosticList.innerHTML = diagnostics.map((diagnostic) => `
    <div class="diagnostic-item ${escapeHtml(diagnostic.severity || "info")}">
      <div class="diagnostic-title">
        <span>${escapeHtml(humanizeCode(diagnostic.code || diagnostic.name || "diagnostic"))}</span>
        <span class="pill ${escapeHtml(diagnostic.severity || "info")}">${escapeHtml(diagnostic.severity || "info")}</span>
      </div>
      <div class="diagnostic-message">${escapeHtml(diagnostic.message || diagnostic.reason || "")}</div>
      ${diagnostic.sourceId ? `<div class="diagnostic-source">${escapeHtml(diagnostic.sourceId)}</div>` : ""}
    </div>
  `).join("");
}

function renderSchedule(output) {
  const variant = selectedVariant(output);
  const units = variant && Array.isArray(variant.units) ? variant.units : [];
  els.unitCountLabel.textContent = String(units.length);
  if (units.length === 0) {
    els.unitSchedule.innerHTML = `<div class="empty-list">No generated units</div>`;
    return;
  }

  els.unitSchedule.innerHTML = `
    <table>
      <thead>
        <tr>
          <th>Unit</th>
          <th>Type</th>
          <th>Area</th>
          <th>Rooms</th>
          <th>Score</th>
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
    els.validationList.innerHTML = `<div class="empty-list">No checks to show</div>`;
    return;
  }

  if (notPassed.length === 0) {
    els.validationList.innerHTML = `<div class="empty-list good">${checks.length} validation checks passed</div>`;
    return;
  }

  els.validationList.innerHTML = notPassed.slice(0, 12).map((check) => {
    const severity = String(check.severity || "warning").toLowerCase();
    const label = severity === "error" ? "Fail" : "Warn";
    return `
    <div class="check-item ${severity === "error" ? "failed" : "warning"}">
      <span>${label}</span>
      <strong>${escapeHtml(humanizeCode(check.name))}</strong>
      ${check.reason ? `<em>${escapeHtml(check.reason)}</em>` : ""}
    </div>
  `;
  }).join("") + (notPassed.length > 12 ? `<div class="empty-list">${notPassed.length - 12} more checks in Output JSON</div>` : "");
}

function renderExportSummary(output) {
  if (!output) {
    els.exportSummary.innerHTML = `
      <div><span>Schema</span><strong>-</strong></div>
      <div><span>Hypergraph</span><strong>-</strong></div>
      <div><span>Agent Ready</span><strong>CLI and API available</strong></div>
    `;
    return;
  }

  const variant = selectedVariant(output);
  const hypergraph = variant && variant.topology ? variant.topology.hypergraph : null;
  const hypergraphText = hypergraph
    ? `${hypergraph.nodes ? hypergraph.nodes.length : 0} nodes, ${hypergraph.hyperedges ? hypergraph.hyperedges.length : 0} edges, ${hypergraph.incidence ? hypergraph.incidence.length : 0} incidence`
    : "validated input only";

  els.exportSummary.innerHTML = `
    <div><span>Schema</span><strong>${escapeHtml(output.metadata ? output.metadata.schemaVersion : "-")}</strong></div>
    <div><span>Hypergraph</span><strong>${escapeHtml(hypergraphText)}</strong></div>
    <div><span>Best Variant</span><strong>${escapeHtml(variant ? variant.variantId : "-")}</strong></div>
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

function polygonEl(points, className) {
  return svgEl("polygon", {
    class: className,
    points: (points || []).map((p) => `${p.x},${p.y}`).join(" ")
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
  try {
    await navigator.clipboard.writeText(text);
    setStatus("Output copied");
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
    setStatus("Output copied");
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

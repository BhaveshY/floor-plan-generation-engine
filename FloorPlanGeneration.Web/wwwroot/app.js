const state = {
  samples: [],
  input: null,
  response: null,
  selectedVariantId: ""
};

const draftKey = "floor-engine-web-draft-v1";

const els = {
  sampleSelect: document.getElementById("sampleSelect"),
  variantInput: document.getElementById("variantInput"),
  seedInput: document.getElementById("seedInput"),
  inputEditor: document.getElementById("inputEditor"),
  runStatus: document.getElementById("runStatus"),
  loadSampleBtn: document.getElementById("loadSampleBtn"),
  validateBtn: document.getElementById("validateBtn"),
  generateBtn: document.getElementById("generateBtn"),
  openInputBtn: document.getElementById("openInputBtn"),
  inputFile: document.getElementById("inputFile"),
  formatBtn: document.getElementById("formatBtn"),
  downloadInputBtn: document.getElementById("downloadInputBtn"),
  variantSelect: document.getElementById("variantSelect"),
  saveSvgBtn: document.getElementById("saveSvgBtn"),
  planSvg: document.getElementById("planSvg"),
  emptyPreview: document.getElementById("emptyPreview"),
  metricStatus: document.getElementById("metricStatus"),
  metricValid: document.getElementById("metricValid"),
  metricScore: document.getElementById("metricScore"),
  metricNetGross: document.getElementById("metricNetGross"),
  variantList: document.getElementById("variantList"),
  diagnosticList: document.getElementById("diagnosticList"),
  outputJson: document.getElementById("outputJson"),
  copyOutputBtn: document.getElementById("copyOutputBtn"),
  downloadOutputBtn: document.getElementById("downloadOutputBtn")
};

init();

async function init() {
  bindEvents();
  await loadSamples();
  if (!restoreDraft()) {
    await loadSelectedSample();
  }
  await runEngine(false);
}

function bindEvents() {
  els.loadSampleBtn.addEventListener("click", loadSelectedSample);
  els.validateBtn.addEventListener("click", () => runEngine(true));
  els.generateBtn.addEventListener("click", () => runEngine(false));
  els.openInputBtn.addEventListener("click", () => els.inputFile.click());
  els.inputFile.addEventListener("change", openInputFile);
  els.inputEditor.addEventListener("input", saveDraft);
  els.variantInput.addEventListener("input", saveDraft);
  els.seedInput.addEventListener("input", saveDraft);
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

  document.querySelectorAll(".tab").forEach((tab) => {
    tab.addEventListener("click", () => activateTab(tab.dataset.tab));
  });
}

async function loadSamples() {
  const samples = await fetchJson("/api/samples");
  state.samples = samples;
  els.sampleSelect.innerHTML = samples
    .map((sample) => `<option value="${escapeHtml(sample.name)}">${escapeHtml(sample.name)}</option>`)
    .join("");
  if (samples.some((sample) => sample.name === "rectangular-core")) {
    els.sampleSelect.value = "rectangular-core";
  }
}

async function loadSelectedSample() {
  const name = els.sampleSelect.value || "rectangular-core";
  const sample = await fetchJson(`/api/samples/${encodeURIComponent(name)}`);
  state.input = sample;
  els.inputEditor.value = JSON.stringify(sample, null, 2);
  if (sample.project && Number.isInteger(sample.project.seed)) {
    els.seedInput.value = "";
    els.seedInput.placeholder = String(sample.project.seed);
  }
  if (sample.generationSettings && Number.isInteger(sample.generationSettings.variantCount)) {
    els.variantInput.value = sample.generationSettings.variantCount;
  }
  saveDraft();
  setStatus(`Loaded ${name}`);
}

function restoreDraft() {
  try {
    const draft = JSON.parse(localStorage.getItem(draftKey) || "null");
    if (!draft || !draft.inputJson) {
      return false;
    }

    els.inputEditor.value = draft.inputJson;
    els.variantInput.value = draft.variants || els.variantInput.value;
    els.seedInput.value = draft.seed || "";
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
      variants: els.variantInput.value,
      seed: els.seedInput.value,
      sampleName: els.sampleSelect.value,
      savedAt: new Date().toISOString()
    }));
  } catch (_) {
    // Draft persistence is a convenience; generation should keep working without it.
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
    els.inputEditor.value = JSON.stringify(parsed, null, 2);
    state.input = parsed;
    saveDraft();
    setStatus(`Opened ${file.name}`);
    await runEngine(false);
  } catch (error) {
    setStatus("Input file could not be opened");
    renderError("input_file_invalid", error.message);
  }
}

async function runEngine(validateOnly) {
  let input;
  try {
    input = JSON.parse(els.inputEditor.value);
  } catch (error) {
    setStatus("Input JSON is invalid");
    renderError("invalid_json", error.message);
    return;
  }

  const variants = parseInteger(els.variantInput.value);
  if (variants === null && String(els.variantInput.value).trim() !== "") {
    setStatus("Variants must be a number");
    renderError("invalid_variants", "Variants must be a whole number between 1 and 20.");
    return;
  }

  if (variants !== null && (variants < 1 || variants > 20)) {
    setStatus("Variants must be 1-20");
    renderError("invalid_variants", "Variants must be between 1 and 20.");
    return;
  }

  const seed = parseInteger(els.seedInput.value);
  if (seed === null && String(els.seedInput.value).trim() !== "") {
    setStatus("Seed must be a number");
    renderError("invalid_seed", "Seed must be a whole number.");
    return;
  }

  const request = {
    input,
    validateOnly,
    variants,
    seed
  };

  saveDraft();
  setBusy(true, validateOnly ? "Validating" : "Generating");
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
  els.outputJson.textContent = output ? JSON.stringify(output, null, 2) : "";
  els.copyOutputBtn.disabled = !output;
  els.downloadOutputBtn.disabled = !output;
  els.saveSvgBtn.disabled = !output || !selectedVariant(output);
}

function renderVariantSelect(output) {
  const variants = output && Array.isArray(output.variants) ? output.variants : [];
  els.variantSelect.innerHTML = variants
    .map((variant) => `<option value="${escapeHtml(variant.variantId)}">${escapeHtml(variant.variantId)}</option>`)
    .join("");
  els.variantSelect.value = state.selectedVariantId || firstVariantId(output);
}

function renderPreview(output) {
  clearSvg();
  const variant = selectedVariant(output);
  const metadata = output ? output.metadata : null;
  const bounds = metadata && metadata.floorplate ? metadata.floorplate.bounds : collectBounds(output, variant);

  if (!variant || !bounds || bounds.width <= 0 || bounds.height <= 0) {
    els.emptyPreview.style.display = "grid";
    return;
  }

  els.emptyPreview.style.display = "none";
  const pad = Math.max(bounds.width, bounds.height) * 0.06;
  const viewBox = [bounds.minX - pad, -bounds.maxY - pad, bounds.width + pad * 2, bounds.height + pad * 2].join(" ");
  els.planSvg.setAttribute("viewBox", viewBox);
  els.planSvg.setAttribute("preserveAspectRatio", "xMidYMid meet");

  const group = svgEl("g", { transform: "scale(1,-1)" });
  els.planSvg.appendChild(group);

  const input = parseEditorOrNull();
  const floorplate = input && input.floorplate ? input.floorplate : null;
  if (floorplate && floorplate.outer) {
    group.appendChild(polygonEl(floorplate.outer.points, "boundary"));
  }
  if (input && Array.isArray(input.fixedElements)) {
    input.fixedElements.forEach((fixed) => {
      if (fixed.polygon && fixed.polygon.points) {
        group.appendChild(polygonEl(fixed.polygon.points, "fixed"));
      }
    });
  }

  (variant.corridors || []).forEach((corridor) => group.appendChild(polygonEl(corridor.polygon.points, "corridor")));
  (variant.units || []).forEach((unit) => group.appendChild(polygonEl(unit.polygon.points, "unit")));
  (variant.rooms || []).forEach((room) => group.appendChild(polygonEl(room.polygon.points, "room")));
  (variant.doorsOpenings || []).forEach((door) => {
    group.appendChild(svgEl("circle", {
      class: "door",
      cx: door.location.x,
      cy: door.location.y,
      r: Math.max(bounds.width, bounds.height) * 0.006
    }));
  });

  const labels = variant.labels || [];
  labels
    .filter((label) => label.targetId && label.targetId.indexOf("unit-") === 0)
    .forEach((label) => {
      const text = svgEl("text", {
        class: "label",
        x: label.location.x,
        y: -label.location.y,
        transform: "scale(1,-1)",
        "text-anchor": "middle"
      });
      text.textContent = labelText(label.text);
      els.planSvg.appendChild(text);
    });
}

function renderMetrics(output) {
  const variant = selectedVariant(output);
  els.metricStatus.textContent = output ? output.status : "-";
  els.metricValid.textContent = output && output.variants ? `${output.variants.filter((v) => v.validation && v.validation.passed).length}/${output.variants.length}` : "-";
  els.metricScore.textContent = variant && variant.metrics ? formatNumber(variant.metrics.score, 3) : "-";
  els.metricNetGross.textContent = variant && variant.metrics ? formatNumber(variant.metrics.netGrossRatio, 3) : "-";
}

function renderVariants(output) {
  const variants = output && Array.isArray(output.variants) ? output.variants : [];
  if (variants.length === 0) {
    els.variantList.innerHTML = `<div class="variant-item"><div class="variant-title">No variants</div></div>`;
    return;
  }

  els.variantList.innerHTML = "";
  variants.forEach((variant) => {
    const item = document.createElement("button");
    item.type = "button";
    item.className = `variant-item${variant.variantId === state.selectedVariantId ? " active" : ""}`;
    item.innerHTML = `
      <div class="variant-title">
        <span>${escapeHtml(variant.variantId)}</span>
        <span class="pill ${escapeHtml(variant.status)}">${escapeHtml(variant.status)}</span>
      </div>
      <div class="variant-meta">
        ${variant.units ? variant.units.length : 0} units - ${variant.rooms ? variant.rooms.length : 0} rooms - score ${formatNumber(variant.metrics ? variant.metrics.score : 0, 3)}
      </div>
    `;
    item.addEventListener("click", () => {
      state.selectedVariantId = variant.variantId;
      renderAll();
    });
    els.variantList.appendChild(item);
  });
}

function renderDiagnostics(output) {
  const top = output && Array.isArray(output.diagnostics) ? output.diagnostics : [];
  const variant = selectedVariant(output);
  const variantDiagnostics = variant && Array.isArray(variant.diagnostics) ? variant.diagnostics : [];
  const diagnostics = [...top, ...variantDiagnostics];
  if (diagnostics.length === 0) {
    els.diagnosticList.innerHTML = `<div class="diagnostic-item"><div class="diagnostic-title">No diagnostics</div></div>`;
    return;
  }

  els.diagnosticList.innerHTML = diagnostics.map((diagnostic) => `
    <div class="diagnostic-item">
      <div class="diagnostic-title">
        <span>${escapeHtml(diagnostic.code || "diagnostic")}</span>
        <span class="pill ${escapeHtml(diagnostic.severity || "info")}">${escapeHtml(diagnostic.severity || "info")}</span>
      </div>
      <div class="diagnostic-message">${escapeHtml(diagnostic.message || "")}</div>
      ${diagnostic.sourceId ? `<div class="diagnostic-message">${escapeHtml(diagnostic.sourceId)}</div>` : ""}
    </div>
  `).join("");
}

function renderError(code, message) {
  const output = {
    status: "failed",
    variants: [],
    diagnostics: [{ severity: "error", code, message, sourceId: "web" }]
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

function collectBounds(output, variant) {
  const points = [];
  const input = parseEditorOrNull();
  if (input && input.floorplate && input.floorplate.outer) {
    points.push(...input.floorplate.outer.points);
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
  if (points.length === 0) {
    return null;
  }
  const xs = points.map((p) => p.x);
  const ys = points.map((p) => p.y);
  const minX = Math.min(...xs);
  const maxX = Math.max(...xs);
  const minY = Math.min(...ys);
  const maxY = Math.max(...ys);
  return { minX, minY, maxX, maxY, width: maxX - minX, height: maxY - minY };
}

function parseEditorOrNull() {
  try {
    return JSON.parse(els.inputEditor.value);
  } catch (_) {
    return null;
  }
}

function formatInput() {
  try {
    const parsed = JSON.parse(els.inputEditor.value);
    els.inputEditor.value = JSON.stringify(parsed, null, 2);
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

  const clone = els.planSvg.cloneNode(true);
  clone.setAttribute("xmlns", "http://www.w3.org/2000/svg");
  clone.insertBefore(svgStyleElement(), clone.firstChild);
  const xml = new XMLSerializer().serializeToString(clone);
  downloadText("floor-plan-preview.svg", xml, "image/svg+xml");
  setStatus("SVG saved");
}

function svgStyleElement() {
  const style = document.createElementNS("http://www.w3.org/2000/svg", "style");
  style.textContent = `
    .boundary{fill:#fbfbf8;stroke:#1f2428;stroke-width:0.18}
    .fixed{fill:#39424d;stroke:#111820;stroke-width:0.12}
    .corridor{fill:#ffe2a8;stroke:#b98221;stroke-width:0.12}
    .unit{fill:#dceee8;stroke:#4e9487;stroke-width:0.12}
    .room{fill:rgba(255,255,255,0.38);stroke:rgba(31,36,40,0.35);stroke-width:0.06}
    .label{fill:#1f2428;font-size:1.35px;font-weight:700}
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

function activateTab(name) {
  document.querySelectorAll(".tab").forEach((tab) => tab.classList.toggle("active", tab.dataset.tab === name));
  document.querySelectorAll(".tab-panel").forEach((panel) => panel.classList.remove("active"));
  document.getElementById(`${name}Tab`).classList.add("active");
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

function parseInteger(value) {
  if (value === null || value === undefined || String(value).trim() === "") {
    return null;
  }
  const text = String(value).trim();
  if (!/^-?\d+$/.test(text)) {
    return null;
  }
  const parsed = Number.parseInt(text, 10);
  return Number.isFinite(parsed) ? parsed : null;
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

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

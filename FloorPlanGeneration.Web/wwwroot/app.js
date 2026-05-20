const state = {
  samples: [],
  input: null,
  response: null,
  selectedVariantId: ""
};

const els = {
  sampleSelect: document.getElementById("sampleSelect"),
  variantInput: document.getElementById("variantInput"),
  seedInput: document.getElementById("seedInput"),
  inputEditor: document.getElementById("inputEditor"),
  runStatus: document.getElementById("runStatus"),
  loadSampleBtn: document.getElementById("loadSampleBtn"),
  validateBtn: document.getElementById("validateBtn"),
  generateBtn: document.getElementById("generateBtn"),
  formatBtn: document.getElementById("formatBtn"),
  downloadInputBtn: document.getElementById("downloadInputBtn"),
  variantSelect: document.getElementById("variantSelect"),
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
  await loadSelectedSample();
  await runEngine(false);
}

function bindEvents() {
  els.loadSampleBtn.addEventListener("click", loadSelectedSample);
  els.validateBtn.addEventListener("click", () => runEngine(true));
  els.generateBtn.addEventListener("click", () => runEngine(false));
  els.formatBtn.addEventListener("click", formatInput);
  els.downloadInputBtn.addEventListener("click", () => downloadText("floor-plan-input.json", els.inputEditor.value));
  els.copyOutputBtn.addEventListener("click", copyOutput);
  els.downloadOutputBtn.addEventListener("click", () => downloadText("floor-plan-output.json", els.outputJson.textContent || "{}"));
  els.variantSelect.addEventListener("change", () => {
    state.selectedVariantId = els.variantSelect.value;
    renderAll();
  });

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
  setStatus(`Loaded ${name}`);
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

  const request = {
    input,
    validateOnly,
    variants: parseInteger(els.variantInput.value),
    seed: parseInteger(els.seedInput.value)
  };

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
  const parsed = JSON.parse(els.inputEditor.value);
  els.inputEditor.value = JSON.stringify(parsed, null, 2);
  setStatus("Formatted input");
}

function copyOutput() {
  navigator.clipboard.writeText(els.outputJson.textContent || "{}");
  setStatus("Output copied");
}

function downloadText(fileName, text) {
  const blob = new Blob([text], { type: "application/json" });
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
  const parsed = Number.parseInt(value, 10);
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

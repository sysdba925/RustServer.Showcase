"use strict";
const updater = "RustServer.Updater";
const order = ["RustServer.Auth", "RustServer.Rcon", "RustServer.Control", "RustServer.Versions", "RustServer.Plugins", "RustServer.Snapshots", "RustServer.PluginCatalog", "RustServer.Web"];
const terminal = new Set(["Succeeded", "Failed", "Interrupted"]);
const $ = id => document.getElementById(id);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
let applications = [], records = [], selected = updater, manualSelection = false, busy = false, rolloutFailed = false, current = null, unknownId = null, selfOutcome = "";
const alerts = new Map(), versions = new Map();

async function request(path, options = {}) {
  const response = await fetch(path, { credentials: "same-origin", cache: "no-store", ...options });
  const text = await response.text(); let body;
  try { body = text ? JSON.parse(text) : null; } catch { body = null; }
  if (!response.ok) throw new Error(body?.detail || body?.title || text || `HTTP ${response.status}`);
  return body;
}
const post = (path, body) => request(path, { method: "POST", headers: body ? { "Content-Type": "application/json" } : {}, body: body ? JSON.stringify(body) : undefined });
const app = name => applications.find(a => a.name === name);
const pathFor = name => encodeURIComponent(name);
function report(o) {
  const d = o.deployment || {};
  const duration = o.startedAt && o.completedAt ? `${Math.max(0, (Date.parse(o.completedAt) - Date.parse(o.startedAt)) / 1000)} s` : "Unavailable";
  return [`Operation: ${o.id}`, `Application: ${o.applicationName}`, `Action: ${o.type}`, `Status: ${o.status}`, `Last step: ${o.step || "Unavailable"}`, `Previous version: ${d.previousVersion || o.previousVersion || "Unavailable"}`, `Target version: ${d.targetVersion || o.targetVersion || "Unavailable"}`, `Active version: ${d.activeVersion || "Unavailable"}`, `Rolled back: ${d.rolledBack ?? "Unavailable"}`, `Created: ${o.createdAt || "Unavailable"}`, `Started: ${o.startedAt || "Unavailable"}`, `Completed: ${o.completedAt || "Unavailable"}`, `Duration: ${duration}`, `Exit code: ${d.provisioningExitCode ?? "Unavailable"}`, `Error: ${o.error || "None"}`, `Provisioning output:\n${d.provisioningOutput ?? "Unavailable"}`].join("\n");
}
async function copy(text) {
  if (navigator.clipboard && window.isSecureContext) return navigator.clipboard.writeText(text);
  const input = document.createElement("textarea"); input.value = text; document.body.append(input); input.select();
  try { if (!document.execCommand("copy")) throw new Error("Copy unavailable"); } finally { input.remove(); }
}
function alertFor(name, message, id = null) { alerts.set(name, { message, id }); selected = name; render(); }
function render() {
  const list = $("services"); list.replaceChildren();
  [updater, ...order].forEach((name, index) => {
    const a = app(name), row = document.createElement("button"); row.className = `service${selected === name ? " selected" : ""}${alerts.has(name) ? " failed" : ""}`;
    const number = document.createElement("span"), content = document.createElement("span"), title = document.createElement("span"), version = document.createElement("span"), icon = document.createElement("span");
    number.className = "num"; number.textContent = index || "◆"; title.className = "service-name"; title.textContent = name.replace("RustServer.", ""); version.className = "service-version"; version.textContent = a ? `${a.installedVersion || "Not installed"} · ${a.status}` : "Not registered"; icon.className = "statusicon"; icon.textContent = alerts.has(name) ? "!" : a?.installedVersion ? "●" : "○"; content.append(title, version); row.append(number, content, icon);
    row.onclick = () => { selected = name; manualSelection = true; render(); }; list.append(row);
    if (!index) { const divider = document.createElement("div"); divider.className = "side-divider"; list.append(divider); }
  });
  const done = order.filter(name => app(name)?.installedVersion).length;
  $("count").textContent = `${done} / 8 installed`; $("progress").style.width = `${done / 8 * 100}%`;
  $("rollout").hidden = done === 8; $("rollout").disabled = busy; $("rollout").textContent = rolloutFailed ? "Retry rollout" : done ? "Resume rollout" : "Rollout";
  $("rollout-title").textContent = done === 8 ? "Ready" : rolloutFailed ? "Rollout stopped" : "Initial rollout";
  const a = app(selected), alert = alerts.get(selected), op = current?.applicationName === selected ? current : records.find(r => r.applicationName === selected);
  $("name").textContent = selected; $("runtime").textContent = a ? `Runtime: ${a.status}` : "Application is not registered";
  $("installed").textContent = a?.installedVersion || "Not installed"; $("latest").textContent = a?.availableVersions?.[0] || "Unavailable";
  $("outcome").textContent = op ? `Last operation: ${op.status}` : "No operation";
  const select = $("version"), choices = a?.availableVersions || []; select.replaceChildren();
  choices.forEach(v => { const option = document.createElement("option"); option.value = v; option.textContent = v; select.append(option); });
  if (choices.includes(versions.get(selected))) select.value = versions.get(selected);
  select.disabled = busy || !choices.length; const chosen = select.value;
  const conflicting = (selected === updater && !!unknownId) || records.some(r => r.applicationName === selected && !terminal.has(r.status));
  $("install").hidden = !!a?.installedVersion; $("install").disabled = busy || conflicting || !a || !chosen;
  $("update").hidden = !a?.installedVersion; $("update").disabled = busy || conflicting || !a || !chosen || chosen === a.installedVersion;
  $("rollback").disabled = busy || conflicting || !a?.installedVersion;
  $("release-warning").textContent = a?.availableVersionsError || (a?.installedVersion && chosen && choices.indexOf(chosen) > choices.indexOf(a.installedVersion) && choices.includes(a.installedVersion) ? "Selected release is older than the installed release." : "");
  $("error").hidden = !alert; $("error-text").textContent = alert?.message || ""; $("error-details").hidden = !alert?.id;
  $("retry-connection").hidden = !unknownId || selected !== updater;
  renderStages(op);
  if (selected === updater && selfOutcome) $("step").textContent = selfOutcome;
}
function renderStages(op) {
  const labels = ["Resolving", "Downloading", "Verifying", "Staging", "Activation & readiness"];
  const step = (op?.step || "").toLowerCase(); let index = step.includes("resolv") ? 0 : step.includes("download") ? 1 : step.includes("verif") ? 2 : /extract|stag/.test(step) ? 3 : /activat|deploy|execut|readiness|restart/.test(step) ? 4 : -1;
  $("stages").replaceChildren(...labels.map((label, i) => {
    const el = document.createElement("div"), marker = document.createElement("div"), text = document.createElement("span");
    // Only infer earlier stages when the latest durable step is recognizable.
    const completed = op?.status === "Succeeded" && (op.type !== "Prepare" || i < 4);
    const state = completed || (index >= 0 && i < index) ? "done" : index === i && op?.status !== "Succeeded" ? (["Failed", "Interrupted"].includes(op?.status) ? "error" : "active") : "";
    el.className = `stage${state ? ` ${state}` : ""}`;
    el.setAttribute("aria-label", `${label}: ${{ done: "Done", active: "Running", error: "Failed" }[state] || "Pending"}`);
    marker.className = "stage-marker"; text.textContent = label; el.append(marker, text); return el;
  }));
  $("op-status").textContent = op?.status || "Idle"; $("step").textContent = op?.step || "No active operation"; $("op-id").textContent = op?.id || "";
}
async function loadState() { const state = await request("/api/emergency"); applications = state.applications; $("channel").textContent = state.channel; $("connection").textContent = "Connected"; render(); }
async function loadRecords() { records = await request("/api/operations?limit=50"); render(); }
async function poll(id, self = false) {
  let disconnectedAt = null;
  while (true) {
    try {
      const controller = new AbortController(), timer = setTimeout(() => controller.abort(), 5000); let op;
      try { op = await request(`/api/operations/${encodeURIComponent(id)}`, { signal: controller.signal }); } finally { clearTimeout(timer); }
      disconnectedAt = null; current = op; $("connection").textContent = "Connected"; render();
      if (terminal.has(op.status)) return op;
    } catch (error) {
      if (!self) throw error;
      disconnectedAt ??= Date.now(); $("connection").textContent = "Restarting / Reconnecting"; $("step").textContent = "Restarting / Reconnecting";
      if (Date.now() - disconnectedAt >= 90000) { unknownId = id; throw new Error("Result unknown. Retry connection to check the existing operation."); }
    }
    await sleep(self ? 2000 : 1000);
  }
}
async function operation(name, action, body, self = false) {
  const prefix = action === "update" ? "/api/emergency/applications" : "/api/applications";
  const accepted = await post(`${prefix}/${pathFor(name)}/${action}`, body);
  if (self) unknownId = accepted.operationId;
  let op;
  try { op = await poll(accepted.operationId, self); }
  catch (error) { error.operationId = accepted.operationId; throw error; }
  if (op.status !== "Succeeded") { const error = new Error(op.error || `${op.type} ${op.status}: ${op.step}`); error.operation = op; throw error; }
  return op;
}
async function refreshState() { await post("/api/applications/refresh"); await loadState(); await loadRecords(); }
async function runAction(action) {
  if (busy) return; const name = selected, version = $("version").value; busy = true; current = null; selfOutcome = ""; render();
  try {
    if (action === "install") { await operation(name, "prepare", { version }); await operation(name, "install", { version }); }
    else await operation(name, action, action === "update" ? { version } : undefined, name === updater);
    if (name === updater && action !== "install") {
      await loadState(); await loadRecords();
      selfOutcome = describeSelfOutcome(current);
      unknownId = null;
    } else await refreshState();
  } catch (error) {
    if (name === updater && error.operation) {
      try { await loadState(); selfOutcome = describeSelfOutcome(error.operation); unknownId = null; }
      catch { selfOutcome = "Result unknown: retry connection to verify the active release."; }
    }
    alertFor(name, error.message, error.operation?.id || error.operationId || unknownId);
  }
  finally { try { await loadRecords(); } catch {} busy = false; render(); }
}
function describeSelfOutcome(op) {
  const d = op?.deployment, installed = app(updater)?.installedVersion, target = d?.targetVersion || op?.targetVersion;
  if (d?.rolledBack && d.activeVersion && d.activeVersion === installed) return `Previous release restored: ${installed}`;
  if (op?.status === "Succeeded" && target && installed === target && (!d?.activeVersion || d.activeVersion === installed)) return `Active release: ${installed}`;
  if (/rollback failed/i.test(op?.step || "")) return `Rollback failed. Active version: ${installed || "unknown"}`;
  if (op?.status === "Failed" || op?.status === "Interrupted") return `Operation ${op.status.toLowerCase()}; rollback outcome not confirmed. Active version: ${installed || "unknown"}`;
  return "Result unknown: active version could not be verified.";
}
async function rollout() {
  if (busy) return; busy = true; rolloutFailed = false; manualSelection = false; let name = selected; render();
  try {
    $("rollout-status").textContent = "Checking installed state, operations and releases…";
    await refreshState();
    const active = records.filter(r => order.includes(r.applicationName) && !terminal.has(r.status));
    if (active.length) throw new Error("An existing rollout service operation is still active. Wait for it to finish before retrying.");
    const targets = order.map(n => app(n));
    const missingRegistrations = order.filter((n, i) => !targets[i]);
    if (missingRegistrations.length) throw new Error(`Applications not registered: ${missingRegistrations.join(", ")}`);
    const missing = targets.filter(a => !a.installedVersion);
    const unavailable = missing.filter(a => !a.availableVersions?.includes(versions.get(a.name) || a.availableVersions[0]));
    if (unavailable.length) throw new Error(`Selected release unavailable: ${unavailable.map(a => a.name).join(", ")}`);
    for (const a of missing) {
      name = a.name; if (!manualSelection) selected = name; const version = versions.get(name) || a.availableVersions[0];
      $("rollout-status").textContent = `${name} · Preparing ${version}`; render(); await operation(name, "prepare", { version });
      $("rollout-status").textContent = `${name} · Installing ${version}`; await operation(name, "install", { version });
      await loadState(); if (!app(name)?.installedVersion) throw new Error(`Install succeeded but ${name} is not confirmed installed.`);
    }
    $("rollout-status").textContent = "All rollout services installed.";
  } catch (error) { rolloutFailed = true; $("rollout-status").textContent = "Rollout stopped."; alertFor(name, error.message, error.operation?.id || error.operationId); }
  finally { busy = false; try { await loadRecords(); } catch {} render(); }
}
async function showHistory() {
  $("drawer").classList.add("open"); $("veil").classList.add("visible"); $("drawer").setAttribute("aria-hidden", "false"); $("details").hidden = true; $("back").hidden = true; $("history-list").hidden = false;
  try { await loadRecords(); $("history-list").replaceChildren(); records.forEach(o => { const row = document.createElement("div"), label = document.createElement("strong"), button = document.createElement("button"); row.className = "history-row"; label.textContent = `${o.applicationName} · ${o.type} · ${o.status}`; button.className = "btn sm"; button.textContent = "Details"; button.onclick = () => showDetails(o.id); row.append(label, button); $("history-list").append(row); }); if (!records.length) $("history-list").textContent = "No recent operations."; } catch (e) { $("history-list").textContent = e.message; }
}
async function showDetails(id) { try { const o = await request(`/api/operations/${encodeURIComponent(id)}`); $("drawer").classList.add("open"); $("veil").classList.add("visible"); $("drawer").setAttribute("aria-hidden", "false"); $("history-list").hidden = true; $("back").hidden = false; $("details").hidden = false; $("report").textContent = report(o); } catch (e) { alertFor(selected, e.message, id); } }
$("version").onchange = () => { versions.set(selected, $("version").value); render(); };
for (const action of ["install", "update", "rollback"]) $(action).onclick = () => runAction(action);
$("rollout").onclick = rollout;
$("refresh").onclick = async () => { if (busy) return; busy = true; render(); try { await refreshState(); } catch (e) { alertFor(selected, e.message); } finally { busy = false; render(); } };
$("dismiss").onclick = () => { alerts.delete(selected); render(); };
$("copy-error").onclick = async () => { try { await copy(alerts.get(selected).message); alerts.delete(selected); render(); } catch { $("copy-status").textContent = "Copy unavailable; select the text manually."; } };
$("error-details").onclick = () => showDetails(alerts.get(selected).id);
$("history").onclick = showHistory; $("back").onclick = showHistory;
$("close").onclick = $("veil").onclick = () => { $("drawer").classList.remove("open"); $("veil").classList.remove("visible"); $("drawer").setAttribute("aria-hidden", "true"); };
$("copy-report").onclick = async () => { try { await copy($("report").textContent); $("copy-status").textContent = "Copied."; } catch { $("copy-status").textContent = "Copy unavailable; select the report manually."; } };
$("retry-connection").onclick = async () => { if (!unknownId || busy) return; busy = true; render(); try { current = await poll(unknownId, true); await loadState(); await loadRecords(); selfOutcome = describeSelfOutcome(current); unknownId = null; if (current.status !== "Succeeded" || current.deployment?.rolledBack) alertFor(updater, current.error || selfOutcome, current.id); } catch (e) { alertFor(updater, e.message, unknownId); } finally { busy = false; render(); } };
(async () => { try { await loadState(); await loadRecords(); const active = records.find(o => o.applicationName === updater && ["SelfUpdate", "Rollback"].includes(o.type) && !terminal.has(o.status)); if (active) { unknownId = active.id; selected = updater; render(); } } catch (e) { $("connection").textContent = "Unavailable"; alertFor(selected, e.message); } })();

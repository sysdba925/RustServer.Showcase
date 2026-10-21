// Real Chromium, a local in-memory API, and the Chrome DevTools Protocol; no npm packages.
// Run: node tests/console-browser.cjs (set CHROME to override the browser path).
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');
const root = path.resolve('src/RustServer.Updater.Api/wwwroot/console');
const names = ['Auth', 'Rcon', 'Control', 'Versions', 'Plugins', 'Snapshots', 'PluginCatalog', 'Web'].map(n => `RustServer.${n}`);
const updater = 'RustServer.Updater';
let apps, operations, commands, requests, fail, outages;
function reset() {
  apps = [updater, ...names].map(name => ({ name, installedVersion: name === updater ? 'v1' : null, status: 'Running', availableVersions: ['v2', 'v1'] }));
  operations = []; commands = []; requests = []; fail = null; outages = 0;
}
reset();
const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost'), p = url.pathname;
  requests.push({ method: req.method, path: p, time: Date.now() });
  function json(value, status = 200) { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(value)); }
  if (p === '/api/emergency') return json({ channel: 'Development', applications: apps });
  if (p === '/api/applications/refresh') return json({});
  if (p === '/api/operations') return json([...operations].reverse());
  if (p.startsWith('/api/operations/')) {
    if (outages > 0) { outages--; res.destroy(); return; }
    return json(operations.find(o => o.id === p.split('/').pop()));
  }
  const match = p.match(/^\/api\/(?:emergency\/)?applications\/([^/]+)\/(prepare|install|update|rollback)$/);
  if (req.method === 'POST' && match) {
    const name = decodeURIComponent(match[1]), action = match[2]; commands.push(`${name}:${action}`);
    const failed = fail === `${name}:${action}`; if (failed) fail = null;
    const target = action === 'rollback' ? 'v1' : 'v2';
    const o = { id: `operation-${operations.length}`, applicationName: name, type: action === 'update' && name === updater ? 'SelfUpdate' : action[0].toUpperCase() + action.slice(1), status: failed ? 'Failed' : 'Succeeded', step: failed ? 'Downloading' : 'Deployment completed', targetVersion: target, error: failed ? '<error> full diagnostic\nsecond line' : null, deployment: { targetVersion: target, activeVersion: target, rolledBack: false, provisioningOutput: '<safe> output' } };
    operations.push(o);
    if (!failed && ['install', 'update', 'rollback'].includes(action)) apps.find(a => a.name === name).installedVersion = target;
    // Chromium may transparently retry a severed connection once.
    if (name === updater) outages = 4;
    return json({ operationId: o.id }, 202);
  }
  const relative = p === '/emergency' ? 'index.html' : p.startsWith('/console/') ? p.slice('/console/'.length) : '';
  const file = path.resolve(root, relative);
  if (!relative || !file.startsWith(root + path.sep) || !fs.existsSync(file)) { res.writeHead(404); return res.end(); }
  res.setHeader('Content-Type', file.endsWith('.js') ? 'text/javascript' : file.endsWith('.css') ? 'text/css' : file.endsWith('.html') ? 'text/html' : 'image/x-icon');
  fs.createReadStream(file).pipe(res);
});
const delay = ms => new Promise(r => setTimeout(r, ms));
async function main() {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'updater-browser-'));
  const chrome = spawn(process.env.CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless=new', '--no-first-run', '--disable-gpu', '--disable-extensions', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore' });
  try {
    const portFile = path.join(profile, 'DevToolsActivePort');
    for (let i = 0; i < 100 && !fs.existsSync(portFile); i++) await delay(100);
    const port = fs.readFileSync(portFile, 'utf8').split('\n')[0];
    const tabs = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
    const ws = new WebSocket(tabs.find(tab => tab.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
    let seq = 0; const pending = new Map();
    ws.onmessage = event => { const msg = JSON.parse(event.data); if (msg.id && pending.has(msg.id)) { const [resolve, reject] = pending.get(msg.id); pending.delete(msg.id); msg.error ? reject(new Error(msg.error.message)) : resolve(msg.result); } };
    const send = (method, params = {}) => new Promise((resolve, reject) => { const id = ++seq; pending.set(id, [resolve, reject]); ws.send(JSON.stringify({ id, method, params })); });
    const evalJs = async expression => { const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }); if (r.exceptionDetails) throw new Error(r.exceptionDetails.text); return r.result.value; };
    const wait = async expression => { for (let i = 0; i < 300; i++) { if (await evalJs(expression)) return; await delay(100); } throw new Error(`Timed out: ${expression}; page: ${await evalJs('location.href + " " + document.body.innerText.slice(0, 1000)')}`); };
    const page = async () => { await send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/emergency` }); await wait('document.querySelectorAll("#services .service").length === 9'); };
    const click = id => evalJs(`document.getElementById(${JSON.stringify(id)}).click()`);
    await page();
    assert.deepEqual(await evalJs('[...document.querySelectorAll("#services .service-name")].map(x => x.textContent)'), ['Updater', ...names.map(x => x.slice(11))]);
    await click('rollout'); await wait('document.querySelector("#count").textContent === "8 / 8 installed" && document.querySelector("#rollout-title").textContent === "Ready"');
    assert.deepEqual(commands, names.flatMap(n => [`${n}:prepare`, `${n}:install`]));
    await evalJs('renderStages({status:"Running", step:"Verifying"})');
    assert.equal(await evalJs('document.querySelectorAll("#stages .done").length'), 2);
    assert.equal(await evalJs('document.querySelectorAll("#stages .active").length'), 1);
    await evalJs('renderStages({status:"Failed", step:"Downloading"})');
    assert.equal(await evalJs('document.querySelectorAll("#stages .done").length'), 1);
    assert.equal(await evalJs('document.querySelectorAll("#stages .error").length'), 1);
    reset(); fail = `${names[0]}:prepare`; await page(); await click('rollout');
    await wait('document.querySelector("#rollout").textContent === "Retry rollout" && !document.querySelector("#error").hidden');
    assert.deepEqual(commands, [names[0] + ':prepare']);
    reset(); fail = `${names[1]}:install`; await page(); await click('rollout');
    await wait('document.querySelector("#rollout").textContent === "Retry rollout" && !document.querySelector("#error").hidden');
    assert.deepEqual(commands, [names[0] + ':prepare', names[0] + ':install', names[1] + ':prepare', names[1] + ':install']);
    assert.equal(await evalJs('document.querySelector("#error-text").textContent'), '<error> full diagnostic\nsecond line');
    assert.equal(await evalJs('document.querySelector("#error-text").children.length'), 0);
    await click('error-details'); await wait('!document.querySelector("#details").hidden');
    assert.match(await evalJs('document.querySelector("#report").textContent'), /<safe> output/);
    assert.equal(await evalJs('document.querySelector("#report").children.length'), 0);
    await evalJs('window.copied = null; Object.defineProperty(navigator, "clipboard", {value:{writeText: async text => { window.copied = text; }}, configurable:true})');
    await click('copy-report'); await wait('window.copied !== null');
    assert.match(await evalJs('window.copied'), /<safe> output/);
    await click('close');
    await evalJs('window.copied = null; Object.defineProperty(navigator, "clipboard", {value:{writeText: async text => { window.copied = text; }}, configurable:true})');
    await click('copy-error'); await wait('window.copied !== null');
    assert.equal(await evalJs('window.copied'), '<error> full diagnostic\nsecond line');
    assert.equal(operations[3].status, 'Failed');
    await click('rollout'); await wait('document.querySelector("#count").textContent === "8 / 8 installed"');
    assert.equal(commands.filter(x => x === names[0] + ':install').length, 1);
    assert.deepEqual(commands.slice(4), names.slice(1).flatMap(n => [`${n}:prepare`, `${n}:install`]));
    reset(); await page(); await click('update'); await wait('document.querySelector("#connection").textContent === "Restarting / Reconnecting"');
    await wait('document.querySelector("#step").textContent === "Active release: v2"');
    assert.deepEqual(commands, [updater + ':update']);
    const selfGets = requests.filter(x => x.path === '/api/operations/operation-0');
    assert.ok(selfGets.length >= 3);
    assert.ok(selfGets.at(-1).time - selfGets[0].time >= 1900);
    assert.ok(requests.slice(requests.findIndex(x => x.path.endsWith('/update')) + 1).every(x => x.method === 'GET'));
    await click('rollback'); await wait('document.querySelector("#connection").textContent === "Restarting / Reconnecting"');
    await wait('document.querySelector("#step").textContent === "Active release: v1"');
    assert.deepEqual(commands, [updater + ':update', updater + ':rollback']);
    assert.equal(await evalJs('document.querySelectorAll("#stages .done").length'), 5);
    reset(); await page();
    await evalJs('window.realNow = Date.now; let fakeNow = Date.now(); Date.now = () => (fakeNow += 100000)');
    await click('update'); await wait('!document.querySelector("#retry-connection").hidden && !document.querySelector("#error").hidden');
    assert.match(await evalJs('document.querySelector("#error-text").textContent'), /Result unknown/);
    await evalJs('Date.now = window.realNow');
    await click('retry-connection'); await wait('document.querySelector("#step").textContent === "Active release: v2"');
    assert.deepEqual(commands, [updater + ':update']);
    ws.close(); console.log('Browser scenarios passed: rollout, failure/retry, error copy, self-update and rollback reconnect.');
  } finally { chrome.kill(); server.close(); }
}
main().catch(e => { console.error(e); process.exitCode = 1; server.close(); });

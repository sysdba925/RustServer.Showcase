// Lightweight DOM smoke test for the shipped console script. Run with Node.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
class Element {
  constructor() { this.children = []; this.hidden = false; this.textContent = ''; this.style = {}; this.value = ''; this.classList = { add() {}, remove() {} }; }
  append(...nodes) { this.children.push(...nodes); if (nodes[0]?.value && !this.value) this.value = nodes[0].value; }
  replaceChildren(...nodes) { this.children = nodes; this.value = nodes[0]?.value || ''; }
  setAttribute() {}
  remove() {}
  select() {}
}
const elements = new Map();
const document = { getElementById(id) { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); }, createElement() { return new Element(); }, body: new Element() };
const names = ['Updater', 'Auth', 'Rcon', 'Control', 'Versions', 'Plugins', 'Snapshots', 'PluginCatalog', 'Web'];
const applications = names.map(n => ({ name: `RustServer.${n}`, installedVersion: n === 'Updater' ? 'v1' : null, status: 'Running', availableVersions: ['v2', 'v1'] }));
let copied = '';
const context = vm.createContext({ document, console, Date, Set, Map, AbortController, encodeURIComponent, setTimeout, clearTimeout, window: { isSecureContext: true }, navigator: { clipboard: { writeText: async text => { copied = text; } } }, fetch: async url => ({ ok: true, text: async () => JSON.stringify(url === '/api/emergency' ? { channel: 'Development', applications } : []) }) });
vm.runInContext(fs.readFileSync('src/RustServer.Updater.Api/wwwroot/console/assets/console.js', 'utf8'), context);
(async () => {
  await new Promise(resolve => setTimeout(resolve, 20));
  assert.equal(elements.get('services').children.filter(x => x.className?.startsWith('service')).length, 9);
  assert.equal(elements.get('count').textContent, '0 / 8 installed');
  elements.get('services').children[2].onclick();
  assert.equal(elements.get('name').textContent, 'RustServer.Auth');
  vm.runInContext("alertFor('RustServer.Auth', 'complete error text')", context);
  assert.equal(elements.get('error-text').textContent, 'complete error text');
  await elements.get('copy-error').onclick();
  assert.equal(copied, 'complete error text');
  assert.equal(elements.get('error').hidden, true);
  console.log('Console DOM smoke test passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });

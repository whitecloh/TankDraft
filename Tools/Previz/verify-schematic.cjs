'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const source = fs.readFileSync(path.join(__dirname, 'schematic.html'), 'utf8').match(/<script>([\s\S]*?)<\/script>/);
if (!source) throw new Error('Inline script not found in index.html');

class ElementMock {
  constructor(id = '') { this.id = id; this.value = id === 'lang' ? 'ru' : ''; this.checked = false; this.style = {}; this.options = []; this.files = []; this.offsetHeight = 58; this.textContent = ''; this.events = {}; this.validationMessage = ''; }
  addEventListener(type, handler) { (this.events[type] ||= []).push(handler); }
  dispatch(type) { for (const handler of this.events[type] || []) handler({ target: this }); }
  setCustomValidity(message) { this.validationMessage = message; }
  append(...items) { this.options.push(...items); }
  appendChild(item) { this.options.push(item); }
  replaceChildren(...items) { this.options = items; }
  remove() {}
  click() {}
  getBoundingClientRect() { return { left: 0, top: 0, width: this.width || 576, height: this.height || 1280 }; }
  getContext() { return context2d; }
  toBlob(callback) { callback(new Blob(['mock'])); }
}
const context2d = { fillRect() {}, strokeRect() {}, setLineDash() {}, fillText() {}, measureText(text) { return { width: String(text).length * 9 }; } };
const elements = {};
const documentMock = { body: new ElementMock('body'), getElementById(id) { return elements[id] || (elements[id] = new ElementMock(id)); }, querySelector() { return new ElementMock('header'); }, createElement() { return new ElementMock(); } };
const sandbox = { console, Blob, URL, crypto: require('crypto').webcrypto, document: documentMock, innerHeight: 900, addEventListener() {}, alert() {}, confirm() { return true; }, Option: class Option { constructor(text, value) { this.text = text; this.value = value; } } };
sandbox.window = sandbox;
vm.createContext(sandbox);
vm.runInContext(`${source[1]}\nglobalThis.__previz={preset,valid,screens,states,$,choose,rebuild,getLayout:()=>layout};`, sandbox, { filename: 'schematic.html' });
const api = sandbox.__previz;
const args = process.argv.slice(2);
let exportDir = null;
if (args.length) {
  if (args.length !== 2 || args[0] !== '--export-dir') throw new Error('Usage: node verify.cjs [--export-dir <path>]');
  exportDir = path.resolve(args[1]);
  fs.mkdirSync(exportDir, { recursive: true });
}
let checked = 0;
for (const screen of api.screens) for (const state of api.states) for (const viewport of ['576x1280', '1080x1920', '1080x2400']) for (const language of ['ru', 'en']) for (const long of [false, true]) {
  api.$('screen').value = screen; api.$('state').value = state; api.$('viewport').value = viewport; api.$('lang').value = language; api.$('long').checked = long;
  const layout = api.preset(); const error = api.valid(layout);
  if (error) throw new Error(`${screen}/${state}/${viewport}/${language}/${long}: ${error}`);
  checked++;
}
api.rebuild();
const selected = api.getLayout().elements[0];
api.choose(selected.id);
const labelInput = elements.fields.options[0].options[1];
labelInput.value = 'Live preview label';
labelInput.dispatch('input');
if (api.getLayout().elements.find((element) => element.id === selected.id).label !== 'Live preview label') throw new Error(`Label input handler did not update layout: ${labelInput.validationMessage || 'unknown error'}`);
api.$('screen').value = 'arena'; api.$('state').value = 'normal'; api.$('viewport').value = '576x1280'; api.$('lang').value = 'ru'; api.$('long').checked = false;
const sample = api.preset();
const expectReject = (value, name) => { if (!api.valid(value)) throw new Error(`Invalid case accepted: ${name}`); };
expectReject({ ...sample, theme: { ...sample.theme, accent: '#xyz123' } }, 'invalid color');
expectReject({ ...sample, viewport: { ...sample.viewport, width: 4097 } }, 'oversize viewport');
expectReject({ ...sample, elements: [sample.elements[0], { ...sample.elements[0] }] }, 'duplicate id');
expectReject({ ...sample, elements: sample.elements.map((element, index) => index ? element : { ...element, x: sample.viewport.width }) }, 'out of bounds');
if (exportDir) for (const screen of api.screens) { api.$('screen').value = screen; api.$('state').value = 'normal'; api.$('viewport').value = '576x1280'; api.$('lang').value = 'ru'; api.$('long').checked = false; fs.writeFileSync(path.join(exportDir, `${screen}.json`), JSON.stringify(api.preset(), null, 2)); }
console.log(`Preset validation OK: ${checked}; negative validation cases rejected: 4${exportDir ? `; exported 11 layouts to ${exportDir}` : ''}`);


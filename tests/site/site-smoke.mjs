// Site smoke (CV3.DS5.US1): content parity PT/EN, three opening variants, seven diagram scenes, reduced motion,
// URL parameters used in review sessions, and no external resources. Runs the real site scripts in a minimal fake DOM.
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..', '..', 'site');
const read = (f) => fs.readFileSync(path.join(root, f), 'utf8');
const html = read('index.html'), css = read('site.css'), i18n = read('i18n.js'), model = read('model.js'), site = read('site.js');

// 1. No external resources: only the project's own GitHub links may leave the page.
for (const [name, text] of [['index.html', html], ['site.css', css], ['i18n.js', i18n], ['model.js', model], ['site.js', site]]) {
  const urls = text.match(/https?:\/\/[^\s"'`)<]+/g) || [];
  const foreign = urls.filter((u) => !u.startsWith('https://github.com/rodrigoimmaginario/ipsec-studio') && !u.startsWith('http://www.w3.org/2000/svg'));
  assert.deepEqual(foreign, [], `${name} must not load external resources: ${foreign.join(', ')}`);
}
assert.ok(!/<script[^>]+src=["']https?:/i.test(html) && !/<link[^>]+href=["']https?:/i.test(html), 'no external scripts or styles');

// 2. Fake DOM, just enough for site.js.
function element(id) {
  const listeners = {};
  const classes = new Set();
  return {
    id, textContent: '', innerHTML: '', value: '', hidden: false, dataset: {}, style: {}, attrs: {},
    setAttribute(k, v) { this.attrs[k] = String(v); }, getAttribute(k) { return this.attrs[k] ?? null; },
    addEventListener(t, fn) { (listeners[t] ||= []).push(fn); }, fire(t, ev = {}) { (listeners[t] || []).forEach((fn) => fn({ preventDefault() {}, target: this, ...ev })); },
    classList: { add: (c) => classes.add(c), remove: (c) => classes.delete(c), toggle: (c, on) => { const v = on ?? !classes.has(c); v ? classes.add(c) : classes.delete(c); return v; }, contains: (c) => classes.has(c) },
    querySelectorAll() { return []; }, scrollIntoView() { this.scrolled = true; }, focus() {},
  };
}
function load({ search = '', lang = null, reducedMotion = false } = {}) {
  const els = {};
  const ids = [...html.matchAll(/id="([^"]+)"/g)].map((m) => m[1]);
  for (const id of ids) els[id] = element(id);
  const store = new Map(lang ? [['ipsec-site-lang', lang]] : []);
  const timers = [];
  const ctx = {
    console, URLSearchParams,
    location: { search },
    localStorage: { getItem: (k) => store.get(k) ?? null, setItem: (k, v) => store.set(k, String(v)) },
    matchMedia: (q) => ({ matches: q.includes('reduce') ? reducedMotion : false, addEventListener() {} }),
    setInterval: (fn, ms) => { timers.push({ fn, ms }); return timers.length; }, clearInterval: (id) => { if (timers[id - 1]) timers[id - 1].cleared = true; },
    document: { getElementById: (id) => els[id] || null, documentElement: element('html'), querySelectorAll: () => [] },
  };
  ctx.window = ctx;
  vm.createContext(ctx);
  for (const code of [i18n, model, site]) vm.runInContext(code, ctx);
  return { ctx, els, timers, store };
}

// 3. Content parity and structure.
{
  const { ctx } = load();
  const { pt, en } = ctx.SITE_I18N;
  const keys = (o, p = '') => Object.entries(o).flatMap(([k, v]) => (v && typeof v === 'object' && !Array.isArray(v) ? keys(v, p + k + '.') : [p + k]));
  assert.deepEqual(keys(en).sort(), keys(pt).sort(), 'PT and EN have the same keys');
  assert.deepEqual(Object.keys(pt.variants).sort(), ['a', 'b', 'c'], 'three opening variants');
  assert.equal(ctx.SITE_MODEL.scenes.length, 7, 'seven scenes');
  for (const lang of [pt, en]) assert.equal(lang.scenes.length, 7, 'a caption per scene and language');
  const nodeIds = new Set(ctx.SITE_MODEL.nodes.map((n) => n.id));
  for (const s of ctx.SITE_MODEL.scenes) for (const l of s.links) {
    assert.ok(nodeIds.has(l.from) && nodeIds.has(l.to), `scene ${s.id}: link ${l.from}->${l.to} uses known nodes`);
    assert.ok(['clear', 'auth', 'enc', 'refused'].includes(l.kind), `scene ${s.id}: known outcome`);
  }
  for (const n of ctx.SITE_MODEL.nodes) for (const lang of [pt, en]) assert.ok(lang.nodes[n.id], `label for node ${n.id}`);
  assert.equal(pt.variants.a.title, 'Você ainda se lembra do IPsec?');
  assert.ok(!/(melhor do mundo|revolucion|incr[ií]vel)/i.test(JSON.stringify(pt)), 'no hype words');
}

// 4. Default page: PT, variant A, scene 1 rendered, autoplay running.
{
  const { els, timers } = load();
  assert.equal(els.headline.textContent, 'Você ainda se lembra do IPsec?');
  assert.ok(els.diagram.innerHTML.includes('<svg') && els.diagram.innerHTML.includes('data-scene="1"'), 'diagram drawn at scene 1');
  assert.ok(els.caption.textContent.length > 20, 'scene caption shown');
  assert.match(html, /id="caption"[^>]*aria-live="polite"/, 'caption announced to screen readers');
  assert.ok(timers.some((t) => !t.cleared), 'autoplay running');
  els['btn-next'].fire('click');
  assert.ok(els.diagram.innerHTML.includes('data-scene="2"'), 'next scene');
  els['btn-after'].fire('click');
  assert.ok(els.diagram.innerHTML.includes('data-mode="after"'), 'after overview');
  els['btn-before'].fire('click');
  assert.ok(els.diagram.innerHTML.includes('data-mode="before"'), 'before overview');
  els['cta-how'].fire('click');
  assert.ok(els.how.scrolled, '"see how it works" scrolls to the diagram');
  assert.ok(els['cta-download'].getAttribute('href').startsWith('https://github.com/rodrigoimmaginario/ipsec-studio/releases'), 'download goes to releases');
}

// 5. Variants, language, review parameters, prototype selector.
{
  const b = load({ search: '?v=b' });
  assert.equal(b.els.headline.textContent, 'E se o IPsec ainda fosse a melhor defesa que você não está usando?');
  assert.ok(b.els['proto-panel'].hidden, 'selector hidden outside prototype sessions');
  const p = load({ search: '?v=c&prototipo' });
  assert.equal(p.els['proto-panel'].hidden, false, 'selector visible with ?prototipo');
  p.els['proto-a'].fire('click');
  assert.equal(p.els.headline.textContent, 'Você ainda se lembra do IPsec?');
  const en = load({ search: '?lang=en&cena=4' });
  assert.equal(en.els.headline.textContent, 'Remember IPsec?');
  assert.ok(en.els.diagram.innerHTML.includes('data-scene="4"') && en.timers.every((t) => t.cleared), 'review param opens a scene without autoplay');
  const stored = load({ lang: 'en' });
  assert.equal(stored.els.headline.textContent, 'Remember IPsec?', 'remembered language');
  stored.els['lang-pt'].fire('click');
  assert.equal(stored.store.get('ipsec-site-lang'), 'pt');
  assert.equal(stored.els.headline.textContent, 'Você ainda se lembra do IPsec?');
}

// 6. Reduced motion: no autoplay, static steps.
{
  const { els, timers } = load({ reducedMotion: true });
  assert.ok(timers.length === 0 || timers.every((t) => t.cleared), 'no autoplay with reduced motion');
  assert.ok(els.diagram.innerHTML.includes('data-motion="reduced"'), 'diagram marked as static');
}

console.log('PASS site: PT/EN parity, 3 openings, 7 scenes, before/after, review params, reduced motion, no external resources');

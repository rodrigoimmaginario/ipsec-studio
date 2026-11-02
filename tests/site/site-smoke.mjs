// Site smoke (CV3.DS5.US1): content parity PT/EN, four opening variants, one diagram with the four states, reduced motion,
// URL parameters used in review sessions, and no external resources. Runs the real site scripts in a minimal fake DOM.
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..', '..', 'site');
const read = (f) => fs.readFileSync(path.join(root, f), 'utf8');
const html = read('index.html'), css = read('site.css'), i18n = read('i18n.js'), model = read('model.js'), site = read('site.js');

// 1. No external resources: only the project's own GitHub links and the reference links (plain navigation, nothing loaded) may leave the page.
const refBlock = (model.match(/references: \[[\s\S]*?\n  \],/) || [''])[0];
const refUrls = refBlock.match(/https:\/\/[^\s'"]+/g) || [];
assert.ok(refUrls.every((u) => /^https:\/\/(learn\.microsoft\.com|web\.archive\.org)\//.test(u)), 'reference links only to Microsoft Learn or archived copies');
for (const [name, text] of [['index.html', html], ['site.css', css], ['i18n.js', i18n], ['model.js', model.replace(refBlock, '')], ['site.js', site]]) {
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

// 3. Content parity and structure: one diagram showing the four states at once, outside machines stopped.
{
  const { ctx } = load();
  const { pt, en } = ctx.SITE_I18N;
  const keys = (o, p = '') => Object.entries(o).flatMap(([k, v]) => (v && typeof v === 'object' && !Array.isArray(v) ? keys(v, p + k + '.') : [p + k]));
  assert.deepEqual(keys(en).sort(), keys(pt).sort(), 'PT and EN have the same keys');
  assert.deepEqual(Object.keys(pt.variants).sort(), ['a', 'b', 'c', 'd'], 'four opening variants');
  assert.equal(pt.variants.d.title, 'Você sabe quem está usando a sua rede?', 'opening D comes from the 2006 article');
  assert.equal(pt.variants.a.title, 'Você conhece o IPsec — ou ainda se lembra dele?');
  assert.equal(pt.cta.download, 'Baixar grátis e colaborar com o projeto');
  const M = ctx.SITE_MODEL;
  assert.deepEqual([...M.states], ['refused', 'auth', 'enc', 'boundary'], 'four states');
  for (const lang of [pt, en]) for (const k of M.states) assert.ok(lang.states[k].title && lang.states[k].text, 'state text ' + k);
  const ids = new Set(M.nodes.map((n) => n.id));
  for (const l of [...M.flows, ...M.before]) assert.ok(ids.has(l.from) && ids.has(l.to) && ['clear', 'auth', 'enc', 'refused'].includes(l.kind), 'known flow ' + l.from + '->' + l.to);
  for (const n of M.nodes) for (const lang of [pt, en]) assert.ok(lang.nodes[n.id], 'label for node ' + n.id);
  const outside = M.nodes.filter((n) => n.zone === 'outside').map((n) => n.id);
  assert.ok(outside.length >= 2, 'machines outside the domain');
  assert.ok(M.flows.some((l) => outside.includes(l.from) && l.kind === 'refused'), 'an outside machine is stopped');
  assert.ok(M.flows.some((l) => l.kind === 'enc' && ids.has(l.to) && M.nodes.find((n) => n.id === l.to).zone === 'restricted'), 'encrypted to the restricted server');
  assert.ok(M.flows.some((l) => l.kind === 'refused' && !outside.includes(l.from)), 'a domain machine outside the group is refused');
  assert.ok(M.flows.some((l) => M.nodes.find((n) => n.id === l.to).zone === 'boundary' && l.kind === 'clear') && M.flows.some((l) => M.nodes.find((n) => n.id === l.to).zone === 'boundary' && l.kind === 'auth'), 'boundary serves both worlds');
  assert.ok(M.before.every((l) => l.kind === 'clear'), 'before: everything in clear');
  for (const l of M.flows) if (l.note) for (const lang of [pt, en]) assert.ok(lang.notes[l.note], 'note ' + l.note);
  assert.ok(!/(melhor do mundo|revolucion|incr[ií]vel)/i.test(JSON.stringify(pt)), 'no hype words');
  // Protection (risks and attacks) and benefits sections, with honest limits.
  for (const lang of [pt, en]) {
    assert.equal(lang.protects.items.length, 6, 'six risk scenarios');
    assert.ok(lang.protects.items.every((p) => p.risk && p.how), 'each risk says how IPsec helps');
    assert.ok(lang.protects.limits.title && lang.protects.limits.text, 'limits stated');
    assert.equal(lang.benefits.items.length, 6, 'six benefits');
    assert.ok(lang.benefits.items.every((b) => b.title && b.text), 'benefits');
    assert.ok(lang.protects.featured.risk && lang.protects.featured.how, 'group access among trusted machines highlighted');
    assert.ok(lang.references.items && lang.references.noLink, 'references texts');
  }
  assert.ok(/RH/.test(pt.protects.featured.how) && /HR/.test(en.protects.featured.how), 'featured risk uses the HR database example');
  assert.ok(!('caseNote' in pt.benefits), 'no case quote in the benefits');
}

// 4. Default page: PT, variant A, the single diagram with the four states, no carousel.
{
  const { els, timers } = load();
  assert.equal(els.headline.textContent, 'Você conhece o IPsec — ou ainda se lembra dele?');
  const d = els.diagram.innerHTML;
  assert.ok(d.includes('<svg') && d.includes('data-mode="after"'), 'diagram drawn with IPsec');
  for (const k of ['refused', 'auth', 'enc', 'clear']) assert.ok(d.includes('class="link ' + k + '"'), 'flow ' + k + ' drawn');
  assert.ok(d.includes('fora do domínio') && d.includes('do domínio, mas não do RH') && d.includes('só o grupo'), 'flow notes');
  assert.ok(els.states.innerHTML.includes('Bloqueado') && els.states.innerHTML.includes('Fronteira'), 'four states explained');
  assert.equal((els['protect-items'].innerHTML.match(/<li /g) || []).length, 6, 'risk cards rendered');
  assert.equal((els['benefit-items'].innerHTML.match(/<li>/g) || []).length, 6, 'benefits rendered');
  assert.ok(els['protect-featured'].innerHTML.includes('passe livre'), 'featured risk rendered');
  assert.equal((els['ref-items'].innerHTML.match(/<li>/g) || []).length, 5, 'references rendered');
  assert.ok(els['ref-items'].innerHTML.includes('quemusa-rodrigo.mspx'), 'Portuguese 2006 column linked');
  assert.ok(els['ref-items'].innerHTML.includes('href="referencias/microsoft-case-study-uvv-ipsec-2006.pdf"'), 'case study PDF linked');
  assert.ok(fs.existsSync(path.join(root, 'referencias', 'microsoft-case-study-uvv-ipsec-2006.pdf')), 'case study PDF published with the site');
  // A blocked connection is drawn only up to its X: the line ends where the badge is, before the responder.
  const blocked = [...d.matchAll(/<g class="link refused"[^>]*><path class="track" d="[^"]* ([\d.]+) ([\d.]+)"\/>.*?<circle cx="([\d.]+)" cy="([\d.]+)"/g)];
  assert.equal(blocked.length, 2, "both blocked flows found");
  for (const m of blocked) assert.ok(Math.abs(+m[1] - +m[3]) < 1 && Math.abs(+m[2] - +m[4]) < 1, 'blocked line ends at the X');
  assert.equal(timers.length, 0, 'no carousel timer');
  els['btn-before'].fire('click');
  assert.ok(els.diagram.innerHTML.includes('data-mode="before"') && !els.diagram.innerHTML.includes('class="link refused"'), 'before: no protection');
  assert.equal(els['before-text'].hidden, false);
  els['btn-after'].fire('click');
  assert.ok(els.diagram.innerHTML.includes('data-mode="after"'));
  assert.equal(els['before-text'].hidden, true);
  els['cta-how'].fire('click');
  assert.ok(els.how.scrolled, '"see how it works" scrolls to the diagram');
  assert.ok(els['cta-download'].getAttribute('href').startsWith('https://github.com/rodrigoimmaginario/ipsec-studio/releases'), 'download goes to releases');
}

// 5. Variants, language, review parameters, prototype selector.
{
  const b = load({ search: '?v=b' });
  assert.equal(b.els.headline.textContent, 'E se o IPsec ainda fosse a melhor defesa que você não está usando?');
  assert.ok(b.els['proto-panel'].hidden, 'selector hidden outside prototype sessions');
  assert.equal(load({ search: '?v=d' }).els.headline.textContent, 'Você sabe quem está usando a sua rede?', 'opening D selectable');
  const p = load({ search: '?v=c&prototipo' });
  assert.equal(p.els['proto-panel'].hidden, false, 'selector visible with ?prototipo');
  p.els['proto-a'].fire('click');
  assert.equal(p.els.headline.textContent, 'Você conhece o IPsec — ou ainda se lembra dele?');
  const en = load({ search: '?lang=en&modo=antes' });
  assert.equal(en.els.headline.textContent, 'Do you know IPsec — or still remember it?');
  assert.ok(en.els.diagram.innerHTML.includes('data-mode="before"'), 'review param opens the before view');
  const stored = load({ lang: 'en' });
  assert.ok(stored.els.diagram.innerHTML.includes('outside the domain'), 'remembered language in the diagram');
  stored.els['lang-pt'].fire('click');
  assert.equal(stored.store.get('ipsec-site-lang'), 'pt');
  assert.equal(stored.els.headline.textContent, 'Você conhece o IPsec — ou ainda se lembra dele?');
}

// 6. Reduced motion: the diagram is marked static (CSS hides the moving pulses).
{
  const { els } = load({ reducedMotion: true });
  assert.ok(els.diagram.innerHTML.includes('data-motion="reduced"'), 'diagram marked as static');
  assert.match(css, /svg\[data-motion="reduced"\] \.pulse \{ display: none; \}/, 'pulses hidden with reduced motion');
}

console.log('PASS site: PT/EN parity, 4 openings, one diagram with the 4 states and outside machines stopped, before/after, review params, reduced motion, no external resources');

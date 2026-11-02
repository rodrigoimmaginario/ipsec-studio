// Site smoke (CV3.DS5): PT/EN parity, the opening (question first, IPsec as the answer), the scroll story of the diagram
// (before, the four states one at a time, all together), the five steps with real screens, protection, benefits, why,
// proof with stated limits, download, references, share metadata, reduced motion and no external resources.
// Runs the real site scripts in a minimal fake DOM.
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..', '..', 'site');
const read = (f) => fs.readFileSync(path.join(root, f), 'utf8');
const html = read('index.html'), css = read('site.css'), i18n = read('i18n.js'), model = read('model.js'), site = read('site.js');

// 1. No external resources: only the project's own GitHub links, the reference links (plain navigation, nothing loaded)
//    and the Pages address in the share metadata may leave the page.
const refBlock = (model.match(/references: \[[\s\S]*?\n  \],/) || [''])[0];
const refUrls = refBlock.match(/https:\/\/[^\s'"]+/g) || [];
// Rodrigo (02/11): no links to web caches; references point to official Microsoft pages or to files published with the site.
assert.ok(refUrls.length > 0 && refUrls.every((u) => /^https:\/\/learn\.microsoft\.com\//.test(u)), 'reference links only to official Microsoft Learn pages');
assert.ok(!/web\.archive\.org/.test(html + model + i18n + site), 'no web cache links');
const PAGES = 'https://rodrigoimmaginario.github.io/ipsec-studio/';
for (const [name, text] of [['index.html', html], ['site.css', css], ['i18n.js', i18n], ['model.js', model.replace(refBlock, '')], ['site.js', site]]) {
  const urls = text.match(/https?:\/\/[^\s"'`)<]+/g) || [];
  const foreign = urls.filter((u) => !u.startsWith('https://github.com/rodrigoimmaginario/ipsec-studio') && !u.startsWith('http://www.w3.org/2000/svg') && !(name === 'index.html' && u.startsWith(PAGES)));
  assert.deepEqual(foreign, [], `${name} must not load external resources: ${foreign.join(', ')}`);
}
assert.ok(!/<script[^>]+src=["']https?:/i.test(html) && !/<link(?![^>]*rel="canonical")[^>]+href=["']https?:/i.test(html), 'no external scripts or styles');
assert.ok(!/<img[^>]+src=["']https?:/i.test(html) && !/url\(\s*['"]?https?:/i.test(css), 'no external images');

// 2. Fake DOM, just enough for site.js.
function element(id) {
  const listeners = {};
  const classes = new Set();
  return {
    id, textContent: '', innerHTML: '', hidden: false, dataset: {}, style: {}, attrs: {},
    setAttribute(k, v) { this.attrs[k] = String(v); }, getAttribute(k) { return this.attrs[k] ?? null; }, removeAttribute(k) { delete this.attrs[k]; },
    addEventListener(t, fn) { (listeners[t] ||= []).push(fn); }, fire(t, ev = {}) { (listeners[t] || []).forEach((fn) => fn({ preventDefault() {}, target: this, ...ev })); },
    classList: { add: (c) => classes.add(c), remove: (c) => classes.delete(c), toggle: (c, on) => { const v = on ?? !classes.has(c); v ? classes.add(c) : classes.delete(c); return v; }, contains: (c) => classes.has(c) },
    querySelectorAll() { return []; }, focus() {},
  };
}
function load({ search = '', lang = null, reducedMotion = false } = {}) {
  const els = {};
  for (const m of html.matchAll(/<[^>]*\sid="([^"]+)"[^>]*>/g)) { els[m[1]] = element(m[1]); els[m[1]].hidden = /\shidden[\s>]/.test(m[0]); }
  const store = new Map(lang ? [['ipsec-site-lang', lang]] : []);
  const timers = [];
  const ctx = {
    console, URLSearchParams,
    location: { search },
    localStorage: { getItem: (k) => store.get(k) ?? null, setItem: (k, v) => store.set(k, String(v)) },
    matchMedia: (q) => ({ matches: q.includes('reduce') ? reducedMotion : false, addEventListener() {} }),
    setInterval: (fn, ms) => { timers.push({ fn, ms }); return timers.length; }, clearInterval() {},
    addEventListener() {}, scrollTo() {}, scrollY: 0, innerHeight: 900,
    document: { getElementById: (id) => els[id] || null, documentElement: element('html'), querySelectorAll: () => [] },
  };
  ctx.window = ctx;
  vm.createContext(ctx);
  for (const code of [i18n, model, site]) vm.runInContext(code, ctx);
  return { ctx, els, timers, store };
}

// 3. Content: parity, the opening, the scroll story, every section.
{
  const { ctx } = load();
  const { pt, en } = ctx.SITE_I18N;
  const keys = (o, p = '') => Object.entries(o).flatMap(([k, v]) => (v && typeof v === 'object' && !Array.isArray(v) ? keys(v, p + k + '.') : [p + k]));
  assert.deepEqual(keys(en).sort(), keys(pt).sort(), 'PT and EN have the same keys');
  // opening decided by Rodrigo: the question first, IPsec as the answer; no variant selector any more
  assert.equal(pt.hero.title, 'Você sabe quem está usando a sua rede?');
  assert.equal(pt.hero.lead, 'Você conhece o IPsec — ou ainda se lembra dele?');
  assert.equal(en.hero.title, 'Do you know who is using your network?');
  assert.ok(!('variants' in pt) && !('proto' in pt), 'no opening variants left');
  assert.equal(pt.cta.download, 'Baixar grátis e colaborar com o projeto');
  assert.equal(pt.voice.questions.length, 3, 'three questions from the people who run the network');
  const M = ctx.SITE_MODEL;
  assert.deepEqual([...M.states], ['refused', 'auth', 'enc', 'boundary'], 'four states');
  for (const lang of [pt, en]) {
    for (const k of M.states) assert.ok(lang.states[k].title && lang.states[k].text, 'state text ' + k);
    assert.ok(lang.how.beforeTitle && lang.how.allTitle && lang.beforeText && lang.alsoNote, 'scene texts');
    assert.equal(lang.protects.items.length, 6, 'six risks');
    assert.ok(lang.protects.featured.risk && lang.protects.limits.text, 'featured risk and limits');
    assert.equal(lang.benefits.items.length, 6, 'six benefits');
    assert.equal(lang.why.items.length, 4, 'four fears');
    assert.equal(lang.steps.items.length, 5, 'five steps');
    assert.ok(lang.steps.items.every((s) => s.title && s.text && s.alt) && lang.steps.sample, 'steps described, sample data stated');
    assert.equal(lang.proof.stats.length, 4, 'four proof numbers');
    assert.ok(lang.proof.items.length >= 4 && lang.proof.limits.items.length >= 3, 'proof with stated limits');
    assert.ok(lang.download.requirements.length >= 3 && lang.download.verifyCmd && lang.download.contribute, 'download, verify and contribute');
  }
  assert.ok(/RH/.test(pt.protects.featured.how), 'featured risk uses the HR database example');
  const visible = JSON.stringify(pt);
  assert.ok(!/drift/i.test(visible) && !/totalidade/i.test(visible) && !/(melhor do mundo|revolucion|incr[ií]vel)/i.test(visible), 'plain, sober wording');
  const ids = new Set(M.nodes.map((n) => n.id));
  for (const l of [...M.flows, ...M.before]) assert.ok(ids.has(l.from) && ids.has(l.to), 'known flow ' + l.from + '->' + l.to);
  const outside = M.nodes.filter((n) => n.zone === 'outside').map((n) => n.id);
  assert.ok(M.flows.some((l) => outside.includes(l.from) && l.kind === 'refused'), 'an outside machine is stopped');
  assert.ok(M.flows.some((l) => l.kind === 'refused' && !outside.includes(l.from)), 'a domain machine outside the group is refused');
  assert.ok(M.before.every((l) => l.kind === 'clear'), 'before: everything in clear');
}

// 3b. Page structure, navigation, share metadata, images.
{
  const order = ['inicio', 'pergunta', 'como', 'protecao', 'beneficios', 'por-que', 'passos', 'prova', 'baixar', 'referencias'];
  const pos = order.map((id) => html.search(new RegExp(`<section[^>]+id="${id}"`)));
  assert.ok(pos.every((p) => p > 0) && pos.every((p, i) => i === 0 || p > pos[i - 1]), 'sections in the planned order');
  for (const id of ['como', 'protecao', 'passos', 'prova']) assert.ok(html.includes(`href="#${id}" data-sec="${id}"`), 'header link to ' + id);
  assert.ok(html.includes('id="menu"') && html.includes('aria-controls="sheet"') && html.includes('id="to-top"') && html.includes('id="progress"'), 'mobile menu, back to top, reading progress');
  assert.match(html, /<link rel="canonical" href="https:\/\/rodrigoimmaginario\.github\.io\/ipsec-studio\/">/, 'canonical');
  for (const p of ['og:title', 'og:description', 'og:url', 'og:type']) assert.ok(html.includes(`property="${p}"`), p);
  assert.match(html, /property="og:image" content="https:\/\/rodrigoimmaginario\.github\.io\/ipsec-studio\/img\/og\.png"/, 'og:image on Pages');
  assert.match(html, /<link rel="icon" href="img\/favicon\.svg"/, 'local favicon');
  const images = ['og.png', 'favicon.svg', ...[1, 2, 3, 4, 5].flatMap((n) => [`passo-${n}-pt.png`, `passo-${n}-en.png`])];
  for (const f of images) {
    const file = path.join(root, 'img', f);
    assert.ok(fs.existsSync(file), 'image ' + f);
    assert.ok(fs.statSync(file).size <= 300 * 1024, 'image under 300 KB: ' + f);
  }
  assert.ok(fs.existsSync(path.join(root, 'referencias', 'microsoft-case-study-uvv-ipsec-2006.pdf')), 'case study PDF published with the site');
}

// 4. Default page: PT, the diagram with all states, the scenes, the steps and every section rendered.
{
  const { ctx, els, timers } = load();
  assert.equal(els['hero-title'].textContent, 'Você sabe quem está usando a sua rede?');
  assert.equal(els['hero-lead'].textContent, 'Você conhece o IPsec — ou ainda se lembra dele?');
  assert.equal(els['hero-shot'].getAttribute('src'), 'img/passo-3-pt.png');
  assert.equal((els['voice-items'].innerHTML.match(/<blockquote>/g) || []).length, 3, 'questions rendered');
  const d = els.diagram.innerHTML;
  assert.ok(d.includes('<svg') && d.includes('data-mode="after"') && !d.includes('data-focus'), 'diagram drawn with IPsec, all states');
  for (const k of ['refused', 'auth', 'enc', 'clear']) assert.ok(d.includes('class="link ' + k + '"'), 'flow ' + k + ' drawn');
  assert.ok(d.includes('fora do domínio') && d.includes('do domínio, mas não do RH') && d.includes('só o grupo'), 'flow notes');
  // a blocked connection is drawn only up to its X: the line ends where the badge is, before the responder
  const blocked = [...d.matchAll(/<g class="link refused"[^>]*><path class="track" d="[^"]* ([\d.]+) ([\d.]+)"\/>.*?<circle cx="([\d.]+)" cy="([\d.]+)"/g)];
  assert.equal(blocked.length, 2, 'both blocked flows found');
  for (const m of blocked) assert.ok(Math.abs(+m[1] - +m[3]) < 1 && Math.abs(+m[2] - +m[4]) < 1, 'blocked line ends at the X');
  // scroll story: 6 scenes (before, the 4 states, all together)
  assert.equal((els.scenes.innerHTML.match(/<article class="scene/g) || []).length, 6, 'six scenes');
  ctx.SITE_VIEW.scene(0);
  assert.ok(els.diagram.innerHTML.includes('data-mode="before"') && !els.diagram.innerHTML.includes('class="link refused"'), 'scene 1: the network without IPsec');
  ctx.SITE_VIEW.scene(3);
  assert.ok(els.diagram.innerHTML.includes('data-mode="after"') && els.diagram.innerHTML.includes('data-focus="enc"'), 'scene 4 lights the encrypted state');
  ctx.SITE_VIEW.scene(5);
  assert.ok(!els.diagram.innerHTML.includes('data-focus'), 'last scene shows everything together');
  // steps: texts scroll, the screen stays
  assert.equal((els['step-texts'].innerHTML.match(/<li class="step-text/g) || []).length, 5, 'steps rendered');
  const shots = els.shots.innerHTML;
  for (const n of [1, 2, 3, 4, 5]) assert.ok(shots.includes(`src="img/passo-${n}-pt.png"`), 'step screen ' + n);
  assert.ok(/<img [^>]*alt="[^"]{10,}"/.test(shots) && shots.includes('data-zoom=') && !/ style="/.test(shots + els.diagram.innerHTML), 'screens described and zoomed, no inline styles (blocked by the CSP)');
  ctx.SITE_VIEW.step(3);
  assert.ok(els['shot-label'].textContent.includes('Aplicar em ondas'), 'screen label follows the step');
  assert.ok(els['protect-featured'].innerHTML.includes('passe livre') && (els['protect-items'].innerHTML.match(/<li /g) || []).length === 6, 'protection');
  assert.equal((els['benefit-items'].innerHTML.match(/<li>/g) || []).length, 6, 'benefits');
  assert.equal((els['why-items'].innerHTML.match(/<li>/g) || []).length, 4, 'fears');
  assert.ok(els['why-art'].innerHTML.includes('<svg'), 'abstract image drawn');
  // Origin section removed by Rodrigo (02/11); his publications stay in the references.
  assert.ok(!html.includes('id="origem"') && !('origin' in ctx.SITE_I18N.pt) && !html.includes('href="#origem"'), 'no Origin section');
  assert.equal((els['proof-stats'].innerHTML.match(/<li>/g) || []).length, 4, 'proof numbers');
  assert.ok(els['proof-limits'].innerHTML.includes('<li>'), 'proof limits');
  assert.equal((els['ref-items'].innerHTML.match(/<li>/g) || []).length, 3, 'references');
  assert.ok(els['ref-items'].innerHTML.includes('href="referencias/microsoft-case-study-uvv-ipsec-2006.pdf"'), 'case study published with the site');
  assert.equal(els['dl-release'].getAttribute('href'), 'https://github.com/rodrigoimmaginario/ipsec-studio/releases/latest');
  assert.equal(els['cta-download'].getAttribute('href'), 'https://github.com/rodrigoimmaginario/ipsec-studio/releases/latest');
  assert.equal(els['dl-install'].getAttribute('href'), 'https://github.com/rodrigoimmaginario/ipsec-studio/blob/main/docs/pt-br/instalacao.md');
  assert.equal(els['dl-contribute'].getAttribute('href'), 'https://github.com/rodrigoimmaginario/ipsec-studio/blob/main/CONTRIBUTING.md');
  assert.equal(timers.length, 0, 'no carousel timer');
  els.menu.fire('click');
  assert.equal(els.sheet.hidden, false, 'menu opens');
  assert.equal(els.menu.getAttribute('aria-expanded'), 'true');
}

// 5. Language: parameter, remembered choice, switch.
{
  const en = load({ search: '?lang=en' });
  assert.equal(en.els['hero-title'].textContent, 'Do you know who is using your network?');
  assert.equal(en.els['hero-shot'].getAttribute('src'), 'img/passo-3-en.png');
  const stored = load({ lang: 'en' });
  assert.ok(stored.els.diagram.innerHTML.includes('outside the domain'), 'remembered language in the diagram');
  assert.ok(stored.els.shots.innerHTML.includes('src="img/passo-1-en.png"'), 'English screens in English');
  assert.equal(stored.els['dl-install'].getAttribute('href'), 'https://github.com/rodrigoimmaginario/ipsec-studio/blob/main/docs/en/install.md');
  stored.els['lang-pt'].fire('click');
  assert.equal(stored.store.get('ipsec-site-lang'), 'pt');
  assert.equal(stored.els['hero-title'].textContent, 'Você sabe quem está usando a sua rede?');
}

// 6. Reduced motion: the diagram is static, scenes and steps stay readable, nothing floats.
{
  const { els } = load({ reducedMotion: true });
  assert.ok(els.diagram.innerHTML.includes('data-motion="reduced"'), 'diagram marked as static');
  assert.match(css, /svg\[data-motion="reduced"\] \.pulse \{ display: none; \}/, 'pulses hidden with reduced motion');
  const rm = css.slice(css.lastIndexOf('@media (prefers-reduced-motion: reduce)'));
  assert.ok(/\.chip \{ animation: none; \}/.test(rm) && /\.js \.scene, \.js \.step-text \{ opacity: 1; \}/.test(rm), 'no floating chips, every scene readable');
}

console.log('PASS site: PT/EN parity, opening (question first), scroll story of the diagram (before, 4 states, all), 5 steps with real screens, protection, benefits, why, proof numbers and limits, download, references, navigation, share metadata, reduced motion, no external resources');

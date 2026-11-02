// IPsec Studio site — the "product first" layout (concept 2) with the scroll storytelling of concept 1 in
// "How it works" (the diagram stays, each scene lights one state) and in the five steps (the app screen stays and
// zooms into what matters). Review parameter: ?lang=pt|en. window.SITE_VIEW exposes scene/step for tests and reviews.
(function () {
  const T = window.SITE_I18N, M = window.SITE_MODEL;
  const $ = (id) => document.getElementById(id);
  const root = document.documentElement;
  const params = new URLSearchParams(location.search);
  const reduced = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const LANG_KEY = 'ipsec-site-lang';
  const state = {
    lang: ['pt', 'en'].includes(params.get('lang')) ? params.get('lang') : (localStorage.getItem(LANG_KEY) === 'en' ? 'en' : 'pt'),
    scene: null, step: 0,
  };
  const t = () => T[state.lang];
  const get = (obj, path) => path.split('.').reduce((o, k) => (o == null ? o : o[k]), obj);
  const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const each = (sel, fn) => document.querySelectorAll(sel).forEach(fn);
  root.classList.add('js');

  // ---------- diagram (built from SITE_MODEL) ----------
  const ORDER = ['refused', 'auth', 'enc', 'boundary'];
  const node = (id) => M.nodes.find((n) => n.id === id);
  const stateOf = (l) => (node(l.to).zone === 'boundary' ? 'boundary' : l.kind);
  function icon(n) {
    const { x, y } = n;
    if (n.kind === 'server') return `<rect class="dev" x="${x - 20}" y="${y - 26}" width="40" height="52" rx="5"/><path class="dev-line" d="M${x - 12} ${y - 13}h24M${x - 12} ${y - 3}h24M${x - 12} ${y + 7}h24"/>`;
    if (n.kind === 'laptop') return `<rect class="dev" x="${x - 24}" y="${y - 22}" width="48" height="32" rx="4"/><path class="dev" d="M${x - 32} ${y + 14}h64l-6 8h-52z"/>`;
    return `<rect class="dev" x="${x - 26}" y="${y - 24}" width="52" height="34" rx="4"/><path class="dev" d="M${x - 8} ${y + 10}h16v8h-16z"/><path class="dev-line" d="M${x - 18} ${y + 20}h36"/>`;
  }
  function geometry(l) {
    const a = node(l.from), b = node(l.to);
    const dx = b.x - a.x, dy = b.y - a.y, len = Math.hypot(dx, dy) || 1, ux = dx / len, uy = dy / len, r = 42;
    const p0 = [a.x + ux * r, a.y + uy * r], p3 = [b.x - ux * r, b.y - uy * r];
    const p1 = [p0[0] + (p3[0] - p0[0]) * 0.45, p0[1]], p2 = [p3[0] - (p3[0] - p0[0]) * 0.45, p3[1]];
    const at = (s) => [0, 1].map((i) => (1 - s) ** 3 * p0[i] + 3 * (1 - s) ** 2 * s * p1[i] + 3 * (1 - s) * s ** 2 * p2[i] + s ** 3 * p3[i]);
    const path = (q0, q1, q2, q3) => `M${q0[0].toFixed(1)} ${q0[1].toFixed(1)} C${q1[0].toFixed(1)} ${q1[1].toFixed(1)} ${q2[0].toFixed(1)} ${q2[1].toFixed(1)} ${q3[0].toFixed(1)} ${q3[1].toFixed(1)}`;
    // The curve cut at s (de Casteljau): a blocked connection is drawn only up to its "X" and never reaches the responder.
    const until = (s) => {
      const lerp = (u, v) => [u[0] + (v[0] - u[0]) * s, u[1] + (v[1] - u[1]) * s];
      const a1 = lerp(p0, p1), b1 = lerp(p1, p2), c1 = lerp(p2, p3), a2 = lerp(a1, b1), b2 = lerp(b1, c1), e = lerp(a2, b2);
      return path(p0, a1, a2, e);
    };
    return { d: path(p0, p1, p2, p3), at, until };
  }
  function badge(kind, [x, y]) {
    if (kind === 'refused') return `<g class="badge refused"><circle cx="${x}" cy="${y}" r="13"/><path d="M${x - 4.5} ${y - 4.5}l9 9M${x + 4.5} ${y - 4.5}l-9 9"/></g>`;
    if (kind === 'enc') return `<g class="badge enc"><circle cx="${x}" cy="${y}" r="14"/><rect x="${x - 5.5}" y="${y - 1}" width="11" height="8" rx="1.5"/><path d="M${x - 3.5} ${y - 1}v-3a3.5 3.5 0 0 1 7 0v3"/></g>`;
    if (kind === 'auth') return `<g class="badge auth"><circle cx="${x}" cy="${y}" r="13"/><path d="M${x - 5} ${y}l3.5 3.5 6.5-7"/></g>`;
    return '';
  }
  function svg(mode, focus) {
    const L = t();
    const links = mode === 'before' ? M.before : M.flows;
    const zones = M.zones.map((z) => `<g class="zone ${z.style}" data-zone="${z.id}"><rect x="${z.x}" y="${z.y}" width="${z.w}" height="${z.h}" rx="14"/><text x="${z.x + 14}" y="${z.y + 22}">${esc(L.zones[z.id])}</text></g>`).join('');
    const paths = links.map((l, i) => {
      const g = geometry(l);
      const at = l.at ?? (l.kind === 'refused' ? 0.84 : 0.5);
      const pos = g.at(at).map((v) => +v.toFixed(1));
      const d = l.kind === 'refused' ? g.until(at) : g.d;
      const note = l.note && mode !== 'before' ? `<text class="note ${l.kind}" x="${pos[0]}" y="${pos[1] - 20}" text-anchor="middle">${esc(L.notes[l.note])}</text>` : '';
      return `<g class="link ${l.kind}" data-state="${stateOf(l)}" data-i="${i}"><path class="track" d="${d}"/><path class="pulse" d="${d}" pathLength="100"/>${badge(l.kind, pos)}${note}</g>`;
    }).join('');
    const nodes = M.nodes.map((n) => `<g class="node${n.zone === 'outside' ? ' outsider' : ''}">${icon(n)}<text x="${n.x}" y="${n.y + 50}" text-anchor="middle">${String(L.nodes[n.id]).split('\n').map((p, k) => `<tspan x="${n.x}" dy="${k ? 18 : 0}">${esc(p)}</tspan>`).join('')}</text></g>`).join('');
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 960 540" data-mode="${mode}" data-motion="${reduced ? 'reduced' : 'full'}"${focus ? ` data-focus="${focus}"` : ''}>${zones}<g class="links">${paths}</g>${nodes}</svg>`;
  }
  // Scenes of "How it works": first the network without IPsec, then one state at a time, then everything together.
  function scenes() {
    const L = t();
    return [{ mode: 'before', focus: null, key: 'before', title: L.how.beforeTitle, text: L.beforeText }]
      .concat(ORDER.map((k) => ({ mode: 'after', focus: k, key: k, title: L.states[k].title, text: L.states[k].text })))
      .concat([{ mode: 'after', focus: null, key: 'all', title: L.how.allTitle, text: L.alsoNote }]);
  }
  function drawDiagram() {
    const s = state.scene === null ? { mode: 'after', focus: null } : scenes()[state.scene];
    $('diagram').innerHTML = svg(s.mode, s.focus);
    $('diagram').setAttribute('aria-label', `${t().how.title} ${s.mode === 'before' ? t().beforeText : ''}`.trim());
    // The page CSP blocks inline style attributes; per-element values go through the CSSOM, which it allows.
    each('#diagram .link', (g) => g.style.setProperty('--i', g.dataset.i));
  }
  function setScene(i) {
    if (state.scene === i) return;
    const s = scenes()[i];
    const el = $('diagram').querySelector ? $('diagram').querySelector('svg') : null;
    state.scene = i;
    each('.scene', (x) => x.classList.toggle('on', +x.dataset.i === i));
    if (el && el.getAttribute('data-mode') === s.mode) { if (s.focus) el.setAttribute('data-focus', s.focus); else el.removeAttribute('data-focus'); } else drawDiagram();
  }

  // ---------- five steps: where each screen has the detail that matters (percent of the capture) and the zoom ----------
  const STEP_FOCUS = [{ x: 62, y: 74, s: 1.55 }, { x: 56, y: 58, s: 1.45 }, { x: 60, y: 52, s: 1.3 }, { x: 84, y: 60, s: 1.85 }, { x: 57, y: 66, s: 1.4 }];
  function setStep(i) {
    state.step = i;
    each('.step-text', (x) => x.classList.toggle('on', +x.dataset.i === i));
    each('#shots img', (img, k) => img.classList.toggle('on', k === i));
    $('shot-label').textContent = `IPsec Studio — ${t().steps.items[i].title}`;
  }

  // ---------- abstract images in the diagram's language ----------
  function rng(seed) { let s = seed; return () => ((s = (s * 1664525 + 1013904223) % 4294967296) / 4294967296); }
  // Tangled rules on the left become ordered, verified connections on the right.
  function whyArt() {
    const r = rng(11);
    const tangle = Array.from({ length: 9 }, () => { const y1 = 20 + r() * 200, y2 = 20 + r() * 200; return `<path class="tangle" d="M10 ${y1.toFixed(0)} C ${(70 + r() * 80).toFixed(0)} ${(r() * 240).toFixed(0)}, ${(110 + r() * 80).toFixed(0)} ${(r() * 240).toFixed(0)}, 230 ${y2.toFixed(0)}"/>`; }).join('');
    const kinds = ['auth', 'enc', 'auth', 'refused', 'auth'];
    const ordered = kinds.map((k, i) => { const y = 36 + i * 42; const end = k === 'refused' ? 400 : 500; return `<path class="ord ${k}" d="M300 ${y}H${end}"/><circle class="dot" cx="300" cy="${y}" r="5"/>${k === 'refused' ? '' : `<circle class="dot" cx="500" cy="${y}" r="5"/>`}<circle class="b ${k}" cx="${k === 'refused' ? 400 : 420}" cy="${y}" r="9"/>`; }).join('');
    return `<svg viewBox="0 0 520 240"><path class="arrow" d="M244 120h40m-10-8 10 8-10 8"/>${tangle}${ordered}</svg>`;
  }
  // Two separate networks (students and administration) that become one isolated network.
  function originArt() {
    const r = rng(5);
    const cluster = (cx, cy, n, cls) => Array.from({ length: n }, (_, i) => { const a = (i / n) * Math.PI * 2 + r(); const d = 18 + r() * 26; return [cx + Math.cos(a) * d, cy + Math.sin(a) * d]; }).map(([x, y]) => `<line class="spoke ${cls}" x1="${cx}" y1="${cy}" x2="${x.toFixed(0)}" y2="${y.toFixed(0)}"/><circle class="n ${cls}" cx="${x.toFixed(0)}" cy="${y.toFixed(0)}" r="4"/>`).join('');
    return `<svg viewBox="0 0 520 200">
      <circle class="net a" cx="70" cy="62" r="52"/>${cluster(70, 62, 7, 'a')}<circle class="net b" cx="70" cy="148" r="44"/>${cluster(70, 148, 6, 'b')}
      <path class="arrow" d="M150 105h60m-10-8 10 8-10 8"/>
      <circle class="net one" cx="370" cy="102" r="92"/>${cluster(340, 80, 7, 'a')}${cluster(400, 128, 6, 'b')}
      <g class="lockb"><circle cx="370" cy="104" r="12"/><rect x="364.5" y="103" width="11" height="8" rx="1.5"/><path d="M366.5 103v-3a3.5 3.5 0 0 1 7 0v3"/></g></svg>`;
  }

  // ---------- rendering ----------
  function render() {
    const L = t();
    root.lang = L.lang;
    each('[data-i18n]', (el) => { const v = get(L, el.getAttribute('data-i18n')); if (typeof v === 'string') el.textContent = v; });
    $('hero-title').textContent = L.hero.title;
    $('hero-lead').textContent = L.hero.lead;
    $('hero-text').textContent = L.hero.text;
    $('hero-shot').setAttribute('src', `img/passo-3-${state.lang}.png`);
    $('hero-shot').setAttribute('alt', L.hero.shotAlt);
    $('facts').innerHTML = L.facts.map((f) => `<li>${esc(f)}</li>`).join('');
    $('voice-items').innerHTML = L.voice.questions.map((q, i) => `<figure><div class="face f${i + 1}" aria-hidden="true"></div><blockquote>${esc(q)}</blockquote></figure>`).join('');
    $('scenes').innerHTML = scenes().map((s, i) => `<article class="scene${state.scene === i ? ' on' : ''}" data-i="${i}" data-key="${s.key}"><span class="scene-n">${String(i + 1).padStart(2, '0')}</span><h3>${esc(s.title)}</h3><p>${esc(s.text)}</p></article>`).join('');
    drawDiagram();
    $('protect-featured').innerHTML = `<h3>${esc(L.protects.featured.risk)}</h3><p>${esc(L.protects.featured.how)}</p>`;
    $('protect-items').innerHTML = L.protects.items.map((p) => `<li class="tile"><h3>${esc(p.risk)}</h3><p>${esc(p.how)}</p></li>`).join('');
    $('benefit-items').innerHTML = L.benefits.items.map((b) => `<li><b>${esc(b.title)}</b><p>${esc(b.text)}</p></li>`).join('');
    $('why-items').innerHTML = L.why.items.map((w) => `<li><b>${esc(w.title)}</b><p>${esc(w.text)}</p></li>`).join('');
    $('why-art').innerHTML = whyArt();
    $('step-texts').innerHTML = L.steps.items.map((s, i) => `<li class="step-text${state.step === i ? ' on' : ''}" data-i="${i}"><span class="step-n">${i + 1}</span><h3>${esc(s.title)}</h3><p>${esc(s.text)}</p></li>`).join('');
    $('shots').innerHTML = L.steps.items.map((s, i) => { const f = STEP_FOCUS[i]; return `<img src="img/passo-${i + 1}-${state.lang}.png" alt="${esc(s.alt)}" loading="lazy" width="1280" height="800" data-zoom="${f.s}" data-ox="${f.x}" data-oy="${f.y}"${state.step === i ? ' class="on"' : ''}>`; }).join('');
    each('#shots img', (img) => { img.style.setProperty('--s', img.dataset.zoom); img.style.transformOrigin = `${img.dataset.ox}% ${img.dataset.oy}%`; });
    $('shot-label').textContent = `IPsec Studio — ${L.steps.items[state.step].title}`;
    const li = (arr) => arr.map((x) => `<li>${esc(x)}</li>`).join('');
    $('proof-stats').innerHTML = L.proof.stats.map((s) => `<li><b>${esc(s.value)}</b><span>${esc(s.label)}</span></li>`).join('');
    $('proof-items').innerHTML = li(L.proof.items);
    $('proof-limits').innerHTML = li(L.proof.limits.items);
    $('origin-timeline').innerHTML = L.origin.timeline.map((x) => `<li><b>${esc(x.year)}</b><span>${esc(x.text)}</span></li>`).join('');
    $('origin-art').innerHTML = originArt();
    $('origin-text').innerHTML = L.origin.paragraphs.map((p) => `<p>${esc(p)}</p>`).join('');
    $('req-items').innerHTML = li(L.download.requirements);
    $('ref-items').innerHTML = M.references.map((r) => {
      const x = L.references.items[r.id];
      const title = r.url ? `<a href="${esc(r.url)}" rel="noopener">${esc(x.title)}</a>` : `<span>${esc(x.title)} <em>(${esc(L.references.noLink)})</em></span>`;
      return `<li>${title}<small>${esc(r.author)} · ${esc(x.meta)}</small></li>`;
    }).join('');
    $('cta-download').setAttribute('href', M.releases);
    $('dl-release').setAttribute('href', M.releases);
    $('dl-install').setAttribute('href', M.docs[state.lang].install);
    $('proof-more').setAttribute('href', M.docs[state.lang].tests);
    $('dl-contribute').setAttribute('href', M.contributing);
    $('lang-pt').setAttribute('aria-pressed', String(state.lang === 'pt'));
    $('lang-en').setAttribute('aria-pressed', String(state.lang === 'en'));
    observe();
  }

  // ---------- scroll: whatever crosses the middle of the screen is "on" ----------
  let io = null;
  function observe() {
    if (!('IntersectionObserver' in window)) return;
    if (io) io.disconnect();
    io = new IntersectionObserver((entries) => entries.forEach((e) => {
      if (!e.isIntersecting) return;
      const el = e.target;
      if (el.classList.contains('scene')) setScene(+el.dataset.i);
      else if (el.classList.contains('step-text')) setStep(+el.dataset.i);
      else each('#nav-links a', (a) => a.classList.toggle('on', a.dataset.sec === el.id));
    }), { rootMargin: window.innerWidth < 900 ? '-62% 0px -32% 0px' : '-45% 0px -50% 0px' });
    each('.scene, .step-text, main > section[id]', (el) => io.observe(el));
  }
  let last = 0;
  window.addEventListener('scroll', () => {
    const y = window.scrollY || 0, max = Math.max(1, document.documentElement.scrollHeight - window.innerHeight);
    $('progress').style.width = `${(100 * y / max).toFixed(2)}%`;
    $('top').classList.toggle('lined', y > 10);
    $('top').classList.toggle('away', y > last && y > 500 && $('sheet').hidden);
    $('to-top').hidden = y < window.innerHeight * 1.5;
    last = y;
  }, { passive: true });

  // ---------- behaviour ----------
  const setLang = (l) => { state.lang = l; localStorage.setItem(LANG_KEY, l); render(); };
  $('lang-pt').addEventListener('click', () => setLang('pt'));
  $('lang-en').addEventListener('click', () => setLang('en'));
  const closeMenu = () => { $('sheet').hidden = true; $('menu').setAttribute('aria-expanded', 'false'); };
  $('menu').addEventListener('click', () => { const open = $('sheet').hidden; $('sheet').hidden = !open; $('menu').setAttribute('aria-expanded', String(open)); });
  $('sheet').addEventListener('click', (e) => { if (e.target.closest && e.target.closest('a')) closeMenu(); });
  $('to-top').addEventListener('click', () => window.scrollTo({ top: 0, behavior: reduced ? 'auto' : 'smooth' }));
  render();
  window.SITE_VIEW = { scene: setScene, step: setStep, state };
})();

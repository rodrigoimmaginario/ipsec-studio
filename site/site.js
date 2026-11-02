// IPsec Studio site — opening variants, language, and one animated diagram (SVG built from SITE_MODEL).
// Review parameters: ?v=a|b|c  ?lang=pt|en  ?modo=antes  ?prototipo (shows the opening selector).
(function () {
  const T = window.SITE_I18N, M = window.SITE_MODEL;
  const $ = (id) => document.getElementById(id);
  const params = new URLSearchParams(location.search);
  const reduced = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const LANG_KEY = 'ipsec-site-lang';
  const state = {
    lang: ['pt', 'en'].includes(params.get('lang')) ? params.get('lang') : (localStorage.getItem(LANG_KEY) === 'en' ? 'en' : 'pt'),
    variant: ['a', 'b', 'c', 'd'].includes(params.get('v')) ? params.get('v') : 'a',
    mode: params.get('modo') === 'antes' ? 'before' : 'after',
  };
  const t = () => T[state.lang];
  const get = (obj, path) => path.split('.').reduce((o, k) => (o == null ? o : o[k]), obj);
  const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  // ---------- diagram ----------
  const node = (id) => M.nodes.find((n) => n.id === id);
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
  function svg() {
    const L = t();
    const links = state.mode === 'before' ? M.before : M.flows;
    const zones = M.zones.map((z) => `<g class="zone ${z.style}"><rect x="${z.x}" y="${z.y}" width="${z.w}" height="${z.h}" rx="14"/><text x="${z.x + 14}" y="${z.y + 22}">${esc(L.zones[z.id])}</text></g>`).join('');
    const paths = links.map((l, i) => {
      const g = geometry(l);
      const t = l.at ?? (l.kind === 'refused' ? 0.84 : 0.5);
      const pos = g.at(t).map((v) => +v.toFixed(1));
      const d = l.kind === 'refused' ? g.until(t) : g.d;
      const note = l.note && state.mode === 'after' ? `<text class="note ${l.kind}" x="${pos[0]}" y="${pos[1] - 20}" text-anchor="middle">${esc(L.notes[l.note])}</text>` : '';
      return `<g class="link ${l.kind}" style="--i:${i}"><path class="track" d="${d}"/><path class="pulse" d="${d}" pathLength="100"/>${badge(l.kind, pos)}${note}</g>`;
    }).join('');
    const nodes = M.nodes.map((n) => `<g class="node${n.zone === 'outside' ? ' outsider' : ''}">${icon(n)}<text x="${n.x}" y="${n.y + 50}" text-anchor="middle">${String(L.nodes[n.id]).split('\n').map((p, k) => `<tspan x="${n.x}" dy="${k ? 18 : 0}">${esc(p)}</tspan>`).join('')}</text></g>`).join('');
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 960 540" data-mode="${state.mode}" data-motion="${reduced ? 'reduced' : 'full'}">${zones}<g class="links">${paths}</g>${nodes}</svg>`;
  }

  // ---------- rendering ----------
  function renderDiagram() {
    const L = t();
    $('diagram').innerHTML = svg();
    $('diagram').setAttribute('aria-label', `${L.how.title} ${state.mode === 'before' ? L.beforeText : ''}`.trim());
    $('before-text').hidden = state.mode !== 'before';
    $('states').innerHTML = M.states.map((k) => `<li class="state ${k}"><span class="mark" aria-hidden="true"></span><div><b>${esc(L.states[k].title)}</b><p>${esc(L.states[k].text)}</p></div></li>`).join('');
    $('states').classList.toggle('dim', state.mode === 'before');
    $('btn-before').setAttribute('aria-pressed', String(state.mode === 'before'));
    $('btn-after').setAttribute('aria-pressed', String(state.mode === 'after'));
  }
  function render() {
    const L = t();
    document.documentElement.lang = L.lang;
    $('headline').textContent = L.variants[state.variant].title;
    $('subtitle').textContent = L.variants[state.variant].subtitle;
    document.querySelectorAll('[data-i18n]').forEach((el) => { const v = get(L, el.getAttribute('data-i18n')); if (typeof v === 'string') el.textContent = v; });
    $('facts').innerHTML = L.facts.map((f) => `<li>${esc(f)}</li>`).join('');
    $('protect-items').innerHTML = L.protects.items.map((p) => `<li class="card"><h3>${esc(p.risk)}</h3><p>${esc(p.how)}</p></li>`).join('');
    $('protect-featured').innerHTML = `<h3>${esc(L.protects.featured.risk)}</h3><p>${esc(L.protects.featured.how)}</p>`;
    $('ref-items').innerHTML = M.references.map((r) => {
      const t = L.references.items[r.id];
      const title = r.url ? `<a href="${esc(r.url)}" rel="noopener">${esc(t.title)}</a>` : `<span>${esc(t.title)} <em>(${esc(L.references.noLink)})</em></span>`;
      return `<li>${title}<small>${esc(r.author)} · ${esc(t.meta)}</small></li>`;
    }).join('');
    const li = (arr) => arr.map((x) => `<li>${esc(x)}</li>`).join('');
    $('why-items').innerHTML = L.why.items.map((w) => `<li><b>${esc(w.title)}</b><p>${esc(w.text)}</p></li>`).join('');
    $('step-items').innerHTML = L.steps.items.map((s, i) => `<li><div class="step-text"><span class="step-n">${i + 1}</span><h3>${esc(s.title)}</h3><p>${esc(s.text)}</p></div><img src="img/passo-${i + 1}-${state.lang}.png" alt="${esc(s.alt)}" loading="lazy" width="1280" height="800"></li>`).join('');
    $('proof-items').innerHTML = li(L.proof.items);
    $('proof-limits').innerHTML = li(L.proof.limits.items);
    $('origin-text').innerHTML = L.origin.paragraphs.map((p) => `<p>${esc(p)}</p>`).join('');
    $('req-items').innerHTML = li(L.download.requirements);
    $('dl-release').setAttribute('href', M.releases);
    $('dl-install').setAttribute('href', M.docs[state.lang].install);
    $('proof-more').setAttribute('href', M.docs[state.lang].tests);
    $('dl-contribute').setAttribute('href', M.contributing);
    $('benefit-items').innerHTML = L.benefits.items.map((b) => `<li><b>${esc(b.title)}</b><p>${esc(b.text)}</p></li>`).join('');
    $('btn-before').textContent = L.controls.before; $('btn-after').textContent = L.controls.after;
    $('lang-pt').setAttribute('aria-pressed', String(state.lang === 'pt'));
    $('lang-en').setAttribute('aria-pressed', String(state.lang === 'en'));
    $('cta-download').setAttribute('href', M.releases);
    ['a', 'b', 'c', 'd'].forEach((v) => $('proto-' + v).setAttribute('aria-pressed', String(state.variant === v)));
    renderDiagram();
  }

  // ---------- behaviour ----------
  $('btn-before').addEventListener('click', () => { state.mode = 'before'; renderDiagram(); });
  $('btn-after').addEventListener('click', () => { state.mode = 'after'; renderDiagram(); });
  $('cta-how').addEventListener('click', (e) => { e.preventDefault(); $('how').scrollIntoView({ behavior: reduced ? 'auto' : 'smooth' }); });
  const setLang = (l) => { state.lang = l; localStorage.setItem(LANG_KEY, l); render(); };
  $('lang-pt').addEventListener('click', () => setLang('pt'));
  $('lang-en').addEventListener('click', () => setLang('en'));
  ['a', 'b', 'c', 'd'].forEach((v) => $('proto-' + v).addEventListener('click', () => { state.variant = v; render(); }));
  $('proto-panel').hidden = !params.has('prototipo');
  render();
})();

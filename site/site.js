// IPsec Studio site — opening variants, language, and the animated diagram (SVG built from SITE_MODEL).
// Review parameters: ?v=a|b|c  ?lang=pt|en  ?cena=1..7  ?modo=antes|depois  ?prototipo (shows the opening selector).
(function () {
  const T = window.SITE_I18N, M = window.SITE_MODEL;
  const $ = (id) => document.getElementById(id);
  const params = new URLSearchParams(location.search);
  const reduced = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const LANG_KEY = 'ipsec-site-lang';
  const state = {
    lang: ['pt', 'en'].includes(params.get('lang')) ? params.get('lang') : (localStorage.getItem(LANG_KEY) === 'en' ? 'en' : 'pt'),
    variant: ['a', 'b', 'c'].includes(params.get('v')) ? params.get('v') : 'a',
    scene: Math.min(Math.max(parseInt(params.get('cena') || '1', 10) || 1, 1), M.scenes.length) - 1,
    mode: params.get('modo') === 'antes' ? 'before' : params.get('modo') === 'depois' ? 'after' : 'story',
    timer: null,
  };
  const t = () => T[state.lang];
  const get = (obj, path) => path.split('.').reduce((o, k) => (o == null ? o : o[k]), obj);
  const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const fmt = (s, ...a) => s.replace(/\{(\d)\}/g, (_, i) => a[i]);

  // ---------- diagram ----------
  const node = (id) => M.nodes.find((n) => n.id === id);
  function icon(n) {
    const { x, y } = n;
    if (n.kind === 'server') return `<rect class="dev" x="${x - 20}" y="${y - 26}" width="40" height="52" rx="5"/><path class="dev-line" d="M${x - 12} ${y - 13}h24M${x - 12} ${y - 3}h24M${x - 12} ${y + 7}h24"/>`;
    if (n.kind === 'laptop') return `<rect class="dev" x="${x - 24}" y="${y - 22}" width="48" height="32" rx="4"/><path class="dev" d="M${x - 32} ${y + 14}h64l-6 8h-52z"/>`;
    if (n.kind === 'printer') return `<rect class="dev" x="${x - 24}" y="${y - 10}" width="48" height="26" rx="4"/><rect class="dev" x="${x - 14}" y="${y - 24}" width="28" height="14" rx="2"/><path class="dev-line" d="M${x - 10} ${y + 6}h20"/>`;
    return `<rect class="dev" x="${x - 26}" y="${y - 24}" width="52" height="34" rx="4"/><path class="dev" d="M${x - 8} ${y + 10}h16v8h-16z"/><path class="dev-line" d="M${x - 18} ${y + 20}h36"/>`;
  }
  function geometry(l) {
    const a = node(l.from), b = node(l.to);
    const dx = b.x - a.x, dy = b.y - a.y, len = Math.hypot(dx, dy) || 1, ux = dx / len, uy = dy / len, r = 40;
    const p0 = [a.x + ux * r, a.y + uy * r], p3 = [b.x - ux * r, b.y - uy * r];
    const horizontal = Math.abs(dx) > Math.abs(dy);
    // Nearly vertical links run along the right side of the nodes, clear of the labels underneath them.
    if (Math.abs(dx) < 40) {
      const q0 = [a.x + 34, a.y], q3 = [b.x + 34, b.y], q1 = [a.x + 86, a.y + dy * 0.3], q2 = [b.x + 86, b.y - dy * 0.3];
      const atv = (s) => [0, 1].map((i) => (1 - s) ** 3 * q0[i] + 3 * (1 - s) ** 2 * s * q1[i] + 3 * (1 - s) * s ** 2 * q2[i] + s ** 3 * q3[i]);
      return { d: `M${q0[0]} ${q0[1]} C${q1[0]} ${q1[1].toFixed(1)} ${q2[0]} ${q2[1].toFixed(1)} ${q3[0]} ${q3[1]}`, at: atv };
    }
    const p1 = horizontal ? [p0[0] + (p3[0] - p0[0]) * 0.45, p0[1]] : [p0[0], p0[1] + (p3[1] - p0[1]) * 0.45];
    const p2 = horizontal ? [p3[0] - (p3[0] - p0[0]) * 0.45, p3[1]] : [p3[0], p3[1] - (p3[1] - p0[1]) * 0.45];
    const at = (s) => [0, 1].map((i) => (1 - s) ** 3 * p0[i] + 3 * (1 - s) ** 2 * s * p1[i] + 3 * (1 - s) * s ** 2 * p2[i] + s ** 3 * p3[i]);
    return { d: `M${p0[0].toFixed(1)} ${p0[1].toFixed(1)} C${p1[0].toFixed(1)} ${p1[1].toFixed(1)} ${p2[0].toFixed(1)} ${p2[1].toFixed(1)} ${p3[0].toFixed(1)} ${p3[1].toFixed(1)}`, at };
  }
  function badge(kind, [x, y]) {
    if (kind === 'refused') return `<g class="badge refused"><circle cx="${x}" cy="${y}" r="13"/><path d="M${x - 4.5} ${y - 4.5}l9 9M${x + 4.5} ${y - 4.5}l-9 9"/></g>`;
    if (kind === 'enc') return `<g class="badge enc"><circle cx="${x}" cy="${y}" r="14"/><rect x="${x - 5.5}" y="${y - 1}" width="11" height="8" rx="1.5"/><path d="M${x - 3.5} ${y - 1}v-3a3.5 3.5 0 0 1 7 0v3"/></g>`;
    if (kind === 'auth') return `<g class="badge auth"><circle cx="${x}" cy="${y}" r="13"/><path d="M${x - 5} ${y}l3.5 3.5 6.5-7"/></g>`;
    return '';
  }
  function svg(links, scene, mode) {
    const L = t();
    const involved = new Set(links.flatMap((l) => [l.from, l.to]));
    const zones = M.zones.map((z) => `<g class="zone ${z.style}"><rect x="${z.x}" y="${z.y}" width="${z.w}" height="${z.h}" rx="14"/><text x="${z.x + 14}" y="${z.label === 'bottom' ? z.y + z.h - 12 : z.y + 22}">${esc(L.zones[z.id])}</text></g>`).join('');
    const paths = links.map((l, i) => {
      const g = geometry(l);
      const end = l.kind === 'refused' ? 0.86 : 0.5;
      return `<g class="link ${l.kind}" style="--i:${i}"><path class="track" d="${g.d}"/><path class="pulse" d="${g.d}" pathLength="100"/>${badge(l.kind, g.at(end).map((v) => +v.toFixed(1)))}</g>`;
    }).join('');
    const nodes = M.nodes.map((n) => `<g class="node${involved.has(n.id) ? ' on' : ''}${n.zone === 'outside' ? ' outsider' : ''}">${icon(n)}<text x="${n.x}" y="${n.y + 50}" text-anchor="middle">${String(L.nodes[n.id]).split('\n').map((p, k) => `<tspan x="${n.x}" dy="${k ? 18 : 0}">${esc(p)}</tspan>`).join('')}</text></g>`).join('');
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 960 620" data-scene="${scene}" data-mode="${mode}" data-motion="${reduced ? 'reduced' : 'full'}">${zones}<g class="links">${paths}</g>${nodes}</svg>`;
  }

  // ---------- rendering ----------
  function renderDiagram() {
    const L = t();
    let links, title, caption, counter;
    if (state.mode === 'story') {
      const s = M.scenes[state.scene];
      links = s.links; title = L.scenes[state.scene].title; caption = L.scenes[state.scene].caption;
      counter = fmt(L.controls.scene, state.scene + 1, M.scenes.length);
    } else {
      links = M.overview[state.mode];
      title = `${L.controls.overview} — ${state.mode === 'before' ? L.controls.before : L.controls.after}`;
      caption = L.overview[state.mode]; counter = '';
    }
    $('diagram').innerHTML = svg(links, state.mode === 'story' ? state.scene + 1 : 0, state.mode);
    $('scene-title').textContent = title;
    $('caption').textContent = caption;
    $('scene-count').textContent = counter;
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
    $('legend').innerHTML = ['auth', 'enc', 'clear', 'refused'].map((k) => `<li class="lg ${k}"><span></span>${esc(L.legend[k])}</li>`).join('');
    $('btn-prev').textContent = L.controls.prev; $('btn-next').textContent = L.controls.next;
    $('btn-play').textContent = state.timer ? L.controls.pause : L.controls.play;
    $('btn-before').textContent = L.controls.before; $('btn-after').textContent = L.controls.after;
    $('lang-pt').setAttribute('aria-pressed', String(state.lang === 'pt'));
    $('lang-en').setAttribute('aria-pressed', String(state.lang === 'en'));
    $('cta-download').setAttribute('href', M.releases);
    ['a', 'b', 'c'].forEach((v) => $('proto-' + v).setAttribute('aria-pressed', String(state.variant === v)));
    renderDiagram();
  }

  // ---------- behaviour ----------
  function go(delta) { state.mode = 'story'; state.scene = (state.scene + delta + M.scenes.length) % M.scenes.length; renderDiagram(); }
  function play(on) {
    if (state.timer) { clearInterval(state.timer); state.timer = null; }
    if (on && !reduced) state.timer = setInterval(() => go(1), 6500);
    $('btn-play').textContent = state.timer ? t().controls.pause : t().controls.play;
  }
  function stopAndGo(delta) { play(false); go(delta); }
  $('btn-next').addEventListener('click', () => stopAndGo(1));
  $('btn-prev').addEventListener('click', () => stopAndGo(-1));
  $('btn-play').addEventListener('click', () => { const on = !state.timer; if (on && state.mode !== 'story') state.mode = 'story'; play(on); renderDiagram(); });
  $('btn-before').addEventListener('click', () => { play(false); state.mode = 'before'; renderDiagram(); });
  $('btn-after').addEventListener('click', () => { play(false); state.mode = 'after'; renderDiagram(); });
  $('cta-how').addEventListener('click', (e) => { e.preventDefault(); $('how').scrollIntoView({ behavior: reduced ? 'auto' : 'smooth' }); });
  const setLang = (l) => { state.lang = l; localStorage.setItem(LANG_KEY, l); render(); };
  $('lang-pt').addEventListener('click', () => setLang('pt'));
  $('lang-en').addEventListener('click', () => setLang('en'));
  ['a', 'b', 'c'].forEach((v) => $('proto-' + v).addEventListener('click', () => { state.variant = v; render(); }));
  $('proto-panel').hidden = !params.has('prototipo');

  render();
  play(!params.has('cena') && !params.has('modo'));
})();

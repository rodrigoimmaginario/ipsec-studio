// Shared by the three design concepts (CV3.DS5.US3, phase 1). Builds the same diagram as the site from SITE_MODEL,
// with one addition: a "focus" that lights one state at a time (refused, auth, enc, boundary) for scroll storytelling.
(function () {
  const M = window.SITE_MODEL;
  const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
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
    const until = (s) => { const lerp = (u, v) => [u[0] + (v[0] - u[0]) * s, u[1] + (v[1] - u[1]) * s]; const a1 = lerp(p0, p1), b1 = lerp(p1, p2), c1 = lerp(p2, p3), a2 = lerp(a1, b1), b2 = lerp(b1, c1), e = lerp(a2, b2); return path(p0, a1, a2, e); };
    return { d: path(p0, p1, p2, p3), at, until };
  }
  function badge(kind, [x, y]) {
    if (kind === 'refused') return `<g class="badge refused"><circle cx="${x}" cy="${y}" r="13"/><path d="M${x - 4.5} ${y - 4.5}l9 9M${x + 4.5} ${y - 4.5}l-9 9"/></g>`;
    if (kind === 'enc') return `<g class="badge enc"><circle cx="${x}" cy="${y}" r="14"/><rect x="${x - 5.5}" y="${y - 1}" width="11" height="8" rx="1.5"/><path d="M${x - 3.5} ${y - 1}v-3a3.5 3.5 0 0 1 7 0v3"/></g>`;
    if (kind === 'auth') return `<g class="badge auth"><circle cx="${x}" cy="${y}" r="13"/><path d="M${x - 5} ${y}l3.5 3.5 6.5-7"/></g>`;
    return '';
  }
  const ORDER = ['refused', 'auth', 'enc', 'boundary'];
  // opts: { lang, mode: 'after'|'before', reduced, focus: null|state, callouts: bool }
  function svg(opts = {}) {
    const L = window.SITE_I18N[opts.lang || 'pt'];
    const links = opts.mode === 'before' ? M.before : M.flows;
    const zones = M.zones.map((z) => `<g class="zone ${z.style}" data-zone="${z.id}"><rect x="${z.x}" y="${z.y}" width="${z.w}" height="${z.h}" rx="14"/><text x="${z.x + 14}" y="${z.y + 22}">${esc(L.zones[z.id])}</text></g>`).join('');
    const paths = links.map((l, i) => {
      const g = geometry(l); const t = l.at ?? (l.kind === 'refused' ? 0.84 : 0.5);
      const pos = g.at(t).map((v) => +v.toFixed(1)); const d = l.kind === 'refused' ? g.until(t) : g.d;
      const st = stateOf(l);
      const note = l.note && opts.mode !== 'before' ? `<text class="note ${l.kind}" x="${pos[0]}" y="${pos[1] - 20}" text-anchor="middle">${esc(L.notes[l.note])}</text>` : '';
      const callout = opts.callouts && opts.mode !== 'before' ? `<g class="callout"><circle cx="${pos[0] + 22}" cy="${pos[1] + 16}" r="10"/><text x="${pos[0] + 22}" y="${pos[1] + 20.5}" text-anchor="middle">${ORDER.indexOf(st) + 1}</text></g>` : '';
      return `<g class="link ${l.kind}" data-state="${st}" style="--i:${i}"><path class="track" d="${d}"/><path class="pulse" d="${d}" pathLength="100"/>${badge(l.kind, pos)}${note}${callout}</g>`;
    }).join('');
    const nodes = M.nodes.map((n) => `<g class="node${n.zone === 'outside' ? ' outsider' : ''}">${icon(n)}<text x="${n.x}" y="${n.y + 50}" text-anchor="middle">${String(L.nodes[n.id]).split('\n').map((p, k) => `<tspan x="${n.x}" dy="${k ? 18 : 0}">${esc(p)}</tspan>`).join('')}</text></g>`).join('');
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 960 540" role="img" aria-label="${esc(L.how.title)}" data-mode="${opts.mode || 'after'}" data-motion="${opts.reduced ? 'reduced' : 'full'}"${opts.focus ? ` data-focus="${opts.focus}"` : ''}>${zones}<g class="links">${paths}</g>${nodes}</svg>`;
  }
  // Where each app screen has the detail that matters (percent of the 1280x800 capture) and how much to zoom.
  const STEP_FOCUS = [
    { x: 62, y: 74, s: 1.55 },  // 1 inventory: computers and their profile
    { x: 56, y: 58, s: 1.45 },  // 2 profiles: profile cards
    { x: 60, y: 52, s: 1.3 },   // 3 simulate: the matrix
    { x: 84, y: 60, s: 1.85 },  // 4 deploy: approval note, apply and rollback
    { x: 57, y: 66, s: 1.4 },   // 5 verify: expected x observed, on the wire
  ];
  const reduced = () => !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  window.ConceptKit = { svg, ORDER, STEP_FOCUS, reduced, esc, L: (lang) => window.SITE_I18N[lang || 'pt'] };
})();

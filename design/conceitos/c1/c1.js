// Concept 1 — Guided narrative. Texts come from the approved site content (site/i18n.js, PT).
(function () {
  const K = window.ConceptKit, L = K.L('pt'), M = window.SITE_MODEL, esc = K.esc, $ = (id) => document.getElementById(id);
  const reduced = K.reduced();

  // ---------- texts ----------
  $('h1').textContent = L.variants.a.title;
  $('sub').textContent = L.variants.a.subtitle;
  $('cta-how').textContent = L.cta.how;
  $('cta-dl').textContent = L.cta.download; $('cta-dl').href = M.releases; $('bar-download').href = M.releases;
  $('facts').innerHTML = L.facts.map((f) => `<li>${esc(f)}</li>`).join('');
  $('steps-eyebrow').textContent = L.steps.eyebrow; $('steps-title').textContent = L.steps.title; $('steps-intro').textContent = L.steps.intro;
  $('sample').textContent = L.steps.sample;

  // ---------- abstract art: a constellation of machines; the one from outside fades at its X ----------
  function rng(seed) { let s = seed; return () => ((s = (s * 1664525 + 1013904223) % 4294967296) / 4294967296); }
  function constellation() {
    const r = rng(7), W = 1600, H = 1000, pts = [];
    for (let i = 0; i < 54; i++) pts.push({ x: 780 + r() * 790, y: 50 + r() * 900, big: r() < 0.16 });
    const edges = new Set(); const E = [];
    pts.forEach((p, i) => pts.map((q, j) => [j, Math.hypot(p.x - q.x, p.y - q.y)]).filter(([j]) => j !== i).sort((a, b) => a[1] - b[1]).slice(0, 2)
      .forEach(([j]) => { const k = i < j ? `${i}-${j}` : `${j}-${i}`; if (!edges.has(k)) { edges.add(k); E.push([i, j]); } }));
    const line = ([i, j], cls) => `<line class="${cls}" x1="${pts[i].x.toFixed(0)}" y1="${pts[i].y.toFixed(0)}" x2="${pts[j].x.toFixed(0)}" y2="${pts[j].y.toFixed(0)}"/>`;
    const lines = E.map((e, n) => line(e, n % 9 === 4 ? 'e enc' : 'e')).join('');
    const sigs = E.filter((_, n) => n % 6 === 1).map(([i, j], n) => `<path class="sig" pathLength="100" style="animation-delay:${(n * 0.7).toFixed(1)}s" d="M${pts[i].x.toFixed(0)} ${pts[i].y.toFixed(0)}L${pts[j].x.toFixed(0)} ${pts[j].y.toFixed(0)}"/>`).join('');
    const locks = E.filter((_, n) => n % 9 === 4).map(([i, j]) => { const x = (pts[i].x + pts[j].x) / 2, y = (pts[i].y + pts[j].y) / 2; return `<circle class="lock" cx="${x.toFixed(0)}" cy="${y.toFixed(0)}" r="5"/>`; }).join('');
    const nodes = pts.map((p) => `<g class="n" style="--dx:${(r() * 12 - 6).toFixed(1)}px;--dy:${(r() * 12 - 6).toFixed(1)}px;animation-delay:-${(r() * 9).toFixed(1)}s">${p.big ? `<circle class="ring" cx="${p.x.toFixed(0)}" cy="${p.y.toFixed(0)}" r="11"/>` : ''}<circle class="dot" cx="${p.x.toFixed(0)}" cy="${p.y.toFixed(0)}" r="${p.big ? 4.5 : 3}"/></g>`).join('');
    const near = pts.reduce((b, p) => (Math.hypot(p.x - 640, p.y - 930) < Math.hypot(b.x - 640, b.y - 930) ? p : b));
    const sx = 640 + (near.x - 640) * 0.62, sy = 930 + (near.y - 930) * 0.62;
    const outsider = `<g class="gone"><line class="out" x1="640" y1="930" x2="${sx.toFixed(0)}" y2="${sy.toFixed(0)}"/><circle class="outsider" cx="640" cy="930" r="9"/><g transform="translate(${sx.toFixed(0)} ${sy.toFixed(0)})"><circle r="11" class="x"/><path class="xm" d="M-4 -4l8 8M4 -4l-8 8"/></g></g>`;
    return `<svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="xMidYMid slice"><style>
      .e{stroke:rgba(91,155,224,.28);stroke-width:1.2}.e.enc{stroke:rgba(224,112,122,.5)}.sig{fill:none;stroke:#8cc0ff;stroke-width:2.4;stroke-linecap:round}
      .dot{fill:#a9c3e4}.ring{fill:none;stroke:rgba(169,195,228,.45);stroke-width:1.2}.lock{fill:#e0707a}
      .out{stroke:#c9a25a;stroke-width:1.6;stroke-dasharray:3 6}.outsider{fill:none;stroke:#c9a25a;stroke-width:2;stroke-dasharray:3 3}.x{fill:#2a303c;stroke:#6f7888}.xm{stroke:#fff;stroke-width:2;stroke-linecap:round}
    </style>${lines}${sigs}${locks}${nodes}${outsider}</svg>`;
  }
  $('sky').innerHTML = constellation();

  // ---------- abstract people: whoever runs the network, with the questions around ----------
  $('people').innerHTML = `<svg viewBox="0 0 420 380" fill="none" stroke-linecap="round">
    <g stroke="#2b4670" stroke-width="1.2">${[[60, 70], [150, 40], [300, 52], [370, 120], [40, 190], [380, 230]].map(([x, y]) => `<line x1="210" y1="210" x2="${x}" y2="${y}"/><circle cx="${x}" cy="${y}" r="4" fill="#2b4670"/>`).join('')}</g>
    <g opacity=".45" stroke="#95a8c0" stroke-width="2"><circle cx="105" cy="250" r="26"/><path d="M55 345c4-40 26-62 50-62s46 22 50 62"/><circle cx="318" cy="252" r="26"/><path d="M268 345c4-40 26-62 50-62s46 22 50 62"/></g>
    <g stroke="#eaf0f8" stroke-width="2.4"><circle cx="210" cy="200" r="36"/><path d="M140 352c6-58 36-90 70-90s64 32 70 90"/></g>
    <g font-family="Segoe UI, sans-serif" font-weight="700" text-anchor="middle">
      <g transform="translate(150 105)"><rect x="-24" y="-24" width="48" height="40" rx="12" stroke="#5b9be0" stroke-width="2"/><text y="4" fill="#5b9be0" stroke="none" font-size="20">?</text></g>
      <g transform="translate(268 82)"><rect x="-24" y="-24" width="48" height="40" rx="12" stroke="#e0707a" stroke-width="2"/><text y="4" fill="#e0707a" stroke="none" font-size="20">?</text></g>
      <g transform="translate(330 165)"><rect x="-24" y="-24" width="48" height="40" rx="12" stroke="#c9a25a" stroke-width="2"/><text y="5" fill="#c9a25a" stroke="none" font-size="20">↺</text></g>
    </g></svg>`;

  // ---------- scroll story ----------
  const scenes = [{ focus: null, num: '', title: L.how.title, text: L.how.intro, big: true }]
    .concat(K.ORDER.map((k, i) => ({ focus: k, num: `0${i + 1}`, title: L.states[k].title, text: L.states[k].text })))
    .concat([{ focus: null, num: '05', title: 'Tudo ao mesmo tempo', text: L.alsoNote }]);
  $('scenes').innerHTML = scenes.map((s, i) => `<article class="scene" data-i="${i}" data-state="${s.focus || ''}">${s.num ? `<span class="num">${s.num}</span>` : `<p class="kicker">${esc(L.how.eyebrow)}</p>`}${s.big ? `<h2>${esc(s.title)}</h2>` : `<h3>${esc(s.title)}</h3>`}<p>${esc(s.text)}</p>${s.focus ? `<span class="tag"><i></i>${esc(L.states[s.focus].title)}</span>` : ''}</article>`).join('');
  $('diagram').innerHTML = K.svg({ lang: 'pt', reduced });
  const svg = $('diagram').querySelector('svg');
  const focus = (f) => (f ? svg.setAttribute('data-focus', f) : svg.removeAttribute('data-focus'));

  // ---------- steps ----------
  $('step-texts').innerHTML = L.steps.items.map((s, i) => `<li data-i="${i}"><span class="num">0${i + 1}</span><h3>${esc(s.title)}</h3><p>${esc(s.text)}</p></li>`).join('');
  $('shots').innerHTML = L.steps.items.map((s, i) => { const f = K.STEP_FOCUS[i]; return `<img src="../../../site/img/passo-${i + 1}-pt.png" alt="${esc(s.alt)}" style="--s:${f.s};transform-origin:${f.x}% ${f.y}%"${i === 0 ? ' class="on"' : ''}>`; }).join('');
  const shots = [...$('shots').querySelectorAll('img')];

  // ---------- observers: whatever crosses the middle of the screen is "on" ----------
  const middle = (els, on) => { const io = new IntersectionObserver((es) => es.forEach((e) => e.isIntersecting && on(e.target)), { rootMargin: '-48% 0px -48% 0px' }); els.forEach((el) => io.observe(el)); };
  const sceneEls = [...document.querySelectorAll('.scene')];
  middle(sceneEls, (el) => { sceneEls.forEach((x) => x.classList.toggle('on', x === el)); focus(el.dataset.state || null); });
  const stepEls = [...document.querySelectorAll('.step-texts li')];
  middle(stepEls, (el) => { stepEls.forEach((x) => x.classList.toggle('on', x === el)); shots.forEach((img, i) => img.classList.toggle('on', i === +el.dataset.i)); });
  const chapters = [...document.querySelectorAll('.chapters a')];
  middle([...document.querySelectorAll('[data-chapter]')], (el) => chapters.forEach((a) => a.classList.toggle('on', a.dataset.ch === el.id)));

  // ---------- progress and the header ----------
  let last = 0;
  addEventListener('scroll', () => {
    const y = scrollY, max = document.documentElement.scrollHeight - innerHeight;
    $('progress').style.width = `${(100 * y / max).toFixed(2)}%`;
    $('bar').classList.toggle('solid', y > 40);
    $('bar').classList.toggle('away', y > last && y > 300);
    last = y;
  }, { passive: true });
})();

// Concept 2 — Product first. Texts come from the approved site content (site/i18n.js, PT).
(function () {
  const K = window.ConceptKit, L = K.L('pt'), M = window.SITE_MODEL, esc = K.esc, $ = (id) => document.getElementById(id);
  const reduced = K.reduced();
  $('h1').textContent = L.variants.a.title; $('sub').textContent = L.variants.a.subtitle;
  $('cta-dl').textContent = L.cta.download; $('cta-dl').href = M.releases; $('top-dl').href = M.releases;
  $('cta-how').textContent = L.cta.how;
  $('facts').innerHTML = L.facts.map((f) => `<li>${esc(f)}</li>`).join('');
  $('how-eyebrow').textContent = L.how.eyebrow; $('how-title').textContent = L.how.title; $('how-intro').textContent = L.how.intro;
  $('steps-eyebrow').textContent = L.steps.eyebrow; $('steps-title').textContent = L.steps.title; $('steps-intro').textContent = L.steps.intro; $('sample').textContent = L.steps.sample;

  // ---------- diagram with tabs ----------
  const tabs = [['all', 'Tudo']].concat(K.ORDER.map((k) => [k, L.states[k].title]));
  $('tabs').innerHTML = tabs.map(([k, t]) => `<button class="tab" role="tab" data-tab="${k}" aria-selected="${k === 'all'}"><i></i>${esc(t)}</button>`).join('');
  $('diagram').innerHTML = K.svg({ lang: 'pt', reduced });
  const svg = $('diagram').querySelector('svg');
  const explain = (k) => { $('explain').innerHTML = k === 'all'
    ? `<span class="k">Quatro estados ao mesmo tempo</span><h3>${esc(L.how.title)}</h3><p>${esc(L.alsoNote)}</p>`
    : `<span class="k">Estado ${K.ORDER.indexOf(k) + 1} de 4</span><h3>${esc(L.states[k].title)}</h3><p>${esc(L.states[k].text)}</p>`; };
  $('tabs').addEventListener('click', (e) => { const b = e.target.closest('[data-tab]'); if (!b) return; const k = b.dataset.tab;
    $('tabs').querySelectorAll('[data-tab]').forEach((x) => x.setAttribute('aria-selected', String(x === b)));
    k === 'all' ? svg.removeAttribute('data-focus') : svg.setAttribute('data-focus', k); explain(k); });
  explain('all');

  // ---------- steps ----------
  const items = L.steps.items; let cur = 0;
  $('stepper').innerHTML = items.map((s, i) => `<li><button role="tab" data-go="${i}" aria-selected="${i === 0}"><span>PASSO ${i + 1}</span>${esc(s.title)}</button></li>`).join('');
  $('zoom').innerHTML = items.map((s, i) => { const f = K.STEP_FOCUS[i]; return `<img src="../../../site/img/passo-${i + 1}-pt.png" alt="${esc(s.alt)}" style="--s:${f.s};transform-origin:${f.x}% ${f.y}%"${i === 0 ? ' class="on"' : ''}>`; }).join('');
  const show = (i) => { cur = (i + items.length) % items.length;
    $('stepper').querySelectorAll('[data-go]').forEach((b) => b.setAttribute('aria-selected', String(+b.dataset.go === cur)));
    $('zoom').querySelectorAll('img').forEach((img, k) => img.classList.toggle('on', k === cur));
    $('step-copy').innerHTML = `<div class="n">${cur + 1}</div><h3>${esc(items[cur].title)}</h3><p>${esc(items[cur].text)}</p>`;
    $('step-label').textContent = `IPsec Studio — ${items[cur].title}`;
    const b = $('stepper').querySelector(`[data-go="${cur}"]`); const s = $('stepper'); if (s.scrollWidth > s.clientWidth) s.scrollTo({ left: b.parentElement.offsetLeft - 18, behavior: reduced ? 'auto' : 'smooth' }); };
  $('stepper').addEventListener('click', (e) => { const b = e.target.closest('[data-go]'); if (b) show(+b.dataset.go); });
  $('prev').addEventListener('click', () => show(cur - 1)); $('next').addEventListener('click', () => show(cur + 1));
  show(0);

  // ---------- header: current section, hide on the way down, menu on small screens ----------
  const links = [...document.querySelectorAll('#links a')];
  const io = new IntersectionObserver((es) => es.forEach((e) => { if (e.isIntersecting) links.forEach((a) => a.classList.toggle('on', a.getAttribute('href') === '#' + e.target.id)); }), { rootMargin: '-45% 0px -50% 0px' });
  ['como', 'passos', 'prova', 'origem'].forEach((id) => io.observe($(id)));
  let last = 0;
  addEventListener('scroll', () => { const y = scrollY; $('top').classList.toggle('lined', y > 10); $('top').classList.toggle('away', y > last && y > 400 && $('sheet').hidden); last = y; }, { passive: true });
  $('menu').addEventListener('click', () => { const open = $('sheet').hidden; $('sheet').hidden = !open; $('menu').setAttribute('aria-expanded', String(open)); });
  $('sheet').addEventListener('click', (e) => { if (e.target.closest('a')) { $('sheet').hidden = true; $('menu').setAttribute('aria-expanded', 'false'); } });
})();

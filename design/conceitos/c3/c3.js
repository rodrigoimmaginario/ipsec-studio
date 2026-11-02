// Concept 3 — Field manual. Texts come from the approved site content (site/i18n.js, PT).
(function () {
  const K = window.ConceptKit, L = K.L('pt'), M = window.SITE_MODEL, esc = K.esc, $ = (id) => document.getElementById(id);
  const reduced = K.reduced();
  $('h1').textContent = L.variants.a.title; $('sub').textContent = L.variants.a.subtitle;
  $('cta-dl').textContent = L.cta.download; $('cta-dl').href = M.releases; $('toc-dl').href = M.releases; $('cta-how').textContent = L.cta.how;
  $('facts').innerHTML = L.facts.map((f) => `<li>${esc(f)}</li>`).join('');
  $('how-eyebrow').textContent = L.how.eyebrow; $('how-title').textContent = L.how.title; $('how-intro').textContent = L.how.intro; $('also').textContent = L.alsoNote;
  $('steps-eyebrow').textContent = L.steps.eyebrow; $('steps-title').textContent = L.steps.title; $('steps-intro').textContent = L.steps.intro; $('sample').textContent = L.steps.sample;

  // ---------- figures ----------
  $('fig0').innerHTML = K.svg({ lang: 'pt', reduced: true });
  $('fig1').innerHTML = K.svg({ lang: 'pt', reduced, callouts: true });
  const svg = $('fig1').querySelector('svg');
  $('legend').innerHTML = K.ORDER.map((k, i) => `<li tabindex="0" data-state="${k}"><b><i>${i + 1}</i>${esc(L.states[k].title)}</b><p>${esc(L.states[k].text)}</p></li>`).join('');
  const lis = [...$('legend').querySelectorAll('li')];
  const focus = (li) => { lis.forEach((x) => x.classList.toggle('on', x === li)); li ? svg.setAttribute('data-focus', li.dataset.state) : svg.removeAttribute('data-focus'); };
  lis.forEach((li) => { li.addEventListener('mouseenter', () => focus(li)); li.addEventListener('focus', () => focus(li)); li.addEventListener('click', () => focus(li)); });
  $('legend').addEventListener('mouseleave', () => focus(null));

  // ---------- steps: crops enlarged on the detail that matters ----------
  $('steps').innerHTML = L.steps.items.map((s, i) => { const f = K.STEP_FOCUS[i];
    return `<div class="step" data-i="${i}"><div><span class="mono n">3.${i + 1}</span><h3>${esc(s.title)}</h3><p>${esc(s.text)}</p></div>
      <figure class="fig"><div class="crop" role="img" aria-label="${esc(s.alt)}" style="background-image:url('../../../site/img/passo-${i + 1}-pt.png');background-size:${(f.s * 100).toFixed(0)}% auto;background-position:${f.x}% ${f.y}%;--bx:${Math.min(48, Math.max(2, f.x - 25))}%;--by:${Math.min(48, Math.max(2, f.y - 25))}%"></div>
      <figcaption><span class="mono">Fig. 3.${i + 1}</span>${esc(s.title)} — recorte ampliado da tela.</figcaption></figure></div>`; }).join('');
  const reveal = new IntersectionObserver((es) => es.forEach((e) => e.isIntersecting && e.target.classList.add('in')), { rootMargin: '0px 0px -12% 0px' });
  document.querySelectorAll('.step').forEach((el) => reveal.observe(el));

  // ---------- table of contents: current section and reading rail ----------
  const links = [...document.querySelectorAll('#toc-list a')];
  const io = new IntersectionObserver((es) => es.forEach((e) => { if (e.isIntersecting) links.forEach((a) => a.classList.toggle('on', a.dataset.s === e.target.id)); }), { rootMargin: '-40% 0px -55% 0px' });
  ['abertura', 'como', 'notas', 'passos'].forEach((id) => io.observe($(id)));
  const rail = () => { const max = document.documentElement.scrollHeight - innerHeight; const p = Math.min(1, scrollY / max);
    const r = $('rail'); if (innerWidth > 980) { r.style.height = `${(p * 100).toFixed(1)}%`; r.style.width = ''; } else { r.style.width = `${(p * 100).toFixed(1)}%`; r.style.height = ''; } };
  addEventListener('scroll', rail, { passive: true }); addEventListener('resize', rail); rail();
  $('toc-toggle').addEventListener('click', () => { const open = !$('toc').classList.contains('open'); $('toc').classList.toggle('open', open); $('toc-toggle').setAttribute('aria-expanded', String(open)); });
  $('toc-list').addEventListener('click', (e) => { if (e.target.closest('a')) { $('toc').classList.remove('open'); $('toc-toggle').setAttribute('aria-expanded', 'false'); } });
})();

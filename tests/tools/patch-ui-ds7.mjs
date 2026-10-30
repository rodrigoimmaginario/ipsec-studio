// One-off UI patch for CV4.DS7: settle countdown before probing, pending DCs, reach of other IPsec GPOs, replication wait setting.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (s, a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 80)); return s.replace(a, () => b); };

let i = read('app/IpsecStudio/wwwroot/i18n.js');
i = rep(i, `capture: 'Capturar pacotes nos destinos durante a sonda',`, `probeIn: 'Sondar em {0} s', settle: 'A política acabou de mudar. Aguarde cerca de 2 minutos antes de sondar: um par que caiu para claro durante a implantação pode continuar em claro por alguns minutos com um host de fronteira.', probeAnyway: 'Sondar assim mesmo', pendingDcs: 'Estes controladores de domínio ainda não receberam a mudança: {0}. Computadores que usam esses DCs podem continuar com a política antiga. Force a replicação (repadmin /syncall) ou atualize os hosts mais tarde.', capture: 'Capturar pacotes nos destinos durante a sonda',`);
i = rep(i, `capture: 'Capture packets on the responders during the probe',`, `probeIn: 'Probe in {0} s', settle: 'The policy just changed. Wait about 2 minutes before probing: a pair that fell back to clear during the rollout may stay clear for a few minutes with a boundary host.', probeAnyway: 'Probe anyway', pendingDcs: 'These domain controllers have not received the change yet: {0}. Computers using them may keep the old policy. Force replication (repadmin /syncall) or refresh the hosts later.', capture: 'Capture packets on the responders during the probe',`);
i = rep(i, `external: 'Outras GPOs com regras IPsec',`, `external: 'Outras GPOs com regras IPsec', reach: 'atinge {0} computador(es)', inPlan: 'no plano: {0}', notEvaluated: 'Alcance pelos vínculos ativos de domínio e OU; herança bloqueada, vínculo imposto e filtragem por grupo não são avaliados.', scan: '{1} de {0} GPOs do domínio abertas na busca',`);
i = rep(i, `external: 'Other GPOs with IPsec rules',`, `external: 'Other GPOs with IPsec rules', reach: 'reaches {0} computer(s)', inPlan: 'in the plan: {0}', notEvaluated: 'Reach from enabled domain and OU links; blocked inheritance, enforced links and security filtering are not evaluated.', scan: '{1} of {0} domain GPOs opened by the search',`);
i = rep(i, `exemptIcmp: 'Isentar ICMP',`, `exemptIcmp: 'Isentar ICMP', replWait: 'Espera de replicação entre DCs (s)',`);
i = rep(i, `exemptIcmp: 'Exempt ICMP',`, `exemptIcmp: 'Exempt ICMP', replWait: 'Replication wait between DCs (s)',`);
fs.writeFileSync('app/IpsecStudio/wwwroot/i18n.js', i);

let a = read('app/IpsecStudio/wwwroot/app.js');
a = rep(a, `drift: null, ackDrift: false, capture: false };`, `drift: null, ackDrift: false, capture: false, settleUntil: 0, pendingDcs: [] };
// After a policy change the probe waits ~2 minutes (CV4.DS7): Windows keeps a recent clear fallback with boundary hosts for a while.
const SETTLE_MS = 120000;
const settleLeft = () => Math.max(0, Math.ceil((state.settleUntil - Date.now()) / 1000));
function settle() { state.settleUntil = Date.now() + SETTLE_MS; clearInterval(settle.timer); settle.timer = setInterval(() => { const b = $('#probe-btn'); const left = settleLeft(); if (b) { b.disabled = left > 0 || state.busy; b.textContent = left > 0 ? t('dep.probeIn', left) : t('dep.probe'); } if (!left) { clearInterval(settle.timer); const n = $('#settle-note'); if (n) n.remove(); } }, 1000); }`);
a = rep(a, `exemptDomainControllers: true, exemptIcmp: true, requireOutbound: false,`, `exemptDomainControllers: true, exemptIcmp: true, requireOutbound: false, replicationWaitSeconds: 180,`);
a = rep(a, `data-field="settings.crypto.quickModeLifetimeMinutes" value="\${s.crypto.quickModeLifetimeMinutes}"></label>`, `data-field="settings.crypto.quickModeLifetimeMinutes" value="\${s.crypto.quickModeLifetimeMinutes}"></label><label>\${t('prof.replWait')} <input type="number" min="0" max="3600" data-field="settings.replicationWaitSeconds" value="\${s.replicationWaitSeconds ?? 180}"></label>`);
a = rep(a, `<button class="btn" data-action="probe" \${state.busy ? 'disabled' : ''}>\${t('dep.probe')}</button>`, `<button class="btn" id="probe-btn" data-action="probe" \${state.busy || settleLeft() ? 'disabled' : ''}>\${settleLeft() ? t('dep.probeIn', settleLeft()) : t('dep.probe')}</button>`);
a = rep(a, `<p class="hint">\${t('dep.help')}</p>`, `<p class="hint">\${t('dep.help')}</p>\${settleLeft() ? \`<p class="hint" id="settle-note">\${t('dep.settle')} <a href="#" data-action="probe-now">\${t('dep.probeAnyway')}</a></p>\` : ''}\${state.pendingDcs.length ? \`<div class="notice red" id="pending-dcs">\${html(t('dep.pendingDcs', state.pendingDcs.join(', ')))}</div>\` : ''}`);
a = rep(a, "\${state.drift.external.map((e) => `<span class=\"code\">\${html(e.name)}</span> · \${e.ruleCount} \${t('drift.rules')} · \${t('drift.links')}: \${html(e.links.join(', '))}`).join('<br>')}</p>",
  "\${state.drift.external.map((e) => `<span class=\"code\">\${html(e.name)}</span> · \${e.ruleCount} \${t('drift.rules')} · \${t('drift.links')}: \${html(e.links.join(', '))} · \${t('drift.reach', (e.computers || []).length)}\${(e.inPlan || []).length ? ` · <b class=\"o-blocked\">\${html(t('drift.inPlan', e.inPlan.join(', ')))}</b>` : ''}`).join('<br>')}<br><span class=\"tiny muted\">\${t('drift.notEvaluated')}</span></p>");
a = rep(a, `toast(r.succeeded ? t('dep.done', el.dataset.kind) : t('dep.failedSee', el.dataset.kind), 8000); });`, `state.pendingDcs = r.pendingDcs || []; if (r.succeeded) settle(); toast(r.succeeded ? t('dep.done', el.dataset.kind) : t('dep.failedSee', el.dataset.kind), 8000); });`);
a = rep(a, `state.hosts = await api('/api/hosts', { planId: state.saved.id, version: state.saved.version, action: el.dataset.kind });`, `state.hosts = await api('/api/hosts', { planId: state.saved.id, version: state.saved.version, action: el.dataset.kind }); if (el.dataset.kind === 'refresh') settle();`);
a = rep(a, `state.hosts = state.waveResult.verify; state.probe = state.waveResult.probe;`, `state.hosts = state.waveResult.verify; state.probe = state.waveResult.probe; state.pendingDcs = state.waveResult.pendingDcs || []; settle();`);
a = rep(a, `  if (a === 'probe') run(`, `  if (a === 'probe-now') { ev.preventDefault(); state.settleUntil = 0; }
  if (a === 'probe' || a === 'probe-now') run(`);
fs.writeFileSync('app/IpsecStudio/wwwroot/app.js', a);

let p = read('tests/browser/prototype-render.mjs');
p = rep(p, `assert.ok(main.innerHTML.includes('identidade pendente')`, `vm.runInContext("state.page = 'deploy'; state.settleUntil = Date.now() + 60000; state.pendingDcs = ['dc2.lab.ipsec.test']; state.drift = { clean: false, gpos: [], external: [{ name: 'Corp-Legacy', ruleCount: 1, links: ['DC=lab'], computers: ['db1', 'x'], inPlan: ['db1'] }], utc: '' }; render()", context);
assert.ok(main.innerHTML.includes('id="settle-note"') && main.innerHTML.includes('Sondar em') && main.innerHTML.includes('data-action="probe-now"'), 'settle countdown before probing');
assert.ok(main.innerHTML.includes('id="pending-dcs"') && main.innerHTML.includes('dc2.lab.ipsec.test'), 'pending DCs notice');
assert.ok(main.innerHTML.includes('atinge 2 computador(es)') && main.innerHTML.includes('no plano: db1'), 'reach of other IPsec GPOs');
vm.runInContext("state.settleUntil = 0; state.pendingDcs = []; state.drift = null; render()", context);
assert.ok(main.innerHTML.includes('identidade pendente')`);
fs.writeFileSync('tests/browser/prototype-render.mjs', p);
console.log('patched');

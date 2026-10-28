// One-off UI patch for CV4.DS3: drift panel and acknowledgement. Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (s, a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 70)); return s.replace(a, b); };

let i = read('app/IpsecStudio/wwwroot/i18n.js');
i = rep(i, `    approval: { label: 'Nota de aprovação (obrigatória para aplicar)',`, `    drift: { title: 'Drift', check: 'Verificar drift', clean: 'Sem drift: GPOs do Studio iguais à compilação e nenhuma GPO externa com regras IPsec.', dirty: 'Drift detectado. Aplicar e reverter ficam bloqueados até você reconhecer.', external: 'GPOs externas com regras IPsec', links: 'vínculos', rules: 'regra(s)', acknowledge: 'Reconheço o drift acima e quero continuar (fica registrado com a minha identidade).', help: 'Drift = alguém alterou uma GPO do Studio por fora, ou existe outra GPO com regras de segurança de conexão no escopo. O Studio não corrige: mostra e exige reconhecimento.' },
    approval: { label: 'Nota de aprovação (obrigatória para aplicar)',`);
i = rep(i, `    approval: { label: 'Approval note (required to apply)',`, `    drift: { title: 'Drift', check: 'Check drift', clean: 'No drift: Studio GPOs match the compilation and no external GPO carries IPsec rules.', dirty: 'Drift detected. Apply and roll back are blocked until you acknowledge it.', external: 'External GPOs with IPsec rules', links: 'links', rules: 'rule(s)', acknowledge: 'I acknowledge the drift above and want to continue (recorded with my identity).', help: 'Drift = someone changed a Studio GPO outside the Studio, or another GPO with connection security rules exists in scope. The Studio does not fix it: it shows it and requires acknowledgement.' },
    approval: { label: 'Approval note (required to apply)',`);
fs.writeFileSync('app/IpsecStudio/wwwroot/i18n.js', i);

let a = read('app/IpsecStudio/wwwroot/app.js');
a = rep(a, `page: 'inventory', filter: '', busy: false, wave: 0, waveResult: null, approval: '' };`, `page: 'inventory', filter: '', busy: false, wave: 0, waveResult: null, approval: '', drift: null, ackDrift: false };`);
a = rep(a, `  const groupsCard = saved && d.newGroups.length ?`, `  const driftCard = saved && st.deploymentEnabled ? \`<div class="card pad mb"><h2>\${t('drift.title')}</h2><p class="hint">\${t('drift.help')}</p><button class="btn" data-action="drift" \${state.busy ? 'disabled' : ''}>\${t('drift.check')}</button>\${state.drift ? \`<div class="notice \${state.drift.clean ? '' : 'red'}" style="margin-top:12px">\${state.drift.clean ? t('drift.clean') : t('drift.dirty')}</div>\${state.drift.gpos.filter((g) => g.differences.length).map((g) => \`<p class="small"><b class="code">\${html(g.name)}</b><br>\${g.differences.map(html).join('<br>')}</p>\`).join('')}\${state.drift.external.length ? \`<p class="small"><b>\${t('drift.external')}</b><br>\${state.drift.external.map((e) => \`<span class="code">\${html(e.name)}</span> · \${e.ruleCount} \${t('drift.rules')} · \${t('drift.links')}: \${html(e.links.join(', '))}\`).join('<br>')}</p>\` : ''}\${state.drift.clean ? '' : \`<label class="check"><input type="checkbox" id="ack-drift" \${state.ackDrift ? 'checked' : ''}><span>\${t('drift.acknowledge')}</span></label>\`}\` : ''}</div>\` : '';
  const groupsCard = saved && d.newGroups.length ?`);
a = rep(a, `  \${groupsCard}\${waves}\${hosts}\${probe}`, `  \${driftCard}\${groupsCard}\${waves}\${hosts}\${probe}`);
a = rep(a, `  const approval = $('#approval'); if (approval) approval.addEventListener('input', () => { state.approval = approval.value; });`,
`  const approval = $('#approval'); if (approval) approval.addEventListener('input', () => { state.approval = approval.value; });
  const ackDrift = $('#ack-drift'); if (ackDrift) ackDrift.addEventListener('change', () => { state.ackDrift = ackDrift.checked; });`);
a = rep(a, `  if (a === 'renew-host')`, `  if (a === 'drift') run(t('drift.check'), async () => { state.drift = await api(\`/api/plans/\${state.saved.id}/drift\`); state.ackDrift = false; });
  if (a === 'renew-host')`);
a = rep(a, `const r = await api('/api/deploy', { planId: state.saved.id, version: state.saved.version, hash: state.compilation.sha256, action: el.dataset.kind, approval: state.approval });`,
`const r = await api('/api/deploy', { planId: state.saved.id, version: state.saved.version, hash: state.compilation.sha256, action: el.dataset.kind, approval: state.approval, acknowledgeDrift: state.ackDrift }); state.drift = null; state.ackDrift = false;`);
a = rep(a, `<td>\${r.succeeded ? \`<span class="badge teal">\${t('dep.ok')}</span>\` : \`<span class="badge red">\${t('dep.failed')}</span>\`}</td>`, `<td>\${r.succeeded ? \`<span class="badge \${r.action === 'drift-acknowledged' ? 'amber' : 'teal'}">\${t('dep.ok')}</span>\` : \`<span class="badge red">\${t('dep.failed')}</span>\`}</td>`);
fs.writeFileSync('app/IpsecStudio/wwwroot/app.js', a);

let p = read('tests/browser/prototype-render.mjs');
p = rep(p, `: url.endsWith('/probe') ?`, `: url.endsWith('/drift') ? { clean: false, gpos: [{ name: 'IpsecStudio-Base', exists: true, differences: ['IpsecStudio-Main: inbound esperado Require, encontrado Request'] }], external: [{ name: 'Corp-Legacy', ruleCount: 1, links: ['DC=test'] }], utc: new Date().toISOString() } : url.endsWith('/probe') ?`);
p = rep(p, `assert.ok(main.innerHTML.includes('identidade pendente')`, `vm.runInContext("state.drift = routes_drift; render()".replace('routes_drift', JSON.stringify(routes('/x/drift'))), context);
assert.ok(main.innerHTML.includes('Drift detectado') && main.innerHTML.includes('Corp-Legacy') && main.innerHTML.includes('id="ack-drift"') && main.innerHTML.includes('encontrado Request'), 'drift panel with acknowledgement');
assert.ok(main.innerHTML.includes('identidade pendente')`);
fs.writeFileSync('tests/browser/prototype-render.mjs', p);
console.log('patched');

// One-off UI patch for CV4.DS2: identity state per host and per-host renew. Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (s, a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 70)); return s.replace(a, b); };

let i = read('app/IpsecStudio/wwwroot/i18n.js');
i = rep(i, `    approval: { label: 'Nota de aprovação (obrigatória para aplicar)',`, `    identity: { synced: 'identidade sincronizada', pending: 'identidade pendente', unknown: 'identidade desconhecida', tgt: 'ticket de máquina', change: 'última mudança de grupo', renew: 'Renovar', help: 'A identidade que o IPsec apresenta vem do ticket Kerberos da máquina. Se o ticket é anterior à última mudança de um grupo de perfil, o host ainda negocia com os grupos antigos: renove (purge do ticket, gpupdate, limpeza de SAs) ou aguarde o reinício.' },
    approval: { label: 'Nota de aprovação (obrigatória para aplicar)',`);
i = rep(i, `    approval: { label: 'Approval note (required to apply)',`, `    identity: { synced: 'identity synced', pending: 'identity pending', unknown: 'identity unknown', tgt: 'machine ticket', change: 'last group change', renew: 'Renew', help: 'The identity IPsec presents comes from the machine Kerberos ticket. If the ticket predates the last change of a profile group, the host still negotiates with the old groups: renew (ticket purge, gpupdate, SA cleanup) or wait for a reboot.' },
    approval: { label: 'Approval note (required to apply)',`);
fs.writeFileSync('app/IpsecStudio/wwwroot/i18n.js', i);

let a = read('app/IpsecStudio/wwwroot/app.js');
a = rep(a, `<td class="small">\${o ? (o.studioRules ? \`\${o.healthy ? \`<span class="badge teal">\${t('dep.rulesOk')}</span>\` : \`<span class="badge red">\${html(t('dep.ruleError', o.studioRules.filter((r) => r.status !== 'OK').map((r) => r.statusText).join('; ') || t('dep.noRule')))}</span>\`} · \${html(t('dep.rules', o.studioRules.length, (o.gpos || []).join(', ') || t('dep.none'), o.enabledIpsecRules))} <span class="code">\${html(o.authorizationList || '—')}</span>\` : html(t('dep.refreshed', o.refreshedUtc, o.gpupdateExit))) : html(h.output)}</td></tr>\`; }).join('')}</tbody></table></div></div>\` : '';`,
`<td class="small">\${o ? (o.studioRules ? \`\${o.healthy ? \`<span class="badge teal">\${t('dep.rulesOk')}</span>\` : \`<span class="badge red">\${html(t('dep.ruleError', o.studioRules.filter((r) => r.status !== 'OK').map((r) => r.statusText).join('; ') || t('dep.noRule')))}</span>\`} \${identityBadge(h)} · \${html(t('dep.rules', o.studioRules.length, (o.gpos || []).join(', ') || t('dep.none'), o.enabledIpsecRules))} <span class="code">\${html(o.authorizationList || '—')}</span>\` : html(t('dep.refreshed', o.refreshedUtc, o.gpupdateExit))) : html(h.output)}</td><td>\${h.reachable && h.identityState ? \`<button class="btn" data-action="renew-host" data-host="\${html(h.host)}" \${state.busy ? 'disabled' : ''}>\${t('identity.renew')}</button>\` : ''}</td></tr>\`; }).join('')}</tbody></table></div><p class="hint">\${t('identity.help')}</p></div>\` : '';`);
a = rep(a, `<thead><tr><th>\${t('dep.host')}</th><th>\${t('dep.reachable')}</th><th>\${t('dep.result')}</th></tr></thead>`, `<thead><tr><th>\${t('dep.host')}</th><th>\${t('dep.reachable')}</th><th>\${t('dep.result')}</th><th></th></tr></thead>`);
a = rep(a, `function savedScreen() {`, `function identityBadge(h) {
  if (!h.identityState) return '';
  const cls = h.identityState === 'synced' ? 'teal' : h.identityState === 'pending' ? 'red' : 'amber';
  const detail = h.tgtStartUtc || h.lastGroupChangeUtc ? \` title="\${html(t('identity.tgt'))}: \${h.tgtStartUtc ? html(fmtDate(h.tgtStartUtc)) : '—'} · \${html(t('identity.change'))}: \${h.lastGroupChangeUtc ? html(fmtDate(h.lastGroupChangeUtc)) : '—'}"\` : '';
  return \`<span class="badge \${cls}"\${detail}>\${t('identity.' + h.identityState)}</span>\`;
}
function savedScreen() {`);
a = rep(a, `  if (a === 'probe') run(t('actions.probe'),`, `  if (a === 'renew-host') run(t('identity.renew'), async () => { const short = el.dataset.host.split('.')[0]; const id = computers().find((c) => c.name.toLowerCase() === short.toLowerCase())?.id; if (!id) throw new Error(el.dataset.host); await api('/api/hosts', { planId: state.saved.id, version: state.saved.version, action: 'refresh', computerIds: [id] }); const fresh = await api('/api/hosts', { planId: state.saved.id, version: state.saved.version, action: 'verify', computerIds: [id] }); state.hosts = (state.hosts || []).map((h) => fresh.find((x) => x.host === h.host) || h); state.log = await api(\`/api/plans/\${state.saved.id}/deployments\`); });
  if (a === 'probe') run(t('actions.probe'),`);
fs.writeFileSync('app/IpsecStudio/wwwroot/app.js', a);

let p = read('tests/browser/prototype-render.mjs');
p = rep(p, `vm.runInContext("state.saved={...saved, input: state.draft}; state.compilation = compilation; state.probe = routes_probe; state.page='deploy'; render()"`,
`vm.runInContext("state.hosts=[{host:'app1.lab.ipsec.test',reachable:true,output:JSON.stringify({healthy:true,studioRules:[],gpos:['IpsecStudio-Base'],enabledIpsecRules:2}),identityState:'pending',tgtStartUtc:'2026-10-28T10:00:00Z',lastGroupChangeUtc:'2026-10-28T11:00:00Z'}]", context);
vm.runInContext("state.saved={...saved, input: state.draft}; state.compilation = compilation; state.probe = routes_probe; state.page='deploy'; render()"`);
p = rep(p, `assert.ok(main.innerHTML.includes('id="approval"')`, `assert.ok(main.innerHTML.includes('identidade pendente') && main.innerHTML.includes('data-action="renew-host"'), 'identity badge and per-host renew');
assert.ok(main.innerHTML.includes('id="approval"')`);
fs.writeFileSync('tests/browser/prototype-render.mjs', p);
console.log('patched');

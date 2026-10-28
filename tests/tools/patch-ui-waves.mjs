// One-off UI patch for CV4.DS1: waves editor, per-wave simulation, group preparation, approval note, wave apply/rollback. Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (s, a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 70)); return s.replace(a, b); };

// ---- i18n ----
let i = read('app/IpsecStudio/wwwroot/i18n.js');
i = rep(i, `    sim: { eyebrow: '03 / SIMULAR',`, `    waves: { title: 'Ondas', hint: 'Cada onda adiciona computadores aos grupos de perfil, na ordem. Aplicar uma onda grava a associação no AD com a sua identidade e sonda só os hosts dela.', add: 'Adicionar onda', none: 'Nenhuma onda: a associação dos grupos é a do inventário.', editNew: 'Nova onda', edit: 'Editar onda', name: 'Nome da onda', group: 'Grupo de perfil', computers: 'Computadores (Ctrl para vários)', addMember: 'Adicionar grupo à onda', members: '{0} computador(es) em {1} grupo(s)', after: 'Após a onda', inventory: 'Associação do inventário', applied: 'aplicada', failed: 'falhou', rolledBack: 'revertida', notApplied: 'não aplicada', apply: 'Aplicar onda', rollback: 'Reverter onda', blocked: 'bloqueada pela onda anterior', needGroup: 'Escolha o grupo e ao menos um computador.' },
    groups: { title: 'Grupos novos', hint: 'Criados pelo Studio no contêiner escolhido; depois releia o inventário para usá-los nos perfis.', add: 'Adicionar grupo', none: 'Nenhum grupo novo.', name: 'Nome do grupo', description: 'Descrição', container: 'Contêiner (DN) — vazio = CN=Users do domínio', prepare: 'Preparar grupos no AD', prepared: 'Grupos preparados; releia o inventário.' },
    approval: { label: 'Nota de aprovação (obrigatória para aplicar)', placeholder: 'Ex.: Aprovado na mudança CHG-123 por …', missing: 'Escreva a nota de aprovação.' },
    sim: { eyebrow: '03 / SIMULAR',`);
i = rep(i, `    sim: { eyebrow: '03 / SIMULATE',`, `    waves: { title: 'Waves', hint: 'Each wave adds computers to profile groups, in order. Applying a wave writes the membership to AD with your identity and probes only its hosts.', add: 'Add wave', none: 'No waves: group membership is the inventory membership.', editNew: 'New wave', edit: 'Edit wave', name: 'Wave name', group: 'Profile group', computers: 'Computers (Ctrl for several)', addMember: 'Add group to wave', members: '{0} computer(s) in {1} group(s)', after: 'After wave', inventory: 'Inventory membership', applied: 'applied', failed: 'failed', rolledBack: 'rolled back', notApplied: 'not applied', apply: 'Apply wave', rollback: 'Roll back wave', blocked: 'blocked by the previous wave', needGroup: 'Choose the group and at least one computer.' },
    groups: { title: 'New groups', hint: 'Created by the Studio in the chosen container; then read the inventory again to use them in profiles.', add: 'Add group', none: 'No new group.', name: 'Group name', description: 'Description', container: 'Container (DN) — empty = the domain CN=Users', prepare: 'Prepare groups in AD', prepared: 'Groups prepared; read the inventory again.' },
    approval: { label: 'Approval note (required to apply)', placeholder: 'E.g. Approved in change CHG-123 by …', missing: 'Write the approval note.' },
    sim: { eyebrow: '03 / SIMULATE',`);
fs.writeFileSync('app/IpsecStudio/wwwroot/i18n.js', i);

// ---- app.js ----
let a = read('app/IpsecStudio/wwwroot/app.js');
a = rep(a, `const state = { status: null, inventory: null, draft: null, saved: null, assessment: null, matrix: null, compilation: null, log: [], hosts: null, probe: null, plans: [], page: 'inventory', filter: '', busy: false };`,
`const state = { status: null, inventory: null, draft: null, saved: null, assessment: null, matrix: null, compilation: null, log: [], hosts: null, probe: null, plans: [], page: 'inventory', filter: '', busy: false, wave: 0, waveResult: null, approval: '' };`);
a = rep(a, `    baseGroupId: null, baseMode: 'secureRequest', restricted: [], exemptions: [], noPolicyGroupId: null, boundaryGroupId: null, exclusionGroupId: null, externalHosts: [] };`,
`    baseGroupId: null, baseMode: 'secureRequest', restricted: [], exemptions: [], noPolicyGroupId: null, boundaryGroupId: null, exclusionGroupId: null, externalHosts: [], newGroups: [], waves: [] };`);
a = rep(a, `  const r = await api('/api/plans/simulate', state.draft);
  state.assessment = r.assessment; state.matrix = r.matrix; state.compilation = null;`,
`  if (state.wave > (state.draft.waves?.length ?? 0)) state.wave = 0;
  const r = await api('/api/plans/simulate?wave=' + state.wave, state.draft);
  state.assessment = r.assessment; state.matrix = r.matrix; state.waveHosts = r.waveHosts; state.compilation = null;`);
// profiles screen: waves + new groups cards
a = rep(a, `  const externals = d.externalHosts.map(`, `  const profileGroups = [...new Set([d.baseGroupId, d.noPolicyGroupId, d.boundaryGroupId, d.exclusionGroupId, s.managementGroupId, ...d.restricted.flatMap((r) => [r.protectedGroupId, r.accessGroupId])].filter(Boolean))];
  const waves = d.waves.map((w, idx) => \`<div class="policy-line"><div><b>\${idx + 1}. \${html(w.name)}</b> · \${html(t('waves.members', w.members.reduce((n, m) => n + m.computerIds.length, 0), w.members.length))}<small class="muted"> · \${html(w.members.map((m) => name(m.groupId) + ': ' + m.computerIds.map(name).join(', ')).join(' · '))}</small></div><div class="row"><button class="btn link" data-action="edit-wave" data-id="\${w.id}">\${t('prof.edit')}</button><button class="btn link" data-action="remove-wave" data-id="\${w.id}">\${t('prof.remove')}</button></div></div>\`).join('');
  const newGroups = d.newGroups.map((g) => \`<div class="policy-line"><div><b>\${html(g.name)}</b> · \${html(g.description)}</div><button class="btn link" data-action="remove-group" data-id="\${g.id}">\${t('prof.remove')}</button></div>\`).join('');
  const externals = d.externalHosts.map(`);
a = rep(a, `  </div>
  \${a?.warnings.length ? \`<details class="card pad" style="margin-top:18px"><summary>\${t('prof.warnings', a.warnings.length)}</summary>\${a.warnings.map((w) => \`<p class="hint">\${html(w)}</p>\`).join('')}</details>\` : ''}\`;
}
function simulateScreen() {`,
`    <article class="profile-card waves"><h2>\${t('waves.title')}</h2><p class="hint">\${t('waves.hint')}</p><div class="policy-lines">\${waves || \`<p class="hint">\${t('waves.none')}</p>\`}</div><button class="btn soft" data-action="edit-wave" \${profileGroups.length ? '' : 'disabled'}>\${t('waves.add')}</button></article>
    <article class="profile-card groups"><h2>\${t('groups.title')}</h2><p class="hint">\${t('groups.hint')}</p><div class="field"><label>\${t('groups.container')}</label><input data-field="settings.groupContainer" value="\${html(s.groupContainer || '')}"></div><div class="policy-lines">\${newGroups || \`<p class="hint">\${t('groups.none')}</p>\`}</div><button class="btn soft" data-action="edit-group">\${t('groups.add')}</button></article>
  </div>
  \${a?.warnings.length ? \`<details class="card pad" style="margin-top:18px"><summary>\${t('prof.warnings', a.warnings.length)}</summary>\${a.warnings.map((w) => \`<p class="hint">\${html(w)}</p>\`).join('')}</details>\` : ''}\`;
}
function waveSelector() {
  const d = state.draft; if (!d?.waves?.length) return '';
  return \`<select data-action="select-wave" class="wave-select" aria-label="\${t('waves.after')}"><option value="0" \${state.wave === 0 ? 'selected' : ''}>\${t('waves.inventory')}</option>\${d.waves.map((w, i) => \`<option value="\${i + 1}" \${state.wave === i + 1 ? 'selected' : ''}>\${t('waves.after')} \${i + 1}: \${html(w.name)}</option>\`).join('')}</select>\`;
}
function simulateScreen() {`);
a = rep(a, `<button class="btn soft" data-action="go" data-page="deploy">\${t('sim.deploy')}</button></div>
  <div class="card mb"><div class="table-wrap"><table class="table matrix">`,
`<div class="row">\${waveSelector()}<button class="btn soft" data-action="go" data-page="deploy">\${t('sim.deploy')}</button></div></div>
  <div class="card mb"><div class="table-wrap"><table class="table matrix">`);
a = rep(a, `  const hostRows = a.hosts.map((h) =>`, `  const hostRows = (state.wave > 0 && state.waveHosts ? state.waveHosts : a.hosts).map((h) =>`);
// deploy screen: approval, groups, waves
a = rep(a, `  const log = state.log.slice().reverse().slice(0, 12).map((r) => \`<tr><td>\${html(fmtDate(r.utc))}</td><td>\${html(r.action)}</td><td>v\${r.version}</td>`,
`  const log = state.log.slice().reverse().slice(0, 12).map((r) => \`<tr><td>\${html(fmtDate(r.utc))}</td><td>\${html(r.action)}\${r.waveId ? \` · \${html(d.waves.find((w) => w.id === r.waveId)?.name || r.waveId)}\` : ''}\${r.approval ? \`<small>\${html(r.approval)}</small>\` : ''}</td><td>v\${r.version}</td>`);
a = rep(a, `  return \`<div class="heading"><div><p class="eyebrow">\${t('dep.eyebrow')}</p>`,
`  const waveState = (w) => { const last = state.log.filter((r) => r.waveId === w.id).at(-1); if (!last) return ['notApplied', '']; if (last.action === 'wave-rollback') return ['rolledBack', '']; return last.expansionAllowed ? ['applied', 'teal'] : ['failed', 'red']; };
  const waves = saved && d.waves.length ? \`<div class="card pad mb"><h2>\${t('waves.title')}</h2><p class="hint">\${t('waves.hint')}</p><table class="table"><tbody>\${d.waves.map((w, idx) => { const [st, cls] = waveState(w); const prev = idx > 0 ? waveState(d.waves[idx - 1])[0] : 'applied'; const blocked = prev === 'failed'; return \`<tr><td><b>\${idx + 1}. \${html(w.name)}</b><small>\${html(w.members.map((m) => name(m.groupId) + ': ' + m.computerIds.map(name).join(', ')).join(' · '))}</small></td><td><span class="badge \${cls}">\${t('waves.' + st)}</span>\${blocked ? \` <span class="badge red">\${t('waves.blocked')}</span>\` : ''}</td><td class="row"><button class="btn primary" data-action="wave" data-kind="wave-apply" data-id="\${w.id}" \${state.busy || blocked || !st.match(/notApplied|rolledBack|failed/) ? 'disabled' : ''}>\${t('waves.apply')}</button><button class="btn danger" data-action="wave" data-kind="wave-rollback" data-id="\${w.id}" \${state.busy || st === 'notApplied' || st === 'rolledBack' ? 'disabled' : ''}>\${t('waves.rollback')}</button></td></tr>\`; }).join('')}</tbody></table>\${state.waveResult ? \`<p class="hint"><b>\${html(state.waveResult.waveName)}</b>: \${state.waveResult.succeeded ? t('dep.ok') : t('dep.failed')} · \${t('dep.match', state.waveResult.probe.filter((p) => p.match).length, state.waveResult.probe.length)}</p>\` : ''}</div>\` : '';
  const groupsCard = saved && d.newGroups.length ? \`<div class="card pad mb"><h2>\${t('groups.title')}</h2><p class="hint">\${html(d.newGroups.map((g) => g.name).join(', '))}</p><button class="btn soft" data-action="prepare-groups" \${state.busy ? 'disabled' : ''}>\${t('groups.prepare')}</button></div>\` : '';
  return \`<div class="heading"><div><p class="eyebrow">\${t('dep.eyebrow')}</p>`);
a = rep(a, `    <div class="card pad"><h2>\${t('dep.step2')}</h2>\${c && st.deploymentEnabled ? \`<label class="check"><input id="ack" type="checkbox"><span>\${html(t('dep.ack', c.sha256.slice(0, 12), st.domain))}</span></label>`,
`    <div class="card pad"><h2>\${t('dep.step2')}</h2>\${st.deploymentEnabled ? \`<div class="field"><label>\${t('approval.label')}</label><input id="approval" value="\${html(state.approval)}" placeholder="\${t('approval.placeholder')}" maxlength="500"></div>\` : ''}\${c && st.deploymentEnabled ? \`<label class="check"><input id="ack" type="checkbox"><span>\${html(t('dep.ack', c.sha256.slice(0, 12), st.domain))}</span></label>`);
a = rep(a, `  \${hosts}\${probe}
  <div class="card"><div class="section-head"><h2>\${t('dep.log')}</h2>`, `  \${groupsCard}\${waves}\${hosts}\${probe}
  <div class="card"><div class="section-head"><h2>\${t('dep.log')}</h2>`);
// editors
a = rep(a, `// ---------- rendering & events ----------`, `function editWave(id) {
  const d = state.draft;
  const w = d.waves.find((x) => x.id === id) || { id: uuid(), name: '', members: [] };
  const profileGroups = [...new Set([d.baseGroupId, d.noPolicyGroupId, d.boundaryGroupId, d.exclusionGroupId, d.settings.managementGroupId, ...d.restricted.flatMap((r) => [r.protectedGroupId, r.accessGroupId])].filter(Boolean))];
  const hosts = computers().filter((c) => c.kind !== 'dc');
  const memberRows = (members) => members.map((m, i) => \`<div class="policy-line"><div>\${html(name(m.groupId))}: \${html(m.computerIds.map(name).join(', '))}</div><button type="button" class="btn link" data-remove-member="\${i}">\${t('prof.remove')}</button></div>\`).join('');
  const members = w.members.map((m) => ({ ...m }));
  openEditor(id ? t('waves.edit') : t('waves.editNew'), \`<div class="field"><label>\${t('waves.name')}</label><input name="name" required maxlength="100" value="\${html(w.name)}"></div><div id="wave-members" class="policy-lines">\${memberRows(members)}</div><div class="field"><label>\${t('waves.group')}</label><select name="groupId">\${profileGroups.map((g) => \`<option value="\${g}">\${html(name(g))}</option>\`).join('')}</select></div><div class="field"><label>\${t('waves.computers')}</label><select name="computerIds" multiple size="6">\${hosts.map((c) => \`<option value="\${c.id}">\${html(c.name)}</option>\`).join('')}</select></div><button type="button" class="btn soft" id="add-member">\${t('waves.addMember')}</button>\`, (f) => {
    if (!members.length) throw new Error(t('waves.needGroup'));
    const v = { id: w.id, name: f.name.value.trim(), members };
    const idx = d.waves.findIndex((x) => x.id === v.id); if (idx >= 0) d.waves[idx] = v; else d.waves.push(v);
  });
  const dlg = $('#editor');
  dlg.querySelector('#add-member').addEventListener('click', () => {
    const f = dlg.querySelector('#editor-form'); const ids = [...f.computerIds.selectedOptions].map((o) => o.value);
    if (!f.groupId.value || !ids.length) { toast(t('waves.needGroup')); return; }
    const existing = members.find((m) => m.groupId === f.groupId.value);
    if (existing) existing.computerIds = [...new Set([...existing.computerIds, ...ids])]; else members.push({ groupId: f.groupId.value, computerIds: ids });
    dlg.querySelector('#wave-members').innerHTML = memberRows(members);
  });
  dlg.querySelector('#wave-members').addEventListener('click', (ev) => { const b = ev.target.closest('[data-remove-member]'); if (!b) return; members.splice(Number(b.dataset.removeMember), 1); dlg.querySelector('#wave-members').innerHTML = memberRows(members); });
}
function editGroup() {
  openEditor(t('groups.add'), \`<div class="field"><label>\${t('groups.name')}</label><input name="name" required maxlength="64" placeholder="IPSEC-Web-Servers"></div><div class="field"><label>\${t('groups.description')}</label><input name="description" maxlength="200"></div>\`, (f) => {
    state.draft.newGroups.push({ id: uuid(), name: f.name.value.trim(), description: f.description.value.trim() });
  });
}

// ---------- rendering & events ----------`);
a = rep(a, `  const ack = $('#ack'); if (ack) ack.addEventListener('change', () => { $('#apply-btn').disabled = !ack.checked || state.busy; });`,
`  const ack = $('#ack'); if (ack) ack.addEventListener('change', () => { $('#apply-btn').disabled = !ack.checked || state.busy; });
  const approval = $('#approval'); if (approval) approval.addEventListener('input', () => { state.approval = approval.value; });`);
a = rep(a, `  if (field === 'settings.secureSubnets') setField(field, el.value.split(',').map((s) => s.trim()).filter(Boolean));`,
`  if (field === 'settings.secureSubnets') setField(field, el.value.split(',').map((s) => s.trim()).filter(Boolean));
  else if (field === 'settings.groupContainer') setField(field, el.value.trim() || null);`);
a = rep(a, `document.addEventListener('change', (ev) => { const el = ev.target; if ((el.dataset.field && el.tagName === 'SELECT') || el.type === 'checkbox') run(t('actions.assess'), reassess); });`,
`document.addEventListener('change', (ev) => { const el = ev.target; if (el.dataset.action === 'select-wave') { state.wave = Number(el.value); run(t('actions.assess'), reassess); return; } if ((el.dataset.field && el.tagName === 'SELECT') || el.type === 'checkbox') run(t('actions.assess'), reassess); });`);
a = rep(a, `  if (a === 'edit-external') editExternal();`, `  if (a === 'edit-external') editExternal();
  if (a === 'edit-wave') editWave(el.dataset.id);
  if (a === 'remove-wave') { state.draft.waves = state.draft.waves.filter((x) => x.id !== el.dataset.id); run(t('actions.assess'), reassess); }
  if (a === 'edit-group') editGroup();
  if (a === 'remove-group') { state.draft.newGroups = state.draft.newGroups.filter((x) => x.id !== el.dataset.id); run(t('actions.assess'), reassess); }
  if (a === 'prepare-groups') run(t('actions.apply'), async () => { if (!state.approval.trim()) throw new Error(t('approval.missing')); const g = await api(\`/api/plans/\${state.saved.id}/compile-groups\`); const r = await api('/api/deploy', { planId: state.saved.id, version: state.saved.version, hash: g.sha256, action: 'groups', approval: state.approval }); state.log = await api(\`/api/plans/\${state.saved.id}/deployments\`); toast(r.succeeded ? t('groups.prepared') : t('dep.failedSee', 'groups'), 8000); });
  if (a === 'wave') run(el.dataset.kind, async () => { if (el.dataset.kind === 'wave-apply' && !state.approval.trim()) throw new Error(t('approval.missing')); state.waveResult = await api('/api/wave', { planId: state.saved.id, version: state.saved.version, waveId: el.dataset.id, action: el.dataset.kind, approval: state.approval }); state.hosts = state.waveResult.verify; state.probe = state.waveResult.probe; state.log = await api(\`/api/plans/\${state.saved.id}/deployments\`); });`);
a = rep(a, `const r = await api('/api/deploy', { planId: state.saved.id, version: state.saved.version, hash: state.compilation.sha256, action: el.dataset.kind });`,
`if (el.dataset.kind === 'apply' && !state.approval.trim()) throw new Error(t('approval.missing')); const r = await api('/api/deploy', { planId: state.saved.id, version: state.saved.version, hash: state.compilation.sha256, action: el.dataset.kind, approval: state.approval });`);
fs.writeFileSync('app/IpsecStudio/wwwroot/app.js', a);

let css = read('app/IpsecStudio/wwwroot/app.css');
css += `.profile-card.waves{--accent:#1f7a6d}.profile-card.groups{--accent:#5c6bc0}.wave-select{height:34px;font-size:12px;max-width:280px}
`;
fs.writeFileSync('app/IpsecStudio/wwwroot/app.css', css);
console.log('patched');

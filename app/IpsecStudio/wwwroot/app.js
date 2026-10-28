'use strict';
// IPsec Studio 0.3 — plan the five protection profiles, simulate Windows IPsec outcomes, deploy GPOs, verify and probe.
const state = { status: null, inventory: null, draft: null, saved: null, assessment: null, matrix: null, compilation: null, log: [], hosts: null, probe: null, plans: [], page: 'inventory', filter: '', busy: false, wave: 0, waveResult: null, approval: '', drift: null, ackDrift: false };
const $ = (sel) => document.querySelector(sel);
const html = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const uuid = () => crypto.randomUUID();
const objects = () => state.inventory?.objects ?? [];
const byId = (id) => objects().find((o) => o.id === id);
const name = (id) => byId(id)?.name ?? state.draft?.externalHosts.find((x) => x.id === id)?.name ?? id;
const groups = () => objects().filter((o) => o.kind === 'group' && o.sid).sort((a, b) => a.name.localeCompare(b.name));
const computers = () => objects().filter((o) => ['server', 'workstation', 'dc', 'computer'].includes(o.kind));
const PAGES = [['inventory', '01'], ['profiles', '02'], ['simulate', '03'], ['deploy', '04'], ['saved', '']];
const OUTCOME_CLASS = { clear: 'o-clear', authenticated: 'o-auth', encrypted: 'o-enc', blocked: 'o-block', indeterminate: 'o-unknown', unreachable: 'o-unknown' };
const outcome = (key) => [t('outcome.' + key) === 'outcome.' + key ? key : t('outcome.' + key), OUTCOME_CLASS[key] || 'o-unknown'];
const fmtDate = (v) => new Date(v).toLocaleString(locale());

async function api(path, body) {
  const headers = { 'X-Lang': lang };
  const init = body === undefined ? { headers } : { method: 'POST', headers: { ...headers, 'Content-Type': 'application/json', 'X-Plan-Token': state.status?.token ?? '' }, body: JSON.stringify(body) };
  const res = await fetch(path, init);
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`);
  return data;
}
function toast(message, ms = 5000) { const el = $('#toast'); el.textContent = message; el.classList.add('show'); clearTimeout(toast.timer); toast.timer = setTimeout(() => el.classList.remove('show'), ms); }
async function run(label, fn) {
  if (state.busy) return; state.busy = true; render();
  try { await fn(); } catch (e) { toast(`${label}: ${e.message}`, 9000); } finally { state.busy = false; render(); }
}
function newDraft() {
  return { name: lang === 'en' ? 'Isolation plan' : 'Plano de isolamento', inventoryId: state.inventory.id, expectedVersion: null, settings: { secureSubnets: [], exemptDomainControllers: true, exemptIcmp: true, linkTarget: 'domain', crypto: { mainModeEncryption: 'AES256', mainModeHash: 'SHA256', keyExchange: 'DH14', quickModeEncryption: 'AES256', quickModeIntegrity: 'SHA256', mainModeLifetimeMinutes: 180, quickModeLifetimeMinutes: 60 }, managementGroupId: null },
    baseGroupId: null, baseMode: 'secureRequest', restricted: [], exemptions: [], noPolicyGroupId: null, boundaryGroupId: null, exclusionGroupId: null, externalHosts: [], newGroups: [], waves: [] };
}
async function reassess() {
  if (!state.draft) return;
  if (state.wave > (state.draft.waves?.length ?? 0)) state.wave = 0;
  const r = await api('/api/plans/simulate?wave=' + state.wave, state.draft);
  state.assessment = r.assessment; state.matrix = r.matrix; state.waveHosts = r.waveHosts; state.compilation = null;
}
function setPage(page) { state.page = page; location.hash = '#' + page; render(); }

// ---------- screens ----------
function inventoryScreen() {
  const inv = state.inventory; const a = state.assessment;
  const counts = inv ? { computers: computers().length, groups: groups().length, dcs: computers().filter((c) => c.kind === 'dc').length, gpos: objects().filter((o) => o.kind === 'gpo').length } : null;
  const rows = computers().filter((c) => !state.filter || c.name.toLowerCase().includes(state.filter.toLowerCase())).map((c) => {
    const h = a?.hosts.find((x) => x.computerId === c.id);
    return `<tr><td><b>${html(c.name)}</b><small>${html(c.os || c.kind)}</small></td><td>${html(c.ip || '—')}</td><td>${h ? `<span class="badge ${h.profile === 'restricted' || h.profile === 'quarantine' ? 'red' : h.profile === 'base' ? 'teal' : ''}">${html(t('profile.' + h.profile))}</span>` : '—'}</td><td class="small">${html(h?.memberships.join(', ') || '')}${h?.conflicts.length ? `<div class="notice red tiny">${html(h.conflicts.join(' '))}</div>` : ''}</td></tr>`;
  }).join('');
  return `<div class="heading"><div><p class="eyebrow">${t('inv.eyebrow')}</p><h1>${t('inv.title')}</h1><p class="subtitle">${inv ? html(t('inv.subtitle', inv.domain, fmtDate(inv.capturedAt), inv.identity)) : t('inv.empty')}</p></div><div class="row"><button class="btn primary" data-action="refresh-inventory" ${state.busy ? 'disabled' : ''}>${t('inv.refresh')}</button>${inv && !state.draft ? `<button class="btn soft" data-action="new-plan">${t('inv.newPlan')}</button>` : ''}</div></div>
  ${state.status?.lastReadError ? `<div class="notice red mb">${html(state.status.lastReadError)}</div>` : ''}
  ${counts ? `<div class="stats"><div class="card stat"><small>${t('inv.computers')}</small><b>${counts.computers}</b><span>${t('inv.dcs', counts.dcs)}</span></div><div class="card stat"><small>${t('inv.groups')}</small><b>${counts.groups}</b></div><div class="card stat"><small>${t('inv.gpos')}</small><b>${counts.gpos}</b></div><div class="card stat"><small>${t('inv.warnings')}</small><b>${inv.warnings.length}</b></div></div>` : ''}
  ${inv ? `<div class="card"><div class="toolbar"><input id="filter" placeholder="${t('inv.filter')}" value="${html(state.filter)}"></div><div class="table-wrap"><table class="table"><thead><tr><th>${t('inv.computer')}</th><th>IPv4</th><th>${t('inv.profileInPlan')}</th><th>${t('inv.relevantGroups')}</th></tr></thead><tbody>${rows || `<tr><td colspan="4" class="empty">${t('inv.none')}</td></tr>`}</tbody></table></div></div>` : ''}
  ${inv?.warnings.length ? `<details class="card pad mb" style="margin-top:18px"><summary>${t('inv.warningsTitle', inv.warnings.length)}</summary>${inv.warnings.map((w) => `<p class="hint">${html(w)}</p>`).join('')}</details>` : ''}`;
}
function groupSelect(field, value, placeholder, extra = '') {
  return `<select data-field="${field}" ${extra}><option value="">${html(placeholder)}</option>${groups().map((g) => `<option value="${g.id}" ${g.id === value ? 'selected' : ''}>${html(g.name)} (${g.memberIds.length})</option>`).join('')}</select>`;
}
function profilesScreen() {
  const d = state.draft; if (!d) return `<div class="notice">${t('prof.needInventory')}</div>`;
  const a = state.assessment; const s = d.settings;
  const members = (key) => a?.profiles.find((p) => p.key === key)?.computerIds.map(name).join(', ') || t('prof.nobody');
  const restricted = d.restricted.map((r) => `<div class="policy-line"><div><b>${html(r.name)}</b> · ${html(name(r.protectedGroupId))} ← ${r.accessGroupId ? html(name(r.accessGroupId)) : t('prof.anyComputer')} · ${r.encryption ? t('prof.encrypted') : t('prof.authenticated')}<small class="muted"> · ${html(t('prof.members', members('restricted:' + r.id)))}</small></div><div class="row"><button class="btn link" data-action="edit-restricted" data-id="${r.id}">${t('prof.edit')}</button><button class="btn link" data-action="remove-restricted" data-id="${r.id}">${t('prof.remove')}</button></div></div>`).join('');
  const exemptions = d.exemptions.map((e) => `<div class="policy-line"><div><b>${html(e.name)}</b> · ${html(e.address)} · ${html(e.reason)} (${html(e.owner)})</div><button class="btn link" data-action="remove-exemption" data-id="${e.id}">${t('prof.remove')}</button></div>`).join('');
  const profileGroups = [...new Set([d.baseGroupId, d.noPolicyGroupId, d.boundaryGroupId, d.exclusionGroupId, s.managementGroupId, ...d.restricted.flatMap((r) => [r.protectedGroupId, r.accessGroupId])].filter(Boolean))];
  const waves = d.waves.map((w, idx) => `<div class="policy-line"><div><b>${idx + 1}. ${html(w.name)}</b> · ${html(t('waves.members', w.members.reduce((n, m) => n + m.computerIds.length, 0), w.members.length))}<small class="muted"> · ${html(w.members.map((m) => name(m.groupId) + ': ' + m.computerIds.map(name).join(', ')).join(' · '))}</small></div><div class="row"><button class="btn link" data-action="edit-wave" data-id="${w.id}">${t('prof.edit')}</button><button class="btn link" data-action="remove-wave" data-id="${w.id}">${t('prof.remove')}</button></div></div>`).join('');
  const newGroups = d.newGroups.map((g) => `<div class="policy-line"><div><b>${html(g.name)}</b> · ${html(g.description)}</div><button class="btn link" data-action="remove-group" data-id="${g.id}">${t('prof.remove')}</button></div>`).join('');
  const externals = d.externalHosts.map((x) => `<div class="policy-line"><div><b>${html(x.name)}</b> · ${html(x.address)} · ${t('prof.externalNote')}</div><button class="btn link" data-action="remove-external" data-id="${x.id}">${t('prof.remove')}</button></div>`).join('');
  const opt = (field, values, current) => values.map((v) => `<option ${current === v ? 'selected' : ''}>${v}</option>`).join('');
  return `<div class="heading"><div><p class="eyebrow">${t('prof.eyebrow')}</p><h1>${t('prof.title')}</h1><p class="subtitle">${t('prof.subtitle')}</p></div><div class="row"><input data-field="name" value="${html(d.name)}" style="width:260px" aria-label="${t('prof.planName')}"><button class="btn soft" data-action="go" data-page="simulate">${t('prof.simulate')}</button></div></div>
  <div class="profiles-grid">
    <article class="profile-card p2"><span class="profile-number">2</span><h2>${t('prof.baseTitle')}</h2><p class="hint">${t('prof.baseHint')}</p><div class="field"><label>${t('prof.baseGroup')}</label>${groupSelect('baseGroupId', d.baseGroupId, t('prof.baseDefault'))}</div><div class="field"><label>${t('prof.mode')}</label><select data-field="baseMode"><option value="secureRequest" ${d.baseMode === 'secureRequest' ? 'selected' : ''}>${t('prof.modeSecureRequest')}</option><option value="request" ${d.baseMode === 'request' ? 'selected' : ''}>${t('prof.modeRequest')}</option><option value="require" ${d.baseMode === 'require' ? 'selected' : ''}>${t('prof.modeRequire')}</option></select></div><p class="tiny muted">${html(t('prof.members', members('base')))}</p></article>
    <article class="profile-card p1"><span class="profile-number">1</span><h2>${t('prof.restrictedTitle')}</h2><p class="hint">${t('prof.restrictedHint')}</p><div class="policy-lines">${restricted || `<p class="hint">${t('prof.noRestricted')}</p>`}</div><button class="btn soft" data-action="edit-restricted">${t('prof.addRestricted')}</button></article>
    <article class="profile-card p3"><span class="profile-number">3</span><h2>${t('prof.exemptTitle')}</h2><p class="hint">${t('prof.exemptHint')}</p><div class="field"><label>${t('prof.noPolicyGroup')}</label>${groupSelect('noPolicyGroupId', d.noPolicyGroupId, t('prof.noneOption'))}</div><div class="policy-lines">${exemptions || `<p class="hint">${t('prof.noExempt')}</p>`}</div><button class="btn soft" data-action="edit-exemption">${t('prof.addExempt')}</button><p class="tiny muted">${html(t('prof.dcExempt', (a?.domainControllerAddresses || []).join(', ') || '—'))}</p></article>
    <article class="profile-card p4"><span class="profile-number">4</span><h2>${t('prof.boundaryTitle')}</h2><p class="hint">${t('prof.boundaryHint')}</p><div class="field"><label>${t('prof.boundaryGroup')}</label>${groupSelect('boundaryGroupId', d.boundaryGroupId, t('prof.noneOption'))}</div><p class="tiny muted">${html(t('prof.members', members('boundary')))}</p></article>
    <article class="profile-card p5"><span class="profile-number">5</span><h2>${t('prof.quarantineTitle')}</h2><p class="hint">${t('prof.quarantineHint')}</p><div class="field"><label>${t('prof.exclusionGroup')}</label>${groupSelect('exclusionGroupId', d.exclusionGroupId, t('prof.noneOption'))}</div><div class="field"><label>${t('prof.managementGroup')}</label>${groupSelect('settings.managementGroupId', s.managementGroupId, t('prof.noneOption'))}</div><p class="tiny muted">${html(t('prof.inQuarantine', members('exclusion')))}</p></article>
    <article class="profile-card settings"><h2>${t('prof.settingsTitle')}</h2><div class="field"><label>${t('prof.subnets')}</label><input data-field="settings.secureSubnets" value="${html(s.secureSubnets.join(', '))}"></div><label class="check"><input type="checkbox" data-field="settings.exemptDomainControllers" ${s.exemptDomainControllers ? 'checked' : ''}><span>${t('prof.exemptDcs')}</span></label><label class="check"><input type="checkbox" data-field="settings.exemptIcmp" ${s.exemptIcmp ? 'checked' : ''}><span>${t('prof.exemptIcmp')}</span></label><div class="field"><label>${t('prof.linkTarget')}</label><select data-field="settings.linkTarget"><option value="domain" ${s.linkTarget === 'domain' ? 'selected' : ''}>${t('prof.domainRoot')}</option>${objects().filter((o) => o.kind === 'ou').map((o) => `<option value="${html(o.dn)}" ${s.linkTarget === o.dn ? 'selected' : ''}>${html(o.name)}</option>`).join('')}</select></div><div class="crypto-grid"><label>${t('prof.mm')} <select data-field="settings.crypto.mainModeEncryption">${opt('', ['AES128', 'AES192', 'AES256'], s.crypto.mainModeEncryption)}</select></label><label>${t('prof.hash')} <select data-field="settings.crypto.mainModeHash">${opt('', ['SHA256', 'SHA384'], s.crypto.mainModeHash)}</select></label><label>${t('prof.dh')} <select data-field="settings.crypto.keyExchange">${opt('', ['DH14', 'DH19', 'DH20', 'DH24'], s.crypto.keyExchange)}</select></label><label>${t('prof.esp')} <select data-field="settings.crypto.quickModeEncryption">${opt('', ['AES128', 'AES192', 'AES256', 'AESGCM128', 'AESGCM256'], s.crypto.quickModeEncryption)}</select></label><label>${t('prof.espIntegrity')} <select data-field="settings.crypto.quickModeIntegrity">${opt('', ['SHA256', 'AESGMAC256'], s.crypto.quickModeIntegrity)}</select></label><label>${t('prof.mmMin')} <input type="number" data-field="settings.crypto.mainModeLifetimeMinutes" value="${s.crypto.mainModeLifetimeMinutes}"></label><label>${t('prof.qmMin')} <input type="number" data-field="settings.crypto.quickModeLifetimeMinutes" value="${s.crypto.quickModeLifetimeMinutes}"></label></div><div class="policy-lines">${externals || `<p class="hint">${t('prof.noExternal')}</p>`}</div><button class="btn soft" data-action="edit-external">${t('prof.addExternal')}</button></article>
    <article class="profile-card waves"><h2>${t('waves.title')}</h2><p class="hint">${t('waves.hint')}</p><div class="policy-lines">${waves || `<p class="hint">${t('waves.none')}</p>`}</div><button class="btn soft" data-action="edit-wave" ${profileGroups.length ? '' : 'disabled'}>${t('waves.add')}</button></article>
    <article class="profile-card groups"><h2>${t('groups.title')}</h2><p class="hint">${t('groups.hint')}</p><div class="field"><label>${t('groups.container')}</label><input data-field="settings.groupContainer" value="${html(s.groupContainer || '')}"></div><div class="policy-lines">${newGroups || `<p class="hint">${t('groups.none')}</p>`}</div><button class="btn soft" data-action="edit-group">${t('groups.add')}</button></article>
  </div>
  ${a?.warnings.length ? `<details class="card pad" style="margin-top:18px"><summary>${t('prof.warnings', a.warnings.length)}</summary>${a.warnings.map((w) => `<p class="hint">${html(w)}</p>`).join('')}</details>` : ''}`;
}
function waveSelector() {
  const d = state.draft; if (!d?.waves?.length) return '';
  return `<select data-action="select-wave" class="wave-select" aria-label="${t('waves.after')}"><option value="0" ${state.wave === 0 ? 'selected' : ''}>${t('waves.inventory')}</option>${d.waves.map((w, i) => `<option value="${i + 1}" ${state.wave === i + 1 ? 'selected' : ''}>${t('waves.after')} ${i + 1}: ${html(w.name)}</option>`).join('')}</select>`;
}
function simulateScreen() {
  const a = state.assessment; const m = state.matrix;
  if (!a || !m) return `<div class="notice">${t('sim.needProfiles')}</div>`;
  const ids = [...new Set(m.flatMap((x) => [x.initiator, x.responder]))];
  const cell = (i, r) => { if (i === r) return '<td class="o-self"></td>'; const e = m.find((x) => x.initiator === i && x.responder === r); const [label, cls] = outcome(e?.outcome || 'indeterminate'); return `<td class="${cls}" title="${html(e?.reasons.join(' '))}" data-action="explain" data-i="${html(i)}" data-r="${html(r)}">${label}</td>`; };
  const hostRows = (state.wave > 0 && state.waveHosts ? state.waveHosts : a.hosts).map((h) => `<tr><td><b>${html(h.name)}</b></td><td>${html(h.ip || '—')}</td><td>${html(t('profile.' + h.profile))}${h.excluded && h.profile !== 'quarantine' ? ` · <b>${t('sim.quarantine')}</b>` : ''}</td><td class="small">${html(h.memberships.join(', '))}</td><td class="small">${h.conflicts.length ? `<span class="badge red">${html(h.conflicts.join(' '))}</span>` : ''}</td></tr>`).join('');
  return `<div class="heading"><div><p class="eyebrow">${t('sim.eyebrow')}</p><h1>${t('sim.title')}</h1><p class="subtitle">${t('sim.subtitle')}</p></div><div class="row">${waveSelector()}<button class="btn soft" data-action="go" data-page="deploy">${t('sim.deploy')}</button></div></div>
  <div class="card mb"><div class="table-wrap"><table class="table matrix"><thead><tr><th>${t('sim.corner')}</th>${ids.map((r) => `<th>${html(r)}</th>`).join('')}</tr></thead><tbody>${ids.map((i) => `<tr><th>${html(i)}</th>${ids.map((r) => cell(i, r)).join('')}</tr>`).join('')}</tbody></table></div><div class="legend"><span class="o-clear">${t('outcome.clear')}</span><span class="o-auth">${t('sim.legendAuth')}</span><span class="o-enc">${t('outcome.encrypted')}</span><span class="o-block">${t('outcome.blocked')}</span></div></div>
  <div id="explain" class="notice mb" hidden></div>
  <div class="card"><div class="section-head"><h2>${t('sim.perHost')}</h2></div><div class="table-wrap"><table class="table"><thead><tr><th>${t('inv.computer')}</th><th>${t('sim.ipv4')}</th><th>${t('sim.profileCol')}</th><th>${t('sim.memberships')}</th><th>${t('sim.conflicts')}</th></tr></thead><tbody>${hostRows}</tbody></table></div></div>
  ${a.warnings.length ? `<details class="card pad" style="margin-top:18px"><summary>${t('prof.warnings', a.warnings.length)}</summary>${a.warnings.map((w) => `<p class="hint">${html(w)}</p>`).join('')}</details>` : ''}`;
}
function deployScreen() {
  const d = state.draft; const saved = state.saved; const c = state.compilation; const st = state.status;
  if (!d) return `<div class="notice">${t('sim.needProfiles')}</div>`;
  const dirty = !saved || JSON.stringify({ ...d, expectedVersion: null }) !== JSON.stringify({ ...saved.input, expectedVersion: null });
  const log = state.log.slice().reverse().slice(0, 12).map((r) => `<tr><td>${html(fmtDate(r.utc))}</td><td>${html(r.action)}${r.waveId ? ` · ${html(d.waves.find((w) => w.id === r.waveId)?.name || r.waveId)}` : ''}${r.approval ? `<small>${html(r.approval)}</small>` : ''}</td><td>v${r.version}</td><td>${r.succeeded ? `<span class="badge ${r.action === 'drift-acknowledged' ? 'amber' : 'teal'}">${t('dep.ok')}</span>` : `<span class="badge red">${t('dep.failed')}</span>`}</td><td class="code" style="max-width:480px;overflow-wrap:anywhere">${html(r.output.slice(0, 400))}</td></tr>`).join('');
  const hosts = state.hosts ? `<div class="card mb"><div class="section-head"><h2>${t('dep.lastVerify')}</h2></div><div class="table-wrap"><table class="table"><thead><tr><th>${t('dep.host')}</th><th>${t('dep.reachable')}</th><th>${t('dep.result')}</th><th></th></tr></thead><tbody>${state.hosts.map((h) => { let o = null; try { o = JSON.parse(h.output); } catch { } return `<tr><td><b>${html(h.host)}</b></td><td>${h.reachable ? t('dep.yes') : `<span class="badge red">${t('dep.no')}</span>`}</td><td class="small">${o ? (o.studioRules ? `${o.healthy ? `<span class="badge teal">${t('dep.rulesOk')}</span>` : `<span class="badge red">${html(t('dep.ruleError', o.studioRules.filter((r) => r.status !== 'OK').map((r) => r.statusText).join('; ') || t('dep.noRule')))}</span>`} ${identityBadge(h)} · ${html(t('dep.rules', o.studioRules.length, (o.gpos || []).join(', ') || t('dep.none'), o.enabledIpsecRules))} <span class="code">${html(o.authorizationList || '—')}</span>` : html(t('dep.refreshed', o.refreshedUtc, o.gpupdateExit))) : html(h.output)}</td><td>${h.reachable && h.identityState ? `<button class="btn" data-action="renew-host" data-host="${html(h.host)}" ${state.busy ? 'disabled' : ''}>${t('identity.renew')}</button>` : ''}</td></tr>`; }).join('')}</tbody></table></div><p class="hint">${t('identity.help')}</p></div>` : '';
  const probe = state.probe ? `<div class="card mb"><div class="section-head"><h2>${t('dep.probeTitle')}</h2><span class="badge ${state.probe.every((p) => p.match) ? 'teal' : 'red'}">${t('dep.match', state.probe.filter((p) => p.match).length, state.probe.length)}</span></div><div class="table-wrap"><table class="table"><thead><tr><th>${t('dep.initiator')}</th><th>${t('dep.responder')}</th><th>${t('dep.expected')}</th><th>${t('dep.observed')}</th><th>${t('dep.detail')}</th></tr></thead><tbody>${state.probe.map((p) => `<tr class="${p.match ? '' : 'mismatch'}"><td>${html(p.initiator)}</td><td>${html(p.responder)}</td><td class="${outcome(p.expected)[1]}">${outcome(p.expected)[0]}</td><td class="${outcome(p.observed)[1]}">${outcome(p.observed)[0]}</td><td class="code">${html(p.detail)}</td></tr>`).join('')}</tbody></table></div><p class="hint">${t('dep.probeHelp', st.probePort)}</p></div>` : '';
  const waveState = (w) => { const last = state.log.filter((r) => r.waveId === w.id).at(-1); if (!last) return ['notApplied', '']; if (last.action === 'wave-rollback') return ['rolledBack', '']; return last.expansionAllowed ? ['applied', 'teal'] : ['failed', 'red']; };
  const waves = saved && d.waves.length ? `<div class="card pad mb"><h2>${t('waves.title')}</h2><p class="hint">${t('waves.hint')}</p><table class="table"><tbody>${d.waves.map((w, idx) => { const [st, cls] = waveState(w); const prev = idx > 0 ? waveState(d.waves[idx - 1])[0] : 'applied'; const blocked = prev === 'failed'; return `<tr><td><b>${idx + 1}. ${html(w.name)}</b><small>${html(w.members.map((m) => name(m.groupId) + ': ' + m.computerIds.map(name).join(', ')).join(' · '))}</small></td><td><span class="badge ${cls}">${t('waves.' + st)}</span>${blocked ? ` <span class="badge red">${t('waves.blocked')}</span>` : ''}</td><td class="row"><button class="btn primary" data-action="wave" data-kind="wave-apply" data-id="${w.id}" ${state.busy || blocked || !st.match(/notApplied|rolledBack|failed/) ? 'disabled' : ''}>${t('waves.apply')}</button><button class="btn danger" data-action="wave" data-kind="wave-rollback" data-id="${w.id}" ${state.busy || st === 'notApplied' || st === 'rolledBack' ? 'disabled' : ''}>${t('waves.rollback')}</button></td></tr>`; }).join('')}</tbody></table>${state.waveResult ? `<p class="hint"><b>${html(state.waveResult.waveName)}</b>: ${state.waveResult.succeeded ? t('dep.ok') : t('dep.failed')} · ${t('dep.match', state.waveResult.probe.filter((p) => p.match).length, state.waveResult.probe.length)}</p>` : ''}</div>` : '';
  const driftCard = saved && st.deploymentEnabled ? `<div class="card pad mb"><h2>${t('drift.title')}</h2><p class="hint">${t('drift.help')}</p><button class="btn" data-action="drift" ${state.busy ? 'disabled' : ''}>${t('drift.check')}</button>${state.drift ? `<div class="notice ${state.drift.clean ? '' : 'red'}" style="margin-top:12px">${state.drift.clean ? t('drift.clean') : t('drift.dirty')}</div>${state.drift.gpos.filter((g) => g.differences.length).map((g) => `<p class="small"><b class="code">${html(g.name)}</b><br>${g.differences.map(html).join('<br>')}</p>`).join('')}${state.drift.external.length ? `<p class="small"><b>${t('drift.external')}</b><br>${state.drift.external.map((e) => `<span class="code">${html(e.name)}</span> · ${e.ruleCount} ${t('drift.rules')} · ${t('drift.links')}: ${html(e.links.join(', '))}`).join('<br>')}</p>` : ''}${state.drift.clean ? '' : `<label class="check"><input type="checkbox" id="ack-drift" ${state.ackDrift ? 'checked' : ''}><span>${t('drift.acknowledge')}</span></label>`}` : ''}</div>` : '';
  const groupsCard = saved && d.newGroups.length ? `<div class="card pad mb"><h2>${t('groups.title')}</h2><p class="hint">${html(d.newGroups.map((g) => g.name).join(', '))}</p><button class="btn soft" data-action="prepare-groups" ${state.busy ? 'disabled' : ''}>${t('groups.prepare')}</button></div>` : '';
  return `<div class="heading"><div><p class="eyebrow">${t('dep.eyebrow')}</p><h1>${t('dep.title')}</h1><p class="subtitle">${html(t('dep.identity', st.identity, st.host))} ${st.deploymentEnabled ? t('dep.writes') : t('dep.readOnly')}</p></div><div class="row"><button class="btn primary" data-action="save" ${state.busy ? 'disabled' : ''}>${saved ? (dirty ? t('dep.saveNew') : t('dep.saved', saved.version)) : t('dep.save')}</button></div></div>
  ${dirty ? `<div class="notice amber mb">${t('dep.dirty')}</div>` : ''}
  <div class="grid cols">
    <div class="card pad"><h2>${t('dep.step1')}</h2>${saved ? `<p class="hint">${html(t('dep.planVersion', saved.id, saved.version))}</p><button class="btn soft" data-action="compile" ${state.busy ? 'disabled' : ''}>${t('dep.compile', saved.version)}</button>` : `<p class="hint">${t('dep.saveFirst')}</p>`}
      ${c ? `<div class="divider"></div><p class="small">SHA-256 <span class="code">${html(c.sha256)}</span></p><table class="table"><thead><tr><th>${t('dep.gpo')}</th><th>${t('dep.profileCol')}</th><th>${t('dep.appliesTo')}</th><th>${t('dep.inOut')}</th><th>${t('dep.cipher')}</th><th>${t('dep.computers')}</th></tr></thead><tbody>${c.gpos.map((g) => `<tr><td class="code">${html(g.name)}</td><td>${html(g.profile)}</td><td>${html(g.applyGroup)}${g.denyGroupSids.length ? `<small>${t('dep.denies', g.denyGroupSids.length)}</small>` : ''}</td><td>${html(g.inbound)} / ${html(g.outbound)}</td><td>${g.encryption ? t('dep.yes') : t('dep.no')}</td><td class="small">${html(g.computerIds.map(name).join(', ') || '—')}</td></tr>`).join('')}</tbody></table><p class="hint">${html(t('dep.exemptAll', c.exemptAddresses.join(', ') || t('dep.none')))}</p><details><summary class="small">${t('dep.applyScript')}</summary><pre class="script">${html(c.applyScript)}</pre></details><details><summary class="small">${t('dep.rollbackScript')}</summary><pre class="script">${html(c.rollbackScript)}</pre></details>` : ''}</div>
    <div class="card pad"><h2>${t('dep.step2')}</h2>${st.deploymentEnabled ? `<div class="field"><label>${t('approval.label')}</label><input id="approval" value="${html(state.approval)}" placeholder="${t('approval.placeholder')}" maxlength="500"></div>` : ''}${c && st.deploymentEnabled ? `<label class="check"><input id="ack" type="checkbox"><span>${html(t('dep.ack', c.sha256.slice(0, 12), st.domain))}</span></label><div class="deploy-actions"><button class="btn primary" data-action="deploy" data-kind="apply" disabled id="apply-btn">${t('dep.apply')}</button><button class="btn" data-action="hosts" data-kind="refresh" ${state.busy ? 'disabled' : ''}>${t('dep.refresh')}</button><button class="btn" data-action="hosts" data-kind="verify" ${state.busy ? 'disabled' : ''}>${t('dep.verify')}</button><button class="btn" data-action="probe" ${state.busy ? 'disabled' : ''}>${t('dep.probe')}</button><button class="btn danger" data-action="deploy" data-kind="rollback" ${state.busy ? 'disabled' : ''}>${t('dep.rollback')}</button></div><p class="hint">${t('dep.help')}</p>` : `<p class="hint">${t('dep.compileElsewhere')}</p>`}</div>
  </div>
  ${driftCard}${groupsCard}${waves}${hosts}${probe}
  <div class="card"><div class="section-head"><h2>${t('dep.log')}</h2></div><div class="table-wrap"><table class="table"><thead><tr><th>${t('dep.when')}</th><th>${t('dep.action')}</th><th>${t('dep.version')}</th><th>${t('dep.result')}</th><th>${t('dep.output')}</th></tr></thead><tbody>${log || `<tr><td colspan="5" class="empty">${t('dep.noLog')}</td></tr>`}</tbody></table></div></div>`;
}
function identityBadge(h) {
  if (!h.identityState) return '';
  const cls = h.identityState === 'synced' ? 'teal' : h.identityState === 'pending' ? 'red' : 'amber';
  const detail = h.tgtStartUtc || h.lastGroupChangeUtc ? ` title="${html(t('identity.tgt'))}: ${h.tgtStartUtc ? html(fmtDate(h.tgtStartUtc)) : '—'} · ${html(t('identity.change'))}: ${h.lastGroupChangeUtc ? html(fmtDate(h.lastGroupChangeUtc)) : '—'}"` : '';
  return `<span class="badge ${cls}"${detail}>${t('identity.' + h.identityState)}</span>`;
}
function savedScreen() {
  const list = state.plans || [];
  return `<div class="heading"><div><p class="eyebrow">${t('saved.eyebrow')}</p><h1>${t('saved.title')}</h1><p class="subtitle">${t('saved.subtitle')}</p></div></div>
  <div class="card"><table class="table"><thead><tr><th>${t('saved.name')}</th><th>${t('saved.version')}</th><th>${t('saved.domain')}</th><th>${t('saved.updated')}</th><th></th></tr></thead><tbody>${list.map((p) => `<tr><td><b>${html(p.name)}</b></td><td>${p.version}</td><td>${html(p.domain)}</td><td>${html(fmtDate(p.updatedAt))}</td><td><button class="btn soft" data-action="open-plan" data-id="${p.id}">${t('saved.open')}</button></td></tr>`).join('') || `<tr><td colspan="5" class="empty">${t('saved.none')}</td></tr>`}</tbody></table></div>`;
}

// ---------- editors ----------
function openEditor(title, body, onSubmit) {
  const dlg = $('#editor');
  dlg.innerHTML = `<div class="dialog-title"><h2 id="editor-title">${html(title)}</h2><button class="close" data-action="close-editor" aria-label="${t('ed.close')}">×</button></div><form id="editor-form">${body}<div class="actions"><button type="button" class="btn" data-action="close-editor">${t('ed.cancel')}</button><button type="submit" class="btn primary">${t('ed.confirm')}</button></div></form>`;
  dlg.querySelector('#editor-form').addEventListener('submit', (ev) => { ev.preventDefault(); const f = ev.target; try { onSubmit(f); dlg.close(); run(t('actions.assess'), reassess); } catch (e) { toast(e.message); } });
  dlg.showModal();
}
function editRestricted(id) {
  const r = state.draft.restricted.find((x) => x.id === id) || { id: uuid(), name: '', protectedGroupId: '', accessGroupId: null, encryption: true, included: true };
  openEditor(id ? t('ed.restrictedEdit') : t('ed.restrictedNew'), `<div class="field"><label>${t('ed.profileName')}</label><input name="name" required maxlength="100" value="${html(r.name)}"></div><div class="field"><label>${t('ed.protectedGroup')}</label>${groupSelect('', r.protectedGroupId, t('ed.choose'), 'name="protectedGroupId" required')}</div><div class="field"><label>${t('ed.accessGroup')}</label>${groupSelect('', r.accessGroupId, t('ed.anyAuth'), 'name="accessGroupId"')}</div><label class="check"><input type="checkbox" name="encryption" ${r.encryption ? 'checked' : ''}><span>${t('ed.requireEncryption')}</span></label><p class="hint">${t('ed.restrictedHelp')}</p>`, (f) => {
    const v = { ...r, name: f.name.value.trim(), protectedGroupId: f.protectedGroupId.value, accessGroupId: f.accessGroupId.value || null, encryption: f.encryption.checked };
    if (!v.protectedGroupId) throw new Error(t('ed.chooseProtected'));
    const i = state.draft.restricted.findIndex((x) => x.id === v.id); if (i >= 0) state.draft.restricted[i] = v; else state.draft.restricted.push(v);
  });
}
function editExemption() {
  openEditor(t('ed.exemptTitle'), `<div class="field"><label>${t('ed.exemptName')}</label><input name="name" required maxlength="100"></div><div class="field"><label>${t('ed.address')}</label><input name="address" required placeholder="10.74.1.60 / 10.74.5.0/24"></div><div class="field"><label>${t('ed.reason')}</label><input name="reason" required maxlength="500"></div><div class="field"><label>${t('ed.owner')}</label><input name="owner" required maxlength="100"></div><p class="hint">${t('ed.exemptHelp')}</p>`, (f) => {
    state.draft.exemptions.push({ id: uuid(), name: f.name.value.trim(), address: f.address.value.trim(), reason: f.reason.value.trim(), owner: f.owner.value.trim(), included: true });
  });
}
function editExternal() {
  openEditor(t('ed.externalTitle'), `<div class="field"><label>${t('ed.name')}</label><input name="name" required maxlength="64" placeholder="ext1"></div><div class="field"><label>${t('ed.ipv4')}</label><input name="address" required placeholder="10.74.1.60"></div><p class="hint">${t('ed.externalHelp')}</p>`, (f) => {
    state.draft.externalHosts.push({ id: uuid(), name: f.name.value.trim(), address: f.address.value.trim() });
  });
}

function editWave(id) {
  const d = state.draft;
  const w = d.waves.find((x) => x.id === id) || { id: uuid(), name: '', members: [] };
  const profileGroups = [...new Set([d.baseGroupId, d.noPolicyGroupId, d.boundaryGroupId, d.exclusionGroupId, d.settings.managementGroupId, ...d.restricted.flatMap((r) => [r.protectedGroupId, r.accessGroupId])].filter(Boolean))];
  const hosts = computers().filter((c) => c.kind !== 'dc');
  const memberRows = (members) => members.map((m, i) => `<div class="policy-line"><div>${html(name(m.groupId))}: ${html(m.computerIds.map(name).join(', '))}</div><button type="button" class="btn link" data-remove-member="${i}">${t('prof.remove')}</button></div>`).join('');
  const members = w.members.map((m) => ({ ...m }));
  openEditor(id ? t('waves.edit') : t('waves.editNew'), `<div class="field"><label>${t('waves.name')}</label><input name="name" required maxlength="100" value="${html(w.name)}"></div><div id="wave-members" class="policy-lines">${memberRows(members)}</div><div class="field"><label>${t('waves.group')}</label><select name="groupId">${profileGroups.map((g) => `<option value="${g}">${html(name(g))}</option>`).join('')}</select></div><div class="field"><label>${t('waves.computers')}</label><select name="computerIds" multiple size="6">${hosts.map((c) => `<option value="${c.id}">${html(c.name)}</option>`).join('')}</select></div><button type="button" class="btn soft" id="add-member">${t('waves.addMember')}</button>`, (f) => {
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
  openEditor(t('groups.add'), `<div class="field"><label>${t('groups.name')}</label><input name="name" required maxlength="64" placeholder="IPSEC-Web-Servers"></div><div class="field"><label>${t('groups.description')}</label><input name="description" maxlength="200"></div>`, (f) => {
    state.draft.newGroups.push({ id: uuid(), name: f.name.value.trim(), description: f.description.value.trim() });
  });
}

// ---------- rendering & events ----------
function render() {
  const st = state.status;
  document.documentElement.lang = lang === 'en' ? 'en' : 'pt-BR';
  $('#tagline').textContent = t('tagline');
  $('#brand-sub').textContent = t('brandSub');
  $('#plan-label').textContent = t('plan');
  $('#flow-label').textContent = t('flow');
  $('#side-note').innerHTML = `<b>${t('sideNote')}</b><p>${t('sideNoteText')}</p>`;
  $('#service-identity').textContent = st ? `${st.identity} @ ${st.host} · v${st.version}` : '';
  $('#deploy-state').textContent = st ? (st.deploymentEnabled ? t('deployOn') : t('readOnly')) : '';
  $('#lang-switch').innerHTML = `<button class="${lang === 'pt' ? 'active' : ''}" data-action="lang" data-lang="pt" aria-label="Português">PT</button><button class="${lang === 'en' ? 'active' : ''}" data-action="lang" data-lang="en" aria-label="English">EN</button>`;
  $('#domain-label').textContent = state.inventory ? state.inventory.domain : (st?.domain || t('notConnected'));
  $('#save-state').textContent = state.saved ? `${state.saved.name} · v${state.saved.version}` : state.draft ? t('draft') : t('noPlan');
  $('#snapshot-state').textContent = state.inventory ? t('inventoryAt', new Date(state.inventory.capturedAt).toLocaleTimeString(locale())) : t('noInventory');
  $('#nav').innerHTML = PAGES.map(([p, n]) => `<a href="#${p}" class="${state.page === p ? 'active' : ''}"><em>${n}</em>${t('nav.' + p)}</a>`).join('');
  $('#crumb').innerHTML = `<b>${html(t('nav.' + state.page))}</b>${state.busy ? ` · ${t('processing')}` : ''}`;
  const screens = { inventory: inventoryScreen, profiles: profilesScreen, simulate: simulateScreen, deploy: deployScreen, saved: savedScreen };
  $('#main').innerHTML = screens[state.page]();
  const ack = $('#ack'); if (ack) ack.addEventListener('change', () => { $('#apply-btn').disabled = !ack.checked || state.busy; });
  const approval = $('#approval'); if (approval) approval.addEventListener('input', () => { state.approval = approval.value; });
  const ackDrift = $('#ack-drift'); if (ackDrift) ackDrift.addEventListener('change', () => { state.ackDrift = ackDrift.checked; });
}
function setField(path, value) {
  const parts = path.split('.'); let o = state.draft; for (let i = 0; i < parts.length - 1; i++) o = o[parts[i]]; o[parts.at(-1)] = value;
}
document.addEventListener('input', (ev) => {
  const el = ev.target; const field = el.dataset.field;
  if (el.id === 'filter') { state.filter = el.value; render(); $('#filter').focus(); $('#filter').setSelectionRange(el.value.length, el.value.length); return; }
  if (!field || !state.draft) return;
  if (field === 'settings.secureSubnets') setField(field, el.value.split(',').map((s) => s.trim()).filter(Boolean));
  else if (field === 'settings.groupContainer') setField(field, el.value.trim() || null);
  else if (el.type === 'checkbox') setField(field, el.checked);
  else if (el.type === 'number') setField(field, Number(el.value));
  else setField(field, el.value === '' && field.endsWith('Id') ? null : el.value);
});
document.addEventListener('change', (ev) => { const el = ev.target; if (el.dataset.action === 'select-wave') { state.wave = Number(el.value); run(t('actions.assess'), reassess); return; } if ((el.dataset.field && el.tagName === 'SELECT') || el.type === 'checkbox') run(t('actions.assess'), reassess); });
document.addEventListener('click', (ev) => {
  const el = ev.target.closest('[data-action]'); if (!el) return;
  const a = el.dataset.action;
  if (a === 'lang') { setLang(el.dataset.lang); if (state.draft) run(t('actions.assess'), reassess); else render(); }
  if (a === 'go') setPage(el.dataset.page);
  if (a === 'close-editor') $('#editor').close();
  if (a === 'refresh-inventory') run(t('actions.inventory'), async () => { state.inventory = await api('/api/inventory/refresh', {}); state.status.lastReadError = null; if (state.draft) { state.draft.inventoryId = state.inventory.id; await reassess(); } });
  if (a === 'new-plan') run(t('actions.plan'), async () => { state.draft = newDraft(); state.saved = null; state.compilation = null; state.log = []; state.hosts = null; state.probe = null; await reassess(); setPage('profiles'); });
  if (a === 'edit-restricted') editRestricted(el.dataset.id);
  if (a === 'remove-restricted') { state.draft.restricted = state.draft.restricted.filter((x) => x.id !== el.dataset.id); run(t('actions.assess'), reassess); }
  if (a === 'edit-exemption') editExemption();
  if (a === 'remove-exemption') { state.draft.exemptions = state.draft.exemptions.filter((x) => x.id !== el.dataset.id); run(t('actions.assess'), reassess); }
  if (a === 'edit-external') editExternal();
  if (a === 'edit-wave') editWave(el.dataset.id);
  if (a === 'remove-wave') { state.draft.waves = state.draft.waves.filter((x) => x.id !== el.dataset.id); run(t('actions.assess'), reassess); }
  if (a === 'edit-group') editGroup();
  if (a === 'remove-group') { state.draft.newGroups = state.draft.newGroups.filter((x) => x.id !== el.dataset.id); run(t('actions.assess'), reassess); }
  if (a === 'prepare-groups') run(t('actions.apply'), async () => { if (!state.approval.trim()) throw new Error(t('approval.missing')); const g = await api(`/api/plans/${state.saved.id}/compile-groups`); const r = await api('/api/deploy', { planId: state.saved.id, version: state.saved.version, hash: g.sha256, action: 'groups', approval: state.approval }); state.log = await api(`/api/plans/${state.saved.id}/deployments`); toast(r.succeeded ? t('groups.prepared') : t('dep.failedSee', 'groups'), 8000); });
  if (a === 'wave') run(el.dataset.kind, async () => { if (el.dataset.kind === 'wave-apply' && !state.approval.trim()) throw new Error(t('approval.missing')); state.waveResult = await api('/api/wave', { planId: state.saved.id, version: state.saved.version, waveId: el.dataset.id, action: el.dataset.kind, approval: state.approval }); state.hosts = state.waveResult.verify; state.probe = state.waveResult.probe; state.log = await api(`/api/plans/${state.saved.id}/deployments`); });
  if (a === 'remove-external') { state.draft.externalHosts = state.draft.externalHosts.filter((x) => x.id !== el.dataset.id); run(t('actions.assess'), reassess); }
  if (a === 'explain') { const e = state.matrix.find((x) => x.initiator === el.dataset.i && x.responder === el.dataset.r); const box = $('#explain'); box.hidden = false; box.innerHTML = `<b>${html(e.initiator)} → ${html(e.responder)}: ${outcome(e.outcome)[0]}</b><br>${e.reasons.map(html).join('<br>')}`; }
  if (a === 'save') run(t('actions.save'), async () => { const body = { ...state.draft, expectedVersion: state.saved?.version ?? null }; state.saved = await api(state.saved ? `/api/plans/${state.saved.id}` : '/api/plans', body); state.draft = { ...state.saved.input, expectedVersion: null }; state.compilation = null; state.log = await api(`/api/plans/${state.saved.id}/deployments`); toast(t('dep.savedToast', state.saved.version)); });
  if (a === 'compile') run(t('actions.compile'), async () => { state.compilation = await api(`/api/plans/${state.saved.id}/compile`); });
  if (a === 'deploy') run(el.dataset.kind === 'apply' ? t('actions.apply') : t('actions.rollback'), async () => { if (el.dataset.kind === 'rollback' && !confirm(t('dep.confirmRollback'))) return; if (el.dataset.kind === 'apply' && !state.approval.trim()) throw new Error(t('approval.missing')); const r = await api('/api/deploy', { planId: state.saved.id, version: state.saved.version, hash: state.compilation.sha256, action: el.dataset.kind, approval: state.approval, acknowledgeDrift: state.ackDrift }); state.drift = null; state.ackDrift = false; state.log = await api(`/api/plans/${state.saved.id}/deployments`); toast(r.succeeded ? t('dep.done', el.dataset.kind) : t('dep.failedSee', el.dataset.kind), 8000); });
  if (a === 'hosts') run(el.dataset.kind, async () => { state.hosts = await api('/api/hosts', { planId: state.saved.id, version: state.saved.version, action: el.dataset.kind }); state.log = await api(`/api/plans/${state.saved.id}/deployments`); });
  if (a === 'drift') run(t('drift.check'), async () => { state.drift = await api(`/api/plans/${state.saved.id}/drift`); state.ackDrift = false; });
  if (a === 'renew-host') run(t('identity.renew'), async () => { const short = el.dataset.host.split('.')[0]; const id = computers().find((c) => c.name.toLowerCase() === short.toLowerCase())?.id; if (!id) throw new Error(el.dataset.host); await api('/api/hosts', { planId: state.saved.id, version: state.saved.version, action: 'refresh', computerIds: [id] }); const fresh = await api('/api/hosts', { planId: state.saved.id, version: state.saved.version, action: 'verify', computerIds: [id] }); state.hosts = (state.hosts || []).map((h) => fresh.find((x) => x.host === h.host) || h); state.log = await api(`/api/plans/${state.saved.id}/deployments`); });
  if (a === 'probe') run(t('actions.probe'), async () => { state.probe = await api('/api/probe', { planId: state.saved.id, version: state.saved.version, action: 'probe' }); state.log = await api(`/api/plans/${state.saved.id}/deployments`); });
  if (a === 'open-plan') run(t('actions.open'), async () => { state.saved = await api(`/api/plans/${el.dataset.id}`); state.draft = { ...state.saved.input, expectedVersion: null }; if (!state.inventory || state.inventory.id !== state.saved.inventory.id) state.inventory = state.saved.inventory; state.compilation = null; state.hosts = null; state.probe = null; state.log = await api(`/api/plans/${state.saved.id}/deployments`); await reassess(); setPage('profiles'); });
});
window.addEventListener('hashchange', () => { const p = location.hash.slice(1); if (PAGES.some((x) => x[0] === p)) { state.page = p; if (p === 'saved') run(t('actions.plans'), async () => { state.plans = await api('/api/plans'); }); else render(); } });
(async () => {
  try { state.status = await api('/api/status'); state.inventory = state.status.inventory; } catch (e) { $('#main').innerHTML = `<div class="notice red">${html(e.message)}</div>`; return; }
  const p = location.hash.slice(1); if (PAGES.some((x) => x[0] === p)) state.page = p;
  if (state.page === 'saved') state.plans = await api('/api/plans');
  render();
})();

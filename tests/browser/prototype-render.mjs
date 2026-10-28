// Render smoke for the 0.3 UI with a stubbed API: inventory, profiles, matrix and deploy screens. Not a browser test.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
const nodes = new Map();
const node = (id) => { if (!nodes.has(id)) nodes.set(id, { innerHTML: '', textContent: '', hidden: false, disabled: false, value: '', addEventListener() {}, querySelector: () => null, close() {}, showModal() {}, classList: { add() {}, remove() {} }, focus() {}, setSelectionRange() {} }); return nodes.get(id); };
const sid = (n) => `S-1-5-21-1-2-3-${n}`;
const comp = (name, ip, id, kind = 'server') => ({ id, name, kind, ip, sid: sid(id), dn: `CN=${name},DC=test`, parentDn: 'DC=test', memberIds: [], unresolvedMemberCount: 0 });
const objects = [comp('app1', '10.74.1.20', 'app1'), comp('db1', '10.74.1.30', 'db1'), comp('dc1', '10.74.1.10', 'dc1', 'dc'),
  { id: 'dcg', name: 'Domain Computers', kind: 'group', sid: 'S-1-5-21-1-2-3-515', memberIds: ['app1', 'db1'], dn: '', parentDn: '', unresolvedMemberCount: 0 },
  { id: 'sql', name: 'IPSEC-SQL-Servers', kind: 'group', sid: sid(9), memberIds: ['db1'], dn: '', parentDn: '', unresolvedMemberCount: 0 },
  { id: 'cli', name: 'IPSEC-SQL-Clients', kind: 'group', sid: sid(8), memberIds: ['app1'], dn: '', parentDn: '', unresolvedMemberCount: 0 }];
const inventory = { id: 'snap', domain: 'lab.ipsec.test', domainSid: 'S-1-5-21-1-2-3', capturedAt: new Date().toISOString(), objects, identity: 'tester', warnings: [] };
const assessment = { hosts: [{ computerId: 'app1', name: 'app1', ip: '10.74.1.20', profile: 'base', restrictedId: null, memberships: ['Domain Computers'], excluded: false, conflicts: [] }, { computerId: 'db1', name: 'db1', ip: '10.74.1.30', profile: 'restricted', restrictedId: 'r1', memberships: ['IPSEC-SQL-Servers'], excluded: false, conflicts: [] }, { computerId: 'dc1', name: 'dc1', ip: '10.74.1.10', profile: 'dc', restrictedId: null, memberships: [], excluded: false, conflicts: [] }],
  profiles: [{ key: 'base', name: 'IPsec autenticado', computerIds: ['app1'], addresses: [], notes: [] }, { key: 'restricted:r1', name: 'SQL', computerIds: ['db1'], addresses: [], notes: [] }, { key: 'exclusion', name: 'Quarentena', computerIds: [], addresses: [], notes: [] }], exemptAddresses: [], domainControllerAddresses: ['10.74.1.10'], warnings: ['aviso'] };
const matrix = [{ initiator: 'app1', responder: 'db1', outcome: 'encrypted', reasons: ['r'] }, { initiator: 'db1', responder: 'app1', outcome: 'encrypted', reasons: ['r'] }];
const compilation = { sha256: 'a'.repeat(64), gpos: [{ name: 'IpsecStudio-Base', profile: 'base', applyGroup: 'Domain Computers', denyGroupSids: [sid(9)], inbound: 'Require', outbound: 'Request', encryption: false, computerIds: ['app1'] }, { name: 'IpsecStudio-Restricted-SQL', profile: 'restricted', applyGroup: 'IPSEC-SQL-Servers', denyGroupSids: [], inbound: 'Require', outbound: 'Require', encryption: true, computerIds: ['db1'] }], exemptAddresses: ['10.74.1.10'], applyScript: 'New-GPO', rollbackScript: 'Remove-GPO' };
const saved = { id: '11111111-1111-1111-1111-111111111111', version: 1, name: 'Lab', input: null, inventory };
const routes = (url, body) => url.endsWith('/status') ? { version: '0.3.0', identity: 'LAB\\op', host: 'admin1', domain: 'lab.ipsec.test', token: 't', inventory, deploymentEnabled: true, probePort: 5985 }
  : url.includes('/simulate') ? { assessment, matrix, wave: 0, waveHosts: assessment.hosts } : url.endsWith('/compile') ? compilation : url.endsWith('/deployments') ? [{ utc: new Date().toISOString(), action: 'apply', version: 1, succeeded: true, output: '{}' }]
  : url === '/api/plans' && body ? { ...saved, input: body } : url === '/api/plans' ? [saved] : url.endsWith('/drift') ? { clean: false, gpos: [{ name: 'IpsecStudio-Base', exists: true, differences: ['IpsecStudio-Main: inbound esperado Require, encontrado Request'] }], external: [{ name: 'Corp-Legacy', ruleCount: 1, links: ['DC=test'] }], utc: new Date().toISOString() } : url.endsWith('/probe') ? [{ initiator: 'app1', responder: 'db1', expected: 'encrypted', observed: 'encrypted', match: true, detail: 'tcp=True cipher=AES256' }] : {};
const storage = new Map();
const context = vm.createContext({ console, localStorage: { getItem: (k) => storage.get(k) ?? null, setItem: (k, v) => storage.set(k, v) }, crypto: { randomUUID: () => '00000000-0000-0000-0000-000000000000' }, setTimeout, clearTimeout, Date, confirm: () => true, location: { hash: '#inventory' }, document: { querySelector: node, addEventListener() {}, documentElement: {} }, window: { addEventListener() {} },
  fetch: async (url, init) => ({ ok: true, json: async () => routes(url, init?.body ? JSON.parse(init.body) : null) }) });
vm.runInContext(readFileSync(new URL('../../app/IpsecStudio/wwwroot/i18n.js', import.meta.url), 'utf8'), context);
vm.runInContext(readFileSync(new URL('../../app/IpsecStudio/wwwroot/app.js', import.meta.url), 'utf8'), context);
await new Promise((r) => setTimeout(r, 20));
const main = node('#main');
assert.ok(main.innerHTML.includes('Computadores e grupos') && main.innerHTML.includes('app1'), 'inventory renders');
vm.runInContext(`state.draft = newDraft(); state.draft.restricted.push({ id: 'r1', name: 'SQL', protectedGroupId: 'sql', accessGroupId: 'cli', encryption: true, included: true });`, context);
await vm.runInContext('reassess()', context);
vm.runInContext("state.page='profiles'; render()", context);
assert.ok(main.innerHTML.includes('Servidores restritos') && main.innerHTML.includes('IPSEC-SQL-Servers') && main.innerHTML.includes('Quarentena') && main.innerHTML.includes('Fronteira') && main.innerHTML.includes('10.74.1.10'), 'five profiles with DC exemption');
assert.ok(main.innerHTML.includes('Ondas') && main.innerHTML.includes('Grupos novos'), 'waves and new groups cards');
vm.runInContext("state.draft.waves.push({ id: 'w1', name: 'Piloto', members: [{ groupId: 'cli', computerIds: ['app1'] }] }); state.draft.newGroups.push({ id: 'g1', name: 'IPSEC-Web', description: '' }); render()", context);
assert.ok(main.innerHTML.includes('1. Piloto') && main.innerHTML.includes('IPSEC-Web'), 'wave and group listed');
vm.runInContext("state.page='simulate'; render()", context);
assert.ok(main.innerHTML.includes('class="o-enc"') && main.innerHTML.includes('Perfil efetivo por computador') && main.innerHTML.includes('Restrito'), 'matrix renders outcomes');
assert.ok(main.innerHTML.includes('wave-select') && main.innerHTML.includes('Após a onda 1'), 'per-wave selector');
vm.runInContext("state.hosts=[{host:'app1.lab.ipsec.test',reachable:true,output:JSON.stringify({healthy:true,studioRules:[],gpos:['IpsecStudio-Base'],enabledIpsecRules:2}),identityState:'pending',tgtStartUtc:'2026-10-28T10:00:00Z',lastGroupChangeUtc:'2026-10-28T11:00:00Z'}]", context);
vm.runInContext("state.saved={...saved, input: state.draft}; state.compilation = compilation; state.probe = routes_probe; state.page='deploy'; render()".replace('saved,', JSON.stringify(saved) + ',').replace('compilation;', JSON.stringify(compilation) + ';').replace('routes_probe', JSON.stringify(routes('/api/probe'))), context);
assert.ok(main.innerHTML.includes('IpsecStudio-Restricted-SQL') && main.innerHTML.includes('Aplicar GPOs') && main.innerHTML.includes('Reverter') && main.innerHTML.includes('1/1 coincidem') && main.innerHTML.includes('New-GPO'), 'deploy panel with compilation, actions and probe');
vm.runInContext("state.drift = routes_drift; render()".replace('routes_drift', JSON.stringify(routes('/x/drift'))), context);
assert.ok(main.innerHTML.includes('Drift detectado') && main.innerHTML.includes('Corp-Legacy') && main.innerHTML.includes('id="ack-drift"') && main.innerHTML.includes('encontrado Request'), 'drift panel with acknowledgement');
assert.ok(main.innerHTML.includes('identidade pendente') && main.innerHTML.includes('data-action="renew-host"'), 'identity badge and per-host renew');
assert.ok(main.innerHTML.includes('id="approval"') && main.innerHTML.includes('Aplicar onda') && main.innerHTML.includes('Preparar grupos no AD') && main.innerHTML.includes('não aplicada'), 'approval note, wave actions and group preparation');
// language switch: English strings, persisted choice, pt-BR default restored
vm.runInContext("setLang('en'); state.page='simulate'; render()", context);
assert.ok(main.innerHTML.includes('What will Windows do') && main.innerHTML.includes('Encrypted') && node('#nav').innerHTML.includes('Simulate'), 'english UI');
assert.equal(storage.get('ipsec-studio-lang'), 'en', 'language persisted');
vm.runInContext("setLang('pt'); render()", context);
assert.ok(main.innerHTML.includes('O que o Windows fará') && storage.get('ipsec-studio-lang') === 'pt', 'pt-BR restored');
console.log('PASS 0.3 UI renders inventory, five profiles, outcome matrix, deployment panel and PT/EN switch (stubbed API; no browser)');

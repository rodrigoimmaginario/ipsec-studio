// Probe details use the exact formats observed in the lab regressions (evidence/regression).
// Captures the five "step" screens of the site from the real IPsec Studio UI (app/IpsecStudio/wwwroot), served locally
// with a sample API in the lab's format, plus the share image (site/img/og.png). No Azure, no AD: the data is a sample.
// Usage: node tests/site/capture-screens.mjs   (needs Microsoft Edge; EDGE env var overrides the path)
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
const run = promisify(execFile);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const repo = path.resolve(here, '..', '..');
const wwwroot = path.join(repo, 'app', 'IpsecStudio', 'wwwroot');
const site = path.join(repo, 'site');
const VERSION = fs.readFileSync(path.join(repo, 'app/IpsecStudio/IpsecStudio.csproj'), 'utf8').match(/<Version>([^<]+)<\/Version>/)[1];
const out = path.join(site, 'img');
const EDGE = process.env.EDGE || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const PORT = 5181;

// ---------- sample directory (lab format) ----------
const D = 'S-1-5-21-11-22-33';
const sid = (n) => `${D}-${n}`;
const pc = (id, kind, ip, os, n) => ({ id, name: id, kind, ip, os, sid: sid(n), dn: `CN=${id},CN=Computers,DC=lab,DC=ipsec,DC=test`, parentDn: 'CN=Computers,DC=lab,DC=ipsec,DC=test', memberIds: [], unresolvedMemberCount: 0 });
const grp = (id, name, members, n) => ({ id, name, kind: 'group', sid: n === 515 ? `${D}-515` : sid(n), memberIds: members, dn: `CN=${name},OU=IPsec,DC=lab,DC=ipsec,DC=test`, parentDn: 'OU=IPsec,DC=lab,DC=ipsec,DC=test', unresolvedMemberCount: 0 });
const computersList = [
  pc('dc1', 'dc', '10.74.1.10', 'Windows Server 2022', 1001), pc('dc2', 'dc', '10.74.1.11', 'Windows Server 2022', 1002),
  pc('app1', 'server', '10.74.1.20', 'Windows Server 2022', 1003), pc('db1', 'server', '10.74.1.30', 'Windows Server 2022', 1004),
  pc('fs1', 'server', '10.74.1.40', 'Windows Server 2022', 1005), pc('admin1', 'workstation', '10.74.1.70', 'Windows Server 2022', 1006),
  pc('wsus1', 'server', '10.74.1.80', 'Windows Server 2022', 1007), pc('win10', 'workstation', '10.74.1.51', 'Windows 10 Enterprise', 1008),
  pc('win11', 'workstation', '10.74.1.52', 'Windows 11 Enterprise', 1009),
];
const groupsList = [
  grp('dcg', 'Domain Computers', ['app1', 'db1', 'fs1', 'admin1', 'wsus1', 'win10', 'win11'], 515),
  grp('rh-srv', 'IPSEC-RH-Servidores', ['db1'], 2001), grp('rh-ws', 'IPSEC-RH-Estacoes', ['win11'], 2002),
  grp('bnd', 'IPSEC-Fronteira', ['wsus1'], 2003), grp('qua', 'IPSEC-Quarentena', [], 2004), grp('mgmt', 'IPSEC-Gestao', ['admin1'], 2005),
];
const gpoObjects = ['Default Domain Policy', 'Default Domain Controllers Policy', 'Workstations Baseline'].map((name, i) => ({ id: 'gpo' + i, name, kind: 'gpo', sid: null, memberIds: [], dn: '', parentDn: '', unresolvedMemberCount: 0 }));
const inventory = { id: 'snap-lab', domain: 'lab.ipsec.test', domainSid: D, capturedAt: '2026-10-30T18:40:00Z', objects: [...computersList, ...groupsList, ...gpoObjects], identity: 'LAB\\laboperator', warnings: [] };

const T = {
  pt: { plan: 'Isolamento da rede interna', rh: 'Banco de dados do RH', pilot: 'Piloto', servers: 'Servidores', all: 'Estações', printer: 'Impressora do 2º andar', noIpsec: 'Sem suporte a IPsec', approval: 'CHG-1042 — janela de manutenção aprovada pelo comitê de mudanças',
    r: { dc: 'Controlador de domínio isento por endereço.', prn: 'Destino isento por endereço (sem IPsec).', enc: 'Destino restrito ao grupo do RH: o iniciador está autorizado e o tráfego é cifrado.', mg: 'Grupo de gestão autorizado nos servidores restritos.', deny: 'Destino restrito ao grupo do RH: o iniciador não está no grupo.', auth: 'Os dois lados autenticam com Kerberos.', bnd: 'Fronteira: aceita IPsec e conexões em claro.', noip: 'O iniciador não tem IPsec e o destino exige autenticação.' } },
  en: { plan: 'Internal network isolation', rh: 'HR database', pilot: 'Pilot', servers: 'Servers', all: 'Workstations', printer: '2nd floor printer', noIpsec: 'No IPsec support', approval: 'CHG-1042 — maintenance window approved by the change board',
    r: { dc: 'Domain controller exempted by address.', prn: 'Responder exempted by address (no IPsec).', enc: 'Responder restricted to the HR group: the initiator is authorized and traffic is encrypted.', mg: 'Management group authorized on restricted servers.', deny: 'Responder restricted to the HR group: the initiator is not in the group.', auth: 'Both sides authenticate with Kerberos.', bnd: 'Boundary: accepts IPsec and clear connections.', noip: 'The initiator has no IPsec and the responder requires authentication.' } },
};

function draft(lang) {
  const t = T[lang];
  return { name: t.plan, inventoryId: inventory.id, expectedVersion: null,
    settings: { secureSubnets: ['10.74.1.0/24'], exemptDomainControllers: true, exemptIcmp: true, requireOutbound: false, replicationWaitSeconds: 180, linkTarget: 'domain', groupContainer: 'OU=IPsec,DC=lab,DC=ipsec,DC=test',
      crypto: { mainModeEncryption: 'AES256', mainModeHash: 'SHA256', keyExchange: 'DH14', quickModeEncryption: 'AES256', quickModeIntegrity: 'SHA256', mainModeLifetimeMinutes: 180, quickModeLifetimeMinutes: 60 }, managementGroupId: 'mgmt' },
    baseGroupId: 'dcg', baseMode: 'secureRequest',
    restricted: [{ id: 'r1', name: t.rh, protectedGroupId: 'rh-srv', accessGroupId: 'rh-ws', encryption: true, included: true }],
    exemptions: [{ id: 'e1', name: t.printer, address: '10.74.1.90', reason: t.noIpsec, owner: 'Infra', included: true }],
    noPolicyGroupId: null, boundaryGroupId: 'bnd', exclusionGroupId: 'qua', externalHosts: [], newGroups: [],
    waves: [
      { id: 'w1', name: t.pilot, members: [{ groupId: 'dcg', computerIds: ['win10', 'app1'] }] },
      { id: 'w2', name: t.servers, members: [{ groupId: 'dcg', computerIds: ['fs1'] }, { groupId: 'rh-srv', computerIds: ['db1'] }, { groupId: 'bnd', computerIds: ['wsus1'] }] },
      { id: 'w3', name: t.all, members: [{ groupId: 'dcg', computerIds: ['win11', 'admin1'] }] },
    ] };
}

const profileOf = { dc1: 'dc', dc2: 'dc', app1: 'base', fs1: 'base', win10: 'base', win11: 'base', admin1: 'management', db1: 'restricted', wsus1: 'boundary' };
const membershipsOf = (id) => groupsList.filter((g) => g.memberIds.includes(id)).map((g) => g.name);
function assessment(lang) {
  return {
    hosts: computersList.map((c) => ({ computerId: c.id, name: c.name, ip: c.ip, profile: profileOf[c.id], restrictedId: c.id === 'db1' ? 'r1' : null, memberships: membershipsOf(c.id), excluded: false, conflicts: [] })),
    profiles: [
      { key: 'base', name: 'base', computerIds: ['app1', 'fs1', 'win10', 'win11'], addresses: [], notes: [] },
      { key: 'restricted:r1', name: T[lang].rh, computerIds: ['db1'], addresses: [], notes: [] },
      { key: 'boundary', name: 'boundary', computerIds: ['wsus1'], addresses: [], notes: [] },
      { key: 'exclusion', name: 'exclusion', computerIds: [], addresses: [], notes: [] },
    ],
    exemptAddresses: ['10.74.1.90'], domainControllerAddresses: ['10.74.1.10', '10.74.1.11'], warnings: [] };
}
// The responder decides (Studio rules): DCs and the printer are exempt by address; the HR database accepts only the HR group
// and management, encrypted; the boundary accepts everyone; the base requires authentication.
const ipsec = new Set(['win10', 'win11', 'admin1', 'app1', 'fs1', 'db1', 'wsus1']);
function outcome(i, r, lang) {
  const R = T[lang].r;
  if (i === 'dc1' || r === 'dc1') return ['clear', R.dc];
  if (r === 'prn1') return ['clear', R.prn];
  if (r === 'db1') return i === 'win11' ? ['encrypted', R.enc] : i === 'admin1' ? ['encrypted', R.mg] : ['blocked', R.deny];
  if (r === 'wsus1') return ipsec.has(i) ? ['authenticated', R.bnd] : ['clear', R.bnd];
  return ipsec.has(i) ? ['authenticated', R.auth] : ['blocked', R.noip];
}
const matrixIds = ['win10', 'win11', 'admin1', 'app1', 'fs1', 'db1', 'wsus1', 'dc1', 'prn1'];
const matrix = (lang) => matrixIds.flatMap((i) => matrixIds.filter((r) => r !== i).map((r) => { const [o, why] = outcome(i, r, lang); return { initiator: i, responder: r, outcome: o, reasons: [why] }; }));

const compilation = {
  sha256: '3f9a1c7e5b2d48a6c0e91f37d5a2b8c4e6f1a09d3b7c52e8f4a1d6b9c0e2f7a3',
  gpos: [
    { name: 'IpsecStudio-Base', profile: 'base', applyGroup: 'Domain Computers', denyGroupSids: [sid(2001), sid(2003), sid(2004)], inbound: 'Require', outbound: 'Request', encryption: false, computerIds: ['app1', 'fs1', 'win10', 'win11'] },
    { name: 'IpsecStudio-Restricted-RH', profile: 'restricted', applyGroup: 'IPSEC-RH-Servidores', denyGroupSids: [], inbound: 'Require', outbound: 'Request', encryption: true, computerIds: ['db1'] },
    { name: 'IpsecStudio-Boundary', profile: 'boundary', applyGroup: 'IPSEC-Fronteira', denyGroupSids: [], inbound: 'Request', outbound: 'Request', encryption: false, computerIds: ['wsus1'] },
    { name: 'IpsecStudio-Management', profile: 'management', applyGroup: 'IPSEC-Gestao', denyGroupSids: [], inbound: 'Require', outbound: 'Request', encryption: false, computerIds: ['admin1'] },
    { name: 'IpsecStudio-Quarantine', profile: 'quarantine', applyGroup: 'IPSEC-Quarentena', denyGroupSids: [], inbound: 'Require', outbound: 'Request', encryption: false, computerIds: [] },
  ],
  exemptAddresses: ['10.74.1.10', '10.74.1.11', '10.74.1.90'],
  applyScript: "New-GPO -Name 'IpsecStudio-Base' ...", rollbackScript: "Remove-GPO -Name 'IpsecStudio-Base' ...",
};
const log = (lang) => [
  { utc: '2026-10-30T19:02:00Z', action: 'wave-apply', waveId: 'w1', version: 3, succeeded: true, expansionAllowed: true, approval: T[lang].approval, output: '{"gpos":5,"hosts":2,"probe":"4/4"}' },
  { utc: '2026-10-30T19:31:00Z', action: 'wave-apply', waveId: 'w2', version: 3, succeeded: true, expansionAllowed: true, approval: T[lang].approval, output: '{"gpos":5,"hosts":3,"probe":"6/6"}' },
];
const verify = ['win10', 'app1', 'fs1', 'db1', 'wsus1'].map((h) => ({ host: `${h}.lab.ipsec.test`, reachable: true, identityState: 'synced', tgtStartUtc: '2026-10-30T19:33:00Z', lastGroupChangeUtc: '2026-10-30T18:50:00Z',
  output: JSON.stringify({ healthy: true, studioRules: [{ status: 'OK', statusText: 'OK' }], gpos: [h === 'db1' ? 'IpsecStudio-Restricted-RH' : h === 'wsus1' ? 'IpsecStudio-Boundary' : 'IpsecStudio-Base'], enabledIpsecRules: 3, authorizationList: h === 'db1' ? 'IPSEC-RH-Estacoes, IPSEC-Gestao' : '' }) }));
const probe = [
  ['win11', 'db1', 'encrypted', 'encrypted', { esp: 24, clear: 0, attempts: 0, match: true }, 'tcp=True cipher=AES256 integrity=SHA256 auth=Kerberos'],
  ['win10', 'db1', 'blocked', 'blocked', { esp: 0, clear: 0, attempts: 2, match: true }, 'tcp=False cipher=- integrity=- auth=-'],
  ['win10', 'app1', 'authenticated', 'authenticated', { esp: 16, clear: 0, attempts: 0, match: true }, 'tcp=True cipher=None integrity=SHA256 auth=Kerberos'],
  ['app1', 'fs1', 'authenticated', 'authenticated', { esp: 18, clear: 0, attempts: 0, match: true }, 'tcp=True cipher=None integrity=SHA256 auth=Kerberos'],
  ['win10', 'wsus1', 'authenticated', 'authenticated', { esp: 12, clear: 0, attempts: 0, match: true }, 'tcp=True cipher=None integrity=SHA256 auth=Kerberos'],
  ['client-no', 'app1', 'blocked', 'blocked', { esp: 0, clear: 0, attempts: 2, match: true }, 'tcp=False cipher=- integrity=- auth=-'],
].map(([initiator, responder, expected, observed, wire, detail]) => ({ initiator, responder, expected, observed, match: true, detail, wire }));

// ---------- server ----------
const SHOT = `(async () => {
  const q = new URLSearchParams(location.search); const shot = q.get('shot'); const lg = q.get('lang') === 'en' ? 'en' : 'pt';
  setLang(lg);
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  while (!state.inventory) await wait(20);
  const data = await (await fetch('/__plan?lang=' + lg)).json();
  state.draft = data.draft; await reassess();
  state.saved = { ...data.saved, input: state.draft }; state.log = data.log;
  if (shot === 'deploy' || shot === 'verify') { state.compilation = data.compilation; state.approval = data.approval; }
  if (shot === 'verify') { state.hosts = data.verify; state.probe = data.probe; state.drift = data.drift; }
  state.page = shot === 'verify' ? 'deploy' : shot; render();
  // Headless screenshots ignore scrolling: report where the probe card is, and the script crops the full-page capture.
  if (shot === 'verify') { const h = [...document.querySelectorAll('#main h2')].find((x) => x.textContent === t('dep.probeTitle')); const b = h.closest('.card').getBoundingClientRect(); await fetch('/__rect', { method: 'POST', body: JSON.stringify({ bottom: Math.round(b.bottom + scrollY) }) }); }
  document.title = 'ready';
})();`;
// Share image: the opening alone (question, answer, the product with the outcome chips), 1200x630.
const OG_CSS = `html, body { overflow: hidden !important; }
.progress, .top, .to-top, .foot, main > section:not(.hero), .facts, .ctas { display: none !important; }
.hero { max-width: none !important; height: 630px; padding: 0 56px 0 60px !important; gap: 40px !important; grid-template-columns: 1fr 1.05fr !important; }
.hero h1 { font-size: 54px !important; margin-bottom: 18px !important; }
.hero-lead { font-size: 24px !important; }
.hero-sub { font-size: 18px !important; margin: 0 !important; }
.chip { animation: none !important; }`;
const mime = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.png': 'image/png', '.svg': 'image/svg+xml', '.pdf': 'application/pdf' };
const json = (res, body) => { res.writeHead(200, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(body)); };
let probeRect = null;
const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://x'); const p = url.pathname; const lang = req.headers['x-lang'] === 'en' ? 'en' : 'pt';
  if (p === '/__shot.js') { res.writeHead(200, { 'Content-Type': mime['.js'] }); return res.end(SHOT); }
  if (p === '/__rect') { let b = ''; req.on('data', (c) => (b += c)); req.on('end', () => { probeRect = JSON.parse(b); res.end(); }); return; }
  if (p === '/__og.css') { res.writeHead(200, { 'Content-Type': mime['.css'] }); return res.end(OG_CSS); }
  if (p === '/__plan') { const lg = url.searchParams.get('lang') === 'en' ? 'en' : 'pt'; const d = draft(lg);
    return json(res, { draft: d, saved: { id: '7c1e2a90-4b1d-4f6a-9d0e-2a5b8c3f1e77', version: 3, name: T[lg].plan, inventory }, log: log(lg), compilation, approval: T[lg].approval, verify, probe, drift: { clean: true, gpos: compilation.gpos.map((g) => ({ name: g.name, exists: true, differences: [] })), external: [], utc: '2026-10-30T19:40:00Z' } }); }
  if (p === '/api/status') return json(res, { version: VERSION, identity: 'LAB\\laboperator', host: 'admin1', domain: 'lab.ipsec.test', token: 't', inventory, deploymentEnabled: true, probePort: 5985 });
  if (p === '/api/plans/simulate') return json(res, { assessment: assessment(lang), matrix: matrix(lang), matrixIds, matrixLimit: 30, wave: 0, waveHosts: assessment(lang).hosts });
  if (p.startsWith('/api/')) return json(res, []);
  const base = p.startsWith('/site/') ? site : wwwroot;
  let file = path.join(base, decodeURIComponent(p.replace(/^\/site/, '')));
  if (p === '/' || p === '/site/') file = path.join(base, 'index.html');
  if (!file.startsWith(base) || !fs.existsSync(file)) { res.writeHead(404); return res.end(); }
  let body = fs.readFileSync(file);
  if (file.endsWith('index.html')) {
    let s = body.toString('utf8');
    s = base === wwwroot ? s.replace('</body>', '<script src="/__shot.js"></script></body>') : url.searchParams.has('og') ? s.replace('</head>', '<link rel="stylesheet" href="/__og.css"></head>') : s;
    body = Buffer.from(s, 'utf8');
  }
  res.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream' }); res.end(body);
});

// Async on purpose: the sample API is served by this same process, so blocking it would deadlock Edge.
const shoot = async (file, url, size) => {
  const target = path.join(out, file);
  if (fs.existsSync(target)) fs.rmSync(target);
  await run(EDGE, ['--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-prefers-reduced-motion', `--user-data-dir=${path.join(repo, 'artifacts', 'edge-shots')}`,
    `--window-size=${size}`, '--virtual-time-budget=15000', `--screenshot=${target}`, url], { timeout: 60000 }).catch(() => {});
  for (let i = 0; i < 100 && !fs.existsSync(target); i++) await sleep(200);
  if (!fs.existsSync(target)) throw new Error('no screenshot: ' + file);
  console.log(`${file}  ${Math.round(fs.statSync(target).size / 1024)} KB`);
};

// Crop a 1280-wide band (System.Drawing, present on Windows).
const crop = async (file, y, h) => {
  const tmp = file + '.full.png'; fs.renameSync(file, tmp);
  const ps = `Add-Type -AssemblyName System.Drawing; $s=[System.Drawing.Bitmap]::FromFile('${tmp}'); $d=$s.Clone((New-Object System.Drawing.Rectangle(0,${y},1280,${h})),$s.PixelFormat); $s.Dispose(); $d.Save('${file}',[System.Drawing.Imaging.ImageFormat]::Png); $d.Dispose()`;
  await run('powershell.exe', ['-NoProfile', '-Command', ps]);
  fs.rmSync(tmp);
  console.log(`  cropped at y=${y}: ${Math.round(fs.statSync(file).size / 1024)} KB`);
};

fs.mkdirSync(out, { recursive: true });
server.listen(PORT, '127.0.0.1', async () => {
  try {
    const steps = ['inventory', 'profiles', 'simulate', 'deploy', 'verify'];
    for (const lang of ['pt', 'en']) for (const [i, s] of steps.entries()) {
      const file = `passo-${i + 1}-${lang}.png`;
      probeRect = null;
      await shoot(file, `http://127.0.0.1:${PORT}/?shot=${s}&lang=${lang}#${s === 'verify' ? 'deploy' : s}`, s === 'verify' ? '1280,3000' : '1280,800');
      if (s === 'verify') await crop(path.join(out, file), Math.max(0, probeRect.bottom + 24 - 800), 800);
    }
    await shoot('og.png', `http://127.0.0.1:${PORT}/site/?og&lang=pt&v=a`, '1200,630');
  } finally { server.close(); }
});

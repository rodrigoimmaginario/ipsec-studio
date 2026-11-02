// Captures the design concepts as a reader sees them while scrolling (Edge via the DevTools protocol; no dependencies).
// Usage: node design/conceitos/shoot.mjs <outDir> [c1|c2|c3 ...]
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const out = path.resolve(process.argv[2] || 'shots');
const only = process.argv.slice(3);
const EDGE = process.env.EDGE || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const PORT = 9333;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Each stop: [name, JS that scrolls]; the screenshot is taken after the page settles.
const at = (sel, off = 0) => `(() => { const el = document.querySelector(${JSON.stringify(sel)}); scrollTo(0, el.getBoundingClientRect().top + scrollY - ${off}); })()`;
const STOPS = {
  c1: [['01-abertura', 'scrollTo(0,0)'], ['02-pergunta', at('#pergunta')], ['03-como-intro', at('.scene[data-i="0"]', 300)], ['04-bloqueado', at('.scene[data-i="1"]', 300)],
    ['05-cifrado', at('.scene[data-i="3"]', 300)], ['06-fronteira', at('.scene[data-i="4"]', 300)], ['07-passos', at('#passos')], ['08-passo-1', at('.step-texts li[data-i="0"]', 300)],
    ['09-passo-3', at('.step-texts li[data-i="2"]', 300)], ['10-passo-5', at('.step-texts li[data-i="4"]', 300)]],
  c2: [['01-abertura', 'scrollTo(0,0)'], ['02-pergunta', at('#pergunta', 60)], ['03-como', at('#como', 60)], ['04-como-cifrado', `${at('#como', 60)};document.querySelector('[data-tab="enc"]').click()`],
    ['05-passos', at('#passos', 60)], ['06-passo-3', `${at('#passos', 60)};document.querySelector('[data-go="2"]').click()`], ['07-menu-rolado', 'scrollTo(0, 1600); scrollTo(0, 1500)']],
  c3: [['01-abertura', 'scrollTo(0,0)'], ['02-como', at('#como', 30)], ['03-como-legenda', `${at('#como', 30)};document.querySelector('.legend li[data-state="enc"]').dispatchEvent(new MouseEvent('mouseenter'))`],
    ['04-notas', at('#notas', 30)], ['05-passos', at('#passos', 30)], ['06-passo-4', at('.step[data-i="3"]', 30)]],
};

async function main() {
  fs.mkdirSync(out, { recursive: true });
  const userDir = path.join(here, '..', '..', 'artifacts', 'edge-cdp');
  const edge = spawn(EDGE, ['--headless=new', '--disable-gpu', '--hide-scrollbars', `--remote-debugging-port=${PORT}`, `--user-data-dir=${userDir}`, 'about:blank'], { stdio: 'ignore' });
  try {
    let ver; for (let i = 0; i < 50 && !ver; i++) { try { ver = await (await fetch(`http://127.0.0.1:${PORT}/json/version`)).json(); } catch { await sleep(200); } }
    const targets = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
    const page = targets.find((t) => t.type === 'page');
    const ws = new WebSocket(page.webSocketDebuggerUrl);
    await new Promise((r) => ws.addEventListener('open', r, { once: true }));
    let id = 0; const waiting = new Map();
    ws.addEventListener('message', (m) => { const d = JSON.parse(m.data); if (d.id && waiting.has(d.id)) { waiting.get(d.id)(d); waiting.delete(d.id); } });
    const cdp = (method, params = {}) => new Promise((r) => { const n = ++id; waiting.set(n, r); ws.send(JSON.stringify({ id: n, method, params })); });
    await cdp('Page.enable'); await cdp('Runtime.enable');
    for (const [concept, stops] of Object.entries(STOPS)) {
      if (only.length && !only.includes(concept)) continue;
      for (const [device, w, h, mobile] of [['desk', 1440, 900, false], ['mob', 390, 844, true]]) {
        await cdp('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile });
        const url = 'file:///' + path.join(here, concept, 'index.html').replace(/\\/g, '/');
        await cdp('Page.navigate', { url }); await sleep(1500);
        for (const [name, js] of stops) {
          if (device === 'mob' && !['01-abertura', '03-como', '04-bloqueado', '05-passos', '09-passo-3', '02-como', '06-passo-3', '06-passo-4', '05-cifrado'].includes(name)) continue;
          await cdp('Runtime.evaluate', { expression: js }); await sleep(1800);
          const shot = await cdp('Page.captureScreenshot', { format: 'png' });
          const file = path.join(out, `${concept}-${device}-${name}.png`);
          fs.writeFileSync(file, Buffer.from(shot.result.data, 'base64'));
          console.log(path.basename(file));
        }
      }
    }
    ws.close();
  } finally { edge.kill(); }
}
main().catch((e) => { console.error(e); process.exit(1); });

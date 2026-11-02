// Captures the site as a reader sees it while scrolling, on desktop and mobile (Edge via the DevTools protocol; no dependencies).
// Usage: node tests/site/scroll-shots.mjs <outDir>
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
  site: [['01-abertura', 'scrollTo(0,0)'], ['02-pergunta', at('#pergunta', 70)], ['03-como-antes', at('.scene[data-i="0"]', 330)], ['04-como-cifrado', at('.scene[data-i="3"]', 330)],
    ['05-protecao', at('#protecao', 70)], ['06-porque', at('#por-que', 70)], ['07-passo-1', at('.step-text[data-i="0"]', 330)], ['08-passo-4', at('.step-text[data-i="3"]', 330)],
    ['09-prova', at('#prova', 70)], ['11-baixar', at('#baixar', 70)]],
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
        const url = 'file:///' + path.join(here, '..', '..', 'site', 'index.html').replace(/\\/g, '/');
        await cdp('Page.navigate', { url }); await sleep(1500);
        for (const [name, js] of stops) {
          if (device === 'mob' && !['01-abertura', '03-como', '08-passo-4', '09-prova', '10-origem', '04-bloqueado', '05-passos', '09-passo-3', '02-como', '06-passo-3', '06-passo-4', '05-cifrado'].includes(name)) continue;
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

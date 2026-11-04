// Regenerate derived brand assets from the approved, canonical wwwroot/brand.svg.
// Requires Chrome/Chromium (BROWSER_EXE can select it). No network, packages or account profile.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import net from 'node:net';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
const root=fileURLToPath(new URL('../',import.meta.url));
const app=path.join(root,'app/IpsecStudio'),web=path.join(app,'wwwroot'),site=path.join(root,'site/img');
const primary=fs.readFileSync(path.join(web,'brand.svg'),'utf8').replace(/\r\n/g,'\n');
const inverse=primary.replaceAll('#14324a','#ffffff').replaceAll('#087e80','#78c9bf');
const inner=inverse.replace(/<svg[^>]*>/,'').replace(/<title>[^<]*<\/title>/,'').replace(/<\/svg>\s*$/,'');
const favicon=`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 80 80"><title>IPsec Studio</title><rect width="80" height="80" rx="18" fill="#14324a"/><g transform="translate(8 8)">${inner}</g></svg>\n`;
const candidates=[process.env.BROWSER_EXE,'C:/Program Files/Google/Chrome/Application/chrome.exe','C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe','/usr/bin/google-chrome','/usr/bin/chromium','/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'].filter(Boolean);
const exe=candidates.find(p=>fs.existsSync(p));if(!exe)throw Error('Set BROWSER_EXE to Chrome or Chromium.');
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'ipsec-icon-'));
const listener=net.createServer();await new Promise(r=>listener.listen(0,'127.0.0.1',r));const port=listener.address().port;await new Promise(r=>listener.close(r));
const chrome=spawn(exe,['--headless=new','--disable-gpu','--remote-debugging-address=127.0.0.1',`--remote-debugging-port=${port}`,`--user-data-dir=${profile}`,'about:blank'],{stdio:'ignore'});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));let ws;
try{
 let pages;for(let i=0;i<60;i++){try{pages=await(await fetch(`http://127.0.0.1:${port}/json/list`,{signal:AbortSignal.timeout(800)})).json();if(pages.some(p=>p.type==='page'))break;}catch{}await sleep(150);}
 const page=pages?.find(p=>p.type==='page');if(!page)throw Error('Browser did not expose a render target.');
 ws=new WebSocket(page.webSocketDebuggerUrl);await new Promise(r=>{ws.addEventListener('open',r,{once:true});});let id=0;const pending=new Map();
 ws.addEventListener('message',m=>{const d=JSON.parse(m.data);if(d.id){pending.get(d.id)?.(d);pending.delete(d.id);}});
 const call=(method,params)=>new Promise(r=>{const key=++id;pending.set(key,r);ws.send(JSON.stringify({id:key,method,params}));});
 const sizes=[16,24,32,48,64,128,256],dataUrl='data:image/svg+xml;base64,'+Buffer.from(favicon).toString('base64');
 const result=await call('Runtime.evaluate',{awaitPromise:true,returnByValue:true,expression:`(async()=>{const image=new Image();await new Promise((resolve,reject)=>{image.onload=resolve;image.onerror=reject;image.src=${JSON.stringify(dataUrl)};});return ${JSON.stringify(sizes)}.map(size=>{const canvas=document.createElement('canvas');canvas.width=canvas.height=size;canvas.getContext('2d').drawImage(image,0,0,size,size);return canvas.toDataURL('image/png').split(',')[1];});})()`});
 if(result.result?.exceptionDetails||!Array.isArray(result.result?.result?.value))throw Error('SVG rasterization failed.');
 const frames=result.result.result.value.map(s=>Buffer.from(s,'base64'));const directory=Buffer.alloc(6+16*frames.length);directory.writeUInt16LE(1,2);directory.writeUInt16LE(frames.length,4);let offset=directory.length;
 frames.forEach((png,n)=>{const entry=6+16*n;directory[entry]=directory[entry+1]=sizes[n]===256?0:sizes[n];directory.writeUInt16LE(1,entry+4);directory.writeUInt16LE(32,entry+6);directory.writeUInt32LE(png.length,entry+8);directory.writeUInt32LE(offset,entry+12);offset+=png.length;});
 fs.mkdirSync(path.join(app,'Assets'),{recursive:true});fs.writeFileSync(path.join(app,'Assets/IpsecStudio.ico'),Buffer.concat([directory,...frames]));
 for(const [file,text]of [[path.join(web,'brand-dark.svg'),inverse],[path.join(web,'favicon.svg'),favicon],[path.join(site,'brand.svg'),primary],[path.join(site,'favicon.svg'),favicon]])fs.writeFileSync(file,text);
 console.log('Generated app/site marks, contrast favicons and native ICO:',sizes.join(', '));
}finally{
 ws?.close();chrome.kill();await Promise.race([new Promise(r=>chrome.once('exit',r)),sleep(1800)]);try{fs.rmSync(profile,{recursive:true,force:true});}catch{}
}

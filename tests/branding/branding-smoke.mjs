// The approved compact mesh must be consistent across the application, site and native icon.
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const root=new URL('../../',import.meta.url),read=p=>readFileSync(new URL(p,root),'utf8').replace(/\r\n/g,'\n');
const version=read('app/IpsecStudio/IpsecStudio.csproj').match(/<Version>([^<]+)<\/Version>/)[1];
const primary=read('app/IpsecStudio/wwwroot/brand.svg');
const inverse=read('app/IpsecStudio/wwwroot/brand-dark.svg');
const favicon=read('app/IpsecStudio/wwwroot/favicon.svg');
assert.equal(read('site/img/brand.svg'),primary,'site and app use the same primary mark');
assert.equal(read('site/img/favicon.svg'),favicon,'site and app use the same favicon');
const shapes=s=>[...s.matchAll(/<(?:path|rect|circle)\b[^>]*>/g)].map(m=>m[0].replace(/\s(?:fill|stroke)="[^"]*"/g,''));
assert.deepEqual(shapes(inverse),shapes(primary),'inverse changes colours, not the approved geometry');
assert.deepEqual(shapes(favicon).slice(1),shapes(inverse),'favicon preserves the mesh inside its contrast tile');
assert.match(primary,/d="m16 20 15 11m9-5 8-10"/,'approved mesh connections');
assert.match(primary,/<rect x="26" y="26" width="23" height="23" rx="6"/,'approved core');
for(const [x,y,r]of [[12,17,6],[51,12,6],[12,48,5]])assert.match(primary,new RegExp(`<circle cx="${x}" cy="${y}" r="${r}"`),'approved nodes');
for(const svg of [primary,inverse,favicon])assert.doesNotMatch(svg,/<(?:script|foreignObject|image|text)\b|\bon\w+\s*=|(?:href|src)=/i,'self-contained static mark without lettering');
assert.ok(read('app/IpsecStudio/wwwroot/index.html').includes(`<link rel="icon" href="favicon.svg?v=${version}" type="image/svg+xml">`),'versioned app favicon avoids stale browser cache');
assert.match(read('app/IpsecStudio/wwwroot/index.html'),/<img[^>]*src="brand-dark\.svg"[^>]*alt=""[^>]*width="40"[^>]*height="40"/,'decorative app mark with stable dimensions');
assert.match(read('site/index.html'),/<img[^>]*src="img\/brand\.svg"[^>]*alt=""[^>]*width="32"[^>]*height="32"/,'decorative site mark with stable dimensions');
assert.doesNotMatch(read('site/index.html'),/class="logo"[^>]*>IS</,'old initial badge removed');
assert.doesNotMatch(read('app/IpsecStudio/wwwroot/index.html'),/<span>IS<\/span>/,'old app initial badge removed');
assert.match(read('app/IpsecStudio/IpsecStudio.csproj'),/<ApplicationIcon>Assets[\\/]IpsecStudio\.ico<\/ApplicationIcon>/);
const ico=readFileSync(new URL('app/IpsecStudio/Assets/IpsecStudio.ico',root));
assert.equal(ico.readUInt16LE(0),0);assert.equal(ico.readUInt16LE(2),1);
const count=ico.readUInt16LE(4),sizes=[];
for(let n=0;n<count;n++){
 const entry=6+n*16,size=ico[entry]||256,height=ico[entry+1]||256,bytes=ico.readUInt32LE(entry+8),offset=ico.readUInt32LE(entry+12);
 assert.equal(size,height);assert.ok(offset>=6+count*16&&offset+bytes<=ico.length,'valid frame bounds');
 const png=ico.subarray(offset,offset+bytes);assert.equal(png.subarray(0,8).toString('hex'),'89504e470d0a1a0a');
 assert.equal(png.readUInt32BE(16),size);assert.equal(png.readUInt32BE(20),size);sizes.push(size);
}
assert.deepEqual(sizes,[16,24,32,48,64,128,256],'native icon includes small and high-DPI frames');
console.log('PASS branding: approved mesh geometry, app/site marks and favicons, accessible markup, native ICO frames.');

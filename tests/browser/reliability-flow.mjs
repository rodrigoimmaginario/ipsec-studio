// Real Edge/DOM regression with a sample API. No AD, credentials, PowerShell or domain writes.
import assert from 'node:assert/strict';
import http from 'node:http';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
const root = fileURLToPath(new URL('../../', import.meta.url));
const web = path.join(root, 'app/IpsecStudio/wwwroot');
const edgePath = process.env.EDGE || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'ipsec-browser-'));
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const ids = ['11111111-1111-4111-8111-111111111111','22222222-2222-4222-8222-222222222222'];
const group = '33333333-3333-4333-8333-333333333333';
const inventory = { id:'44444444-4444-4444-8444-444444444444',domain:'lab.test',domainSid:'S-1-5-21-1-2-3',capturedAt:new Date().toISOString(),identity:'LAB\\operator',warnings:[],objects:[
  ...ids.map((id,i)=>({id,name:'host'+(i+1),kind:'server',ip:'10.0.0.'+(i+1),sid:'S-1-5-21-1-2-3-'+(1000+i),memberIds:[],os:'Sample Windows'})),
  {id:group,name:'Domain Computers',kind:'group',sid:'S-1-5-21-1-2-3-515',memberIds:ids}
] };
const assessment = {hosts:ids.map((computerId,i)=>({computerId,name:'host'+(i+1),ip:'10.0.0.'+(i+1),profile:'base',memberships:['Domain Computers'],conflicts:[],excluded:false,restrictedId:null})),profiles:[{key:'base',computerIds:ids}],warnings:[],domainControllerAddresses:[],exemptAddresses:[]};
const matrix = [{initiator:'host1',responder:'host2',outcome:'authenticated',reasons:['sample']},{initiator:'host2',responder:'host1',outcome:'authenticated',reasons:['sample']}];
let latest = null;
const revisions = new Map(), log = [], requests = [];
const compile = revision => ({sha256:String(revision).repeat(64),broadScope:true,gpos:[{name:'IpsecStudio-Base',profile:'base',applyGroup:'Domain Computers',denyGroupSids:[],inbound:'Require',outbound:'Request',encryption:false,computerIds:ids}],exemptAddresses:[],applyScript:'sample only',rollbackScript:'sample only'});
const server = http.createServer(async (req,res) => {
  const url = new URL(req.url,'http://local');
  let text=''; for await (const chunk of req) text+=chunk;
  const body = text ? JSON.parse(text) : null;
  requests.push({url:url.pathname+url.search,body});
  const json = (data,status=200) => {res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(data));};
  if(req.method==='POST' && req.headers['x-plan-token']!=='test-token') return json({error:'Missing token'},403);
  if(url.pathname==='/api/status') return json({version:'0.4.0',identity:'LAB\\operator',host:'admin',domain:'lab.test',token:'test-token',inventory:null,deploymentEnabled:true,probePort:5985});
  if(url.pathname==='/api/inventory/refresh') return json(inventory);
  if(url.pathname==='/api/plans/simulate') return json({assessment,matrix,matrixIds:ids,wave:0,waveHosts:assessment.hosts});
  if(url.pathname.endsWith('/deployments')) return json(log);
  if(url.pathname.endsWith('/compile')) return json(compile(Number(url.searchParams.get('version') || latest.version)));
  if(req.method==='POST' && /^\/api\/plans(?:\/[^/]+)?$/.test(url.pathname)) {
    if(latest && body.expectedVersion!==latest.version) return json({error:'Version conflict'},409);
    latest={id:'55555555-5555-4555-8555-555555555555',version:(latest?.version || 0)+1,name:body.name,input:structuredClone({...body,expectedVersion:null}),inventory};
    revisions.set(latest.version,structuredClone(latest)); return json(latest);
  }
  if(req.method==='GET' && /^\/api\/plans\/[^/]+$/.test(url.pathname)) return json(revisions.get(Number(url.searchParams.get('version') || latest.version)));
  if(url.pathname==='/api/deploy') {
    if(body.action==='apply' && !body.acknowledgeScope) return json({error:'Broad scope not acknowledged'},400);
    if(body.hash!==compile(body.version).sha256) return json({error:'Wrong revision artifact'},409);
    const record={utc:new Date().toISOString(),action:body.action,version:body.version,succeeded:body.action!=='apply'||body.version===1,output:'{}',pendingDcs:[]};
    log.push(record); return json(record);
  }
  const file = url.pathname==='/' ? 'index.html' : url.pathname.slice(1);
  if(!['index.html','app.js','i18n.js','app.css','style.css'].includes(file)){res.writeHead(404);return res.end();}
  res.writeHead(200,{'Content-Type':file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html','Content-Security-Policy':"default-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'"});
  res.end(fs.readFileSync(path.join(web,file)));
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const edge=spawn(edgePath,['--headless=new','--disable-gpu','--remote-debugging-port=0',`--user-data-dir=${profile}`,'about:blank'],{stdio:'ignore'});
let socket, cdp;
try {
  const activePort=path.join(profile,'DevToolsActivePort');
  for(let i=0;i<100 && !fs.existsSync(activePort);i++) await sleep(100);
  assert.ok(fs.existsSync(activePort),'Edge DevTools started');
  const debugPort=fs.readFileSync(activePort,'utf8').split('\n')[0];
  const tabs=await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json();
  socket=new WebSocket(tabs.find(t=>t.type==='page').webSocketDebuggerUrl);
  await new Promise(resolve=>socket.addEventListener('open',resolve,{once:true}));
  let counter=0; const pending=new Map(), errors=[];
  socket.addEventListener('message',event=>{
    const data=JSON.parse(event.data);
    if(data.method==='Runtime.exceptionThrown') errors.push(data.params.exceptionDetails.text);
    if(pending.has(data.id)){ const {resolve,reject,timer}=pending.get(data.id); clearTimeout(timer); pending.delete(data.id); data.error?reject(new Error(JSON.stringify(data.error))):resolve(data.result); }
  });
  cdp=(method,params={})=>new Promise((resolve,reject)=>{const id=++counter;const timer=setTimeout(()=>{pending.delete(id);reject(new Error('CDP timeout '+method));},10000);pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));});
  const js=async expression=>{const r=await cdp('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);return r.result.value;};
  const wait=async expression=>{for(let i=0;i<150;i++){if(await js(expression))return;await sleep(50);}throw new Error('Browser wait failed: '+expression);};
  const click=selector=>js(`document.querySelector(${JSON.stringify(selector)}).click()`);
  await cdp('Runtime.enable'); await cdp('Page.enable');
  await cdp('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/`});
  await wait("typeof state !== 'undefined' && state.status !== null");
  await click('[data-action="refresh-inventory"]'); await wait('!state.busy && state.inventory !== null');
  await click('[data-action="new-plan"]'); await wait('!state.busy && state.assessment !== null');
  await js("location.hash='#deploy'"); await wait("state.page==='deploy'");
  await click('[data-action="save"]'); await wait('!state.busy && state.saved?.version===1');
  await click('[data-action="compile"]'); await wait('!state.busy && state.compilation !== null');
  await js("document.querySelector('#approval').value='CHG-browser-sample';document.querySelector('#approval').dispatchEvent(new Event('input',{bubbles:true}))");
  await click('#ack-scope'); await click('#ack');
  assert.equal(await js("document.querySelector('#apply-btn').disabled"),false,'acknowledgements do not erase compilation or disable apply');
  await click('#apply-btn'); await wait("!state.busy && state.log.some(r=>r.action==='apply')");
  assert.equal(requests.findLast(r=>r.url==='/api/deploy').body.acknowledgeScope,true,'scope acknowledgement survives busy re-render');
  await js("location.hash='#profiles'"); await wait("state.page==='profiles'");
  await click('[data-field="settings.requireOutbound"]'); await wait('!state.busy && state.assessment !== null');
  assert.equal(await js('state.saved.input.settings.requireOutbound'),false,'editing does not change the saved snapshot');
  await js("location.hash='#deploy'"); await wait("state.page==='deploy'");
  assert.ok(await js("document.querySelector('#main').textContent.includes('alterações não salvas')"),'unsaved change is visible');
  await click('[data-action="save"]'); await wait('!state.busy && state.saved.version===2');
  await click('[data-action="compile"]'); await wait('!state.busy && state.compilation !== null');
  await js("api('/api/deploy',{planId:state.saved.id,version:2,action:'apply',hash:'2'.repeat(64),approval:'CHG-failed-attempt',acknowledgeScope:true}).then(async()=>{state.log=await api('/api/plans/'+state.saved.id+'/deployments');render()})");
  assert.ok(await js("document.querySelector('[data-action=\"open-recovery\"][data-version=\"1\"]') !== null"),'failed v2 attempt must not hide v1 recovery');
  await click('[data-action="open-recovery"][data-version="1"]'); await wait('!state.busy && state.saved.version===1');
  await js("location.hash='#deploy'"); await wait("state.page==='deploy'");
  await click('[data-action="compile"]'); await wait('!state.busy && state.compilation !== null');
  await js('window.confirm=()=>true'); // Only the sample API is reachable; no actual deployment is executed.
  await click('[data-action="deploy"][data-kind="rollback"]');
  await wait("!state.busy && state.log.some(r=>r.action==='rollback')");
  assert.equal(requests.findLast(r=>r.url==='/api/deploy').body.version,1,'rollback targets v1 after saving v2');
  assert.equal(requests.findLast(r=>r.url==='/api/deploy').body.hash,'1'.repeat(64),'rollback uses v1 artifact');
  assert.equal(latest.version,2,'newer saved edit survives rollback');
  assert.deepEqual(errors,[],'no uncaught browser errors');
  console.log('PASS Edge/DOM + sample API: inventory, save/apply v1, acknowledgements, isolated edit/save v2, failed v2 attempt, explicit recovery/rollback v1 with its artifact. No AD writes.');
} finally {
  if(cdp) await cdp('Browser.close').catch(()=>{});
  socket?.close(); edge.kill(); await sleep(500);
  server.closeAllConnections(); await new Promise(resolve=>server.close(resolve));
  fs.rmSync(profile,{recursive:true,force:true,maxRetries:20,retryDelay:100});
}

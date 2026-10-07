// Serves ./out/wwwroot statically and loads it in headless Edge/Chrome via playwright-core.
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import { chromium } from 'playwright-core';
const root = path.resolve(process.env.OUT||'out','wwwroot');
const types = {'.html':'text/html','.js':'text/javascript','.wasm':'application/wasm','.json':'application/json'};
const srv = http.createServer((q,s)=>{ let p=path.join(root,decodeURIComponent(q.url.split('?')[0])); if(p.endsWith(path.sep)) p+='index.html';
  fs.readFile(p,(e,d)=>{ if(e){s.writeHead(404);return s.end();} s.writeHead(200,{'content-type':types[path.extname(p)]||'application/octet-stream'}); s.end(d);});}).listen(0);
const port = srv.address().port;
const b = await chromium.launch(process.env.BROWSER_CHANNEL==='none' ? {} : { channel: process.env.BROWSER_CHANNEL || 'msedge' });
const pg = await b.newPage(); const logs=[]; pg.on('console',m=>logs.push(m.text())); pg.on('pageerror',e=>logs.push('PAGEERROR '+e));
await pg.goto('http://localhost:'+port+'/');
const t0=Date.now(); while(Date.now()-t0<30000 && !logs.some(l=>l.includes('hello from main'))) await new Promise(r=>setTimeout(r,200));
console.log(logs.join('\n')); await b.close(); srv.close();
const ok = logs.some(l=>l.includes('Add(2,3)=5')) && logs.some(l=>l.includes('hello from main'));
console.log(ok?'SMOKE OK':'SMOKE FAIL'); process.exit(ok?0:1);

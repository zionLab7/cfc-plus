const CACHE='cfc-shell-v26';
const SHELL=['/','/index.html',...['app.css','workspace.css','app.js','connection.js','workspace.js','real-data.js','configurable.js','student-resources.js','student-portal.js','community-admin.js','commercial.js','finance.js','tenant-client.js'].map(x=>'/'+x+'?v=20261008a'),'/icon.svg','/portal/icon-192.png','/portal/icon-512.png','/manifest.webmanifest'];
self.addEventListener('install',e=>{e.waitUntil(caches.open(CACHE).then(c=>c.addAll(SHELL)).then(()=>self.skipWaiting()));});
self.addEventListener('activate',e=>{e.waitUntil(caches.keys().then(keys=>Promise.all(keys.filter(k=>k.startsWith('cfc-shell-')&&k!==CACHE).map(k=>caches.delete(k)))).then(()=>self.clients.claim()));});
self.addEventListener('fetch',e=>{if(e.request.method!=='GET'||new URL(e.request.url).pathname.startsWith('/api/'))return;e.respondWith(fetch(e.request).then(r=>{if(r.ok&&SHELL.includes(new URL(e.request.url).pathname+new URL(e.request.url).search)){const copy=r.clone();caches.open(CACHE).then(c=>c.put(e.request,copy));}return r;}).catch(()=>caches.match(e.request).then(r=>r||(e.request.mode==='navigate'?caches.match('/index.html'):Response.error()))));});


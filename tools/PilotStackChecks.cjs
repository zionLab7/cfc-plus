'use strict';
const {execFileSync}=require('node:child_process'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const vars={CFC_BROWSER_IMAGE:'cfc-fixture:browser',CFC_DOMAIN:'cfc.example.org',TRAEFIK_NETWORK:'cfcplus',TRAEFIK_IP:'172.30.0.2',CHROMIUM_SECCOMP_PATH:path.resolve('infra/production/chromium-seccomp.json'),TRAEFIK_CERTRESOLVER:'fixture',EVOLUTION_API_KEY:'a'.repeat(64),EVOLUTION_POSTGRES_PASSWORD:'b'.repeat(64)};
const docker=process.platform==='win32'?'docker.exe':'docker';
const value=JSON.parse(execFileSync(docker,['compose','-f','infra/production/compose.pilot.yaml','config','--format','json'],{env:{...process.env,...vars},encoding:'utf8'}));
assert.deepEqual(Object.keys(value.services).sort(),['app','evolution','postgres','redis']);
for(const s of Object.values(value.services)){assert.equal(s.ports?.length||0,0);assert.notEqual(s.privileged,true);assert.equal(s.deploy?.replicas||1,1);}
assert.equal(value.services.app.environment.CFC_MULTI_TENANT,'true');assert.equal(value.services.app.environment.CFC_CENTRAL_BROWSER_PROFILES,'1');
assert.equal(value.networks.proxy.external,true);assert.equal(value.networks.proxy.name,'cfcplus');assert.equal(value.services.app.labels['traefik.http.routers.cfc-gp.entrypoints'],'websecure');
const seccomp=value.services.app.security_opt.find(x=>x.startsWith('seccomp='));
assert.equal(seccomp,'seccomp='+vars.CHROMIUM_SECCOMP_PATH);
assert.deepEqual(JSON.parse(fs.readFileSync(seccomp.slice(8),'utf8')),JSON.parse(fs.readFileSync('infra/production/chromium-seccomp.json','utf8')));
assert.equal(value.services.postgres.environment.POSTGRES_PASSWORD,vars.EVOLUTION_POSTGRES_PASSWORD);
assert.equal(value.services.evolution.environment.AUTHENTICATION_API_KEY,vars.EVOLUTION_API_KEY);
assert.ok(!value.services.app.environment.Evolution__ApiKey);
const template=Object.fromEntries(fs.readFileSync('infra/production/pilot.env.example','utf8').split(/\r?\n/).filter(x=>x&&!x.startsWith('#')).map(x=>{const at=x.indexOf('=');return [x.slice(0,at),x.slice(at+1)];}));
const placeholders=[...fs.readFileSync('infra/production/compose.pilot.yaml','utf8').matchAll(/\$\{([A-Z_]+)/g)].map(x=>x[1]);
for(const name of placeholders)assert.ok(name in template,'Missing template variable '+name);
console.log('Pilot stack: four services, no public ports, proxy variables, limits, exact sandbox profile and complete environment template passed.');

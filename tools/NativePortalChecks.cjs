'use strict';
// Tests the native product broker using local HTML; it never visits a portal.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict'),{spawn}=require('node:child_process');
async function main(){
 const {allowed}=require('../native-host/navigation.cjs');let boundaryChecks=0;
 assert.equal(allowed('https://idp.sp.gov.br/auth/realms/idpsp'),true);boundaryChecks++;
 for(const url of ['http://idp.sp.gov.br/','https://idp.sp.gov.br.example.org/','https://other.sp.gov.br/','https://idp.sp.gov.br:444/','https://user@idp.sp.gov.br/']){assert.equal(allowed(url),false);boundaryChecks++;}
 const root=path.resolve(process.argv[2]),electron=path.resolve(process.argv[3]);fs.mkdirSync(root,{recursive:true});const token=crypto.randomBytes(32).toString('hex'),config=path.join(root,'control.json');fs.writeFileSync(config,JSON.stringify({root,token,instanceId:crypto.randomUUID().replaceAll('-','')}));
 const prefix=process.env.CFC_TEST_USERNS==='1'?['--disable-setuid-sandbox']:[];const args=process.argv[4]==='packaged'?['--portal-host']:[path.resolve(__dirname,'../native-host')];
 const log=fs.openSync(path.join(root,'host-test.log'),'a');const child=spawn(electron,[...prefix,...args,'--portal-config',config,'--native-smoke'],{windowsHide:true,stdio:['ignore',log,log]});
 let endpoint;
 for(let i=0;i<100;i++){if(fs.existsSync(config+'.ready')){endpoint='http://127.0.0.1:'+JSON.parse(fs.readFileSync(config+'.ready')).port+'/rpc';break;}await new Promise(r=>setTimeout(r,100));}
 if(!endpoint){child.kill();fs.closeSync(log);throw new Error('Broker não iniciou; consulte host-test.log.');}let count=1+boundaryChecks;
 const pid='a'.repeat(32),body=(command,extra={})=>({command,profileId:pid,portal:'student',...extra});
 const request=(data,key=token,extra={})=>fetch(endpoint,{method:'POST',headers:{'Content-Type':'application/json','X-CFC-Native':key,...extra},body:JSON.stringify(data)});
 const rpc=async data=>{const r=await request(data);assert.equal(r.status,200,'Comando local: '+data.command);return r;};
 try{
  assert.equal((await request(body('ping'),'x'.repeat(64))).status,403);count++;
  assert.equal((await request(body('ping'),token,{Origin:'https://example.org'})).status,403);count++;
  assert.equal((await request(body('open',{url:'https://example.org'}))).status,409);count++;
  assert.equal((await request({...body('open',{url:'https://www.detran.sp.gov.br/detransp'}),profileId:'../bad'})).status,409);count++;
  await rpc(body('open',{url:'https://www.detran.sp.gov.br/detransp'}));
  const observed=await (await rpc(body('status'))).json();assert.equal(observed.observation.onGov,false);count++;
  assert.equal((await (await rpc(body('smoke-result'))).json()).visible,false);count++;
  for(const [state,expected] of [['hidden',false],['aria',true],['title',true],['login',false]]){await rpc(body('smoke-auth-state',{state}));const auth=await (await rpc(body('status'))).json();assert.equal(auth.observation.connectedIndicator,expected,'Indicador de conexão: '+state);count++;}
  await rpc(body('smoke-auth-state',{state:'hidden'}));
  await rpc(body('input',{input:{kind:'click',x:45,y:15}}));
  await rpc(body('input',{input:{kind:'click',x:200,y:15}}));
  await rpc(body('input',{input:{kind:'text',text:'local42'}}));await new Promise(r=>setTimeout(r,150));
  const result=await (await rpc(body('smoke-result'))).json();assert.equal(result.title,'clicked');count++;assert.equal(result.value,'local42');count++;
  await rpc(body('assist',{action:'entry',humanGesture:true}));const entryResult=await (await rpc(body('smoke-result'))).json();assert.equal(entryResult.title,'gov-entry');count++;
  assert.deepEqual(result.preferences,{nodeIntegration:false,sandbox:true,contextIsolation:true});count++;
  const frame=await rpc(body('frame'));assert.equal(frame.headers.get('content-type'),'image/jpeg');assert((await frame.arrayBuffer()).byteLength>1000);count++;
  assert.equal((await (await rpc(body('smoke-result'))).json()).visible,false);count++;
  await rpc(body('input',{input:{kind:'click',x:350,y:15}}));await new Promise(r=>setTimeout(r,250));
  const popup=await (await rpc(body('smoke-result'))).json();assert.equal(popup.title,'popup');assert.equal(popup.value,'popup');assert.deepEqual(popup.preferences,result.preferences);count++;
  await rpc(body('smoke-close-popup'));const parent=await (await rpc(body('smoke-result'))).json();assert.equal(parent.value,'local42');count++;
  await rpc(body('input',{input:{kind:'reload'}}));await new Promise(r=>setTimeout(r,200));const refreshed=await (await rpc(body('smoke-result'))).json();assert.equal(refreshed.value,'');assert.notEqual(refreshed.title,'clicked');count++;
  await rpc(body('smoke-hide'));const hiddenFrame=await rpc(body('frame'));assert((await hiddenFrame.arrayBuffer()).byteLength>1000);count++;
  await rpc(body('smoke-window-close'));await new Promise(r=>setTimeout(r,100));const kept=await (await rpc(body('smoke-result'))).json();assert.equal(kept.visible,false);assert.equal(kept.value,'');count++;
  for(let i=0;i<3;i++)await rpc(body('frame'));assert.equal((await (await rpc(body('smoke-result'))).json()).visible,false);count++;
  assert.equal((await request(body('evaluate',{value:'script'}))).status,409);count++;
  await rpc(body('close'));assert.equal((await request(body('frame'))).status,409);count++;
  const detran=(command,extra={})=>({...body(command,extra),portal:'detran'}),other=(command,extra={})=>({...detran(command,extra),profileId:'b'.repeat(32)});
  await rpc(detran('open',{url:'https://www.detran.sp.gov.br/detransp'}));await rpc(other('open',{url:'https://www.detran.sp.gov.br/detransp'}));
  await rpc(detran('smoke-seed-cookie'));await rpc(other('smoke-seed-cookie'));
  await rpc(detran('smoke-load-error'));const failed=await(await rpc(detran('status'))).json();assert.equal(failed.observation.pageReady,false);assert.match(failed.observation.error,/redirecionamentos/);count++;
  assert.equal((await request(detran('recover'))).status,409);count++;
  await rpc({...body('open',{url:'https://www.e-cnhsp.sp.gov.br/'}),portal:'ecnh'});assert.equal((await request(detran('recover',{acknowledged:true}))).status,409);count++;await rpc({...body('close'),portal:'ecnh'});
  await rpc(detran('recover',{acknowledged:true}));await new Promise(r=>setTimeout(r,100));const recovered=await(await rpc(detran('status'))).json();assert.equal(recovered.observation.error,'');count++;
  assert.equal((await(await rpc(detran('smoke-cookie-count'))).json()).count,0);count++;
  assert.equal((await(await rpc(other('smoke-cookie-count'))).json()).count,2);count++;
  await rpc(detran('close'));await rpc(other('close'));
  fs.writeFileSync(path.join(root,'result.json'),JSON.stringify({ok:true,checks:count,preferences:result.preferences,network:'local HTML only'}));console.log(count+' verificações do navegador nativo aprovadas: autenticação do broker, origem, destinos, perfil, clique/texto, captura, isolamento e encerramento.');
 }finally{child.kill();fs.closeSync(log);}
}
main().catch(e=>{console.error(e.message);process.exitCode=1;});

'use strict';
// Native, sandboxed server windows. There is no WebDriver/CDP connection,
// challenge solver, fingerprint override, TLS exception or remote JS API.
const {app,BrowserWindow,session,Menu}=require('electron');
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto');
const {hosts,allowed}=require('./navigation.cjs');
const {checkpointCookie,resetCookie,resetOrigins}=require('./session-policy.cjs');
function id(value){return typeof value==='string'&&/^[a-f0-9]{32}$/.test(value);}
function cookieAllowed(c){return c&&typeof c.name==='string'&&typeof c.value==='string'&&typeof c.domain==='string'&&[...hosts].some(h=>h===c.domain.replace(/^\./,'')||h.endsWith('.'+c.domain.replace(/^\./,'')))&&(!c.expirationDate||c.expirationDate>Date.now()/1000);}
const observeScript=`(()=>{
 const visible=e=>!!e&&e.getClientRects().length>0&&getComputedStyle(e).visibility!=='hidden';
 const text=(document.body?.innerText||'').slice(0,100000);
 const error=text.split(/\\r?\\n/).find(t=>/captcha.*inv[aá]lid|ERL\\d{5,}|senha.*(?:incorret|inv[aá]lid)|conta.*bloquead/i.test(t))||'';
 const a=document.querySelector('#accountId'),p=document.querySelector('#password,input[type="password"]'),next=document.querySelector('#enter-account-id');
 const links=[...document.querySelectorAll('a,button,[role="button"],[role="menuitem"]')].filter(visible);
 return {onGov:location.hostname==='sso.acesso.gov.br',entry:links.some(e=>/^Entrar com gov\\.br$/i.test((e.getAttribute('aria-label')||e.innerText||e.textContent||'').trim().replace(/\\s+/g,' '))),account:visible(a),accountCpf:a?.value||'',password:visible(p),error,
 challenge:[...document.querySelectorAll('iframe[src*="hcaptcha.com"],iframe[src*="recaptcha"]')].some(e=>visible(e)&&e.getBoundingClientRect().width>100&&e.getBoundingClientRect().height>100),
 pageReady:document.readyState==='complete',continueEnabled:visible(next)&&!next.disabled,connectedIndicator:!visible(a)&&!visible(p)&&links.some(e=>[e.getAttribute('aria-label'),e.getAttribute('title'),e.innerText,e.textContent].some(label=>/^(Sair|Logout|Log out|Desconectar|Encerrar sess[aã]o)(?:\\b|$)/i.test((label||'').trim()))),text};
})()`;
function run(){
 const at=process.argv.indexOf('--portal-config'),configFile=process.argv[at+1];
 if(at<0||!path.isAbsolute(configFile))throw new Error('Configuração local ausente.');
 const config=JSON.parse(fs.readFileSync(configFile,'utf8'));
 const smoke=process.argv.includes('--native-smoke');
 if(!path.isAbsolute(config.root)||!/^\w{64}$/.test(config.token))throw new Error('Configuração inválida.');
 fs.mkdirSync(path.join(config.root,'native-portal-host'),{recursive:true});app.setPath('userData',path.join(config.root,'native-portal-host'));app.setName('CFC+ · portais do servidor');
 if(!app.requestSingleInstanceLock()){app.quit();return;}
 const pages=new Map(),sessions=new Map(),groups=new Map(),lastFailures=new Map(),closing=new WeakSet(),loadErrors=new Map();let quitting=false;
 let server;
 app.on('window-all-closed',()=>{}); // Client/server UI closure does not destroy logins.
 app.on('before-quit',()=>{quitting=true;for(const s of sessions.values())s.flushStorageData();server?.close();});
 app.whenReady().then(()=>{
  Menu.setApplicationMenu(null);
  function diagnostic(key,message){if(lastFailures.get(key)===message)return;lastFailures.set(key,message);fs.appendFileSync(path.join(config.root,'native-portal.log'),JSON.stringify({time:new Date().toISOString(),profileId:key.split('/')[0],message})+'\n');}
  function active(key){const w=pages.get(key);if(!w||w.isDestroyed()){diagnostic(key,'Página encerrada: nenhuma janela ativa para esta visualização.');throw new Error('Página encerrada; retome pela central.');}if(!allowed(w.webContents.getURL())&&!(smoke&&(w.webContents.getURL().startsWith('data:text/html,')||w.webContents.getURL()==='about:blank'))){let destination='desconhecido';try{const u=new URL(w.webContents.getURL());destination=u.protocol+' '+u.hostname;}catch{}diagnostic(key,'Tipo de página sem visualização: '+destination);throw new Error('Página fora dos portais autorizados.');}return w;}
  function create(key,pid,url){
   const profilePath=path.join(config.root,'native-browser-profiles',pid);fs.mkdirSync(profilePath,{recursive:true});const s=sessions.get(pid)||session.fromPath(profilePath);sessions.set(pid,s);
   s.setPermissionRequestHandler((_wc,_permission,cb)=>cb(false));s.setPermissionCheckHandler(()=>false);
   const options={width:1280,height:800,useContentSize:true,resizable:false,maximizable:false,show:false,title:'CFC+ · portal no servidor',webPreferences:{session:s,nodeIntegration:false,contextIsolation:true,sandbox:true,webSecurity:true,backgroundThrottling:false,offscreen:true}};
   function log(message){const line=JSON.stringify({time:new Date().toISOString(),profileId:pid,message});fs.appendFileSync(path.join(config.root,'native-portal.log'),line+'\n');}
   s.webRequest.onCompleted({urls:['https://*/*']},details=>{let host='';try{host=new URL(details.url).hostname;}catch{}if(hosts.has(host)&&details.statusCode>=400)log('Requisição '+details.resourceType+' HTTP '+details.statusCode+' em '+host);});
   s.webRequest.onErrorOccurred({urls:['https://*/*']},details=>{let host='';try{host=new URL(details.url).hostname;}catch{}if(hosts.has(host))log('Requisição '+details.resourceType+' falhou em '+host+': '+String(details.error).replace(/[^A-Z0-9_:]/g,'').slice(0,80));});
   function guard(w,parent=null){
    w.removeMenu();const group=groups.get(key)||new Set();groups.set(key,group);group.add(w);
    w.webContents.on('will-attach-webview',e=>e.preventDefault());
    w.webContents.on('render-process-gone',(_e,details)=>log('Renderizador encerrado: '+details.reason));
    w.webContents.on('console-message',details=>{const kind=/^(?:Uncaught )?([A-Za-z]+Error)\b/.exec(details.message||'');if(kind)log('Erro de script: '+kind[1]);});
    const navigation=(e,target)=>{if(!allowed(target)){e.preventDefault();let host='destino não autorizado';try{host=new URL(target).hostname||host;}catch{}log('Navegação bloqueada: '+host);}};
    w.webContents.on('will-navigate',navigation);w.webContents.on('will-redirect',navigation);
    w.webContents.on('did-fail-load',(_e,code,_description,target,isMain)=>{if(code===-3||!isMain)return;let host='portal';try{host=new URL(target).hostname;}catch{}loadErrors.set(key,{code,host});log('Falha de carregamento '+code+' em '+host);
    });
    w.webContents.on('did-start-loading',()=>loadErrors.delete(key));
    w.webContents.setWindowOpenHandler(({url:target})=>{
     if(target!=='about:blank'&&!allowed(target)){log('Janela bloqueada: destino não autorizado');return {action:'deny'};}
     return {action:'allow',overrideBrowserWindowOptions:options};
    });
    w.webContents.on('did-create-window',child=>{guard(child,w);pages.set(key,child);});
    if(!parent)w.on('close',e=>{if(!quitting&&!closing.has(w)){e.preventDefault();w.hide();log('Janela ocultada; sessão preservada.');}});
    w.on('closed',()=>{log(parent?'Janela auxiliar encerrada.':'Janela principal encerrada.');group.delete(w);if(pages.get(key)===w){if(parent&&!parent.isDestroyed())pages.set(key,parent);else{const remaining=[...group].find(x=>!x.isDestroyed());if(remaining)pages.set(key,remaining);else pages.delete(key);}}});
   }
   const w=new BrowserWindow(options);guard(w);s.on('will-download',e=>e.preventDefault());pages.set(key,w);return w;
  }
  async function rpc(body){
   if(body.command==='ping')return {application:'cfc-native-portals',root:config.root,pid:process.pid};
   if(!id(body.profileId)||!['ecnh','detran','student'].includes(body.portal))throw new Error('Perfil/portal inválido.');
   const key=body.profileId+'/'+body.portal;
   if(body.command==='open'){
    if(!allowed(body.url))throw new Error('Portal não autorizado.');let w=pages.get(key);
    if(!w||w.isDestroyed()){
     w=create(key,body.profileId,body.url);const live=await w.webContents.session.cookies.get({});
     for(const c of (body.cookies||[]).filter(c=>cookieAllowed(c)&&checkpointCookie(c))){if(live.some(x=>x.name===c.name&&x.domain===c.domain&&x.path===c.path))continue;const domain=c.domain.replace(/^\./,'');await w.webContents.session.cookies.set({...c,url:'https://'+domain+(c.path||'/')}).catch(()=>{});}
     if(smoke)await w.loadURL('data:text/html,'+encodeURIComponent(`<button id="action" onclick="document.title='clicked'">Teste local</button><input id="typed"><button style="position:absolute;left:330px;top:8px" onclick="const p=window.open('about:blank');p.document.write('&lt;input id=typed value=popup&gt;');p.document.title='popup'">Popup local</button><svg width="100" height="30" style="position:absolute;left:600px;top:50px"><a href="#local"><text x="0" y="15">Ícone</text></a></svg><button aria-label=" Entrar   com gov.br " style="position:absolute;left:600px;top:100px" onclick="document.title='gov-entry'">Entrar com gov.br</button><button id="logout" aria-label="Sair" hidden></button><input id="password" type="password" hidden>`));
     else w.loadURL(body.url).catch(()=>{});
    }
    return {opened:true};
   }
   if(body.command==='recover'){
    if(body.portal!=='detran'||body.acknowledged!==true)throw new Error('Recuperação exige confirmação do DETRAN.');
    const target=pages.get(key);if(!target||target.isDestroyed())throw new Error('Retome a sessão antes de recuperar.');
    const other=groups.get(body.profileId+'/ecnh');if(other&&[...other].some(w=>!w.isDestroyed()))throw new Error('Feche a visualização e-CNH deste profissional antes de reiniciar o SSO.');
    const s=target.webContents.session;target.webContents.stop();
    for(const child of [...(groups.get(key)||[])])if(!child.isDestroyed()){closing.add(child);child.close();}
    for(const c of (await s.cookies.get({})).filter(resetCookie))await s.cookies.remove('https://'+c.domain.replace(/^\./,'')+(c.path||'/'),c.name);
    for(const origin of resetOrigins)await s.clearStorageData({origin,storages:['localstorage','indexdb','serviceworkers','cachestorage']});
    await s.cookies.flushStore();await s.clearCache();loadErrors.delete(key);
    const fresh=create(key,body.profileId,'https://www.detran.sp.gov.br/detransp');fresh.loadURL(smoke?'data:text/html,<p>Recovered local fixture</p>':'https://www.detran.sp.gov.br/detransp').catch(()=>{});diagnostic(key,'Acesso DETRAN reiniciado pelo atendimento; cookies de login removidos somente deste perfil.');return {accepted:true};
   }
   if(body.command==='close'){const target=pages.get(key);if(target&&!target.isDestroyed())await target.webContents.session.cookies.flushStore();for(const item of [...(groups.get(key)||(target?[target]:[]))])if(!item.isDestroyed()){closing.add(item);item.close();}groups.delete(key);pages.delete(key);loadErrors.delete(key);return {closed:true};}
   if(smoke&&body.command==='smoke-load-error'){const target=pages.get(key);loadErrors.set(key,{code:-310,host:'www.detran.sp.gov.br'});target.webContents.emit('did-finish-load');return {ok:true};}
   if(smoke&&body.command==='smoke-seed-cookie'){const target=pages.get(key);const s=target.webContents.session;for(const domain of ['www.detran.sp.gov.br','sso.acesso.gov.br'])await s.cookies.set({url:'https://'+domain+'/',name:'CFC_TEST',value:'fixture',path:'/'});return {ok:true};}
   if(smoke&&body.command==='smoke-cookie-count'){const target=pages.get(key);return {count:(await target.webContents.session.cookies.get({})).filter(c=>c.name==='CFC_TEST').length};}
   if(body.command==='status'){
    const target=pages.get(key);if(!target||target.isDestroyed())throw new Error('Página encerrada.');
    if((!allowed(target.webContents.getURL())&&!(smoke&&(target.webContents.getURL().startsWith('data:text/html,')||target.webContents.getURL()==='about:blank')))||loadErrors.has(key)){
     const fault=loadErrors.get(key);const error=fault?.code===-310?'O DETRAN entrou em um ciclo de redirecionamentos. Use Reiniciar acesso DETRAN para limpar a tentativa deste perfil.':fault?'O portal não carregou (código '+fault.code+'). Confira a conexão ou reinicie este acesso.':'';
     return {url:allowed(target.webContents.getURL())?target.webContents.getURL():'https://www.detran.sp.gov.br/detransp',observation:{pageReady:false,onGov:false,entry:false,account:false,password:false,challenge:false,continueEnabled:false,connectedIndicator:false,error,text:''},cookies:[],instanceId:config.instanceId};
    }
   }
   const w=active(key),wc=w.webContents;
   if(smoke&&body.command==='smoke-result')return {title:wc.getTitle(),value:await wc.executeJavaScript('document.querySelector("#typed").value'),visible:w.isVisible(),preferences:{nodeIntegration:wc.getLastWebPreferences().nodeIntegration,sandbox:wc.getLastWebPreferences().sandbox,contextIsolation:wc.getLastWebPreferences().contextIsolation}};
   if(smoke&&body.command==='smoke-close-popup'){w.close();return {closed:true};}
   if(smoke&&body.command==='smoke-hide'){w.hide();return {hidden:true};}
   if(smoke&&body.command==='smoke-window-close'){w.close();return {requested:true};}
   if(smoke&&body.command==='smoke-auth-state'){
    if(!['hidden','aria','title','login'].includes(body.state))throw new Error('Estado local inválido.');
    await wc.executeJavaScript(`(()=>{const state=${JSON.stringify(body.state)},logout=document.querySelector('#logout'),password=document.querySelector('#password');logout.hidden=state==='hidden';logout.setAttribute('aria-label',state==='title'?'':'Sair');logout.title=state==='title'?'Sair do sistema':'';password.hidden=state!=='login';})()`);return {accepted:true};
   }
   if(smoke&&body.command==='smoke-quit'){app.quit();return {ok:true};}
   if(body.command==='frame'){let image;try{image=await wc.capturePage({x:0,y:0,width:1280,height:800},{stayHidden:true});}catch(error){diagnostic(key,/Current display surface not available/.test(error.message||'')?'Captura: superfície de exibição indisponível.':'Captura: falha do renderizador.');throw error;}if(image.isEmpty()){diagnostic(key,'Captura vazia: visível='+w.isVisible()+', minimizada='+w.isMinimized()+', área='+w.getContentBounds().width+'x'+w.getContentBounds().height);throw new Error('Imagem ainda não disponível.');}return image.resize({width:1280,height:800}).toJPEG(90);}

   if(body.command==='input'){
    const i=body.input||{},x=Math.round(i.x||0),y=Math.round(i.y||0);if(x<0||x>=1280||y<0||y>=800)throw new Error('Posição inválida.');wc.focus();
    const mouse=(type)=>wc.sendInputEvent({type,x,y,button:'left',clickCount:1});
    if(i.kind==='click'){mouse('mouseDown');mouse('mouseUp');}
    else if(i.kind==='down')mouse('mouseDown');else if(i.kind==='up')mouse('mouseUp');else if(i.kind==='move')mouse('mouseMove');
    else if(i.kind==='scroll')wc.sendInputEvent({type:'mouseWheel',x,y,deltaY:-Math.max(-1600,Math.min(1600,i.deltaY)),deltaX:0});
    else if(i.kind==='text'){if(typeof i.text!=='string'||i.text.length>2000)throw new Error('Texto inválido.');for(const c of i.text){if(c.charCodeAt(0)<128){wc.sendInputEvent({type:'keyDown',keyCode:c});wc.sendInputEvent({type:'char',keyCode:c});wc.sendInputEvent({type:'keyUp',keyCode:c});}else wc.insertText(c);}}
    else if(i.kind==='key'){
     const keys={Tab:'Tab',Enter:'Return',Space:'Space',Backspace:'Backspace',Delete:'Delete',Escape:'Escape',ArrowLeft:'Left',ArrowRight:'Right',ArrowUp:'Up',ArrowDown:'Down',Home:'Home',End:'End','Shift+Tab':'Tab','Control+A':'A','Meta+A':'A'};
     if(!keys[i.key])throw new Error('Tecla não autorizada.');const modifiers=i.key.startsWith('Shift+')?['shift']:i.key.startsWith('Control+')?['control']:i.key.startsWith('Meta+')?['meta']:[];wc.sendInputEvent({type:'keyDown',keyCode:keys[i.key],modifiers});wc.sendInputEvent({type:'keyUp',keyCode:keys[i.key],modifiers});
    }else if(i.kind==='back'){if(wc.navigationHistory.canGoBack())wc.navigationHistory.goBack();}else if(i.kind==='reload')wc.reload();else throw new Error('Entrada inválida.');
    return {accepted:true};
   }
   if(body.command==='assist'){
    if(!['entry','cpf','continue','password','qr'].includes(body.action))throw new Error('Ação inválida.');
    if(body.action!=='entry'&&new URL(wc.getURL()).hostname!=='sso.acesso.gov.br')throw new Error('Ação restrita ao GOV.BR.');
    const action=JSON.stringify(body.action),value=JSON.stringify(body.value||'');
    try{await wc.executeJavaScript(`(()=>{const action=${action},value=${value};const visible=e=>e&&e.getClientRects().length>0;
     const challenge=[...document.querySelectorAll('iframe[src*="hcaptcha.com"],iframe[src*="recaptcha"]')].some(e=>visible(e)&&e.getBoundingClientRect().height>100&&e.getBoundingClientRect().width>100);if(challenge)throw new Error('Aguardando verificação humana.');
     if(action==='entry'||action==='qr'){const label=action==='entry'?/^Entrar com gov\\.br$/i:/^Login com QR code$/i;const e=[...document.querySelectorAll('a,button,[role="button"]')].find(e=>visible(e)&&label.test((e.getAttribute('aria-label')||e.innerText||e.textContent||'').trim().replace(/\\s+/g,' ')));if(!e)throw new Error('CFC_ENTRY_UNAVAILABLE');e.click();}
     else if(action==='continue'){const e=document.querySelector('#enter-account-id');if(!visible(e)||e.disabled)throw new Error('Etapa indisponível.');e.click();}
     else {const e=document.querySelector(action==='cpf'?'#accountId':'#password');if(!visible(e))throw new Error('Campo indisponível.');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,value);e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));if(action==='password'){const button=document.querySelector('#submit-button');if(!visible(button)||button.disabled)throw new Error('Etapa indisponível.');button.click();}}
    })()`,body.action==='entry'&&body.humanGesture===true);}catch(error){diagnostic(key,body.action==='entry'&&String(error.message).includes('CFC_ENTRY_UNAVAILABLE')?'Entrada GOV.BR: botão não encontrado na página visível.':'Interação assistida indisponível: '+body.action);throw error;}return {accepted:true};
   }
   if(body.command==='status'){
    const observation=await wc.executeJavaScript(observeScript,false);const cookies=(await wc.session.cookies.get({})).filter(cookieAllowed);await wc.session.cookies.flushStore();
    return {url:wc.getURL(),observation,cookies,instanceId:config.instanceId,width:1280,height:800};
   }
   throw new Error('Comando não autorizado.');
  }
  server=http.createServer(async(req,res)=>{
   res.setHeader('Cache-Control','no-store');const token=req.headers['x-cfc-native'];
   if(req.method!=='POST'||req.url!=='/rpc'||req.headers.origin||typeof token!=='string'||token.length!==config.token.length||!crypto.timingSafeEqual(Buffer.from(token),Buffer.from(config.token))){res.writeHead(403);res.end();return;}
   try{let data='';for await(const part of req){data+=part;if(data.length>100000){res.writeHead(413);res.end();return;}}
    const result=await rpc(JSON.parse(data));if(Buffer.isBuffer(result)){res.setHeader('Content-Type','image/jpeg');res.end(result);}else{res.setHeader('Content-Type','application/json');res.end(JSON.stringify(result));}
   }catch{res.writeHead(409,{'Content-Type':'application/json'});res.end(JSON.stringify({error:'A página mudou ou o comando não está disponível. Confira o portal.'}));}
  });
  server.listen(0,'127.0.0.1',()=>{fs.writeFileSync(configFile+'.ready.tmp',JSON.stringify({port:server.address().port,pid:process.pid,instanceId:config.instanceId}),{mode:0o600});fs.renameSync(configFile+'.ready.tmp',configFile+'.ready');});
 });
}
module.exports={run,allowed,id,cookieAllowed};
if(require.main===module||process.defaultApp)run();

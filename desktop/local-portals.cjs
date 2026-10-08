'use strict';
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {hosts,allowed}=require('./portal-navigation.cjs');
const portals=new Set(['ecnh','detran']);
const checkpointDomains=new Set(['www.detran.sp.gov.br','detran.sp.gov.br','www.e-cnhsp.sp.gov.br','e-cnhsp.sp.gov.br']);
function validateAccess(access,request){
 if(!access||access.profile?.id!==request.profileId||access.schoolId!==request.schoolId||!['Diretor','Instrutor'].includes(access.profile.kind)||!portals.has(request.portal)||!allowed(access.url)||!/^([a-z][a-z0-9-]{2,47})?$/.test(access.schoolId||''))throw Error('Acesso local não autorizado pelo servidor.');
 if(!/^[a-f0-9]{32}$/.test(access.profile.id))throw Error('Perfil inválido.');
 return access;
}
function profileDirectory(root,origin,school,id){
 if(!path.isAbsolute(root)||!/^([a-z][a-z0-9-]{2,47})?$/.test(school)||!/^[a-f0-9]{32}$/.test(id))throw Error('Perfil local inválido.');
 const scope=crypto.createHash('sha256').update(origin+'\n'+school).digest('hex');
 return path.join(root,'local-portals',scope,id);
}
function restorableCookie(cookie,now=Date.now()/1000){
 if(!cookie||typeof cookie.name!=='string'||typeof cookie.value!=='string'||typeof cookie.domain!=='string')return false;
 if(cookie.expirationDate!==undefined&&(!Number.isFinite(cookie.expirationDate)||cookie.expirationDate<=now))return false;
 const domain=cookie.domain.replace(/^\./,'').toLowerCase();return checkpointDomains.has(domain);
}
function create({BrowserWindow,session,dialog,safeStorage,root,authorize,onChange}){
 const pages=new Map(),owned=new WeakSet(),stores=new Map(),failed=new WeakSet();let quitting=false,chain=Promise.resolve();
 async function flush(item){
  item.session.flushStorageData();await item.session.cookies.flushStore();
  if(!safeStorage.isEncryptionAvailable())return;
  const cookies=(await item.session.cookies.get({})).filter(c=>restorableCookie(c));
  const file=path.join(item.directory,'checkpoint.encrypted');
  fs.writeFileSync(file+'.tmp',safeStorage.encryptString(JSON.stringify(cookies)),{mode:0o600});fs.renameSync(file+'.tmp',file);
 }
 async function restore(item){
  const file=path.join(item.directory,'checkpoint.encrypted');if(!fs.existsSync(file)||!safeStorage.isEncryptionAvailable())return;
  const rows=JSON.parse(safeStorage.decryptString(fs.readFileSync(file)));if(!Array.isArray(rows))throw Error('Checkpoint local inválido.');
  const live=await item.session.cookies.get({});
  for(const c of rows.filter(c=>restorableCookie(c))){
   if(live.some(x=>x.name===c.name&&x.domain===c.domain&&x.path===c.path))continue;
   await item.session.cookies.set({...c,url:'https://'+[...hosts].find(h=>h===c.domain.replace(/^\./,'')||h.endsWith('.'+c.domain.replace(/^\./,'')))+(c.path||'/')}).catch(()=>{});
  }
 }
 function guard(w,item,isRoot){
  owned.add(w.webContents);item.windows.add(w);w.removeMenu();
  const navigate=(event,url)=>{if(!allowed(url))event.preventDefault();};
  w.webContents.on('will-navigate',navigate);w.webContents.on('will-redirect',navigate);
  w.webContents.on('will-attach-webview',event=>event.preventDefault());
  w.webContents.setWindowOpenHandler(({url})=>url==='about:blank'||allowed(url)?{action:'allow',overrideBrowserWindowOptions:item.options}:{action:'deny'});
  w.webContents.on('did-create-window',child=>guard(child,item,false));
  w.on('close',event=>{if(isRoot&&!quitting){event.preventDefault();w.hide();for(const child of item.windows)if(!child.isDestroyed())child.hide();flush(item).catch(()=>{});}});
  w.on('closed',()=>item.windows.delete(w));
 }
 async function open(origin,request){
  const access=validateAccess(await authorize(request),request),directory=profileDirectory(root,origin,access.schoolId||'',access.profile.id),key=directory+'/'+request.portal;
  const existing=pages.get(key);if(existing&&!existing.isDestroyed()){const item=stores.get(directory);for(const w of item.windows)if(!w.isDestroyed())w.show();existing.focus();if(failed.has(existing)){await existing.loadURL(access.url);failed.delete(existing);}return {opened:true,location:'local'};}
  if(pages.size>=3)throw Error('Já existem três portais locais abertos. Encerre um pelo menu Aplicativo antes de abrir outro.');
  fs.mkdirSync(directory,{recursive:true});
  let item=stores.get(directory);
  if(!item){
   const s=session.fromPath(directory);s.setPermissionRequestHandler((_wc,_permission,cb)=>cb(false));s.setPermissionCheckHandler(()=>false);
   item={session:s,directory,windows:new Set(),lastError:''};stores.set(directory,item);
   await restore(item);
   let pending; s.cookies.on('changed',()=>{clearTimeout(pending);pending=setTimeout(()=>flush(item).catch(()=>{}),1000);pending.unref();});
  }
  item.options={width:1280,height:850,show:true,title:'CFC+ · '+access.profile.name+' · '+request.portal+' · neste computador',webPreferences:{session:item.session,nodeIntegration:false,contextIsolation:true,sandbox:true,webSecurity:true,backgroundThrottling:false}};
  const w=new BrowserWindow(item.options);guard(w,item,true);pages.set(key,w);
  w.on('closed',()=>{pages.delete(key);onChange?.();});
  w.webContents.on('did-fail-load',(_e,code,_description,_url,main)=>{if(main&&code!==-3){failed.add(w);dialog.showMessageBox(w,{type:'warning',title:'Portal local',message:'A página não carregou (código '+code+').',detail:'Confira a conexão. Abra/retome o perfil pelo aplicativo para tentar novamente; o perfil salvo permanece neste computador.'}).catch(()=>{});}});
  w.webContents.on('did-finish-load',()=>failed.delete(w));
  try{await w.loadURL(access.url);}catch{failed.add(w);throw Error('O portal local não carregou. Confira sua conexão e retome o acesso.');}finally{onChange?.();}
  return {opened:true,location:'local'};
 }
 async function certificates(event,wc,url,list,callback){
  if(!owned.has(wc))return false;event.preventDefault();
  if(!allowed(url)||!list.length){callback();return true;}
  const w=BrowserWindow.fromWebContents(wc),labels=list.map((c,i)=>(i+1)+'. '+(c.subjectName||'Certificado')+' · '+(c.issuerName||'Emissor'));
  try{const answer=await dialog.showMessageBox(w,{type:'question',title:'Escolher certificado e-CPF',message:'Selecione o certificado do profissional deste perfil.',detail:'O certificado e sua chave permanecem neste computador. O PIN, se solicitado, será pedido pelo programa do cartão. Confira o titular antes de continuar.',buttons:['Cancelar',...labels],defaultId:0,cancelId:0,noLink:true});callback(answer.response>0?list[answer.response-1]:undefined);}catch{callback();}
  return true;
 }
 async function closeAll(){try{await Promise.all([...stores.values()].map(flush));}finally{for(const item of stores.values())for(const w of [...item.windows])if(!w.isDestroyed())w.destroy();pages.clear();onChange?.();}}
 const timer=setInterval(()=>{for(const item of stores.values())flush(item).catch(()=>{});},30000);timer.unref();
 return {open:(...args)=>{const next=chain.then(()=>open(...args));chain=next.catch(()=>{});return next;},certificates,hasActive:()=>pages.size>0,closeAll,shutdown:async()=>{quitting=true;clearInterval(timer);await closeAll();}};
}
module.exports={validateAccess,profileDirectory,restorableCookie,create};

'use strict';
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),{EventEmitter}=require('node:events');
const {validateAccess,profileDirectory,restorableCookie,create}=require('../local-portals.cjs');
const pid='a'.repeat(32),request={profileId:pid,portal:'detran',schoolId:'school-alpha'};
const access={profile:{id:pid,kind:'Diretor',name:'Profissional sintético'},schoolId:'school-alpha',url:'https://www.detran.sp.gov.br/detransp'};
function fixture(authorize=async()=>access){
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'cfc-local-test-')),windows=[],sessions=new Map();
 class Window extends EventEmitter{
  constructor(options){super();this.options=options;this.webContents=new EventEmitter();this.webContents.setWindowOpenHandler=h=>this.popup=h;this.webContents.getURL=()=>this.url;this.dead=false;this.visible=true;windows.push(this);}
  removeMenu(){}async loadURL(url){this.url=url;}show(){this.visible=true;}focus(){}hide(){this.visible=false;}isDestroyed(){return this.dead;}destroy(){this.dead=true;this.emit('closed');}
  static fromWebContents(wc){return windows.find(w=>w.webContents===wc);}
 }
 const session={fromPath:directory=>{if(!sessions.has(directory)){const s={flushStorageData(){},setPermissionRequestHandler(h){this.permission=h;},setPermissionCheckHandler(h){this.check=h;},cookies:new EventEmitter()};let rows=[];s.cookies.get=async()=>rows;s.cookies.flushStore=async()=>{};s.cookies.set=async c=>{rows.push(c);};sessions.set(directory,s);}return sessions.get(directory);}};
 const safeStorage={isEncryptionAvailable:()=>true,encryptString:value=>Buffer.from(value).map(v=>v^123),decryptString:value=>Buffer.from(value).map(v=>v^123).toString()};
 const controller=create({BrowserWindow:Window,session,dialog:{showMessageBox:async()=>({response:1})},safeStorage,root,authorize});
 return {root,windows,sessions,controller};
}
test('Autorização vincula escola, perfil, função e portal oficial',()=>{
 assert.equal(validateAccess(access,request),access);
 for(const bad of [{...access,schoolId:'school-beta'},{...access,url:'https://evil.example'},{...access,profile:{...access.profile,id:'b'.repeat(32)}},{...access,profile:{...access.profile,kind:'Aluno'}}])assert.throws(()=>validateAccess(bad,request));
 assert.throws(()=>validateAccess(access,{...request,portal:'arbitrary'}));
});
test('Perfis locais são separados por servidor, escola e profissional',()=>{
 const root=path.resolve('test-results','local-path'),a=profileDirectory(root,'https://server.example','school-alpha',pid);
 assert.notEqual(a,profileDirectory(root,'https://other.example','school-alpha',pid));assert.notEqual(a,profileDirectory(root,'https://server.example','school-beta',pid));assert.notEqual(a,profileDirectory(root,'https://server.example','school-alpha','b'.repeat(32)));
 assert.ok(a.startsWith(root+path.sep));assert.throws(()=>profileDirectory(root,'https://server.example','../outside',pid));
});
test('Checkpoint respeita expiração e exclui tentativas incompletas de SSO',()=>{
 const c={name:'fixture',value:'synthetic',domain:'.detran.sp.gov.br'};assert.equal(restorableCookie(c),true);
 assert.equal(restorableCookie({...c,domain:'evil.example'}),false);assert.equal(restorableCookie({...c,domain:'sso.acesso.gov.br'}),false);assert.equal(restorableCookie({...c,domain:'.acesso.gov.br'}),false);assert.equal(restorableCookie({...c,expirationDate:1}),false);assert.equal(restorableCookie({...c,expirationDate:'invalid'}),false);
});
test('Servidor recusando acesso não abre janela local',async()=>{
 const f=fixture(async()=>{throw Error('Sessão revogada');});try{await assert.rejects(f.controller.open('https://server.example',request),/revogada/);assert.equal(f.windows.length,0);}finally{await f.controller.shutdown();}
});
test('Janela local usa sandbox, bloqueia navegação externa e oculta ao fechar',async()=>{
 const f=fixture();try{await f.controller.open('https://server.example',request);const w=f.windows[0],preferences=w.options.webPreferences;
 assert.equal(preferences.nodeIntegration,false);assert.equal(preferences.contextIsolation,true);assert.equal(preferences.sandbox,true);assert.equal(preferences.backgroundThrottling,false);
 let blocked=false;w.webContents.emit('will-navigate',{preventDefault:()=>blocked=true},'file:///secret');assert.equal(blocked,true);assert.equal(w.popup({url:'https://evil.example'}).action,'deny');assert.equal(w.popup({url:'https://sso.acesso.gov.br'}).action,'allow');
 let prevented=false;w.emit('close',{preventDefault:()=>prevented=true});assert.equal(prevented,true);assert.equal(w.visible,false);assert.equal(f.controller.hasActive(),true);
 await f.controller.open('https://server.example',request);assert.equal(w.visible,true);assert.equal(f.windows.length,1);
 }finally{await f.controller.shutdown();}
});
test('Certificado exige seleção local e recusa origem externa',async()=>{
 const f=fixture();try{await f.controller.open('https://server.example',request);const w=f.windows[0],list=[{subjectName:'Profissional sintético',issuerName:'Emissor fixture'}];let selected;
 const e={preventDefault(){}};assert.equal(await f.controller.certificates(e,w.webContents,access.url,list,c=>selected=c),true);assert.equal(selected,list[0]);
 await f.controller.certificates(e,w.webContents,'https://evil.example',list,c=>selected=c);assert.equal(selected,undefined);
 }finally{await f.controller.shutdown();}
});
test('Checkpoint é cifrado e cookies válidos são retomados após reabrir',async()=>{
 const f=fixture();try{await f.controller.open('https://server.example',request);const s=f.windows[0].options.webPreferences.session;await s.cookies.set({url:'https://www.detran.sp.gov.br',domain:'www.detran.sp.gov.br',name:'CFC_FIXTURE',value:'SYNTHETIC_COOKIE',path:'/'});
 await f.controller.closeAll();const file=path.join(profileDirectory(f.root,'https://server.example','school-alpha',pid),'checkpoint.encrypted');assert.equal(fs.readFileSync(file).includes(Buffer.from('SYNTHETIC_COOKIE')),false);
 }finally{await f.controller.shutdown();}
});

test('Falha de carregamento pode ser retomada sem criar outro perfil',async()=>{
 const f=fixture();try{await f.controller.open('https://server.example',request);const w=f.windows[0];let loads=0;
 w.loadURL=async()=>{loads++;};w.webContents.emit('did-fail-load',{},-105,'DNS','https://www.detran.sp.gov.br',true);
 await f.controller.open('https://server.example',request);assert.equal(loads,1);assert.equal(f.windows.length,1);
 await f.controller.open('https://server.example',request);assert.equal(loads,1);
 }finally{await f.controller.shutdown();}
});
test('Encerramento destrói janelas mesmo se a gravação de cookies falhar',async()=>{
 const f=fixture();try{await f.controller.open('https://server.example',request);const w=f.windows[0];
 w.options.webPreferences.session.cookies.flushStore=async()=>{throw Error('Fixture IO');};
 await assert.rejects(f.controller.closeAll(),/Fixture IO/);assert.equal(w.isDestroyed(),true);assert.equal(f.controller.hasActive(),false);
 }finally{await f.controller.shutdown().catch(()=>{});}
});

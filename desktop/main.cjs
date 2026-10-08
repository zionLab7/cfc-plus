'use strict';
if(process.argv.includes('--portal-host'))require(require('node:path').join(process.resourcesPath,'server','native-host','host.cjs')).run();else{
const {app,BrowserWindow,Menu,ipcMain,dialog,shell,session,safeStorage,Tray}=require('electron');
const fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url');
const settings=require('./settings.cjs'),server=require('./server.cjs');
let window,config,setupMode=false,localPortals,tray,canQuit=false;
const setupUrl=pathToFileURL(path.join(__dirname,'setup.html')).href;
const smoke=process.argv.includes('--smoke-test');
if(smoke){app.setPath('userData',path.resolve(process.env.CFC_DESKTOP_SMOKE_DIR||path.join(process.cwd(),'test-results','desktop-smoke')));}
const configPath=()=>path.join(app.getPath('userData'),'connection.json');
const defaults=()=>({mode:'remote',url:'http://localhost:5050',dataDir:path.join(app.getPath('userData'),'server-data')});
function trusted(event){if(!setupMode||event.sender!==window?.webContents||event.senderFrame?.url!==setupUrl)throw new Error('Origem de configuração inválida.');}
function menu(){Menu.setApplicationMenu(Menu.buildFromTemplate([{label:'Aplicativo',submenu:[{label:'Conexão e servidor',click:showSetup},{label:'Recarregar',click:()=>window?.webContents.reload()},{label:'Encerrar portais locais',click:()=>localPortals?.closeAll().catch(()=>{})},{type:'separator'},{label:'Sair do atendimento',click:()=>app.quit()}]},{label:'Visualização',submenu:[{role:'resetZoom'},{role:'zoomIn'},{role:'zoomOut'},{role:'togglefullscreen'}]}]));}
function createWindow(setup=false){
 setupMode=setup;const previous=window;
 window=new BrowserWindow({width:1440,height:940,minWidth:920,minHeight:650,show:!smoke,backgroundColor:'#f5f7fb',title:'CFC+',webPreferences:{nodeIntegration:false,contextIsolation:true,sandbox:true,webSecurity:true,preload:path.join(__dirname,setup?'preload.cjs':'portal-preload.cjs')}});
 window.webContents.on('will-navigate',(event,url)=>{if(setupMode?url!==setupUrl:!settings.sameOrigin(url,config.url))event.preventDefault();});
 window.webContents.on('will-redirect',(event,url)=>{if(setupMode?url!==setupUrl:!settings.sameOrigin(url,config.url))event.preventDefault();});
 window.webContents.setWindowOpenHandler(({url})=>{if(settings.externalUrl(url))shell.openExternal(url);return {action:'deny'};});
 window.webContents.on('will-attach-webview',event=>event.preventDefault());
 window.webContents.session.setPermissionRequestHandler((_contents,_permission,callback)=>callback(false));
 window.webContents.on('did-fail-load',(_event,code,_description,_url,isMainFrame)=>{if(isMainFrame&&code!==-3&&!setupMode&&!smoke)showSetup();});
 window.on('close',event=>{if(!canQuit&&localPortals?.hasActive()){event.preventDefault();window.hide();}});
 previous?.destroy();menu();return window;
}
function showSetup(){createWindow(true).loadFile(path.join(__dirname,'setup.html'));}
async function connect(candidate){
 if(localPortals?.hasActive())await localPortals.closeAll();
 config=settings.validate(candidate);
 if(config.mode==='local')await server.ensureLocal(config,process.resourcesPath);else await server.health(config.url);
 settings.save(configPath(),config);const target=createWindow(false);await target.loadURL(config.url);
}
function trustedApp(event){if(setupMode||event.sender!==window?.webContents||event.senderFrame!==window?.webContents.mainFrame||!settings.sameOrigin(event.senderFrame.url,config.url))throw Error('Abra o portal local pela gestão autenticada do CFC+.');}
ipcMain.handle('portals:open',async(event,request)=>{
 trustedApp(event);
 if(!request||!/^[a-f0-9]{32}$/.test(request.profileId)||!['ecnh','detran'].includes(request.portal)||!/^([a-z][a-z0-9-]{2,47})?$/.test(request.schoolId||''))throw Error('Perfil local inválido.');
 return localPortals.open(config.url,{profileId:request.profileId,portal:request.portal,schoolId:request.schoolId||''});
});
ipcMain.handle('portals:devices',async event=>{
 trustedApp(event);
 const response=await window.webContents.session.fetch(config.url+'/api/auth/me',{credentials:'include'}),user=await response.json();
 if(!response.ok||!['Administrador','Gerente'].includes(user.role))throw Error('Entre como gerente ou administrador para conferir os leitores.');
 if(process.platform!=='win32')throw Error('O diagnóstico dos leitores está disponível no aplicativo Windows.');
 const executable=path.join(process.resourcesPath,'server','CfcPilot.exe');
 if(!fs.existsSync(executable))throw Error('Use o instalador Windows atualizado para executar o diagnóstico local.');
 const {execFile}=require('node:child_process');
 const value=await new Promise((resolve,reject)=>execFile(executable,['--device-diagnostics'],{cwd:app.getPath('userData'),windowsHide:true,timeout:15000,maxBuffer:131072},(error,stdout)=>error?reject(Error('O diagnóstico local não respondeu.')):resolve(stdout)));
 return JSON.parse(value);
});
async function authorizeLocal(request){
 const response=await window.webContents.session.fetch(config.url+'/api/browser/local-access',{method:'POST',credentials:'include',headers:{'Content-Type':'application/json','X-CFC-Client':'pilot','X-Operation-Id':require('node:crypto').randomUUID(),...(request.schoolId?{'X-CFC-School':request.schoolId}:{})},body:JSON.stringify(request)});
 const value=await response.json();if(!response.ok)throw Error(value.error||'Entre novamente no CFC+ para abrir este perfil.');return value;
}
function configureLocalPortals(){
 localPortals=require('./local-portals.cjs').create({BrowserWindow,session,dialog,safeStorage,root:app.getPath('userData'),authorize:authorizeLocal});
 session.defaultSession.webRequest.onCompleted({urls:['*://*/api/auth/logout']},details=>{if(details.statusCode>=200&&details.statusCode<300&&settings.sameOrigin(details.url,config.url))localPortals.closeAll().catch(()=>{});});
 app.on('select-client-certificate',(event,wc,url,list,callback)=>{localPortals.certificates(event,wc,url,list,callback).catch(()=>callback());});
 if(!smoke){tray=new Tray(path.join(__dirname,'build','icon.ico'));tray.setToolTip('CFC+ · portais locais');tray.setContextMenu(Menu.buildFromTemplate([{label:'Abrir CFC+',click:()=>{window?.show();window?.focus();}},{label:'Encerrar portais locais',click:()=>localPortals.closeAll().catch(()=>{})},{label:'Sair do CFC+',click:()=>app.quit()}]));tray.on('double-click',()=>{window?.show();window?.focus();});}
 app.on('before-quit',event=>{if(!canQuit){event.preventDefault();localPortals.shutdown().catch(()=>{}).finally(()=>{canQuit=true;app.quit();});}});
}
ipcMain.handle('setup:read',event=>{trusted(event);config=settings.read(configPath(),defaults());let firstAccess=null;
 if(config.mode==='local'){try{const value=JSON.parse(fs.readFileSync(path.join(config.dataDir,'first-access.json'),'utf8'));if(value.login==='admin'&&typeof value.password==='string')firstAccess=value;}catch{}}
 return {config,firstAccess,version:app.getVersion()};});
ipcMain.handle('setup:folder',async event=>{trusted(event);const result=await dialog.showOpenDialog(window,{title:'Pasta de dados do servidor local',properties:['openDirectory','createDirectory']});return result.canceled?'':result.filePaths[0];});
ipcMain.handle('setup:connect',async(event,candidate)=>{trusted(event);try{await connect(candidate);return {ok:true};}catch(error){return {ok:false,error:error.message};}});
if(!app.requestSingleInstanceLock())app.quit();else{
 app.on('second-instance',()=>{window?.show();window?.focus();});
 app.whenReady().then(async()=>{
  config=settings.read(configPath(),defaults());
  configureLocalPortals();
  if(smoke){
   const output=path.join(app.getPath('userData'),'smoke-result.json');fs.mkdirSync(path.dirname(output),{recursive:true});
   try{if(process.env.CFC_DESKTOP_SMOKE_LOCAL==='1')config={...defaults(),mode:'local'};await connect(config);fs.writeFileSync(output,JSON.stringify({ok:true,version:app.getVersion(),packaged:app.isPackaged,nodeIntegration:window.webContents.getLastWebPreferences().nodeIntegration,contextIsolation:window.webContents.getLastWebPreferences().contextIsolation,sandbox:window.webContents.getLastWebPreferences().sandbox,url:config.url}));app.quit();}
   catch(error){fs.writeFileSync(output,JSON.stringify({ok:false,error:error.message}));app.exit(1);}return;
  }
  try{await connect(config);}catch{showSetup();}
 });
 app.on('window-all-closed',()=>{if(!localPortals?.hasActive())app.quit();});
}
}

'use strict';
// Real Electron, synthetic cookies only. Never visits GOV or opens a real certificate.
const {app,BrowserWindow,session,safeStorage}=require('electron'),fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {create,profileDirectory}=require('../desktop/local-portals.cjs');
const root=path.resolve(process.argv[2]),stage=process.argv[3];app.setPath('userData',root);let controller;
app.on('window-all-closed',()=>{});
app.whenReady().then(async()=>{
 let count=0;const pid='a'.repeat(32),origin='https://fixture.example',request={profileId:pid,portal:'detran',schoolId:'school-alpha'},directory=profileDirectory(root,origin,request.schoolId,pid);
 const created=[];function Window(options){const w=new BrowserWindow({...options,show:false});const load=w.loadURL.bind(w);w.loadURL=()=>load('data:text/html,<title>CFC local synthetic fixture</title><p>Local browser fixture</p>');w.show=()=>{};created.push(w);return w;}Window.fromWebContents=BrowserWindow.fromWebContents;
 try{
  assert.equal(safeStorage.isEncryptionAvailable(),true);count++;
  if(stage==='restore')await session.fromPath(directory).clearStorageData({storages:['cookies']});
  controller=create({BrowserWindow:Window,session,safeStorage,dialog:{showMessageBox:async()=>({response:0})},root,authorize:async()=>({schoolId:request.schoolId,profile:{id:pid,kind:'Diretor',name:'Fixture'},url:'https://www.detran.sp.gov.br/detransp'})});
  await controller.open(origin,request);const w=created[0],preferences=w.webContents.getLastWebPreferences();assert.equal(preferences.sandbox,true);assert.equal(preferences.nodeIntegration,false);assert.equal(preferences.contextIsolation,true);count+=3;
  if(stage==='seed')await w.webContents.session.cookies.set({url:'https://www.detran.sp.gov.br/',name:'CFC_LOCAL_FIXTURE',value:'SYNTHETIC',path:'/'});
  else{assert.equal((await w.webContents.session.cookies.get({name:'CFC_LOCAL_FIXTURE'})).length,1);count++;}
  const other=session.fromPath(profileDirectory(root,origin,'school-beta',pid));assert.equal((await other.cookies.get({name:'CFC_LOCAL_FIXTURE'})).length,0);count++;
  w.close();await new Promise(r=>setTimeout(r,100));assert.equal(w.isDestroyed(),false);assert.equal(w.isVisible(),false);count+=2;
  await controller.shutdown();assert.equal(fs.existsSync(path.join(directory,'checkpoint.encrypted')),true);assert.equal(fs.readFileSync(path.join(directory,'checkpoint.encrypted')).includes(Buffer.from('SYNTHETIC')),false);count+=2;
  fs.writeFileSync(path.join(root,stage+'-result.json'),JSON.stringify({ok:true,checks:count}));console.log('Local Electron '+stage+': '+count+' checks passed');app.quit();
 }catch(error){fs.mkdirSync(root,{recursive:true});fs.writeFileSync(path.join(root,stage+'-result.json'),JSON.stringify({ok:false,error:error.stack}));if(controller)await controller.shutdown().catch(()=>{});app.exit(1);}
});

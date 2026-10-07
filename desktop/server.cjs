'use strict';
const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process');
async function health(url){
 const response=await fetch(url+'/api/health',{signal:AbortSignal.timeout(2500),redirect:'error'});
 if(!response.ok)throw new Error('O servidor não respondeu corretamente.');
 const value=await response.json();if(value.application!=='cfc-plus')throw new Error('A porta/endereço informado pertence a outro serviço.');return value;
}
async function ensureLocal(config,resources){
 try{return await health(config.url);}catch(error){
  if(error.message.includes('outro serviço'))throw error;
  // Any HTTP responder reserves the port, even when it is not a CFC health API.
  try{await fetch(config.url,{signal:AbortSignal.timeout(1500),redirect:'manual'});throw new Error('Já existe outro serviço na porta 5050. Não foi iniciado um segundo servidor.');}catch(check){if(check.message.includes('outro serviço'))throw check;}
 }
 const executable=path.join(resources,'server','CfcPilot.exe');if(!fs.existsSync(executable))throw new Error('O servidor local não está incluído nesta distribuição.');
 fs.mkdirSync(config.dataDir,{recursive:true});
 const stdout=fs.openSync(path.join(config.dataDir,'desktop-server.log'),'a',0o600),stderr=fs.openSync(path.join(config.dataDir,'desktop-server-error.log'),'a',0o600);
 let child;try{child=spawn(executable,['--urls',config.url],{cwd:path.dirname(executable),env:{...process.env,CFC_DATA_DIR:config.dataDir,CFC_SERVICE_MODE:'1',CFC_REQUIRE_BOOTSTRAP:'1',NativePortal__Enabled:'true'},detached:true,windowsHide:true,stdio:['ignore',stdout,stderr]});
 await new Promise((resolve,reject)=>{child.once('spawn',resolve);child.once('error',reject);});child.unref();
 }finally{fs.closeSync(stdout);fs.closeSync(stderr);}
 // Ownership stays with the server: there is deliberately no kill-on-client-exit.
 for(let n=0;n<60;n++){try{return await health(config.url);}catch{}await new Promise(resolve=>setTimeout(resolve,500));}
 throw new Error('O servidor ainda não iniciou. Consulte desktop-server-error.log na pasta dos dados.');
}
module.exports={health,ensureLocal};

'use strict';
const fs=require('node:fs'),path=require('node:path');
function serverUrl(value){
 const url=new URL(String(value).trim());
 const local=['localhost','127.0.0.1','[::1]'].includes(url.hostname);
 if((url.protocol!=='https:'&&!(url.protocol==='http:'&&local))||url.username||url.password||url.search||url.hash||url.pathname!=='/')throw new Error('Use HTTPS para a VPS ou HTTP em localhost. Informe apenas o endereço do servidor, sem caminho ou credenciais.');
 return url.origin;
}
function validate(config){
 if(!config||!['remote','local'].includes(config.mode))throw new Error('Escolha o modo de funcionamento.');
 const result={mode:config.mode,url:serverUrl(config.url),dataDir:String(config.dataDir||'')};
 if(config.mode==='local'&&!['http://localhost:5050','http://127.0.0.1:5050'].includes(result.url))throw new Error('O piloto local utiliza a porta 5050.');
 if(!path.isAbsolute(result.dataDir))throw new Error('Selecione uma pasta absoluta para os dados locais.');
 return result;
}
function read(file,defaults){try{return validate(JSON.parse(fs.readFileSync(file,'utf8')));}catch{return defaults;}}
function save(file,config){const value=validate(config);fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file+'.tmp',JSON.stringify(value,null,2),{mode:0o600});fs.renameSync(file+'.tmp',file);return value;}
function sameOrigin(target,origin){try{return new URL(target).origin===origin;}catch{return false;}}
function externalUrl(target){try{const u=new URL(target);return ['https:','http:'].includes(u.protocol)&&!u.username&&!u.password;}catch{return false;}}
module.exports={serverUrl,validate,read,save,sameOrigin,externalUrl};

'use strict';
// Server reachability, rather than navigator.onLine, determines local availability.
globalThis.CfcConnection={create({ping,isOffline,onUnavailable,onRecovery,schedule=setInterval,cancel=clearInterval,interval=20000}){
 let pending=false,reachable=null,timer;
 async function probe(){
  if(pending)return;pending=true;
  try{
   let up=false;try{up=await ping();}catch{}
   const recovering=up&&(reachable===false||isOffline());reachable=up;
   if(!up)onUnavailable();else if(recovering)await onRecovery();
  }finally{pending=false;}
 }
 return {probe,start(){if(timer===undefined)timer=schedule(()=>{probe().catch(()=>{});},interval);probe().catch(()=>{});},stop(){if(timer!==undefined)cancel(timer);timer=undefined;}};
}};

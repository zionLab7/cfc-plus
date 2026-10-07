'use strict';let promptInstall;
window.addEventListener('beforeinstallprompt',e=>{e.preventDefault();promptInstall=e;document.querySelector('#install').hidden=false;});
document.querySelector('#install').addEventListener('click',async()=>{if(!promptInstall)return;await promptInstall.prompt();await promptInstall.userChoice;promptInstall=null;document.querySelector('#install').hidden=true;});
fetch('/api/distribution',{cache:'no-store'}).then(r=>r.json()).then(c=>{document.querySelector('#school').textContent=c.schoolName;if(c.desktop){const a=document.querySelector('#windows');a.href=c.desktop;a.hidden=false;document.querySelector('#windows-hint').hidden=true;}document.querySelector('#support').textContent=c.support?'Suporte: '+c.support:'';}).catch(()=>{});

if('serviceWorker' in navigator)navigator.serviceWorker.register('/sw.js').catch(()=>{});

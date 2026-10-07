'use strict';
const form=document.getElementById('connection'),error=document.getElementById('error');
function update(){const local=form.elements.mode.value==='local';document.getElementById('local-options').hidden=!local;if(local)form.elements.url.value='http://localhost:5050';}
window.cfcSetup.read().then(({config,firstAccess})=>{for(const key of ['mode','url','dataDir'])form.elements[key].value=config[key];update();if(firstAccess){document.getElementById('first-access').hidden=false;document.getElementById('initial-password').value=firstAccess.password;}});
form.elements.mode.addEventListener('change',update);
document.getElementById('folder').onclick=async()=>{const directory=await window.cfcSetup.folder();if(directory)form.elements.dataDir.value=directory;};
document.getElementById('reveal').onclick=()=>{const input=document.getElementById('initial-password');input.type=input.type==='password'?'text':'password';};
form.onsubmit=async event=>{event.preventDefault();error.textContent='';const button=form.querySelector('[type=submit]');button.disabled=true;button.textContent='Conectando…';try{const result=await window.cfcSetup.connect(Object.fromEntries(new FormData(form)));if(!result.ok)error.textContent=result.error;}catch{error.textContent='Não foi possível conectar. Revise o endereço e tente novamente.';}finally{button.disabled=false;button.textContent='Entrar no aplicativo';}};

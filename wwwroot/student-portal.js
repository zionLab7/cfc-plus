'use strict';
let studentPortalView=null;
function stopStudentPortalView(){
 const view=studentPortalView;if(!view)return;studentPortalView=null;view.queue.length=0;clearTimeout(view.timer);view.abort.abort();if(view.professional)fetch('/api/'+portalSessionPath(view)+'/release',{method:'POST',headers:{'X-CFC-Client':'pilot','X-CFC-Viewer':view.lease},keepalive:true}).catch(()=>{});if(view.imageUrl)URL.revokeObjectURL(view.imageUrl);document.getElementById('modal').classList.remove('portal-modal');
}
function portalSessionPath(view){return view.professional?'browser/sessions/'+encodeURIComponent(view.sid)+'/'+encodeURIComponent(view.jid):'students/'+encodeURIComponent(view.sid)+'/gov/sessions/'+encodeURIComponent(view.jid);}
async function portalRequest(view,suffix='',body){
 if(!view.professional)return api(portalSessionPath(view)+suffix,body);
 const response=await fetch('/api/'+portalSessionPath(view)+suffix,{method:body===undefined?'GET':'POST',cache:'no-store',headers:{'X-CFC-Viewer':view.lease,...(body===undefined?{}:{'X-CFC-Client':'pilot','Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});
 const result=await response.json();if(!response.ok)throw new Error(result.error||'Não foi possível acessar a sessão.');return result;
}
function updatePortalStatus(view,state){
 if(studentPortalView!==view||!view.root.isConnected)return;
 view.root.querySelector('#portal-host').textContent=(state.host||'Carregando portal…')+(state.engine==='native'?' · navegador interativo':'');
 if(!view.confirming)view.root.querySelector('#portal-status').textContent=state.message||'';
 view.root.querySelector('#portal-status').classList.toggle('green',!!state.authenticated);
 view.root.querySelector('#portal-server-error').textContent=state.error||'';
 view.root.querySelector('#portal-network').textContent=state.network||'';
 view.root.querySelector('#portal-pause').disabled=!state.active;
 if(view.professional){
  const form=view.root.querySelector('.professional-confirm');view.authenticated=!!state.authenticated;
  form.querySelector('#professional-confirm-button').disabled=!form.elements.confirmed.checked||!!view.expired||view.authenticated;
  form.querySelector('#professional-confirm-hint').textContent=view.authenticated?'Conta conferida. A sessão está pronta para uso.':state.connectedIndicator?'O portal indica que há uma conta conectada. Confira se ela pertence ao profissional deste perfil.':'Depois do login, abra o menu da conta no portal, onde aparece Sair, e confira a pessoa conectada antes de marcar abaixo.';
 }
 view.root.querySelector('#portal-diagnostics').textContent=(state.diagnostics||[]).map(x=>new Date(x.time).toLocaleTimeString('pt-BR')+' · '+x.stage+' · '+x.message).join('\n')||'Nenhum erro registrado nesta sessão.';
}
async function portalInput(view,input){
 if(studentPortalView!==view||view.expired)return;
 const last=view.queue.at(-1);if(input.kind==='move'&&last?.kind==='move')view.queue[view.queue.length-1]=input;else if(input.kind==='text'&&last?.kind==='text'&&(last.text.length+input.text.length)<=2000)last.text+=input.text;else view.queue.push(input);
 if(view.busy)return;
 view.busy=true;view.root.querySelector('#portal-view-error').textContent='';
 try{while(studentPortalView===view&&view.queue.length){const state=await portalRequest(view,'/input',view.queue.shift());updatePortalStatus(view,state);}}
 catch(err){view.queue.length=0;if(view.root.isConnected){view.root.querySelector('#portal-view-error').textContent=err.message;portalLeaseError(view,err);}}
 finally{view.busy=false;}
}
function portalLeaseError(view,error){
 if(!view.professional||!error.message.includes('O controle dessa visualização terminou'))return;
 view.expired=true;view.queue.length=0;clearTimeout(view.timer);
 view.root.querySelector('#portal-resume').hidden=false;
 view.root.querySelector('#portal-status').textContent='Controle da visualização pausado. Clique em Retomar controle para continuar na mesma página.';
 view.root.querySelector('#professional-confirm-button').disabled=true;
}
async function refreshPortalView(view){
 if(studentPortalView!==view||!view.root.isConnected||!document.getElementById('modal').open){if(studentPortalView===view)stopStudentPortalView();return;}
 if(view.busy||view.pointer!==undefined){view.timer=setTimeout(()=>refreshPortalView(view),150);return;}
 try{
  const [status,response]=await Promise.all([portalRequest(view),fetch('/api/'+portalSessionPath(view)+'/frame',{cache:'no-store',signal:view.abort.signal,headers:view.professional?{'X-CFC-Viewer':view.lease}:{}})]);
  if(studentPortalView!==view||!view.root.isConnected)return;
  updatePortalStatus(view,status);
  if(!response.ok){const error=await response.json().catch(()=>({}));throw new Error(error.error||'A imagem do portal ainda não está disponível.');}
  const imageUrl=URL.createObjectURL(await response.blob());
  if(studentPortalView!==view){URL.revokeObjectURL(imageUrl);return;}
  const image=view.root.querySelector('#portal-screen');const old=view.imageUrl;view.imageUrl=imageUrl;
  image.onload=()=>{if(old)URL.revokeObjectURL(old);};image.src=imageUrl;image.hidden=false;
  view.root.querySelector('#portal-loading').hidden=true;view.root.querySelector('#portal-view-error').textContent='';
 }catch(err){if(studentPortalView===view&&err.name!=='AbortError'){view.root.querySelector('#portal-view-error').textContent=err.message;portalLeaseError(view,err);}}
 finally{if(studentPortalView===view&&!view.expired)view.timer=setTimeout(()=>refreshPortalView(view),1200);}
}
function portalPoint(image,e){const box=image.getBoundingClientRect();return {x:Math.max(0,Math.min(1279,(e.clientX-box.left)*1280/box.width)),y:Math.max(0,Math.min(799,(e.clientY-box.top)*800/box.height))};}
function bindPortalPointer(image,view){
 image.addEventListener('pointerdown',e=>{
  if(e.button!==0||view.pointer!==undefined)return;e.preventDefault();image.focus({preventScroll:true});view.pointer=e.pointerId;image.setPointerCapture(e.pointerId);view.point=portalPoint(image,e);portalInput(view,{kind:'down',...view.point});
 });
 image.addEventListener('pointermove',e=>{
  if(view.pointer!==undefined&&view.pointer!==e.pointerId)return;
  view.point=portalPoint(image,e);portalInput(view,{kind:'move',...view.point});
 });
 function release(e){
  if(view.pointer!==e.pointerId)return;view.point=portalPoint(image,e);view.pointer=undefined;portalInput(view,{kind:'up',...view.point});if(image.hasPointerCapture(e.pointerId))image.releasePointerCapture(e.pointerId);
 }
 image.addEventListener('pointerup',release);image.addEventListener('pointercancel',release);
 image.addEventListener('lostpointercapture',e=>{if(view.pointer===e.pointerId){view.pointer=undefined;portalInput(view,{kind:'up',...view.point});}});
 // Keyboard/accessibility activation still produces a single click, never a
 // second click after the pointer-down/up pair.
 image.addEventListener('click',e=>{if(e.detail===0)portalInput(view,{kind:'click',...portalPoint(image,e)});});
}
function showStudentPortal(sid,jid,state,professional=null){
 stopStudentPortalView();const student=professional?{name:professional.name,cpf:professional.kind}:find('students',sid);if(!student)throw new Error('Selecione o aluno.');
 modal('Navegador do aluno · GOV.BR / DETRAN','<section id="student-portal"><div class="portal-identity"><strong>'+esc(student.name)+'</strong><span>CPF '+esc(student.cpf)+' · perfil exclusivo no servidor</span></div><div class="portal-toolbar"><span id="portal-host"></span><div class="actions"><button id="portal-back" type="button">← Voltar na página</button><button id="portal-pause" type="button">Pausar preenchimento</button><button id="portal-qr" type="button">Entrar com QR Code</button><button id="portal-new" type="button">Novo acesso</button></div></div><p id="portal-status" class="notice" role="status"></p><p id="portal-server-error" class="form-error" role="alert"></p><div class="portal-canvas"><p id="portal-loading">Carregando a página do navegador do servidor…</p><img id="portal-screen" alt="Página interativa do navegador do aluno no servidor" tabindex="0" hidden draggable="false"></div><form id="portal-text-form" class="portal-text"><label>Digitar no campo selecionado da página<input name="text" type="password" autocomplete="off" maxlength="2000" placeholder="Clique no campo da página acima antes de digitar"></label><button type="submit">Enviar texto</button><button id="portal-enter" type="button">Enter</button><button id="portal-tab" type="button">Tab</button></form><p class="hint">Clique na página e use o teclado ou o campo acima. Resolva você mesmo o CAPTCHA e a verificação em duas etapas nessa página. Fechar esta tela conserva o navegador; o portal pode expirar o login.</p><p id="portal-view-error" class="form-error" role="alert"></p><details><summary>Diagnóstico do acesso</summary><p id="portal-network" class="form-error"></p><pre id="portal-diagnostics" class="portal-diagnostics"></pre></details><div class="form-actions"><button data-action="close-modal" type="button">Fechar visualização</button></div></section>','SESSÃO PERSISTENTE DO ALUNO');
 document.getElementById('modal').classList.add('portal-modal');
 const view={sid,jid,professional:!!professional,lease:professional?.lease||'',root:document.getElementById('student-portal'),abort:new AbortController(),timer:null,imageUrl:'',busy:false,queue:[]};studentPortalView=view;
 const zoom=document.createElement('button');zoom.type='button';zoom.textContent='Tamanho original';view.root.querySelector('.portal-toolbar .actions').prepend(zoom);zoom.onclick=()=>{const original=view.root.querySelector('.portal-canvas').classList.toggle('portal-original');zoom.textContent=original?'Ajustar à tela':'Tamanho original';};
 if(professional){
  document.getElementById('modal-title').textContent='Navegador do '+professional.kind.toLowerCase()+' · '+(jid==='ecnh'?'e-CNH':'DETRAN');document.getElementById('modal-eyebrow').textContent='SESSÃO NO SERVIDOR';
  view.root.querySelector('.portal-identity span').textContent=professional.kind+' · perfil persistente no servidor';view.root.querySelector('#portal-screen').alt='Navegador do profissional no servidor';
  for(const key of ['pause','qr','new'])view.root.querySelector('#portal-'+key).hidden=true;
  if(jid==='detran'){
   const recover=document.createElement('button');recover.type='button';recover.textContent='Reiniciar acesso DETRAN';recover.title='Limpa somente a tentativa de autenticação deste profissional. O próximo acesso exigirá novo login.';
   recover.onclick=async()=>{if(recover.dataset.confirmed!=='yes'){recover.dataset.confirmed='yes';recover.textContent='Confirmar reinício de '+professional.name;view.root.querySelector('#portal-view-error').textContent='Será necessário novo login. Clique novamente para reiniciar somente o acesso deste profissional. Os dados da escola serão preservados.';return;}recover.dataset.confirmed='';recover.textContent='Reiniciar acesso DETRAN';recover.disabled=true;view.queue.length=0;try{updatePortalStatus(view,await portalRequest(view,'/recover',{acknowledged:true}));view.root.querySelector('#portal-screen').hidden=true;view.root.querySelector('#portal-loading').hidden=false;view.root.querySelector('#portal-view-error').textContent='';}catch(err){view.root.querySelector('#portal-view-error').textContent=err.message;portalLeaseError(view,err);}finally{recover.disabled=false;}};
   view.root.querySelector('.portal-toolbar .actions').append(recover);
  }
  const confirm=document.createElement('form');confirm.className='professional-confirm';confirm.innerHTML='<strong>Conferir a conta conectada</strong><p id="professional-confirm-hint" class="hint"></p><label>Perfil que você está conferindo<input name="name" value="'+esc(professional.name)+'" readonly></label><label><input name="confirmed" type="checkbox" required> Conferi que a conta no portal pertence a '+esc(professional.name)+'</label><button id="professional-confirm-button" type="submit" disabled>Confirmar conta conectada</button>';
  const resume=document.createElement('button');resume.type='button';resume.id='portal-resume';resume.textContent='Retomar controle';resume.hidden=true;view.root.querySelector('.portal-toolbar .actions').append(resume);
  resume.onclick=async()=>{resume.disabled=true;try{const result=await api('browser/open',{profileId:view.sid,portal:view.jid});view.lease=result.lease;view.expired=false;resume.hidden=true;view.root.querySelector('#portal-view-error').textContent='';updatePortalStatus(view,result.session);refreshPortalView(view);}catch(err){view.root.querySelector('#portal-view-error').textContent=err.message;}finally{resume.disabled=false;}};
  view.root.querySelector('.form-actions').before(confirm);confirm.elements.confirmed.onchange=()=>{confirm.querySelector('#professional-confirm-button').disabled=!confirm.elements.confirmed.checked||!!view.expired||!!view.authenticated;};
  confirm.onsubmit=async event=>{event.preventDefault();try{updatePortalStatus(view,await portalRequest(view,'/confirm',{name:confirm.elements.name.value,confirmed:confirm.elements.confirmed.checked}));}catch(err){view.root.querySelector('#portal-view-error').textContent=err.message;portalLeaseError(view,err);}};
 }
 if(state)updatePortalStatus(view,state);
 const image=view.root.querySelector('#portal-screen');
 bindPortalPointer(image,view);
 image.addEventListener('wheel',e=>{e.preventDefault();portalInput(view,{kind:'scroll',...portalPoint(image,e),deltaY:Math.max(-1600,Math.min(1600,e.deltaY))});},{passive:false});
 image.addEventListener('keydown',e=>{
  if(e.key==='Escape'){e.preventDefault();portalInput(view,{kind:'key',key:'Escape'});return;}
  const allowed=['Tab','Enter',' ','Backspace','Delete','ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'];
  if(allowed.includes(e.key)){e.preventDefault();portalInput(view,{kind:'key',key:e.key===' '?'Space':e.key==='Tab'&&e.shiftKey?'Shift+Tab':e.key});}
  else if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='a'){e.preventDefault();portalInput(view,{kind:'key',key:e.metaKey?'Meta+A':'Control+A'});}
  else if(e.key.length===1&&!e.ctrlKey&&!e.metaKey&&!e.altKey){e.preventDefault();portalInput(view,{kind:'text',text:e.key});}
 });
 view.root.querySelector('#portal-text-form').addEventListener('submit',async e=>{e.preventDefault();const input=e.target.elements.text,text=input.value;input.value='';if(text)await portalInput(view,{kind:'text',text});});
 view.root.querySelector('#portal-back').addEventListener('click',()=>portalInput(view,{kind:'back'}));
 const reload=document.createElement('button');reload.type='button';reload.textContent='Recarregar página';reload.title='Recarrega a página atual conservando o perfil do navegador.';view.root.querySelector('.portal-toolbar .actions').append(reload);reload.addEventListener('click',()=>portalInput(view,{kind:'reload'}));
 if(professional&&jid==='detran'){const entry=document.createElement('button');entry.type='button';entry.textContent='Entrar com GOV.BR';view.root.querySelector('.portal-toolbar .actions').append(entry);entry.addEventListener('click',()=>portalInput(view,{kind:'entry'}));}
 view.root.querySelector('#portal-enter').addEventListener('click',()=>portalInput(view,{kind:'key',key:'Enter'}));
 view.root.querySelector('#portal-tab').addEventListener('click',()=>portalInput(view,{kind:'key',key:'Tab'}));
 view.root.querySelector('#portal-pause').addEventListener('click',async()=>{try{updatePortalStatus(view,await api(portalSessionPath(view)+'/pause',{}));}catch(err){view.root.querySelector('#portal-view-error').textContent=err.message;}});
 view.root.querySelector('#portal-qr').addEventListener('click',async()=>{const button=view.root.querySelector('#portal-qr');button.disabled=true;try{updatePortalStatus(view,await api(portalSessionPath(view)+'/qr',{}));}catch(err){view.root.querySelector('#portal-view-error').textContent=err.message;}finally{button.disabled=false;}});
 view.root.querySelector('#portal-new').addEventListener('click',()=>{
  view.confirming=true;
  view.root.querySelector('#portal-status').innerHTML='Um novo acesso reinicia a página e envia o CPF e a senha salva deste aluno. <button id="portal-confirm-new" type="button">Iniciar novo acesso</button> <button id="portal-cancel-new" type="button">Manter página</button>';
  view.root.querySelector('#portal-cancel-new').onclick=()=>{view.confirming=false;};
  view.root.querySelector('#portal-confirm-new').onclick=async()=>{view.confirming=false;try{updatePortalStatus(view,await api(portalSessionPath(view)+'/restart',{}));}catch(err){view.root.querySelector('#portal-view-error').textContent=err.message;}};
 });
 refreshPortalView(view);
 const end=document.createElement('button');end.type='button';end.textContent='Encerrar navegador';end.title='Fecha o navegador do servidor e preserva o perfil salvo.';view.root.querySelector('.form-actions').prepend(end);
 end.addEventListener('click',async()=>{if(view.professional&&!confirm('Encerrar esta página no servidor e desativar sua restauração automática? Os dados do perfil serão preservados.'))return;end.disabled=true;try{await portalRequest(view,'/close',{});stopStudentPortalView();document.getElementById('modal').close();toast('Navegador encerrado. O perfil foi preservado.');}catch(err){end.disabled=false;if(view.root.isConnected)view.root.querySelector('#portal-view-error').textContent=err.message;}});
}
document.getElementById('modal').addEventListener('close',stopStudentPortalView);

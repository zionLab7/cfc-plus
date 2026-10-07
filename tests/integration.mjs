import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdir, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { createServer } from 'node:http';
import { randomUUID } from 'node:crypto';

const base='http://localhost:5051';
const assembly=resolve(process.env.CFC_TEST_ASSEMBLY||'bin/Debug/net10.0/CfcPilot.dll');
const directory=resolve('test-results',randomUUID());await mkdir(directory,{recursive:true});
const relational=process.env.CFC_TEST_RELATIONAL==='1';if(relational){const init=spawnSync('dotnet',[assembly,'--create-relational-fixture',directory],{cwd:resolve('.'),encoding:'utf8'});assert.equal(init.status,0,init.stderr);}
let sent=0, passed=0;const historyPages=[];
const mock=createServer(async(req,res)=>{assert.equal(req.headers.apikey,'test-secret');let body='';for await(const chunk of req)body+=chunk;res.setHeader('Content-Type','application/json');if(req.url==='/instance/connectionState/pilot')res.end(JSON.stringify({instance:{state:'open'}}));else if(req.url==='/message/sendText/pilot'){sent++;assert.equal(JSON.parse(body).number,'5511999999999');res.end(JSON.stringify({key:{id:'mock-message'},status:'PENDING'}));}else if(req.url==='/chat/findChats/pilot')res.end(JSON.stringify([{id:'chat-1'}]));else if(req.url==='/chat/findMessages/pilot'){const query=JSON.parse(body);assert.equal(query.where.key.remoteJid,'5511999999999@s.whatsapp.net');assert.equal(query.offset,50);historyPages.push(query.page);res.end(JSON.stringify({messages:{records:[{message:{conversation:'Histórico de teste'}}]}}));}else if(req.url==='/instance/connect/pilot')res.end(JSON.stringify({count:1,base64:'data:image/png;base64,dGVzdA=='}));else{res.statusCode=404;res.end('{}');}});
await new Promise(r=>mock.listen(5052,'127.0.0.1',r));
let server;
function start(){server=spawn('dotnet',[assembly,'--urls',base],{cwd:resolve('.'),env:{...process.env,CFC_DATA_DIR:directory,Evolution__BaseUrl:'http://127.0.0.1:5052',Evolution__Instance:'pilot',Evolution__ApiKey:'test-secret'},stdio:['ignore','pipe','pipe']});server.stdout.on('data',()=>{});server.stderr.on('data',b=>process.stderr.write(b));}
async function ready(){for(let n=0;n<100;n++){try{if((await fetch(base+'/api/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw new Error('Servidor de teste não iniciou.');}
async function stop(){if(server?.exitCode===null){server.kill();await new Promise(r=>server.once('exit',r));}}
function client(){let cookie='';return async(path,body,operation)=>{const r=await fetch(base+'/api/'+path,{method:body===undefined?'GET':'POST',headers:{...(cookie?{Cookie:cookie}:{}),...(body===undefined?{}:{'Content-Type':'application/json','X-CFC-Client':'pilot'}),...(operation?{'X-Operation-Id':operation}:{})},body:body===undefined?undefined:JSON.stringify(body)});if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];return {status:r.status,body:await r.json().catch(()=>({}))};};}
function ok(condition,label){assert.ok(condition,label);passed++;console.log('PASS '+label);}
start();
try{
await ready();const admin=client(), clerk=client(), pupil=client(), instructor=client();
ok((await admin('snapshot')).status===401,'Consulta exige autenticação');
ok((await admin('auth/login',{login:'admin',password:'wrong'})).status===401,'Senha inválida recusada');
const authResult=await admin('auth/login',{login:'admin',password:'cfc2026'}); assert.equal(authResult.status,200,JSON.stringify(authResult)); ok(true,'Login de demonstração');
const initial=(await admin('snapshot')).body;
ok(initial.students.length===12&&initial.units.length===2,'Carga fictícia completa');
ok(!JSON.stringify(initial).includes('passwordHash'),'Hashes não expostos no snapshot');
let op=()=>randomUUID();const command=(action,body,key=op())=>admin('commands/'+action,body,key);
ok((await command('student',{name:'Teste CPF',cpf:'11111111111',unitId:'u1'})).status===400,'CPF repetido inválido');
const student=(await command('student',{name:'Aluno de integração',cpf:'52998224725',unitId:'u1',birth:'2000-01-01'})).body;
ok(student.id&&student.name==='Aluno de integração','Cadastro persistente');
ok((await command('student',{name:'Duplicado',cpf:'52998224725',unitId:'u2'})).status===409,'Duplicidade de CPF entre filiais');
const today=initial.lessons[0].start.slice(0,10);
const matriculationKey=op(),matriculationBody={student:{name:'Matrícula unificada teste',cpf:'12345678909',unitId:'u1',phone:'11999990000'},enrollment:{packageId:'p1',unitId:'u1',parts:4,firstDue:today,discount:0}};
const unified=await command('matriculate',matriculationBody,matriculationKey);
assert.equal(unified.status,200,JSON.stringify(unified.body));
ok(unified.body.studentId&&unified.body.enrollmentId,'Um salvamento cria cadastro e matrícula');
ok((await command('matriculate',matriculationBody,matriculationKey)).body.studentId===unified.body.studentId,'Matrícula unificada é idempotente');
const afterUnified=(await admin('snapshot')).body;
ok(afterUnified.installments.filter(x=>x.enrollmentId===unified.body.enrollmentId).length===4&&afterUnified.documents.some(x=>x.enrollmentId===unified.body.enrollmentId),'Matrícula unificada gera parcelas e contrato');
const countBeforeFailed=afterUnified.students.length;
ok((await command('matriculate',{student:{name:'Cadastro incompleto teste',cpf:'11144477735',unitId:'u1'}})).status===400,'Matrícula sem serviço é recusada sem cadastro parcial');
ok((await command('matriculate',{student:{name:'Aluno sem contrato inválido',cpf:'11144477735',unitId:'u1'},enrollment:{...matriculationBody.enrollment,parts:0}})).status===400,'Matrícula inválida é recusada');
ok((await admin('snapshot')).body.students.length===countBeforeFailed,'Falha na matrícula não deixa cadastro parcial');
ok((await command('matriculate',matriculationBody)).status===409,'CPF existente exige selecionar seu cadastro');
ok((await command('matriculate',{studentId:unified.body.studentId,enrollment:{...matriculationBody.enrollment,packageId:'p2'}})).status===200,'Nova matrícula reutiliza aluno existente');
const enrollmentKey=op(), enrollBody={studentId:student.id,packageId:'p1',unitId:'u1',parts:7,firstDue:today,discount:0.01};
const enrolled=await command('enroll',enrollBody,enrollmentKey);ok(enrolled.status===200,'Matrícula gera dados relacionados');
const eid=enrolled.body.id, state=(await admin('snapshot')).body;
const parts=state.installments.filter(x=>x.enrollmentId===eid);
ok(parts.length===7&&Math.round(parts.reduce((v,x)=>v+x.amount,0)*100)===219999,'Parcelas somam o contrato até o centavo');
ok(state.documents.some(x=>x.enrollmentId===eid&&x.text.includes('Aluno de integração')),'Contrato preenchido automaticamente');
ok((await command('enroll',enrollBody,enrollmentKey)).body.id===eid,'Matrícula repetida é idempotente');
ok((await command('enroll',{...enrollBody,parts:4},enrollmentKey)).status===409,'Identificador não pode representar outro payload');
const paymentKey=op(),payment={enrollmentId:eid,amount:100,method:'Dinheiro',description:'Pagamento de teste'};
const received=await command('receive',payment,paymentKey);ok(received.status===200&&received.body.receipt,'Recebimento cria recibo');
await Promise.all([command('receive',payment,paymentKey),command('receive',payment,paymentKey)]);
let after=(await admin('snapshot')).body;
ok(after.entries.filter(x=>x.enrollmentId===eid&&x.kind==='Recebimento').length===1,'Repetições concorrentes não duplicam recebimento');
ok(after.installments.filter(x=>x.enrollmentId===eid).reduce((v,x)=>v+x.paid,0)===100,'Baixa parcial nas parcelas');
ok((await command('receive',{...payment,amount:9000})).status===409,'Recebimento acima do saldo recusado');
ok((await command('void',{id:received.body.id,reason:'Erro de lançamento no teste'})).status===200,'Estorno com trilha de reversão');
after=(await admin('snapshot')).body;
ok(after.installments.filter(x=>x.enrollmentId===eid).every(x=>x.paid===0),'Estorno reabre as parcelas corretas');
ok((await command('void',{id:received.body.id,reason:'Tentativa repetida'})).status===409,'Estorno não pode ser duplicado');
ok((await command('void',{id:'paid0',reason:'Reverter entrada de demonstração'})).status===200,'Estorno funciona também com carga inicial');
after=(await admin('snapshot')).body;ok(after.installments.find(x=>x.id==='part0-0').paid===0,'Entrada fictícia reabre a parcela vinculada');
const day=new Date(today+'T12:00:00');day.setDate(day.getDate()+2);while([0,6].includes(day.getDay()))day.setDate(day.getDate()+1);const ds=day.toISOString().slice(0,10),startTime=ds+'T09:00:00';
const lesson={enrollmentId:eid,instructorId:'i1',vehicleId:'v1',start:startTime};
ok((await command('lesson',lesson)).status===200,'Agendamento dentro da grade');
ok((await command('lesson',{...lesson,enrollmentId:'e2'})).status===409,'Conflito simultâneo de recurso bloqueado');
ok((await command('lesson',{...lesson,start:ds+'T06:00:00'})).status===409,'Horário fora da grade bloqueado');
const beforeBatch=(await admin('snapshot')).body.lessons.length;
ok((await command('lesson',{...lesson,starts:[ds+'T10:00:00',startTime]})).status===409,'Lote parcialmente conflitante recusado');
ok((await admin('snapshot')).body.lessons.length===beforeBatch,'Falha no lote não grava horários parciais');
ok((await command('lesson',{...lesson,vehicleId:'v4',start:ds+'T11:00:00'})).status===409,'Veículo em manutenção bloqueado');
ok((await command('exam',{enrollmentId:eid,type:'Prático',start:ds+'T14:00:00',location:'Banca teste',instructorId:'i1',vehicleId:'v1'})).status===400,'Exame prático exige pré-requisitos');
const stale={...student,name:'Edição antiga',expectedRevision:0};ok((await command('student',stale)).status===409,'Edição desatualizada não sobrescreve dados');
await clerk('auth/login',{login:'atendente',password:'cfc2026'});
ok((await clerk('commands/policy',{cancelHours:0,maxDaily:10,minutes:20,debtMode:'Aviso'},op())).status===403,'Atendente não altera políticas');
ok((await clerk('commands/enroll',{...enrollBody,discount:10},op())).status===403,'Atendente não concede desconto');
await pupil('auth/login',{login:'aluno',password:'cfc2026'});
const limited=(await pupil('snapshot')).body;
ok(limited.students.length===1&&limited.students[0].id==='s1'&&!limited.entries.some(x=>x.studentId!=='s1'),'Aluno acessa somente seus dados');
ok((await pupil('commands/receive',payment,op())).status===403,'Aluno não altera financeiro');
ok((await pupil('commands/task',{title:'Gostaria de remarcar uma aula'},op())).status===200,'Aluno pode solicitar atendimento');
await instructor('auth/login',{login:'instrutor',password:'cfc2026'});
const teacher=(await instructor('snapshot')).body;
ok(teacher.entries.length===0&&teacher.students.every(x=>x.cpf===''),'Instrutor não recebe financeiro nem CPF de alunos');
ok((await instructor('commands/lesson-status',{id:'a1',status:'Cancelada',reason:'Outra aula'},op())).status===403,'Instrutor não altera aula de outro profissional');
ok((await admin('integrations/evolution')).body.configured,'Conector Evolution consulta servidor de teste');
const profile=(await command('browser-profile',{kind:'Instrutor',instructorId:'i1'})).body;
ok(/^[a-f0-9]{32}$/.test(profile.id)&&profile.name===initial.instructors.find(x=>x.id==='i1').name,'Perfil persistente vinculado ao instrutor');
ok((await command('browser-profile',{kind:'Instrutor',instructorId:'i1'})).status===409,'Perfil do instrutor não pode ser duplicado');
ok((await command('browser-profile',{kind:'Instrutor',instructorId:'inexistente'})).status===400,'Perfil exige instrutor ativo existente');
ok((await admin('browser/profiles')).body.profiles.length===1,'Equipe consulta perfis de navegador');
const professionalPath='browser/sessions/'+profile.id+'/ecnh';
ok((await client()(professionalPath+'/frame')).status===401,'Imagem profissional exige autenticação');
ok((await pupil(professionalPath+'/frame')).status===403,'Aluno não visualiza sessão do diretor ou instrutor');
ok((await instructor(professionalPath+'/input',{kind:'text',text:'teste'})).status===403,'Conta de instrutor não comanda sessão privada do atendimento');
ok((await admin(professionalPath+'/frame')).status===409,'Imagem profissional exige token da visualização ativa');
ok((await clerk('commands/browser-profile',{kind:'Diretor',name:'Diretor teste'},op())).status===403,'Criação de perfil exige gerente ou administrador');
ok((await pupil('browser/profiles')).status===403,'Aluno não consulta sessões de profissionais');
ok((await pupil('integrations/devices')).status===403,'Aluno não consulta equipamentos do computador');
ok((await admin('integrations/devices')).status===200,'Diagnóstico dos leitores disponível à gestão local');
ok((await pupil('browser/open',{profileId:profile.id,portal:'ecnh'},op())).status===403,'Aluno não abre navegador de profissional');
ok((await admin('browser/open',{profileId:profile.id,portal:'file:///C:/Windows'},op())).status===400,'Abertura permite somente portais definidos pelo sistema');
ok((await admin('browser/open',{profileId:'../../perfil',portal:'ecnh'},op())).status===404,'Perfil arbitrário não pode abrir navegador');
ok((await admin('integrations/evolution/chats')).body[0].id==='chat-1','Conector consulta conversas');
ok((await admin('integrations/evolution/messages',{jid:'5511999999999@s.whatsapp.net'})).body.messages.records[0].message.conversation==='Histórico de teste','Histórico consulta somente a conversa selecionada');
ok((await admin('integrations/evolution/messages',{jid:'../../outra-rota'})).status===400,'Identificador de conversa inválido recusado');
ok((await admin('integrations/evolution/messages',{jid:'5511999999999@s.whatsapp.net',page:2})).status===200&&historyPages.at(-1)===2,'Histórico permite solicitar a segunda página');
ok((await admin('integrations/evolution/messages',{jid:'5511999999999@s.whatsapp.net',page:0})).status===400,'Página inválida do histórico é recusada');
ok((await admin('integrations/evolution/connect',{})).body.count===1,'Administrador consulta QR de conexão');
ok((await clerk('integrations/evolution/connect',{})).status===403,'Atendente não altera pareamento do WhatsApp');
const draft=(await command('message-draft',{phone:'5511999999999',text:'Teste local'})).body;
const messageBody={messageId:draft.id,phone:draft.phone,text:draft.text},sendKey=op();
const sends=await Promise.all([admin('integrations/evolution/send',messageBody,sendKey),admin('integrations/evolution/send',messageBody,sendKey)]);
ok(sends.every(x=>x.status===200)&&sent===1,'Conector envia somente uma vez ao mock local mesmo com chamadas concorrentes');
ok((await admin('integrations/evolution/send',messageBody,op())).status===409,'Nova tentativa não reenvia mensagem aceita');
ok((await pupil('integrations/evolution')).status===403,'Portal do aluno não acessa a integração');
ok((await fetch(base+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({login:'admin',password:'cfc2026'})})).status===403,'Mutação sem cabeçalho de origem recusada');
const persisted=relational?(await admin('snapshot')).body:JSON.parse(await readFile(resolve(directory,'pilot.json'),'utf8'));ok(persisted.students.some(x=>x.id===student.id),'Dados gravados em disco');
await stop();start();await ready();const restarted=client();await restarted('auth/login',{login:'admin',password:'cfc2026'});const final=(await restarted('snapshot')).body;ok(final.enrollments.some(x=>x.id===eid)&&final.entries.some(x=>x.reverses===received.body.id),'Matrícula e estorno sobrevivem ao reinício');
if(relational){const secondReceipt=await restarted('commands/receive',{enrollmentId:'e2',amount:1,method:'PIX',description:'Teste isolado de sequência'},op());ok(secondReceipt.status===200&&secondReceipt.body.receipt!==received.body.receipt,'Recibos únicos entre alunos e após reinício');const c=(await restarted('automation/catalog')).body;ok(c.operations.length===32,'Catálogo dos portais disponível no SQLite');const prepareKey=op(),profile=(await restarted('browser/profiles')).body.profiles[0];const prepared=await restarted('automation/prepare',{operation:'ecnh-aulas',profileId:profile.id,studentId:student.id},prepareKey);ok(prepared.status===200,'Operação externa pode ser preparada sem envio');const replay=await restarted('automation/prepare',{operation:'ecnh-aulas',profileId:profile.id,studentId:student.id},prepareKey);ok(replay.body.id===prepared.body.id,'Preparação da operação é idempotente');ok((await restarted('automation/jobs/'+prepared.body.id+'/capture',{},op())).status===409,'Captura exige janela e identidade conferidas');ok((await pupil('automation/catalog')).status===403,'Aluno não acessa operações governamentais');}
if(relational){const pupilProfile=(await command('student-browser-profile',{studentId:student.id})).body;ok(pupilProfile.kind==='Aluno'&&pupilProfile.studentId===student.id,'Sessão do portal pertence ao aluno selecionado');ok((await command('student-browser-profile',{studentId:student.id})).body.id===pupilProfile.id,'Perfil do aluno reutilizado');ok((await admin('automation/prepare',{operation:'student-detran',profileId:pupilProfile.id,studentId:'s3'},op())).status===409,'Sessão de um aluno não é usada para outro');const professional=(await admin('browser/profiles')).body.profiles.find(x=>x.kind==='Instrutor');ok((await admin('automation/prepare',{operation:'student-detran',profileId:professional.id,studentId:student.id},op())).status===409,'Login do aluno não usa perfil de certificado profissional');ok((await admin('automation/prepare',{operation:'student-detran',profileId:pupilProfile.id,studentId:student.id},op())).status===200,'Teste DETRAN do aluno preparado sem envio externo');const p=(await admin('snapshot')).body.policy;ok((await command('policy',{...p,retroactiveEntries:'Administrador',retroactiveExams:'Administrador',retroactiveLessons:'Administrador'})).status===200,'Administrador configura permissões retroativas');const saved=(await admin('snapshot')).body.policy;ok(saved.retroactiveEntries==='Administrador'&&saved.retroactiveLessons==='Administrador','Regras persistidas no SQLite');const historical=(await command('receive',{enrollmentId:eid,amount:1,method:'PIX',description:'Pagamento histórico isolado',date:'2025-06-10',reason:'Conferência de recibo de teste'}));ok(historical.status===200&&historical.body.date==='2025-06-10'&&historical.body.receipt.includes('-2025-'),'Recibo retroativo gravado com sequência do ano correto');}
console.log(`\n${passed} verificações aprovadas. Base isolada em ${directory}`);
}finally{await stop();await new Promise(r=>mock.close(r));}



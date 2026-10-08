// End-to-end central isolation, branches, restart persistence and financial reconciliation.
// Only synthetic data; never uses the local GP database.
import assert from 'node:assert/strict';
import {spawn,spawnSync} from 'node:child_process';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {randomUUID} from 'node:crypto';
const root=resolve('test-results','central-'+randomUUID()),base='http://127.0.0.1:5058';await mkdir(root,{recursive:true});
let server,output='',passed=0,number=10;
function start(){server=spawn('dotnet',[resolve('bin/Debug/net10.0/CfcPilot.dll'),'--urls',base],{cwd:resolve('.'),env:{...process.env,CFC_DATA_DIR:root,CFC_MULTI_TENANT:'true',CFC_TRUSTED_PROXY:'127.0.0.1',NativePortal__Enabled:'false'},windowsHide:true,stdio:['ignore','pipe','pipe']});server.stdout.on('data',b=>output+=b);server.stderr.on('data',b=>output+=b);}
async function stop(){if(server?.exitCode===null){server.kill();await new Promise(r=>server.once('exit',r));}}
async function ready(){for(let i=0;i<100;i++){try{if((await fetch(base+'/api/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw Error('Central did not start');}
function check(v,n){assert.ok(v,n);passed++;console.log('PASS '+n);}
function client(){let cookie='',school='';const ip='192.0.2.'+(number++);return {setSchool:v=>school=v,request:async(path,body,extra={})=>{const r=await fetch(base+'/api/'+path,{method:body===undefined?'GET':'POST',headers:{'X-Forwarded-Proto':'https','X-Forwarded-For':ip,...(cookie?{Cookie:cookie}:{}),...(school?{'X-CFC-School':school}:{}),...(body===undefined?{}:{...(body instanceof FormData?{}:{'Content-Type':Buffer.isBuffer(body)?'application/octet-stream':'application/json'}),'X-CFC-Client':'pilot','X-Operation-Id':randomUUID()}),...extra},body:body===undefined?undefined:(Buffer.isBuffer(body)||body instanceof FormData)?body:JSON.stringify(body)});if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];const text=await r.text();let data;try{data=JSON.parse(text);}catch{data={text};}return {status:r.status,body:data,headers:r.headers};}};}
async function expect(c,path,body,status=200,extra){const r=await c.request(path,body,extra);assert.equal(r.status,status,path+': '+JSON.stringify(r.body));return r.body;}
const platform=client(),a=client(),b=client();
try{
start();await ready();check((await fetch(base+'/api/health').then(r=>r.json())).central,'Um servidor central');
const owner=JSON.parse(await readFile(resolve(root,'platform-first-access.json'),'utf8'));
await expect(platform,'platform/login',owner);await expect(platform,'platform/schools',undefined,428);
await expect(platform,'platform/password',{currentPassword:owner.password,newPassword:'Central-Test-Password-2026'});
await expect(platform,'platform/login',{login:owner.login,password:'Central-Test-Password-2026'});
for(const id of ['school-alpha','school-beta'])await expect(platform,'platform/schools',{id,name:'Autoescola '+id});
check((await expect(platform,'platform/schools')).length===2,'Admin da plataforma cria duas escolas sem novo deploy');
await expect(platform,'platform/schools',{id:'school-alpha',name:'Duplicada'},409);
await expect(platform,'platform/schools',{id:'../outside',name:'Inválida'},401);
await expect(platform,'snapshot',undefined,401);
for(const [c,id] of [[a,'school-alpha'],[b,'school-beta']]){const first=JSON.parse(await readFile(resolve(root,'tenants',id,'first-access.json'),'utf8'));await expect(c,'auth/login',{schoolId:id,...first});c.setSchool(id);await expect(c,'snapshot',undefined,428);await expect(c,'auth/password',{currentPassword:first.password,newPassword:'Same-School-Password-2026'});await expect(c,'auth/login',{schoolId:id,login:'admin',password:'Same-School-Password-2026'});}
check((await expect(a,'auth/me')).schoolId==='school-alpha'&&(await expect(b,'auth/me')).id==='admin','Mesmo ID de usuário e senha, contextos de escola diferentes');
await expect(a,'snapshot',undefined,403,{'X-CFC-School':'school-beta'});await expect(a,'snapshot?schoolId=school-beta',undefined,403);await expect(a,'auth/units?schoolId=unknown-school',undefined,401);
check((await expect(platform,'platform/schools')).length===2,'Login não cadastra escolas desconhecidas');
await expect(a,'platform/schools',undefined,401);
const branch=await expect(a,'commands/unit',{name:'Filial Alpha'});check((await expect(a,'snapshot')).units.length===2&&(await expect(b,'snapshot')).units.length===1,'SubIDs de filiais pertencem à escola');
const pkg=await expect(a,'commands/package',{name:'Pacote piloto',category:'B',price:1000,parts:2,lessons:2});
const student=await expect(a,'commands/student',{name:'Aluno Alpha',cpf:'52998224725',unitId:'main',birth:'2000-01-01'});
const studentB=await expect(b,'commands/student',{name:'Aluno Beta',cpf:'52998224725',unitId:'main',birth:'2000-01-01'});
check(student.id!==studentB.id,'Mesmo CPF em escolas independentes');
await expect(b,'finance/students/'+student.id,undefined,404);await expect(b,'students/'+student.id+'/gov-credential',undefined,404);
check((await expect(b,'snapshot')).packages.length===0,'Pacotes e fichas isolados');
const d=new Date(),today=new Intl.DateTimeFormat('en-CA',{timeZone:'America/Sao_Paulo'}).format(d);d.setDate(d.getDate()-1);const yesterday=new Intl.DateTimeFormat('en-CA',{timeZone:'America/Sao_Paulo'}).format(d);
const enrollment=await expect(a,'commands/enroll',{studentId:student.id,packageId:pkg.id,unitId:'main',parts:2,firstDue:yesterday,discount:0});
let finance=await expect(a,'finance/students/'+student.id);const [first,second]=finance.installments;
const pay=await expect(a,'commands/receive',{enrollmentId:enrollment.id,installmentId:second.id,amount:200,method:'PIX',description:'Pagamento segunda parcela'});
finance=await expect(a,'finance/students/'+student.id);check(finance.summary.debits===1000&&finance.summary.paid===200&&finance.summary.outstanding===800,'Totais de débito, pago e saldo completos');check(finance.installments.find(p=>p.id===second.id).paid===200&&finance.installments.find(p=>p.id===first.id).paid===0,'Recebimento parcial na parcela escolhida');
await expect(a,'commands/receive',{enrollmentId:enrollment.id,installmentId:second.id,amount:301,method:'PIX',description:'Excede parcela'},409);
const enrollment2=await expect(a,'commands/enroll',{studentId:student.id,packageId:pkg.id,unitId:branch.id,parts:2,firstDue:today,discount:0});
await expect(a,'commands/receive',{enrollmentId:enrollment2.id,installmentId:first.id,amount:10,method:'PIX',description:'Parcela de outra matrícula'},409);
const credit=await expect(a,'commands/entry',{enrollmentId:enrollment.id,kind:'Crédito',amount:50,method:'Ajuste',description:'Desconto teste'});
finance=await expect(a,'finance/students/'+student.id);check(finance.summary.paid===200&&finance.summary.credits===50&&finance.summary.outstanding===1750,'Desconto não infla dinheiro pago e soma todas as matrículas');
await expect(a,'commands/void',{id:credit.id,reason:'Estorno desconto teste'});
const cash=await expect(a,'commands/closecash',{unitId:'main'});check(cash.total===200,'Estorno de crédito não retira dinheiro do caixa');
await expect(a,'commands/void',{id:pay.id,reason:'Devolução teste'});finance=await expect(a,'finance/students/'+student.id);check(finance.summary.paid===0&&finance.summary.credits===0&&finance.summary.outstanding===2000&&finance.summary.paymentRefunds===200,'Estornos restauram saldo sem apagar originais');
check(finance.summary.overdue===500&&finance.summary.scheduledOutstanding===2000,'Parcelas vencidas e saldo das parcelas conciliados');
const general=await expect(a,'finance/summary?from='+today+'&to='+today);check(general.period.cashNet===0&&general.period.paymentRefunds===200&&general.portfolio.outstanding===2000&&general.accountOutstanding===2000,'Caixa líquido e carteira de todas as datas separados');
check((await expect(a,'finance/summary?unit='+branch.id+'&from='+today+'&to='+today)).accountOutstanding===1000,'Financeiro pode filtrar filial sem misturar saldos');
const before=(await expect(b,'finance/summary?from='+today+'&to='+today));check(before.accountOutstanding===0&&before.total===0,'Nenhum lançamento financeiro vazou para a outra escola');
await expect(a,'students/'+student.id+'/gov-credential',{password:'SYNTHETIC-NOT-A-REAL-GOV-PASSWORD'});check((await expect(b,'students/'+studentB.id+'/gov-credential')).hasPassword===false,'Credenciais GOV da escola ficam separadas');
const job=await expect(a,'system/imports',{name:'test.zip',bytes:4});check((await expect(b,'system/imports')).jobs.length===0,'Fila de importação é por escola');await expect(b,'system/imports/'+job.id+'/discard',{},404);
const personal=await expect(a,'commands/user',{name:'Aluno teste',login:'pupil',role:'Aluno',linkedId:student.id,password:'Pupil-Test-Password-2026',active:true});const pupil=client();await expect(pupil,'auth/login',{schoolId:'school-alpha',login:'pupil',password:'Pupil-Test-Password-2026'});pupil.setSchool('school-alpha');await expect(pupil,'auth/password',{currentPassword:'Pupil-Test-Password-2026',newPassword:'New-Pupil-Password-2026'});await expect(pupil,'auth/login',{schoolId:'school-alpha',login:'pupil',password:'New-Pupil-Password-2026'});await expect(pupil,'finance/students/'+student.id);await expect(pupil,'finance/summary?from='+today+'&to='+today,undefined,403);
const other=await expect(a,'commands/student',{name:'Outro Alpha',cpf:'12345678909',unitId:'main'});await expect(pupil,'finance/students/'+other.id,undefined,403);await expect(a,'community/settings',{studentFinance:false});await expect(pupil,'finance/students/'+student.id,undefined,403);check(personal.id&&true,'Aluno só consulta seu financeiro e respeita a configuração da escola');
const form=new FormData();form.append('file',new Blob(['%PDF-1.4\n%%EOF']), 'synthetic.pdf');form.append('title','Documento sintético');form.append('category','Outro');const file=await expect(a,'students/'+student.id+'/files',form);check((await expect(a,'student-files/'+file.id)).text.startsWith('%PDF-'),'Documento protegido pode ser aberto na escola de origem');await expect(b,'student-files/'+file.id,undefined,404);check((await expect(b,'students/'+studentB.id+'/files')).total===0,'Anexos não vazam entre escolas');
const profile=await expect(a,'commands/browser-profile',{name:'Diretor sintético',kind:'Diretor'});check((await expect(a,'browser/profiles')).profiles.some(p=>p.id===profile.id)&&(await expect(b,'browser/profiles')).profiles.length===0,'Perfis governamentais pertencem à escola');
const local=await expect(a,'browser/local-access',{profileId:profile.id,portal:'ecnh'});
check(local.schoolId==='school-alpha'&&local.profile.id===profile.id&&local.url.startsWith('https://www.e-cnhsp.sp.gov.br/'),'Abertura local autorizada retorna escola e URL oficiais');
await expect(b,'browser/local-access',{profileId:profile.id,portal:'ecnh'},404);
await expect(pupil,'browser/local-access',{profileId:profile.id,portal:'ecnh'},403);
await expect(a,'browser/local-access',{profileId:profile.id,portal:'ecnh'},403,{'X-CFC-School':'school-beta'});
await expect(a,'browser/local-access',{profileId:profile.id,portal:'arbitrary'},400);
check((await expect(a,'snapshot')).audit.some(x=>x.action==='local-browser-open'),'Abertura local auditada sem abrir navegador na VPS');

await expect(platform,'platform/schools',{id:'school-import',name:'Escola de importação'});const importer=client(),initialImport=JSON.parse(await readFile(resolve(root,'tenants/school-import/first-access.json'),'utf8'));await expect(importer,'auth/login',{schoolId:'school-import',...initialImport});importer.setSchool('school-import');await expect(importer,'auth/password',{currentPassword:initialImport.password,newPassword:'Import-Test-Password-2026'});await expect(importer,'auth/login',{schoolId:'school-import',login:'admin',password:'Import-Test-Password-2026'});
const fixture=resolve(root,'synthetic-package');const creation=spawnSync('dotnet',[resolve('bin/Debug/net10.0/CfcPilot.dll'),'--create-import-fixture',fixture],{encoding:'utf8',windowsHide:true});assert.equal(creation.status,0,creation.stderr);const bytes=await readFile(resolve(fixture,'fixture.zip')),upload=await expect(importer,'system/imports',{name:'synthetic.zip',bytes:bytes.length});await expect(importer,'system/imports/'+upload.id+'/chunk?offset=0',bytes);await expect(importer,'system/imports/'+upload.id+'/verify',{});let imported;for(let i=0;i<200;i++){await new Promise(r=>setTimeout(r,100));imported=(await expect(importer,'system/imports')).jobs.find(j=>j.id===upload.id);assert.notEqual(imported.state,'Falhou',imported.message);if(imported.state==='Pronta')break;}assert.equal(imported.state,'Pronta');await expect(importer,'system/imports/'+upload.id+'/activate',{confirmed:true});await new Promise(r=>setTimeout(r,2500));check((await expect(b,'auth/me')).schoolId==='school-beta'&&(await expect(importer,'snapshot')).statistics.datasetId===imported.importId,'Ativação recarrega só a escola importada, sem derrubar outra sessão');
const times=[];await Promise.all(Array.from({length:24},async(_,i)=>{const t=performance.now();assert.equal((await (i%2?a:b).request('snapshot')).status,200);times.push(performance.now()-t);}));times.sort((x,y)=>x-y);check(times.length===24,'Consultas concorrentes em duas escolas concluídas');
await stop();start();await ready();await expect(a,'auth/me');await expect(b,'auth/me');check((await expect(a,'finance/students/'+student.id)).summary.outstanding===2000&&(await expect(b,'snapshot')).students.length===1,'Reinício preserva cookies, bancos e separação entre escolas');
await writeFile(resolve(root,'result.json'),JSON.stringify({ok:true,checks:passed,realData:false,concurrentRequests:24,p95Ms:times[22]},null,2));console.log(passed+' verificações centrais aprovadas. p95 sintético: '+Math.round(times[22])+' ms');
}catch(e){await writeFile(resolve(root,'server-test.log'),output);throw e;}finally{await stop();}

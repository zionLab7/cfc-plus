const assert=require('node:assert/strict');const core=require('../wwwroot/portal/core.js');let count=0;function check(label,f){f();count++;console.log('PASS '+label);}
const sample={id:'lesson-1',start:'2026-10-07T23:40:00',minutes:50,category:'B',student:'Aluno; teste\nPRIVATE',instructor:'Prof',vehicle:'Carro',plate:'TEST123',unit:'Escola'};
check('Calendário mantém horário de São Paulo',()=>assert.match(core.ics(sample),/DTSTART;TZID=America\/Sao_Paulo:20261007T234000/));
check('Calendário soma duração atravessando meia-noite',()=>assert.match(core.ics(sample),/DTEND;TZID=America\/Sao_Paulo:20261008T003000/));
check('Conteúdo de calendário não injeta linhas',()=>assert.match(core.ics(sample),/Aluno\\; teste\\nPRIVATE/));
check('Horário vazio não vira evento inválido',()=>assert.throws(()=>core.ics({...sample,start:''})));
check('Conteúdo HTML escapado',()=>assert.equal(core.escape('<img onerror="x">'),'&lt;img onerror=&quot;x&quot;&gt;'));
check('Histórico longo fica paginado',()=>assert.deepEqual(core.page(Array.from({length:500},(_,i)=>i),2),Array.from({length:12},(_,i)=>i+12)));
check('Próximas aulas não incluem dias antigos',()=>assert.equal(core.visibleLessons([{id:'past',start:'2026-10-06T10:00'},{id:'today',start:'2026-10-07T10:00'}],{mode:'upcoming',today:'2026-10-07'}).length,1));
check('Filtro do instrutor mantém apenas aluno escolhido',()=>assert.equal(core.visibleLessons([{studentId:'a',start:'2026-10-07T10:00'},{studentId:'b',start:'2026-10-07T10:00'}],{mode:'history',student:'a'}).length,1));
console.log(count+' verificações da interface passaram.');

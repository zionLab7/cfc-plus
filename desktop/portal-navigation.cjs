'use strict';
// Official portal destinations only; shared by runtime and offline checks.
const hosts=new Set(['www.e-cnhsp.sp.gov.br','www.detran.sp.gov.br','detran.sp.gov.br','sso.acesso.gov.br','certificado.sso.acesso.gov.br','login.sp.gov.br','idp.sp.gov.br','operacoes.sp.gov.br','www.poupatempo.sp.gov.br','poupatempo.sp.gov.br','portalservicos.senatran.serpro.gov.br']);
function allowed(value){try{const u=new URL(value);return u.protocol==='https:'&&!u.username&&!u.password&&(!u.port||u.port==='443')&&hosts.has(u.hostname);}catch{return false;}}
module.exports={hosts,allowed};

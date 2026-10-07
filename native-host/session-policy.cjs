'use strict';
const portalDomains=new Set(['www.detran.sp.gov.br','detran.sp.gov.br','www.e-cnhsp.sp.gov.br','e-cnhsp.sp.gov.br']);
const resetPortalDomains=new Set(['www.detran.sp.gov.br','detran.sp.gov.br']);
const loginDomains=new Set(['sso.acesso.gov.br','acesso.gov.br','login.sp.gov.br','idp.sp.gov.br','operacoes.sp.gov.br']);
const domain=c=>String(c?.domain||'').replace(/^\./,'').toLowerCase();
function checkpointCookie(c){return !!c&&portalDomains.has(domain(c))&&(!c.expirationDate||c.expirationDate>Date.now()/1000);}
function resetCookie(c){return resetPortalDomains.has(domain(c))||loginDomains.has(domain(c));}
const resetOrigins=[...resetPortalDomains,...loginDomains].map(h=>'https://'+h);
module.exports={checkpointCookie,resetCookie,resetOrigins};

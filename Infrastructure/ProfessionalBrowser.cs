using System.Net;
namespace CfcPilot;

public sealed class ProfessionalBrowser(BrowserRuntime runtime)
{
    public object Availability()=>runtime.Availability();
    public static string StudentPortal()=>"https://www.detran.sp.gov.br/login_with_sso.do?glide_sso_id=54bb16bb1bca42503eddea8ce54bcbcf";
    public static string Portal(string key)=>key switch {
        "ecnh"=>"https://www.e-cnhsp.sp.gov.br/gefor/SGU/login.do?method=iniciarLogin",
        "detran"=>"https://www.detran.sp.gov.br/detransp",
        _=>throw new RuleException("Selecione e-CNHsp ou DETRAN-SP.")
    };
    public void Validate(BrowserProfile profile,string portal,IPAddress? remote)
    {
        if(!Guid.TryParseExact(profile.Id,"N",out _))throw new RuleException("Perfil de navegador inválido.");
        if(profile.Kind is not("Diretor" or "Instrutor"))throw new RuleException("Selecione um perfil de diretor ou instrutor.",409);
        Portal(portal);
    }
}

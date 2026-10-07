using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Playwright;
namespace CfcPilot;
public static class ProfessionalSessionChecks
{
    sealed class Clock:TimeProvider{public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    sealed class Env(string root):IWebHostEnvironment
    {public string ApplicationName{get;set;}="CfcPilot";public string EnvironmentName{get;set;}="Testing";public string ContentRootPath{get;set;}=root;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public string WebRootPath{get;set;}=root;public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();}
    public static void Run()
    {
        var passed=0;void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);passed++;Console.WriteLine("PASS "+label);}
        void Reject(Action action,int status,string label){try{action();throw new InvalidOperationException(label);}catch(RuleException ex){Check(ex.Status==status,label);}}
        var clock=new Clock();var lease=new PortalControl(clock);var a=lease.Acquire("a");Check(lease.Held&&lease.Owner=="a","Controle identifica o usuário atual");
        Reject(()=>lease.Acquire("b"),409,"Outro atendimento não controla o mesmo portal simultaneamente");Reject(()=>lease.Check("b",a),409,"Token sozinho não dá controle a outro usuário");
        var next=lease.Acquire("a");Check(a!=next,"Reabrir a visualização renova o token");Reject(()=>lease.Check("a",a),409,"Visualização antiga não envia comandos após a retomada");
        lease.Release("a",next);Check(!lease.Held,"Fechar a visualização libera o controle");var b=lease.Acquire("b");clock.Now=clock.Now.AddSeconds(40);lease.Check("b",b);clock.Now=clock.Now.AddSeconds(40);Check(lease.Held,"Atualizações da visualização renovam apenas o controle local");clock.Now=clock.Now.AddSeconds(6);Reject(()=>lease.Check("b",b),409,"Computador desconectado perde controle após o prazo");Check(!lease.Held,"Controle expirado permite outro atendimento");
        Reject(()=>lease.Acquire(""),401,"Controle exige usuário identificado");
        var root=Path.Combine(Directory.GetCurrentDirectory(),"test-results","professional-"+Guid.NewGuid().ToString("N"));var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"CFC_DATA_DIR",root},{"CFC_SERVICE_MODE","1"},{"CFC_REQUIRE_BOOTSTRAP","1"}}).Build();var env=new Env(root);
        var registry=new ProfessionalSessionRegistry(config,env);var profile=Guid.NewGuid().ToString("N");registry.Set(profile,"ecnh",true);registry.Set(profile,"detran",true);
        Check(new ProfessionalSessionRegistry(config,env).Read().Count(x=>x.Restore)==2,"Preferências sobrevivem à recriação do serviço");registry.Set(profile,"ecnh",false);Check(registry.Read().Single(x=>x.Portal=="detran").Restore&&!registry.Read().Single(x=>x.Portal=="ecnh").Restore,"Encerrar e-CNH preserva a preferência do DETRAN");
        var registryPath=Path.Combine(root,"professional-sessions.json");var before=File.ReadAllText(registryPath);registry.Set(profile,"detran",true);
        Check(File.ReadAllText(registryPath)==before,"Restaurar preferência existente não regrava o registro");
        var retryPath=Path.Combine(root,"retry.json");File.WriteAllText(retryPath,"original");var attempts=0;
        ProfessionalSessionRegistry.ReplaceFile(retryPath,"novo",(source,target)=>{if(++attempts<3)throw new UnauthorizedAccessException();File.Move(source,target,true);});
        Check(attempts==3&&File.ReadAllText(retryPath)=="novo","Bloqueio temporário de arquivo é repetido sem perder o registro");
        try{ProfessionalSessionRegistry.ReplaceFile(retryPath,"perdido",(_,_)=>throw new UnauthorizedAccessException());throw new InvalidOperationException("Era esperado acesso negado.");}catch(UnauthorizedAccessException){}
        Check(File.ReadAllText(retryPath)=="novo"&&!Directory.GetFiles(root,"retry.json.*.tmp").Any(),"Falha persistente conserva registro anterior e limpa somente o temporário próprio");
        Check(ProfessionalSessionRestorer.Recoverable(new UnauthorizedAccessException()),"Acesso negado na restauração não encerra o servidor");
        Check(ProfessionalSessionRestorer.Recoverable(new IOException())&&!ProfessionalSessionRestorer.Recoverable(new InvalidOperationException()),"Restauração isola falhas de arquivo sem mascarar erros inesperados");
        Reject(()=>registry.Set("../fora","ecnh",true),400,"Registro impede caminho arbitrário de perfil");Reject(()=>registry.Set(profile,"outro",true),400,"Registro aceita somente portais conhecidos");
        var runtime=new BrowserRuntime(config,env);var options=new PortalBrowserOptions(MaxProfessionalProfiles:12);runtime.Save(new User{Role="Administrador"},options);Check(runtime.Options().MaxProfessionalProfiles==12,"Administrador configura o limite de perfis persistentes");
        Check(!BrowserRuntime.ResolveHeadless(false,true),"Servidor com desktop respeita a execução com janela");
        Check(BrowserRuntime.ResolveHeadless(false,false),"Servidor sem desktop mantém execução sem janela");
        Check(BrowserRuntime.ResolveHeadless(true,true),"Opção explícita sem janela continua disponível");
        Check(runtime.LaunchOptions(options).Headless==runtime.EffectiveHeadless(options),"Modo do navegador usa a disponibilidade real de desktop");Reject(()=>runtime.Save(new User{Role="Administrador"},options with{MaxProfessionalProfiles=0}),400,"Limite inválido é recusado");
        Check(runtime.ProfileDirectory(options,profile)!=runtime.ProfileDirectory(options,Guid.NewGuid().ToString("N")),"Perfis de profissionais não compartilham cookies");
        var store=new Store(config,env);var access=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"first-access.json"))).RootElement;var password=access.GetProperty("password").GetString()!;var admin=store.UsersByLogin("admin","").GetAwaiter().GetResult().Single();Check(password!="cfc2026"&&Passwords.Verify(password,admin.PasswordHash),"Instalação nova gera senha privada de administrador");Check(!store.Read().GetAwaiter().GetResult().Users.Any(x=>x.Active&&x.Login!="admin"),"Instalação nova desativa acessos fictícios conhecidos");
        var registryText=File.ReadAllText(Path.Combine(root,"professional-sessions.json"));Check(!registryText.Contains(password)&&!registryText.Contains("cookie",StringComparison.OrdinalIgnoreCase),"Registro de restauração não contém credenciais ou cookies");
        var protection=DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"test-keys")));var vault=new ProfessionalCookieVault(runtime,protection);var secret="COOKIE_FICTICIO_SEM_ACESSO_REAL";
        vault.Save(options,profile,[new(){Name="session",Value=secret,Domain="www.e-cnhsp.sp.gov.br",Path="/",Secure=true},new(){Name="expired",Value="old",Domain="www.detran.sp.gov.br",Path="/",Expires=1},new(){Name="foreign",Value="other",Domain="example.org",Path="/"}]);
        Check(vault.Read(options,profile).Single().Value==secret,"Cookie de sessão sobrevive a checkpoint sem mudar sua validade");Check(!File.ReadAllText(Path.Combine(runtime.ProfileDirectory(options,profile),"cfc-session-cookies.protected")).Contains(secret),"Cookie de sessão fica protegido pelas chaves do servidor");
        Check(new ProfessionalCookieVault(runtime,protection).Read(options,profile).Length==1,"Cookies vencidos e de domínio externo não são restaurados");Check(vault.Read(options,Guid.NewGuid().ToString("N")).Length==0,"Cookies de outro profissional não são compartilhados");
        Cookie[] snapshot=[new(){Name="csrf",Value="old",Domain=".sso.acesso.gov.br",Path="/"},new(){Name="session",Value="missing",Domain="sso.acesso.gov.br",Path="/"}];
        var missing=ProfessionalCookieVault.MissingCookies(snapshot,[new(){Name="csrf",Value="new",Domain="sso.acesso.gov.br",Path="/"}]);
        Check(missing.Length==1&&missing[0].Name=="session","Checkpoint não substitui cookies SSO atuais do perfil");
        Check(ProfessionalCookieVault.MissingCookies(snapshot,[new(){Name="csrf",Value="other-path",Domain="sso.acesso.gov.br",Path="/other"}]).Length==2,"Cookies com caminhos diferentes conservam identidades distintas");
        vault.Save(options,profile,[]);Check(vault.Read(options,profile).Length==0,"Remoção de cookies no portal atualiza o checkpoint");
        Check(NativePortalSessions.CookiesToRestore("[{\"name\":\"pending-sso\"}]").Length==0,"Checkpoint nativo antigo sem confirmação não restaura tentativa interrompida");
        Check(NativePortalSessions.CookiesToRestore("{\"verified\":false,\"cookies\":[{\"name\":\"pending-sso\"}]}").Length==0,"Logout e identidade não confirmada invalidam restauração nativa");
        Check(NativePortalSessions.CookiesToRestore("{\"verified\":true,\"cookies\":[{\"name\":\"session\"}]}").Length==1,"Sessão nativa confirmada conserva cookies no checkpoint");
        Check(PortalSessions.Allowed("https://idp.sp.gov.br/auth/realms/idpsp").Host=="idp.sp.gov.br","Redirecionamento oficial IDP.SP permitido");
        foreach(var url in new[]{"http://idp.sp.gov.br/","https://idp.sp.gov.br.example.org/","https://other.sp.gov.br/","https://idp.sp.gov.br:444/","https://user@idp.sp.gov.br/"})
            Reject(()=>PortalSessions.Allowed(url),400,"IDP.SP não amplia destinos: "+url);
        var portalPage=new GovLoginObservation(PageReady:true);
        Check(NativePortalSessions.DetranConnection("www.detran.sp.gov.br",portalPage,"Menu\nOlá, Ricardo!\nComo podemos ajudar?"),"Saudação pessoal do DETRAN reconhece conexão com menu fechado");
        Check(!NativePortalSessions.DetranConnection("sso.acesso.gov.br",portalPage,"Olá, Ricardo!"),"Saudação fora do DETRAN não confirma conexão");
        Check(!NativePortalSessions.DetranConnection("www.detran.sp.gov.br",portalPage with{Entry=true},"Olá, Ricardo!"),"Entrada GOV.BR visível impede reconhecimento pela saudação");
        Check(!NativePortalSessions.DetranConnection("www.detran.sp.gov.br",portalPage with{Password=true},"Olá, Ricardo!"),"Senha aberta impede reconhecimento pela saudação");
        Check(!NativePortalSessions.DetranConnection("www.detran.sp.gov.br",portalPage with{Challenge=true},"Olá, Ricardo!"),"Verificação pendente impede reconhecimento pela saudação");
        Check(!NativePortalSessions.DetranConnection("www.detran.sp.gov.br",portalPage,"Olá, cidadão!"),"Saudação genérica não confirma conexão");
        Check(!NativePortalSessions.DetranConnection("www.detran.sp.gov.br",portalPage with{PageReady=false},"Olá, Ricardo!"),"Página incompleta não confirma conexão pela saudação");
        Console.WriteLine(passed+" verificações de sessões profissionais aprovadas.");
    }
}

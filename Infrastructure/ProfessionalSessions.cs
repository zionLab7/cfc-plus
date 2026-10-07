using Microsoft.Playwright;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace CfcPilot;

public sealed partial class PortalSessions
{
    sealed class ProfessionalContext(BrowserProfile profile,IBrowserContext context,string runtimeKey,PortalBrowserOptions options)
    {
        public readonly BrowserProfile Profile=profile;
        public readonly IBrowserContext Context=context;
        public readonly string RuntimeKey=runtimeKey;
        public readonly string InstanceId=Guid.NewGuid().ToString("N");
        public readonly PortalBrowserOptions Options=options;
        public string CookiesHash="";
        public string CookieRestoreError="";
        public readonly SemaphoreSlim Gate=new(1);
        public readonly ConcurrentDictionary<string,IPage> Pages=new();
        public readonly Dictionary<string,PortalControl> Controls=[];
        public readonly Dictionary<string,ProfessionalObservation> Observations=[];
        public readonly HashSet<string> Confirmed=[];
        public readonly ConcurrentQueue<object> Diagnostics=new();
        public string LastNetwork="";
        public string LastPortalError="";
    }
    record ProfessionalObservation(string Stage,string Message,string Error,bool ConnectedIndicator=false,bool IdentityVisible=false,bool Authenticated=false,string CheckedAt="");
    readonly ConcurrentDictionary<string,ProfessionalContext> professionals=new();

    async Task<IPage> OpenProfessionalPage(BrowserProfile profile,string portal,string url)
    {
        if(profile.Kind is not("Diretor" or "Instrutor")||!Guid.TryParseExact(profile.Id,"N",out _))throw new RuleException("Selecione um perfil profissional válido.");
        ProfessionalBrowser.Portal(portal);Allowed(url);await launchGate.WaitAsync();
        try
        {
            if(!professionals.TryGetValue(profile.Id,out var session))
            {
                var options=runtime.Options();
                if(professionals.Count>=options.MaxProfessionalProfiles)throw new RuleException("O limite configurado de navegadores profissionais foi atingido. O administrador pode ajustar o limite para a capacidade do servidor.",409);
                engine??=await Playwright.CreateAsync();
                var key=BrowserRuntime.Key(options)+"/"+profile.Id;
                var context=await engine.Chromium.LaunchPersistentContextAsync(runtime.ProfileDirectory(options,profile.Id),runtime.LaunchOptions(options));
                contexts[key]=context;session=new(profile,context,key,options);professionals[profile.Id]=session;
                try{var savedCookies=ProfessionalCookieVault.MissingCookies(cookieVault.Read(options,profile.Id),await context.CookiesAsync());if(savedCookies.Length>0)await context.AddCookiesAsync(savedCookies);}catch(RuleException ex){session.CookieRestoreError=ex.Message;logger.LogWarning("Cookies profissionais {Profile}: {Message}",profile.Id,ex.Message);}
                var observed=session;
                context.Close+=(_,_)=>
                {
                    contexts.TryRemove(key,out _);professionals.TryRemove(profile.Id,out _);
                    foreach(var item in jobPages.Where(x=>x.Value.Context==context).ToArray())jobPages.TryRemove(item.Key,out _);
                };
                context.Page+=async(_,popup)=>
                {
                    try
                    {
                        var opener=await popup.OpenerAsync();if(opener==null)return;
                        await popup.WaitForLoadStateAsync(LoadState.DOMContentLoaded,new(){Timeout=15000});Allowed(popup.Url);
                        await observed.Gate.WaitAsync();
                        try{var parent=observed.Pages.FirstOrDefault(x=>x.Value==opener);if(parent.Key!=null){observed.Pages[parent.Key]=popup;AttachProfessionalDiagnostics(observed,popup);}}
                        finally{observed.Gate.Release();}
                    }
                    catch(Exception ex) when(ex is PlaywrightException or RuleException or ObjectDisposedException){/* Keep the original page when a popup is outside the permitted portals. */}
                };
            }
            await session.Gate.WaitAsync();
            try
            {
                if(session.Pages.TryGetValue(portal,out var existing)&&!existing.IsClosed)return existing;
                var page=session.Context.Pages.FirstOrDefault(x=>x.Url=="about:blank"&&!x.IsClosed)??await session.Context.NewPageAsync();
                session.Pages[portal]=page;session.Controls[portal]=new();session.Confirmed.Remove(portal);AttachProfessionalDiagnostics(session,page);
                try{await page.GotoAsync(url,new(){WaitUntil=WaitUntilState.DOMContentLoaded,Timeout=25000});}
                catch(PlaywrightException) when(page.Url!="about:blank"){/* Display the actual portal/network failure without erasing the profile. */}
                registry.Set(profile.Id,portal,true);return page;
            }
            finally{session.Gate.Release();}
        }
        catch(PlaywrightException){throw new RuleException("Não foi possível abrir o navegador profissional. Confira Chrome/Chromium, o modo de execução e se esse perfil já está aberto por outro servidor. O perfil foi preservado.",503);}
        finally{launchGate.Release();}
    }
    void AttachProfessionalDiagnostics(ProfessionalContext session,IPage page)
    {
        void Log(string message)
        {
            if(session.LastNetwork==message)return;session.LastNetwork=message;
            session.Diagnostics.Enqueue(new{time=DateTimeOffset.UtcNow.ToString("O"),stage="network",message});while(session.Diagnostics.Count>20)session.Diagnostics.TryDequeue(out _);
            logger.LogInformation("Professional browser {Profile}: {Message}",session.Profile.Id,message);
        }
        page.RequestFailed+=(_,request)=>
        {
            if(!Uri.TryCreate(request.Url,UriKind.Absolute,out var uri)||!DiagnosticHost(uri.Host))return;
            var code=Regex.Match(request.Failure??"",@"net::ERR_[A-Z_]+").Value;if(code=="net::ERR_ABORTED")return;
            Log("Falha de rede em "+uri.Host+(code==""?"":" · "+code));
        };
        page.Response+=(_,response)=>{if(response.Status>=400&&Uri.TryCreate(response.Url,UriKind.Absolute,out var uri)&&DiagnosticHost(uri.Host))Log("HTTP "+response.Status+" em "+uri.Host);};
    }
    ProfessionalContext Professional(string profileId,string portal)
    {
        ProfessionalBrowser.Portal(portal);
        if(!professionals.TryGetValue(profileId,out var session)||!session.Pages.TryGetValue(portal,out var page)||page.IsClosed)throw new RuleException("Abra esse navegador pela central de sessões. Os dados do perfil continuam salvos no servidor.",409);
        return session;
    }
    static PortalControl ControlFor(ProfessionalContext session,string portal)
    {
        if(!session.Pages.TryGetValue(portal,out var page)||page.IsClosed||!session.Controls.TryGetValue(portal,out var control))throw new RuleException("A página foi encerrada. Reabra pela central de sessões; o perfil foi preservado.",409);
        return control;
    }
    static string FoldName(string value)=>Regex.Replace(string.Concat(value.Normalize(NormalizationForm.FormD).Where(c=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark)).ToUpperInvariant(),@"\s+"," ").Trim();
    async Task<ProfessionalObservation> ObserveProfessional(ProfessionalContext session,string portal)
    {
        await CheckpointProfessional(session);
        var page=session.Pages[portal];var now=DateTimeOffset.UtcNow.ToString("O");
        if(page.IsClosed)return new("closed","O navegador foi encerrado. O perfil em disco foi preservado.","",CheckedAt:now);
        try
        {
            Allowed(page.Url);var login=false;var logout=false;var identity=false;
            if(Gov(page.Url))
            {
                var error=await GovPageError(page);
                if(error!="")
                {
                    session.Confirmed.Remove(portal);
                    if(error!=session.LastPortalError)
                    {
                        session.Diagnostics.Enqueue(new{time=now,stage="portal-error",message=error});while(session.Diagnostics.Count>20)session.Diagnostics.TryDequeue(out _);
                        // Only error codes reach the log, never arbitrary page content.
                        logger.LogInformation("Professional browser {Profile}: GOV.BR recusou a etapa {Code}",session.Profile.Id,Regex.Match(error,@"ERL\d{3,}").Value);
                    }
                    session.LastPortalError=error;
                    return new("portal-error","O GOV.BR recusou a verificação. A mensagem abaixo vem da página, sem repetir o envio automaticamente.",error,CheckedAt:now);
                }
                session.LastPortalError="";
                if(await HumanChallengeVisible(page))return new("challenge","Conclua a verificação humana na página. Movimentos, cliques e arrastos são transmitidos ao navegador do servidor.","",CheckedAt:now);
            }
            foreach(var frame in page.Frames.Take(8))
            {
                try{Allowed(frame.Url);}catch(RuleException){continue;}
                var access=frame.Locator("input[type=password]:visible, #accountId:visible");if(await access.CountAsync()>0)login=true;
                var links=frame.GetByRole(AriaRole.Link,new(){NameRegex=new Regex("^(Sair|Logout|Desconectar|Encerrar sessão)(?:\\b|$)",RegexOptions.IgnoreCase)}).Or(frame.GetByRole(AriaRole.Button,new(){NameRegex=new Regex("^(Sair|Logout|Desconectar|Encerrar sessão)(?:\\b|$)",RegexOptions.IgnoreCase)}));
                for(var i=0;i<Math.Min(await links.CountAsync(),5);i++)if(await links.Nth(i).IsVisibleAsync())logout=true;
                if(session.Profile.Name.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length>=2)
                {
                    var text=await frame.Locator("body").InnerTextAsync(new(){Timeout=1500});identity|=FoldName(text[..Math.Min(text.Length,50000)]).Contains(FoldName(session.Profile.Name),StringComparison.Ordinal);
                }
            }
            if(login){session.Confirmed.Remove(portal);return new("login-required","O portal está solicitando acesso. Faça o login deste profissional na página; certificado ou leitor devem estar disponíveis no servidor.","",CheckedAt:now);}
            if(logout)
            {
                var confirmed=session.Confirmed.Contains(portal);
                return new(confirmed?"connected":"check-identity",confirmed?"Identidade conferida pelo atendimento; a página indica uma sessão conectada.":"A página indica sessão conectada. Confira o profissional e confirme a identidade antes de utilizá-la.","",true,identity,confirmed,now);
            }
            session.Confirmed.Remove(portal);
            return new("unconfirmed","Navegador aberto no servidor. O estado do login ainda não foi confirmado nesta página.","",CheckedAt:now);
        }
        catch(PlaywrightException){return new("loading","A página está carregando. O navegador permanece aberto.","",CheckedAt:now);}
        catch(RuleException ex){session.Confirmed.Remove(portal);return new("outside-portal","Confira a página aberta no servidor.",ex.Message,CheckedAt:now);}
    }
    object ProfessionalStatusObject(ProfessionalContext session,string portal)
    {
        var page=session.Pages[portal];var state=session.Observations.GetValueOrDefault(portal)??new("open","Navegador profissional aberto no servidor.","");
        var host=Uri.TryCreate(page.Url,UriKind.Absolute,out var uri)?uri.Host:"";
        return new{profileId=session.Profile.Id,portal,instanceId=session.InstanceId,profileName=session.Profile.Name,kind=session.Profile.Kind,stage=state.Stage,message=state.Message,error=state.Error==""?session.CookieRestoreError:state.Error,active=false,authenticated=state.Authenticated,connectedIndicator=state.ConnectedIndicator,identityVisible=state.IdentityVisible,checkedAt=state.CheckedAt,host,network=session.LastNetwork,diagnostics=session.Diagnostics.ToArray(),width=1280,height=800,persistent=true,browserOpen=!page.IsClosed,restore=registry.Read().Any(x=>x.ProfileId==session.Profile.Id&&x.Portal==portal&&x.Restore)};
    }
    public async Task<object> OpenProfessional(BrowserProfile profile,string portal,string userId)
    {
        if(native.Enabled)return await native.OpenProfessional(profile,portal,userId);
        await OpenProfessionalPage(profile,portal,ProfessionalBrowser.Portal(portal));var session=Professional(profile.Id,portal);await session.Gate.WaitAsync();
        try{var lease=ControlFor(session,portal).Acquire(userId);session.Observations[portal]=await ObserveProfessional(session,portal);return new{lease,session=ProfessionalStatusObject(session,portal)};}
        finally{session.Gate.Release();}
    }
    public async Task RestoreProfessional(BrowserProfile profile,string portal)
    {
        if(native.Enabled){await native.Restore(profile,portal);return;}
        await OpenProfessionalPage(profile,portal,ProfessionalBrowser.Portal(portal));var session=Professional(profile.Id,portal);await session.Gate.WaitAsync();
        try{session.Observations[portal]=await ObserveProfessional(session,portal);}finally{session.Gate.Release();}
    }
    public async Task<object[]> ProfessionalOverview()
    {
        if(native.Enabled)return await native.Overview();
        var result=new List<object>();foreach(var x in professionals.Values){await x.Gate.WaitAsync();try{foreach(var p in x.Pages)result.Add(new{profileId=x.Profile.Id,portal=p.Key,browserOpen=!p.Value.IsClosed,stage=x.Observations.GetValueOrDefault(p.Key)?.Stage??"open",authenticated=x.Observations.GetValueOrDefault(p.Key)?.Authenticated??false,checkedAt=x.Observations.GetValueOrDefault(p.Key)?.CheckedAt??"",controlled=x.Controls.GetValueOrDefault(p.Key)?.Held??false});}finally{x.Gate.Release();}}return result.ToArray();
    }
    public async Task<object> ProfessionalStatus(string profileId,string portal,string userId,string lease)
    {
        if(native.Enabled)return await native.Status(profileId,portal,userId,lease);
        var session=Professional(profileId,portal);await session.Gate.WaitAsync();
        try{ControlFor(session,portal).Check(userId,lease);session.Observations[portal]=await ObserveProfessional(session,portal);return ProfessionalStatusObject(session,portal);}finally{session.Gate.Release();}
    }
    public async Task<byte[]> ProfessionalFrame(string profileId,string portal,string userId,string lease)
    {
        if(native.Enabled)return await native.Frame(profileId,portal,userId,lease);
        var session=Professional(profileId,portal);await session.Gate.WaitAsync();
        try{ControlFor(session,portal).Check(userId,lease);Allowed(session.Pages[portal].Url);return await session.Pages[portal].ScreenshotAsync(new(){Type=ScreenshotType.Jpeg,Quality=90,FullPage=false,Timeout=5000});}
        catch(Exception ex) when(ex is PlaywrightException or TimeoutException){throw new RuleException("A página está mudando. A imagem será atualizada na próxima tentativa.",409);}finally{session.Gate.Release();}
    }
    public async Task<object> ProfessionalInput(string profileId,string portal,string userId,string lease,PortalInput input)
    {
        if(native.Enabled)return await native.Input(profileId,portal,userId,lease,input);
        input.Validate();var session=Professional(profileId,portal);await session.Gate.WaitAsync();
        try
        {
            ControlFor(session,portal).Check(userId,lease);var page=session.Pages[portal];Allowed(page.Url);
            await input.Dispatch(page);
            if(!input.PointerOnly)session.Observations[portal]=await ObserveProfessional(session,portal);return ProfessionalStatusObject(session,portal);
        }
        catch(PlaywrightException){throw new RuleException("A página mudou durante o comando. Confira a imagem antes de repetir.",409);}finally{session.Gate.Release();}
    }
    public async Task<object> ConfirmProfessional(string profileId,string portal,string userId,string lease,bool confirmed,string name)
    {
        if(native.Enabled)return await native.Confirm(profileId,portal,userId,lease,confirmed,name);
        var session=Professional(profileId,portal);await session.Gate.WaitAsync();
        try
        {
            ControlFor(session,portal).Check(userId,lease);var observed=await ObserveProfessional(session,portal);
            if(!confirmed||FoldName(name)!=FoldName(session.Profile.Name))throw new RuleException("Confira a identidade do profissional e informe o nome do perfil.");
            if(!observed.ConnectedIndicator)throw new RuleException("A página ainda não indica uma sessão conectada. Termine o login antes de confirmar a identidade.",409);
            session.Confirmed.Add(portal);session.Observations[portal]=observed with{Stage="connected",Message="Identidade conferida pelo atendimento; a página indica uma sessão conectada.",Authenticated=true};
            logger.LogInformation("Identidade profissional conferida no app: {Profile}/{Portal}, usuário {User}",profileId,portal,userId);
            return ProfessionalStatusObject(session,portal);
        }
        finally{session.Gate.Release();}
    }
    public async Task<object> ReleaseProfessional(string profileId,string portal,string userId,string lease)
    {
        if(native.Enabled)return await native.Release(profileId,portal,userId,lease);
        var session=Professional(profileId,portal);await session.Gate.WaitAsync();try{var control=ControlFor(session,portal);control.Check(userId,lease);await session.Pages[portal].Mouse.UpAsync();control.Release(userId,lease);return new{released=true,browserOpen=true,persistent=true};}finally{session.Gate.Release();}
    }
    public async Task<object> CloseProfessional(string profileId,string portal,string userId,string lease)
    {
        if(native.Enabled)return await native.Close(profileId,portal,userId,lease);
        var session=Professional(profileId,portal);await session.Gate.WaitAsync();
        try
        {
            ControlFor(session,portal).Check(userId,lease);registry.Set(profileId,portal,false);session.Confirmed.Remove(portal);
            await CheckpointProfessional(session);await session.Pages[portal].CloseAsync();session.Pages.TryRemove(portal,out _);session.Controls.Remove(portal);session.Observations.Remove(portal);
            if(session.Pages.Count==0)await session.Context.CloseAsync();return new{closed=true,persistent=true,restore=false};
        }
        catch(PlaywrightException){throw new RuleException("Não foi possível encerrar a página. Confira o navegador do servidor.",409);}finally{session.Gate.Release();}
    }
    async Task CheckpointProfessional(ProfessionalContext session)
    {
        if(session.CookieRestoreError!="")return;
        try
        {
            var cookies=(await session.Context.CookiesAsync()).Select(x=>new Cookie{Name=x.Name,Value=x.Value,Domain=x.Domain,Path=x.Path,Expires=x.Expires>0?x.Expires:null,HttpOnly=x.HttpOnly,Secure=x.Secure,SameSite=x.SameSite}).Where(ProfessionalCookieVault.AllowedCookie).OrderBy(x=>x.Domain).ThenBy(x=>x.Path).ThenBy(x=>x.Name).ToArray();
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(cookies,Store.Json))));
            if(session.CookiesHash==hash)return;cookieVault.Save(session.Options,session.Profile.Id,cookies);session.CookiesHash=hash;
        }
        catch(Exception ex) when(ex is PlaywrightException or IOException or System.Security.Cryptography.CryptographicException){logger.LogWarning("Checkpoint profissional {Profile} indisponível: {Type}",session.Profile.Id,ex.GetType().Name);}
    }
}

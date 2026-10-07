using Microsoft.Playwright;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace CfcPilot;

public record PortalInput(string Kind,double X=0,double Y=0,string Text="",string Key="",double DeltaY=0)
{
    public void Validate()
    {
        if(Kind is not("click" or "move" or "down" or "up" or "text" or "key" or "scroll" or "back" or "reload" or "entry"))throw new RuleException("Comando de navegação inválido.");
        if(!double.IsFinite(X)||!double.IsFinite(Y)||X<0||X>=1280||Y<0||Y>=800)throw new RuleException("Posição fora da página.");
        if(Kind=="text"&&(string.IsNullOrEmpty(Text)||Text.Length>2000))throw new RuleException("Digite até 2.000 caracteres por envio.");
        if(Kind=="key"&&!new[]{"Tab","Shift+Tab","Enter","Space","Backspace","Delete","Escape","ArrowLeft","ArrowRight","ArrowUp","ArrowDown","Home","End","Control+A","Meta+A"}.Contains(Key))throw new RuleException("Tecla não permitida.");
        if(Kind=="scroll"&&(!double.IsFinite(DeltaY)||Math.Abs(DeltaY)>1600))throw new RuleException("Rolagem fora do limite.");
    }
    public bool PointerOnly=>Kind is "move" or "down" or "up";
    public async Task Dispatch(IPage page)
    {
        switch(Kind)
        {
            case "click":await page.Mouse.ClickAsync((float)X,(float)Y);break;
            case "move":await page.Mouse.MoveAsync((float)X,(float)Y);break;
            case "down":await page.Mouse.MoveAsync((float)X,(float)Y);await page.Mouse.DownAsync();break;
            case "up":await page.Mouse.MoveAsync((float)X,(float)Y);await page.Mouse.UpAsync();break;
            case "text":await page.Keyboard.TypeAsync(Text);break;
            case "key":await page.Keyboard.PressAsync(Key);break;
            case "scroll":await page.Mouse.MoveAsync((float)X,(float)Y);await page.Mouse.WheelAsync(0,(float)DeltaY);break;
            case "back":await page.GoBackAsync(new(){WaitUntil=WaitUntilState.DOMContentLoaded,Timeout=10000});break;
            case "reload":await page.ReloadAsync(new(){WaitUntil=WaitUntilState.DOMContentLoaded,Timeout=10000});break;
            case "entry":await page.GetByRole(AriaRole.Link,new(){Name="Entrar com gov.br",Exact=true}).Or(page.GetByRole(AriaRole.Button,new(){Name="Entrar com gov.br",Exact=true})).ClickAsync(new(){Timeout=5000});break;
        }
    }
}

// Persistent server profiles. The viewer displays the actual page, without
// proxying GOV.BR HTML or recreating authentication/challenge requests.
public sealed partial class PortalSessions(BrowserRuntime runtime,ILogger<PortalSessions> logger,ProfessionalSessionRegistry registry,ProfessionalCookieVault cookieVault,NativePortalSessions native) : IAsyncDisposable
{
    sealed class StudentSession(string profileId,string studentId,string controller,IPage page)
    {
        public readonly string ProfileId=profileId,StudentId=studentId,Controller=controller;
        public IPage Page=page;
        public IPage? LoginPage;
        public readonly SemaphoreSlim Gate=new(1);
        public StudentGovLogin? Login;
        public GovLoginStatus? ObservedState;
        public CancellationTokenSource? Cancellation;
        public Task? Worker;
        public string JobId="",Network="",Logged="";
        public readonly ConcurrentQueue<object> Diagnostics=new();
    }
    readonly ConcurrentDictionary<string,IBrowserContext> contexts=new();
    readonly ConcurrentDictionary<string,IPage> jobPages=new();
    readonly ConcurrentDictionary<string,StudentSession> studentSessions=new(),studentJobs=new();
    readonly SemaphoreSlim launchGate=new(1);
    IPlaywright? engine;
    static readonly string[] Hosts=["www.e-cnhsp.sp.gov.br","www.detran.sp.gov.br","detran.sp.gov.br","sso.acesso.gov.br","login.sp.gov.br","idp.sp.gov.br","operacoes.sp.gov.br","pixveiculos.fazenda.sp.gov.br","www.poupatempo.sp.gov.br","poupatempo.sp.gov.br","detransp.custhelp.com","www.spsempapel.sp.gov.br","www.documentos.spsempapel.sp.gov.br","portalservicos.senatran.serpro.gov.br","rastreamento.correios.com.br"];
    internal static bool CookieDomainAllowed(string domain)=>Hosts.Any(host=>host.Equals(domain,StringComparison.OrdinalIgnoreCase)||host.EndsWith("."+domain,StringComparison.OrdinalIgnoreCase));
    public static Uri Allowed(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!Hosts.Contains(uri.Host,StringComparer.OrdinalIgnoreCase)||!uri.IsDefaultPort||uri.UserInfo!="")throw new RuleException("Endereço fora dos portais permitidos.");
        return uri;
    }
    static bool Gov(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="sso.acesso.gov.br"&&uri.IsDefaultPort&&uri.UserInfo=="";
    public async Task<IPage?> Open(BrowserProfile profile,string url,IPAddress? remote,string jobId="",string controller="")
    {
        if(native.Enabled){await native.Open(profile,profile.Kind=="Aluno"?"student":Allowed(url).Host.Contains("e-cnhsp")?"ecnh":"detran",url,jobId,controller);return null;}
        var student=profile.Kind=="Aluno";
        if(!student)
        {
            var portal=Allowed(url).Host.Contains("e-cnhsp",StringComparison.OrdinalIgnoreCase)?"ecnh":"detran";
            var professional=await OpenProfessionalPage(profile,portal,url);
            if(jobId!="")jobPages[jobId]=professional;
            return professional;
        }
        if(student&&(controller==""||profile.StudentId==""||jobId==""))throw new RuleException("Abra pela ficha de um aluno com um usuário de atendimento autenticado.",403);
        if(!Guid.TryParseExact(profile.Id,"N",out _))throw new RuleException("Perfil inválido.");
        Allowed(url);await launchGate.WaitAsync();
        try
        {
            var options=runtime.Options();var contextKey=BrowserRuntime.Key(options)+"/"+profile.Id;
            if(studentSessions.TryGetValue(contextKey,out var session)&&!session.Page.IsClosed)
            {
                if(session.Controller!=controller)throw new RuleException("A sessão deste aluno está em uso por outro atendente. Feche a janela do servidor antes de transferir o controle.",409);
                studentJobs[jobId]=session;jobPages[jobId]=session.Page;
                await session.Page.BringToFrontAsync();return session.Page;
            }
            session?.Cancellation?.Cancel();
            if(!contexts.TryGetValue(contextKey,out var context))
            {
                engine??=await Playwright.CreateAsync();
                var directory=runtime.ProfileDirectory(options,profile.Id);Directory.CreateDirectory(directory);
                context=await engine.Chromium.LaunchPersistentContextAsync(directory,runtime.LaunchOptions(options));
                contexts[contextKey]=context;context.Close+=(_,_)=>
                {
                    contexts.TryRemove(contextKey,out _);
                    if(studentSessions.TryRemove(contextKey,out var closed))
                    {
                        closed.Cancellation?.Cancel();
                        foreach(var item in studentJobs.Where(x=>x.Value==closed).ToArray())studentJobs.TryRemove(item.Key,out _);
                    }
                    foreach(var item in jobPages.Where(x=>x.Value.Context==context).ToArray())jobPages.TryRemove(item.Key,out _);
                };
            }
            if(!student&&jobId!=""&&jobPages.TryGetValue(jobId,out var existing)&&!existing.IsClosed&&existing.Context==context){await existing.BringToFrontAsync();return existing;}
            var page=student?context.Pages.FirstOrDefault(p=>!p.IsClosed&&p.Url=="about:blank")??await context.NewPageAsync():await context.NewPageAsync();
            if(student)
            {
                session=new(profile.Id,profile.StudentId,controller,page){JobId=jobId};studentSessions[contextKey]=session;studentJobs[jobId]=session;
                AttachDiagnostics(session,page);
                context.Page+=(_,popup)=>
                {
                    AttachDiagnostics(session,popup);
                    popup.DOMContentLoaded+=(_,_)=>{try{Allowed(popup.Url);session.Page=popup;}catch(RuleException){}};
                };
            }
            if(jobId!="")jobPages[jobId]=page;
            try{await page.GotoAsync(url,new(){WaitUntil=WaitUntilState.DOMContentLoaded,Timeout=25000});}
            catch(PlaywrightException) when(student&&page.Url!="about:blank"){/* Preserve the page; its network error is reported by the viewer. */}
            await page.BringToFrontAsync();return page;
        }
        catch(PlaywrightException){throw new RuleException("Não foi possível abrir o navegador do servidor. Confira Chrome/Chromium, a conexão e se o perfil já está aberto por outra instância do app. O perfil existente foi preservado.",503);}
        finally{launchGate.Release();}
    }
    static bool DiagnosticHost(string host)=>Hosts.Contains(host,StringComparer.OrdinalIgnoreCase)||host.EndsWith(".hcaptcha.com",StringComparison.OrdinalIgnoreCase)||host=="hcaptcha.com"||host=="www.google.com"||host=="www.gstatic.com"||host=="www.recaptcha.net";
    void Diagnostic(StudentSession session,string stage,string message)
    {
        message=StudentGovLogin.SafeError(message);session.Diagnostics.Enqueue(new{time=DateTimeOffset.UtcNow.ToString("O"),stage,message});
        while(session.Diagnostics.Count>20)session.Diagnostics.TryDequeue(out _);
        logger.LogInformation("GOV browser {Profile} job {Job} {Stage}: {Message}",session.ProfileId,session.JobId,stage,message);
    }
    void AttachDiagnostics(StudentSession session,IPage page)
    {
        page.RequestFailed+=(_,request)=>
        {
            if(!Uri.TryCreate(request.Url,UriKind.Absolute,out var uri)||!DiagnosticHost(uri.Host))return;
            var code=Regex.Match(request.Failure??"",@"net::ERR_[A-Z_]+").Value;
            if(code=="net::ERR_ABORTED")return; // A normal navigation can cancel a challenge asset.
            var message="Falha de rede em "+uri.Host+(code==""?"":" · "+code);if(session.Network==message)return;
            session.Network=message;Diagnostic(session,"network",message);
        };
        page.Response+=(_,response)=>
        {
            if(response.Status<400||!Uri.TryCreate(response.Url,UriKind.Absolute,out var uri)||!DiagnosticHost(uri.Host))return;
            var message="HTTP "+response.Status+" em "+uri.Host;if(session.Network==message)return;
            session.Network=message;Diagnostic(session,"http",message);
        };
    }
    StudentSession Session(string jobId,string sid,string userId)
    {
        if(!studentJobs.TryGetValue(jobId,out var session)||session.StudentId!=sid)throw new RuleException("Abra a sessão deste aluno pela ficha. Após reiniciar o servidor, reabra o navegador para recuperar o perfil salvo.",409);
        if(session.Controller!=userId)throw new RuleException("Esta sessão é controlada por outro atendente.",403);
        if(session.Page.IsClosed)throw new RuleException("A janela foi fechada. Abra novamente pela ficha.",409);
        return session;
    }
    public async Task<object> StudentLogin(string jobId,string sid,string cpf,string password,string userId,bool restart=false)
    {
        if(native.Enabled)return await native.Login(jobId,sid,userId,cpf,password,restart);
        var session=Session(jobId,sid,userId);await session.Gate.WaitAsync();
        try
        {
            if(session.Login!=null&&!restart)return Status(session);
            session.Cancellation?.Cancel();session.Login?.Stop();
            if(restart){Allowed(session.Page.Url);await session.Page.GotoAsync(ProfessionalBrowser.StudentPortal(),new(){WaitUntil=WaitUntilState.DOMContentLoaded,Timeout=25000});}
            session.JobId=jobId;session.Network="";session.Logged="";
            session.ObservedState=null;
            session.LoginPage=session.Page;
            var login=new StudentGovLogin(StudentGovLogin.Digits(cpf),password);session.Login=login;session.Cancellation=new();
            session.Worker=Monitor(session,login,session.Cancellation.Token);
            return Status(session);
        }
        catch(PlaywrightException){throw new RuleException("O portal não respondeu à abertura de um novo acesso. Confira a página antes de tentar novamente.",409);}
        finally{session.Gate.Release();}
    }
    async Task<GovLoginObservation> Observe(IPage page,string cpf)
    {
        if(page.IsClosed)return new(Closed:true);
        Allowed(page.Url);
        if(!Gov(page.Url))
        {
            var entry=page.GetByRole(AriaRole.Link,new(){Name="Entrar com gov.br",Exact=true});
            var text=await page.Locator("body").InnerTextAsync(new(){Timeout=3000});
            var entryVisible=await entry.CountAsync()==1&&await entry.IsVisibleAsync();
            var logout=page.GetByRole(AriaRole.Link,new(){NameRegex=new Regex("^Sair(?: da conta)?$|^Encerrar sessão$",RegexOptions.IgnoreCase)}).Or(page.GetByRole(AriaRole.Button,new(){NameRegex=new Regex("^Sair(?: da conta)?$|^Encerrar sessão$",RegexOptions.IgnoreCase)}));
            var signedIn=false;for(var i=0;i<Math.Min(await logout.CountAsync(),5);i++)if(await logout.Nth(i).IsVisibleAsync()){signedIn=true;break;}
            return new(Entry:entryVisible,IdentityMatched:Allowed(page.Url).Host.Contains("detran")&&!entryVisible&&signedIn&&PortalExtraction.ContainsCpf(text,cpf));
        }
        var account=page.Locator("#accountId");var secret=page.Locator("#password");
        var error=await GovPageError(page);
        var challenge=await HumanChallengeVisible(page);
        var ready=await page.EvaluateAsync<bool>("document.readyState === 'complete'");
        var next=page.Locator("#enter-account-id");var enabled=await next.CountAsync()==1&&await next.IsVisibleAsync()&&await next.IsEnabledAsync();
        return new(OnGov:true,Account:await account.IsVisibleAsync(),AccountCpf:await account.CountAsync()==1?await account.InputValueAsync(new(){Timeout=1000}):"",Password:await secret.IsVisibleAsync(),Error:error,Challenge:challenge,PageReady:ready,ContinueEnabled:enabled);
    }
    internal static async Task<string> GovPageError(IPage page)
    {
        var error="";var notices=page.Locator("#login-error, #error-message, #mensagemErro, .feedback, [class*='error' i], [class*='erro' i], .alert-danger, [role=alert]");
        for(var i=0;i<Math.Min(await notices.CountAsync(),12);i++)
        {
            var notice=notices.Nth(i);if(!await notice.IsVisibleAsync())continue;
            var text=await notice.InnerTextAsync(new(){Timeout=1000});
            if(Regex.IsMatch(text,@"inv[aá]lid|incorret|bloquead|erro|falh|ERL\d{3,}",RegexOptions.IgnoreCase)){error=text;break;}
        }
        if(error=="")
        {
            var text=await page.Locator("body").InnerTextAsync(new(){Timeout=1500});text=text[..Math.Min(text.Length,20000)];
            var match=Regex.Match(text,@"^[^\r\n]*(?:captcha[^\r\n]*inv[aá]lid|inv[aá]lid[^\r\n]*captcha|ERL\d{5,}|senha[^\r\n]*(?:incorret|inv[aá]lid)|conta[^\r\n]*bloquead)[^\r\n]*$",RegexOptions.IgnoreCase|RegexOptions.Multiline);
            if(match.Success)error=match.Value;
        }
        return StudentGovLogin.SafeError(error);
    }
    internal static async Task<bool> HumanChallengeVisible(IPage page)
    {
        // A small checkbox iframe remains visible after success. Only the open
        // challenge panel pauses assisted login; no challenge response is read.
        var frames=page.Locator("iframe[src*='hcaptcha.com'], iframe[src*='recaptcha']");
        for(var i=0;i<Math.Min(await frames.CountAsync(),8);i++)
        {
            var frame=frames.Nth(i);if(!await frame.IsVisibleAsync())continue;
            var box=await frame.BoundingBoxAsync();if(box is {Width:>100,Height:>100})return true;
        }
        return false;
    }
    async Task Monitor(StudentSession session,StudentGovLogin login,CancellationToken cancellation)
    {
        var deadline=DateTimeOffset.UtcNow.AddMinutes(runtime.LoginWaitMinutes());
        try
        {
            while(!cancellation.IsCancellationRequested&&login.Status.Active&&DateTimeOffset.UtcNow<deadline)
            {
                await session.Gate.WaitAsync(cancellation);
                try
                {
                    var page=session.Page;var command=login.Observe(page!=session.LoginPage?new(ChangedPage:true):await Observe(page,login.Cpf));
                    if(command!=GovLoginCommand.None)
                    {
                        var password=login.Password;login.Attempted(command);
                        if(command==GovLoginCommand.EnterGov)await page.GetByRole(AriaRole.Link,new(){Name="Entrar com gov.br",Exact=true}).ClickAsync(new(){Timeout=5000});
                        else
                        {
                            if(!Gov(page.Url))throw new RuleException("O acesso saiu do GOV.BR. O preenchimento foi interrompido.",409);
                            if(command==GovLoginCommand.FillCpf)
                            {
                                await page.Locator("#accountId").FillAsync(login.Cpf,new(){Timeout=3000});
                            }
                            else if(command==GovLoginCommand.SubmitCpf)
                            {
                                if(!Gov(page.Url))throw new RuleException("O endereço mudou antes de enviar o CPF.",409);
                                await page.Locator("#enter-account-id").ClickAsync(new(){Timeout=3000});
                            }
                            else
                            {
                                try
                                {
                                    await page.Locator("#password").FillAsync(password,new(){Timeout=3000});
                                    if(!Gov(page.Url))throw new RuleException("O endereço mudou antes de enviar a senha.",409);
                                    await page.Locator("#submit-button").ClickAsync(new(){Timeout=3000});
                                }
                                finally{login.PasswordDispatched();}
                            }
                        }
                    }
                    LogStatus(session,login);
                }
                catch(PlaywrightException){login.Failure("A página mudou ou ainda está carregando. Acompanhando a mesma sessão, sem repetir os dados de acesso.");LogStatus(session,login);}
                catch(RuleException ex){login.Stop("blocked",ex.Message);LogStatus(session,login);}
                finally{session.Gate.Release();}
                await Task.Delay(750,cancellation);
            }
            if(!cancellation.IsCancellationRequested&&login.Status.Active){login.Stop("timeout","O prazo de preenchimento terminou. A sessão continua salva. Use Novo acesso se quiser iniciar outra tentativa.");LogStatus(session,login);}
        }
        catch(OperationCanceledException){login.Stop();}
        catch(Exception ex) when(ex is ObjectDisposedException or InvalidOperationException){login.Stop("closed","O navegador foi encerrado. Reabra pela ficha para recuperar o perfil.");}
    }
    void LogStatus(StudentSession session,StudentGovLogin login)
    {
        var marker=login.Status.Stage+"|"+login.Status.Error;if(marker==session.Logged)return;session.Logged=marker;
        Diagnostic(session,login.Status.Stage,login.Status.Error==""?login.Status.Message:login.Status.Error);
    }
    static object Status(StudentSession session)
    {
        var state=session.ObservedState??session.Login?.Status??new GovLoginStatus("open","Navegador do aluno aberto.","",false);
        var host=Uri.TryCreate(session.Page.Url,UriKind.Absolute,out var uri)?uri.Host:"";
        return new{stage=state.Stage,message=state.Message,error=state.Error,active=state.Active,authenticated=state.Authenticated,host,network=session.Network,diagnostics=session.Diagnostics.ToArray(),width=1280,height=800,persistent=true};
    }
    public async Task<object> StudentStatus(string jobId,string sid,string userId,string cpf)
    {
        if(native.Enabled)return await native.Status(jobId,"student",userId,"",true,sid,cpf);
        var session=Session(jobId,sid,userId);await session.Gate.WaitAsync();
        try
        {
            // Keep diagnostics working when autofill is paused or disabled. This
            // reads only the visible page, without starting another login attempt.
            if(session.Login?.Status.Active!=true)
            {
                var observed=await Observe(session.Page,StudentGovLogin.Digits(cpf));
                session.ObservedState=StudentGovLogin.ManualStatus(session.ObservedState??session.Login?.Status,observed,cpf)??session.ObservedState;
                if(session.ObservedState is {Error.Length:>0} state)
                {
                    var marker="observed|"+state.Error;if(marker!=session.Logged){session.Logged=marker;Diagnostic(session,state.Stage,state.Error);}
                }
            }
            else session.ObservedState=null;
            return Status(session);
        }
        catch(PlaywrightException){return Status(session);}
        finally{session.Gate.Release();}
    }
    public async Task<byte[]> StudentFrame(string jobId,string sid,string userId)
    {
        if(native.Enabled)return await native.Frame(jobId,"student",userId,"",true,sid);
        var session=Session(jobId,sid,userId);await session.Gate.WaitAsync();try{Allowed(session.Page.Url);return await session.Page.ScreenshotAsync(new(){Type=ScreenshotType.Jpeg,Quality=80,FullPage=false,Timeout=5000});}catch(PlaywrightException){throw new RuleException("A página está mudando. A imagem será atualizada na próxima tentativa.",409);}finally{session.Gate.Release();}
    }
    public async Task<object> StudentInput(string jobId,string sid,string userId,PortalInput input)
    {
        if(native.Enabled)return await native.Input(jobId,"student",userId,"",input,true,sid);
        input.Validate();var session=Session(jobId,sid,userId);await session.Gate.WaitAsync();
        try
        {
            var page=session.Page;Allowed(page.Url);
            if(input.Kind is "click" or "down" or "text" or "key")session.Login?.HumanInput(Gov(page.Url)&&await page.Locator("#password").IsVisibleAsync());
            await input.Dispatch(page);
            return Status(session);
        }
        catch(PlaywrightException){throw new RuleException("A página mudou durante o comando. Confira a imagem antes de repetir.",409);}
        finally{session.Gate.Release();}
    }
    public async Task<object> PauseStudent(string jobId,string sid,string userId)
    {
        if(native.Enabled)return await native.Pause(jobId,sid,userId);
        var session=Session(jobId,sid,userId);await session.Gate.WaitAsync();try{session.Cancellation?.Cancel();session.Login?.Stop();return Status(session);}finally{session.Gate.Release();}
    }
    public async Task<object> StudentQr(string jobId,string sid,string userId)
    {
        if(native.Enabled)return await native.Pause(jobId,sid,userId,true);
        var session=Session(jobId,sid,userId);await session.Gate.WaitAsync();
        try
        {
            var page=session.Page;Allowed(page.Url);
            // Switch methods in the SAME portal authorization and persistent profile.
            // Only the official QR option is opened; the student approves on their phone.
            session.Cancellation?.Cancel();session.Login?.Stop();session.ObservedState=null;
            if(!Gov(page.Url))
            {
                var entry=page.GetByRole(AriaRole.Link,new(){Name="Entrar com gov.br",Exact=true});
                if(await entry.CountAsync()!=1||!await entry.IsVisibleAsync())throw new RuleException("Abra a entrada GOV.BR na página do DETRAN. Uma sessão já conectada deve ser conferida antes de trocar o acesso.",409);
                await entry.ClickAsync(new(){Timeout=5000});
                await page.WaitForURLAsync("https://sso.acesso.gov.br/**",new(){Timeout=10000,WaitUntil=WaitUntilState.DOMContentLoaded});
            }
            if(!Gov(page.Url))throw new RuleException("A opção QR Code está disponível na página oficial do GOV.BR.",409);
            var qr=page.GetByRole(AriaRole.Link,new(){NameRegex=new Regex("^Login com QR code$",RegexOptions.IgnoreCase)}).Or(page.GetByRole(AriaRole.Button,new(){NameRegex=new Regex("^Login com QR code$",RegexOptions.IgnoreCase)}));
            if(await qr.CountAsync()!=1||!await qr.IsVisibleAsync())throw new RuleException("O portal não exibiu Login com QR code nesta etapa. Volte à identificação na própria página e escolha essa opção.",409);
            await qr.ClickAsync(new(){Timeout=5000});
            session.ObservedState=new("qr","Leia o QR Code com o aplicativo GOV.BR do aluno selecionado e conclua a autorização no celular. A sessão será conferida pelo CPF visível no DETRAN.","",false);
            Diagnostic(session,"qr","Opção oficial de acesso com QR Code aberta. Nenhuma credencial foi enviada pelo app.");
            return Status(session);
        }
        catch(PlaywrightException){throw new RuleException("A página mudou ao abrir o QR Code. Confira a tela e selecione Login com QR code na página oficial.",409);}
        finally{session.Gate.Release();}
    }
    public async Task<object> CloseStudent(string jobId,string sid,string userId)
    {
        if(native.Enabled)return await native.Close(jobId,"student",userId,"",true,sid);
        var session=Session(jobId,sid,userId);await session.Gate.WaitAsync();
        try{session.Cancellation?.Cancel();session.Login?.Stop();await session.Page.Context.CloseAsync();return new{closed=true,persistent=true};}
        catch(PlaywrightException){throw new RuleException("Não foi possível encerrar a janela. Confira o navegador do servidor.",409);}
        finally{session.Gate.Release();}
    }
    public async Task<object> Capture(string profileId,string cpf,string jobId)
    {
        if(native.Enabled)throw new RuleException("Neste modo interativo, faça o login e a conferência no portal. A leitura automática de documentos será habilitada após validar o acesso.",409);
        var student=studentJobs.GetValueOrDefault(jobId);if(student!=null)await student.Gate.WaitAsync();
        try
        {
            var page=student?.Page??jobPages.GetValueOrDefault(jobId);
            if(page==null||page.IsClosed||!contexts.Any(x=>x.Key.EndsWith("/"+profileId,StringComparison.Ordinal)&&x.Value==page.Context))throw new RuleException("Abra a janela desta operação e confira a sessão primeiro.",409);
            Allowed(page.Url);if(Gov(page.Url))throw new RuleException("Conclua o login e abra os dados do aluno no DETRAN antes da leitura.",409);
            var frames=new List<object>();var matches=false;
            foreach(var frame in page.Frames)
            {
                if(frame.Url=="about:blank")continue;try{Allowed(frame.Url);}catch(RuleException){continue;}
                var text=await frame.Locator("body").InnerTextAsync(new(){Timeout=8000});if(PortalExtraction.ContainsCpf(text,cpf))matches=true;
                var tables=await frame.Locator("table").EvaluateAllAsync<string[][][]>("tables => tables.slice(0,30).map(t=>Array.from(t.rows).slice(0,500).map(r=>Array.from(r.cells).map(c=>c.innerText.slice(0,2000))))");
                frames.Add(new{url=frame.Url,text=text[..Math.Min(text.Length,100000)],tables});
            }
            if(!matches)throw new RuleException("O CPF do aluno não foi localizado no conteúdo visível. Confira a identidade antes de capturar os dados.",409);
            return new{captured=Operations.Now.ToString("s"),studentMatched=true,frames,automaticSuccess=false};
        }
        catch(PlaywrightException){throw new RuleException("A página mudou ou a sessão expirou. Confira o portal e repita a leitura.",409);}
        finally{student?.Gate.Release();}
    }
    public async ValueTask DisposeAsync()
    {
        foreach(var session in studentSessions.Values)session.Cancellation?.Cancel();
        await Task.WhenAll(studentSessions.Values.Select(x=>x.Worker??Task.CompletedTask));
        foreach(var session in professionals.Values){await session.Gate.WaitAsync();try{await CheckpointProfessional(session);}finally{session.Gate.Release();}}
        foreach(var context in contexts.Values)try{await context.CloseAsync();}catch(PlaywrightException){}
        engine?.Dispose();launchGate.Dispose();
    }
}


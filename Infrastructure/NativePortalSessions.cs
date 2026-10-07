using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace CfcPilot;

// Local broker owns native server windows; app clients retain the existing
// authenticated, per-person API and exclusive-control leases.
public sealed class NativePortalSessions(IConfiguration config,IWebHostEnvironment env,BrowserRuntime runtime,ProfessionalSessionRegistry registry,IDataProtectionProvider protection,ILogger<NativePortalSessions> logger)
{
    sealed record CookieCheckpoint(bool Verified,JsonElement[] Cookies);
    sealed class View(BrowserProfile profile,string portal,string owner)
    {
        public readonly BrowserProfile Profile=profile;public readonly string Portal=portal;public readonly string Owner=owner;
        public readonly SemaphoreSlim Gate=new(1);public readonly PortalControl Control=new();
        public StudentGovLogin? Login;public string Cpf="",Host="",Error="",Instance="",CookieHash="",Logged="";public GovLoginStatus State=new("open","Navegador interativo aberto no servidor.","",false);public bool Connected,Identity,Confirmed;
    }
    readonly ConcurrentDictionary<string,View> views=new(),jobs=new();
    readonly SemaphoreSlim startGate=new(1),openGate=new(1);
    readonly HttpClient http=new(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(20)};
    string Root=>Path.GetFullPath(config["CFC_DATA_DIR"]??Path.Combine(env.ContentRootPath,"App_Data"));
    string ControlFile=>Path.Combine(Root,"native-portal-control.json");
    string token="",address="";
    public bool Enabled=>config["NativePortal:Enabled"]=="true";
    internal static JsonElement[] CookiesToRestore(string saved)
    {
        if(saved.TrimStart().StartsWith('['))return [];
        var checkpoint=JsonSerializer.Deserialize<CookieCheckpoint>(saved,Store.Json);
        return checkpoint?.Verified==true?checkpoint.Cookies:[];
    }
    internal static bool DetranConnection(string host,GovLoginObservation page,string text)
    {
        if(host is not("www.detran.sp.gov.br" or "detran.sp.gov.br")||!page.PageReady||page.OnGov||page.Account||page.Password||page.Entry||page.Challenge||page.Error!="")return false;
        var greeting=System.Text.RegularExpressions.Regex.Match(text,@"(?m)^\s*Olá,\s*([^!\r\n]{2,100})!\s*$");
        return greeting.Success&&!new[]{"cidadão","cidadã","visitante"}.Contains(greeting.Groups[1].Value.Trim(),StringComparer.OrdinalIgnoreCase);
    }
    string CookieFile(string id)=>Path.Combine(Root,"native-cookie-checkpoints",id+".protected");
    IDataProtector Protector(string id)=>protection.CreateProtector("CFC.NativePortalCookies.v1",id);
    async Task<bool> Ping()
    {
        if(address==""||token=="")return false;
        try{using var reply=await Send(new{command="ping"});var data=await reply.Content.ReadFromJsonAsync<JsonElement>();return reply.IsSuccessStatusCode&&data.GetProperty("application").GetString()=="cfc-native-portals"&&data.GetProperty("root").GetString()==Root;}
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException){return false;}
    }
    async Task Start()
    {
        await startGate.WaitAsync();try
        {
            if(await Ping())return;Directory.CreateDirectory(Root);
            if(File.Exists(ControlFile)&&File.Exists(ControlFile+".ready"))
            {
                var settings=JsonDocument.Parse(File.ReadAllText(ControlFile)).RootElement;var ready=JsonDocument.Parse(File.ReadAllText(ControlFile+".ready")).RootElement;
                token=settings.GetProperty("token").GetString()!;address="http://127.0.0.1:"+ready.GetProperty("port").GetInt32()+"/rpc";if(await Ping())return;
            }
            if(!OperatingSystem.IsWindows()&&string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))throw new RuleException("O navegador interativo exige desktop no servidor. Na VPS, provisione um desktop de servidor antes de usar este modo.",503);
            var packaged=Path.GetFullPath(Path.Combine(env.ContentRootPath,"../../CFC+.exe"));
            var development=config["NativePortal:ExecutablePath"]??Path.Combine(Path.GetDirectoryName(Root)!,"desktop/node_modules/electron/dist/electron.exe");
            var executable=config["NativePortal:ExecutablePath"] is {Length:>0}?development:File.Exists(packaged)?packaged:development;if(!File.Exists(executable))throw new RuleException("O runtime do navegador interativo não está instalado neste servidor.",503);
            token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var instance=Guid.NewGuid().ToString("N");
            File.WriteAllText(ControlFile,JsonSerializer.Serialize(new{root=Root,token,instanceId=instance}));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(ControlFile,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=env.ContentRootPath};
            if(config["NativePortal:UseUserNamespaceSandbox"]=="true")start.ArgumentList.Add("--disable-setuid-sandbox");
            if(executable!=packaged)start.ArgumentList.Add(Path.Combine(env.ContentRootPath,"native-host"));else start.ArgumentList.Add("--portal-host");
            start.ArgumentList.Add("--portal-config");start.ArgumentList.Add(ControlFile);Process.Start(start);
            for(var i=0;i<100;i++)
            {
                if(File.Exists(ControlFile+".ready")){var ready=JsonDocument.Parse(File.ReadAllText(ControlFile+".ready")).RootElement;if(ready.GetProperty("instanceId").GetString()==instance){address="http://127.0.0.1:"+ready.GetProperty("port").GetInt32()+"/rpc";if(await Ping())return;}}
                await Task.Delay(100);
            }
            throw new RuleException("O navegador interativo não iniciou. Confira a disponibilidade de desktop e o runtime no servidor.",503);
        }finally{startGate.Release();}
    }
    async Task<HttpResponseMessage> Send(object body)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,address){Content=JsonContent.Create(body,options:Store.Json)};request.Headers.Add("X-CFC-Native",token);return await http.SendAsync(request);
    }
    async Task<JsonElement> Call(View view,string command,object? input=null,string action="",string value="",bool humanGesture=false)
    {
        try{using var response=await Send(new{command,profileId=view.Profile.Id,portal=view.Portal,input,action,value,humanGesture});if(!response.IsSuccessStatusCode)throw new RuleException("A página mudou ou foi encerrada. Confira o navegador interativo do servidor.",409);return await response.Content.ReadFromJsonAsync<JsonElement>();}
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException){throw new RuleException("O navegador interativo não respondeu. Reabra pela central; o perfil permanece salvo.",503);}
    }
    public async Task Open(BrowserProfile profile,string portal,string url,string jobId="",string owner="")
    {
        PortalSessions.Allowed(url);if(!Guid.TryParseExact(profile.Id,"N",out _))throw new RuleException("Perfil inválido.");
        if(profile.Kind=="Aluno"&&(owner==""||profile.StudentId==""||jobId==""))throw new RuleException("Abra pela ficha do aluno com um atendente autenticado.",403);
        await Start();await openGate.WaitAsync();try
        {
            var key=profile.Id+"/"+portal;var view=views.GetValueOrDefault(key);
            if(view!=null&&profile.Kind=="Aluno"&&view.Owner!=owner)throw new RuleException("A sessão do aluno está em uso por outro atendente.",409);
            if(view==null&&!views.Values.Any(v=>v.Profile.Id==profile.Id)&&views.Keys.Select(x=>x.Split('/')[0]).Distinct().Count()>=runtime.Options().MaxProfessionalProfiles)throw new RuleException("Limite de perfis interativos atingido.",409);
            view??=new(profile,portal,owner);JsonElement[] cookies=[];
            if(File.Exists(CookieFile(profile.Id)))
            {
                try
                {
                    var saved=Protector(profile.Id).Unprotect(File.ReadAllText(CookieFile(profile.Id)));
                    // Old snapshots did not record a completed identity check. Keep
                    // them on disk, but do not resurrect interrupted SSO requests.
                    cookies=CookiesToRestore(saved);
                }
                catch(CryptographicException){throw new RuleException("Não foi possível abrir o checkpoint protegido do perfil. Nenhum dado foi apagado.",409);}
                catch(JsonException){throw new RuleException("O checkpoint do perfil tem formato inválido. Nenhum dado foi apagado.",409);}
            }
            using var response=await Send(new{command="open",profileId=profile.Id,portal,url,cookies});if(!response.IsSuccessStatusCode)throw new RuleException("O navegador interativo não abriu o portal.",503);
            views[key]=view;if(jobId!="")jobs[jobId]=view;if(profile.Kind!="Aluno")registry.Set(profile.Id,portal,true);
        }finally{openGate.Release();}
    }
    View Student(string job,string sid,string user)
    {
        if(!jobs.TryGetValue(job,out var v)||v.Profile.StudentId!=sid)throw new RuleException("Reabra o navegador pela ficha do aluno.",409);if(v.Owner!=user)throw new RuleException("Sessão pertence a outro atendente.",403);return v;
    }
    View Professional(string id,string portal)=>views.GetValueOrDefault(id+"/"+portal)??throw new RuleException("Retome o navegador pela central de sessões.",409);
    async Task Observe(View v)
    {
        var data=await Call(v,"status");var observation=data.GetProperty("observation");var url=data.GetProperty("url").GetString()!;v.Host=PortalSessions.Allowed(url).Host;v.Instance=data.GetProperty("instanceId").GetString()!;
        var page=observation.Deserialize<GovLoginObservation>(Store.Json)!;var text=observation.GetProperty("text").GetString()??"";v.Connected=observation.GetProperty("connectedIndicator").GetBoolean()||DetranConnection(v.Host,page,text);v.Identity=v.Profile.Kind=="Aluno"?v.Connected&&v.Host.Contains("detran")&&PortalExtraction.ContainsCpf(text,v.Cpf):text.Contains(v.Profile.Name,StringComparison.OrdinalIgnoreCase);
        page=page with{IdentityMatched=v.Profile.Kind=="Aluno"&&v.Identity};v.Error=StudentGovLogin.SafeError(page.Error,v.Cpf,v.Login?.Password??"");
        if(v.Error!=""&&v.Error!=v.Logged){logger.LogInformation("Native portal {Profile}: GOV.BR recusou etapa {Code}",v.Profile.Id,System.Text.RegularExpressions.Regex.Match(v.Error,@"ERL\d{3,}").Value);v.Logged=v.Error;}
        if(v.Profile.Kind=="Aluno")
        {
            if(v.Login is {Status.Active:true} login)
            {
                var command=login.Observe(page);if(command!=GovLoginCommand.None)
                {
                    var password=login.Password;login.Attempted(command);
                    try{await Call(v,"assist",action:command switch{GovLoginCommand.EnterGov=>"entry",GovLoginCommand.FillCpf=>"cpf",GovLoginCommand.SubmitCpf=>"continue",_=>"password"},value:command==GovLoginCommand.FillCpf?login.Cpf:command==GovLoginCommand.SubmitPassword?password:"");}
                    catch(RuleException){login.Failure("A página mudou. Confira a mesma tentativa sem repetir o envio.");}
                    finally{if(command==GovLoginCommand.SubmitPassword)login.PasswordDispatched();}
                }
                v.State=login.Status;
            }
            else v.State=StudentGovLogin.ManualStatus(v.State,page,v.Cpf)??v.State;
        }
        else
        {
            if(page.Account||page.Password||!v.Connected)v.Confirmed=false;
            v.State=!page.PageReady?new(v.Error!=""?"page-error":"opening",v.Error!=""?"A página do portal não carregou. Reinicie o acesso deste profissional.":"Carregando o portal no navegador do servidor.",v.Error,false):v.Error!=""?new("portal-error","O GOV.BR recusou a verificação; veja a mensagem da página.",v.Error,false):new(v.Connected?(v.Confirmed?"connected":"check-identity"):page.Challenge?"challenge":"login-required",v.Connected?"Confira a identidade conectada na página.":"Navegador interativo do servidor. Faça o login e a verificação diretamente na página.","",false,v.Confirmed);
        }
        // Do not overwrite a confirmed checkpoint with an incomplete/error page or an unconfirmed account.
        if(!page.PageReady||page.Account||page.Password||(v.Connected&&v.Profile.Kind!="Aluno"&&!v.Confirmed))return;
        var verified=v.Connected&&(v.Profile.Kind=="Aluno"?v.Identity:v.Confirmed);
        var snapshot=JsonSerializer.Serialize(new CookieCheckpoint(verified,data.GetProperty("cookies").Deserialize<JsonElement[]>()??[]),Store.Json);
        var hash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot)));
        if(v.CookieHash!=hash)
        {
            var file=CookieFile(v.Profile.Id);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if(File.Exists(file)&&!File.Exists(file+".previous.protected"))File.Copy(file,file+".previous.protected");
            File.WriteAllText(file+".tmp",Protector(v.Profile.Id).Protect(snapshot));
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(file+".tmp",UnixFileMode.UserRead|UnixFileMode.UserWrite);
            File.Move(file+".tmp",file,true);v.CookieHash=hash;
        }
    }
    object StatusObject(View v)=>new{profileId=v.Profile.Id,profileName=v.Profile.Name,kind=v.Profile.Kind,portal=v.Portal,stage=v.State.Stage,message=v.State.Message,error=v.Error,active=v.State.Active,authenticated=v.State.Authenticated,connectedIndicator=v.Connected,identityVisible=v.Identity,host=v.Host,instanceId=v.Instance,width=1280,height=800,persistent=true,browserOpen=true,engine="native",diagnostics=Array.Empty<object>()};
    public async Task<object> Login(string job,string sid,string user,string cpf,string password,bool restart=false)
    {
        var v=Student(job,sid,user);await v.Gate.WaitAsync();try
        {
            if(v.Login==null||restart)
            {
                v.Login?.Stop();v.Cpf=StudentGovLogin.Digits(cpf);v.Login=new(v.Cpf,password);v.State=v.Login.Status;
                var attempt=v.Login;_ = Task.Run(async()=>{await Task.Delay(TimeSpan.FromMinutes(runtime.LoginWaitMinutes()));await v.Gate.WaitAsync();try{if(v.Login==attempt&&attempt.Status.Active)attempt.Stop("timeout","O prazo de preenchimento terminou. Continue diretamente na página; a sessão permanece salva.");}finally{v.Gate.Release();}});
            }
            try{await Observe(v);}catch(RuleException ex) when(ex.Status==409){v.State=new("opening","Carregando o acesso no navegador interativo.","",true);}
            return StatusObject(v);
        }finally{v.Gate.Release();}
    }
    public async Task<object> Status(string id,string portal,string user,string lease,bool student=false,string sid="",string cpf="")
    {
        var v=student?Student(id,sid,user):Professional(id,portal);await v.Gate.WaitAsync();try{if(!student)v.Control.Check(user,lease);else if(cpf!="")v.Cpf=StudentGovLogin.Digits(cpf);await Observe(v);return StatusObject(v);}finally{v.Gate.Release();}
    }
    public async Task<object> OpenProfessional(BrowserProfile profile,string portal,string user)
    {
        await Open(profile,portal,ProfessionalBrowser.Portal(portal));var v=Professional(profile.Id,portal);await v.Gate.WaitAsync();try{var lease=v.Control.Acquire(user);try{await Observe(v);}catch(RuleException ex) when(ex.Status==409){v.State=new("opening","Carregando o portal no navegador interativo.","",false);}return new{lease,session=StatusObject(v)};}finally{v.Gate.Release();}
    }
    public async Task Restore(BrowserProfile profile,string portal){await Open(profile,portal,ProfessionalBrowser.Portal(portal));var v=Professional(profile.Id,portal);await v.Gate.WaitAsync();try{await Observe(v);}catch(RuleException){/* Portal can still be loading on restore. */}finally{v.Gate.Release();}}
    public Task<object[]> Overview()=>Task.FromResult(views.Values.Where(v=>v.Profile.Kind!="Aluno").Select(v=>(object)new{profileId=v.Profile.Id,portal=v.Portal,browserOpen=true,stage=v.State.Stage,authenticated=v.State.Authenticated,controlled=v.Control.Held,engine="native"}).ToArray());
    public async Task<byte[]> Frame(string id,string portal,string user,string lease,bool student=false,string sid="")
    {
        var v=student?Student(id,sid,user):Professional(id,portal);await v.Gate.WaitAsync();try
        {
            if(!student)v.Control.Check(user,lease);
            using var response=await Send(new{command="frame",profileId=v.Profile.Id,portal=v.Portal});
            if(!response.IsSuccessStatusCode)throw new RuleException("A imagem ainda não está disponível.",409);
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException)
        {
            throw new RuleException("O navegador interativo não respondeu. Feche a visualização e retome o acesso; o perfil permanece salvo.",503);
        }
        finally{v.Gate.Release();}
    }
    public async Task<object> Input(string id,string portal,string user,string lease,PortalInput input,bool student=false,string sid="")
    {
        input.Validate();var v=student?Student(id,sid,user):Professional(id,portal);await v.Gate.WaitAsync();try{if(!student)v.Control.Check(user,lease);else if(input.Kind is "click" or "down" or "text" or "key"){var current=await Call(v,"status");v.Login?.HumanInput(current.GetProperty("observation").GetProperty("password").GetBoolean());}if(input.Kind=="entry"){if(student)v.Login?.Stop();await Call(v,"assist",action:"entry",humanGesture:true);}else await Call(v,"input",input);return StatusObject(v);}finally{v.Gate.Release();}
    }
    public async Task<object> Pause(string job,string sid,string user,bool qr=false)
    {
        var v=Student(job,sid,user);await v.Gate.WaitAsync();try{v.Login?.Stop();v.State=v.Login?.Status??new("paused","Continue diretamente na página.","",false);if(qr)await Call(v,"assist",action:"qr");return StatusObject(v);}finally{v.Gate.Release();}
    }
    public async Task<object> Confirm(string id,string portal,string user,string lease,bool confirmed,string name)
    {
        var v=Professional(id,portal);await v.Gate.WaitAsync();try{v.Control.Check(user,lease);await Observe(v);if(!confirmed||!name.Trim().Equals(v.Profile.Name.Trim(),StringComparison.OrdinalIgnoreCase))throw new RuleException("Marque a conferência somente depois de verificar a conta do profissional no portal.",409);if(!v.Connected)throw new RuleException("O login ainda não foi detectado. Se já entrou, abra o menu da conta no portal para deixar a opção Sair visível e tente confirmar novamente.",409);v.Confirmed=true;v.State=v.State with{Stage="connected",Authenticated=true,Message="Identidade conferida pelo atendimento; página indica sessão conectada."};await Observe(v);logger.LogInformation("Identidade profissional conferida no app: {Profile}/{Portal}, usuário {User}",id,portal,user);return StatusObject(v);}finally{v.Gate.Release();}
    }
    public async Task<object> Recover(string id,string portal,string user,string lease,bool acknowledged)
    {
        if(portal!="detran"||!acknowledged)throw new RuleException("Confirme a recuperação do DETRAN deste perfil.",400);
        var v=Professional(id,portal);await v.Gate.WaitAsync();try
        {
            v.Control.Check(user,lease);using var response=await Send(new{command="recover",profileId=id,portal,acknowledged=true});
            if(!response.IsSuccessStatusCode)throw new RuleException("Não foi possível reiniciar. Feche primeiro o e-CNH deste mesmo profissional, se estiver aberto, e retome o DETRAN.",409);
            v.Confirmed=false;v.Connected=false;v.Identity=false;v.Error="";v.Logged="";v.CookieHash="";v.State=new("opening","Acesso DETRAN reiniciado. Faça um novo login e conclua as verificações pessoais.","",false);
            var file=CookieFile(id);if(File.Exists(file))File.Move(file,file+".reset-"+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+".protected");
            logger.LogInformation("Recuperação de acesso DETRAN: perfil {Profile}, usuário {User}",id,user);return StatusObject(v);
        }finally{v.Gate.Release();}
    }
    public async Task<object> Release(string id,string portal,string user,string lease)
    {
        var v=Professional(id,portal);await v.Gate.WaitAsync();try{v.Control.Check(user,lease);await Call(v,"input",new PortalInput("up"));v.Control.Release(user,lease);return new{released=true,browserOpen=true};}finally{v.Gate.Release();}
    }
    public async Task<object> Close(string id,string portal,string user,string lease,bool student=false,string sid="")
    {
        var v=student?Student(id,sid,user):Professional(id,portal);await v.Gate.WaitAsync();try{if(!student)v.Control.Check(user,lease);v.Login?.Stop();try{await Observe(v);}catch(RuleException ex) when(ex.Status==409){logger.LogDebug("Encerrando visualização indisponível: {Profile}/{Portal}",id,portal);}await Call(v,"close");views.TryRemove(v.Profile.Id+"/"+v.Portal,out _);foreach(var job in jobs.Where(x=>x.Value==v).ToArray())jobs.TryRemove(job.Key,out _);if(!student)registry.Set(id,portal,false);return new{closed=true,persistent=true};}finally{v.Gate.Release();}
    }
}

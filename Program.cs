using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using CfcPilot;
using Microsoft.AspNetCore.DataProtection;

if (args.FirstOrDefault() == "--import-infor")
{
    var package = args.Length > 1 ? args[1] : throw new ArgumentException("Informe a pasta do pacote exportado.");
    var dataDirectory = Environment.GetEnvironmentVariable("CFC_DATA_DIR") ?? Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
    await PackageImporter.Run(package, dataDirectory, args.Contains("--activate"));
    return;
}

if(args.FirstOrDefault()=="--device-diagnostics"){Console.WriteLine(JsonSerializer.Serialize(DeviceDiagnostics.Read()));return;}
if(args.FirstOrDefault()=="--create-import-fixture"){CommercialChecks.CreateFixture(args[1]);return;}
if(args.FirstOrDefault()=="--self-test-finance"){await FinanceChecks.Run();return;}
if(args.FirstOrDefault()=="--self-test-commercial"){CommercialChecks.Run();return;}
if(args.FirstOrDefault()=="--self-test-community"){CommunityChecks.Run();return;}
if(args.FirstOrDefault()=="--self-test-extraction"){PortalExtractionChecks.Run();return;}
if(args.FirstOrDefault()=="--self-test-configuration"){ConfigurationChecks.Run();return;}
if(args.FirstOrDefault()=="--self-test-manual-browser"){ManualBrowserChecks.Run();return;}
if(args.FirstOrDefault()=="--self-test-student-login"){StudentGovLoginChecks.Run();return;}
if(args.FirstOrDefault()=="--self-test-professional-sessions"){ProfessionalSessionChecks.Run();return;}
if(args.FirstOrDefault()=="--self-test-portal-input"){await PortalInputChecks.Run();return;}
if(args.FirstOrDefault()=="--repair-agenda"){AgendaRepair.Run(Environment.GetEnvironmentVariable("CFC_DATA_DIR")??Path.Combine(Directory.GetCurrentDirectory(),"App_Data"));return;}
if(args.FirstOrDefault()=="--create-relational-fixture"){RelationalFixture.Create(args[1]);return;}
if(args.FirstOrDefault()=="--upgrade-projection"){ProjectionUpgrade.Run(Environment.GetEnvironmentVariable("CFC_DATA_DIR")??Path.Combine(Directory.GetCurrentDirectory(),"App_Data"));return;}
if(args.FirstOrDefault()=="--self-test-student-dedup"){StudentDedupChecks.Run();return;}
if(args.FirstOrDefault()=="--activate-import"){OperatorImport.Activate(Environment.GetEnvironmentVariable("CFC_DATA_DIR")??throw new ArgumentException("Informe CFC_DATA_DIR."));return;}
if(args.FirstOrDefault()=="--audit-import"){ImportChecks.Run(Environment.GetEnvironmentVariable("CFC_DATA_DIR")??Path.Combine(Directory.GetCurrentDirectory(),"App_Data"));return;}
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWindowsService(options=>options.ServiceName="CFC Plus Server");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dataDir = builder.Configuration["CFC_DATA_DIR"] ?? Path.Combine(builder.Environment.ContentRootPath,TenantPlatform.Enabled(builder.Configuration)?"App_Data_Central":"App_Data");
if(!TenantPlatform.Enabled(builder.Configuration))builder.Configuration.AddJsonFile(Path.Combine(dataDir, "evolution.config.json"), optional: true, reloadOnChange: true).AddJsonFile(Path.Combine(dataDir,"native-portal-settings.json"),optional:true,reloadOnChange:false).AddEnvironmentVariables();
var central=TenantPlatform.Enabled(builder.Configuration);
if(central)builder.Configuration["CFC_INSTALLATION_MODE"]="commercial";
var protection = builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys"))).SetApplicationName(central?"CfcPilot-platform":"CfcPilot");
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
if(!central)CommercialInstallation.Prepare(builder.Configuration,builder.Environment);
builder.Services.AddSingleton<TenantPlatform>();
if(central)builder.Services.AddHostedService<TenantWorkers>();
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<FinanceService>();
builder.Services.AddSingleton<ImportQueue>();
if(!central)builder.Services.AddHostedService(p=>p.GetRequiredService<ImportQueue>());
builder.Services.AddSingleton<BrowserRuntime>();
builder.Services.AddSingleton<NativePortalSessions>();
builder.Services.AddSingleton<ProfessionalBrowser>();
builder.Services.AddSingleton<IPortalWindowLauncher,PortalWindowLauncher>();
builder.Services.AddSingleton<ManualPortalBrowser>();
builder.Services.AddSingleton<LegacyArchive>();
builder.Services.AddSingleton<ImportedModules>();
builder.Services.AddSingleton<PortalSessions>();
builder.Services.AddSingleton<ProfessionalSessionRegistry>();
builder.Services.AddSingleton<ProfessionalCookieVault>();
if(!central)builder.Services.AddHostedService<ProfessionalSessionRestorer>();
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options=>{
 options.ForwardedHeaders=Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor|Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;options.ForwardLimit=1;
 foreach(var ip in (builder.Configuration["CFC_TRUSTED_PROXY"]??"").Split(';',StringSplitOptions.RemoveEmptyEntries))options.KnownProxies.Add(System.Net.IPAddress.Parse(ip.Trim()));
});
builder.Services.AddSingleton<AutomationService>();
builder.Services.AddSingleton<AgendaReview>();
builder.Services.AddSingleton<StudentResources>();
builder.Services.AddSingleton<CommunityPortal>();
builder.Services.AddHttpClient<EvolutionClient>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "cfc.pilot"; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Strict;
    if(CommercialInstallation.Enabled(builder.Configuration)&&!(central&&builder.Environment.IsDevelopment()&&builder.Configuration["CFC_ALLOW_LOCAL_HTTP"]=="true"))options.Cookie.SecurePolicy=CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8); options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options => options.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
var app = builder.Build();
var localCentral=central&&app.Environment.IsDevelopment()&&builder.Configuration["CFC_ALLOW_LOCAL_HTTP"]=="true";
if(central)app.Services.GetRequiredService<TenantPlatform>().Prepare();
if(args.FirstOrDefault()=="--provision-school"){if(!central||args.Length!=3)throw new ArgumentException("Provisionamento exige modo central, ID e nome da escola.");Console.WriteLine(JsonSerializer.Serialize(await app.Services.GetRequiredService<TenantPlatform>().Register(args[1],args[2])));await app.DisposeAsync();return;}
if(args.FirstOrDefault()=="--adopt-school"){if(!central)throw new InvalidOperationException("Adoção exige modo central.");string Arg(string name){var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:throw new ArgumentException("Informe "+name);}Console.WriteLine(JsonSerializer.Serialize(await app.Services.GetRequiredService<TenantPlatform>().Register(args[1],Arg("--school-name"),Arg("--legacy-data"))));await app.DisposeAsync();return;}
app.UseForwardedHeaders();
app.Use(async(ctx,next)=>{
 if(CommercialInstallation.Enabled(builder.Configuration)&&!(localCentral&&System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress??System.Net.IPAddress.None))&&!ctx.Request.IsHttps&&ctx.Request.Path.StartsWithSegments("/api")&&ctx.Request.Path!="/api/health"){ctx.Response.Headers.CacheControl="no-store";ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{error="Use o endereço HTTPS da autoescola. Confira o proxy confiável do servidor."});return;}
 if(!central&&ctx.Request.Method!="GET"&&ctx.Request.Path.StartsWithSegments("/api")&&ctx.Request.Path!="/api/auth/logout"&&app.Services.GetRequiredService<ImportQueue>().Maintenance){ctx.Response.StatusCode=503;await ctx.Response.WriteAsJsonAsync(new{error="Base ativada. Serviço em reinício para concluir a importação."});return;}
 await next();
});
app.Use(async (context, next) =>
{
    if(CommercialInstallation.Enabled(builder.Configuration)&&context.Request.IsHttps)context.Response.Headers["Strict-Transport-Security"]="max-age=31536000";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Method != "GET" && context.Request.Headers["X-CFC-Client"] != "pilot") { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "Origem da operação inválida." }); return; }
        var origin = context.Request.Headers.Origin.ToString();
        if (origin != "" && origin != $"{context.Request.Scheme}://{context.Request.Host}") { context.Response.StatusCode = 403; return; }
    }
    try { await next(); }
    catch (RuleException ex) { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (JsonException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Dados inválidos." }); }
    catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or OverflowException)
    { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Confira os valores informados." }); }
});
var shellStaticFiles=new StaticFileOptions{OnPrepareResponse=ctx=>
{
    if(ctx.File.Name is "index.html" or "sw.js"||ctx.File.Name.EndsWith(".js",StringComparison.OrdinalIgnoreCase))ctx.Context.Response.Headers.CacheControl="no-cache";
}};
app.UseDefaultFiles(); app.UseStaticFiles(shellStaticFiles); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
if(central){app.UseTenantSelection();app.MapPlatform();}
app.MapGet("/api/health", () => new { status = "ok", application="cfc-plus", version = "0.4.2", central, demo=!central&&!CommercialInstallation.Enabled(builder.Configuration)&&!app.Services.GetRequiredService<Store>().IsReal });
app.MapGet("/api/auth/units",(Store store)=>Results.Ok(store.LoginUnits()));
app.MapPost("/api/auth/login", async (HttpContext ctx, Store store, JsonElement body) =>
{
    var login = body.GetProperty("login").GetString() ?? ""; var password = body.GetProperty("password").GetString() ?? "";
    var unit=body.TryGetProperty("unit",out var selectedUnit)?selectedUnit.GetString()??"":"";
    if(login.Length is <1 or >80||password.Length is <1 or >128)throw new RuleException("Login ou senha incorretos.",401);
    var candidates=(await store.UsersByLogin(login,unit)).Where(x=>x.Active&&Passwords.Verify(password,x.PasswordHash)).ToArray();
    if(candidates.Length==0)throw new RuleException("Login ou senha incorretos.",401);
    if(candidates.Length>1)throw new RuleException("Há acessos com este login em mais de uma unidade. Selecione a unidade ou peça ao administrador para diferenciar os logins duplicados.",409);
    var user=candidates[0];
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Name, user.Name), new(ClaimTypes.Role, user.Role),new("cfc-stamp",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash)))),new("cfc-school",ctx.Items["cfc-school"]?.ToString()??"")], CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new { user.Id, user.Name, user.Role, schoolId=ctx.Items["cfc-school"]?.ToString()??"", schoolName=ctx.Items["cfc-school-name"]?.ToString()??"", mustChangePassword=CommercialInstallation.Enabled(builder.Configuration)&&user.MustChangePassword });
}).RequireRateLimiting("login");
app.MapPost("/api/auth/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.Ok(); });
async Task<User> Current(HttpContext ctx, Store store)
{
    var user = await store.UserById(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");if(user is not {Active:true})throw new RuleException("Sessão encerrada. Entre novamente.",401);
    var stamp=ctx.User.FindFirstValue("cfc-stamp");if((stamp!=null||CommercialInstallation.Enabled(builder.Configuration))&&stamp!=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash))))throw new RuleException("Seu acesso mudou. Entre novamente.",401);
    if(CommercialInstallation.Enabled(builder.Configuration)&&user.MustChangePassword&&ctx.Request.Path!="/api/auth/password"&&ctx.Request.Path!="/api/auth/me")throw new RuleException("Troque a senha inicial antes de utilizar o sistema.",428);return user;
}
app.MapGet("/api/auth/me",async(HttpContext ctx,Store store)=>{var u=await Current(ctx,store);return Results.Ok(new{u.Id,u.Name,u.Role,schoolId=ctx.Items["cfc-school"]?.ToString()??"",schoolName=ctx.Items["cfc-school-name"]?.ToString()??"",mustChangePassword=CommercialInstallation.Enabled(builder.Configuration)&&u.MustChangePassword});}).RequireAuthorization();
app.MapPost("/api/auth/password",async(HttpContext ctx,Store store,JsonElement body)=>{
 var u=await Current(ctx,store);var old=body.GetProperty("currentPassword").GetString()??"";var password=body.GetProperty("newPassword").GetString()??"";
 if(old.Length>128||password.Length is <12 or >128||password==old||!Passwords.Verify(old,u.PasswordHash))throw new RuleException("Informe a senha atual e uma nova senha diferente, de 12 a 128 caracteres.");
 var op=ctx.Request.Headers["X-Operation-Id"].ToString();if(!Guid.TryParse(op,out _))throw new RuleException("Operação sem identificador.");
 await store.Mutate(u.Id,op,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.GetRawText()))),s=>{var a=s.Users.Find(x=>x.Id==u.Id&&x.Active)??throw new RuleException("Usuário inativo.",401);if(a.PasswordHash!=u.PasswordHash)throw new RuleException("A senha mudou. Entre novamente.",409);a.PasswordHash=Passwords.Hash(password);a.MustChangePassword=false;s.Audit.Add(new(){Id=Operations.Id(),User=u.Name,Action="Troca de senha própria",Date=Operations.Now.ToString("s")});return new{ok=true};},new StateScope{Action="password-self",Body=body});
 if(u.Id=="admin"){var initial=Path.Combine(store.DataRoot,"first-access.json");if(File.Exists(initial))File.Delete(initial);}
 await ctx.SignOutAsync();return Results.Ok(new{loginRequired=true});
}).RequireAuthorization().RequireRateLimiting("login");
app.MapGet("/api/distribution",(HttpContext ctx)=>Results.Ok(central?new{central=true,commercial=true,schoolName="CFC+",version="0.4.2",portal="/portal/",install="/install/",desktop=File.Exists(Path.Combine(dataDir,"downloads","CFC-Plus-Windows.exe"))?"/api/distribution/windows":"",support=builder.Configuration["CFC_SUPPORT_EMAIL"]??""}:CommercialInstallation.Public(builder.Configuration)));
app.MapGet("/api/distribution/windows",()=>{var file=Path.Combine(dataDir,"downloads","CFC-Plus-Windows.exe");return File.Exists(file)?Results.File(file,"application/vnd.microsoft.portable-executable","CFC-Plus-Windows.exe",enableRangeProcessing:true):Results.NotFound(new{error="O instalador será disponibilizado pelo responsável da plataforma."});});
app.MapGet("/api/system/imports",async(HttpContext ctx,Store store,ImportQueue queue)=>Results.Ok(queue.List(await Current(ctx,store)))).RequireAuthorization();
app.MapPost("/api/system/imports",async(HttpContext ctx,Store store,ImportQueue queue,JsonElement body)=>Results.Ok(await queue.Create(await Current(ctx,store),body.GetProperty("name").GetString()??"",body.GetProperty("bytes").GetInt64()))).RequireAuthorization();
app.MapPost("/api/system/imports/{id}/chunk",async(string id,long offset,HttpContext ctx,Store store,ImportQueue queue)=>Results.Ok(await queue.Append(await Current(ctx,store),id,offset,ctx.Request.Body,ctx.Request.ContentLength))).RequireAuthorization();
app.MapPost("/api/system/imports/{id}/verify",async(string id,HttpContext ctx,Store store,ImportQueue queue)=>Results.Ok(await queue.Enqueue(await Current(ctx,store),id))).RequireAuthorization();
app.MapPost("/api/system/imports/{id}/discard",async(string id,HttpContext ctx,Store store,ImportQueue queue)=>Results.Ok(await queue.Discard(await Current(ctx,store),id))).RequireAuthorization();
app.MapPost("/api/system/imports/{id}/activate",async(string id,HttpContext ctx,Store store,ImportQueue queue,JsonElement body)=>{var result=await queue.Activate(await Current(ctx,store),id,body.TryGetProperty("confirmed",out var c)&&c.ValueKind==JsonValueKind.True);ctx.Response.OnCompleted(async()=>{await Task.Delay(2000);if(central)await app.Services.GetRequiredService<TenantPlatform>().Reload(ctx.User.FindFirstValue("cfc-school")!);else app.Lifetime.StopApplication();});return Results.Ok(result);}).RequireAuthorization();
app.MapGet("/api/students/{sid}/gov-credential",async(string sid,HttpContext ctx,Store store,StudentResources resources)=>Results.Ok(await resources.CredentialStatus(await Current(ctx,store),sid))).RequireAuthorization();
app.MapPost("/api/students/{sid}/gov-credential",async(string sid,HttpContext ctx,Store store,StudentResources resources,JsonElement body)=>Results.Ok(await resources.SaveCredential(await Current(ctx,store),sid,body.GetProperty("password").GetString()??""))).RequireAuthorization();
app.MapPost("/api/students/{sid}/gov-credential/reveal",async(string sid,HttpContext ctx,Store store,StudentResources resources)=>Results.Ok(new{password=await resources.Password(await Current(ctx,store),sid,true)})).RequireAuthorization();
app.MapGet("/api/students/{sid}/gov/access",async(string sid,HttpContext ctx,Store store,StudentResources resources,BrowserRuntime runtime)=>
{
    var user=await Current(ctx,store);var student=await resources.Authorize(user,sid);
    return Results.Ok(new{studentId=sid,name=student.Name,cpf=student.Cpf,portalUrl=ProfessionalBrowser.StudentPortal(),browser=runtime.Availability()});
}).RequireAuthorization();
app.MapGet("/api/browser/config",async(HttpContext ctx,Store store,BrowserRuntime runtime)=>{StudentResources.Team(await Current(ctx,store));return Results.Ok(runtime.Availability());}).RequireAuthorization();
app.MapPost("/api/browser/config",async(HttpContext ctx,Store store,BrowserRuntime runtime,PortalBrowserOptions options)=>Results.Ok(runtime.Save(await Current(ctx,store),options))).RequireAuthorization();
async Task<(User User,Student Student,object Job,string JobId)> PrepareStudentGov(string sid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation,JsonElement body)
{
    var user=await Current(ctx,store);var student=await resources.Authorize(user,sid);var request=ctx.Request.Headers["X-Operation-Id"].ToString();if(!Guid.TryParse(request,out _))throw new RuleException("Operação sem identificador válido.");
    var loginMode=body.TryGetProperty("mode",out var mode)?mode.GetString():"assisted";if(loginMode is not("manual" or "assisted"))throw new RuleException("Escolha login manual ou navegador interno.");
    var operation=body.TryGetProperty("operation",out var selected)?selected.GetString():"student-detran";if(operation is not("student-detran" or "logaralunogov"))throw new RuleException("Escolha o acesso GOV.BR do aluno.");
    var profileBody=JsonSerializer.SerializeToElement(new{studentId=sid});var profileJson=await store.Mutate(user.Id,request+":student-profile",sid,s=>Operations.Execute(s,s.Users.Find(x=>x.Id==user.Id)!,"student-browser-profile",profileBody),new StateScope{Action="student-browser-profile",Body=profileBody});
    var profile=JsonSerializer.Deserialize<BrowserProfile>(profileJson,Store.Json)!;var job=automation.Prepare(user,JsonSerializer.SerializeToElement(new{studentId=sid,profileId=profile.Id,operation,loginMode}),request);var jobId=JsonSerializer.SerializeToElement(job,Database.Compact).GetProperty("id").GetString()!;return(user,student,job,jobId);
}
app.MapPost("/api/students/{sid}/gov/prepare",async(string sid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation,JsonElement body)=>Results.Ok((await PrepareStudentGov(sid,ctx,store,resources,automation,body)).Job)).RequireAuthorization();
app.MapPost("/api/students/{sid}/gov/open",async(string sid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation,PortalSessions sessions,JsonElement body)=>
{
    var mode=body.TryGetProperty("mode",out var selectedMode)?selectedMode.GetString():"assisted";if(mode is not("manual" or "assisted"))throw new RuleException("Escolha login manual ou navegador interno.");
    var useSaved=!body.TryGetProperty("useSavedPassword",out var saved)?mode=="assisted":saved.ValueKind==JsonValueKind.True;
    if(mode=="manual"&&useSaved)throw new RuleException("No Chrome normal, informe a senha diretamente no portal. Você pode consultar a senha salva na ficha.");
    var prepared=await PrepareStudentGov(sid,ctx,store,resources,automation,body);await automation.Open(prepared.JobId,prepared.User,ctx.Connection.RemoteIpAddress,mode=="manual");
    var password=mode=="assisted"&&useSaved?await resources.Password(prepared.User,sid,false):"";
    var autofill=!body.TryGetProperty("autofill",out var fill)||fill.ValueKind!=JsonValueKind.False;
    var session=mode!="assisted"?null:autofill?await sessions.StudentLogin(prepared.JobId,sid,prepared.Student.Cpf,password,prepared.User.Id):await sessions.PauseStudent(prepared.JobId,sid,prepared.User.Id);
    var loginNote=mode=="manual"?"Janela normal aberta. Informe os dados e conclua as verificações nessa janela.":"Navegador interno do aluno aberto. Acompanhe abaixo o preenchimento e conclua as verificações solicitadas pelo portal.";
    return Results.Ok(new{job=automation.Job(prepared.JobId,prepared.User),session,loginNote,loginMode=mode,automaticSuccess=false});
}).RequireAuthorization();
async Task<User> StudentSessionUser(string sid,string jid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation)
{
    var user=await Current(ctx,store);await resources.Authorize(user,sid);automation.AuthorizeStudentSession(jid,sid,user);return user;
}
app.MapGet("/api/students/{sid}/gov/sessions/{jid}",async(string sid,string jid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation,PortalSessions sessions)=>
{
    var user=await StudentSessionUser(sid,jid,ctx,store,resources,automation);var student=await resources.Authorize(user,sid);
    return Results.Ok(await sessions.StudentStatus(jid,sid,user.Id,student.Cpf));
}).RequireAuthorization();
app.MapGet("/api/students/{sid}/gov/sessions/{jid}/frame",async(string sid,string jid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation,PortalSessions sessions)=>Results.File(await sessions.StudentFrame(jid,sid,(await StudentSessionUser(sid,jid,ctx,store,resources,automation)).Id),"image/jpeg")).RequireAuthorization();
app.MapPost("/api/students/{sid}/gov/sessions/{jid}/input",async(string sid,string jid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation,PortalSessions sessions,PortalInput input)=>Results.Ok(await sessions.StudentInput(jid,sid,(await StudentSessionUser(sid,jid,ctx,store,resources,automation)).Id,input))).RequireAuthorization();
app.MapPost("/api/students/{sid}/gov/sessions/{jid}/pause",async(string sid,string jid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation,PortalSessions sessions)=>Results.Ok(await sessions.PauseStudent(jid,sid,(await StudentSessionUser(sid,jid,ctx,store,resources,automation)).Id))).RequireAuthorization();
app.MapPost("/api/students/{sid}/gov/sessions/{jid}/qr",async(string sid,string jid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation,PortalSessions sessions)=>Results.Ok(await sessions.StudentQr(jid,sid,(await StudentSessionUser(sid,jid,ctx,store,resources,automation)).Id))).RequireAuthorization();
app.MapPost("/api/students/{sid}/gov/sessions/{jid}/close",async(string sid,string jid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation,PortalSessions sessions)=>Results.Ok(await sessions.CloseStudent(jid,sid,(await StudentSessionUser(sid,jid,ctx,store,resources,automation)).Id))).RequireAuthorization();
app.MapPost("/api/students/{sid}/gov/sessions/{jid}/restart",async(string sid,string jid,HttpContext ctx,Store store,StudentResources resources,AutomationService automation,PortalSessions sessions)=>
{
    var user=await StudentSessionUser(sid,jid,ctx,store,resources,automation);var student=await resources.Authorize(user,sid);
    return Results.Ok(await sessions.StudentLogin(jid,sid,student.Cpf,await resources.Password(user,sid,false),user.Id,true));
}).RequireAuthorization();
app.MapGet("/api/students/{sid}/files",async(string sid,HttpContext ctx,Store store,StudentResources resources)=>Results.Ok(await resources.List(await Current(ctx,store),sid,int.TryParse(ctx.Request.Query["page"],out var page)?Math.Clamp(page,1,100000):1))).RequireAuthorization();
app.MapPost("/api/students/{sid}/files",async(string sid,HttpContext ctx,Store store,StudentResources resources)=>
{
    var user=await Current(ctx,store);await resources.Authorize(user,sid);if(!ctx.Request.HasFormContentType)throw new RuleException("Envie o arquivo pelo formulário de anexos.");
    var size=ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();if(size is {IsReadOnly:false})size.MaxRequestBodySize=StudentResources.MaxFileBytes+1024*1024;
    var form=await ctx.Request.ReadFormAsync();if(form.Files.Count!=1)throw new RuleException("Envie um arquivo por operação.");return Results.Ok(await resources.Upload(user,sid,form.Files[0],form["title"].ToString(),form["category"].ToString(),ctx.Request.Headers["X-Operation-Id"].ToString()));
}).RequireAuthorization();
app.MapGet("/api/student-files/{id}",async(string id,HttpContext ctx,Store store,StudentResources resources)=>
{
    var file=await resources.Read(await Current(ctx,store),id);if(ctx.Request.Query["download"]=="1")return Results.File(file.Bytes,file.Info.ContentType,file.Info.FileName);
    ctx.Response.Headers.ContentDisposition=new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline"){FileNameStar=file.Info.FileName}.ToString();return Results.File(file.Bytes,file.Info.ContentType);
}).RequireAuthorization();
app.MapGet("/api/snapshot", async (HttpContext ctx, Store store) =>
{
    var user = await Current(ctx, store); if(CommercialInstallation.Enabled(builder.Configuration)&&user.Role is "Aluno" or "Instrutor")throw new RuleException("Utilize seu portal pessoal.",403);var q = ctx.Request.Query;
    var scope = new StateScope { StudentId = q["studentId"].ToString(), UnitId = q["unit"].ToString(), Query = q["q"].ToString(), View = q["view"].ToString(), Page = int.TryParse(q["page"], out var n) ? Math.Clamp(n, 1, 10000) : 1 };
    if (user.Role == "Aluno") scope.StudentId = user.LinkedId;
    foreach (var key in new[] { "from", "to" }) if (q[key].ToString() != "") { if (!DateOnly.TryParseExact(q[key], "yyyy-MM-dd", out var date)) throw new RuleException("Período inválido."); if (key == "from") scope.From = date.ToString("yyyy-MM-dd"); else scope.To = date.ToString("yyyy-MM-dd"); }
    if (DateOnly.Parse(scope.To).DayNumber - DateOnly.Parse(scope.From).DayNumber is < 0 or > 366) throw new RuleException("Consulte períodos de até um ano.");
    return Results.Ok(Operations.Snapshot(await store.Read(scope), user));
}).RequireAuthorization();
app.MapGet("/api/students/search", async (string q, string? unit, HttpContext ctx, Store store) =>
{
    var user = await Current(ctx, store); if (user.Role is "Aluno" or "Instrutor") throw new RuleException("Acesso restrito ao atendimento.", 403);
    return Results.Ok(store.IsReal ? store.SearchStudents(q, unit ?? "") : (await store.Read()).Students.Where(x => RelationalStore.SearchText(x.Name).Contains(RelationalStore.SearchText(q)) || x.Cpf.Contains(string.Concat(q.Where(char.IsDigit))) && q.Any(char.IsDigit)).Take(20));
}).RequireAuthorization();
app.MapPost("/api/commands/{action}", async (string action, HttpContext ctx, Store store, JsonElement body) =>
{
    var user = await Current(ctx, store); var op = ctx.Request.Headers["X-Operation-Id"].ToString();
    if (!Guid.TryParse(op, out _)) throw new RuleException("Operação sem identificador válido.");
    var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(action + body.GetRawText())));
    var result = await store.Mutate(user.Id, op, fingerprint, s =>
    {
        var actual = s.Users.Find(x => x.Id == user.Id && x.Active) ?? throw new RuleException("Usuário inativo.", 403);
        if (body.TryGetProperty("expectedRevision", out var revision) && revision.GetInt64() != s.Revision) throw new RuleException("Os dados mudaram em outro atendimento. Atualize e revise antes de salvar.", 409);
        var value=Operations.Execute(s,actual,action,body);if(CommercialInstallation.Enabled(builder.Configuration)&&action=="user"&&body.TryGetProperty("password",out var pw)&&pw.GetString() is {Length:>0}){var created=JsonSerializer.SerializeToElement(value,Database.Compact);var target=s.Users.Find(x=>x.Id==created.GetProperty("id").GetString());if(target!=null)target.MustChangePassword=true;}return value;
    }, new StateScope { Action = action, Body = body,StudentId=user.Role=="Aluno"?user.LinkedId:"" });
    return Results.Text(result, "application/json");
}).RequireAuthorization();
app.MapGet("/api/integrations/evolution", async (HttpContext ctx, Store store, EvolutionClient evo) =>
{
    var u = await Current(ctx, store); if (u.Role is "Aluno" or "Instrutor") throw new RuleException("Acesso restrito à equipe.", 403);
    return Results.Ok(await evo.Status());
}).RequireAuthorization();
app.MapGet("/api/browser/profiles", async (HttpContext ctx, Store store, ProfessionalBrowser browser,PortalSessions sessions,ProfessionalSessionRegistry registry) =>
{
    var u = await Current(ctx, store); if (u.Role is "Aluno" or "Instrutor") throw new RuleException("Acesso restrito à equipe.", 403);
    return Results.Ok(new { profiles = await store.Profiles(), browser = browser.Availability(),sessions=await sessions.ProfessionalOverview(),preferences=registry.Read(),serverOwnsSessions=true });
}).RequireAuthorization();
app.MapPost("/api/browser/local-access", async (HttpContext ctx, Store store, JsonElement body) =>
{
    var u = await Current(ctx, store); StudentResources.Team(u);
    var profileId=body.GetProperty("profileId").GetString()??"";
    var portal=body.GetProperty("portal").GetString()??"";
    var profile=(await store.Profiles()).Find(x=>x.Id==profileId)??throw new RuleException("Perfil não encontrado.",404);
    if(!Guid.TryParseExact(profile.Id,"N",out _)||profile.Kind is not("Diretor" or "Instrutor"))throw new RuleException("Escolha um perfil profissional.",403);
    var url=ProfessionalBrowser.Portal(portal);
    var op=ctx.Request.Headers["X-Operation-Id"].ToString();if(!Guid.TryParse(op,out _))throw new RuleException("Operação sem identificador válido.");
    await store.Mutate(u.Id,op+":local-browser",profile.Id+":"+portal,s=>{s.Audit.Add(new(){Id=Operations.Id(),User=u.Name,Action="local-browser-open",Detail=profile.Name+" · "+portal+" · computador do atendimento",Date=Operations.Now.ToString("s")});return new{ok=true};});
    return Results.Ok(new{schoolId=ctx.User.FindFirstValue("cfc-school")??"",profile=new{profile.Id,profile.Name,profile.Kind},url});
}).RequireAuthorization();
app.MapPost("/api/browser/open", async (HttpContext ctx, Store store, ProfessionalBrowser browser, PortalSessions sessions, JsonElement body) =>
{
    var u = await Current(ctx, store); if (u.Role is "Aluno" or "Instrutor") throw new RuleException("Acesso restrito à equipe.", 403);
    var profileId = body.GetProperty("profileId").GetString() ?? "";
    var portal = body.GetProperty("portal").GetString() ?? "";
    var profile = (await store.Profiles()).Find(x => x.Id == profileId) ?? throw new RuleException("Perfil não encontrado.", 404);
    browser.Validate(profile, portal, ctx.Connection.RemoteIpAddress);
    var op = ctx.Request.Headers["X-Operation-Id"].ToString();
    if (!Guid.TryParse(op, out _)) throw new RuleException("Operação sem identificador válido.");
    await store.Mutate(u.Id, op + ":browser", profileId + ":" + portal, s => {
        s.Audit.Add(new() { Id = Operations.Id(), User = u.Name, Action = "browser-open", Detail = profile.Name + " · " + portal, Date = Operations.Now.ToString("s") });
        return new { ok = true };
    });
    return Results.Ok(await sessions.OpenProfessional(profile,portal,u.Id));
}).RequireAuthorization();
async Task<(User User,string Lease)> ProfessionalViewer(string pid,string portal,HttpContext ctx,Store store)
{
    var user=await Current(ctx,store);StudentResources.Team(user);ProfessionalBrowser.Portal(portal);
    var profile=(await store.Profiles()).Find(x=>x.Id==pid)??throw new RuleException("Perfil não encontrado.",404);
    if(profile.Kind is not("Diretor" or "Instrutor"))throw new RuleException("Escolha um perfil profissional.",403);
    var lease=ctx.Request.Headers["X-CFC-Viewer"].ToString();if(!Guid.TryParseExact(lease,"N",out _))throw new RuleException("Abra a sessão pela central de profissionais.",409);
    return(user,lease);
}
app.MapGet("/api/browser/sessions/{pid}/{portal}",async(string pid,string portal,HttpContext ctx,Store store,PortalSessions sessions)=>{var v=await ProfessionalViewer(pid,portal,ctx,store);return Results.Ok(await sessions.ProfessionalStatus(pid,portal,v.User.Id,v.Lease));}).RequireAuthorization();
app.MapGet("/api/browser/sessions/{pid}/{portal}/frame",async(string pid,string portal,HttpContext ctx,Store store,PortalSessions sessions)=>{var v=await ProfessionalViewer(pid,portal,ctx,store);return Results.File(await sessions.ProfessionalFrame(pid,portal,v.User.Id,v.Lease),"image/jpeg");}).RequireAuthorization();
app.MapPost("/api/browser/sessions/{pid}/{portal}/input",async(string pid,string portal,HttpContext ctx,Store store,PortalSessions sessions,PortalInput input)=>{var v=await ProfessionalViewer(pid,portal,ctx,store);return Results.Ok(await sessions.ProfessionalInput(pid,portal,v.User.Id,v.Lease,input));}).RequireAuthorization();
app.MapPost("/api/browser/sessions/{pid}/{portal}/confirm",async(string pid,string portal,HttpContext ctx,Store store,PortalSessions sessions,JsonElement body)=>{var v=await ProfessionalViewer(pid,portal,ctx,store);return Results.Ok(await sessions.ConfirmProfessional(pid,portal,v.User.Id,v.Lease,body.TryGetProperty("confirmed",out var c)&&c.ValueKind==JsonValueKind.True,body.TryGetProperty("name",out var n)?n.GetString()??"":""));}).RequireAuthorization();
app.MapPost("/api/browser/sessions/{pid}/{portal}/recover",async(string pid,string portal,HttpContext ctx,Store store,NativePortalSessions native,JsonElement body)=>{var v=await ProfessionalViewer(pid,portal,ctx,store);if(!native.Enabled)throw new RuleException("Recuperação disponível no navegador interativo.",409);return Results.Ok(await native.Recover(pid,portal,v.User.Id,v.Lease,body.TryGetProperty("acknowledged",out var a)&&a.ValueKind==JsonValueKind.True));}).RequireAuthorization();
app.MapPost("/api/browser/sessions/{pid}/{portal}/release",async(string pid,string portal,HttpContext ctx,Store store,PortalSessions sessions)=>{var v=await ProfessionalViewer(pid,portal,ctx,store);return Results.Ok(await sessions.ReleaseProfessional(pid,portal,v.User.Id,v.Lease));}).RequireAuthorization();
app.MapPost("/api/browser/sessions/{pid}/{portal}/close",async(string pid,string portal,HttpContext ctx,Store store,PortalSessions sessions)=>{var v=await ProfessionalViewer(pid,portal,ctx,store);return Results.Ok(await sessions.CloseProfessional(pid,portal,v.User.Id,v.Lease));}).RequireAuthorization();
app.MapGet("/api/integrations/evolution/chats", async (HttpContext ctx, Store store, EvolutionClient evo) =>
{
    var u = await Current(ctx, store); if (u.Role is "Aluno" or "Instrutor") throw new RuleException("Acesso restrito à equipe.", 403);
    return Results.Ok(await evo.Chats());
}).RequireAuthorization();
app.MapPost("/api/integrations/evolution/messages", async (HttpContext ctx, Store store, EvolutionClient evo, JsonElement body) =>
{
    var u = await Current(ctx, store); if (u.Role is "Aluno" or "Instrutor") throw new RuleException("Acesso restrito à equipe.", 403);
    var jid = body.GetProperty("jid").GetString() ?? "";
    if (!System.Text.RegularExpressions.Regex.IsMatch(jid, @"^[0-9\-]+@(s\.whatsapp\.net|g\.us|lid)$")) throw new RuleException("Conversa inválida.");
    var page = body.TryGetProperty("page", out var pageValue) && pageValue.TryGetInt32(out var requestedPage) ? requestedPage : 1;
    if (page is < 1 or > 10000) throw new RuleException("Página de histórico inválida.");
    return Results.Ok(await evo.Messages(jid, page));
}).RequireAuthorization();
app.MapPost("/api/integrations/evolution/connect", async (HttpContext ctx, Store store, EvolutionClient evo) =>
{
    var u = await Current(ctx, store); if (u.Role is not ("Administrador" or "Gerente")) throw new RuleException("Somente gerente ou administrador conecta a conta.", 403);
    return Results.Ok(await evo.Connect());
}).RequireAuthorization();
app.MapPost("/api/integrations/evolution/send", async (HttpContext ctx, Store store, EvolutionClient evo, JsonElement body) =>
{
    var u = await Current(ctx, store); if (u.Role is "Aluno" or "Instrutor") throw new RuleException("Acesso restrito à equipe.", 403);
    if (!evo.Configured) throw new RuleException("Configure o servidor Evolution antes de enviar mensagens.", 503);
    var phone = new string((body.GetProperty("phone").GetString() ?? "").Where(char.IsAsciiDigit).ToArray()); var text = body.GetProperty("text").GetString() ?? "";
    if (phone.Length is < 12 or > 15 || text.Length is < 1 or > 4000) throw new RuleException("Informe telefone com país/DDD e mensagem de até 4.000 caracteres.");
    var messageId = body.GetProperty("messageId").GetString() ?? "";
    var op = ctx.Request.Headers["X-Operation-Id"].ToString();
    if (!Guid.TryParse(op, out _)) throw new RuleException("Operação sem identificador válido.");
    var reserved = false;
    await store.Mutate(u.Id, op + ":reserve", messageId, s =>
    {
        var msg = s.Messages.Find(x => x.Id == messageId) ?? throw new RuleException("Prepare o rascunho antes de enviar.");
        if (msg.Status != "Rascunho") throw new RuleException("Esta mensagem já foi enviada ou está em conferência. Verifique na Evolution antes de repetir.", 409);
        if (msg.Phone != phone || msg.Text != text) throw new RuleException("A mensagem mudou. Revise o rascunho.", 409);
        msg.Status = "Em envio";
        reserved = true;
        s.Audit.Add(new() { Id = Operations.Id(), User = u.Name, Action = "message-send", Detail = messageId, Date = Operations.Now.ToString("s") });
        return new { ok = true };
    });
    // A replay never repeats the external call, including concurrent requests and restarts.
    if (!reserved) return Results.Ok(new { status = (await store.Read()).Messages.Find(x => x.Id == messageId)?.Status });
    try
    {
        var result = await evo.Send(phone, text);
        await store.Mutate(u.Id, op + ":finished", messageId, s => { s.Messages.Find(x => x.Id == messageId)!.Status = "Aceita pela Evolution"; return new { ok = true }; });
        return Results.Ok(result);
    }
    catch (RuleException ex)
    {
        await store.Mutate(u.Id, op + ":finished", messageId, s => { s.Messages.Find(x => x.Id == messageId)!.Status = ex.Status == 504 ? "Envio incerto — conferir" : "Falha — conferir"; return new { ok = true }; });
        throw;
    }
}).RequireAuthorization();
app.MapGet("/api/finance/students/{sid}",async(string sid,int? page,HttpContext ctx,Store store,FinanceService finance,StudentResources resources,CommunityPortal community)=>{var u=await Current(ctx,store);if(u.Role=="Aluno"&&!community.Policy().StudentFinance)throw new RuleException("Consulta financeira desabilitada pela escola.",403);if(u.Role=="Instrutor")throw new RuleException("Acesso restrito.",403);var target=await resources.Authorize(u,sid,true);if(u.Role!="Aluno"&&u.UnitId!=""&&u.UnitId!=target.UnitId)throw new RuleException("Unidade fora do seu acesso.",403);return Results.Ok(await finance.Student(sid,page??1));}).RequireAuthorization();
app.MapGet("/api/finance/summary",async(HttpContext ctx,Store store,FinanceService finance)=>{var u=await Current(ctx,store);if(u.Role is "Aluno" or "Instrutor")throw new RuleException("Acesso restrito à equipe.",403);var q=ctx.Request.Query;var unit=q["unit"].ToString();if(u.UnitId!=""){if(unit!=""&&unit!=u.UnitId)throw new RuleException("Unidade fora do seu acesso.",403);unit=u.UnitId;}if(!DateOnly.TryParseExact(q["from"],"yyyy-MM-dd",out var from)||!DateOnly.TryParseExact(q["to"],"yyyy-MM-dd",out var to)||to.DayNumber-from.DayNumber is <0 or >366)throw new RuleException("Selecione um período de até um ano.");return Results.Ok(await finance.General(unit,from.ToString("yyyy-MM-dd"),to.AddDays(1).ToString("yyyy-MM-dd"),int.TryParse(q["page"],out var n)?Math.Clamp(n,1,100000):1));}).RequireAuthorization();
app.MapGet("/api/finance/ledger",async(HttpContext ctx,Store store)=>{var u=await Current(ctx,store);if(u.Role is "Aluno" or "Instrutor")throw new RuleException("Acesso restrito à equipe.",403);var q=ctx.Request.Query;if(!DateOnly.TryParseExact(q["from"],"yyyy-MM-dd",out var from)||!DateOnly.TryParseExact(q["to"],"yyyy-MM-dd",out var to)||to.DayNumber-from.DayNumber is <0 or >366)throw new RuleException("Selecione um período de até um ano.");var unit=q["unit"].ToString();if(u.UnitId!=""){if(unit!=""&&unit!=u.UnitId)throw new RuleException("Unidade fora do seu acesso.",403);unit=u.UnitId;}return Results.Ok(store.Ledger(unit,from.ToString("yyyy-MM-dd"),to.AddDays(1).ToString("yyyy-MM-dd"),int.TryParse(q["page"],out var n)?Math.Clamp(n,1,100000):1));}).RequireAuthorization();
app.MapGet("/api/legacy/catalog", async (HttpContext ctx, Store store, LegacyArchive archive) => { var u=await Current(ctx,store); if(u.Role is "Aluno" or "Instrutor")throw new RuleException("Acesso restrito à equipe.",403); return Results.Ok(archive.Catalog()); }).RequireAuthorization();
app.MapGet("/api/legacy/tables/{table}", async (string table, HttpContext ctx, Store store, LegacyArchive archive) => { var u=await Current(ctx,store);if(u.Role is "Aluno" or "Instrutor")throw new RuleException("Acesso restrito à equipe.",403);var q=ctx.Request.Query;return Results.Ok(archive.Table(table,int.TryParse(q["page"],out var n)?Math.Clamp(n,1,100000):1,q["q"].ToString(),q["studentId"].ToString(),q["unit"].ToString())); }).RequireAuthorization();
app.MapGet("/api/legacy/issues",async (HttpContext ctx,Store store,LegacyArchive archive)=>{var u=await Current(ctx,store);if(u.Role is not ("Administrador" or "Gerente"))throw new RuleException("Acesso restrito à gestão.",403);return Results.Ok(archive.Issues(int.TryParse(ctx.Request.Query["page"],out var n)?Math.Clamp(n,1,100000):1));}).RequireAuthorization();
app.MapGet("/api/legacy/theory",async(HttpContext ctx,Store store,ImportedModules modules)=>{var user=await Current(ctx,store);if(user.Role is "Aluno" or "Instrutor")throw new RuleException("Acesso restrito à equipe.",403);var q=ctx.Request.Query;return Results.Ok(modules.Theory(q["studentId"].ToString(),q["unit"].ToString(),int.TryParse(q["page"],out var n)?Math.Clamp(n,1,100000):1));}).RequireAuthorization();
app.MapGet("/api/legacy/journey",async(HttpContext ctx,Store store,ImportedModules modules)=>{var user=await Current(ctx,store);if(user.Role is "Aluno" or "Instrutor")throw new RuleException("Acesso restrito à equipe.",403);return Results.Ok(modules.Journey(ctx.Request.Query["studentId"].ToString()));}).RequireAuthorization();
app.MapGet("/api/integrations/devices",async(HttpContext ctx,Store store)=>{var user=await Current(ctx,store);if(user.Role is not ("Administrador" or "Gerente"))throw new RuleException("Diagnóstico disponível à gestão.",403);var address=ctx.Connection.RemoteIpAddress;if(address==null||!System.Net.IPAddress.IsLoopback(address))throw new RuleException("Execute o diagnóstico no computador que tem os leitores conectados.",403);return Results.Ok(DeviceDiagnostics.Read());}).RequireAuthorization();
app.MapGet("/api/automation/catalog",async(HttpContext ctx,Store store,AutomationService service)=>Results.Ok(service.Catalog(await Current(ctx,store)))).RequireAuthorization();
app.MapGet("/api/agenda/review",async(HttpContext ctx,Store store,AgendaReview review)=>{var user=await Current(ctx,store);if(user.Role is "Aluno" or "Instrutor")throw new RuleException("Auditoria disponível à equipe.",403);var q=ctx.Request.Query;return Results.Ok(review.Page(q["unit"].ToString(),q["from"].ToString(),q["to"].ToString(),int.TryParse(q["page"],out var n)?Math.Clamp(n,1,100000):1));}).RequireAuthorization();
app.MapGet("/api/automation/jobs/{id}",async(string id,HttpContext ctx,Store store,AutomationService service)=>Results.Ok(service.Job(id,await Current(ctx,store)))).RequireAuthorization();
app.MapPost("/api/automation/prepare",async(HttpContext ctx,Store store,AutomationService service,JsonElement body)=>Results.Ok(service.Prepare(await Current(ctx,store),body,ctx.Request.Headers["X-Operation-Id"].ToString()))).RequireAuthorization();
app.MapPost("/api/automation/jobs/{id}/open",async(string id,HttpContext ctx,Store store,AutomationService service)=>Results.Ok(await service.Open(id,await Current(ctx,store),ctx.Connection.RemoteIpAddress))).RequireAuthorization();
app.MapPost("/api/automation/jobs/{id}/capture",async(string id,HttpContext ctx,Store store,AutomationService service)=>Results.Ok(await service.Capture(id,await Current(ctx,store)))).RequireAuthorization();
app.MapGet("/api/automation/jobs/{id}/evidence",async(string id,HttpContext ctx,Store store,AutomationService service)=>Results.Ok(service.Evidence(id,await Current(ctx,store)))).RequireAuthorization();
app.MapPost("/api/automation/jobs/{id}/record",async(string id,HttpContext ctx,Store store,AutomationService service,JsonElement body)=>Results.Ok(service.Record(id,await Current(ctx,store),body))).RequireAuthorization();
app.MapGet("/api/community/me",async(HttpContext ctx,Store store,CommunityPortal service)=>
{
    var u=await Current(ctx,store);var role=ctx.Request.Query["role"].ToString();var id=ctx.Request.Query["id"].ToString();
    if(u.Role is "Administrador" or "Gerente" or "Atendente"&&role==""&&id=="")return Results.Ok(new{team=true,role=u.Role});
    return Results.Ok(await service.Snapshot(u,role,id));
}).RequireAuthorization();
app.MapGet("/api/community/settings",async(HttpContext ctx,Store store,CommunityPortal service)=>Results.Ok(service.Settings(await Current(ctx,store)))).RequireAuthorization();
app.MapPost("/api/community/settings",async(HttpContext ctx,Store store,CommunityPortal service,JsonElement body)=>Results.Ok(service.Configure(await Current(ctx,store),body))).RequireAuthorization();
app.MapGet("/api/community/requests",async(HttpContext ctx,Store store,CommunityPortal service)=>Results.Ok(service.Inbox(await Current(ctx,store),ctx.Request.Query["unit"].ToString()))).RequireAuthorization();
app.MapPost("/api/community/requests",async(HttpContext ctx,Store store,CommunityPortal service,JsonElement body)=>Results.Content(await service.Request(await Current(ctx,store),body,ctx.Request.Headers["X-Operation-Id"].ToString()),"application/json")).RequireAuthorization();
app.MapPost("/api/community/requests/{id}/reply",async(string id,HttpContext ctx,Store store,CommunityPortal service,JsonElement body)=>Results.Content(service.Reply(await Current(ctx,store),id,body,ctx.Request.Headers["X-Operation-Id"].ToString()),"application/json")).RequireAuthorization();
app.MapPost("/api/community/lessons/{id}/assessment",async(string id,HttpContext ctx,Store store,CommunityPortal service,JsonElement body)=>Results.Content(await service.Assess(await Current(ctx,store),id,body,ctx.Request.Headers["X-Operation-Id"].ToString()),"application/json")).RequireAuthorization();
app.MapPost("/api/community/lessons/{id}/attendance",async(string id,HttpContext ctx,Store store,CommunityPortal service,JsonElement body)=>Results.Content(await service.Attendance(await Current(ctx,store),id,body,ctx.Request.Headers["X-Operation-Id"].ToString()),"application/json")).RequireAuthorization();
app.MapPost("/api/community/files",async(HttpContext ctx,Store store,CommunityPortal service)=>
{
    var u=await Current(ctx,store);var form=await ctx.Request.ReadFormAsync();var file=form.Files.GetFile("file")??throw new RuleException("Escolha o arquivo.");
    return Results.Ok(await service.Upload(u,file,form["title"].ToString(),form["category"].ToString(),ctx.Request.Headers["X-Operation-Id"].ToString()));
}).RequireAuthorization().DisableAntiforgery();
app.MapGet("/api/community/documents/{id}",async(string id,HttpContext ctx,Store store,CommunityPortal service)=>
{
    var d=await service.Document(await Current(ctx,store),id,ctx.Request.Query["studentId"].ToString());return Results.File(System.Text.Encoding.UTF8.GetBytes(d.Name+"\n\n"+d.Text),"text/plain; charset=utf-8","documento-"+d.Id.Replace("/","")+".txt");
}).RequireAuthorization();

app.MapGet("/portal",(HttpContext ctx)=>Results.Redirect("/portal/index.html"+ctx.Request.QueryString));
// Give each public entry point its own shell when endpoint routing selects a fallback.
foreach(var entry in new[]{"install","account","portal","platform"})app.MapFallbackToFile("/"+entry+"/{*path:nonfile}",entry+"/index.html",shellStaticFiles);
app.MapFallbackToFile("index.html",shellStaticFiles);
app.Run();

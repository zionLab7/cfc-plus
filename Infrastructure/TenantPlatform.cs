using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
namespace CfcPilot;

public sealed class SchoolTenant
{
 public string Id{get;set;}="";public string Name{get;set;}="";public bool Active{get;set;}=true;public string Created{get;set;}="";
 // Only a trusted server operator can adopt a pre-existing data directory.
 public string LegacyRoot{get;set;}="";
}
public sealed class PlatformCredential {public string Login{get;set;}="platform-admin";public string PasswordHash{get;set;}="";public bool MustChange{get;set;}=true;}
public sealed class TenantPlatform(IConfiguration config,IWebHostEnvironment env,ILoggerFactory logs):IAsyncDisposable
{
 readonly SemaphoreSlim gate=new(1);readonly ConcurrentDictionary<string,Lazy<TenantRuntime>> runtimes=new();
 public static bool Enabled(IConfiguration c)=>c["CFC_MULTI_TENANT"]=="true";
 public string Root=>Path.GetFullPath(config["CFC_DATA_DIR"]??Path.Combine(env.ContentRootPath,"App_Data_Central"));
 string Registry=>Path.Combine(Root,"platform-schools.json");string Credential=>Path.Combine(Root,"platform-admin.json");
 public void Prepare(){Directory.CreateDirectory(Root);if(!File.Exists(Registry))CommercialInstallation.Write(Registry,Array.Empty<SchoolTenant>());if(!File.Exists(Credential)){var pass=Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));CommercialInstallation.Write(Credential,new PlatformCredential{PasswordHash=Passwords.Hash(pass)});CommercialInstallation.Write(Path.Combine(Root,"platform-first-access.json"),new{login="platform-admin",password=pass});}}
 public List<SchoolTenant> Schools()=>JsonSerializer.Deserialize<List<SchoolTenant>>(File.ReadAllText(Registry),Store.Json)??[];
 public static string Normalize(string id){id=id.Trim().ToLowerInvariant();if(!System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-z][a-z0-9-]{2,47}$"))throw new RuleException("Informe um ID de autoescola válido.",401);return id;}
 public TenantRuntime Get(string id){id=Normalize(id);var school=Schools().Find(x=>x.Id==id&&x.Active)??throw new RuleException("ID da escola ou acesso inválido.",401);var entry=runtimes.GetOrAdd(id,_=>new(()=>new TenantRuntime(config,env,logs,school,Root),LazyThreadSafetyMode.ExecutionAndPublication));try{return entry.Value;}catch{((ICollection<KeyValuePair<string,Lazy<TenantRuntime>>>)runtimes).Remove(new(id,entry));throw;}}
 public async Task<object> Register(string id,string name,string legacyRoot="")
 {
 id=Normalize(id);name=name.Trim();if(name.Length is <3 or >120)throw new RuleException("Informe o nome da autoescola.");await gate.WaitAsync();try{var schools=Schools();if(schools.Any(s=>s.Id==id))throw new RuleException("Este ID de autoescola já está cadastrado.",409);
 if(legacyRoot!=""){legacyRoot=Path.GetFullPath(legacyRoot);if(!File.Exists(Path.Combine(legacyRoot,"active-database.json")))throw new RuleException("Adoção exige uma base já ativada.");if(schools.Any(s=>s.LegacyRoot==legacyRoot))throw new RuleException("Essa base já está vinculada a outra escola.",409);}
 var school=new SchoolTenant{Id=id,Name=name,Created=DateTimeOffset.UtcNow.ToString("O"),LegacyRoot=legacyRoot};var runtime=new TenantRuntime(config,env,logs,school,Root);schools.Add(school);try{CommercialInstallation.Write(Registry,schools);}catch{await runtime.DisposeAsync();throw;}var ready=new Lazy<TenantRuntime>(()=>runtime);_ = ready.Value;runtimes[id]=ready;return new{schoolId=id,schoolName=name,initialAccessFile=legacyRoot==""?"tenants/"+id+"/first-access.json":"Acesso existente preservado"};
 }finally{gate.Release();}}
 public PlatformCredential Account()=>JsonSerializer.Deserialize<PlatformCredential>(File.ReadAllText(Credential),Store.Json)!;
 public void Authorize(HttpContext ctx){var a=Account();if(ctx.User.FindFirstValue("cfc-platform")!="owner"||ctx.User.FindFirstValue("cfc-stamp")!=Stamp(a.PasswordHash))throw new RuleException("Entre como administrador da plataforma.",401);}
 public static string Stamp(string hash)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash)));
 public async Task ChangePassword(HttpContext ctx,string old,string password){Authorize(ctx);if(password.Length is <12 or >128||password==old)throw new RuleException("Escolha uma nova senha de 12 a 128 caracteres.");await gate.WaitAsync();try{var a=Account();if(!Passwords.Verify(old,a.PasswordHash))throw new RuleException("Senha atual incorreta.",401);a.PasswordHash=Passwords.Hash(password);a.MustChange=false;CommercialInstallation.Write(Credential,a);File.Delete(Path.Combine(Root,"platform-first-access.json"));}finally{gate.Release();}}
 public async Task Reload(string id){if(runtimes.TryRemove(id,out var old)&&old.IsValueCreated)await old.Value.DisposeAsync();Get(id);}
 public async ValueTask DisposeAsync(){foreach(var r in runtimes.Values.Where(x=>x.IsValueCreated))await r.Value.DisposeAsync();}
}
public sealed class TenantRuntime:IAsyncDisposable
{
 public ServiceProvider Services{get;}public string SchoolName{get;}public string Root{get;}readonly List<BackgroundService> workers=[];readonly IConfigurationRoot configuration;
 public TenantRuntime(IConfiguration parent,IWebHostEnvironment env,ILoggerFactory logs,SchoolTenant school,string platformRoot)
 {
 SchoolName=school.Name;Root=school.LegacyRoot!=""?school.LegacyRoot:Path.Combine(platformRoot,"tenants",school.Id);
 var values=parent.AsEnumerable().ToDictionary(x=>x.Key,x=>x.Value);values["CFC_DATA_DIR"]=Root;values["CFC_SCHOOL_ID"]=school.Id;values["CFC_SCHOOL_NAME"]=school.Name;values["CFC_INSTALLATION_MODE"]="commercial";values["Evolution:BaseUrl"]="";values["Evolution:Instance"]="";values["Evolution:ApiKey"]="";values["CFC_ADMIN_PASSWORD_FILE"]="";
 var c=new ConfigurationBuilder().AddInMemoryCollection(values).AddJsonFile(Path.Combine(Root,"evolution.config.json"),true,true).AddJsonFile(Path.Combine(Root,"native-portal-settings.json"),true,false).Build();
 configuration=c;CommercialInstallation.Prepare(c,env);var services=new ServiceCollection();services.AddSingleton<IConfiguration>(c);services.AddSingleton(env);services.AddSingleton<IWebHostEnvironment>(env);services.AddSingleton(logs);services.AddLogging();
 var dp=services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Root,"keys"))).SetApplicationName(school.LegacyRoot!=""?"CfcPilot":"CfcPilot:school:"+school.Id);if(OperatingSystem.IsWindows())dp.ProtectKeysWithDpapi();
 services.AddSingleton<Store>();services.AddSingleton<FinanceService>();services.AddSingleton<ImportQueue>();services.AddSingleton<BrowserRuntime>();services.AddSingleton<NativePortalSessions>();services.AddSingleton<ProfessionalBrowser>();services.AddSingleton<IPortalWindowLauncher,PortalWindowLauncher>();services.AddSingleton<ManualPortalBrowser>();services.AddSingleton<LegacyArchive>();services.AddSingleton<ImportedModules>();services.AddSingleton<PortalSessions>();services.AddSingleton<ProfessionalSessionRegistry>();services.AddSingleton<ProfessionalCookieVault>();services.AddSingleton<ProfessionalSessionRestorer>();services.AddSingleton<AutomationService>();services.AddSingleton<AgendaReview>();services.AddSingleton<StudentResources>();services.AddSingleton<CommunityPortal>();services.AddHttpClient<EvolutionClient>(h=>h.Timeout=TimeSpan.FromSeconds(20));
 Services=services.BuildServiceProvider();workers.Add(Services.GetRequiredService<ImportQueue>());workers.Add(Services.GetRequiredService<ProfessionalSessionRestorer>());foreach(var worker in workers)worker.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
 }
 public async ValueTask DisposeAsync(){foreach(var w in workers)await w.StopAsync(CancellationToken.None);await Services.DisposeAsync();(configuration as IDisposable)?.Dispose();}
}
// Child singleton services remain owned by their school, never by the request scope.
public sealed class TenantRequestServices(IServiceProvider original,IServiceProvider school):IServiceProvider
{
 static readonly HashSet<Type> Types=[typeof(FinanceService),typeof(Store),typeof(ImportQueue),typeof(BrowserRuntime),typeof(NativePortalSessions),typeof(ProfessionalBrowser),typeof(IPortalWindowLauncher),typeof(ManualPortalBrowser),typeof(LegacyArchive),typeof(ImportedModules),typeof(PortalSessions),typeof(ProfessionalSessionRegistry),typeof(ProfessionalCookieVault),typeof(AutomationService),typeof(AgendaReview),typeof(StudentResources),typeof(CommunityPortal),typeof(EvolutionClient)];
 public object? GetService(Type type)=>Types.Contains(type)?school.GetService(type):original.GetService(type);
}
public sealed class TenantWorkers(TenantPlatform platform,ILogger<TenantWorkers> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct){foreach(var s in platform.Schools().Where(s=>s.Active)){try{platform.Get(s.Id);}catch(Exception ex)when(ex is IOException or InvalidDataException or InvalidOperationException){logger.LogError("Escola {School} não pôde iniciar: {Type}",s.Id,ex.GetType().Name);}}await Task.Delay(Timeout.Infinite,ct);}
}

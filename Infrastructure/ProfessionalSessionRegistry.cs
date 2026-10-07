using System.Text.Json;

namespace CfcPilot;

public record ProfessionalSessionPreference(string ProfileId,string Portal,bool Restore,string Updated);

// Only public portal keys and profile IDs are stored here. Authentication stays
// in Chromium's private profile, never in this registry or the desktop client.
public sealed class ProfessionalSessionRegistry(IConfiguration config,IWebHostEnvironment env)
{
    readonly object gate=new();
    string PathName=>Path.Combine(config["CFC_DATA_DIR"]??Path.Combine(env.ContentRootPath,"App_Data"),"professional-sessions.json");
    public ProfessionalSessionPreference[] Read()
    {
        lock(gate)
        {
            if(!File.Exists(PathName))return [];
            return (JsonSerializer.Deserialize<ProfessionalSessionPreference[]>(File.ReadAllText(PathName),Store.Json)??[]).Where(x=>Guid.TryParseExact(x.ProfileId,"N",out _)&&x.Portal is "ecnh" or "detran").ToArray();
        }
    }
    public void Set(string profileId,string portal,bool restore)
    {
        if(!Guid.TryParseExact(profileId,"N",out _))throw new RuleException("Perfil inválido.");
        ProfessionalBrowser.Portal(portal);
        lock(gate)
        {
            var existing=Read();
            // Restoring an existing browser must not rewrite this file every minute.
            if(existing.Any(x=>x.ProfileId==profileId&&x.Portal==portal&&x.Restore==restore))return;
            var rows=existing.Where(x=>x.ProfileId!=profileId||x.Portal!=portal).ToList();
            rows.Add(new(profileId,portal,restore,DateTimeOffset.UtcNow.ToString("O")));
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            ReplaceFile(PathName,JsonSerializer.Serialize(rows,Store.Json));
        }
    }
    internal static void ReplaceFile(string path,string contents,Action<string,string>? replace=null)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temp,contents);
            for(var attempt=0;;attempt++)
            {
                try{if(replace==null)File.Move(temp,path,true);else replace(temp,path);return;}
                catch(Exception ex) when(attempt<3&&ex is IOException or UnauthorizedAccessException){Thread.Sleep(75*(attempt+1));}
            }
        }
        finally
        {
            try{File.Delete(temp);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){/* Never delete or truncate the existing registry. */}
        }
    }
}

public sealed class PortalControl(TimeProvider? clock=null)
{
    readonly TimeProvider time=clock??TimeProvider.System;
    string owner="",token="";
    DateTimeOffset until;
    public bool Held=>token!=""&&until>time.GetUtcNow();
    public string Owner=>Held?owner:"";
    public string Acquire(string userId)
    {
        if(string.IsNullOrEmpty(userId))throw new RuleException("Entre no aplicativo para controlar o navegador.",401);
        if(Held&&owner!=userId)throw new RuleException("Este navegador está em atendimento por outro usuário. Ao fechar a visualização, o controle fica disponível sem encerrar a sessão do portal.",409);
        owner=userId;token=Guid.NewGuid().ToString("N");until=time.GetUtcNow().AddSeconds(45);return token;
    }
    public void Check(string userId,string lease,bool renew=true)
    {
        if(!Held||owner!=userId||lease==""||token!=lease)throw new RuleException("O controle dessa visualização terminou. Reabra a sessão pela central; o navegador do servidor foi preservado.",409);
        if(renew)until=time.GetUtcNow().AddSeconds(45);
    }
    public void Release(string userId,string lease){Check(userId,lease,false);owner="";token="";until=default;}
}

public sealed class ProfessionalSessionRestorer(Store store,PortalSessions sessions,ProfessionalSessionRegistry registry,ILogger<ProfessionalSessionRestorer> logger) : BackgroundService
{
    internal static bool Recoverable(Exception ex)=>ex is IOException or UnauthorizedAccessException or JsonException or RuleException;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // This recreates closed browser contexts; it never performs synthetic
        // portal activity, retries a login, or defeats the portal's expiry policy.
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var profiles=await store.Profiles();
                foreach(var desired in registry.Read().Where(x=>x.Restore))
                {
                    if(stoppingToken.IsCancellationRequested)break;
                    var profile=profiles.Find(x=>x.Id==desired.ProfileId&&x.Kind is "Diretor" or "Instrutor");if(profile==null)continue;
                    try{await sessions.RestoreProfessional(profile,desired.Portal);}
                    catch(Exception ex) when(Recoverable(ex)){logger.LogWarning("Restauração do navegador {Profile}/{Portal} adiada ({Type}); o servidor permanece disponível.",desired.ProfileId,desired.Portal,ex.GetType().Name);}
                }
            }
            catch(Exception ex) when(Recoverable(ex)){logger.LogWarning("A central de navegadores precisa de atenção: {Type}; nova tentativa em 60 segundos, sem encerrar o servidor.",ex.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromSeconds(60),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
}

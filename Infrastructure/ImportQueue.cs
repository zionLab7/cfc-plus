using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace CfcPilot;

public sealed class ImportUpload
{
    public string Id{get;set;}="";public string State{get;set;}="Enviando";public string Name{get;set;}="";public string Owner{get;set;}="";public long Bytes{get;set;}public long Offset{get;set;}
    public string Message{get;set;}="";public string Created{get;set;}="";public string ImportId{get;set;}="";public long Rows{get;set;}public long Issues{get;set;}public int Tables{get;set;}
}
public sealed class ImportQueue(Store store,IConfiguration config,IWebHostEnvironment env,ILogger<ImportQueue> logger):BackgroundService
{
    readonly SemaphoreSlim gate=new(1);string Root=>CommercialInstallation.Root(config,env);string Jobs=>Path.Combine(Root,"import-queue");
    public const long MaxZip=512L*1024*1024,MaxExpanded=8L*1024*1024*1024,MaxFile=512L*1024*1024;public const int ChunkBytes=4*1024*1024;
    bool maintenance;public bool Maintenance=>maintenance;
    string Folder(string id){if(!Guid.TryParseExact(id,"N",out _))throw new RuleException("Importação inválida.");return Path.Combine(Jobs,id);}
    string StatusPath(string id)=>Path.Combine(Folder(id),"status.json");
    ImportUpload Read(string id)=>File.Exists(StatusPath(id))?JsonSerializer.Deserialize<ImportUpload>(File.ReadAllText(StatusPath(id)),Store.Json)!:throw new RuleException("Importação não encontrada.",404);
    void Save(ImportUpload j)=>CommercialInstallation.Write(StatusPath(j.Id),j);
    public object List(User u){CommercialInstallation.RequireAdmin(u);Directory.CreateDirectory(Jobs);return new{empty=CommercialInstallation.Empty(store),maxBytes=MaxZip,chunkBytes=ChunkBytes,jobs=Directory.GetDirectories(Jobs).Where(d=>File.Exists(Path.Combine(d,"status.json"))).Select(d=>Read(Path.GetFileName(d))).OrderByDescending(j=>j.Created).Take(20)};}
    public async Task<ImportUpload> Create(User u,string name,long bytes)
    {
        CommercialInstallation.RequireAdmin(u);if(!name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)||bytes is <=0 or >MaxZip)throw new RuleException("Envie um ZIP de até 512 MB.");
        await gate.WaitAsync();try{Directory.CreateDirectory(Jobs);var existing=Directory.GetDirectories(Jobs).Where(d=>File.Exists(Path.Combine(d,"status.json"))).Select(d=>Read(Path.GetFileName(d))).ToArray();
        if(existing.Any(x=>x.State is "Enviando" or "Na fila" or "Conferindo"))throw new RuleException("Conclua ou descarte a importação anterior antes de enviar outra.",409);
        if(new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace<MaxExpanded+MaxZip)throw new RuleException("Reserve pelo menos 9 GB livres para conferir o pacote sem afetar a base ativa.",409);
        var j=new ImportUpload{Id=Guid.NewGuid().ToString("N"),Owner=u.Id,Name=Path.GetFileName(name)[..Math.Min(Path.GetFileName(name).Length,150)],Bytes=bytes,Created=DateTimeOffset.UtcNow.ToString("O")};Directory.CreateDirectory(Folder(j.Id));Save(j);return j;
        }finally{gate.Release();}
    }
    public async Task<ImportUpload> Append(User u,string id,long offset,Stream input,long? length)
    {
        CommercialInstallation.RequireAdmin(u);await gate.WaitAsync();try{var j=Read(id);if(j.State!="Enviando"||offset!=j.Offset||length is null or <=0 or >ChunkBytes||j.Offset+length>j.Bytes)throw new RuleException("Trecho inválido. Atualize o envio para retomar do último ponto confirmado.",409);
        var file=Path.Combine(Folder(id),"package.zip");await using var output=new FileStream(file,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None,65536,true);output.SetLength(j.Offset);output.Position=j.Offset;
        var buffer=new byte[65536];long copied=0;try{while(true){var n=await input.ReadAsync(buffer);if(n==0)break;copied+=n;if(copied>length)throw new RuleException("Trecho maior que o informado.");await output.WriteAsync(buffer.AsMemory(0,n));}if(copied!=length)throw new RuleException("Envio interrompido. Retome o trecho.",409);await output.FlushAsync();}catch{output.SetLength(j.Offset);throw;}j.Offset+=copied;Save(j);return j;
        }finally{gate.Release();}
    }
    public async Task<ImportUpload> Enqueue(User u,string id)
    {
        CommercialInstallation.RequireAdmin(u);await gate.WaitAsync();try{var j=Read(id);if(j.State=="Na fila"||j.State=="Conferindo"||j.State=="Pronta")return j;if(j.State!="Enviando"||j.Offset!=j.Bytes)throw new RuleException("Conclua o envio antes da conferência.",409);j.State="Na fila";j.Message="Aguardando conferência em processo separado.";Save(j);return j;}finally{gate.Release();}
    }
    public async Task<ImportUpload> Discard(User u,string id)
    {
        CommercialInstallation.RequireAdmin(u);await gate.WaitAsync();try{var j=Read(id);if(j.State is "Conferindo" or "Ativada" or "Ativando")throw new RuleException("Esta importação não pode ser descartada agora.",409);j.State="Descartada";j.Message="Pacote preservado para recuperação; não será ativado.";Save(j);return j;}finally{gate.Release();}
    }
    public static async Task Extract(string zip,string target,CancellationToken ct)
    {
        using var archive=ZipFile.OpenRead(zip);if(archive.Entries.Count>4096)throw new InvalidDataException("ZIP com arquivos demais.");long total=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int extracted=0;
        foreach(var entry in archive.Entries){ct.ThrowIfCancellationRequested();var raw=entry.FullName.Replace('\\','/');if(raw.StartsWith('/')||raw.Split('/').Any(s=>s is ".." or "."||s.Contains(':'))||(entry.ExternalAttributes>>16&0xF000)==0xA000)throw new InvalidDataException("ZIP contém caminho ou link não permitido.");
            var segments=raw.Split('/');var marker=Array.IndexOf(segments,"01_BANCO_DE_DADOS_COMPLETO");if(marker<0)continue;var relative=string.Join('/',segments.Skip(marker));if(relative.EndsWith('/'))continue;
            var ext=Path.GetExtension(relative).ToLowerInvariant();if(ext is not(".sql" or ".csv" or ".md" or ".json"))throw new InvalidDataException("O pacote de dados contém um formato não permitido.");
            if(!seen.Add(relative)||entry.Length>MaxFile||(total+=entry.Length)>MaxExpanded)throw new InvalidDataException("ZIP duplicado ou acima do limite de extração.");
            var full=Path.GetFullPath(Path.Combine(target,relative));if(!full.StartsWith(Path.GetFullPath(target)+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new InvalidDataException("Caminho fora do staging.");Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await using var src=entry.Open();await using var dest=new FileStream(full,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);var buffer=new byte[65536];long count=0;while(true){var n=await src.ReadAsync(buffer,ct);if(n==0)break;count+=n;if(count>entry.Length||count>MaxFile)throw new InvalidDataException("Tamanho extraído inválido.");await dest.WriteAsync(buffer.AsMemory(0,n),ct);}if(count!=entry.Length)throw new InvalidDataException("Arquivo incompleto.");extracted++;
        }
        if(extracted==0)throw new InvalidDataException("Inclua a pasta 01_BANCO_DE_DADOS_COMPLETO com SQL, CSV e inventário.");
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(Jobs);foreach(var d in Directory.GetDirectories(Jobs).Where(d=>File.Exists(Path.Combine(d,"status.json")))){var j=Read(Path.GetFileName(d));if(j.State is "Conferindo" or "Ativando"){j.State="Interrompida";j.Message="Conferência interrompida pelo reinício. A base anterior permanece ativa.";Save(j);}}
        while(!stoppingToken.IsCancellationRequested){var j=Directory.GetDirectories(Jobs).Where(d=>File.Exists(Path.Combine(d,"status.json"))).Select(d=>Read(Path.GetFileName(d))).FirstOrDefault(j=>j.State=="Na fila");if(j!=null)await Run(j,stoppingToken);else await Task.Delay(1000,stoppingToken);}
    }
    async Task Run(ImportUpload j,CancellationToken ct)
    {
        await gate.WaitAsync(ct);try{j=Read(j.Id);if(j.State!="Na fila")return;j.State="Conferindo";j.Message="Extraindo e verificando SQL/CSV; sua base ativa continua disponível.";Save(j);}finally{gate.Release();}
        try{var folder=Folder(j.Id);var export=Path.Combine(folder,"source");await Extract(Path.Combine(folder,"package.zip"),export,ct);
            var exe=Environment.ProcessPath!;var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=env.ContentRootPath,RedirectStandardOutput=true,RedirectStandardError=true};if(Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(ImportQueue).Assembly.Location);
            start.ArgumentList.Add("--import-infor");start.ArgumentList.Add(export);start.Environment["CFC_DATA_DIR"]=Root;start.Environment["CFC_INSTALLATION_MODE"]="commercial";
            using var process=Process.Start(start)??throw new InvalidOperationException("Não foi possível iniciar o importador.");using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromHours(2));using var registration=timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
            var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);var stderr=process.StandardError.ReadToEndAsync(timeout.Token);await process.WaitForExitAsync(timeout.Token);var output=await stdout;var error=await stderr;await File.WriteAllTextAsync(Path.Combine(folder,"worker.log"),output+error,CancellationToken.None);if(process.ExitCode!=0)throw new InvalidDataException("Importador recusou o pacote. Consulte o relatório privado worker.log no servidor; a base ativa não foi alterada.");
            using var report=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Root,"import-report.json"),ct));var r=report.RootElement;j.ImportId=r.GetProperty("importId").GetString()!;j.Rows=r.GetProperty("total").GetInt64();j.Tables=r.GetProperty("sourceTables").GetInt32();j.Issues=r.GetProperty("issues").GetInt64();j.State="Pronta";j.Message="Conferência concluída. Revise as pendências antes de ativar.";Save(j);
        }catch(Exception ex)when(ex is IOException or InvalidDataException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException or Microsoft.Data.Sqlite.SqliteException){j.State="Falhou";j.Message=ex is OperationCanceledException?"Importação interrompida ou acima do tempo limite. A base anterior foi preservada.":ex.Message;Save(j);logger.LogWarning("Conferência de importação {Id} recusada: {Type}",j.Id,ex.GetType().Name);}
    }
    public async Task<object> Activate(User u,string id,bool acknowledged)
    {
        CommercialInstallation.RequireAdmin(u);await gate.WaitAsync();try{var j=Read(id);if(!acknowledged||j.State!="Pronta"||!CommercialInstallation.Empty(store))throw new RuleException("Ativação disponível somente em instalação vazia, após revisar o relatório e confirmar. Não substitui dados já operacionais.",409);
        if(!System.Text.RegularExpressions.Regex.IsMatch(j.ImportId,"^[0-9]{8}T[0-9]{6}-[a-f0-9]{8}$"))throw new RuleException("Identificador do pacote inválido.");var dbPath=Path.Combine(Root,"imports",j.ImportId,"cfc.sqlite");
        maintenance=true;j.State="Ativando";Save(j);using(var db=Database.Open(dbPath)){using var tx=db.BeginTransaction();var users=Database.Rows(db,"SELECT json FROM entities WHERE kind='Users'");foreach(var row in users){var user=JsonSerializer.Deserialize<User>(row["json"]!,Database.Compact)!;user.Active=false;Database.SaveEntity(db,"Users",user);}Database.SaveEntity(db,"Users",u);Database.Execute(db,"INSERT INTO metadata(key,value) VALUES('installationAdmin',$0) ON CONFLICT(key) DO UPDATE SET value=$0",u.Id);tx.Commit();}
        var active=Path.Combine(Root,"active-database.json");File.Copy(active,Path.Combine(Folder(id),"previous-active.json"),true);CommercialInstallation.Write(active,new{path=Path.GetRelativePath(Root,dbPath),importId=j.ImportId});j.State="Ativada";j.Message="Base ativada. Reinicie o serviço para abrir a nova base. Usuários importados aguardam revisão e ativação do administrador.";Save(j);return new{restartRequired=true,message=j.Message};
        }catch{maintenance=false;throw;}finally{gate.Release();}
    }
}

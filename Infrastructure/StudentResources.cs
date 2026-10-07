using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CfcPilot;

public record StudentFileInfo(string Id,string StudentId,string Title,string FileName,string Category,string ContentType,long Size,string Created);
public sealed class StudentResources(Store store,IConfiguration config,IWebHostEnvironment env,IDataProtectionProvider protection)
{
    public const int MaxFileBytes=20*1024*1024;
    readonly IDataProtector passwords=protection.CreateProtector("CfcPilot.StudentGovPassword.v1");
    readonly IDataProtector files=protection.CreateProtector("CfcPilot.StudentFiles.v1");
    readonly object gate=new();
    string Root=>Path.GetFullPath(config["CFC_DATA_DIR"]??Path.Combine(env.ContentRootPath,"App_Data"));
    string Dataset { get { if(store.DatabasePath==null)return "pilot";using var db=Database.Open(store.DatabasePath,true);return Database.Scalar(db,"SELECT value FROM metadata WHERE key='importId'"); } }
    Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        Directory.CreateDirectory(Root);var db=Database.Open(Path.Combine(Root,"student-resources.sqlite"));
        Database.Execute(db,"""
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS gov_credentials(dataset TEXT NOT NULL,student_id TEXT NOT NULL,secret TEXT NOT NULL,updated TEXT NOT NULL,user_id TEXT NOT NULL,PRIMARY KEY(dataset,student_id));
        CREATE TABLE IF NOT EXISTS student_files(id TEXT PRIMARY KEY,dataset TEXT NOT NULL,student_id TEXT NOT NULL,title TEXT NOT NULL,file_name TEXT NOT NULL,category TEXT NOT NULL,content_type TEXT NOT NULL,size INTEGER NOT NULL,created TEXT NOT NULL,user_id TEXT NOT NULL,payload BLOB NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_student_files ON student_files(dataset,student_id,created);
        CREATE TABLE IF NOT EXISTS file_requests(dataset TEXT NOT NULL,user_id TEXT NOT NULL,request_id TEXT NOT NULL,fingerprint TEXT NOT NULL,file_id TEXT NOT NULL,PRIMARY KEY(dataset,user_id,request_id));
        CREATE TABLE IF NOT EXISTS resource_audit(id INTEGER PRIMARY KEY,dataset TEXT NOT NULL,student_id TEXT NOT NULL,user_id TEXT NOT NULL,action TEXT NOT NULL,date TEXT NOT NULL);
        """);return db;
    }
    public static void Team(User user){if(user.Role is not ("Administrador" or "Gerente" or "Atendente"))throw new RuleException("Acesso restrito ao atendimento.",403);}
    public async Task<Student> Authorize(User user,string studentId,bool readOwn=false)
    {
        if(!readOwn||user.Role!="Aluno")Team(user);else if(user.LinkedId!=studentId)throw new RuleException("Este documento pertence a outro aluno.",403);
        return await store.StudentById(studentId)??throw new RuleException("Aluno não encontrado.",404);
    }
    void Audit(Microsoft.Data.Sqlite.SqliteConnection db,string dataset,string sid,User user,string action)=>Database.Execute(db,"INSERT INTO resource_audit(dataset,student_id,user_id,action,date) VALUES($0,$1,$2,$3,$4)",dataset,sid,user.Id,action,Operations.Now.ToString("s"));
    public async Task<object> CredentialStatus(User user,string sid)
    {
        await Authorize(user,sid);lock(gate){using var db=Open();var row=Database.Rows(db,"SELECT updated FROM gov_credentials WHERE dataset=$0 AND student_id=$1",Dataset,sid).FirstOrDefault();return new{studentId=sid,hasPassword=row!=null,updated=row?["updated"]??""};}
    }
    public async Task<object> SaveCredential(User user,string sid,string password)
    {
        await Authorize(user,sid);if(string.IsNullOrWhiteSpace(password)||password.Length>256)throw new RuleException("Informe uma senha de até 256 caracteres.");
        var cipher=passwords.CreateProtector(Dataset,sid).Protect(password);lock(gate){using var db=Open();using var tx=db.BeginTransaction();Database.Execute(db,"INSERT INTO gov_credentials VALUES($0,$1,$2,$3,$4) ON CONFLICT(dataset,student_id) DO UPDATE SET secret=excluded.secret,updated=excluded.updated,user_id=excluded.user_id",Dataset,sid,cipher,Operations.Now.ToString("s"),user.Id);Audit(db,Dataset,sid,user,"gov-password-saved");tx.Commit();}return new{studentId=sid,hasPassword=true};
    }
    public async Task<string> Password(User user,string sid,bool reveal)
    {
        await Authorize(user,sid);lock(gate){using var db=Open();var cipher=Database.Scalar(db,"SELECT secret FROM gov_credentials WHERE dataset=$0 AND student_id=$1",Dataset,sid);if(cipher=="")return "";if(reveal)Audit(db,Dataset,sid,user,"gov-password-revealed");try{return passwords.CreateProtector(Dataset,sid).Unprotect(cipher);}catch(CryptographicException){throw new RuleException("Não foi possível abrir a senha protegida neste computador. Confira a conta Windows e as chaves do app.",409);}}
    }
    static StudentFileInfo Info(Dictionary<string,string?> row)=>new(row["id"]!,row["student_id"]!,row["title"]!,row["file_name"]!,row["category"]!,row["content_type"]!,long.Parse(row["size"]!),row["created"]!);
    const string Columns="id,student_id,title,file_name,category,content_type,size,created";
    public async Task<object> List(User user,string sid,int page)
    {
        await Authorize(user,sid,true);lock(gate){using var db=Open();return new{studentId=sid,page,pageSize=50,maxFileBytes=MaxFileBytes,total=Database.Count(db,"SELECT count(*) FROM student_files WHERE dataset=$0 AND student_id=$1",Dataset,sid),rows=Database.Rows(db,"SELECT "+Columns+" FROM student_files WHERE dataset=$0 AND student_id=$1 ORDER BY created DESC,id LIMIT 50 OFFSET $2",Dataset,sid,(page-1)*50).Select(Info).ToArray()};}
    }
    public static string FileType(string fileName,byte[] bytes)
    {
        var ext=Path.GetExtension(fileName).ToLowerInvariant();
        if(ext==".pdf"&&bytes.Length>8&&bytes.AsSpan(0,5).SequenceEqual("%PDF-"u8)&&Encoding.ASCII.GetString(bytes,Math.Max(0,bytes.Length-1024),Math.Min(1024,bytes.Length)).Contains("%%EOF",StringComparison.Ordinal))return "application/pdf";
        if(ext==".png"&&bytes.Length>24&&bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return "image/png";
        if(ext is ".jpg" or ".jpeg"&&bytes.Length>4&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255&&bytes[^2]==255&&bytes[^1]==217)return "image/jpeg";
        if(ext==".webp"&&bytes.Length>16&&bytes.AsSpan(0,4).SequenceEqual("RIFF"u8)&&bytes.AsSpan(8,4).SequenceEqual("WEBP"u8))return "image/webp";
        throw new RuleException("Envie um PDF, JPEG, PNG ou WebP válido. O conteúdo precisa corresponder ao formato do arquivo.");
    }
    public async Task<StudentFileInfo> Upload(User user,string sid,IFormFile file,string title,string category,string requestId,bool allowOwn=false)
    {
        await Authorize(user,sid,allowOwn);if(!Guid.TryParse(requestId,out _))throw new RuleException("Envio sem identificador válido.");
        if(file.Length<1||file.Length>MaxFileBytes)throw new RuleException("Cada arquivo deve ter até 20 MB e não pode estar vazio.",413);
        var filename=file.FileName.Replace('\\','/').Split('/')[^1];filename=new string(filename.Where(c=>!char.IsControl(c)).ToArray());
        if(filename.Length is <1 or >200)throw new RuleException("Nome de arquivo inválido.");title=title.Trim();category=category.Trim();if(title=="")title=filename;if(title.Length>200||category.Length>100)throw new RuleException("Título ou categoria muito longos.");if(category=="")category="Outro";
        await using var memory=new MemoryStream();await file.CopyToAsync(memory);var bytes=memory.ToArray();if(bytes.Length!=file.Length||bytes.Length>MaxFileBytes)throw new RuleException("Tamanho do arquivo inválido.",413);
        var type=FileType(filename,bytes);var fingerprint=Convert.ToHexString(SHA256.HashData(bytes))+JsonSerializer.Serialize(new{sid,filename,title,category});var dataset=Dataset;
        lock(gate)
        {
            using var db=Open();using var tx=db.BeginTransaction();var prior=Database.Rows(db,"SELECT fingerprint,file_id FROM file_requests WHERE dataset=$0 AND user_id=$1 AND request_id=$2",dataset,user.Id,requestId).FirstOrDefault();
            if(prior!=null){if(prior["fingerprint"]!=fingerprint)throw new RuleException("Identificador usado em outro envio.",409);return Info(Database.Rows(db,"SELECT "+Columns+" FROM student_files WHERE id=$0 AND dataset=$1",prior["file_id"],dataset).Single());}
            var id=Guid.NewGuid().ToString("N");var created=Operations.Now.ToString("s");var payload=files.CreateProtector(dataset,sid,id).Protect(bytes);
            Database.Execute(db,"INSERT INTO student_files VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8,$9,$10)",id,dataset,sid,title,filename,category,type,bytes.LongLength,created,user.Id,payload);
            Database.Execute(db,"INSERT INTO file_requests VALUES($0,$1,$2,$3,$4)",dataset,user.Id,requestId,fingerprint,id);Audit(db,dataset,sid,user,"student-file-uploaded");tx.Commit();return new(id,sid,title,filename,category,type,bytes.LongLength,created);
        }
    }
    public async Task<(StudentFileInfo Info,byte[] Bytes)> Read(User user,string id)
    {
        StudentFileInfo info;byte[] payload;var dataset=Dataset;
        lock(gate){using var db=Open();var row=Database.Rows(db,"SELECT "+Columns+" FROM student_files WHERE id=$0 AND dataset=$1",id,dataset).FirstOrDefault()??throw new RuleException("Arquivo não encontrado.",404);info=Info(row);}
        await Authorize(user,info.StudentId,true);
        lock(gate){using var db=Open();using var command=Database.Command(db,"SELECT payload FROM student_files WHERE id=$0 AND dataset=$1",id,dataset);payload=(byte[])command.ExecuteScalar()!;Audit(db,dataset,info.StudentId,user,"student-file-read");}
        try{return(info,files.CreateProtector(dataset,info.StudentId,id).Unprotect(payload));}catch(CryptographicException){throw new RuleException("Não foi possível abrir o arquivo protegido. Confira as chaves do aplicativo.",409);}
    }
}

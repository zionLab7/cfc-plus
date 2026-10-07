using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace CfcPilot;

public sealed class CommunityPolicy
{
    public bool StudentRequests {get;set;}=true;
    public bool StudentUploads {get;set;}=true;
    public bool StudentConfirmation {get;set;}=true;
    public bool StudentFinance {get;set;}=true;
    public bool InstructorRequests {get;set;}=true;
    public bool InstructorAssessment {get;set;}=true;
    public bool InstructorAttendance {get;set;}=true;
}
public sealed class CommunityRequest
{
    public string Id {get;set;}=""; public string Role {get;set;}=""; public string Owner {get;set;}="";
    public string UnitId {get;set;}=""; public string Author {get;set;}=""; public string Name {get;set;}="";
    public string LessonId {get;set;}=""; public string Type {get;set;}=""; public string Message {get;set;}="";
    public string Status {get;set;}="Aberta"; public string Reply {get;set;}=""; public string Created {get;set;}="";
    public string Updated {get;set;}=""; public string Version {get;set;}="";
}
public sealed class CommunityAssessment
{
    public string Id {get;set;}=""; public string InstructorId {get;set;}=""; public string StudentId {get;set;}="";
    public Dictionary<string,int> Skills {get;set;}=[]; public string PublicNote {get;set;}="";
    public string PrivateNote {get;set;}=""; public string Updated {get;set;}=""; public string Version {get;set;}="";
}
public sealed class CommunityPortal(Store store,IConfiguration config,IWebHostEnvironment env,StudentResources resources)
{
    readonly object gate=new();
    string Root=>Path.GetFullPath(config["CFC_DATA_DIR"]??Path.Combine(env.ContentRootPath,"App_Data"));
    string Dataset {get{if(store.DatabasePath==null)return "pilot";using var db=Database.Open(store.DatabasePath,true);return Database.Scalar(db,"SELECT value FROM metadata WHERE key='importId'");}}
    SqliteConnection Open()
    {
        Directory.CreateDirectory(Root);var db=Database.Open(Path.Combine(Root,"community.sqlite"));
        Database.Execute(db,"""
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS community_records(dataset TEXT NOT NULL,kind TEXT NOT NULL,id TEXT NOT NULL,role TEXT NOT NULL,owner TEXT NOT NULL,unit TEXT NOT NULL,json TEXT NOT NULL,PRIMARY KEY(dataset,kind,id));
        CREATE INDEX IF NOT EXISTS ix_community_owner ON community_records(dataset,kind,role,owner);
        CREATE TABLE IF NOT EXISTS community_operations(dataset TEXT NOT NULL,user_id TEXT NOT NULL,operation TEXT NOT NULL,fingerprint TEXT NOT NULL,result TEXT NOT NULL,PRIMARY KEY(dataset,user_id,operation));
        CREATE TABLE IF NOT EXISTS community_audit(id INTEGER PRIMARY KEY,dataset TEXT NOT NULL,user_id TEXT NOT NULL,action TEXT NOT NULL,record_id TEXT NOT NULL,created TEXT NOT NULL);
        """);return db;
    }
    static string Text(JsonElement b,string key,int max=3000){var s=b.TryGetProperty(key,out var v)?v.GetString()?.Trim()??"":"";if(s.Length>max)throw new RuleException("O campo "+key+" excede o limite de caracteres.");return s;}
    static string Stamp()=>Operations.Now.ToString("s");
    static void Require(bool yes,string text,int status=403){if(!yes)throw new RuleException(text,status);}
    public static readonly string[] SkillNames=["Controle do veículo","Observação e sinalização","Manobras","Segurança e autonomia"];
    public static readonly string[] StudentTypes=["Remarcar aula","Informar disponibilidade","Dúvida sobre o processo","Financeiro","Documentos"];
    public static readonly string[] InstructorTypes=["Ajustar agenda","Indisponibilidade","Ocorrência com veículo","Apoio ao aluno"];
    static void Team(User u){StudentResources.Team(u);}
    static void UnitAccess(User u,string unit){Require(u.Role=="Administrador"||u.UnitId==""||u.UnitId==unit,"Registro de outra unidade.");}
    static void Own(User u){Require(u.Role is "Aluno" or "Instrutor","Esta ação é exclusiva do titular do portal.");Require(!string.IsNullOrWhiteSpace(u.LinkedId),"Este acesso ainda não foi vinculado à ficha. Peça ao administrador para vinculá-lo.",409);}
    List<T> Rows<T>(SqliteConnection db,string kind,string where="1=1",params object?[] values)=>Database.Rows(db,"SELECT json FROM community_records WHERE dataset=$0 AND kind=$1 AND ("+where+")",new object?[]{Dataset,kind}.Concat(values).ToArray()).Select(x=>JsonSerializer.Deserialize<T>(x["json"]!,Database.Compact)!).ToList();
    void Save(SqliteConnection db,string kind,string id,string role,string owner,string unit,object value)=>Database.Execute(db,"INSERT INTO community_records VALUES($0,$1,$2,$3,$4,$5,$6) ON CONFLICT(dataset,kind,id) DO UPDATE SET json=excluded.json",Dataset,kind,id,role,owner,unit,JsonSerializer.Serialize(value,Database.Compact));
    void Audit(SqliteConnection db,User u,string action,string id)=>Database.Execute(db,"INSERT INTO community_audit(dataset,user_id,action,record_id,created) VALUES($0,$1,$2,$3,$4)",Dataset,u.Id,action,id,Stamp());
    public CommunityPolicy Policy(){lock(gate){using var db=Open();return Rows<CommunityPolicy>(db,"policy").SingleOrDefault()??new();}}
    public object Settings(User u){Team(u);return new{policy=Policy(),skillNames=SkillNames,studentTypes=StudentTypes,instructorTypes=InstructorTypes};}
    public CommunityPolicy Configure(User u,JsonElement b)
    {
        Require(u.Role=="Administrador","Somente o administrador configura os portais.");var p=JsonSerializer.Deserialize<CommunityPolicy>(b.GetRawText(),Database.Compact)??throw new RuleException("Configuração inválida.");
        lock(gate){using var db=Open();using var tx=db.BeginTransaction();Save(db,"policy","policy","","","",p);Audit(db,u,"policy","policy");tx.Commit();}return p;
    }
    async Task<(State State,string Unit,string Name)> Owner(string role,string id)
    {
        var s=await store.Community(role,id);
        if(role=="Aluno"){var p=s.Students.SingleOrDefault(x=>x.Id==id)??throw new RuleException("Aluno vinculado não encontrado.",404);return(s,p.UnitId,p.Name);}
        var i=s.Instructors.SingleOrDefault(x=>x.Id==id)??throw new RuleException("Instrutor vinculado não encontrado.",404);return(s,i.UnitId,i.Name);
    }
    public async Task<object> Snapshot(User u,string previewRole="",string previewId="")
    {
        var preview=false;var role=u.Role;var owner=u.LinkedId;
        if(role is "Aluno" or "Instrutor"){Own(u);Require(previewRole==""&&previewId=="","Não é permitido consultar outro titular.");}
        else{Team(u);Require(previewRole is "Aluno" or "Instrutor"&&previewId!="","Escolha um aluno ou instrutor para visualizar o portal.",400);role=previewRole;owner=previewId;preview=true;}
        var(s,unit,name)=await Owner(role,owner);if(preview)UnitAccess(u,unit);var policy=Policy();
        var lessons=s.Lessons.Where(x=>!x.ExcludedFromAgenda&&(role=="Aluno"?x.StudentId==owner:x.InstructorId==owner)).OrderBy(x=>x.Start).ToList();
        var student=s.Students.FirstOrDefault(x=>role=="Aluno"&&x.Id==owner);
        var instructor=s.Instructors.FirstOrDefault(x=>role=="Instrutor"&&x.Id==owner);
        List<CommunityRequest> requests;List<CommunityAssessment> reviews;
        lock(gate){using var db=Open();requests=Rows<CommunityRequest>(db,"request","role=$2 AND owner=$3",role,owner).OrderByDescending(x=>x.Created).ToList();reviews=Rows<CommunityAssessment>(db,"assessment",role=="Aluno"?"json_extract(json,'$.studentId')=$2":"owner=$2",owner);}
        var enrollments=s.Enrollments.Where(x=>x.StudentId==owner).ToList();
        return new{
            serverNow=Operations.Now.ToString("s"),preview,role,user=new{u.Id,u.Name},profile=new{id=owner,name,unitId=unit,unit=s.Units.Find(x=>x.Id==unit)?.Name??"",category=student?.Category??instructor?.Category??"",cnhUntil=instructor?.CnhUntil??"",ecpfUntil=instructor?.EcpfUntil??""},policy,skillNames=SkillNames,requestTypes=role=="Aluno"?StudentTypes:InstructorTypes,
            period=role=="Instrutor"?new{from=Operations.Now.AddDays(-90).ToString("yyyy-MM-dd"),to=Operations.Now.AddDays(60).ToString("yyyy-MM-dd")}:null,
            lessons=lessons.Select(x=>new{x.Id,x.StudentId,x.EnrollmentId,student=s.Students.Find(a=>a.Id==x.StudentId)?.Name??"Aluno",x.Category,x.Start,x.Minutes,x.Status,x.Validated,instructor=s.Instructors.Find(i=>i.Id==x.InstructorId)?.Name??"",vehicle=s.Vehicles.Find(v=>v.Id==x.VehicleId)?.Name??"",plate=s.Vehicles.Find(v=>v.Id==x.VehicleId)?.Plate??"",unit=s.Units.Find(v=>v.Id==x.UnitId)?.Name??""}),
            students=role=="Instrutor"?s.Students.Where(x=>lessons.Any(l=>l.StudentId==x.Id)).Select(x=>new{x.Id,x.Name,category=string.Join("/",lessons.Where(l=>l.StudentId==x.Id).Select(l=>l.Category).Where(c=>c.Length is >0 and <=2).Distinct().Order()),course=x.Category}).ToArray():[],
            exams=role=="Aluno"?s.Exams.Where(x=>x.StudentId==owner).Select(x=>new{x.Id,x.Type,x.Start,x.Location,x.Status}).ToArray():[],
            progress=role=="Aluno"?new{hasRenach=student!.Renach!="",ladv=student.Ladv,medicalUntil=student.MedicalUntil,enrollments=enrollments.Select(x=>new{x.Id,x.Number,x.Category,x.Service,x.LessonLimit,x.LessonLimits,x.Status})}:null,
            finance=role=="Aluno"&&policy.StudentFinance?new{balance=enrollments.Sum(x=>Operations.Balance(s,x.Id)),installments=s.Installments.Where(x=>x.StudentId==owner).Select(x=>new{x.Id,x.Due,x.Amount,x.Paid,x.Status}),entries=s.Entries.Where(x=>x.StudentId==owner).OrderByDescending(x=>x.Date).Select(x=>new{x.Id,x.Date,x.Kind,x.Amount,x.Method,x.Description,x.Receipt})}:null,
            documents=role=="Aluno"?s.Documents.Where(x=>x.StudentId==owner).Select(x=>new{x.Id,x.Name,x.Created}).ToArray():[],
            files=role=="Aluno"?await resources.List(u,owner,1):null,
            requests=requests.Select(x=>new{x.Id,x.Type,x.Message,x.Status,x.Reply,x.LessonId,x.Created,x.Updated,x.Version}),
            assessments=reviews.Select(x=>new{x.Id,x.StudentId,x.InstructorId,x.Skills,x.PublicNote,privateNote=role=="Instrutor"?x.PrivateNote:"",x.Updated,x.Version})
        };
    }
    string Operation(User u,string op,string fingerprint,Func<SqliteConnection,object> action)
    {
        Require(Guid.TryParse(op,out _),"Identificador de operação inválido.",400);
        lock(gate){using var db=Open();using var tx=db.BeginTransaction();var prior=Database.Rows(db,"SELECT fingerprint,result FROM community_operations WHERE dataset=$0 AND user_id=$1 AND operation=$2",Dataset,u.Id,op).SingleOrDefault();if(prior!=null){Require(prior["fingerprint"]==fingerprint,"Identificador reutilizado para outra operação.",409);return prior["result"]!;}
            var result=JsonSerializer.Serialize(action(db),Database.Compact);Database.Execute(db,"INSERT INTO community_operations VALUES($0,$1,$2,$3,$4)",Dataset,u.Id,op,fingerprint,result);tx.Commit();return result;}
    }
    public async Task<string> Request(User u,JsonElement b,string op)
    {
        Own(u);var p=Policy();var type=Text(b,"type",80);var lessonId=Text(b,"lessonId",100);var message=Text(b,"message");
        var(s,unit,name)=await Owner(u.Role,u.LinkedId);var confirmation=type=="Confirmar presença";
        if(confirmation){Require(u.Role=="Aluno"&&p.StudentConfirmation,"A confirmação de presença não está habilitada.");Require(lessonId!="","Escolha a aula.",400);}
        else{Require(u.Role=="Aluno"?p.StudentRequests:p.InstructorRequests,"Solicitações desabilitadas pelo administrador.");Require((u.Role=="Aluno"?StudentTypes:InstructorTypes).Contains(type),"Tipo de solicitação inválido.",400);Require(message.Length>=5,"Descreva sua solicitação com pelo menos 5 caracteres.",400);}
        if(lessonId!=""){var lesson=s.Lessons.SingleOrDefault(x=>x.Id==lessonId&&!x.ExcludedFromAgenda&&(u.Role=="Aluno"?x.StudentId==u.LinkedId:x.InstructorId==u.LinkedId));Require(lesson!=null,"Esta aula pertence a outro titular ou está fora do período disponível.");if(confirmation)Require(lesson!.Status=="Agendada"&&DateTime.TryParse(lesson.Start,out var start)&&start>Operations.Now,"Confirme apenas aulas futuras e agendadas.",409);}
        return Operation(u,op,"request:"+b.GetRawText(),db=>{if(confirmation){var prior=Rows<CommunityRequest>(db,"request","role=$2 AND owner=$3",u.Role,u.LinkedId).FirstOrDefault(x=>x.Type==type&&x.LessonId==lessonId);if(prior!=null)return prior;}
            var r=new CommunityRequest{Id=Guid.NewGuid().ToString("N"),Role=u.Role,Owner=u.LinkedId,Author=u.Id,Name=name,UnitId=unit,LessonId=lessonId,Type=type,Message=confirmation?"O aluno confirmou que pretende comparecer. Registro de intenção de presença.":message,Created=Stamp(),Updated=Stamp(),Version=Guid.NewGuid().ToString("N")};Save(db,"request",r.Id,u.Role,u.LinkedId,unit,r);Audit(db,u,"request",r.Id);return r;});
    }
    public object Inbox(User u,string selectedUnit="")
    {
        Team(u);var unit=u.Role=="Administrador"?selectedUnit:u.UnitId!=""?u.UnitId:selectedUnit;
        lock(gate){using var db=Open();return new{rows=Rows<CommunityRequest>(db,"request","($2='' OR unit=$2)",unit).OrderByDescending(x=>x.Created).ToArray()};}
    }
    public string Reply(User u,string id,JsonElement b,string op)
    {
        Team(u);var reply=Text(b,"reply");var status=Text(b,"status",50);var expected=Text(b,"version",100);
        Require(new[]{"Aberta","Em atendimento","Concluída"}.Contains(status),"Situação inválida.",400);Require(reply.Length>=3,"Informe a resposta para o titular.",400);
        return Operation(u,op,"reply:"+id+":"+b.GetRawText(),db=>{var r=Rows<CommunityRequest>(db,"request","id=$2",id).SingleOrDefault()??throw new RuleException("Solicitação não encontrada.",404);UnitAccess(u,r.UnitId);Require(r.Version==expected,"Esta solicitação mudou. Atualize antes de responder.",409);r.Status=status;r.Reply=reply;r.Version=Guid.NewGuid().ToString("N");r.Updated=Stamp();Save(db,"request",id,r.Role,r.Owner,r.UnitId,r);Audit(db,u,"reply",id);return r;});
    }
    public async Task<string> Assess(User u,string lessonId,JsonElement b,string op)
    {
        Own(u);Require(u.Role=="Instrutor"&&Policy().InstructorAssessment,"Avaliações não estão habilitadas para este acesso.");
        var(s,unit,_)=await Owner(u.Role,u.LinkedId);var l=s.Lessons.SingleOrDefault(x=>x.Id==lessonId&&x.InstructorId==u.LinkedId&&!x.ExcludedFromAgenda);Require(l!=null,"Esta aula não pertence a este instrutor.");
        Require(l!.Status=="Realizada"&&DateTime.TryParse(l.Start,out var start)&&start<=Operations.Now,"Registre a evolução depois da realização da aula.",409);
        var skills=b.TryGetProperty("skills",out var raw)?JsonSerializer.Deserialize<Dictionary<string,int>>(raw,Database.Compact)??[]:[];
        Require(skills.Count==SkillNames.Length&&SkillNames.All(x=>skills.TryGetValue(x,out var value)&&value is >=1 and <=5),"Avalie as quatro habilidades com notas de 1 a 5.",400);
        var publicNote=Text(b,"publicNote");var privateNote=Text(b,"privateNote");var expected=Text(b,"version",100);
        return Operation(u,op,"assessment:"+lessonId+":"+b.GetRawText(),db=>{var prior=Rows<CommunityAssessment>(db,"assessment","id=$2",lessonId).SingleOrDefault();Require((prior?.Version??"")==expected,"A avaliação mudou. Atualize antes de salvar.",409);var r=new CommunityAssessment{Id=lessonId,InstructorId=u.LinkedId,StudentId=l.StudentId,Skills=skills,PublicNote=publicNote,PrivateNote=privateNote,Updated=Stamp(),Version=Guid.NewGuid().ToString("N")};Save(db,"assessment",lessonId,u.Role,u.LinkedId,unit,r);Audit(db,u,"assessment",lessonId);return r;});
    }
    public async Task<string> Attendance(User u,string lessonId,JsonElement b,string op)
    {
        Own(u);Require(u.Role=="Instrutor"&&Policy().InstructorAttendance,"O registro de realização/falta está desabilitado.");Require(Guid.TryParse(op,out _),"Identificador inválido.",400);
        var status=Text(b,"status",50);Require(status is "Realizada" or "Falta","Registre realização ou falta.",400);
        var body=JsonSerializer.SerializeToElement(new{id=lessonId,status},Database.Compact);
        return await store.Mutate(u.Id,op,"community-attendance:"+body.GetRawText(),s=>Operations.Execute(s,u,"lesson-status",body),new StateScope{Action="lesson-status",Body=body});
    }
    public async Task<StudentFileInfo> Upload(User u,IFormFile file,string title,string category,string op)
    {
        Own(u);Require(u.Role=="Aluno"&&Policy().StudentUploads,"Envio de documentos desabilitado.");return await resources.Upload(u,u.LinkedId,file,title,category,op,true);
    }
    public async Task<Document> Document(User u,string id,string sid="")
    {
        if(u.Role=="Aluno"){Own(u);Require(sid==""||sid==u.LinkedId,"Documento de outro aluno.");sid=u.LinkedId;}else{Team(u);Require(sid!="","Informe o titular.",400);}
        var(s,unit,_)=await Owner("Aluno",sid);if(u.Role!="Aluno")UnitAccess(u,unit);return s.Documents.SingleOrDefault(x=>x.Id==id&&x.StudentId==sid)??throw new RuleException("Documento não encontrado.",404);
    }
}

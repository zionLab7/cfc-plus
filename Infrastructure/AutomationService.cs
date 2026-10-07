using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CfcPilot;

public sealed class PortalOperation
{
    public string Id{get;set;}="";public string Name{get;set;}="";public string Portal{get;set;}="";public string Url{get;set;}="";public string Status{get;set;}="";public string[] Steps{get;set;}=[];public string[] Sources{get;set;}=[];public bool ReadOnly{get;set;}public bool StudentSession{get;set;}
}
public sealed class AutomationService(Store store,PortalSessions sessions,ManualPortalBrowser manualBrowser,IWebHostEnvironment env,IDataProtectionProvider protection)
{
    readonly IDataProtector protector=protection.CreateProtector("CfcPilot.PortalEvidence.v1");
    string Path=>store.DatabasePath??throw new RuleException("Importe a base real para habilitar as operações.",409);
    public PortalOperation[] OperationsCatalog()=>JsonSerializer.Deserialize<PortalOperation[]>(File.ReadAllText(System.IO.Path.Combine(env.ContentRootPath,"Config/automation-catalog.json")),Database.Compact)!;
    static void Team(User user){if(user.Role is "Aluno" or "Instrutor")throw new RuleException("Acesso restrito à equipe.",403);}
    public object Catalog(User user){Team(user);using var db=Database.Open(Path,true);return new{operations=OperationsCatalog(),sourceFiles=393,jobs=Database.Rows(db,"SELECT id,operation,status,updated FROM automation_jobs ORDER BY updated DESC LIMIT 100")};}
    public object Job(string id,User user)
    {
        Team(user);using var db=Database.Open(Path,true);var row=Database.Rows(db,"SELECT * FROM automation_jobs WHERE id=$0",id).FirstOrDefault()??throw new RuleException("Operação não encontrada.",404);
        var op=OperationsCatalog().Single(x=>x.Id==row["operation"]);var result=JsonSerializer.Deserialize<JsonElement>(row["result_json"]!);
        var request=JsonSerializer.Deserialize<JsonElement>(row["request_json"]!);var loginMode=result.TryGetProperty("loginMode",out var mode)?mode.GetString():request.TryGetProperty("loginMode",out var requested)?requested.GetString():"assisted";
        var student=JsonSerializer.Deserialize<Student>(Database.Scalar(db,"SELECT json FROM entities WHERE kind='Students' AND id=$0",row["student_id"]) is {Length:>0} json?json:"null",Database.Compact);
        return new{id,name=op.Name,portal=op.Portal,studentSession=op.StudentSession,studentId=row["student_id"],studentName=student?.Name??"",studentCpf=student?.Cpf??"",status=row["status"],loginMode,steps=op.Steps,detail=row["error"]==""?(op.StudentSession?"Este acesso usa a conta GOV.BR e a sessão exclusiva do aluno acima. Não utiliza o login do funcionário.":"Confira aluno, unidade, profissional e condições no portal antes de concluir."):row["error"],protocol=result.TryGetProperty("protocol",out var p)?p.GetString():"",hasEvidence=result.TryGetProperty("evidence",out _)};
    }
    public object Prepare(User user,JsonElement body,string requestId)
    {
        Team(user);if(!Guid.TryParse(requestId,out _))throw new RuleException("Operação sem identificador.");
        var op=OperationsCatalog().SingleOrDefault(x=>x.Id==body.GetProperty("operation").GetString())??throw new RuleException("Serviço inexistente.");
        var sid=body.GetProperty("studentId").GetString()??"";var pid=body.GetProperty("profileId").GetString()??"";
        using var db=Database.Open(Path);using var tx=db.BeginTransaction();
        var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.Id+":"+requestId))).ToLowerInvariant()[..32];
        var prior=Database.Scalar(db,"SELECT request_json FROM automation_jobs WHERE id=$0",id);
        if(prior!=""){if(prior!=body.GetRawText())throw new RuleException("Identificador reutilizado.",409);tx.Commit();return Job(id,user);}
        if(sid!=""&&Database.Count(db,"SELECT count(*) FROM entities WHERE kind='Students' AND id=$0",sid)!=1)throw new RuleException("Aluno não encontrado.");
        if(Database.Count(db,"SELECT count(*) FROM entities WHERE kind='BrowserProfiles' AND id=$0",pid)!=1)throw new RuleException("Profissional não encontrado.");
        var profile=JsonSerializer.Deserialize<BrowserProfile>(Database.Scalar(db,"SELECT json FROM entities WHERE kind='BrowserProfiles' AND id=$0",pid),Database.Compact)!;
        if(op.StudentSession&&(sid==""||profile.Kind!="Aluno"||profile.StudentId!=sid))throw new RuleException("Este serviço exige a sessão GOV.BR do aluno selecionado.",409);
        if(!op.StudentSession&&profile.Kind=="Aluno")throw new RuleException("Esta operação usa um perfil profissional. Escolha diretor ou instrutor.",409);
        if(op.Url.Contains("{cpf}")&&sid=="")throw new RuleException("Selecione o aluno em atendimento.");
        var now=Operations.Now.ToString("s");Database.Execute(db,"INSERT INTO automation_jobs(id,operation,student_id,profile_id,status,created,updated,request_json,user_id) VALUES($0,$1,$2,$3,'Preparada para revisão',$4,$4,$5,$6)",id,op.Id,sid,pid,now,body.GetRawText(),user.Id);tx.Commit();return Job(id,user);
    }
    public async Task<object> Open(string id,User user,IPAddress? remote,bool? manualMode=null)
    {
        Team(user);using var db=Database.Open(Path);var row=Database.Rows(db,"SELECT * FROM automation_jobs WHERE id=$0",id).FirstOrDefault()??throw new RuleException("Operação inexistente.",404);
        if(row["status"] is "Confirmado manualmente" or "Não realizado")throw new RuleException("Esta operação foi encerrada. Prepare outra consulta, se necessário.",409);
        var op=OperationsCatalog().Single(x=>x.Id==row["operation"]);var profile=JsonSerializer.Deserialize<BrowserProfile>(Database.Scalar(db,"SELECT json FROM entities WHERE kind='BrowserProfiles' AND id=$0",row["profile_id"]),Database.Compact)!;
        var request=JsonSerializer.Deserialize<JsonElement>(row["request_json"]!);
        var manual=manualMode??(request.TryGetProperty("loginMode",out var configuredMode)&&configuredMode.GetString()=="manual");
        if(request.TryGetProperty("loginMode",out var requested)&&requested.GetString()!=(manual?"manual":"assisted"))throw new RuleException("Prepare outra operação para mudar o modo de acesso.",409);
        var result=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(row["result_json"]!)!;
        if(result.TryGetValue("loginMode",out var previous)&&previous.GetString()!=(manual?"manual":"assisted"))throw new RuleException("Prepare outra operação para mudar o modo de acesso.",409);
        if(manual&&!op.StudentSession)throw new RuleException("O login manual nesta opção é exclusivo do aluno.",409);
        var cpf=Database.Scalar(db,"SELECT cpf FROM entities WHERE kind='Students' AND id=$0",row["student_id"]);
        try
        {
            var url=op.StudentSession?ProfessionalBrowser.StudentPortal():op.Url.Replace("{cpf}",Uri.EscapeDataString(cpf));
            if(manual)manualBrowser.Open(profile,url,remote,id);else await sessions.Open(profile,url,remote,id,user.Id);
            result["loginMode"]=JsonSerializer.SerializeToElement(manual?"manual":"assisted");
            Database.Execute(db,"UPDATE automation_jobs SET status=$1,result_json=$2,updated=$3,error='' WHERE id=$0",id,manual?"Login manual no navegador":"Aguardando operador no portal",JsonSerializer.Serialize(result),Operations.Now.ToString("s"));
        }
        catch(RuleException ex){Database.Execute(db,"UPDATE automation_jobs SET status='Abertura pendente',error=$1,updated=$2 WHERE id=$0",id,ex.Message,Operations.Now.ToString("s"));throw;}
        return Job(id,user);
    }
    public async Task<object> Capture(string id,User user)
    {
        Team(user);using var db=Database.Open(Path);var row=Database.Rows(db,"SELECT profile_id,student_id,result_json FROM automation_jobs WHERE id=$0",id).FirstOrDefault()??throw new RuleException("Operação inexistente.",404);
        var state=JsonSerializer.Deserialize<JsonElement>(row["result_json"]!);
        if(state.TryGetProperty("loginMode",out var mode)&&mode.GetString()=="manual")throw new RuleException("Esta janela usa login manual. Anexe os documentos obtidos à ficha ou registre o resultado conferido. A leitura automática exige uma sessão assistida separada.",409);
        var cpf=Database.Scalar(db,"SELECT cpf FROM entities WHERE kind='Students' AND id=$0",row["student_id"]);var evidence=await sessions.Capture(row["profile_id"]!,cpf,id);
        var captured=JsonSerializer.SerializeToElement(evidence,Database.Compact);var proposals=PortalExtraction.Proposals(captured,cpf);var result=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(row["result_json"]!)!;result["proposals"]=JsonSerializer.SerializeToElement(proposals);result["evidence"]=JsonSerializer.SerializeToElement(protector.Protect(JsonSerializer.Serialize(evidence,Database.Compact)));
        Database.Execute(db,"UPDATE automation_jobs SET status='Dados capturados para conferência',result_json=$1,updated=$2 WHERE id=$0",id,JsonSerializer.Serialize(result),Operations.Now.ToString("s"));return Job(id,user);
    }
    public void AuthorizeStudentSession(string id,string sid,User user)
    {
        StudentResources.Team(user);using var db=Database.Open(Path,true);
        var row=Database.Rows(db,"SELECT student_id,user_id,operation,request_json,result_json FROM automation_jobs WHERE id=$0",id).FirstOrDefault()??throw new RuleException("Operação inexistente.",404);
        if(row["student_id"]!=sid||row["user_id"]!=user.Id)throw new RuleException("Esta operação pertence a outro aluno ou atendente.",403);
        if(!OperationsCatalog().Single(x=>x.Id==row["operation"]).StudentSession)throw new RuleException("Esta operação não é uma sessão do aluno.",409);
        var result=JsonSerializer.Deserialize<JsonElement>(row["result_json"]!);var request=JsonSerializer.Deserialize<JsonElement>(row["request_json"]!);
        var mode=result.TryGetProperty("loginMode",out var previous)?previous.GetString():request.TryGetProperty("loginMode",out var selected)?selected.GetString():"assisted";
        if(mode!="assisted")throw new RuleException("Esta operação usa a janela manual. Abra o navegador interno pela ficha.",409);
    }
    public object Evidence(string id,User user){Team(user);using var db=Database.Open(Path,true);var result=JsonSerializer.Deserialize<JsonElement>(Database.Scalar(db,"SELECT result_json FROM automation_jobs WHERE id=$0",id));if(!result.TryGetProperty("evidence",out var e))throw new RuleException("Nenhuma evidência capturada.",404);return new{capture=JsonSerializer.Deserialize<JsonElement>(protector.Unprotect(e.GetString()!)),proposals=result.TryGetProperty("proposals",out var proposals)?proposals:JsonSerializer.SerializeToElement(new{}),studentId=Database.Scalar(db,"SELECT student_id FROM automation_jobs WHERE id=$0",id)};}
    public object Record(string id,User user,JsonElement body)
    {
        Team(user);var status=body.GetProperty("status").GetString();if(status is not("Confirmado manualmente" or "Não realizado" or "Resultado incerto"))throw new RuleException("Estado inválido.");
        var notes=body.GetProperty("notes").GetString()??"";if(notes.Length<5||notes.Length>10000)throw new RuleException("Descreva a evidência conferida.");
        using var db=Database.Open(Path);using var tx=db.BeginTransaction();var prior=Database.Scalar(db,"SELECT result_json FROM automation_jobs WHERE id=$0",id);if(prior=="")throw new RuleException("Operação inexistente.",404);
        var result=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(prior)!;result["protocol"]=body.GetProperty("protocol");result["notes"]=JsonSerializer.SerializeToElement(protector.Protect(notes));result["verifiedBy"]=JsonSerializer.SerializeToElement(user.Id);
        Database.Execute(db,"UPDATE automation_jobs SET status=$1,result_json=$2,updated=$3 WHERE id=$0",id,status,JsonSerializer.Serialize(result),Operations.Now.ToString("s"));tx.Commit();return Job(id,user);
    }
}

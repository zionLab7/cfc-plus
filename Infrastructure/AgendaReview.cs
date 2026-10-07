using System.Globalization;
using System.Text.Json;
namespace CfcPilot;
public record AgendaIssue(string Type,string LessonId,string OtherId,string StudentId,string Start,string UnitId,string Detail);
public sealed class AgendaReview(Store store)
{
    readonly object gate=new();string version="";List<AgendaIssue> issues=[];object summary=new{};
    public static List<AgendaIssue> Scan(List<Lesson> lessons,HashSet<string> students,HashSet<string> instructors,HashSet<string> vehicles)
    {
        var issues=new List<AgendaIssue>();var valid=new List<(Lesson Lesson,DateTime Start,DateTime End)>();
        foreach(var lesson in lessons.Where(x=>!x.ExcludedFromAgenda&&x.Status!="Cancelada"))
        {
            if(!DateTime.TryParse(lesson.Start,CultureInfo.InvariantCulture,DateTimeStyles.None,out var start)||lesson.Minutes<=0){issues.Add(new("Data inválida",lesson.Id,"",lesson.StudentId,lesson.Start,lesson.UnitId,"Data ou duração precisa de revisão."));continue;}
            foreach(var (exists,detail) in new[]{(students.Contains(lesson.StudentId),"Aluno ausente"),(instructors.Contains(lesson.InstructorId),"Instrutor ausente"),(vehicles.Contains(lesson.VehicleId),"Veículo ausente")})if(!exists)issues.Add(new("Vínculo ausente",lesson.Id,"",lesson.StudentId,lesson.Start,lesson.UnitId,detail));
            valid.Add((lesson,start,start.AddMinutes(lesson.Minutes)));
        }
        foreach(var kind in new[]{"Aluno","Instrutor","Veículo"})
        foreach(var group in valid.GroupBy(x=>kind=="Aluno"?x.Lesson.StudentId:kind=="Instrutor"?x.Lesson.InstructorId:x.Lesson.VehicleId).Where(x=>x.Key!=""))
        {
            var running=new List<(Lesson Lesson,DateTime Start,DateTime End)>();
            foreach(var item in group.OrderBy(x=>x.Start))
            {
                running.RemoveAll(x=>x.End<=item.Start);
                foreach(var prior in running)
                {
                    // Different participants in the same imported session share resources intentionally.
                    if(kind!="Aluno"&&item.Lesson.Imported&&prior.Lesson.Imported&&item.Lesson.SourceId!=""&&item.Lesson.SourceId==prior.Lesson.SourceId)continue;
                    var exact=prior.Start==item.Start&&prior.Lesson.StudentId==item.Lesson.StudentId&&prior.Lesson.InstructorId==item.Lesson.InstructorId&&prior.Lesson.VehicleId==item.Lesson.VehicleId;
                    if(exact&&kind!="Aluno")continue;
                    var historical=item.End<Operations.Now&&prior.End<Operations.Now;
                    issues.Add(new(exact?"Duplicata":"Sobreposição de "+kind,item.Lesson.Id,prior.Lesson.Id,item.Lesson.StudentId,item.Lesson.Start,item.Lesson.UnitId,(historical?"Histórico: ":"Reserva: ")+"intervalos simultâneos; conferir registros e recursos antes de alterar."));
                }
                running.Add(item);
            }
        }
        return issues;
    }
    public object Page(string unit,string from,string to,int page)
    {
        var path=store.DatabasePath??throw new RuleException("Auditoria completa disponível na base importada.",409);
        lock(gate)
        {
            using var db=Database.Open(path,true);var revision=Database.Scalar(db,"SELECT value FROM metadata WHERE key='revision'");
            if(version!=path+":"+revision)
            {
                var all=Database.Rows(db,"SELECT json FROM entities WHERE kind='Lessons'").Select(row=>JsonSerializer.Deserialize<Lesson>(row["json"]!,Database.Compact)!).ToList();
                HashSet<string> Ids(string kind)=>Database.Rows(db,"SELECT id FROM entities WHERE kind=$0",kind).Select(row=>row["id"]!).ToHashSet();
                issues=Scan(all,Ids("Students"),Ids("Instructors"),Ids("Vehicles"));
                summary=new{inspected=all.Count,linkedRecords=all.Count(x=>x.ExcludedFromAgenda),issues=issues.Count,byType=issues.GroupBy(x=>x.Type).Select(x=>new{type=x.Key,count=x.Count()}),verifiedAt=Operations.Now.ToString("s")};version=path+":"+revision;
            }
            var filtered=issues.Where(x=>(unit==""||x.UnitId==unit)&&(from==""||string.CompareOrdinal(x.Start,from)>=0)&&(to==""||string.CompareOrdinal(x.Start[..Math.Min(x.Start.Length,10)],to)<=0)).OrderByDescending(x=>x.Start).ToList();
            return new{summary,total=filtered.Count,page,pageSize=50,rows=filtered.Skip((page-1)*50).Take(50)};
        }
    }
}

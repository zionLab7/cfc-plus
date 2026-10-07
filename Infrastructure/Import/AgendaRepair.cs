using System.Text.Json;
namespace CfcPilot;
public static class AgendaRepair
{
    public static void Run(string root)
    {
        root=Path.GetFullPath(root);var active=Path.Combine(root,"active-database.json");var manifest=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(active));var source=Path.GetFullPath(Path.Combine(root,manifest.GetProperty("path").GetString()!));
        if(!source.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Banco fora da pasta de dados.");
        using(var db=Database.Open(source,true))if(Database.Scalar(db,"SELECT value FROM metadata WHERE key='agendaSchema'")=="1"){Console.WriteLine("Correção da agenda já aplicada.");return;}
        var folder=Path.Combine(root,"imports","agenda-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(folder);var target=Path.Combine(folder,"cfc.sqlite");
        using(var original=Database.Open(source,true))using(var copy=Database.Open(target))original.BackupDatabase(copy);
        var corrections=new List<object>();
        using(var db=Database.Open(target))
        {
            var before=Database.Count(db,"SELECT count(*) FROM entities WHERE kind='Lessons'");
            var all=Database.Rows(db,"SELECT e.json,a.Tipo AS sourceType FROM entities e LEFT JOIN raw_Agendaaula a ON a.AgendaAula_id=json_extract(e.json,'$.sourceId') WHERE e.kind='Lessons'").Select(row=>(Lesson:JsonSerializer.Deserialize<Lesson>(row["json"]!,Database.Compact)!,Type:row["sourceType"])).ToList();
            using(var tx=db.BeginTransaction())
            {
                foreach(var group in all.Where(x=>x.Lesson.Imported&&!x.Lesson.ExcludedFromAgenda&&x.Lesson.Status!="Cancelada").GroupBy(x=>(x.Lesson.StudentId,x.Lesson.EnrollmentId,x.Lesson.Start,x.Lesson.InstructorId,x.Lesson.VehicleId)))
                {
                    var primary=group.Where(x=>x.Type=="1").ToList();if(primary.Count!=1)continue;
                    foreach(var duplicate in group.Where(x=>x.Type=="2"&&!x.Lesson.AttendanceStart&&!x.Lesson.AttendanceEnd))
                    {
                        var item=duplicate.Lesson;item.ExcludedFromAgenda=true;item.CanonicalLessonId=primary[0].Lesson.Id;item.AgendaCorrectionReason="Registro auxiliar Tipo=2 vinculado à aula Tipo=1 com mesmo aluno, matrícula, horário, instrutor e veículo. Original preservado.";Database.SaveEntity(db,"Lessons",item);corrections.Add(new{id=item.Id,canonical=item.CanonicalLessonId,reason=item.AgendaCorrectionReason});
                    }
                }
                Database.Execute(db,"INSERT INTO metadata(key,value) VALUES('agendaSchema','1')");Database.Execute(db,"UPDATE metadata SET value=CAST(value AS INTEGER)+1 WHERE key='revision'");tx.Commit();
            }
            if(Database.Count(db,"SELECT count(*) FROM entities WHERE kind='Lessons'")!=before||Database.Scalar(db,"PRAGMA integrity_check")!="ok")throw new InvalidOperationException("Conferência falhou; banco anterior permanece ativo.");
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonSerializer.Serialize(new{inspected=before,corrections,sourceRows=Database.Count(db,"SELECT SUM(imported) FROM import_tables"),originalRetained=true,verifiedAt=Operations.Now.ToString("s")},Store.Json));Database.Execute(db,"PRAGMA wal_checkpoint(TRUNCATE)");
        }
        File.Copy(active,Path.Combine(folder,"previous-active-database.json"));File.WriteAllText(active+".tmp",JsonSerializer.Serialize(new{path=Path.GetRelativePath(root,target),importId=manifest.GetProperty("importId").GetString()}));File.Move(active+".tmp",active,true);
        Console.WriteLine("Agenda conferida em cópia; "+corrections.Count+" registros auxiliares vinculados, sem apagar linhas. Banco anterior preservado.");
    }
}

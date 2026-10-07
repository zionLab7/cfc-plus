using System.Text.Json;
using System.Globalization;
namespace CfcPilot;
public static class ProjectionRepair
{
    public static string Category(string type)=>type switch{"1"=>"A","2"=>"B","3"=>"C","4"=>"D","5"=>"E","10"=>"S",_=>"Tipo "+type};
    public static void Run(Microsoft.Data.Sqlite.SqliteConnection db)
    {
        if(Database.Scalar(db,"SELECT value FROM metadata WHERE key='projectionSchema'")=="2")return;
        using var tx=db.BeginTransaction();
        Database.Execute(db,"CREATE INDEX IF NOT EXISTS ix_pedido_identity ON raw_Pedido(Pedido_id,Aluno,Unidade);CREATE INDEX IF NOT EXISTS ix_pedido_number ON raw_Pedido(Pedido_num);");
        var controls=Database.Rows(db,"SELECT Aluno,Matricula,Tipo_id,SUM(CAST(AulasControle_TotalBrinde AS INTEGER)+CAST(COALESCE(Extra,'0') AS INTEGER)) AS total FROM raw_Aulascontrole GROUP BY Aluno,Matricula,Tipo_id").GroupBy(r=>r["Aluno"]+":"+r["Matricula"]).ToDictionary(g=>g.Key,g=>g.ToDictionary(r=>Category(r["Tipo_id"]!),r=>int.Parse(r["total"]??"0")));
        var package=Database.Rows(db,"SELECT Pacote_id,Categoria FROM raw_Pacote").ToDictionary(r=>r["Pacote_id"]!,r=>r["Categoria"]??"");
        Database.Execute(db,"DELETE FROM entities WHERE kind='Enrollments' AND json_extract(json,'$.imported')=1");
        foreach(var r in Database.Rows(db,"SELECT p.*,a.Cat AS StudentCategory FROM raw_Pedido p LEFT JOIN raw_Aluno a ON a.Aluno=p.Aluno"))
        {
            var limits=controls.GetValueOrDefault(r["Aluno"]+":"+r["Pedido_id"])??[];
            Database.SaveEntity(db,"Enrollments",new Enrollment{Id="infor:PedidoNum:"+r["Pedido_num"],Number=r["Pedido_id"]??"",SourceId=r["Pedido_num"]??"",SourceTable="Pedido",Imported=true,StudentId=Projector.Id("Aluno",r["Aluno"]??""),PackageId=Projector.Id("Pacote",r["Pacote_id"]??""),UnitId=r["Unidade"]??"",Category=package.GetValueOrDefault(r["Pacote_id"]??"")??r["StudentCategory"]??"",Service=r["Pedido_tipo"]??"",Price=decimal.TryParse(r["Pedido_valor"],NumberStyles.Float,CultureInfo.InvariantCulture,out var price)?decimal.Round(price,2):0,Created=DateTime.TryParse(r["Pedido_data"],CultureInfo.InvariantCulture,out var day)?day.ToString("yyyy-MM-dd"):"",LessonLimits=limits,LessonLimit=limits.Where(x=>x.Key is "A" or "B" or "C" or "D" or "E").Sum(x=>x.Value),Status=r["Pedido_sit"] switch{"A"=>"Ativa","F"=>"Encerrada","P"=>"Pendente",_=>"A conferir"}});
        }
        // Matrícula is a displayed number scoped by student, whereas Pedido_num is the unique key.
        // A nonunique student/number pair remains unresolved; it is never attached by guessing.
        Database.Execute(db,"""
        CREATE TEMP TABLE enrollment_links(student_id TEXT NOT NULL,old_id TEXT NOT NULL,new_id TEXT NOT NULL,PRIMARY KEY(student_id,old_id));
        INSERT INTO enrollment_links SELECT 'infor:Aluno:'||Aluno,'infor:Pedido:'||Pedido_id,'infor:PedidoNum:'||MIN(Pedido_num) FROM raw_Pedido GROUP BY Aluno,Pedido_id HAVING count(*)=1;
        UPDATE entities AS e SET enrollment_id=COALESCE((SELECT new_id FROM enrollment_links l WHERE l.student_id=e.student_id AND l.old_id=e.enrollment_id),''),
        json=json_set(json,'$.enrollmentId',COALESCE((SELECT new_id FROM enrollment_links l WHERE l.student_id=e.student_id AND l.old_id=e.enrollment_id),''))
        WHERE kind IN('Lessons','Entries','Exams','Installments','Documents') AND enrollment_id LIKE 'infor:Pedido:%';
        """);
        foreach(var r in Database.Rows(db,"SELECT Pedido_id,Aluno FROM raw_Pedido GROUP BY Pedido_id,Aluno HAVING count(*)>1"))Database.Execute(db,"INSERT INTO import_issues(table_name,source_id,relation,target_id,detail) VALUES('Pedido',$0,'Número + aluno ambíguos',$1,'Há mais de uma matrícula com este número para o mesmo aluno; os vínculos operacionais não foram presumidos.')",r["Pedido_id"],r["Aluno"]);
        Database.Execute(db,"INSERT INTO metadata(key,value) VALUES('projectionSchema','2') ON CONFLICT(key) DO UPDATE SET value='2'");tx.Commit();
    }
}

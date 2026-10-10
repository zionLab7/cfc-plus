using System.Text.Json;
namespace CfcPilot;
public static class StudentDedupChecks
{
 public static void Run()
 {
  var root=Path.Combine(Path.GetTempPath(),"cfc-dedup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  using var db=Database.Open(Path.Combine(root,"test.sqlite"));Database.Initialize(db);
  void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
  Student Student(string id,string source,string name,string birth,string cpf,string unit)=>new(){Id=id,SourceId=source,Imported=true,SourceTable="Aluno",Name=name,Birth=birth,Cpf=cpf,UnitId=unit,Phones=["(11) 99999-1234","11999991234"]};
  Database.SaveEntity(db,"Students",Student("a","1","Teste Árvore","2000-01-01","52998224725","one"));
  Database.SaveEntity(db,"Students",Student("b","2","  TESTE ARVORE ","2000-01-01","529.982.247-25","two"));
  Database.SaveEntity(db,"Students",Student("c","3","Outra Pessoa","1999-01-01","11144477735","one"));
  Database.SaveEntity(db,"Students",Student("d","4","Nome Divergente","1999-01-01","11144477735","two"));
  Database.SaveEntity(db,"Students",Student("e","5","CPF Inválido","1999-01-01","11111111111","one"));
  Database.SaveEntity(db,"Students",Student("f","6","CPF Inválido","1999-01-01","11111111111","one"));
  Database.SaveEntity(db,"Enrollments",new Enrollment{Id="en-one",StudentId="a",UnitId="one"});
  Database.SaveEntity(db,"Enrollments",new Enrollment{Id="en-two",StudentId="b",UnitId="two"});
  Database.SaveEntity(db,"Entries",new Entry{Id="entry-one",StudentId="a",UnitId="one",Amount=125.35m});
  Database.SaveEntity(db,"Entries",new Entry{Id="entry-two",StudentId="b",UnitId="two",Amount=-70.20m});
  Database.SaveEntity(db,"Lessons",new Lesson{Id="lesson",StudentId="a",EnrollmentId="en-one"});
  Database.SaveEntity(db,"Users",new User{Id="student-user",Role="Aluno",LinkedId="a"});
  Database.Execute(db,"CREATE TABLE raw_sample(Aluno TEXT,value TEXT);INSERT INTO raw_sample VALUES('1','original one'),('2','original two');");
  var before=Database.Rows(db,"SELECT id,amount,unit_id FROM entities WHERE kind='Entries' ORDER BY id");
  var report=StudentDedup.Run(db);
  Check(Database.Count(db,"SELECT count(*) FROM entities WHERE kind='Students'")==5,"Only proven duplicates are merged");
  Check(Database.Count(db,"SELECT count(*) FROM student_aliases")==1,"Original alias profile is retained");
  Check(Database.Count(db,"SELECT count(*) FROM entities WHERE kind='Enrollments' AND student_id='b'")==2,"Enrollments from both units preserved");
  Check(Database.Scalar(db,"SELECT unit_id FROM entities WHERE id='en-one'")=="one","Original enrollment unit preserved");
  Check(JsonSerializer.Serialize(before)==JsonSerializer.Serialize(Database.Rows(db,"SELECT id,amount,unit_id FROM entities WHERE kind='Entries' ORDER BY id")),"Financial IDs, amounts and units unchanged");
  Check(Database.Scalar(db,"SELECT json_extract(json,'$.studentId') FROM entities WHERE id='lesson'")=="b","Lesson association remapped");
  Check(Database.Scalar(db,"SELECT json_extract(json,'$.linkedId') FROM entities WHERE id='student-user'")=="b","Student portal association remapped");
  var sources=StudentDedup.Sources(db,"b");Check(sources.Length==2,"All source IDs remain available");Check(Database.Count(db,"SELECT count(*) FROM entities WHERE kind='Students' AND EXISTS(SELECT 1 FROM json_each(json_extract(entities.json,'$.legacyUnitIds')) WHERE value='one')")==1,"Search retains membership of original unit");
  Check(Database.Count(db,"SELECT count(*) FROM raw_sample WHERE Aluno IN(SELECT value FROM json_each($0))",JsonSerializer.Serialize(sources))==2,"Legacy queries include both originals");
  Check(Database.Count(db,"SELECT count(*) FROM import_issues")==4,"Ambiguous and invalid CPFs remain flagged");
  StudentDedup.Run(db);Check(Database.Count(db,"SELECT count(*) FROM student_aliases")==1,"Deduplication is idempotent");
  Check(Database.Scalar(db,"PRAGMA integrity_check")=="ok","SQLite integrity");
  Console.WriteLine("13 verificações de deduplicação aprovadas: identidade, unidades, financeiro, vínculos, arquivos originais, ambiguidade e repetição segura.");
 }
}

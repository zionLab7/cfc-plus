using System.Text.Json;
namespace CfcPilot;
// Trusted server CLI; preserves the bootstrap administrator and never replaces a school already in operation.
public static class OperatorImport
{
 public static void Activate(string root)
 {
  root=Path.GetFullPath(root);var active=Path.Combine(root,"active-database.json");
  using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"import-report.json")));
  var id=report.RootElement.GetProperty("importId").GetString()!;
  if(!System.Text.RegularExpressions.Regex.IsMatch(id,"^[0-9]{8}T[0-9]{6}-[a-f0-9]{8}$"))throw new InvalidDataException("Importação inválida.");
  using var manifest=JsonDocument.Parse(File.ReadAllText(active));
  var previous=Path.GetFullPath(Path.Combine(root,manifest.RootElement.GetProperty("path").GetString()!));
  if(!previous.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new InvalidDataException("Base anterior fora da escola.");
  User admin;
  using(var old=Database.Open(previous,true))
  {
   if(Database.Count(old,"SELECT count(*) FROM entities WHERE kind NOT IN ('Users','Units','Templates','Audit')")!=0)throw new InvalidOperationException("Escola já possui dados operacionais; ativação recusada.");
   admin=JsonSerializer.Deserialize<User>(Database.Scalar(old,"SELECT json FROM entities WHERE kind='Users' AND id='admin'"),Database.Compact)!;
   if(admin is not{Active:true,Role:"Administrador"})throw new InvalidDataException("Administrador inicial não encontrado.");
  }
  var next=Path.Combine(root,"imports",id,"cfc.sqlite");
  using(var db=Database.Open(next))
  {
   if(Database.Scalar(db,"PRAGMA integrity_check")!="ok"||Database.Count(db,"SELECT SUM(imported) FROM import_tables")!=report.RootElement.GetProperty("total").GetInt64())throw new InvalidDataException("Relatório e banco não conferem.");
   using var tx=db.BeginTransaction();
   Database.Execute(db,"UPDATE entities SET json=json_set(json,'$.active',json('false')) WHERE kind='Users'");
   Database.SaveEntity(db,"Users",admin);
   Database.Execute(db,"INSERT INTO metadata(key,value) VALUES('installationAdmin',$0) ON CONFLICT(key) DO UPDATE SET value=$0",admin.Id);
   tx.Commit();Database.Execute(db,"PRAGMA wal_checkpoint(TRUNCATE)");
  }
  var backup=Path.Combine(root,"backups","before-"+id);Directory.CreateDirectory(backup);File.Copy(active,Path.Combine(backup,"active-database.json"),false);
  CommercialInstallation.Write(active,new{path=Path.GetRelativePath(root,next),importId=id});
  Console.WriteLine("Base conferida ativada. Administrador inicial preservado; usuários importados aguardam revisão. Manifesto anterior guardado para reversão.");
 }
}

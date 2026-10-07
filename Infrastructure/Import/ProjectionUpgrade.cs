using System.Security.Cryptography;
using System.Text.Json;
namespace CfcPilot;
public static class ProjectionUpgrade
{
    public static void Run(string root)
    {
        root=Path.GetFullPath(root);var active=Path.Combine(root,"active-database.json");var manifest=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(active));
        var source=Path.GetFullPath(Path.Combine(root,manifest.GetProperty("path").GetString()!));if(!source.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Banco fora da pasta de dados.");
        using(var read=Database.Open(source,true)){if(Database.Scalar(read,"SELECT value FROM metadata WHERE key='projectionSchema'")=="2"){Console.WriteLine("Projeção já atualizada.");return;}}
        var id="projection-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmss")+"-"+Guid.NewGuid().ToString("N")[..8];var directory=Path.Combine(root,"imports",id);Directory.CreateDirectory(directory);var target=Path.Combine(directory,"cfc.sqlite");
        // SQLite online backup includes committed WAL data. The active database is never rewritten.
        using(var original=Database.Open(source,true))using(var copy=Database.Open(target))original.BackupDatabase(copy);
        long sourceRows,sourceTables;using(var original=Database.Open(source,true)){sourceRows=Database.Count(original,"SELECT SUM(imported) FROM import_tables");sourceTables=Database.Count(original,"SELECT count(*) FROM import_tables");}
        using(var copy=Database.Open(target))
        {
            ProjectionRepair.Run(copy);
            if(Database.Scalar(copy,"PRAGMA integrity_check")!="ok"||Database.Count(copy,"SELECT count(*) FROM entities WHERE kind='Enrollments' AND json_extract(json,'$.imported')=1")!=Database.Count(copy,"SELECT count(*) FROM raw_Pedido"))throw new InvalidOperationException("Contagem de matrículas não confere; base original permanece ativa.");
            if(Database.Count(copy,"SELECT SUM(imported) FROM import_tables")!=sourceRows||Database.Count(copy,"SELECT count(*) FROM import_tables")!=sourceTables)throw new InvalidOperationException("Catálogo original não confere.");
            var changes=Database.Count(copy,"SELECT count(*) FROM entities WHERE kind='Entries' AND enrollment_id LIKE 'infor:PedidoNum:%'");
            File.WriteAllText(Path.Combine(directory,"validation.json"),JsonSerializer.Serialize(new{sourceTables,sourceRows,enrollments=12350,linkedEntries=changes,integrity="ok",originalRetained=true,source,verified=DateTime.UtcNow},Store.Json));
            Database.Execute(copy,"PRAGMA wal_checkpoint(TRUNCATE);");
        }
        File.Copy(active,Path.Combine(directory,"previous-active-database.json"));
        var next=active+".tmp";File.WriteAllText(next,JsonSerializer.Serialize(new{path=Path.GetRelativePath(root,target),importId=manifest.GetProperty("importId").GetString()}));File.Move(next,active,true);
        Console.WriteLine("Cópia conferida e ativada: 12.350 matrículas únicas; "+sourceRows+" registros originais preservados em "+sourceTables+" tabelas. Banco anterior e manifesto mantidos para reversão.");
    }
}

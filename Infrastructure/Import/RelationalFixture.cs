using System.Text.Json;
namespace CfcPilot;
public static class RelationalFixture
{
    public static void Create(string path)
    {
        path=Path.GetFullPath(path);var allowed=Path.Combine(Directory.GetCurrentDirectory(),"test-results")+Path.DirectorySeparatorChar;
        if(!path.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||File.Exists(Path.Combine(path,"active-database.json")))throw new InvalidOperationException("Fixture exige uma pasta nova de test-results.");
        Directory.CreateDirectory(path);var state=Seed.Create();using var db=Database.Open(Path.Combine(path,"fixture.sqlite"));Database.Initialize(db);
        foreach(var property in typeof(State).GetProperties().Where(p=>p.PropertyType.IsGenericType&&p.PropertyType.GetGenericTypeDefinition()==typeof(List<>)))foreach(var entity in (System.Collections.IEnumerable)property.GetValue(state)!)Database.SaveEntity(db,property.Name,entity);
        foreach(var (key,value) in new[]{("revision","0"),("policy",JsonSerializer.Serialize(state.Policy,Database.Compact)),("sourceTotal","0"),("importId","fixture"),("searchSchema","1"),("projectionSchema","2")})Database.Execute(db,"INSERT INTO metadata VALUES($0,$1)",key,value);
        File.WriteAllText(Path.Combine(path,"active-database.json"),"{\"path\":\"fixture.sqlite\",\"importId\":\"fixture\"}");Console.WriteLine("Fixture relacional fictícia criada.");
    }
}

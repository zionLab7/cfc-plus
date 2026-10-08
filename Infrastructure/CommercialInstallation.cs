using System.Security.Cryptography;
using System.Text.Json;
namespace CfcPilot;

// Each school owns an isolated database and directory, in standalone or central mode.
public static class CommercialInstallation
{
    public static bool Enabled(IConfiguration c)=>c["CFC_INSTALLATION_MODE"]=="commercial";
    public static string Root(IConfiguration c,IWebHostEnvironment e)=>Path.GetFullPath(c["CFC_DATA_DIR"]??Path.Combine(e.ContentRootPath,"App_Data"));
    public static void Prepare(IConfiguration c,IWebHostEnvironment e)
    {
        if(!Enabled(c))return;var root=Root(c,e);Directory.CreateDirectory(root);
        var id=c["CFC_SCHOOL_ID"]??"";if(!System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-z][a-z0-9-]{2,47}$"))throw new InvalidOperationException("CFC_SCHOOL_ID exige um identificador exclusivo de 3 a 48 caracteres.");
        var identity=Path.Combine(root,"installation.json");
        if(File.Exists(identity)){using var j=JsonDocument.Parse(File.ReadAllText(identity));if(j.RootElement.GetProperty("schoolId").GetString()!=id)throw new InvalidOperationException("O volume pertence a outra autoescola. Inicialização recusada.");}
        else Write(identity,new{schoolId=id,created=DateTimeOffset.UtcNow});
        if(File.Exists(Path.Combine(root,"active-database.json")))return;
        if(File.Exists(Path.Combine(root,"pilot.json")))throw new InvalidOperationException("Base JSON existente. Migre-a explicitamente antes de ativar modo comercial.");
        var login=c["CFC_ADMIN_LOGIN"]??"admin";if(login.Length is <3 or >80)throw new InvalidOperationException("CFC_ADMIN_LOGIN inválido.");
        var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var secret=c["CFC_ADMIN_PASSWORD_FILE"];if(!string.IsNullOrWhiteSpace(secret)){password=File.ReadAllText(secret).TrimEnd('\r','\n');if(password.Length is <12 or >128)throw new InvalidOperationException("A senha inicial deve conter de 12 a 128 caracteres.");}
        var state=new State();state.Units=[new(){Id="main",Name=c["CFC_SCHOOL_NAME"]??"Minha autoescola"}];
        state.Users=[new(){Id="admin",Name="Administrador",Login=login,Role="Administrador",PasswordHash=Passwords.Hash(password),MustChangePassword=true}];
        state.Templates=[new(){Id="enrollment",Name="Comprovante de matrícula",Text="Aluno: {{nome}}\nMatrícula: {{matricula}}\nUnidade: {{unidade}}\nServiço: {{servico}}\nData: {{data}}"}];
        var target=Path.Combine(root,File.Exists(Path.Combine(root,"school.sqlite"))?"school-"+Guid.NewGuid().ToString("N")+".sqlite":"school.sqlite");
        using(var db=Database.Open(target)){Database.Initialize(db);using var tx=db.BeginTransaction();foreach(var prop in typeof(State).GetProperties().Where(p=>p.PropertyType.IsGenericType&&p.PropertyType.GetGenericTypeDefinition()==typeof(List<>)))foreach(var item in (System.Collections.IEnumerable)prop.GetValue(state)!)Database.SaveEntity(db,prop.Name,item);
            foreach(var(key,value)in new[]{("revision","0"),("policy",JsonSerializer.Serialize(state.Policy,Database.Compact)),("sourceTotal","0"),("importId","school-"+id),("searchSchema","2"),("projectionSchema","2")})Database.Execute(db,"INSERT INTO metadata VALUES($0,$1)",key,value);tx.Commit();}
        Write(Path.Combine(root,"first-access.json"),new{login,password});
        Write(Path.Combine(root,"active-database.json"),new{path=Path.GetFileName(target),importId="school-"+id});
    }
    internal static void Write(string p,object data){var temp=p+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(data,Store.Json));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);File.Move(temp,p,true);}
    public static object Public(IConfiguration c)=>new{commercial=Enabled(c),schoolName=c["CFC_SCHOOL_NAME"]??"CFC+",version="0.4.0",portal="/portal/",install="/install/",desktop=File.Exists(Path.Combine(c["CFC_DATA_DIR"]??"App_Data","downloads","CFC-Plus-Windows.exe"))?"/api/distribution/windows":"",support=c["CFC_SUPPORT_EMAIL"]??""};
    public static void RequireAdmin(User u){if(u.Role!="Administrador")throw new RuleException("Somente o administrador gerencia esta instalação.",403);}
    public static bool Empty(Store s){if(s.DatabasePath==null)return false;using var db=Database.Open(s.DatabasePath,true);return Database.Count(db,"SELECT count(*) FROM entities WHERE kind NOT IN ('Users','Units','Templates','Audit')")==0;}
}

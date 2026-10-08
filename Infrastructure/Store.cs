using System.Security.Cryptography;
using System.Text.Json;

namespace CfcPilot;

public sealed class Store
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;
    private State? current;
    private readonly RelationalStore? database;
    public string DataRoot=>Path.GetDirectoryName(path)!;
    public bool IsReal => database != null;
    public string? DatabasePath => database?.Path;
    public async Task<Student?> StudentById(string id) => database != null ? database.Get<Student>("Students",id) : (await Read()).Students.Find(x=>x.Id==id);
    public async Task<State> Community(string role,string linkedId) => database != null ? database.Community(role,linkedId) : await Read();
    public async Task<List<BrowserProfile>> Profiles()=>database!=null?database.Profiles():(await Read()).BrowserProfiles;
    public Store(IConfiguration config, IWebHostEnvironment env)
    {
        var dir = config["CFC_DATA_DIR"] ?? Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        path = Path.Combine(dir, "pilot.json");
        var active = Path.Combine(dir, "active-database.json");
        if (File.Exists(active))
        {
            var manifest = JsonDocument.Parse(File.ReadAllText(active));
            var fullDirectory = Path.GetFullPath(dir);
            var databasePath = Path.GetFullPath(Path.Combine(fullDirectory, manifest.RootElement.GetProperty("path").GetString() ?? ""));
            if (!databasePath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(databasePath)) throw new InvalidDataException("Manifesto da base real inválido.");
            database = new(databasePath); return;
        }
        current = File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Base inválida") : Seed.Create();
        if (!File.Exists(path))
        {
            if(config["CFC_REQUIRE_BOOTSTRAP"]=="1")
            {
                var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
                foreach(var user in current.Users){if(user.Login=="admin"){user.PasswordHash=Passwords.Hash(password);user.Active=true;}else user.Active=false;}
                var credentials=Path.Combine(dir,"first-access.json");File.WriteAllText(credentials,JsonSerializer.Serialize(new{login="admin",password},Json));
                if(!OperatingSystem.IsWindows())File.SetUnixFileMode(credentials,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            }
            File.WriteAllText(path, JsonSerializer.Serialize(current, Json));
        }
    }
    public async Task<State> Read(StateScope? scope = null)
    {
        await gate.WaitAsync();
        try { return database != null ? database.Read(scope) : Clone(current!); } finally { gate.Release(); }
    }
    public async Task<User?> UserById(string id) => database != null ? database.Get<User>("Users", id) : (await Read()).Users.Find(x => x.Id == id);
    public async Task<List<User>> UsersByLogin(string login,string unit) => database != null ? database.Logins(login,unit) : (await Read()).Users.Where(x => x.Login.Equals(login,StringComparison.OrdinalIgnoreCase)).ToList();
    public object LoginUnits() => database?.LoginUnits() ?? Array.Empty<object>();
    public List<Student> SearchStudents(string query, string unit) => database?.Search(query, unit) ?? [];
    public object Ledger(string unit,string from,string to,int page) => database?.Ledger(unit,from,to,page) ?? throw new RuleException("Consulta paginada disponível na base real.");
    public async Task<string> Mutate(string user, string operationId, string fingerprint, Func<State, object> action, StateScope? scope = null)
    {
        await gate.WaitAsync();
        try
        {
            var key = user + ":" + operationId;
            if (database != null) return database.Mutate(user, operationId, fingerprint, action, scope);
            if (current!.Processed.TryGetValue(key, out var prior))
            {
                if (prior.Fingerprint != fingerprint) throw new RuleException("Identificador reutilizado para outra operação.", 409);
                return prior.Result;
            }
            var next = Clone(current);
            var result = JsonSerializer.Serialize(action(next), Json);
            next.Revision++;
            next.Processed[key] = new() { Fingerprint = fingerprint, Result = result };
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(next, Json));
            File.Move(temp, path, true);
            current = next;
            return result;
        }
        finally { gate.Release(); }
    }
    private static State Clone(State value) => JsonSerializer.Deserialize<State>(JsonSerializer.Serialize(value, Json), Json)!;
}

public static class Passwords
{
    public static string Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(value, salt, 150_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }
    public static bool Verify(string value, string stored)
    {
        if(value.Length>128)return false;
        try{var parts=stored.Split(':');return parts.Length==2&&CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(parts[1]),Rfc2898DeriveBytes.Pbkdf2(value,Convert.FromBase64String(parts[0]),150_000,HashAlgorithmName.SHA256,32));}catch(FormatException){return false;}
    }
}

public static class Seed
{
    public static State Create()
    {
        var s = new State();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Sao_Paulo"));
        s.Units = [new() { Id = "u1", Name = "Centro · demonstração", Address = "São Paulo / SP" }, new() { Id = "u2", Name = "Zona Sul · demonstração", Address = "São Paulo / SP" }];
        s.Packages = [new() { Id = "p1", Name = "Habilitação B", Price = 2200, Category = "B", Lessons = 20 }, new() { Id = "p2", Name = "Habilitação A", Price = 1800, Category = "A", Lessons = 20 }, new() { Id = "p3", Name = "Adição B", Service = "Adição", Price = 1400, Lessons = 15 }];
        string[] names = ["Ana Martins", "Bruno Costa", "Camila Souza", "Diego Lima", "Elisa Santos", "Felipe Rocha", "Gabriela Alves", "Hugo Pereira", "Isabela Ramos", "João Oliveira", "Larissa Melo", "Marcos Dias"];
        for (var n = 0; n < names.Length; n++)
        {
            var id = "s" + (n + 1); var unit = n < 7 ? "u1" : "u2";
            s.Students.Add(new() { Id = id, Name = names[n] + " (teste)", Cpf = DemoCpf(n), UnitId = unit, Email = $"aluno{n + 1}@example.invalid", Phone = "", Address = "Endereço fictício, São Paulo/SP", Birth = "2000-01-01", Notes = "Cadastro fictício para demonstração.", MedicalUntil = today.AddYears(1).ToString("yyyy-MM-dd"), Ladv = n < 4, Renach = n < 4 ? "DEMO-" + (n + 1) : "" });
            if (n > 8) continue;
            var eid = "e" + (n + 1);
            s.Enrollments.Add(new() { Id = eid, StudentId = id, PackageId = "p1", UnitId = unit, Category = "B", Service = "Primeira habilitação", LessonLimit = 20, Price = 2200, Created = today.AddDays(-10).ToString("yyyy-MM-dd") });
            s.Entries.Add(new() { Id = "debit" + n, StudentId = id, EnrollmentId = eid, UnitId = unit, Kind = "Débito", Amount = 2200, Description = "Pacote Habilitação B", Date = today.AddDays(-10).ToString("yyyy-MM-dd"), UserId = "admin" });
            for (var p = 0; p < 4; p++) s.Installments.Add(new() { Id = $"part{n}-{p}", EnrollmentId = eid, StudentId = id, UnitId = unit, Amount = 550, Due = today.AddMonths(p).AddDays(n == 2 ? -5 : 5).ToString("yyyy-MM-dd"), Paid = p == 0 && n < 2 ? 550 : 0 });
            if (n < 2) s.Entries.Add(new() { Id = "paid" + n, StudentId = id, EnrollmentId = eid, UnitId = unit, Kind = "Recebimento", Amount = 550, Date = today.ToString("yyyy-MM-dd"), Receipt = $"REC-{n + 1:000000}", UserId = "admin", Description = "Entrada de demonstração", Method = "PIX", Allocations = new() { [$"part{n}-0"] = 550 } });
        }
        s.Instructors = [new() { Id = "i1", Name = "Ricardo Almeida (teste)", Category = "AB", UnitId = "u1", CnhUntil = today.AddYears(1).ToString("yyyy-MM-dd") }, new() { Id = "i2", Name = "Patrícia Silva (teste)", Category = "B", UnitId = "u1" }, new() { Id = "i3", Name = "André Ferreira (teste)", Category = "AB", UnitId = "u2" }];
        s.Vehicles = [new() { Id = "v1", Name = "Fiat Mobi · teste", Plate = "DEMO001", UnitId = "u1", Km = 28000 }, new() { Id = "v2", Name = "VW Polo · teste", Plate = "DEMO002", UnitId = "u1", Km = 19000 }, new() { Id = "v3", Name = "Honda CG · teste", Plate = "DEMO003", Category = "A", UnitId = "u2" }, new() { Id = "v4", Name = "Chevrolet Onix · teste", Plate = "DEMO004", UnitId = "u2", Status = "Manutenção", Maintenance = 350 }];
        for (var n = 0; n < 4; n++) s.Lessons.Add(new() { Id = "a" + n, StudentId = "s" + (n + 1), EnrollmentId = "e" + (n + 1), UnitId = "u1", InstructorId = n % 2 == 0 ? "i1" : "i2", VehicleId = n % 2 == 0 ? "v1" : "v2", Start = today.ToString("yyyy-MM-dd") + $"T{8 + n:00}:00:00", Minutes = 50 });
        s.Exams.Add(new() { Id = "x1", StudentId = "s1", EnrollmentId = "e1", Type = "Teórico", Start = today.AddDays(3).ToString("yyyy-MM-dd") + "T09:00:00", Location = "Local fictício de demonstração", UnitId = "u1" });
        s.Leads = [new() { Id = "l1", Name = "Daniela (teste)", Phone = "", UnitId = "u1", Stage = "Proposta enviada", Followup = today.ToString("yyyy-MM-dd"), Notes = "Interessada na categoria B." }, new() { Id = "l2", Name = "Pedro (teste)", UnitId = "u2", Stage = "Novo contato", Source = "Indicação" }];
        s.Tasks = [new() { Id = "t1", Title = "Conferir disponibilidade para novos alunos", UnitId = "u1", Due = today.ToString("yyyy-MM-dd"), Owner = "Gerente" }];
        s.Waitlist = [new() { Id = "w1", StudentId = "s5", UnitId = "u1", Availability = "Manhã, segunda a sexta", Since = today.AddDays(-2).ToString("yyyy-MM-dd") }];
        s.Templates = [new() { Id = "contract", Name = "Contrato de matrícula · modelo de teste", Text = "CONTRATO DE PRESTAÇÃO DE SERVIÇOS — DEMONSTRAÇÃO\n\nAluno: {{nome}}\nCPF: {{cpf}}\nUnidade: {{unidade}}\nServiço: {{servico}}\nCategoria: {{categoria}}\nAulas contratadas: {{aulas}}\nValor: {{valor}}\nData: {{data}}\n\nEste é um modelo fictício. Substitua pelas condições do contrato da autoescola antes do uso real." }, new() { Id = "attendance", Name = "Comprovante de matrícula", Text = "COMPROVANTE DE MATRÍCULA\n\n{{nome}} — CPF {{cpf}}\nMatrícula {{matricula}}\nServiço {{servico}} / categoria {{categoria}}\nUnidade {{unidade}}\nData {{data}}" }];
        foreach (var (id, name, role, link) in new[] { ("admin", "Administrador", "Administrador", ""), ("gerente", "Gerente", "Gerente", ""), ("atendente", "Atendente", "Atendente", ""), ("instrutor", "Ricardo", "Instrutor", "i1"), ("aluno", "Ana", "Aluno", "s1") }) s.Users.Add(new() { Id = id, Login = id, Name = name, Role = role, LinkedId = link, PasswordHash = Passwords.Hash("cfc2026") });
        return s;
    }
    public static string DemoCpf(int n)
    {
        var b = (123450000 + n).ToString();
        for (var len = 9; len <= 10; len++) { var sum = 0; for (var j = 0; j < len; j++) sum += (b[j] - '0') * (len + 1 - j); var digit = sum * 10 % 11; b += digit == 10 ? "0" : digit.ToString(); }
        return b;
    }
}

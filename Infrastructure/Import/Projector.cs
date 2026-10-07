using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CfcPilot;

public static class Projector
{
    public static string Id(string table, string value) => string.IsNullOrWhiteSpace(value) || value == "0" ? "" : "infor:" + table + ":" + value.Trim();
    public sealed class Row(Dictionary<string, string?> values)
    {
        public string this[string key] => values.GetValueOrDefault(key)?.Trim() ?? "";
        public decimal Money(string key) => decimal.TryParse(this[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? decimal.Round(n, 2, MidpointRounding.AwayFromZero) : 0;
        public int Int(string key) => int.TryParse(this[key], out var n) ? n : 0;
        public bool Flag(string key) => this[key] is "1" or "True" or "true";
        public string Date(string key) => DateTime.TryParse(this[key], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year >= 1900 ? d.ToString("yyyy-MM-dd") : "";
        public string Time(string key) => DateTime.TryParse(this[key], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("HH:mm") : TimeOnly.TryParse(this[key], out var time) ? time.ToString("HH:mm") : "";
        public string Start(string date, string time) => Date(date) is { Length: > 0 } day ? day + "T" + (Time(time) is { Length: > 0 } hour ? hour : "00:00") + ":00" : "";
    }
    static IEnumerable<Row> Rows(SqliteConnection db, string sql)
    {
        using var c = Database.Command(db, sql); using var r = c.ExecuteReader();
        while (r.Read()) { var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase); for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetString(i); yield return new Row(row); }
    }
    static string Category(string value) => value.Trim().ToUpperInvariant();
    static string ControlCategory(string value) => value switch { "1" => "A", "2" => "B", "4" => "S", "10" => "Teórico", _ => "Tipo " + value };
    public static async Task Run(SqliteConnection db, string directory, IDataProtector protector)
    {
        using var tx = db.BeginTransaction();
        void Save(string kind, object value) => Database.SaveEntity(db, kind, value);
        foreach (var r in Rows(db, "SELECT * FROM raw_Unidade")) Save("Units", new Unit { Id = r["unidade"], SourceId = r["unidade"], SourceTable = "Unidade", Imported = true, Name = r["NOME"], Cfc = r["ncfc"], Address = string.Join(", ", new[] { r["ENDERECO"], r["N"], r["Complemento"], r["BAIRRO"], r["CIDADE"], r["UF"], r["cep"] }.Where(x => x != "")) });
        var phones = Rows(db, "SELECT * FROM raw_AlunoTelefone ORDER BY __row").GroupBy(r => r["AlunoId"]).ToDictionary(g => g.Key, g => g.Select(r => r["Telefone"]).Where(x => x != "").ToList());
        var ladvs = Rows(db, "SELECT AlunoId FROM raw_Ladv GROUP BY AlunoId").Select(r => r["AlunoId"]).ToHashSet();
        foreach (var r in Rows(db, "SELECT * FROM raw_Aluno"))
        {
            var numbers = phones.GetValueOrDefault(r["Aluno"]) ?? [];
            Save("Students", new Student { Id = Id("Aluno", r["Aluno"]), SourceId = r["Aluno"], SourceTable = "Aluno", Imported = true, Name = r["Nome"], Cpf = string.Concat(r["Cpf"].Where(char.IsDigit)), Rg = r["Rg"], RgIssued = r.Date("Rgexpedicao"), Birth = r.Date("Dtnasc"), Email = r["Mail"], Phone = numbers.FirstOrDefault() ?? "", Phones = numbers, Mother = r["Mae"], Father = r["Pai"], Nationality = r["Nacionalidade"], Birthplace = r["Natural"], Address = string.Join(", ", new[] { r["End1"], r["N1"], r["Compl1"], r["Bairro1"], r["Municipio1"], r["Uf"], r["Cep1"] }.Where(x => x != "")), UnitId = r["Unidade"], Renach = r["Renach"], Process = r["Processo"], Category = Category(r["Cat"]), MedicalUntil = r.Date("Dtexmedico"), Ear = r.Flag("Ear"), Ladv = ladvs.Contains(r["Aluno"]), Blocked = r.Flag("Bloqueado"), Notes = r["Obs"] });
        }
        Console.WriteLine("Cadastros, telefones e unidades projetados.");
        var vehicleCategories = Rows(db, "SELECT carro,categoria FROM raw_Carro").ToDictionary(r => r["carro"], r => Category(r["categoria"]));
        foreach (var r in Rows(db, "SELECT * FROM raw_Carro")) Save("Vehicles", new Vehicle { Id = Id("Carro", r["carro"]), SourceId = r["carro"], SourceTable = "Carro", Imported = true, Name = r["marca"], Plate = r["placa"], Category = Category(r["categoria"]), UnitId = r["unidade"], Km = r.Int("KM"), Pcd = r.Flag("PCD"), Status = r.Flag("Desativado") ? "Inativo" : r.Flag("Suspenso") ? "Manutenção" : "Disponível" });
        var grades = Rows(db, "SELECT * FROM raw_Aulas").GroupBy(r => r["INSTRUTOR"]).ToDictionary(g => g.Key, g => g.ToList());
        var blocks = Rows(db, "SELECT * FROM raw_Bloqueiohorarioinstrutor ORDER BY Data").GroupBy(r => r["Instrutor"]).ToDictionary(g => g.Key, g => g.Select(r => new InstructorBlock { Start = r.Start("Data", "Data"), End = r.Start("Data", "Data") is { Length: > 0 } start ? DateTime.Parse(start, CultureInfo.InvariantCulture).AddMinutes(50).ToString("s") : "", Reason = r["MotivoBloqueio"] }).Where(x => x.Start != "").ToList());
        foreach (var r in Rows(db, "SELECT * FROM raw_Instrutor"))
        {
            var slots = new List<InstructorSlot>();
            foreach (var grade in grades.GetValueOrDefault(r["instrutor"]) ?? []) foreach (var (key, day) in new[] { ("DOM", 0), ("SEG", 1), ("TER", 2), ("QUA", 3), ("QUI", 4), ("SEX", 5), ("SAB", 6) }) if (grade.Time(key) is { Length: > 0 } time) slots.Add(new() { Since = grade.Date("DataInicio"), Day = day, Time = time });
            var vehicle = r["carro"] != "" ? r["carro"] : r["VEICULO"];
            Save("Instructors", new Instructor { Id = Id("Instrutor", r["instrutor"]), SourceId = r["instrutor"], SourceTable = "Instrutor", Imported = true, Name = r["nome"], Cpf = string.Concat(r["CPF_INS"].Where(char.IsDigit)), UnitId = r["unidade"], Category = r["Categoria"] != "" ? Category(r["Categoria"]) : vehicleCategories.GetValueOrDefault(vehicle) ?? "", DefaultVehicleId = Id("Carro", vehicle), Active = !r.Flag("Desativado") && !r.Flag("Suspenso"), CnhUntil = r.Date("VencCnh"), EcpfUntil = r.Date("VencEcpf"), Slots = slots, Blocks = blocks.GetValueOrDefault(r["instrutor"]) ?? [], Days = string.Join(',', slots.Select(x => x.Day).Distinct().Order()), WorkStart = slots.Count > 0 ? slots.Min(x => x.Time)! : "07:00", WorkEnd = slots.Count > 0 ? DateTime.Parse(slots.Max(x => x.Time)!, CultureInfo.InvariantCulture).AddMinutes(50).ToString("HH:mm") : "19:00", BlockFrom = r.Date("DataBloqueioIni"), BlockTo = r.Date("DataBloqueioFim"), HourRate = r.Money("vlpago") });
        }
        foreach (var r in Rows(db, "SELECT * FROM raw_Pacote")) Save("Packages", new Package { Id = Id("Pacote", r["Pacote_id"]), SourceId = r["Pacote_id"], SourceTable = "Pacote", Imported = true, Name = r["Pacote_Descricao"], Category = Category(r["Categoria"]), Service = r["Tipo"], Price = r.Money("Pacote_Valor"), Lessons = 0, NeedsConfiguration = true, Active = !r.Flag("Excluido") });
        var controls = Rows(db, "SELECT * FROM raw_Aulascontrole").GroupBy(r => r["Matricula"]).ToDictionary(g => g.Key, g => g.GroupBy(r => ControlCategory(r["Tipo_id"])).ToDictionary(x => x.Key, x => x.Sum(r => r.Int("AulasControle_TotalBrinde") + r.Int("Extra"))));
        var students = Rows(db, "SELECT Aluno,Cat FROM raw_Aluno").ToDictionary(r => r["Aluno"], r => Category(r["Cat"]));
        var packageCategories = Rows(db, "SELECT Pacote_id,Categoria FROM raw_Pacote").ToDictionary(r => r["Pacote_id"], r => Category(r["Categoria"]));
        foreach (var r in Rows(db, "SELECT * FROM raw_Pedido"))
        {
            var limits = controls.GetValueOrDefault(r["Pedido_id"]) ?? [];
            Save("Enrollments", new Enrollment { Id = Id("Pedido", r["Pedido_id"]), Number = r["Pedido_num"], SourceId = r["Pedido_id"], SourceTable = "Pedido", Imported = true, StudentId = Id("Aluno", r["Aluno"]), PackageId = Id("Pacote", r["Pacote_id"]), UnitId = r["Unidade"], Category = packageCategories.GetValueOrDefault(r["Pacote_id"]) ?? students.GetValueOrDefault(r["Aluno"]) ?? "", Service = r["Pedido_tipo"], Price = r.Money("Pedido_valor"), LessonLimits = limits, LessonLimit = limits.Where(x => x.Key is "A" or "B" or "C" or "D" or "E").Sum(x => x.Value), Created = r.Date("Pedido_data"), Status = r["Pedido_sit"] switch { "A" => "Ativa", "F" => "Encerrada", "P" => "Pendente", _ => "A conferir" } });
        }
        Console.WriteLine("Matrículas, controles por categoria, instrutores e frota projetados.");
        // Start and finish records remain intact in the raw table. Never count them as two lessons.
        var participants = """
        SELECT a.*,p.Aluno AS ParticipantAluno,p.Matricula AS ParticipantMatricula,p.BeginPresent,p.EndPresent,p.ItemFalta
        FROM raw_Agendaaula a LEFT JOIN (
          SELECT AgendaAula_id,Aluno,Matricula,MAX(CASE WHEN TipoAula='2' AND Presente='1' THEN 1 ELSE 0 END) AS BeginPresent,
          MAX(CASE WHEN TipoAula='3' AND Presente='1' THEN 1 ELSE 0 END) AS EndPresent,
          MAX(CASE WHEN SitItem='F' THEN 1 ELSE 0 END) AS ItemFalta
          FROM raw_Agendaitens GROUP BY AgendaAula_id,Aluno,Matricula
        ) p ON p.AgendaAula_id=a.AgendaAula_id
        ORDER BY a.__row
        """;
        long projectedLessons = 0;
        foreach (var r in Rows(db, participants))
        {
            var sid = r["ParticipantAluno"] != "" ? r["ParticipantAluno"] : r["AlunoId"]; var eid = r["ParticipantMatricula"] != "" ? r["ParticipantMatricula"] : r["MatriculaId"];
            var start = r.Start("Data", "Hora"); var present = r.Flag("BeginPresent") && r.Flag("EndPresent");
            var status = r["SitAula"] == "F" || r.Flag("ItemFalta") ? "Falta" : r["SitAula"] == "C" ? "Cancelada" : present ? "Realizada" : start != "" && string.CompareOrdinal(start, Operations.Now.ToString("s")) > 0 ? "Agendada" : "Registro histórico";
            Save("Lessons", new Lesson { Id = Id("Agendaaula", r["AgendaAula_id"]) + (sid != "" ? ":" + sid + ":" + eid : ""), SourceId = r["AgendaAula_id"], SourceTable = "Agendaaula", Imported = true, StudentId = Id("Aluno", sid), EnrollmentId = Id("Pedido", eid), InstructorId = Id("Instrutor", r["Instrutor"]), VehicleId = Id("Carro", r["VEICULO"]), UnitId = r["Unidade"], Start = start, Category = Category(r["Categoria"]), Status = status, SourceStatus = r["SitAula"], AttendanceStart = r.Flag("BeginPresent"), AttendanceEnd = r.Flag("EndPresent"), Validated = r["DIGINI"] != "" && r["DIGFIM"] != "" && r["DIGSIT"] == "2", Notes = r["Obs"] });
            if (++projectedLessons % 50000 == 0) Console.WriteLine("Aulas projetadas: " + projectedLessons);
        }
        foreach (var r in Rows(db, "SELECT * FROM raw_agendamento")) Save("Exams", new Exam { Id = Id("agendamento", r["ID"]), SourceId = r["ID"], SourceTable = "agendamento", Imported = true, StudentId = Id("Aluno", r["ALUNO"]), EnrollmentId = Id("Pedido", r["MATRICULA"]), UnitId = r["UNIDADE"], Start = r.Start("DATA", "HORA"), Type = r["TIPO"] switch { "Pratico" => "Prático", "Teorico" or "cad-teorico" => "Teórico", _ => r["TIPO"] }, Status = r["RESULTADO"] switch { "APROVADO" => "Aprovado", "REPROVADO" => "Reprovado", "FALTA" or "FALTOU" or "FALTA ( ABONADA )" => "Falta", "CANCELADO" => "Cancelado", "" => "Agendado", _ => r["RESULTADO"] }, Protocol = r["IdEcnh"], Location = r["LOCAL"], InstructorId = Id("Instrutor", r["Instrutor"]), VehicleId = Id("Carro", r["Veiculo"]) });
        Console.WriteLine("Aulas e exames projetados.");
        var payments = Rows(db, "SELECT * FROM raw_FormaPagamento").ToDictionary(r => r["ID"], r => r["DESCRICAO"]);
        long movements = 0;
        foreach (var r in Rows(db, "SELECT * FROM raw_Movimento"))
        {
            var debit = r["Modo"].Equals("DEBITO", StringComparison.OrdinalIgnoreCase); var amount = r.Money("Valorp");
            Save("Entries", new Entry { Id = Id("Movimento", r["Mov_id"]), SourceId = r["Mov_id"], SourceTable = "Movimento", Imported = true, StudentId = Id("Aluno", r["Aluno"]), EnrollmentId = Id("Pedido", r["Matricula"]), UnitId = r["Unidade"], Kind = debit ? "Débito" : "Recebimento", Amount = debit ? -amount : amount, Description = r["Descricao"], Date = r.Date("Data"), UserId = Id("Atend", r["Atend"]), Method = payments.GetValueOrDefault(r["Formaid"]) ?? r["Trans"], Receipt = debit ? "" : r["Lanc"], Closed = r["Fechamento"] != "" });
            if (++movements % 100000 == 0) Console.WriteLine("Movimentos financeiros projetados: " + movements);
        }
        foreach (var r in Rows(db, "SELECT * FROM raw_Movfinanca")) Save("Entries", new Entry { Id = Id("Movfinanca", r["Lanc_id"]), SourceId = r["Lanc_id"], SourceTable = "Movfinanca", Imported = true, UnitId = r["unidade"], Kind = r["LANC_TIPO"] == "R" ? "Receita" : r["LANC_TIPO"] == "D" ? "Despesa" : "Lançamento a conferir", Amount = r.Money("Lanc_valor"), Description = r["Lanc_des"], Date = r.Date("LANC_PAG") != "" ? r.Date("LANC_PAG") : r.Date("Lanc_data"), UserId = Id("Atend", r["ATEND"]), Method = r["Conta"], Closed = r["marcacao"] != "" });
        foreach (var r in Rows(db, "SELECT * FROM raw_Carne")) Save("Installments", new Installment { Id = Id("Carne", r["ID"]), SourceId = r["ID"], SourceTable = "Carne", Imported = true, StudentId = Id("Aluno", r["Aluno"]), EnrollmentId = Id("Pedido", r["Matricula"]), UnitId = r["Unidade"], Amount = r.Money("Valor"), Paid = r["Sit"] == "P" ? r.Money("Valor") : 0, Due = r.Date("Vencimento"), Status = r["Sit"] switch { "P" => "Paga", "A" => "Aberta", "C" => "Cancelada", _ => "A conferir" } });
        Console.WriteLine("Extrato e carnês projetados. Parcelas de cartão mantidas em módulo próprio, sem duplicar a dívida do aluno.");
        foreach (var r in Rows(db, "SELECT * FROM raw_Documento")) Save("Documents", new Document { Id = Id("Documento", r["ID"]), SourceId = r["ID"], SourceTable = "Documento", Imported = true, StudentId = Id("Aluno", r["Aluno"]), EnrollmentId = Id("Pedido", r["Matricula"]), Name = "Documento do legado · tipo " + r["DOCUMENTO"], Created = r.Date("DATA"), Text = "Registro documental importado. A exportação contém o tipo e os vínculos; não contém o conteúdo do modelo ou o arquivo assinado." });
        foreach (var r in Rows(db, "SELECT * FROM raw_Orcamento")) Save("Leads", new Lead { Id = Id("Orcamento", r["ID"]), SourceId = r["ID"], SourceTable = "Orcamento", Imported = true, Name = r["Nome"], Phone = r["Telefone"], UnitId = r["Unidade"], Notes = r["Obs"], Followup = r.Date("DataRetorno"), Stage = "Proposta enviada", Source = "Orçamento Infor" });
        var groups = Rows(db, "SELECT * FROM raw_Grupousuario").ToDictionary(r => r["Grupo_id"], r => r["Grupo_nome"]);
        var permissions = Rows(db, "SELECT g.Grupo_id,f.NivelFuncao_Funcao FROM raw_NivelFuncaoGrupo g JOIN raw_NivelFuncao f ON f.NivelFuncao_id=g.NivelFuncao_id").GroupBy(r => r["Grupo_id"]).ToDictionary(g => g.Key, g => g.Select(r => r["NivelFuncao_Funcao"]).ToList());
        var linked = Rows(db, "SELECT * FROM raw_Rel_atend_instrutor").GroupBy(r => r["USU_ID"]).ToDictionary(g => g.Key, g => g.First()["INSTRUTOR_ID"]);
        foreach (var r in Rows(db, "SELECT * FROM raw_Atend"))
        {
            var group = groups.GetValueOrDefault(r["Nivel"]) ?? (r["Nivel"] == "1" ? "Administrador" : "A conferir");
            var role = r["Nivel"] == "1" ? "Administrador" : group.Contains("Gerent", StringComparison.OrdinalIgnoreCase) ? "Gerente" : group.Contains("Instrutor", StringComparison.OrdinalIgnoreCase) ? "Instrutor" : "Atendente";
            var password = r["Senha"].StartsWith("sealed:") ? protector.Unprotect(r["Senha"][7..]) : "";
            var knownEncoding = password.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(password, @"^(?:[a-fA-F0-9]{32}|[a-fA-F0-9]{64}|\$2[aby]\$.*)$");
            Save("Users", new User { Id = Id("Atend", r["Cod"]), SourceId = r["Cod"], SourceTable = "Atend", Imported = true, Name = r["Nome"], Login = r["Login"], UnitId = r["Unidade"], Role = role, LegacyGroup = group, Permissions = permissions.GetValueOrDefault(r["Nivel"]) ?? [], LinkedId = role == "Instrutor" ? Id("Instrutor", linked.GetValueOrDefault(r["Cod"]) ?? "") : "", Active = !r.Flag("Desativado") && knownEncoding && r["Login"] != "" && (role != "Instrutor" || linked.ContainsKey(r["Cod"])), MustChangePassword = true, PasswordHash = Passwords.Hash(knownEncoding ? password : Convert.ToHexString(RandomNumberGenerator.GetBytes(24))) });
        }
        // The migration administrator is local and has a new random credential, never the public demo password.
        var adminPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        Save("Users", new User { Id = "admin", Name = "Administrador da migração", Login = "admin", Role = "Administrador", PasswordHash = Passwords.Hash(adminPassword), MustChangePassword = true });
        if(Environment.GetEnvironmentVariable("CFC_INSTALLATION_MODE")!="commercial")await File.WriteAllTextAsync(Path.Combine(directory, "acesso-migracao.txt"), "Administrador local da migração\nLogin: admin\nSenha: " + adminPassword + "\nAltere a senha antes de disponibilizar o acesso aos operadores.\n");
        foreach (var r in Rows(db, "SELECT * FROM raw_Diretor")) Save("BrowserProfiles", new BrowserProfile { Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("diretor:" + r["id"]))).ToLowerInvariant()[..32], Name = r["nome"], Kind = "Diretor", Created = Operations.Now.ToString("s") });
        foreach (var r in Rows(db, "SELECT * FROM raw_Instrutor WHERE Desativado='0'")) Save("BrowserProfiles", new BrowserProfile { Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("instrutor:" + r["instrutor"]))).ToLowerInvariant()[..32], Name = r["nome"], Kind = "Instrutor", InstructorId = Id("Instrutor", r["instrutor"]), Created = Operations.Now.ToString("s") });
        var policy = Rows(db, "SELECT * FROM raw_Numero ORDER BY __row").FirstOrDefault();
        Database.Execute(db, "INSERT INTO metadata(key,value) VALUES('policy',$0)", JsonSerializer.Serialize(new Policy { MaxDaily = policy?.Int("QtdAulaDia") is >= 1 and <= 10 ? policy.Int("QtdAulaDia") : 3, CancelHours = policy?.Int("prazoExclusaoAula") ?? 24, DebtMode = policy?["BloqueioCarne"] switch { "1" => "Bloqueio", "2" => "Liberação justificada", _ => "Aviso" } }, Database.Compact));
        tx.Commit();
    }
}

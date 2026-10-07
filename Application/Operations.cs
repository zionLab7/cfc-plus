using System.Globalization;
using System.Text.Json;

namespace CfcPilot;

public static class Operations
{
    public static string Id() => Guid.NewGuid().ToString("N");
    public static DateTime Now => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Sao_Paulo");
    public static string Today => Now.ToString("yyyy-MM-dd");
    static bool Manager(User u) => u.Role is "Administrador" or "Gerente";
    static void Require(bool condition, string message, int status = 400) { if (!condition) throw new RuleException(message, status); }
    static string Text(JsonElement b, string key) => b.TryGetProperty(key, out var v) ? v.GetString()?.Trim() ?? "" : "";
    static T Read<T>(JsonElement b) where T : new() => b.Deserialize<T>(Store.Json) ?? new();
    static decimal Money(JsonElement b, string key) { Require(b.TryGetProperty(key, out var value) && value.TryGetDecimal(out _), "Valor inválido."); var amount = value.GetDecimal(); Require(amount > 0 && amount <= 10_000_000 && decimal.Round(amount, 2) == amount, "Informe um valor positivo com até duas casas decimais."); return amount; }
    static DateTime Date(string value) { Require(DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed), "Data ou horário inválido."); return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified); }
    static void UnitExists(State s, string id) => Require(s.Units.Any(x => x.Id == id), "Unidade não encontrada.");
    static Student Student(State s, string id) => s.Students.Find(x => x.Id == id) ?? throw new RuleException("Aluno não encontrado.", 404);
    static Enrollment Enrollment(State s, string id) => s.Enrollments.Find(x => x.Id == id) ?? throw new RuleException("Matrícula não encontrada.", 404);
    static bool Cpf(string value)
    {
        if (value.Length != 11 || !value.All(char.IsAsciiDigit) || value.Distinct().Count() == 1) return false;
        for (var len = 9; len <= 10; len++) { var sum = 0; for (var n = 0; n < len; n++) sum += (value[n] - '0') * (len + 1 - n); var digit = sum * 10 % 11; if (value[len] - '0' != (digit == 10 ? 0 : digit)) return false; }
        return true;
    }
    public static decimal Balance(State s, string enrollmentId) => s.Entries.Where(x => x.EnrollmentId == enrollmentId).Sum(x => x.Kind switch { "Débito" or "Estorno" => x.Amount, "Recebimento" or "Crédito" => -x.Amount, _ => 0 });
    static void FinancialGate(State s, Enrollment e, User u, JsonElement b)
    {
        var overdue = s.Installments.Any(x => x.EnrollmentId == e.Id && x.Status != "Cancelada" && x.Paid < x.Amount && string.CompareOrdinal(x.Due, Today) < 0);
        if (!overdue || s.Policy.DebtMode == "Aviso") return;
        if (s.Policy.DebtMode == "Liberação justificada" && Manager(u) && Text(b, "reason").Length >= 5) return;
        throw new RuleException("Matrícula com parcelas vencidas. Verifique a política de bloqueio e a autorização.", 409);
    }
    static void ExceptionReason(State s,JsonElement body){if(s.Policy.RequireExceptionReason)Require(Text(body,"reason").Length>=5,"Informe a justificativa para esta exceção configurada.");}
    static void Configured(State s,bool enforce,bool condition,string message,JsonElement body,int status=409){if(enforce)Require(condition,message,status);else if(!condition)ExceptionReason(s,body);}
    static void Retroactive(State s,User user,DateTime date,string mode,JsonElement body)
    {
        Require(mode switch{"Administrador"=>user.Role=="Administrador","Gestão"=>Manager(user),"Equipe"=>user.Role is "Administrador" or "Gerente" or "Atendente",_=>false},"Lançamento retroativo bloqueado para seu perfil. O administrador pode configurar essa permissão.",403);
        Require(s.Policy.MaxRetroactiveDays==0||(Now.Date-date.Date).TotalDays<=s.Policy.MaxRetroactiveDays,"Data fora do limite retroativo configurado.",409);ExceptionReason(s,body);
    }
    public static object Execute(State s, User u, string action, JsonElement b)
    {
        if (u.Role == "Aluno") Require(action == "task" && Text(b, "title").Length > 0, "Seu perfil permite consultas e solicitações.", 403);
        if (u.Role == "Instrutor") Require(action == "lesson-status", "Seu perfil permite registrar aulas próprias.", 403);
        if (action is "policy" or "user" or "void" or "closecash" or "template" or "package" or "unit" or "vehicle" or "instructor" or "browser-profile") Require(Manager(u), "Esta operação exige gerente ou administrador.", 403);
        LegacyPermissions.Check(u,action);
        object result;
        switch (action)
        {
            case "student-browser-profile":
            {
                var student=Student(s,Text(b,"studentId"));var profile=s.BrowserProfiles.FirstOrDefault(x=>x.Kind=="Aluno"&&x.StudentId==student.Id);
                if(profile==null){profile=new(){Id=Id(),Name=student.Name,Kind="Aluno",StudentId=student.Id,Created=Now.ToString("s")};s.BrowserProfiles.Add(profile);}result=profile;break;
            }
            case "matriculate":
            {
                var studentId = Text(b, "studentId");
                if (studentId == "")
                {
                    Require(b.TryGetProperty("student", out var studentBody) && Text(studentBody, "id") == "", "Informe os dados do novo aluno.");
                    studentId = ((Student)Execute(s, u, "student", studentBody)).Id;
                }
                else Student(s, studentId);
                Require(b.TryGetProperty("enrollment", out var enrollmentBody) && enrollmentBody.ValueKind == JsonValueKind.Object, "Informe os dados da matrícula.");
                var enrollment = enrollmentBody.Deserialize<Dictionary<string, JsonElement>>(Store.Json) ?? throw new RuleException("Dados da matrícula inválidos.");
                enrollment["studentId"] = JsonSerializer.SerializeToElement(studentId);
                if (Text(b, "leadId") != "") enrollment["leadId"] = JsonSerializer.SerializeToElement(Text(b, "leadId"));
                var created = (Enrollment)Execute(s, u, "enroll", JsonSerializer.SerializeToElement(enrollment));
                result = new { studentId, enrollmentId = created.Id, enrollment = created }; break;
            }
            case "browser-profile":
            {
                var x = Read<BrowserProfile>(b);
                Require(x.Id == "", "Os perfis de navegador são criados separadamente.");
                Require(x.Kind is "Diretor" or "Instrutor", "Selecione diretor ou instrutor.");
                if (x.Kind == "Instrutor")
                {
                    var instructor = s.Instructors.Find(a => a.Id == x.InstructorId && a.Active) ?? throw new RuleException("Instrutor ativo não encontrado.");
                    Require(!s.BrowserProfiles.Any(a => a.InstructorId == instructor.Id), "Este instrutor já tem um perfil de navegador.", 409);
                    x.Name = instructor.Name;
                }
                else { Require(x.Name.Length is >= 3 and <= 120, "Informe o nome do diretor."); x.InstructorId = ""; }
                x.Id = Id(); x.Created = Now.ToString("s"); s.BrowserProfiles.Add(x); result = x; break;
            }
            case "student":
            {
                var x = Read<Student>(b); x.Cpf = new string(x.Cpf.Where(char.IsAsciiDigit).ToArray());
                Require(x.Name.Length >= 3 && x.Name.Length <= 120, "Informe o nome completo."); Require(Cpf(x.Cpf), "CPF inválido."); UnitExists(s, x.UnitId);
                Require(!s.Students.Any(a => a.Cpf == x.Cpf && a.Id != x.Id), "CPF já cadastrado.", 409);
                if (x.Birth != "") Require(Date(x.Birth).Date <= Now.Date, "Nascimento não pode estar no futuro.");
                if (x.Id == "") { x.Id = Id(); s.Students.Add(x); } else { var old = Student(s, x.Id); x.SourceId=old.SourceId;x.SourceTable=old.SourceTable;x.Imported=old.Imported;x.Process=b.TryGetProperty("process",out _)?x.Process:old.Process;x.Category=old.Category;x.Phones=old.Phones.ToList();if(x.Phone!=""&&!x.Phones.Contains(x.Phone))x.Phones.Insert(0,x.Phone); s.Students[s.Students.IndexOf(old)] = x; }
                result = x; break;
            }
            case "enroll":
            {
                var st = Student(s, Text(b, "studentId")); Require(!st.Blocked, "Aluno bloqueado.");
                var pack = s.Packages.Find(x => x.Id == Text(b, "packageId")) ?? throw new RuleException("Pacote não encontrado.");
                Require(pack.Active && !pack.NeedsConfiguration, "Configure a quantidade de aulas do pacote importado antes de uma nova matrícula.", 409);
                var discount = b.TryGetProperty("discount", out var d) ? d.GetDecimal() : 0;
                Require(discount >= 0 && discount < pack.Price && decimal.Round(discount, 2) == discount, "Desconto inválido.");
                if (discount > 0) Require(Manager(u), "Somente gerente ou administrador pode conceder desconto.", 403);
                var unit = Text(b, "unitId"); UnitExists(s, unit);
                var e = new Enrollment { Id = Id(), StudentId = st.Id, PackageId = pack.Id, UnitId = unit, Category = pack.Category, Service = pack.Service, LessonLimit = pack.Lessons, Price = pack.Price - discount, Created = Today };
                s.Enrollments.Add(e);
                s.Entries.Add(new() { Id = Id(), StudentId = st.Id, EnrollmentId = e.Id, UnitId = unit, Kind = "Débito", Amount = e.Price, Date = Today, UserId = u.Id, Description = pack.Name });
                var firstDue = Text(b, "firstDue"); Require(firstDue != "", "Informe o primeiro vencimento."); Date(firstDue);
                var parts = b.TryGetProperty("parts", out var p) ? p.GetInt32() : pack.Parts; Require(parts is >= 1 and <= 48, "Parcelamento deve ter entre 1 e 48 parcelas.");
                var piece = decimal.Round(e.Price / parts, 2, MidpointRounding.AwayFromZero);
                for (var n = 0; n < parts; n++) s.Installments.Add(new() { Id = Id(), StudentId = st.Id, EnrollmentId = e.Id, UnitId = unit, Amount = n == parts - 1 ? e.Price - piece * (parts - 1) : piece, Due = Date(firstDue).AddMonths(n).ToString("yyyy-MM-dd") });
                var template = s.Templates.Find(x => x.Id == "contract"); if (template != null) Generate(s, st, e, template);
                var leadId = Text(b, "leadId"); if (leadId != "") { var lead = s.Leads.Find(x => x.Id == leadId); if (lead != null) { lead.Stage = "Matriculado"; lead.StudentId = st.Id; } }
                result = e; break;
            }
            case "lesson":
            {
                var incoming = Read<Lesson>(b); var e = Enrollment(s, incoming.EnrollmentId); var st = Student(s, e.StudentId); incoming.StudentId = st.Id; incoming.UnitId = e.UnitId;
                Configured(s,s.Policy.RequireActiveEnrollment,e.Status=="Ativa","Matrícula não está ativa.",b);Configured(s,s.Policy.BlockBlockedStudents,!st.Blocked,"Aluno bloqueado.",b); FinancialGate(s, e, u, b);
                var instructor = s.Instructors.Find(x => x.Id == incoming.InstructorId) ?? throw new RuleException("Instrutor não encontrado.");
                var vehicle = s.Vehicles.Find(x => x.Id == incoming.VehicleId) ?? throw new RuleException("Veículo não encontrado.");
                Configured(s,s.Policy.RequireAvailableResources,instructor.Active&&vehicle.Status=="Disponível","Instrutor inativo ou veículo indisponível.",b);
                Configured(s,s.Policy.EnforceCategory,e.Category.Contains(vehicle.Category)&&instructor.Category.Contains(vehicle.Category),"Categorias do aluno, veículo e instrutor incompatíveis.",b);
                if (incoming.Id != "") Require(s.Lessons.Any(x => x.Id == incoming.Id && x.Status == "Agendada"), "Somente aulas agendadas podem ser remarcadas.");
                var starts = b.TryGetProperty("starts", out var dates) ? dates.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [incoming.Start];
                Require(starts.Length is >= 1 and <= 30 && (incoming.Id == "" || starts.Length == 1), "Selecione até 30 horários.");
                var added = new List<Lesson>();
                foreach (var startText in starts)
                {
                    var start = Date(startText); var end = start.AddMinutes(s.Policy.Minutes);
                    if(start<Now)Retroactive(s,u,start,s.Policy.RetroactiveLessons,b);
                    Configured(s,s.Policy.EnforceInstructorSchedule,instructor.Days.Split(',').Contains(((int)start.DayOfWeek).ToString()),"Instrutor não trabalha nesse dia.",b);
                    Configured(s,s.Policy.EnforceInstructorSchedule,TimeOnly.FromDateTime(start)>=TimeOnly.Parse(instructor.WorkStart)&&end.Date==start.Date&&TimeOnly.FromDateTime(end)<=TimeOnly.Parse(instructor.WorkEnd),"Horário fora da grade do instrutor.",b);
                    if(instructor.Imported&&instructor.Slots.Count>0){var valid=instructor.Slots.Where(x=>string.CompareOrdinal(x.Since,start.ToString("yyyy-MM-dd"))<=0).ToList();var since=valid.Select(x=>x.Since).DefaultIfEmpty("").Max();Configured(s,s.Policy.EnforceInstructorSchedule,valid.Any(x=>x.Since==since&&x.Day==(int)start.DayOfWeek&&x.Time==start.ToString("HH:mm")),"Horário fora da grade importada vigente.",b);}
                    Configured(s,s.Policy.EnforceInstructorBlocks,!instructor.Blocks.Any(x=>Date(x.Start)<end&&Date(x.End)>start),"Horário bloqueado para o instrutor.",b);
                    if(instructor.CnhUntil!="")Configured(s,s.Policy.CheckInstructorCnh,Date(instructor.CnhUntil).Date>=start.Date,"CNH do instrutor vencida.",b);
                    if(instructor.BlockFrom!=""&&instructor.BlockTo!="")Configured(s,s.Policy.EnforceInstructorBlocks,start.Date<Date(instructor.BlockFrom).Date||start.Date>Date(instructor.BlockTo).Date,"Instrutor bloqueado nesse período.",b);
                    var overlapping=s.Lessons.Where(x=>!x.ExcludedFromAgenda&&x.Id!=incoming.Id&&x.Status!="Cancelada"&&Date(x.Start)<end&&Date(x.Start).AddMinutes(x.Minutes)>start).ToList();
                    Require(!overlapping.Any(x=>x.Start==start.ToString("s")&&x.StudentId==st.Id&&x.InstructorId==instructor.Id&&x.VehicleId==vehicle.Id),"Aula idêntica já cadastrada; não será duplicada.",409);
                    Configured(s,s.Policy.EnforceStudentConflict,!overlapping.Any(x=>x.StudentId==st.Id),"Aluno já tem aula nesse intervalo.",b);
                    Configured(s,s.Policy.EnforceInstructorConflict,!overlapping.Any(x=>x.InstructorId==instructor.Id),"Instrutor já tem aula nesse intervalo.",b);
                    Configured(s,s.Policy.EnforceVehicleConflict,!overlapping.Any(x=>x.VehicleId==vehicle.Id),"Veículo já tem aula nesse intervalo.",b);
                    var categoryLimit=e.LessonLimits.GetValueOrDefault(vehicle.Category,e.LessonLimit);Configured(s,s.Policy.EnforceLessonBalance,s.Lessons.Count(x=>!x.ExcludedFromAgenda&&x.Id!=incoming.Id&&x.EnrollmentId==e.Id&&x.Status!="Cancelada"&&(e.LessonLimits.Count==0||x.Category==vehicle.Category||s.Vehicles.Any(v=>v.Id==x.VehicleId&&v.Category==vehicle.Category)))<categoryLimit,"Saldo de aulas do pacote esgotado.",b);
                    Configured(s,s.Policy.EnforceDailyLimit,s.Lessons.Count(x=>!x.ExcludedFromAgenda&&x.Id!=incoming.Id&&x.StudentId==st.Id&&x.Status!="Cancelada"&&Date(x.Start).Date==start.Date)<s.Policy.MaxDaily,"Limite diário de aulas atingido.",b);
                    var x = new Lesson { Id = incoming.Id == "" ? Id() : incoming.Id, StudentId = st.Id, EnrollmentId = e.Id, UnitId = e.UnitId, InstructorId = instructor.Id, VehicleId = vehicle.Id, Category=vehicle.Category, Start = start.ToString("yyyy-MM-ddTHH:mm:ss"), Minutes = s.Policy.Minutes };
                    s.Lessons.RemoveAll(a => a.Id == incoming.Id); s.Lessons.Add(x); added.Add(x);
                }
                result = added; break;
            }
            case "lesson-status":
            {
                var x = s.Lessons.Find(x => x.Id == Text(b, "id")) ?? throw new RuleException("Aula não encontrada.");
                if (u.Role == "Instrutor") Require(x.InstructorId == u.LinkedId, "Aula de outro instrutor.", 403);
                var next = Text(b, "status"); Require(next is "Realizada" or "Falta" or "Cancelada", "Situação inválida.");Require(!x.ExcludedFromAgenda,"Registro vinculado não pode ser tratado como outra aula.",409);
                if(x.Status!="Agendada"){Require(s.Policy.AllowHistoricalStatusEdits&&u.Role=="Administrador","Correção de situação histórica desabilitada ou não autorizada.",403);ExceptionReason(s,b);}
                if (next == "Cancelada" && Date(x.Start) < Now.AddHours(s.Policy.CancelHours)) Require(Manager(u) && Text(b, "reason").Length >= 5, "Cancelamento fora do prazo exige gerente e justificativa.", 403);
                if (next is "Realizada" or "Falta") Require(Date(x.Start) <= Now, "Aula ainda não iniciou.");
                x.Status = next; x.Notes = Text(b, "notes") + (Text(b, "reason") != "" ? "\nMotivo: " + Text(b, "reason") : ""); result = x; break;
            }
            case "entry":
            case "receive":
            {
                var amount = Money(b, "amount"); var eid = Text(b, "enrollmentId"); var unit = Text(b, "unitId"); var kind = action == "receive" ? "Recebimento" : Text(b, "kind");
                Require(kind is "Recebimento" or "Débito" or "Crédito" or "Despesa" or "Receita", "Tipo de lançamento inválido.");
                if (kind == "Crédito") Require(Manager(u), "Crédito exige gerente ou administrador.", 403);
                Enrollment? enrollment = null; if (kind is "Recebimento" or "Débito" or "Crédito") { enrollment = Enrollment(s, eid); unit = enrollment.UnitId; }
                UnitExists(s, unit); if (kind is "Recebimento" or "Crédito") Require(amount <= Balance(s, eid), "Valor excede o saldo devedor.", 409);
                var entryDate=Text(b,"date")==""?Now.Date:Date(Text(b,"date")).Date;if(entryDate<Now.Date)Retroactive(s,u,entryDate,s.Policy.RetroactiveEntries,b);if(entryDate>Now.Date){Require(s.Policy.AllowFutureEntries,"Lançamentos futuros desabilitados.",409);ExceptionReason(s,b);}
                var x = new Entry { Id = Id(), StudentId = enrollment?.StudentId ?? "", EnrollmentId = enrollment?.Id ?? "", UnitId = unit, Kind = kind, Amount = amount, Method = Text(b, "method"), Description = Text(b, "description"), UserId = u.Id, Date = entryDate.ToString("yyyy-MM-dd") };
                Require(x.Description.Length >= 3, "Informe uma descrição."); Require(new[] { "PIX", "Dinheiro", "Cartão", "Boleto", "Transferência", "Ajuste" }.Contains(x.Method), "Forma de pagamento inválida.");
                if (kind is "Recebimento" or "Receita") x.Receipt = Receipt(s,unit,entryDate.Year);
                if (kind is "Recebimento" or "Crédito")
                {
                    var rest = amount;
                    foreach (var part in s.Installments.Where(a => a.EnrollmentId == eid && a.Status != "Cancelada" && a.Paid < a.Amount).OrderBy(a => a.Due)) { var applied = Math.Min(rest, part.Amount - part.Paid); part.Paid += applied; x.Allocations[part.Id] = applied; rest -= applied; if (rest == 0) break; }
                }
                if (kind == "Débito") s.Installments.Add(new() { Id = Id(), EnrollmentId = eid, StudentId = x.StudentId, UnitId = unit, Amount = amount, Due = Text(b, "due") != "" ? Date(Text(b, "due")).ToString("yyyy-MM-dd") : Today });
                s.Entries.Add(x); result = x; break;
            }
            case "void":
            {
                var entry = s.Entries.Find(x => x.Id == Text(b, "id")) ?? throw new RuleException("Lançamento não encontrado.");
                Require(!entry.Imported, "O estorno de recebimento importado exige conciliar as parcelas e os vínculos originais. Consulte o histórico do Infor antes de ajustar.", 409);
                Require(entry.Kind is "Recebimento" or "Crédito", "Esta versão permite estornar recebimentos e créditos do aluno."); Require(!entry.Voided, "Lançamento já estornado.", 409); Require(Text(b, "reason").Length >= 5, "Informe a justificativa do estorno.");
                foreach (var (id, paid) in entry.Allocations) { var part = s.Installments.Find(x => x.Id == id); if (part != null) part.Paid -= paid; }
                entry.Voided = true;
                var reversal = new Entry { Id = Id(), StudentId = entry.StudentId, EnrollmentId = entry.EnrollmentId, UnitId = entry.UnitId, Kind = "Estorno", Amount = entry.Amount, Method = entry.Method, UserId = u.Id, Date = Today, Reverses = entry.Id, Description = Text(b, "reason") }; s.Entries.Add(reversal); result = reversal; break;
            }
            case "closecash":
            {
                var unit = Text(b, "unitId"); UnitExists(s, unit); var rows = s.Entries.Where(x => x.UnitId == unit && !x.Closed && x.Kind is "Recebimento" or "Receita" or "Despesa" or "Estorno").ToList(); Require(rows.Count > 0, "Nenhum movimento aberto nessa unidade."); foreach (var row in rows) row.Closed = true;
                result = new { count = rows.Count, total = rows.Sum(x => x.Kind is "Despesa" or "Estorno" ? -x.Amount : x.Amount) }; break;
            }
            case "exam":
            {
                var x = Read<Exam>(b); var e = Enrollment(s, x.EnrollmentId); x.StudentId = e.StudentId; x.UnitId = e.UnitId; var st = Student(s, x.StudentId); var start = Date(x.Start);
                Configured(s,s.Policy.RequireActiveEnrollment,e.Status=="Ativa","Matrícula não está ativa.",b);Configured(s,s.Policy.BlockBlockedStudents,!st.Blocked,"Aluno bloqueado.",b);if(start<=Now)Retroactive(s,u,start,s.Policy.RetroactiveExams,b);Require(x.Type is "Médico" or "Psicológico" or "Teórico" or "Prático", "Tipo de exame inválido."); Require(x.Location.Length >= 3, "Informe o local."); FinancialGate(s, e, u, b);
                Configured(s,!s.Policy.AllowMultipleOpenExams,!s.Exams.Any(a=>a.EnrollmentId==e.Id&&a.Type==x.Type&&a.Status is "Agendado" or "Aprovado"),"Já existe exame aberto ou aprovado desse tipo.",b);
                if(x.Type is "Teórico" or "Prático")Configured(s,s.Policy.RequireMedicalForExams,st.MedicalUntil!=""&&Date(st.MedicalUntil).Date>=start.Date,"Exame médico não está válido para essa data.",b,400);
                if (x.Type == "Prático")
                {
                    Configured(s,s.Policy.RequireLadvForPractical,st.Ladv,"LADV pendente.",b,400);Configured(s,s.Policy.RequireTheoryForPractical,s.Exams.Any(a=>a.EnrollmentId==e.Id&&a.Type=="Teórico"&&a.Status=="Aprovado"),"Aprovação teórica pendente.",b,400);
                    Configured(s,s.Policy.RequireLessonsForPractical,s.Lessons.Count(a=>!a.ExcludedFromAgenda&&a.EnrollmentId==e.Id&&a.Status=="Realizada"&&(!s.Policy.RequireExternalValidation||a.Validated))>=e.LessonLimit,"Carga prática concluída/validada insuficiente.",b,400);
                    Require(s.Instructors.Any(a=>a.Id==x.InstructorId)&&s.Vehicles.Any(a=>a.Id==x.VehicleId),"Selecione instrutor e veículo existentes.");
                    Configured(s,s.Policy.RequireAvailableResources,s.Instructors.Any(a=>a.Id==x.InstructorId&&a.Active)&&s.Vehicles.Any(a=>a.Id==x.VehicleId&&a.Status=="Disponível"),"Instrutor ou veículo indisponível.",b);
                    Configured(s,s.Policy.EnforceCategory,s.Vehicles.Any(a=>a.Id==x.VehicleId&&e.Category.Contains(a.Category)),"Veículo incompatível com a categoria.",b,400);
                }
                x.Id = Id(); x.Status = "Agendado"; s.Exams.Add(x); result = x; break;
            }
            case "exam-result":
            {
                var x = s.Exams.Find(x => x.Id == Text(b, "id")) ?? throw new RuleException("Exame não encontrado."); var next = Text(b, "status"); Require(next is "Aprovado" or "Reprovado" or "Falta" or "Cancelado", "Resultado inválido."); Require(x.Status == "Agendado", "Exame já finalizado.", 409); if (next != "Cancelado") Require(Date(x.Start) <= Now, "Exame ainda não aconteceu."); x.Status = next; result = x; break;
            }
            case "instructor": { var x = Read<Instructor>(b); Require(x.Name.Length >= 3 && x.Category.Length > 0, "Nome e categoria obrigatórios."); UnitExists(s, x.UnitId); Require(TimeOnly.TryParse(x.WorkStart, out var begin) && TimeOnly.TryParse(x.WorkEnd, out var finish) && begin < finish, "Grade inválida."); Require(x.Days.Split(',').All(a => int.TryParse(a, out var n) && n is >= 0 and <= 6), "Dias da grade inválidos."); if (x.BlockFrom != "" || x.BlockTo != "") Require(x.BlockFrom != "" && x.BlockTo != "" && Date(x.BlockFrom) <= Date(x.BlockTo), "Período de bloqueio inválido."); Upsert(s.Instructors, x, a => a.Id, (a, id) => a.Id = id); result = x; break; }
            case "vehicle": { var x = Read<Vehicle>(b); Require(x.Name.Length >= 3 && x.Plate.Length >= 6 && x.Category.Length == 1, "Nome, placa e categoria obrigatórios."); Require(x.Status is "Disponível" or "Manutenção" or "Inativo", "Situação inválida."); UnitExists(s, x.UnitId); Require(!s.Vehicles.Any(a => a.Id != x.Id && a.Plate.Equals(x.Plate, StringComparison.OrdinalIgnoreCase)), "Placa já cadastrada.", 409); Require(x.Km >= 0 && x.Fuel >= 0 && x.Maintenance >= 0, "Valores não podem ser negativos."); Upsert(s.Vehicles, x, a => a.Id, (a, id) => a.Id = id); result = x; break; }
            case "package": { var x = Read<Package>(b); Require(x.Name.Length >= 3 && x.Category.Length > 0 && x.Price > 0 && decimal.Round(x.Price, 2) == x.Price && x.Lessons is >= 0 and <= 200 && x.Parts is >= 1 and <= 48, "Dados do pacote inválidos."); x.NeedsConfiguration=false; Upsert(s.Packages, x, a => a.Id, (a, id) => a.Id = id); result = x; break; }
            case "unit": { var x = Read<Unit>(b); Require(x.Name.Length >= 3, "Nome obrigatório."); Upsert(s.Units, x, a => a.Id, (a, id) => a.Id = id); result = x; break; }
            case "lead": { var x = Read<Lead>(b); Require(x.Name.Length >= 3, "Nome obrigatório."); UnitExists(s, x.UnitId); Require(new[] { "Novo contato", "Em atendimento", "Proposta enviada", "Matriculado", "Perdido" }.Contains(x.Stage), "Etapa inválida."); if (x.Followup != "") Date(x.Followup); Upsert(s.Leads, x, a => a.Id, (a, id) => a.Id = id); result = x; break; }
            case "task": { var x = Read<PendingTask>(b); Require(x.Title.Length >= 3, "Descreva a tarefa."); if (u.Role == "Aluno") { var st = Student(s, u.LinkedId); x.Id = ""; x.UnitId = st.UnitId; x.Title = "Solicitação de " + st.Name + ": " + x.Title; x.Owner = "Atendimento"; x.Done = false; } UnitExists(s, x.UnitId); if (x.Due != "") Date(x.Due); Upsert(s.Tasks, x, a => a.Id, (a, id) => a.Id = id); result = x; break; }
            case "waitlist": { var x = Read<WaitItem>(b); Student(s, x.StudentId); UnitExists(s, x.UnitId); Require(x.Availability.Length >= 3, "Informe a disponibilidade."); Require(!s.Waitlist.Any(a => a.StudentId == x.StudentId && a.Id != x.Id), "Aluno já está na fila.", 409); x.Since = x.Since == "" ? Today : x.Since; Upsert(s.Waitlist, x, a => a.Id, (a, id) => a.Id = id); result = x; break; }
            case "wait-remove": { Require(s.Waitlist.RemoveAll(x => x.Id == Text(b, "id")) > 0, "Item não encontrado."); result = new { ok = true }; break; }
            case "template": { var x = Read<Template>(b); Require(x.Name.Length >= 3 && x.Text.Length >= 10 && x.Text.Length < 30_000, "Informe nome e conteúdo do modelo."); Upsert(s.Templates, x, a => a.Id, (a, id) => a.Id = id); result = x; break; }
            case "document": { var e = Enrollment(s, Text(b, "enrollmentId")); var template = s.Templates.Find(x => x.Id == Text(b, "templateId")) ?? throw new RuleException("Modelo não encontrado."); result = Generate(s, Student(s, e.StudentId), e, template); break; }
            case "policy": { Require(u.Role=="Administrador","Somente o administrador configura as regras.",403);var x=Read<Policy>(b);Require(x.CancelHours is >=0 and <=8760&&x.MaxDaily is >=1 and <=10000&&x.Minutes is >=1 and <=1440&&x.MaxRetroactiveDays is >=0 and <=36500&&x.DebtMode is "Aviso" or "Bloqueio" or "Liberação justificada","Política inválida.");Require(new[]{x.RetroactiveLessons,x.RetroactiveExams,x.RetroactiveEntries}.All(mode=>new[]{"Bloqueado","Administrador","Gestão","Equipe"}.Contains(mode)),"Permissão retroativa inválida.");s.Policy=x;result=x;break;}
            case "user":
            {
                Require(u.Role == "Administrador", "Somente administrador gerencia usuários.", 403); var x = Read<User>(b); Require(x.Name.Length >= 3 && x.Login.Length >= 3 && x.Role is "Administrador" or "Gerente" or "Atendente" or "Instrutor" or "Aluno", "Dados de usuário inválidos."); Require(!s.Users.Any(a => a.Login.Equals(x.Login,StringComparison.OrdinalIgnoreCase) && a.Id != x.Id), "Login já existe.", 409);
                if (x.Role == "Instrutor") Require(s.Instructors.Any(a => a.Id == x.LinkedId), "Vincule um instrutor."); if (x.Role == "Aluno") Student(s, x.LinkedId);
                var password = Text(b, "password"); var old = s.Users.Find(a => a.Id == x.Id); Require(old != null || password.Length >= 8, "Nova senha deve ter pelo menos oito caracteres."); if (password != "") Require(password.Length >= 8, "Senha muito curta."); x.MustChangePassword=password==""&&(old?.MustChangePassword??false);x.LegacyGroup=old?.LegacyGroup??"";x.Permissions=old?.Permissions??[]; x.PasswordHash = password != "" ? Passwords.Hash(password) : old!.PasswordHash;
                Require(x.Id != u.Id || (x.Active && x.Role == "Administrador"), "Você não pode desativar ou rebaixar seu próprio administrador."); Upsert(s.Users, x, a => a.Id, (a, id) => a.Id = id); result = new { x.Id, x.Name, x.Login, x.Role, x.Active }; break;
            }
            case "message-draft": { var x = Read<Message>(b); if (x.StudentId != "") Student(s, x.StudentId); Require(x.Text.Length is > 0 and <= 4000, "Mensagem deve conter entre 1 e 4.000 caracteres."); x.Id = Id(); x.Created = Now.ToString("s"); x.Status = "Rascunho"; s.Messages.Add(x); result = x; break; }
            default: throw new RuleException("Operação não disponível.", 404);
        }
        s.Audit.Add(new() { Id = Id(), User = u.Name, Action = action, Detail = action == "user" ? "Usuário atualizado" : Text(b, "reason") != "" ? Text(b, "reason") : Text(b, "name") != "" ? Text(b, "name") : Text(b, "id"), Date = Now.ToString("s") });
        return result;
    }
    static string Receipt(State state,string unit,int year)
    {
        if(!state.RealData)return "REC-"+(state.Entries.Count(x=>x.Receipt!="")+1).ToString("000000");
        var key=unit+":"+year;var next=state.ReceiptCounters.GetValueOrDefault(key)+1;state.ReceiptCounters[key]=next;return "REC-"+unit+"-"+year+"-"+next.ToString("000000");
    }
    static void Upsert<T>(List<T> list, T item, Func<T, string> getId, Action<T, string> setId)
    {
        var id = getId(item); if (id == "") { setId(item, Id()); list.Add(item); return; }
        var index = list.FindIndex(x => getId(x) == id); Require(index >= 0, "Registro não encontrado.", 404); if(item is LegacyEntity next && list[index] is LegacyEntity prior){next.SourceId=prior.SourceId;next.SourceTable=prior.SourceTable;next.Imported=prior.Imported;} if(item is Instructor updated && list[index] is Instructor original){updated.Slots=original.Slots;updated.Blocks=original.Blocks;updated.DefaultVehicleId=original.DefaultVehicleId;} list[index] = item;
    }
    static Document Generate(State s, Student st, Enrollment e, Template t)
    {
        var text = t.Text;
        foreach (var (key, value) in new Dictionary<string, string> { ["nome"] = st.Name, ["cpf"] = st.Cpf, ["unidade"] = s.Units.Find(a => a.Id == e.UnitId)?.Name ?? "", ["servico"] = e.Service, ["categoria"] = e.Category, ["aulas"] = e.LessonLimit.ToString(), ["valor"] = e.Price.ToString("C", CultureInfo.GetCultureInfo("pt-BR")), ["data"] = Now.ToString("dd/MM/yyyy"), ["matricula"] = e.Id }) text = text.Replace("{{" + key + "}}", value);
        var doc = new Document { Id = Id(), Name = t.Name, StudentId = st.Id, EnrollmentId = e.Id, Text = text, Created = Now.ToString("s") }; s.Documents.Add(doc); return doc;
    }
    public static object Snapshot(State s, User u)
    {
        s.Lessons=s.Lessons.Where(x=>!x.ExcludedFromAgenda).ToList();
        if (u.Role is "Aluno" or "Instrutor")
        {
            s.Statistics = [];
            var allowed = u.Role == "Aluno" ? new HashSet<string> { u.LinkedId } : s.Lessons.Where(x => x.InstructorId == u.LinkedId).Select(x => x.StudentId).ToHashSet();
            s.Students = s.Students.Where(x => allowed.Contains(x.Id)).Select(x => u.Role == "Aluno" ? x : new Student { Id = x.Id, Name = x.Name, UnitId = x.UnitId }).ToList();
            s.Enrollments = s.Enrollments.Where(x => allowed.Contains(x.StudentId)).ToList();
            if (u.Role == "Instrutor") foreach (var e in s.Enrollments) e.Price = 0;
            s.Lessons = s.Lessons.Where(x => u.Role == "Aluno" ? x.StudentId == u.LinkedId : x.InstructorId == u.LinkedId).ToList();
            s.Exams = s.Exams.Where(x => allowed.Contains(x.StudentId)).ToList();
            s.Entries = u.Role == "Aluno" ? s.Entries.Where(x => x.StudentId == u.LinkedId).ToList() : [];
            s.Installments = u.Role == "Aluno" ? s.Installments.Where(x => x.StudentId == u.LinkedId).ToList() : [];
            s.Documents = u.Role == "Aluno" ? s.Documents.Where(x => x.StudentId == u.LinkedId).ToList() : [];
            s.Tasks = []; s.Leads = []; s.Waitlist = []; s.Messages = []; s.Templates = []; s.Packages = []; s.Audit = [];
            s.Instructors = s.Instructors.Select(x => new Instructor { Id = x.Id, Name = x.Name, UnitId = x.UnitId }).ToList();
            s.Vehicles = s.Vehicles.Select(x => new Vehicle { Id = x.Id, Name = x.Name, Plate = x.Plate, UnitId = x.UnitId }).ToList();
        }
        if (s.RealData && u.Role is not ("Aluno" or "Instrutor")) foreach (var instructor in s.Instructors) instructor.Blocks = instructor.Blocks.Where(x => x.Start.Length >= 10 && string.CompareOrdinal(x.Start[..10], Operations.Now.AddDays(-7).ToString("yyyy-MM-dd")) >= 0).ToList();
        var users = u.Role == "Administrador" ? s.Users.Select(x => new { x.Id, x.Name, x.Login, x.Role, x.Active, x.LinkedId, x.UnitId, x.LegacyGroup, x.Permissions, x.Imported, x.SourceId, x.MustChangePassword }).ToArray() : [];
        return new { s.RealData, s.Revision, s.Units, s.Students, s.Enrollments, s.Packages, s.Lessons, s.Exams, s.Entries, s.Installments, s.Instructors, s.Vehicles, s.Leads, s.Tasks, s.Waitlist, s.Templates, s.Documents, s.Messages, s.Policy, s.Statistics, audit = Manager(u) ? s.Audit.OrderByDescending(x => x.Date).Take(500) : [], users, user = new { u.Id, u.Name, u.Login, u.Role, u.LinkedId, u.UnitId, u.LegacyGroup, u.Permissions, u.MustChangePassword }, demo = !s.RealData };
    }
}

using Microsoft.Data.Sqlite;
using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace CfcPilot;

public sealed class StateScope
{
    public string StudentId { get; set; } = "";
    public string UnitId { get; set; } = "";
    public string Query { get; set; } = "";
    public string View { get; set; } = "";
    public string From { get; set; } = Operations.Now.AddDays(-7).ToString("yyyy-MM-dd");
    public string To { get; set; } = Operations.Now.AddDays(8).ToString("yyyy-MM-dd");
    public int Page { get; set; } = 1;
    public string Action { get; set; } = "";
    public JsonElement Body { get; set; }
    public bool Mutation => Action != "";
}

public sealed class RelationalStore
{
    public string Path { get; }
    public RelationalStore(string path)
    {
        Path = path;
        using var db = Database.Open(path);
        if (Database.Scalar(db,"SELECT value FROM metadata WHERE key='searchSchema'") != "2")
        {
            using var tx=db.BeginTransaction();
            foreach(var student in List<Student>(db,"Students")){if(student.Cpf.Length is >0 and <11 && student.Cpf.All(char.IsAsciiDigit))student.Cpf=student.Cpf.PadLeft(11,'0');Database.SaveEntity(db,"Students",student);}
            Database.Execute(db,"INSERT INTO metadata(key,value) VALUES('searchSchema','2') ON CONFLICT(key) DO UPDATE SET value='2'"); tx.Commit();
        }
    }
    public T? Get<T>(string kind, string id)
    {
        using var db = Database.Open(Path, true); var json = Database.Scalar(db, "SELECT json FROM entities WHERE kind=$0 AND id=$1", kind, id);
        return json == "" ? default : JsonSerializer.Deserialize<T>(json, Database.Compact);
    }
    public List<User> Logins(string login,string unit)
    {
        using var db=Database.Open(Path,true);return List<User>(db,"Users","json_extract(json,'$.login')=$0 COLLATE NOCASE AND ($1='' OR unit_id=$1 OR id='admin')",login,unit);
    }
    public List<BrowserProfile> Profiles(){using var db=Database.Open(Path,true);return List<BrowserProfile>(db,"BrowserProfiles");}
    public object LoginUnits(){using var db=Database.Open(Path,true);return Database.Rows(db,"SELECT id,name FROM entities WHERE kind='Units' ORDER BY name");}
    static List<T> List<T>(SqliteConnection db, string kind, string where = "1=1", params object?[] args)
    {
        using var cmd = Database.Command(db, "SELECT json FROM entities WHERE kind='" + kind + "' AND (" + where + ")", args); using var reader = cmd.ExecuteReader(); var rows = new List<T>();
        while (reader.Read()) rows.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), Database.Compact)!); return rows;
    }
    // Portal reads never load staff credentials, unrelated finances or the global student directory.
    public State Community(string role,string linkedId)
    {
        using var db=Database.Open(Path,true);var s=new State{RealData=true};
        var p=Database.Scalar(db,"SELECT value FROM metadata WHERE key='policy'");s.Policy=p==""?new():JsonSerializer.Deserialize<Policy>(p,Database.Compact)!;
        s.Units=List<Unit>(db,"Units");
        s.Lessons=List<Lesson>(db,"Lessons",role=="Aluno"?"student_id=$0":"json_extract(json,'$.instructorId')=$0 AND date >= $1 AND date < $2",linkedId,Operations.Now.AddDays(-90).ToString("yyyy-MM-dd"),Operations.Now.AddDays(61).ToString("yyyy-MM-dd"));
        s.Lessons.RemoveAll(x=>x.ExcludedFromAgenda);
        s.Students=role=="Aluno"?List<Student>(db,"Students","id=$0",linkedId):List<Student>(db,"Students","id IN(SELECT student_id FROM entities WHERE kind='Lessons' AND json_extract(json,'$.instructorId')=$0 AND date >= $1 AND date < $2)",linkedId,Operations.Now.AddDays(-90).ToString("yyyy-MM-dd"),Operations.Now.AddDays(61).ToString("yyyy-MM-dd"));
        s.Instructors=List<Instructor>(db,"Instructors");s.Vehicles=List<Vehicle>(db,"Vehicles");
        if(role=="Aluno") {s.Enrollments=List<Enrollment>(db,"Enrollments","student_id=$0",linkedId);s.Exams=List<Exam>(db,"Exams","student_id=$0",linkedId);s.Entries=List<Entry>(db,"Entries","student_id=$0",linkedId);s.Installments=List<Installment>(db,"Installments","student_id=$0",linkedId);s.Documents=List<Document>(db,"Documents","student_id=$0",linkedId);}
        return s;
    }
    public State Read(StateScope? request = null)
    {
        using var db = Database.Open(Path, true); return Read(db, request ?? new());
    }
    State Read(SqliteConnection db, StateScope scope)
    {
        var s = new State { RealData = true, Revision = long.Parse(Database.Scalar(db, "SELECT value FROM metadata WHERE key='revision'"), CultureInfo.InvariantCulture), Policy = JsonSerializer.Deserialize<Policy>(Database.Scalar(db, "SELECT value FROM metadata WHERE key='policy'"), Database.Compact) ?? new() };
        var counters=Database.Scalar(db,"SELECT value FROM metadata WHERE key='receiptCounters'");s.ReceiptCounters=counters==""?[]:JsonSerializer.Deserialize<Dictionary<string,long>>(counters,Database.Compact)!;
        s.Units = List<Unit>(db, "Units"); s.Users = List<User>(db, "Users"); s.Packages = List<Package>(db, "Packages"); s.Instructors = List<Instructor>(db, "Instructors"); s.Vehicles = List<Vehicle>(db, "Vehicles"); s.BrowserProfiles = List<BrowserProfile>(db, "BrowserProfiles");
        s.Templates = List<Template>(db, "Templates"); s.Leads = List<Lead>(db, "Leads"); s.Tasks = List<PendingTask>(db, "Tasks"); s.Waitlist = List<WaitItem>(db, "Waitlist"); s.Messages = List<Message>(db, "Messages", "id IN(SELECT id FROM entities WHERE kind='Messages' ORDER BY date DESC LIMIT 1000)"); s.Audit = List<Audit>(db, "Audit", "id IN(SELECT id FROM entities WHERE kind='Audit' ORDER BY date DESC LIMIT 100)");
        var sid = scope.StudentId;
        if (scope.Mutation)
        {
            string B(string key) => scope.Body.ValueKind == JsonValueKind.Object && scope.Body.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var eid = B("enrollmentId");
            if (eid != "") sid = Database.Scalar(db, "SELECT student_id FROM entities WHERE kind='Enrollments' AND id=$0", eid);
            if (B("studentId") != "") sid = B("studentId");
            if(scope.Action=="user"&&B("role")=="Aluno")sid=B("linkedId");
            var targetKind = scope.Action switch { "student" => "Students", "lesson" or "lesson-status" => "Lessons", "exam-result" => "Exams", "void" => "Entries", "enroll" => "Students", _ => "" };
            if (B("id") != "" && targetKind != "") sid = Database.Scalar(db, "SELECT student_id FROM entities WHERE kind=$0 AND id=$1", targetKind, B("id"));
            var start = B("start");
            if (scope.Body.TryGetProperty("starts", out var dates) && dates.ValueKind == JsonValueKind.Array) { var values = dates.EnumerateArray().Select(x => x.GetString() ?? "").Order().ToArray(); if (values.Length > 0) { scope.From = values[0][..10]; scope.To = DateTime.Parse(values[^1], CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd"); } }
            else if (start.Length >= 10) { scope.From = start[..10]; scope.To = DateTime.Parse(start, CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd"); }
            s.Students = sid != "" ? List<Student>(db, "Students", "id=$0", sid) : [];
            var cpf = B("cpf"); if (scope.Body.TryGetProperty("student", out var newStudent) && newStudent.ValueKind == JsonValueKind.Object && newStudent.TryGetProperty("cpf", out var rawCpf)) cpf = rawCpf.GetString() ?? "";
            if (cpf != "") foreach (var duplicate in List<Student>(db, "Students", "cpf=$0", string.Concat(cpf.Where(char.IsDigit)))) if (s.Students.All(x => x.Id != duplicate.Id)) s.Students.Add(duplicate);
        }
        else
        {
            var normalized = SearchText(scope.Query); var digits = string.Concat(scope.Query.Where(char.IsDigit));
            var where = "($0='' OR unit_id=$0 OR EXISTS(SELECT 1 FROM json_each(json_extract(entities.json,'$.legacyUnitIds')) WHERE value=$0)) AND ($1='' OR name LIKE $2 ESCAPE '\\' OR ($3<>'' AND cpf LIKE $4))";
            var args = new object?[] { scope.UnitId, scope.Query.Trim(), "%" + normalized.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%", digits, "%" + digits + "%" };
            // Unicode folding is handled by an indexed search column installed after the projection.
            where = where.Replace("name LIKE", "json_extract(json,'$.searchName') LIKE");
            var total = Database.Count(db, "SELECT count(*) FROM entities WHERE kind='Students' AND " + where, args);
            s.Students = List<Student>(db, "Students", "id IN(SELECT id FROM entities WHERE kind='Students' AND " + where + " ORDER BY name LIMIT 50 OFFSET " + (Math.Max(1, scope.Page) - 1) * 50 + ")", args);
            s.Statistics["studentsPage"] = new { total, page = scope.Page, pageSize = 50, ids = s.Students.Select(x => x.Id).ToArray() };
            if (sid != "" && s.Students.All(x => x.Id != sid)) s.Students.AddRange(List<Student>(db, "Students", "id=$0", sid));
        }
        var ids = s.Students.Select(x => x.Id).ToHashSet();
        s.Lessons = List<Lesson>(db, "Lessons", "(date >= $0 AND date < $1 AND ($2='' OR unit_id=$2)) OR ($3<>'' AND student_id=$3)", scope.From, scope.To, scope.UnitId, sid);
        if (scope.View == "agenda" && s.Lessons.Count > 3000 && !scope.Mutation) { s.Statistics["calendarTruncated"] = true; s.Lessons = s.Lessons.OrderBy(x => x.Start).Take(3000).ToList(); }
        var missing=s.Lessons.Select(x=>x.StudentId).Where(x=>x!=""&&!ids.Contains(x)).Distinct().ToArray();
        if(missing.Length>0){var quoted=string.Join(',',missing.Select(x=>"'"+x.Replace("'","''")+"'"));s.Students.AddRange(List<Student>(db,"Students","id IN("+quoted+")"));ids.UnionWith(missing);}
        var idSql = string.Join(',', ids.Select(x => "'" + x.Replace("'", "''") + "'")); if (idSql == "") idSql = "''";
        s.Enrollments = List<Enrollment>(db, "Enrollments", "student_id IN(" + idSql + ")");
        var relatedWhere = scope.Mutation || sid != "" ? "student_id=$0" : "date >= $1 AND date < $2 AND ($3='' OR unit_id=$3)";
        s.Entries = List<Entry>(db, "Entries", relatedWhere, sid, scope.From, scope.To, scope.UnitId);
        s.Exams = List<Exam>(db, "Exams", scope.Mutation || sid != "" ? "student_id=$0" : "date >= $1 AND date < $2 AND ($3='' OR unit_id=$3)", sid, scope.From, scope.To, scope.UnitId);
        s.Installments = List<Installment>(db, "Installments", sid != "" ? "student_id=$0" : "id IN(SELECT id FROM entities WHERE kind='Installments' AND ($1='' OR unit_id=$1) AND json_extract(json,'$.status')='Aberta' ORDER BY date LIMIT 100)", sid, scope.UnitId);
        s.Documents = List<Document>(db, "Documents", sid != "" ? "student_id=$0" : "id IN(SELECT id FROM entities WHERE kind='Documents' ORDER BY date DESC LIMIT 50)", sid);
        if (scope.Action == "closecash") {s.Entries = List<Entry>(db, "Entries", "unit_id=$0 AND date >= $1 AND json_extract(json,'$.closed')=0 AND json_extract(json,'$.kind') IN('Recebimento','Receita','Despesa','Estorno')", scope.Body.GetProperty("unitId").GetString(), Operations.Today);s.Entries.AddRange(List<Entry>(db,"Entries","json_extract(json,'$.kind')='Crédito' AND id IN(SELECT json_extract(json,'$.reverses') FROM entities WHERE kind='Entries' AND unit_id=$0 AND date>=$1)",scope.Body.GetProperty("unitId").GetString(),Operations.Today));}
        if (!scope.Mutation) Statistics(db, s, scope);
        return s;
    }
    static void Statistics(SqliteConnection db, State s, StateScope scope)
    {
        s.Statistics["counts"] = Database.Rows(db, "SELECT kind,count(*) AS count FROM entities GROUP BY kind");
        s.Statistics["activeEnrollments"] = Database.Count(db, "SELECT count(*) FROM entities WHERE kind='Enrollments' AND json_extract(json,'$.status')='Ativa' AND ($0='' OR unit_id=$0)", scope.UnitId);
        s.Statistics["lessonsToday"] = Database.Count(db, "SELECT count(*) FROM entities WHERE kind='Lessons' AND COALESCE(json_extract(json,'$.excludedFromAgenda'),0)=0 AND date >= $0 AND date < $1 AND ($2='' OR unit_id=$2)", Operations.Today, Operations.Now.AddDays(1).ToString("yyyy-MM-dd"), scope.UnitId);
        var sum = "COALESCE(SUM(CAST(round(CAST(amount AS REAL)*100) AS INTEGER)),0)";
        s.Statistics["receivedTodayCents"] = Database.Count(db, "SELECT " + sum + " FROM entities WHERE kind='Entries' AND date=$0 AND json_extract(json,'$.kind')='Recebimento' AND ($1='' OR unit_id=$1)", Operations.Today, scope.UnitId);
        s.Statistics["overdueCount"] = Database.Count(db, "SELECT count(*) FROM entities WHERE kind='Installments' AND date<$0 AND json_extract(json,'$.status')='Aberta' AND CAST(json_extract(json,'$.paid') AS REAL)<CAST(amount AS REAL) AND ($1='' OR unit_id=$1)", Operations.Today, scope.UnitId);
        s.Statistics["overdueCents"]=Database.Count(db,"SELECT COALESCE(SUM(CAST(round((CAST(amount AS REAL)-CAST(json_extract(json,'$.paid') AS REAL))*100) AS INTEGER)),0) FROM entities WHERE kind='Installments' AND date<$0 AND json_extract(json,'$.status')='Aberta' AND CAST(json_extract(json,'$.paid') AS REAL)<CAST(amount AS REAL) AND ($1='' OR unit_id=$1)",Operations.Today,scope.UnitId);
        s.Statistics["period"] = new { from = scope.From, to = scope.To };
        s.Statistics["sourceTotal"] = Database.Scalar(db, "SELECT value FROM metadata WHERE key='sourceTotal'");
        s.Statistics["datasetId"] = Database.Scalar(db, "SELECT value FROM metadata WHERE key='importId'");
        var studentIds=string.Join(',',s.Students.Select(x=>"'"+x.Id.Replace("'","''")+"'"));
        if(studentIds!="") s.Statistics["studentBalances"] = Database.Rows(db,"SELECT student_id,COALESCE(SUM(CASE WHEN json_extract(json,'$.kind') IN('Débito','Estorno') THEN CAST(round(CAST(amount AS REAL)*100) AS INTEGER) WHEN json_extract(json,'$.kind') IN('Recebimento','Crédito') THEN -CAST(round(CAST(amount AS REAL)*100) AS INTEGER) ELSE 0 END),0) AS cents FROM entities WHERE kind='Entries' AND student_id IN("+studentIds+") GROUP BY student_id");
        s.Statistics["pendingReferences"] = Database.Count(db, "SELECT count(*) FROM import_issues");
    }
    public static string SearchText(string value) => string.Concat(value.Normalize(System.Text.NormalizationForm.FormD).Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
    public List<Student> Search(string q, string unit)
    {
        using var db = Database.Open(Path, true); var term = SearchText(q.Trim()); if (term.Length < 2) return [];
        var digits = string.Concat(q.Where(char.IsDigit));
        return List<Student>(db, "Students", "id IN(SELECT id FROM entities WHERE kind='Students' AND ($0='' OR unit_id=$0 OR EXISTS(SELECT 1 FROM json_each(json_extract(entities.json,'$.legacyUnitIds')) WHERE value=$0)) AND (json_extract(json,'$.searchName') LIKE $1 ESCAPE '\\' OR ($2<>'' AND cpf LIKE $3)) ORDER BY name LIMIT 20)", unit, "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%", digits, "%" + digits + "%");
    }
    public object Ledger(string unit,string from,string to,int page)
    {
        using var db=Database.Open(Path,true);
        var condition="kind='Entries' AND ($0='' OR unit_id=$0) AND date>=$1 AND date<$2";
        var total=Database.Count(db,"SELECT count(*) FROM entities WHERE "+condition,unit,from,to);
        var summary=Database.Rows(db,"SELECT json_extract(json,'$.kind') AS kind,COALESCE(SUM(CAST(round(CAST(amount AS REAL)*100) AS INTEGER)),0) AS cents,count(*) AS count FROM entities WHERE "+condition+" GROUP BY json_extract(json,'$.kind')",unit,from,to);
        var rows=Database.Rows(db,"SELECT e.json,COALESCE(json_extract(s.json,'$.name'),'Sem vínculo no pacote') AS student_name,COALESCE(json_extract(u.json,'$.name'),'') AS user_name FROM entities e LEFT JOIN entities s ON s.kind='Students' AND s.id=e.student_id LEFT JOIN entities u ON u.kind='Users' AND u.id=json_extract(e.json,'$.userId') WHERE e."+condition.Replace("unit_id","e.unit_id").Replace("date","e.date")+" ORDER BY e.date DESC,e.id DESC LIMIT 100 OFFSET "+(Math.Max(1,page)-1)*100,unit,from,to);
        return new {total,page,pageSize=100,summary,from,to,rows=rows.Select(r=>new{entry=JsonSerializer.Deserialize<Entry>(r["json"]!,Database.Compact),studentName=r["student_name"],userName=r["user_name"]})};
    }
    public string Mutate(string user, string operation, string fingerprint, Func<State, object> action, StateScope? scope)
    {
        using var db = Database.Open(Path); using var tx = db.BeginTransaction(); var key = user + ":" + operation;
        var prior = Database.Rows(db, "SELECT fingerprint,result FROM processed WHERE key=$0", key).FirstOrDefault();
        if (prior != null) { if (prior["fingerprint"] != fingerprint) throw new RuleException("Identificador reutilizado para outra operação.", 409); return prior["result"]!; }
        var state = Read(db, scope ?? new());
        var before = Capture(state); var result = JsonSerializer.Serialize(action(state), Database.Compact);
        state.Revision++; var after = Capture(state);
        foreach (var (entityKey, serialized) in after)
        {
            if (before.TryGetValue(entityKey, out var existing) && existing == serialized) continue;
            var split = entityKey.IndexOf(':'); var kind = entityKey[..split]; var json = JsonSerializer.Deserialize<JsonElement>(serialized);
            Database.SaveEntity(db, kind, json);
        }
        foreach (var entityKey in before.Keys.Where(x => !after.ContainsKey(x))) { var split = entityKey.IndexOf(':'); Database.Execute(db, "DELETE FROM entities WHERE kind=$0 AND id=$1", entityKey[..split], entityKey[(split + 1)..]); }
        Database.Execute(db, "UPDATE metadata SET value=$0 WHERE key='revision'", state.Revision.ToString(CultureInfo.InvariantCulture));
        Database.Execute(db, "UPDATE metadata SET value=$0 WHERE key='policy'", JsonSerializer.Serialize(state.Policy, Database.Compact));
        Database.Execute(db,"INSERT INTO metadata(key,value) VALUES('receiptCounters',$0) ON CONFLICT(key) DO UPDATE SET value=excluded.value",JsonSerializer.Serialize(state.ReceiptCounters,Database.Compact));
        Database.Execute(db, "INSERT INTO processed VALUES($0,$1,$2)", key, fingerprint, result); tx.Commit(); return result;
    }
    static Dictionary<string, string> Capture(State state)
    {
        var values = new Dictionary<string, string>();
        foreach (var property in typeof(State).GetProperties().Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(List<>)))
        foreach (var value in (IEnumerable)property.GetValue(state)!) { var json = JsonSerializer.SerializeToElement(value, Database.Compact); values[property.Name + ":" + json.GetProperty("id").GetString()] = json.GetRawText(); }
        return values;
    }
}

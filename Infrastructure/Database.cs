using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace CfcPilot;

public static class Database
{
    public static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web);
    public static SqliteConnection Open(string path, bool readOnly = false)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = true }.ToString());
        db.Open(); Execute(db, "PRAGMA busy_timeout=15000; PRAGMA foreign_keys=ON;"); return db;
    }
    public static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    public static SqliteCommand Command(SqliteConnection db, string sql, params object?[] args)
    {
        var command = db.CreateCommand(); command.CommandText = sql;
        for (var i = 0; i < args.Length; i++) command.Parameters.AddWithValue("$" + i, args[i] ?? DBNull.Value);
        return command;
    }
    public static void Execute(SqliteConnection db, string sql, params object?[] args) { using var c = Command(db, sql, args); c.ExecuteNonQuery(); }
    public static string Scalar(SqliteConnection db, string sql, params object?[] args) { using var c = Command(db, sql, args); return Convert.ToString(c.ExecuteScalar(), CultureInfo.InvariantCulture) ?? ""; }
    public static long Count(SqliteConnection db, string sql, params object?[] args) => long.Parse(Scalar(db, sql, args), CultureInfo.InvariantCulture);
    public static List<Dictionary<string, string?>> Rows(SqliteConnection db, string sql, params object?[] args)
    {
        using var c = Command(db, sql, args); using var r = c.ExecuteReader(); var rows = new List<Dictionary<string, string?>>();
        while (r.Read()) { var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase); for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i).ToString(); rows.Add(row); }
        return rows;
    }
    public static void Initialize(SqliteConnection db)
    {
        Execute(db, """
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS entities (kind TEXT NOT NULL,id TEXT NOT NULL,student_id TEXT NOT NULL DEFAULT '',enrollment_id TEXT NOT NULL DEFAULT '',unit_id TEXT NOT NULL DEFAULT '',date TEXT NOT NULL DEFAULT '',name TEXT NOT NULL DEFAULT '',cpf TEXT NOT NULL DEFAULT '',amount TEXT NOT NULL DEFAULT '0',json TEXT NOT NULL,PRIMARY KEY(kind,id));
        CREATE INDEX IF NOT EXISTS ix_entity_student ON entities(kind,student_id,date);
        CREATE INDEX IF NOT EXISTS ix_entity_enrollment ON entities(kind,enrollment_id);
        CREATE INDEX IF NOT EXISTS ix_entity_unit ON entities(kind,unit_id,date);
        CREATE INDEX IF NOT EXISTS ix_entity_date ON entities(kind,date);
        CREATE INDEX IF NOT EXISTS ix_entity_instructor ON entities(kind,json_extract(json,'$.instructorId'),date);
        CREATE INDEX IF NOT EXISTS ix_entity_cpf ON entities(kind,cpf);
        CREATE TABLE IF NOT EXISTS processed (key TEXT PRIMARY KEY,fingerprint TEXT NOT NULL,result TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS import_tables (name TEXT PRIMARY KEY,module TEXT NOT NULL,columns_json TEXT NOT NULL,expected INTEGER NOT NULL,imported INTEGER NOT NULL,csv_count INTEGER NOT NULL,source_hash TEXT NOT NULL,raw_hash TEXT NOT NULL,sealed_cells INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS import_issues (id INTEGER PRIMARY KEY,table_name TEXT NOT NULL,source_id TEXT NOT NULL,relation TEXT NOT NULL,target_id TEXT NOT NULL,detail TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS automation_jobs (id TEXT PRIMARY KEY,operation TEXT NOT NULL,student_id TEXT NOT NULL,profile_id TEXT NOT NULL,status TEXT NOT NULL,created TEXT NOT NULL,updated TEXT NOT NULL,request_json TEXT NOT NULL,result_json TEXT NOT NULL DEFAULT '{}',error TEXT NOT NULL DEFAULT '',user_id TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_jobs_status ON automation_jobs(status,created);
        """);
    }
    public static void SaveEntity(SqliteConnection db, string kind, object entity)
    {
        var json = JsonSerializer.SerializeToElement(entity, Compact);
        string Get(string name) => json.TryGetProperty(name, out var p) ? p.ToString() : "";
        var sid = kind == "Students" ? Get("id") : Get("studentId"); var date = Get("start"); if (date == "") date = Get("date"); if (date == "") date = Get("due"); if (date == "") date = Get("created");
        Execute(db, "INSERT INTO entities(kind,id,student_id,enrollment_id,unit_id,date,name,cpf,amount,json) VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8,$9) ON CONFLICT(kind,id) DO UPDATE SET student_id=excluded.student_id,enrollment_id=excluded.enrollment_id,unit_id=excluded.unit_id,date=excluded.date,name=excluded.name,cpf=excluded.cpf,amount=excluded.amount,json=excluded.json", kind, Get("id"), sid, Get("enrollmentId"), Get("unitId"), date, Get("name"), Get("cpf"), Get("amount") == "" ? "0" : Get("amount"), json.GetRawText());
    }
}

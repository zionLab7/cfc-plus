using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CfcPilot;

public static partial class PackageImporter
{
    public static bool Sensitive(string column) => SecretColumn().IsMatch(column);
    [GeneratedRegex(@"senha|password|secret|token|cookie|puk|pin|digital|chave|seguranca|hashcobranca", RegexOptions.IgnoreCase)]
    private static partial Regex SecretColumn();
    [GeneratedRegex(@"\| \*\*(\w+)\*\* \| ([\d.]+) \|")]
    private static partial Regex ExpectedRows();
    public static async Task<Dictionary<string,long>> Inventory(string source)
    {
        var manifest=Path.Combine(source,"import-manifest.json");Dictionary<string,long> expected;
        if(File.Exists(manifest)){using var j=JsonDocument.Parse(await File.ReadAllTextAsync(manifest));if(j.RootElement.GetProperty("version").GetInt32()!=1)throw new InvalidDataException("Versão do inventário não suportada.");expected=j.RootElement.GetProperty("tables").EnumerateArray().ToDictionary(x=>x.GetProperty("name").GetString()!,x=>x.GetProperty("rows").GetInt64(),StringComparer.OrdinalIgnoreCase);}
        else{var summary=Directory.GetFiles(source,"RESUMO_IMPORTACAO*.md").SingleOrDefault()??throw new InvalidDataException("Inclua import-manifest.json com a contagem de cada tabela exportada.");expected=ExpectedRows().Matches(await File.ReadAllTextAsync(summary)).ToDictionary(m=>m.Groups[1].Value,m=>long.Parse(m.Groups[2].Value.Replace(".","")),StringComparer.OrdinalIgnoreCase);}
        if(expected.Count is <1 or >256||expected.Any(x=>!Regex.IsMatch(x.Key,"^[A-Za-z_][A-Za-z0-9_]{0,127}$")||x.Value<0))throw new InvalidDataException("Inventário inválido.");return expected;
    }
    public static async Task Run(string package, string directory, bool activate)
    {
        package = Path.GetFullPath(package); directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var source = Path.Combine(package, "01_BANCO_DE_DADOS_COMPLETO");
        var expected=await Inventory(source);
        var files = Directory.GetFiles(source, "*.sql", SearchOption.AllDirectories).Where(f => !Path.GetFileName(f).StartsWith("00_")).Order().ToArray();
        if (files.Length != expected.Count||files.Any(f=>new FileInfo(f).Length>ImportQueue.MaxFile)) throw new InvalidDataException("O inventário e os arquivos SQL divergem. Importação não ativada.");
        var importId = DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var staging = Path.Combine(directory, "imports", importId); Directory.CreateDirectory(staging);
        var path = Path.Combine(staging, "cfc.sqlite");
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys")), p => { p.SetApplicationName("CfcPilot"); if (OperatingSystem.IsWindows()) p.ProtectKeysWithDpapi(); }).CreateProtector("legacy-fields-v1");
        var report = new List<object>(); long total = 0;
        using (var db = Database.Open(path))
        {
            Database.Initialize(db);
            foreach (var file in files)
            {
                var text = await File.ReadAllTextAsync(file); var (table, columns) = SqlExportReader.Header(text);
                if (!expected.TryGetValue(table, out var wanted)) throw new InvalidDataException("Tabela sem contagem de referência: " + table);
                using var csv = File.OpenText(Path.ChangeExtension(file, ".csv")); var csvRows = SqlExportReader.Csv(csv).GetEnumerator();
                if (!csvRows.MoveNext() || !csvRows.Current.SequenceEqual(columns)) throw new InvalidDataException("Colunas CSV/SQL divergentes em " + table);
                Database.Execute(db, "CREATE TABLE " + Database.Quote("raw_" + table) + " (__row INTEGER PRIMARY KEY," + string.Join(',', columns.Select(c => Database.Quote(c) + " TEXT")) + ")");
                using var transaction = db.BeginTransaction();
                using var insert = db.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO " + Database.Quote("raw_" + table) + " VALUES(" + string.Join(',', Enumerable.Range(0, columns.Length + 1).Select(i => "$" + i)) + ")";
                for (var i = 0; i <= columns.Length; i++) insert.Parameters.AddWithValue("$" + i, DBNull.Value); insert.Prepare();
                using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long count = 0, sealedCells = 0, csvDifferences = 0;
                foreach (var row in SqlExportReader.Read(text, table, columns))
                {
                    if (!csvRows.MoveNext() || csvRows.Current.Length != columns.Length) throw new InvalidDataException("Contagem/colunas CSV divergentes em " + table);
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row, Database.Compact) + "\n"); checksum.AppendData(bytes);
                    for (var i = 0; i < columns.Length; i++)
                    {
                        var value = row[i];
                        // SQL preserves NULL versus an empty string, binary values and exact numeric literals.
                        if ((value ?? "") != csvRows.Current[i]) csvDifferences++;
                        if (value is { Length: > 0 } && Sensitive(columns[i])) { value = "sealed:" + protection.Protect(value); sealedCells++; }
                        insert.Parameters[i + 1].Value = (object?)value ?? DBNull.Value;
                    }
                    insert.Parameters[0].Value = ++count; insert.ExecuteNonQuery();
                }
                if (csvRows.MoveNext() || count != wanted) throw new InvalidDataException("Contagem diferente da exportação em " + table + ". Importação não ativada.");
                var hash = Convert.ToHexString(checksum.GetHashAndReset());
                using (var stored = Database.Command(db, "SELECT " + string.Join(',', columns.Select(Database.Quote)) + " FROM " + Database.Quote("raw_" + table) + " ORDER BY __row"))
                using (var reader = stored.ExecuteReader())
                using (var verifiedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    while (reader.Read())
                    {
                        var original = new string?[columns.Length];
                        for (var i = 0; i < original.Length; i++) { var v = reader.IsDBNull(i) ? null : reader.GetString(i); original[i] = v != null && Sensitive(columns[i]) && v.StartsWith("sealed:") ? protection.Unprotect(v[7..]) : v; }
                        verifiedHash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(original, Database.Compact) + "\n"));
                    }
                    if (Convert.ToHexString(verifiedHash.GetHashAndReset()) != hash) throw new InvalidDataException("Conferência de todos os campos falhou em " + table);
                }
                var sourceHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file)));
                var module = Path.GetFileName(Path.GetDirectoryName(file)!);
                Database.Execute(db, "INSERT INTO import_tables VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8)", table, module, JsonSerializer.Serialize(columns), wanted, count, count, sourceHash, hash, sealedCells);
                transaction.Commit();
                foreach (var column in columns.Where(c => !Sensitive(c) && (c.Equals("id", StringComparison.OrdinalIgnoreCase) || c.EndsWith("id", StringComparison.OrdinalIgnoreCase) || new[] { "Aluno", "Matricula", "Unidade", "Instrutor", "Data", "Cpf", "Mov_id", "Pedido_id", "AgendaAula_id", "Lanc", "Lanc_id", "Cod", "Carro" }.Contains(c, StringComparer.OrdinalIgnoreCase))))
                    Database.Execute(db, "CREATE INDEX " + Database.Quote("rawidx_" + table + "_" + column) + " ON " + Database.Quote("raw_" + table) + "(" + Database.Quote(column) + ")");
                total += count; report.Add(new { table, expected = wanted, imported = count, csvCount = count, sourceHash, rawHash = hash, sealedCells, csvDifferentCells = csvDifferences, policy = "SQL autoritativo; CSV conferido por colunas e registros" });
                Console.WriteLine(table + ": " + count + " registros conferidos.");
            }
            Database.Execute(db, "INSERT INTO metadata VALUES('importId',$0),('sourceTotal',$1),('revision','1'),('mode','real')", importId, total.ToString());
            await Projector.Run(db, directory, protection);
            LegacyIntegrity.Check(db);
            Database.Execute(db, "PRAGMA optimize; PRAGMA wal_checkpoint(TRUNCATE);");
            if (Database.Scalar(db, "PRAGMA integrity_check") != "ok") throw new InvalidDataException("Falha na integridade física da base.");
            var issues = Database.Count(db, "SELECT count(*) FROM import_issues");
            var entities = Database.Rows(db, "SELECT kind,count(*) AS count FROM entities GROUP BY kind");
            var result = new { importId, created = Operations.Now.ToString("s"), tables = report, total, sourceTables = files.Length, issues, entities, source = package, storage = "SQLite", sourceSqlRetained = true, verified = true };
            await File.WriteAllTextAsync(Path.Combine(staging, "report.json"), JsonSerializer.Serialize(result, Store.Json));
            await File.WriteAllTextAsync(Path.Combine(directory, "import-report.json"), JsonSerializer.Serialize(result, Store.Json));
            if (activate)
            {
                var backup = Path.Combine(directory, "backups", importId); Directory.CreateDirectory(backup);
                foreach (var name in new[] { "pilot.json", "active-database.json" }) { var existing = Path.Combine(directory, name); if (File.Exists(existing)) File.Copy(existing, Path.Combine(backup, name)); }
                var active = Path.Combine(directory, "active-database.json");
                await File.WriteAllTextAsync(active + ".tmp", JsonSerializer.Serialize(new { path = Path.GetRelativePath(directory, path), importId })); File.Move(active + ".tmp", active, true);
            }
            Console.WriteLine("Importação conferida: " + total + " registros em " + files.Length + " tabelas. Referências pendentes preservadas: " + issues + ".");
        }
    }
}

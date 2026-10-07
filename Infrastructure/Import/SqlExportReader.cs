using System.Text;
using System.Text.RegularExpressions;

namespace CfcPilot;

// Interpret a data-only export. Never execute vendor SQL or its commands.
public static partial class SqlExportReader
{
    [GeneratedRegex(@"INSERT\s+INTO\s+(?:\[dbo\]\.)?\[([^\]]+)\]\s*\(([^)]+)\)\s*VALUES", RegexOptions.IgnoreCase)]
    private static partial Regex InsertHeader();
    [GeneratedRegex(@"\[([^\]]+)\]")]
    private static partial Regex ColumnName();
    public static (string Table, string[] Columns) Header(string text)
    {
        var m = InsertHeader().Match(text); if (!m.Success) throw new InvalidDataException("Exportação sem cabeçalho INSERT.");
        return (m.Groups[1].Value, ColumnName().Matches(m.Groups[2].Value).Select(x => x.Groups[1].Value).ToArray());
    }
    public static IEnumerable<string?[]> Read(string text, string table, string[] columns)
    {
        var at = 0; var batches = 0;
        while (true)
        {
            var header = InsertHeader().Match(text, at); if (!header.Success) break;
            var actual = Header(header.Value); if (actual.Table != table || !actual.Columns.SequenceEqual(columns)) throw new InvalidDataException("Cabeçalhos divergentes em " + table);
            at = header.Index + header.Length; batches++;
            while (true)
            {
                while (at < text.Length && (char.IsWhiteSpace(text[at]) || text[at] == ',')) at++;
                if (at == text.Length || text[at] != '(') break;
                at++; var row = new List<string?>();
                while (true)
                {
                    while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
                    if (at < text.Length - 1 && text[at] is 'N' or 'n' && text[at + 1] == '\'') at++;
                    if (at >= text.Length) throw new InvalidDataException("Registro incompleto em " + table);
                    if (text[at] == '\'')
                    {
                        at++; var value = new StringBuilder(); var closed = false;
                        while (at < text.Length)
                        {
                            var ch = text[at++]; if (ch != '\'') { value.Append(ch); continue; }
                            if (at < text.Length && text[at] == '\'') { value.Append('\''); at++; continue; }
                            closed = true; break;
                        }
                        if (!closed) throw new InvalidDataException("Texto incompleto em " + table); row.Add(value.ToString());
                    }
                    else
                    {
                        var start = at; var depth = 0;
                        while (at < text.Length) { var ch = text[at]; if (depth == 0 && ch is ',' or ')') break; if (ch == '(') depth++; if (ch == ')') depth--; at++; }
                        var token = text[start..at].Trim();
                        if (!token.Equals("NULL", StringComparison.OrdinalIgnoreCase) && !NumericOrBinary().IsMatch(token)) throw new InvalidDataException("Literal SQL não suportado em " + table + ": tipo não reconhecido (valor omitido).");
                        row.Add(token.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : token);
                    }
                    while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
                    if (at >= text.Length) throw new InvalidDataException("Registro incompleto em " + table);
                    if (text[at] == ')') { at++; break; }
                    if (text[at++] != ',') throw new InvalidDataException("Separador SQL inválido em " + table);
                }
                if (row.Count != columns.Length) throw new InvalidDataException("Número de colunas divergente em " + table); yield return row.ToArray();
            }
        }
        if (batches == 0) throw new InvalidDataException("Nenhum lote em " + table);
    }
    [GeneratedRegex(@"^(?:[+-]?\d+(?:\.\d*)?(?:[eE][+-]?\d+)?|0x[0-9a-fA-F]*)$")]
    private static partial Regex NumericOrBinary();
    public static IEnumerable<string[]> Csv(TextReader reader)
    {
        var cells = new List<string>(); var value = new StringBuilder(); var quoted = false; var first = true;
        while (reader.Read() is var code && code >= 0)
        {
            var ch = (char)code; if (first) { first = false; if (ch == '\uFEFF') continue; }
            if (ch == '"') { if (quoted && reader.Peek() == '"') { reader.Read(); value.Append('"'); } else quoted = !quoted; }
            else if (ch == ';' && !quoted) { cells.Add(value.ToString()); value.Clear(); }
            else if (ch is '\r' or '\n' && !quoted) { if (ch == '\r' && reader.Peek() == '\n') reader.Read(); cells.Add(value.ToString()); value.Clear(); yield return cells.ToArray(); cells.Clear(); }
            else value.Append(ch);
        }
        if (quoted) throw new InvalidDataException("CSV com texto incompleto.");
        if (value.Length > 0 || cells.Count > 0) { cells.Add(value.ToString()); yield return cells.ToArray(); }
    }
}

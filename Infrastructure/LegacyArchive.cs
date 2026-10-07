using System.Text.Json;

namespace CfcPilot;

public sealed class LegacyArchive(Store store)
{
    string Path => store.DatabasePath ?? throw new RuleException("Nenhuma base real importada.", 404);
    public object Catalog()
    {
        using var db=Database.Open(Path,true);
        return new { tables=Database.Rows(db,"SELECT name,module,expected,imported,csv_count,sealed_cells FROM import_tables ORDER BY module,name"),issues=Database.Rows(db,"SELECT table_name,relation,count(*) AS count FROM import_issues GROUP BY table_name,relation ORDER BY count DESC"),counts=Database.Rows(db,"SELECT kind,count(*) AS count FROM entities GROUP BY kind"),total=Database.Scalar(db,"SELECT value FROM metadata WHERE key='sourceTotal'"),datasetId=Database.Scalar(db,"SELECT value FROM metadata WHERE key='importId'") };
    }
    public object Table(string table,int page,string q,string sid,string unit)
    {
        using var db=Database.Open(Path,true);
        var meta=Database.Rows(db,"SELECT * FROM import_tables WHERE name=$0 COLLATE NOCASE",table).FirstOrDefault()??throw new RuleException("Tabela inexistente.",404);
        table=meta["name"]!;var columns=JsonSerializer.Deserialize<string[]>(meta["columns_json"]!)!;
        var visible=columns.Where(c=>!PackageImporter.Sensitive(c)).ToArray();
        var filters=new List<string>();var values=new List<object?>();
        string Parameter(object? value){values.Add(value);return "$"+(values.Count-1);}
        if(sid!="")
        {
            var source=Database.Scalar(db,"SELECT json_extract(json,'$.sourceId') FROM entities WHERE kind='Students' AND id=$0",sid);
            if(source=="")throw new RuleException("Aluno do legado não encontrado.",404);
            var column=columns.FirstOrDefault(c=>new[]{"Aluno","AlunoId","PacoteLog_aluno"}.Contains(c,StringComparer.OrdinalIgnoreCase));
            if(column!=null)filters.Add(Database.Quote(column)+"="+Parameter(source));
            else if(table.Equals("Agendaaula",StringComparison.OrdinalIgnoreCase))filters.Add("AgendaAula_id IN(SELECT AgendaAula_id FROM raw_Agendaitens WHERE Aluno="+Parameter(source)+")");
            else if(table.Equals("Historico_pedido",StringComparison.OrdinalIgnoreCase))filters.Add("EXISTS(SELECT 1 FROM raw_Pedido p WHERE p.Pedido_id=raw_Historico_pedido.PEDIDO_ID AND p.Unidade=raw_Historico_pedido.UNIDADE AND p.Aluno="+Parameter(source)+") AND (SELECT count(distinct p.Aluno) FROM raw_Pedido p WHERE p.Pedido_id=raw_Historico_pedido.PEDIDO_ID AND p.Unidade=raw_Historico_pedido.UNIDADE)=1");
            else if(table.Equals("CarnePagamento",StringComparison.OrdinalIgnoreCase))filters.Add("CarneID IN(SELECT ID FROM raw_Carne WHERE Aluno="+Parameter(source)+")");
            else filters.Add("0=1");
        }
        if(unit!=""){var col=columns.FirstOrDefault(c=>new[]{"Unidade","UnidadeId"}.Contains(c,StringComparer.OrdinalIgnoreCase));if(col!=null)filters.Add(Database.Quote(col)+"="+Parameter(unit));}
        if(q.Trim()!="")
        {
            var parameter=Parameter("%"+q.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%");
            filters.Add("("+string.Join(" OR ",visible.Select(c=>Database.Quote(c)+" LIKE "+parameter+" ESCAPE '\\'"))+")");
        }
        var where=filters.Count>0?string.Join(" AND ",filters):"1=1";
        var count=Database.Count(db,"SELECT count(*) FROM "+Database.Quote("raw_"+table)+" WHERE "+where,values.ToArray());
        var selected=columns.Select(c=>PackageImporter.Sensitive(c)?"CASE WHEN "+Database.Quote(c)+" IS NULL OR "+Database.Quote(c)+"='' THEN NULL ELSE '[Protegido]' END AS "+Database.Quote(c):Database.Quote(c));
        var rows=Database.Rows(db,"SELECT __row,"+string.Join(',',selected)+" FROM "+Database.Quote("raw_"+table)+" WHERE "+where+" ORDER BY __row LIMIT 50 OFFSET "+(Math.Max(1,page)-1)*50,values.ToArray());
        return new{table,module=meta["module"],columns,rows,total=count,page,pageSize=50,immutable=true};
    }
    public object Issues(int page)
    {
        using var db=Database.Open(Path,true);return new{total=Database.Count(db,"SELECT count(*) FROM import_issues"),page,rows=Database.Rows(db,"SELECT id,table_name,source_id,relation,target_id,detail FROM import_issues ORDER BY id LIMIT 50 OFFSET "+(Math.Max(1,page)-1)*50)};
    }
}

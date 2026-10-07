using System.Text.Json;
namespace CfcPilot;
public static class ImportChecks
{
    public static void Run(string root)
    {
        var manifest=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root,"active-database.json")));
        var path=Path.Combine(root,manifest.GetProperty("path").GetString()!);using var db=Database.Open(path,true);
        var queries=new Dictionary<string,string>{
            ["pedidoIdentity"]="SELECT count(*) AS total,count(distinct Pedido_id) AS ids,SUM(CASE WHEN Pedido_id IS NULL OR Pedido_id='0' OR Pedido_id='' THEN 1 ELSE 0 END) AS missing,count(distinct Pedido_num) AS numbers FROM raw_Pedido",
            ["pedidoDuplicates"]="SELECT count(*) AS repeated,SUM(n) AS rows FROM (SELECT Pedido_id,count(*) AS n FROM raw_Pedido GROUP BY Pedido_id HAVING count(*)>1)",
            ["pedidoLinks"]="SELECT 'Movimento' AS relation, SUM(CASE WHEN EXISTS(SELECT 1 FROM raw_Pedido p WHERE p.Pedido_id=m.Matricula AND p.Aluno=m.Aluno) THEN 1 ELSE 0 END) AS id_match,SUM(CASE WHEN EXISTS(SELECT 1 FROM raw_Pedido p WHERE p.Pedido_num=m.Matricula AND p.Aluno=m.Aluno) THEN 1 ELSE 0 END) AS num_match FROM raw_Movimento m WHERE m.Matricula IS NOT NULL AND m.Matricula<>'0' UNION ALL SELECT 'Aulascontrole',SUM(CASE WHEN EXISTS(SELECT 1 FROM raw_Pedido p WHERE p.Pedido_id=m.Matricula AND p.Aluno=m.Aluno) THEN 1 ELSE 0 END),SUM(CASE WHEN EXISTS(SELECT 1 FROM raw_Pedido p WHERE p.Pedido_num=m.Matricula AND p.Aluno=m.Aluno) THEN 1 ELSE 0 END) FROM raw_Aulascontrole m",
            ["pedidoAmbiguity"]="SELECT count(*) AS repeated,SUM(n) AS rows FROM (SELECT Pedido_id,Aluno,count(*) AS n FROM raw_Pedido GROUP BY Pedido_id,Aluno HAVING count(*)>1)",
            ["controlTypes"]="SELECT Tipo_id,count(*) AS n,min(AulasControle_TotalBrinde) AS min,max(AulasControle_TotalBrinde) AS max FROM raw_Aulascontrole GROUP BY Tipo_id",
            ["lessonCategories"]="SELECT Categoria,count(*) AS n FROM raw_Agendaaula GROUP BY Categoria",
            ["biometricStatuses"]="SELECT DIGSIT,count(*) AS n FROM raw_Agendaaula GROUP BY DIGSIT",
            ["creditTypes"]="SELECT Modo,TipoCredito,count(*) AS n,SUM(CAST(Valorp AS REAL)) AS amount FROM raw_Movimento GROUP BY Modo,TipoCredito",
            ["methodDiscounts"]="SELECT ID,DESCRICAO FROM raw_FormaPagamento WHERE lower(DESCRICAO) LIKE '%desconto%' OR lower(DESCRICAO) LIKE '%crédito%'",
            ["scheduleSizes"]="SELECT kind,count(*) AS n,MAX(length(json)) AS largest FROM entities GROUP BY kind",
            ["issues"]="SELECT table_name,relation,count(*) AS n FROM import_issues GROUP BY table_name,relation",
            ["loginsDuplicated"]="SELECT count(*) AS n FROM (SELECT json_extract(json,'$.login') FROM entities WHERE kind='Users' GROUP BY lower(json_extract(json,'$.login')) HAVING count(*)>1)",
            ["financialKinds"]="SELECT json_extract(json,'$.kind') AS kind,count(*) AS n,sum(CAST(round(CAST(amount AS REAL)*100) AS INTEGER)) AS cents FROM entities WHERE kind='Entries' GROUP BY json_extract(json,'$.kind')",
            ["controlVehicleMatch"]="SELECT c.Tipo_id,v.categoria,count(*) AS n FROM raw_Aulascontrole c JOIN raw_Agendaitens i ON i.Matricula=c.Matricula JOIN raw_Agendaaula a ON a.AgendaAula_id=i.AgendaAula_id JOIN raw_Carro v ON v.carro=a.VEICULO GROUP BY c.Tipo_id,v.categoria"
        };
        foreach(var (name,sql) in queries)Console.WriteLine(name+": "+JsonSerializer.Serialize(Database.Rows(db,sql),Database.Compact));
    }
}

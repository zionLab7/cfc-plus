using Microsoft.Data.Sqlite;

namespace CfcPilot;

public static class LegacyIntegrity
{
    public static void Check(SqliteConnection db)
    {
        var relations = new[] {
            ("Pedido", "Pedido_id", "Aluno", "Aluno", "Aluno"),
            ("Agendaitens", "AgendaItens_id", "Aluno", "Aluno", "Aluno"),
            ("Agendaitens", "AgendaItens_id", "Matricula", "Pedido", "Pedido_id"),
            ("Agendaitens", "AgendaItens_id", "AgendaAula_id", "Agendaaula", "AgendaAula_id"),
            ("Movimento", "Mov_id", "Aluno", "Aluno", "Aluno"),
            ("Movimento", "Mov_id", "Matricula", "Pedido", "Pedido_id"),
            ("Carne", "ID", "Aluno", "Aluno", "Aluno"),
            ("Carne", "ID", "Matricula", "Pedido", "Pedido_id"),
            ("agendamento", "ID", "ALUNO", "Aluno", "Aluno"),
            ("agendamento", "ID", "MATRICULA", "Pedido", "Pedido_id"),
            ("AlunoTelefone", "Id", "AlunoId", "Aluno", "Aluno"),
            ("Ladv", "Id", "AlunoId", "Aluno", "Aluno"),
            ("AlunoProgresso", "ProgressoID", "AlunoID", "Aluno", "Aluno"),
            ("TurmaAluno", "ID", "Aluno", "Aluno", "Aluno"),
            ("TurmaAluno", "ID", "TurmaDetalhe", "TurmaDetalhe", "ID"),
            ("TurmaDetalhe", "ID", "Turma", "Turma", "ID")
        };
        using var tx = db.BeginTransaction();
        foreach (var (table, id, field, target, targetId) in relations)
        {
            var sourceColumn = Database.Quote(field); var targetColumn = Database.Quote(targetId);
            Database.Execute(db, "INSERT INTO import_issues(table_name,source_id,relation,target_id,detail) SELECT $0,s." + Database.Quote(id) + ",$1,s." + sourceColumn + ",'Referência ausente no pacote; registro preservado' FROM " + Database.Quote("raw_" + table) + " s WHERE s." + sourceColumn + " IS NOT NULL AND trim(s." + sourceColumn + ") NOT IN ('','0','-1') AND NOT EXISTS(SELECT 1 FROM " + Database.Quote("raw_" + target) + " t WHERE t." + targetColumn + "=s." + sourceColumn + ")", table, field + " → " + target + "." + targetId);
        }
        tx.Commit();
    }
}

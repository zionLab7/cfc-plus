namespace CfcPilot;
public sealed class ImportedModules(Store store)
{
    public object Theory(string student,string unit,int page)
    {
        using var db=Database.Open(store.DatabasePath??throw new RuleException("Base real não importada."),true);
        var source=student==""?"":Database.Scalar(db,"SELECT json_extract(json,'$.sourceId') FROM entities WHERE kind='Students' AND id=$0",student);
        if(student!=""&&source=="")throw new RuleException("Aluno não encontrado.",404);
        const string join="FROM raw_TurmaAluno a LEFT JOIN raw_TurmaDetalhe d ON d.ID=a.TurmaDetalhe LEFT JOIN raw_Turma t ON t.ID=d.Turma LEFT JOIN raw_Aluno s ON s.Aluno=a.Aluno LEFT JOIN raw_Instrutor i ON i.instrutor=d.Instrutor WHERE ($0='' OR a.Aluno=$0) AND ($1='' OR t.Unidade=$1)";
        var total=Database.Count(db,"SELECT count(*) "+join,source,unit);
        var rows=Database.Rows(db,"SELECT a.ID AS Registro,s.Nome AS Aluno,t.Nome AS Turma,d.Data,d.Disciplina,d.Quantidade AS Aulas,i.nome AS Instrutor,a.Status,a.Ecnh,a.Matricula,a.Provedora,a.AgendaProvId,t.Unidade "+join+" ORDER BY d.Data DESC,a.ID DESC LIMIT 50 OFFSET "+(Math.Max(1,page)-1)*50,source,unit);
        return new{total,page,pageSize=50,rows};
    }
    public object Journey(string student)
    {
        using var db=Database.Open(store.DatabasePath??throw new RuleException("Base real não importada."),true);
        var source=Database.Scalar(db,"SELECT json_extract(json,'$.sourceId') FROM entities WHERE kind='Students' AND id=$0",student);if(source=="")throw new RuleException("Aluno não encontrado.",404);
        return Database.Rows(db,"SELECT p.ProgressoID,p.MatriculaID,p.CategoriaCNH,p.DataInicio,p.DataConclusao,p.Ativo,f.NomeFase AS Fase FROM raw_AlunoProgresso p LEFT JOIN raw_AlunoFases f ON f.FaseID=p.FaseID WHERE p.AlunoID=$0 ORDER BY p.DataInicio DESC",source);
    }
}

namespace CfcPilot;
public static class LegacyPermissions
{
    // NivelFuncaoBuscar returns blocked methods, not granted methods.
    static readonly Dictionary<string,string[]> Methods=new()
    {
        ["student"]=["GravarAluno","AlunoSalvar"], ["matriculate"]=["GravarAluno","AlunoSalvar","NovaMatricula"], ["enroll"]=["GravarAluno","NovaMatricula"],
        ["lesson"]=["MarcarAulas","GravarAulas","AgendarAulas","AgendaaulaMarcar2021"], ["lesson-status"]=["ManutencaoAulas"],
        ["receive"]=["CriarRecibo","Credito","MovimentoCreditoInserir"], ["entry"]=["InserirDebito","InserirCredito","InserirDebitoSimples"], ["void"]=["ExcluirMovimento","DELMOVIMENTO_MATRICULA"],
        ["exam"]=["AgendarExames","AgendamentoInserir"], ["exam-result"]=["AlterarResultadoExame"], ["closecash"]=["FechamentoCaixa","FecharCaixa"],
        ["vehicle"]=["GravarVeiculo","VeiculoSalvar"], ["instructor"]=["GravarInstrutor","InstrutorSalvarBasico"], ["package"]=["GravarPacote","PacoteSalvar"], ["user"]=["GravarAtendente","AtendSalvar"], ["unit"]=["GravarUnidade","UnidadeAlterar"]
    };
    static string Normalize(string s)=>new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    public static void Check(User user,string action)
    {
        if(!user.Imported||user.Role=="Administrador"||!Methods.TryGetValue(action,out var methods))return;
        var blocked=user.Permissions.Select(Normalize).ToHashSet();
        if(methods.Any(m=>blocked.Contains(Normalize(m))))throw new RuleException("Seu grupo do Infor bloqueia esta função. Solicite revisão de acesso ao administrador.",403);
    }
}

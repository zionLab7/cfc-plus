using System.Text.RegularExpressions;

namespace CfcPilot;

public enum GovLoginCommand { None, EnterGov, FillCpf, SubmitCpf, SubmitPassword }
public record GovLoginObservation(bool OnGov=false,bool Entry=false,bool Account=false,string AccountCpf="",bool Password=false,string Error="",bool Challenge=false,bool IdentityMatched=false,bool Closed=false,bool ChangedPage=false,bool PageReady=true,bool ContinueEnabled=true);
public record GovLoginStatus(string Stage,string Message,string Error,bool Active,bool Authenticated=false);

// A single authentication attempt. Time spent on a human challenge does not discard
// the attempt or start another request. No challenge response is read or generated.
public sealed class StudentGovLogin(string cpf,string password)
{
    string secret=password;
    bool entered,filledCpf,submittedCpf,submittedPassword,stopped,humanCpf;
    GovLoginStatus status=new("opening","Abrindo o acesso do aluno.","",true);
    public GovLoginStatus Status=>status;
    public string Cpf=>cpf;
    public string Password=>secret;
    public static string Digits(string value)=>string.Concat(value.Where(char.IsAsciiDigit));
    public static GovLoginStatus? ManualStatus(GovLoginStatus? previous,GovLoginObservation observed,string cpf)
    {
        if(observed.Error!="")return new("portal-error","O portal recusou a verificação. Confira a mensagem e a página antes de iniciar outra tentativa.",SafeError(observed.Error),false);
        if(Digits(observed.AccountCpf) is {Length:11} account&&account!=Digits(cpf))return new("identity-mismatch","O CPF na página não corresponde à ficha selecionada. Confira a identidade antes de continuar.","",false);
        if(observed.IdentityMatched)return new("authenticated","Sessão do aluno confirmada pelo CPF visível no DETRAN.","",false,true);
        if(previous?.Authenticated==true)return new("verify-session","A identidade não está visível nesta página. Confira a conta conectada; o portal pode ter encerrado o login.","",false);
        return previous?.Stage=="portal-error"?new("open","A mensagem de erro não está mais visível. Confira a próxima etapa na página.","",false):null;
    }
    public static string SafeError(string value,string cpf="",string secret="")
    {
        if(secret!="")value=value.Replace(secret,"[senha]",StringComparison.Ordinal);
        if(cpf!="")value=value.Replace(cpf,"[CPF]",StringComparison.Ordinal);
        value=Regex.Replace(value,@"https?://\S+","[endereço]");
        value=Regex.Replace(value,@"(?<!\d)\d{3}\.?\d{3}\.?\d{3}-?\d{2}(?!\d)","[CPF]");
        value=Regex.Replace(value,@"\b[A-Za-z0-9_+/=-]{40,}\b","[identificador]");
        value=Regex.Replace(value,@"\s+"," ").Trim();
        return value[..Math.Min(value.Length,500)];
    }
    void Set(string stage,string message,string error="",bool active=true,bool authenticated=false)=>status=new(stage,message,error,active,authenticated);
    public GovLoginCommand Observe(GovLoginObservation page)
    {
        if(stopped)return GovLoginCommand.None;
        if(page.ChangedPage){Stop("page-changed","Outra aba foi aberta no portal. O preenchimento foi pausado; confira a página e continue manualmente.");return GovLoginCommand.None;}
        if(page.Closed){Stop("closed","A janela do aluno foi fechada. Abra novamente pela ficha.");return GovLoginCommand.None;}
        if(page.IdentityMatched){secret="";stopped=true;Set("authenticated","Sessão do aluno confirmada pelo CPF visível no DETRAN.",active:false,authenticated:true);return GovLoginCommand.None;}
        if(!page.OnGov)
        {
            if(page.Entry&&!entered)return GovLoginCommand.EnterGov;
            Set("portal","Sessão aberta no portal. Confira a conta conectada; o login ainda não foi confirmado pelo CPF.");return GovLoginCommand.None;
        }
        var observed=Digits(page.AccountCpf);
        if(observed.Length==11&&observed!=cpf){Stop("identity-mismatch","O CPF exibido no GOV.BR é diferente do aluno. O preenchimento foi interrompido.");return GovLoginCommand.None;}
        if(page.Error!="")
        {
            var error=SafeError(page.Error,cpf,secret);
            Set("portal-error","O portal recusou a etapa. Confira a mensagem abaixo e a verificação na própria página. Não repetimos o envio automaticamente.",error);
            return GovLoginCommand.None;
        }
        if(page.Challenge){Set("challenge","Conclua a verificação na própria página. CPF e senha não serão enviados enquanto o desafio estiver aberto.");return GovLoginCommand.None;}
        if(submittedPassword){Set("waiting-result","Senha enviada uma vez. Conclua eventual verificação em duas etapas na página.");return GovLoginCommand.None;}
        if(page.Password)
        {
            if(!page.PageReady){Set("loading","Aguardando a página terminar de carregar antes de preencher a senha.");return GovLoginCommand.None;}
            // Never inject a saved secret into an unidentified, resumed password page.
            if(!submittedCpf&&observed!=cpf){Stop("identity-check","Confira a identidade no GOV.BR e continue manualmente. A senha salva não foi enviada.");return GovLoginCommand.None;}
            if(secret==""){Set("manual-password","Informe a senha na página. Nenhuma senha salva foi enviada.");return GovLoginCommand.None;}
            return GovLoginCommand.SubmitPassword;
        }
        if(humanCpf){Set("human-control","Você está controlando a página. Continue nela; o app não enviará o CPF em paralelo.");return GovLoginCommand.None;}
        if(page.Account&&!filledCpf&&!submittedCpf)
        {
            if(cpf.Length!=11){Stop("invalid-cpf","Confira o CPF na ficha antes de iniciar o login.");return GovLoginCommand.None;}
            if(!page.PageReady){Set("loading","Aguardando a página terminar de carregar antes de preencher o CPF.");return GovLoginCommand.None;}
            return GovLoginCommand.FillCpf;
        }
        if(page.Account&&!submittedCpf&&!page.Challenge&&page.PageReady&&page.ContinueEnabled&&observed==cpf)return GovLoginCommand.SubmitCpf;
        Set(page.Challenge?"challenge":"waiting-password",page.Challenge?"Conclua a verificação e clique em Continuar na própria página. O app acompanha a mesma tentativa.":secret==""?"Aguardando a próxima etapa do GOV.BR. Informe a senha na página quando solicitado.":"Aguardando a próxima etapa do GOV.BR. A senha será preenchida quando o portal liberar o campo.");
        return GovLoginCommand.None;
    }
    public void Attempted(GovLoginCommand command)
    {
        // Mark before dispatch so an ambiguous timeout cannot cause duplicate submissions.
        if(command==GovLoginCommand.EnterGov)entered=true;
        if(command==GovLoginCommand.FillCpf){filledCpf=true;Set("cpf-filled","CPF preenchido. Aguardando a página e o botão Continuar.");}
        if(command==GovLoginCommand.SubmitCpf){submittedCpf=true;Set("waiting-password","CPF enviado uma vez. Aguardando a senha ou a verificação solicitada pelo GOV.BR.");}
        if(command==GovLoginCommand.SubmitPassword){submittedPassword=true;Set("waiting-result","Enviando a senha salva do aluno ao GOV.BR.");}
    }
    public void PasswordDispatched()=>secret="";
    public void HumanInput(bool passwordVisible)
    {
        humanCpf=true;
        if(passwordVisible)Stop("human-control","Preenchimento pausado para não alterar a senha enquanto você controla a página.");
    }
    public void Stop(string stage="paused",string message="Preenchimento automático pausado. A sessão do navegador continua disponível.") {secret="";stopped=true;Set(stage,message,active:false);}
    public void Failure(string message)=>Set("page-changed",message);
}

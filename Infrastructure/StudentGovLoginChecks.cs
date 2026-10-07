using System.Text.Json;

namespace CfcPilot;

public static class StudentGovLoginChecks
{
    public static void Run()
    {
        var passed=0;
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);passed++;Console.WriteLine("PASS "+label);}
        void Refused(PortalInput input,string label){try{input.Validate();throw new InvalidOperationException(label);}catch(RuleException){passed++;Console.WriteLine("PASS "+label);}}
        const string cpf="12345000007",password="Senha fictícia! 42";
        var login=new StudentGovLogin(cpf,password);
        Check(login.Observe(new(Entry:true))==GovLoginCommand.EnterGov,"Seleciona a entrada oficial do GOV.BR");login.Attempted(GovLoginCommand.EnterGov);
        Check(login.Observe(new(Entry:true))==GovLoginCommand.None,"Não clica novamente no mesmo pedido de autenticação");
        Check(login.Observe(new(OnGov:true,Account:true))==GovLoginCommand.FillCpf,"Preenche o CPF do aluno sem enviar formulário no mesmo comando");login.Attempted(GovLoginCommand.FillCpf);
        Check(login.Observe(new(OnGov:true,Account:true,AccountCpf:cpf,PageReady:false))==GovLoginCommand.None,"Não envia formulário enquanto a página ainda está carregando");
        Check(login.Observe(new(OnGov:true,Account:true,AccountCpf:cpf,ContinueEnabled:false))==GovLoginCommand.None,"Botão desabilitado não provoca envio antecipado");
        Check(login.Observe(new(OnGov:true,Account:true,AccountCpf:cpf,Challenge:true))==GovLoginCommand.None,"Desafio visível aguarda a pessoa continuar na própria página");
        Check(login.Observe(new(OnGov:true,Account:true,AccountCpf:cpf))==GovLoginCommand.SubmitCpf,"CPF conferido é enviado quando a página termina de carregar");login.Attempted(GovLoginCommand.SubmitCpf);
        Check(login.Observe(new(OnGov:true,Account:true,AccountCpf:cpf))==GovLoginCommand.None,"CPF não é reenviado enquanto a página espera");
        var waits=true;for(var i=0;i<1000;i++)waits&=login.Observe(new(OnGov:true,Challenge:true))==GovLoginCommand.None&&login.Status.Active&&login.Password==password;
        Check(waits,"Mil observações do desafio conservam a tentativa e não reenviam credenciais");
        Check(login.Observe(new(OnGov:true,Error:"CAPTCHA inválido (ERL0000900)",Challenge:true))==GovLoginCommand.None&&login.Status.Error.Contains("ERL0000900"),"Registra a recusa do CAPTCHA sem repetir CPF ou senha");
        Check(login.Observe(new(OnGov:true,Password:true,AccountCpf:cpf))==GovLoginCommand.SubmitPassword,"Retoma a senha quando a verificação humana libera o campo");
        login.Attempted(GovLoginCommand.SubmitPassword);login.PasswordDispatched();
        Check(login.Observe(new(OnGov:true,Password:true,AccountCpf:cpf))==GovLoginCommand.None&&login.Password=="","Senha enviada uma vez e descartada da tentativa");
        Check(login.Observe(new(OnGov:true,Error:"Senha inválida"))==GovLoginCommand.None&&login.Status.Stage=="portal-error","Senha incorreta não dispara novas tentativas");
        Check(login.Observe(new())==GovLoginCommand.None&&!login.Status.Authenticated,"Redirecionamento sozinho não confirma autenticação");
        Check(login.Observe(new(IdentityMatched:true))==GovLoginCommand.None&&login.Status.Authenticated&&!login.Status.Active,"CPF conferido no DETRAN confirma a identidade");
        var wrong=new StudentGovLogin(cpf,password);wrong.Attempted(GovLoginCommand.SubmitCpf);
        Check(wrong.Observe(new(OnGov:true,Password:true,AccountCpf:"98765000001"))==GovLoginCommand.None&&!wrong.Status.Active&&wrong.Password=="","Mudança do CPF interrompe e limpa a senha");
        var resumed=new StudentGovLogin(cpf,password);
        Check(resumed.Observe(new(OnGov:true,Password:true))==GovLoginCommand.None&&!resumed.Status.Active,"Página de senha sem identidade não recebe credenciais salvas");
        var popup=new StudentGovLogin(cpf,password);popup.Attempted(GovLoginCommand.SubmitCpf);
        Check(popup.Observe(new(OnGov:true,Password:true,ChangedPage:true))==GovLoginCommand.None&&popup.Password=="","Nova aba não herda autorização para preencher a senha da tentativa anterior");
        var identified=new StudentGovLogin(cpf,password);
        Check(identified.Observe(new(OnGov:true,Password:true,AccountCpf:cpf,Challenge:true))==GovLoginCommand.None&&identified.Password==password,"Campo de senha com desafio aberto não recebe nem envia credenciais");
        Check(identified.Observe(new(OnGov:true,Password:true,AccountCpf:cpf))==GovLoginCommand.SubmitPassword,"Página existente com CPF exato pode continuar o acesso");
        var manual=new StudentGovLogin(cpf,"");
        Check(manual.Observe(new(OnGov:true,Account:true))==GovLoginCommand.FillCpf,"CPF é preenchido quando não há senha salva");manual.Attempted(GovLoginCommand.FillCpf);
        Check(manual.Observe(new(OnGov:true,Password:true,AccountCpf:cpf))==GovLoginCommand.None&&manual.Status.Stage=="manual-password","Continuação humana com CPF correto pede senha na mesma página");manual.Attempted(GovLoginCommand.SubmitCpf);
        Check(manual.Observe(new(OnGov:true,Password:true))==GovLoginCommand.None&&manual.Status.Stage=="manual-password","Sem senha salva, pede digitação na mesma página");
        var paused=new StudentGovLogin(cpf,password);paused.Stop();
        var human=new StudentGovLogin(cpf,password);human.Attempted(GovLoginCommand.FillCpf);human.HumanInput(false);
        Check(human.Observe(new(OnGov:true,Account:true,AccountCpf:cpf))==GovLoginCommand.None,"Clique humano impede segundo envio de CPF pelo monitor");
        Check(human.Observe(new(OnGov:true,Password:true,AccountCpf:cpf))==GovLoginCommand.SubmitPassword,"Senha salva pode continuar quando a página humana identifica o CPF");
        human.HumanInput(true);Check(human.Password==""&&!human.Status.Active,"Digitação humana na etapa de senha pausa preenchimento concorrente");
        Check(paused.Observe(new(OnGov:true,Account:true))==GovLoginCommand.None&&paused.Password=="","Pausa impede novos envios e libera a senha da memória");
        var closed=new StudentGovLogin(cpf,password);
        Check(closed.Observe(new(Closed:true))==GovLoginCommand.None&&closed.Password==""&&!closed.Status.Active,"Janela fechada descarta a tentativa");
        var sanitized=StudentGovLogin.SafeError("CAPTCHA inválido "+cpf+" "+password+" https://sso.acesso.gov.br/?code=segredo "+new string('x',80),cpf,password);
        Check(sanitized.Contains("CAPTCHA inválido")&&!sanitized.Contains(cpf)&&!sanitized.Contains(password)&&!sanitized.Contains("code=")&&!sanitized.Contains(new string('x',80)),"Diagnóstico exclui senha, CPF, URL e token");
        Check(!JsonSerializer.Serialize(login.Status).Contains(password),"Estado público não expõe a senha");
        var observedError=StudentGovLogin.ManualStatus(null,new(OnGov:true,Error:"Captcha inválido. Tente novamente. (ERL0000900)"),cpf);
        Check(observedError is {Active:false,Authenticated:false}&&observedError.Error.Contains("ERL0000900"),"Diagnóstico manual preserva o código do CAPTCHA sem ativar preenchimento");
        Check(StudentGovLogin.ManualStatus(null,new(OnGov:true,AccountCpf:"98765000001"),cpf)?.Stage=="identity-mismatch","Navegação manual informa CPF diferente da ficha");
        Check(StudentGovLogin.ManualStatus(login.Status,new(OnGov:true,Account:true),cpf)?.Authenticated==false,"Retorno ao login invalida indicação anterior de autenticação");
        Check(StudentGovLogin.ManualStatus(null,new(IdentityMatched:true),cpf)?.Authenticated==true,"Leitura manual confirma sessão somente com identidade verificada");
        Check(StudentGovLogin.ManualStatus(observedError,new(OnGov:true,Account:true),cpf)?.Error=="","Erro manual é removido somente após desaparecer da página");
        var loading=new StudentGovLogin(cpf,password);
        Check(loading.Observe(new(OnGov:true,Account:true,PageReady:false))==GovLoginCommand.None&&loading.Password==password,"Página incompleta não recebe CPF");
        Check(loading.Observe(new(OnGov:true,Password:true,AccountCpf:cpf,PageReady:false))==GovLoginCommand.None,"Página de senha incompleta não recebe segredo");
        loading.Attempted(GovLoginCommand.FillCpf);
        Check(loading.Observe(new(OnGov:true,Password:true,AccountCpf:cpf))==GovLoginCommand.SubmitPassword,"Senha retoma após continuação humana com CPF exato");
        new PortalInput("key",Key:"Space").Validate();Check(true,"Espaço chega ao navegador como tecla para controles da página");
        new PortalInput("click",1279,799).Validate();new PortalInput("key",Key:"Tab").Validate();new PortalInput("text",Text:"Teste").Validate();new PortalInput("scroll",DeltaY:-1200).Validate();
        new PortalInput("down",10,20).Validate();new PortalInput("move",11,21).Validate();new PortalInput("up",12,22).Validate();Check(true,"Eventos reais de pressionar, arrastar e soltar são aceitos");
        Check(true,"Comandos de interação humana válidos são aceitos");
        Refused(new("evaluate",Text:"script"),"Não aceita execução de código no navegador");
        Refused(new("click",double.NaN),"Coordenada inválida é recusada");Refused(new("click",1280),"Clique fora da página é recusado");
        Refused(new("key",Key:"Control+L"),"Teclas de navegação arbitrária não são aceitas");
        Refused(new("text",Text:new string('x',2001)),"Texto excessivo é recusado");Refused(new("scroll",DeltaY:double.PositiveInfinity),"Rolagem inválida é recusada");
        Console.WriteLine(passed+" verificações de login e interação aprovadas. Sem portais, credenciais reais ou resolução de CAPTCHA.");
    }
}

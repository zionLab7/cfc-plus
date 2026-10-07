using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.FileProviders;

namespace CfcPilot;

public static class ManualBrowserChecks
{
    sealed class TestEnvironment(string path) : IWebHostEnvironment
    {
        public string ApplicationName{get;set;}="CfcPilot";public string EnvironmentName{get;set;}="Testing";
        public string ContentRootPath{get;set;}=path;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public string WebRootPath{get;set;}=path;public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
    sealed class TestLauncher : IPortalWindowLauncher
    {
        public int Calls;public bool Fail;
        public void Open(ProcessStartInfo start){Calls++;if(Fail)throw new RuleException("Falha simulada ao abrir o navegador.",503);}
    }
    public static void Run()
    {
        var root=Path.Combine(Directory.GetCurrentDirectory(),"test-results","manual-browser-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"CFC_DATA_DIR",root}}).Build();
        var runtime=new BrowserRuntime(config,new TestEnvironment(root));var executable=Path.Combine(root,"navegador de teste; sem execução.exe");File.WriteAllText(executable,"Fixture de caminho, nunca executada.");
        var options=new PortalBrowserOptions("chrome",executable);runtime.Save(new User{Role="Administrador"},options);
        var student=new BrowserProfile{Id=Guid.NewGuid().ToString("N"),Kind="Aluno",StudentId="s1"};var other=new BrowserProfile{Id=Guid.NewGuid().ToString("N"),Kind="Aluno",StudentId="s2"};
        var passed=0;
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);passed++;Console.WriteLine("PASS "+label);}
        void Refused(Action action,int status,string label){try{action();throw new InvalidOperationException(label);}catch(RuleException ex){Check(ex.Status==status,label);}}
        var plan=runtime.ManualLaunchOptions(options,student.Id,ProfessionalBrowser.StudentPortal());
        Check(plan.FileName==executable&&!plan.UseShellExecute,"Navegador normal abre sem interpretar comandos de shell");
        Check(plan.ArgumentList.Count==4&&plan.ArgumentList.Last()==ProfessionalBrowser.StudentPortal(),"Abre somente o endereço público oficial, sem CPF ou senha");
        Check(plan.ArgumentList.All(x=>!x.Contains("remote-debugging")&&!x.Contains("enable-automation")&&!x.Contains("disable-blink")&&!x.Contains("headless")),"Login manual não usa controle de página, depuração ou flags de disfarce");
        Check(plan.ArgumentList[0]=="--user-data-dir="+runtime.ManualProfileDirectory(options,student.Id),"Janela normal utiliza o perfil persistente do aluno");
        Check(runtime.ManualProfileDirectory(options,student.Id)!=runtime.ManualProfileDirectory(options,other.Id),"Alunos diferentes mantêm cookies separados");
        Check(runtime.ManualProfileDirectory(options,student.Id)!=runtime.ProfileDirectory(options,student.Id),"Sessão manual não compartilha perfil em uso pela automação");
        Check(!Directory.Exists(runtime.ManualProfileDirectory(options,student.Id)),"Preparar abertura não executa navegador ou altera cookies");
        Refused(()=>runtime.ManualLaunchOptions(options,"../perfil",ProfessionalBrowser.StudentPortal()),400,"Perfil não permite sair do diretório privado");
        Refused(()=>runtime.ManualLaunchOptions(options,student.Id,"https://example.com"),400,"Navegador não abre destinos fora dos portais permitidos");
        var launcher=new TestLauncher();var browser=new ManualPortalBrowser(runtime,launcher);var job=Guid.NewGuid().ToString("N");
        Refused(()=>browser.Open(student,ProfessionalBrowser.StudentPortal(),IPAddress.Parse("192.0.2.1"),job),403,"Cliente remoto não abre navegador no servidor");
        Refused(()=>browser.Open(student,ProfessionalBrowser.StudentPortal(),null,job),403,"Abertura exige origem local identificada");
        Refused(()=>browser.Open(new BrowserProfile{Id=student.Id,Kind="Diretor"},ProfessionalBrowser.StudentPortal(),IPAddress.Loopback,job),409,"Acesso manual do aluno não usa perfil profissional");
        Refused(()=>browser.Open(student,ProfessionalBrowser.StudentPortal(),IPAddress.Loopback,"inválido"),400,"Abertura exige identificador válido");
        browser.Open(student,ProfessionalBrowser.StudentPortal(),IPAddress.Loopback,job);
        Check(launcher.Calls==1&&Directory.Exists(runtime.ManualProfileDirectory(options,student.Id)),"Abertura autorizada cria o perfil e solicita uma janela");
        browser.Open(student,ProfessionalBrowser.StudentPortal(),IPAddress.Loopback,job);
        Check(launcher.Calls==1,"Repetição da mesma operação não abre outra janela");
        Refused(()=>browser.Open(other,ProfessionalBrowser.StudentPortal(),IPAddress.Loopback,job),409,"Operação repetida não pode trocar a identidade do aluno");
        var secondJob=Guid.NewGuid().ToString("N");launcher.Fail=true;
        Refused(()=>browser.Open(other,ProfessionalBrowser.StudentPortal(),IPAddress.Loopback,secondJob),503,"Falha de abertura é informada e não confirma o login");
        launcher.Fail=false;browser.Open(other,ProfessionalBrowser.StudentPortal(),IPAddress.Loopback,secondJob);
        Check(launcher.Calls==3,"Após falha real, uma nova solicitação pode abrir a janela");
        Console.WriteLine(passed+" verificações de navegador manual aprovadas. Nenhum portal ou credencial foi acessado.");
    }
}

using System.Diagnostics;
using System.Net;

namespace CfcPilot;

public interface IPortalWindowLauncher { void Open(ProcessStartInfo start); }
public sealed class PortalWindowLauncher : IPortalWindowLauncher
{
    public void Open(ProcessStartInfo start)
    {
        try { using var process=Process.Start(start)??throw new RuleException("O navegador não iniciou. Confira a instalação de Chrome/Chromium.",503); }
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { throw new RuleException("Não foi possível abrir Chrome/Chromium. Confira o navegador configurado e a sessão de desktop deste servidor.",503); }
    }
}

// Manual authentication in a normal browser. No page control, credential submission or CAPTCHA handling.
public sealed class ManualPortalBrowser(BrowserRuntime runtime,IPortalWindowLauncher launcher)
{
    readonly object gate=new();
    readonly Dictionary<string,string> opened=new();
    public void Open(BrowserProfile profile,string url,IPAddress? remote,string jobId)
    {
        if(remote==null||!IPAddress.IsLoopback(remote))throw new RuleException("A janela abre no computador do servidor. Neste computador ou celular, use um navegador externo para acessar o portal.",403);
        if(profile.Kind!="Aluno"||profile.StudentId=="")throw new RuleException("O login manual exige o perfil exclusivo do aluno.",409);
        if(!Guid.TryParseExact(jobId,"N",out _))throw new RuleException("Operação de navegador inválida.");
        var options=runtime.Options();var start=runtime.ManualLaunchOptions(options,profile.Id,url);
        var fingerprint=BrowserRuntime.Key(options)+"/"+profile.Id+"/"+url;
        lock(gate)
        {
            if(opened.TryGetValue(jobId,out var prior))
            {
                if(prior!=fingerprint)throw new RuleException("Identificador de abertura reutilizado para outra sessão.",409);
                return;
            }
            Directory.CreateDirectory(runtime.ManualProfileDirectory(options,profile.Id));
            launcher.Open(start);opened[jobId]=fingerprint;
        }
    }
}

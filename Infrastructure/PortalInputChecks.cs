using Microsoft.Playwright;
namespace CfcPilot;

// Exercises the product's input dispatcher against local HTML only. All
// browser requests are aborted; no portal, credential or CAPTCHA is used.
public static class PortalInputChecks
{
    public static async Task Run()
    {
        var passed=0;
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);passed++;Console.WriteLine("PASS "+label);}
        using var engine=await Playwright.CreateAsync();
        await using var browser=await engine.Chromium.LaunchAsync(new(){Channel="chrome",Headless=true});
        await using var context=await browser.NewContextAsync(new(){ViewportSize=new(){Width=1280,Height=800}});
        await context.RouteAsync("**/*",route=>route.AbortAsync());
        var page=await context.NewPageAsync();
        await page.SetContentAsync("""
            <style>body{margin:0}#drag{position:absolute;left:10px;top:10px;width:200px;height:50px}input{position:absolute;top:80px}</style>
            <button id="drag">Arrastar</button><input id="typed"><script>
            window.events=[]; for(const type of ['mousedown','mousemove','mouseup','click','keydown','keyup'])document.addEventListener(type,e=>events.push(type));
            </script>
            """);
        await new PortalInput("down",30,30).Dispatch(page);
        await new PortalInput("move",100,30).Dispatch(page);
        await new PortalInput("up",100,30).Dispatch(page);
        var events=await page.EvaluateAsync<string[]>("events");
        Check(events.Contains("mousedown")&&events.Contains("mousemove")&&events.Contains("mouseup"),"Arrasto transmite pressionar, mover e soltar ao navegador");
        Check(events.Count(x=>x=="click")==1,"Um gesto não produz dois cliques no formulário");
        await page.Locator("#typed").FocusAsync();await new PortalInput("text",Text:"teste42").Dispatch(page);
        Check(await page.Locator("#typed").InputValueAsync()=="teste42","Texto humano chega ao campo correto");
        events=await page.EvaluateAsync<string[]>("events");Check(events.Contains("keydown")&&events.Contains("keyup"),"Digitação transmite eventos de teclado exigidos por formulários");
        await page.SetContentAsync("<iframe src='https://hcaptcha.com/fixture' style='width:302px;height:76px'></iframe>");
        Check(!await PortalSessions.HumanChallengeVisible(page),"Checkbox pequeno não é confundido com painel de desafio aberto");
        await page.Locator("iframe").EvaluateAsync("el=>el.style.height='400px'");
        Check(await PortalSessions.HumanChallengeVisible(page),"Painel de desafio aberto pausa envio assistido");
        await page.Locator("iframe").EvaluateAsync("el=>el.style.display='none'");
        Check(!await PortalSessions.HumanChallengeVisible(page),"Painel fechado libera a próxima etapa sem ler respostas do desafio");
        await page.SetContentAsync("<p role='alert'>Captcha inválido. Tente novamente. (ERL0000900)</p><input type=password value='NAO_LER'>");
        var error=await PortalSessions.GovPageError(page);
        Check(error.Contains("ERL0000900")&&!error.Contains("NAO_LER"),"Diagnóstico lê a mensagem visível sem ler senha");
        Console.WriteLine(passed+" verificações de interação aprovadas. Página local, rede bloqueada.");
    }
}

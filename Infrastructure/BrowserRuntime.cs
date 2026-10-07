using Microsoft.Playwright;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace CfcPilot;

public record PortalBrowserOptions(string Engine="chrome",string ExecutablePath="",int LoginWaitMinutes=15,bool Headless=false,int MaxProfessionalProfiles=8);
public sealed class BrowserRuntime(IConfiguration config,IWebHostEnvironment env)
{
    readonly object gate=new();
    string Root=>Path.GetFullPath(config["CFC_DATA_DIR"]??Path.Combine(env.ContentRootPath,"App_Data"));
    string ConfigPath=>Path.Combine(Root,"portal-browser.json");
    public PortalBrowserOptions Options()
    {
        lock(gate){return File.Exists(ConfigPath)?JsonSerializer.Deserialize<PortalBrowserOptions>(File.ReadAllText(ConfigPath),Store.Json)??new():new(config["PortalBrowser:Engine"]??"chrome",config["PortalBrowser:ExecutablePath"]??"",MaxProfessionalProfiles:Math.Clamp(config.GetValue<int?>("NativePortal:MaxProfiles")??8,1,128));}
    }
    public object Save(User user,PortalBrowserOptions options)
    {
        if(user.Role!="Administrador")throw new RuleException("Somente o administrador configura o navegador.",403);
        Validate(options);lock(gate){Directory.CreateDirectory(Root);var temp=ConfigPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(options,Store.Json));File.Move(temp,ConfigPath,true);}return Availability();
    }
    static void Validate(PortalBrowserOptions options)
    {
        if(options.Engine is not("chrome" or "chromium"))throw new RuleException("Escolha Google Chrome ou Chromium.");
        if(options.LoginWaitMinutes is <1 or >30)throw new RuleException("O prazo de preenchimento deve ser de 1 a 30 minutos.");
        if(options.MaxProfessionalProfiles is <1 or >128)throw new RuleException("O limite de perfis simultâneos deve ser de 1 a 128.");
        if(options.ExecutablePath!=""&&(!Path.IsPathFullyQualified(options.ExecutablePath)||!File.Exists(options.ExecutablePath)))throw new RuleException("Informe o caminho completo de um navegador instalado neste servidor.");
        if(Path.GetFileNameWithoutExtension(options.ExecutablePath).Contains("msedge",StringComparison.OrdinalIgnoreCase))throw new RuleException("Selecione Chrome ou Chromium. Edge não é usado neste aplicativo.");
    }
    static string? ChromeExecutable()
    {
        string[] candidates=OperatingSystem.IsWindows()?
        [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google/Chrome/Application/chrome.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Google/Chrome/Application/chrome.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google/Chrome/Application/chrome.exe")]:
        OperatingSystem.IsMacOS()?["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Applications/Google Chrome.app/Contents/MacOS/Google Chrome")]:
        ["/opt/google/chrome/chrome","/usr/bin/google-chrome","/usr/bin/google-chrome-stable"];
        return candidates.FirstOrDefault(File.Exists);
    }
    static string? ChromiumExecutable()
    {
        string[] installed=OperatingSystem.IsWindows()?[]:OperatingSystem.IsMacOS()?["/Applications/Chromium.app/Contents/MacOS/Chromium"]:["/usr/bin/chromium","/usr/bin/chromium-browser"];
        var binary=installed.FirstOrDefault(File.Exists);if(binary!=null)return binary;
        var cache=Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        cache=string.IsNullOrWhiteSpace(cache)?OperatingSystem.IsWindows()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ms-playwright"):OperatingSystem.IsMacOS()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library/Caches/ms-playwright"):Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".cache"),"ms-playwright"):cache;
        if(!Directory.Exists(cache))return null;
        return Directory.EnumerateDirectories(cache,"chromium-*").SelectMany(dir=>new[]{Path.Combine(dir,"chrome-win64/chrome.exe"),Path.Combine(dir,"chrome-win/chrome.exe"),Path.Combine(dir,"chrome-linux64/chrome"),Path.Combine(dir,"chrome-linux/chrome"),Path.Combine(dir,"chrome-mac-arm64/Chromium.app/Contents/MacOS/Chromium"),Path.Combine(dir,"chrome-mac/Chromium.app/Contents/MacOS/Chromium")}).FirstOrDefault(File.Exists);
    }
    public object Availability()
    {
        var options=Options();var installed=options.ExecutablePath!=""?File.Exists(options.ExecutablePath):options.Engine=="chrome"?ChromeExecutable()!=null:ChromiumExecutable()!=null;
        var native=config["NativePortal:Enabled"]=="true";
        return new{available=installed,engine=options.Engine,executablePath=options.ExecutablePath,loginWaitMinutes=options.LoginWaitMinutes,headless=native?false:EffectiveHeadless(options),configuredHeadless=options.Headless,nativeLogin=native,maxProfessionalProfiles=options.MaxProfessionalProfiles,name=native?"CFC+ interativo (Chromium)":options.Engine=="chrome"?"Google Chrome":"Chromium",computer="Computador que executa o servidor",mode="Navegador interno com perfil persistente por pessoa",studentLoginMode="assisted",platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription};
    }
    // Running independently of the client is not the same as lacking a desktop.
    // A real Windows service / Linux host without DISPLAY still needs headless.
    public static bool ResolveHeadless(bool configured,bool desktopAvailable)=>configured||!desktopAvailable;
    public bool EffectiveHeadless(PortalBrowserOptions options)=>ResolveHeadless(options.Headless,
        OperatingSystem.IsWindows()?Environment.UserInteractive:
        OperatingSystem.IsMacOS()||!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")));
    public int LoginWaitMinutes()=>Options().LoginWaitMinutes;
    public static string Key(PortalBrowserOptions options)=>options.Engine+"-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.ExecutablePath)))[..12].ToLowerInvariant();
    public string ProfileDirectory(PortalBrowserOptions options,string id)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new RuleException("Perfil de navegador inválido.");
        Validate(options);return Path.Combine(Root,"browser-profiles",Key(options),id);
    }
    public BrowserTypeLaunchPersistentContextOptions LaunchOptions(PortalBrowserOptions options)
    {
        Validate(options);return new(){Channel=options.ExecutablePath==""&&options.Engine=="chrome"?"chrome":null,ExecutablePath=options.ExecutablePath!=""?options.ExecutablePath:options.Engine=="chromium"?ChromiumExecutable():null,Headless=EffectiveHeadless(options),ViewportSize=new(){Width=1280,Height=800},AcceptDownloads=false,Timeout=25000};
    }
    public string ManualProfileDirectory(PortalBrowserOptions options,string id)
    {
        ProfileDirectory(options,id);return Path.Combine(Root,"manual-browser-profiles",Key(options),id);
    }
    public ProcessStartInfo ManualLaunchOptions(PortalBrowserOptions options,string id,string url)
    {
        Validate(options);PortalSessions.Allowed(url);
        var executable=options.ExecutablePath!=""?options.ExecutablePath:options.Engine=="chrome"?ChromeExecutable():ChromiumExecutable();
        if(executable==null)throw new RuleException("Instale ou configure Chrome/Chromium no computador do servidor, ou abra o portal em um navegador externo neste computador.",503);
        var start=new ProcessStartInfo(executable){UseShellExecute=false};
        start.ArgumentList.Add("--user-data-dir="+ManualProfileDirectory(options,id));
        start.ArgumentList.Add("--no-first-run");start.ArgumentList.Add("--new-window");start.ArgumentList.Add(url);
        return start;
    }
}

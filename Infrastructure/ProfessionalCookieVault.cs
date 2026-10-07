using Microsoft.AspNetCore.DataProtection;
using Microsoft.Playwright;
using System.Security.Cryptography;
using System.Text.Json;
namespace CfcPilot;

// Session cookies may otherwise be discarded by Chromium on a clean restart.
// Preserve their original expiry, encrypted under the server's protection keys.
public sealed class ProfessionalCookieVault(BrowserRuntime runtime,IDataProtectionProvider protection)
{
    string PathFor(PortalBrowserOptions options,string profileId)=>Path.Combine(runtime.ProfileDirectory(options,profileId),"cfc-session-cookies.protected");
    IDataProtector Protector(PortalBrowserOptions options,string id)=>protection.CreateProtector("CFC.ProfessionalCookies.v1",BrowserRuntime.Key(options),id);
    internal static bool AllowedCookie(Cookie cookie)
    {
        var domain=cookie.Domain?.TrimStart('.')??"";if(domain=="")return false;
        return PortalSessions.CookieDomainAllowed(domain)&&(cookie.Expires is null or <=0||cookie.Expires>DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
    public Cookie[] Read(PortalBrowserOptions options,string id)
    {
        var file=PathFor(options,id);if(!File.Exists(file))return [];
        try{return (JsonSerializer.Deserialize<Cookie[]>(Protector(options,id).Unprotect(File.ReadAllText(file)),Store.Json)??[]).Where(AllowedCookie).ToArray();}
        catch(Exception ex) when(ex is CryptographicException or JsonException){throw new RuleException("O checkpoint de cookies não pôde ser lido. O perfil foi preservado; confira as chaves e a conta que executa o servidor.",409);}
    }
    internal static Cookie[] MissingCookies(Cookie[] saved,IEnumerable<BrowserContextCookiesResult> current)
    {
        static string Identity(string name,string domain,string path)=>name+"\n"+domain.TrimStart('.').ToLowerInvariant()+"\n"+path;
        var live=current.Select(c=>Identity(c.Name,c.Domain,c.Path)).ToHashSet(StringComparer.Ordinal);
        // Native profile cookies are newer than a checkpoint taken earlier.
        // Rehydrate missing session cookies without replacing live SSO/CSRF state.
        return saved.Where(AllowedCookie).Where(c=>!live.Contains(Identity(c.Name,c.Domain??"",c.Path??"/"))).ToArray();
    }
    public void Save(PortalBrowserOptions options,string id,Cookie[] cookies)
    {
        var file=PathFor(options,id);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var text=Protector(options,id).Protect(JsonSerializer.Serialize(cookies.Where(AllowedCookie).ToArray(),Store.Json));
        File.WriteAllText(file+".tmp",text);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(file+".tmp",UnixFileMode.UserRead|UnixFileMode.UserWrite);File.Move(file+".tmp",file,true);
    }
}

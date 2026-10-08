using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
namespace CfcPilot;
public static class TenantEndpoints
{
 public static void UseTenantSelection(this WebApplication app)
 {
 app.Use(async(ctx,next)=>{
 var path=ctx.Request.Path.ToString();if(!path.StartsWith("/api/")||path=="/api/health"||path.StartsWith("/api/platform/")||path.StartsWith("/api/distribution")||path=="/api/auth/logout"){await next();return;}
 var platform=app.Services.GetRequiredService<TenantPlatform>();string id;
 if(path=="/api/auth/login"){
 if(ctx.Request.ContentLength>16384)throw new RuleException("Dados de acesso inválidos.",400);ctx.Request.EnableBuffering(16384,16384);try{using var body=await JsonDocument.ParseAsync(ctx.Request.Body);id=body.RootElement.TryGetProperty("schoolId",out var school)?school.GetString()??"":"";}catch(IOException){throw new RuleException("Dados de acesso inválidos.",400);}finally{ctx.Request.Body.Position=0;}
 }else if(path=="/api/auth/units"){id=ctx.Request.Query["schoolId"].ToString();if(id==""){await ctx.Response.WriteAsJsonAsync(Array.Empty<object>());return;}}
 else{if(ctx.User.Identity?.IsAuthenticated!=true)throw new RuleException("Entre com o ID da escola e seu usuário.",401);id=ctx.User.FindFirstValue("cfc-school")??"";var requested=ctx.Request.Headers["X-CFC-School"].ToString();if(requested!=""&&requested!=id||ctx.Request.Query.TryGetValue("schoolId",out var q)&&q.ToString()!=id)throw new RuleException("A sessão pertence a outra autoescola.",403);}
 var runtime=platform.Get(id);ctx.Items["cfc-school"]=TenantPlatform.Normalize(id);ctx.Items["cfc-school-name"]=runtime.SchoolName;ctx.Response.Headers["X-CFC-School"]=TenantPlatform.Normalize(id);
 if(ctx.Request.Method!="GET"&&runtime.Services.GetRequiredService<ImportQueue>().Maintenance)throw new RuleException("A base desta escola está sendo recarregada.",503);
 var original=ctx.RequestServices;ctx.RequestServices=new TenantRequestServices(original,runtime.Services);try{await next();}finally{ctx.RequestServices=original;}
 });
 }
 public static void MapPlatform(this WebApplication app)
 {
 app.MapPost("/api/platform/login",async(HttpContext ctx,TenantPlatform platform,JsonElement body)=>{var login=body.GetProperty("login").GetString()??"";var password=body.GetProperty("password").GetString()??"";if(login.Length is <1 or >80||password.Length is <1 or >128)throw new RuleException("Acesso da plataforma inválido.",401);var a=platform.Account();if(login!=a.Login||!Passwords.Verify(password,a.PasswordHash))throw new RuleException("Acesso da plataforma inválido.",401);await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,"platform-owner"),new("cfc-platform","owner"),new("cfc-stamp",TenantPlatform.Stamp(a.PasswordHash))],CookieAuthenticationDefaults.AuthenticationScheme)));return Results.Ok(new{mustChangePassword=a.MustChange});}).RequireRateLimiting("login");
 app.MapGet("/api/platform/me",(HttpContext ctx,TenantPlatform p)=>{p.Authorize(ctx);return Results.Ok(new{mustChangePassword=p.Account().MustChange});}).RequireAuthorization();
 app.MapPost("/api/platform/password",async(HttpContext ctx,TenantPlatform p,JsonElement b)=>{await p.ChangePassword(ctx,b.GetProperty("currentPassword").GetString()??"",b.GetProperty("newPassword").GetString()??"");await ctx.SignOutAsync();return Results.Ok();}).RequireAuthorization().RequireRateLimiting("login");
 app.MapGet("/api/platform/schools",(HttpContext ctx,TenantPlatform p)=>{p.Authorize(ctx);if(p.Account().MustChange)throw new RuleException("Troque a senha inicial da plataforma.",428);return Results.Ok(p.Schools().Select(s=>new{s.Id,s.Name,s.Active,s.Created}));}).RequireAuthorization();
 app.MapPost("/api/platform/schools",async(HttpContext ctx,TenantPlatform p,JsonElement b)=>{p.Authorize(ctx);if(p.Account().MustChange)throw new RuleException("Troque a senha inicial da plataforma.",428);return Results.Ok(await p.Register(b.GetProperty("id").GetString()??"",b.GetProperty("name").GetString()??""));}).RequireAuthorization();
 }
}

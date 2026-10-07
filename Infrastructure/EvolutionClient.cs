using System.Net.Http.Json;
using System.Text.Json;

namespace CfcPilot;
public sealed class EvolutionClient(HttpClient http, IConfiguration config)
{
    private string Base => config["Evolution:BaseUrl"]?.TrimEnd('/') ?? "";
    private string Instance => config["Evolution:Instance"] ?? "";
    private string Key => config["Evolution:ApiKey"] ?? "";
    public bool Configured => Base != "" && Instance != "" && Key != "";
    private async Task<JsonElement> Call(string path, object? body = null)
    {
        if (!Configured) throw new RuleException("Evolution ainda não configurada.", 503);
        if (!Uri.TryCreate(Base, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new RuleException("Endereço da Evolution inválido.", 503);
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, Base + path);
        request.Headers.Add("apikey", Key);
        if (body != null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode) throw new RuleException($"Evolution respondeu com código {(int)response.StatusCode}. Confira instância e credenciais no servidor.", 502);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
        }
        catch (HttpRequestException) { throw new RuleException("Não foi possível acessar a Evolution.", 502); }
        catch (TaskCanceledException) { throw new RuleException("A Evolution não respondeu no prazo. Verifique a mensagem antes de repetir o envio.", 504); }
    }
    public async Task<object> Status() => !Configured ? new { configured = false, status = "Não configurada", provider = "Evolution API" } : new { configured = true, status = (object)await Call("/instance/connectionState/" + Uri.EscapeDataString(Instance)), provider = "Evolution API" };
    public Task<JsonElement> Chats() => Call("/chat/findChats/" + Uri.EscapeDataString(Instance), new { });
    public Task<JsonElement> Messages(string jid, int page = 1) => Call("/chat/findMessages/" + Uri.EscapeDataString(Instance), new { where = new { key = new { remoteJid = jid } }, page, offset = 50, sort = "desc" });
    public Task<JsonElement> Connect() => Call("/instance/connect/" + Uri.EscapeDataString(Instance));
    public Task<JsonElement> Send(string phone, string text) => Call("/message/sendText/" + Uri.EscapeDataString(Instance), new { number = phone, text });
}

using System.Net.Http.Json;
using System.Text.Json;

namespace DevBrain.Cli;

public class DevBrainHttpClient
{
    private readonly HttpClient _http;

    public const int DefaultPort = 37800;

    public DevBrainHttpClient(int port = DefaultPort)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task<T?> Get<T>(string path)
    {
        var response = await _http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    public async Task<JsonElement> GetJson(string path)
    {
        var response = await _http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    public async Task<HttpResponseMessage> Post(string path, object? body = null)
    {
        HttpContent? content = body is not null
            ? JsonContent.Create(body)
            : null;
        return await _http.PostAsync(path, content);
    }

    public async Task<bool> IsHealthy()
    {
        try
        {
            var response = await _http.GetAsync("/api/v1/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

namespace DevBrain.Llm;

using System.Net.Http.Json;
using System.Text.Json;
using DevBrain.Core.Models;

public class AnthropicClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    private bool _configured;

    public AnthropicClient(HttpClient http, string model = "claude-sonnet-4-6")
    {
        _http = http;
        _model = model;
    }

    public bool IsAvailable { get; private set; }

    public void Configure(string apiKey)
    {
        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);

        _http.DefaultRequestHeaders.Remove("anthropic-version");
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        _configured = true;
    }

    public Task CheckHealth()
    {
        IsAvailable = _configured;
        return Task.CompletedTask;
    }

    public async Task<LlmResult> Generate(LlmTask task, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                model = _model,
                max_tokens = 4096,
                messages = new[]
                {
                    new { role = "user", content = task.Prompt }
                }
            };

            var response = await _http.PostAsJsonAsync("https://api.anthropic.com/v1/messages", payload, ct);
            response.EnsureSuccessStatusCode();

            var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var content = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();

            return new LlmResult
            {
                TaskId = task.Id,
                Success = true,
                Content = content,
                Provider = "anthropic"
            };
        }
        catch (Exception ex)
        {
            return new LlmResult
            {
                TaskId = task.Id,
                Success = false,
                Error = ex.Message,
                Provider = "anthropic"
            };
        }
    }
}

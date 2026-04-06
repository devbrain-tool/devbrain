namespace DevBrain.Llm;

using System.Net.Http.Json;
using System.Text.Json;
using DevBrain.Core.Models;

public class AnthropicClient
{
    private readonly HttpClient _http;
    private readonly string _model;

    public AnthropicClient(HttpClient http, string apiKey, string model = "claude-sonnet-4-6")
    {
        _http = http;
        _http.BaseAddress ??= new Uri("https://api.anthropic.com");
        _model = model;

        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", apiKey);
            _http.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
    }

    public bool IsAvailable => _http.DefaultRequestHeaders.Contains("x-api-key");

    public Task CheckHealth()
    {
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

            var response = await _http.PostAsJsonAsync("/v1/messages", payload, ct);
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

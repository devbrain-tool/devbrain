namespace DevBrain.Llm;

using System.Net.Http.Json;
using System.Text.Json;
using DevBrain.Core.Models;

public class OllamaClient
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaClient(HttpClient http, string model = "llama3.2")
    {
        _http = http;
        _model = model;
    }

    public bool IsAvailable { get; private set; }

    public async Task CheckHealth()
    {
        try
        {
            var response = await _http.GetAsync("/api/tags");
            IsAvailable = response.IsSuccessStatusCode;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public async Task<LlmResult> Generate(LlmTask task, CancellationToken ct = default)
    {
        try
        {
            var payload = new { model = _model, prompt = task.Prompt, stream = false };
            var response = await _http.PostAsJsonAsync("/api/generate", payload, ct);
            response.EnsureSuccessStatusCode();

            var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var content = doc.RootElement.GetProperty("response").GetString();

            return new LlmResult
            {
                TaskId = task.Id,
                Success = true,
                Content = content,
                Provider = "ollama"
            };
        }
        catch (Exception ex)
        {
            return new LlmResult
            {
                TaskId = task.Id,
                Success = false,
                Error = ex.Message,
                Provider = "ollama"
            };
        }
    }

    public async Task<float[]> Embed(string text, CancellationToken ct = default)
    {
        try
        {
            var payload = new { model = "nomic-embed-text", input = text };
            var response = await _http.PostAsJsonAsync("/api/embed", payload, ct);
            response.EnsureSuccessStatusCode();

            var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var embeddings = doc.RootElement.GetProperty("embeddings");
            var first = embeddings[0];
            var result = new float[first.GetArrayLength()];
            int i = 0;
            foreach (var el in first.EnumerateArray())
            {
                result[i++] = el.GetSingle();
            }
            return result;
        }
        catch
        {
            return Array.Empty<float>();
        }
    }
}

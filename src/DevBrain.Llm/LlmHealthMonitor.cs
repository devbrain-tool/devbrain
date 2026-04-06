namespace DevBrain.Llm;

public class LlmHealthMonitor
{
    private readonly OllamaClient _ollama;
    private readonly AnthropicClient _anthropic;

    public LlmHealthMonitor(OllamaClient ollama, AnthropicClient anthropic)
    {
        _ollama = ollama;
        _anthropic = anthropic;
    }

    public bool IsLocalAvailable => _ollama.IsAvailable;
    public bool IsCloudAvailable => _anthropic.IsAvailable;

    public async Task CheckAll(CancellationToken ct = default)
    {
        await Task.WhenAll(
            _ollama.CheckHealth(),
            _anthropic.CheckHealth()
        );
    }
}

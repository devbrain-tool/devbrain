namespace DevBrain.Llm;

public class EmbeddingService
{
    private readonly OllamaClient? _ollama;

    public EmbeddingService(OllamaClient? ollama = null)
    {
        _ollama = ollama;
    }

    public async Task<float[]> Embed(string text, CancellationToken ct = default)
    {
        if (_ollama is { IsAvailable: true })
        {
            var result = await _ollama.Embed(text, ct);
            if (result.Length > 0)
                return result;
        }

        // Return zero vector as ONNX placeholder
        return new float[384];
    }
}

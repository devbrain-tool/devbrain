namespace DevBrain.Capture.Truncation;

using System.Text.Json;

public record TruncationResult(JsonElement Input, JsonElement Output);

public static class SmartTruncator
{
    private static readonly Dictionary<string, (int InputLimit, int OutputLimit)> Limits = new()
    {
        ["Bash"] = (4096, 8192),
        ["Read"] = (0, 2048),
        ["Grep"] = (1024, 2048),
        ["Glob"] = (1024, 2048),
        ["Edit"] = (4096, 0),
        ["Write"] = (0, 0),
        ["WebFetch"] = (1024, 4096),
        ["WebSearch"] = (1024, 4096),
        ["Agent"] = (2048, 4096),
    };

    private const int DefaultInputLimit = 2048;
    private const int DefaultOutputLimit = 4096;

    public static TruncationResult Truncate(string toolName, JsonElement input, JsonElement output)
    {
        var (inputLimit, outputLimit) = Limits.GetValueOrDefault(toolName, (DefaultInputLimit, DefaultOutputLimit));

        var truncatedInput = TruncateElement(toolName, input, inputLimit, isInput: true);
        var truncatedOutput = TruncateElement(toolName, output, outputLimit, isInput: false);

        return new TruncationResult(truncatedInput, truncatedOutput);
    }

    private static JsonElement TruncateElement(string toolName, JsonElement element, int limit, bool isInput)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
            return element;

        if (isInput && limit == 0 && (toolName is "Write" or "Read"))
            return KeepOnlyFilePath(element);

        if (!isInput && limit == 0)
            return default;

        return TruncateStringProperties(element, limit);
    }

    private static JsonElement KeepOnlyFilePath(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return element;

        var dict = new Dictionary<string, object?>();
        if (element.TryGetProperty("file_path", out var fp))
            dict["file_path"] = fp.GetString();
        if (element.TryGetProperty("path", out var p))
            dict["path"] = p.GetString();

        return JsonDocument.Parse(JsonSerializer.Serialize(dict)).RootElement;
    }

    private static JsonElement TruncateStringProperties(JsonElement element, int limit)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return element;

        var dict = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var str = prop.Value.GetString() ?? "";
                if (str.Length > limit)
                    dict[prop.Name] = str[..limit] + $" [truncated at {limit / 1024}KB]";
                else
                    dict[prop.Name] = str;
            }
            else
            {
                dict[prop.Name] = prop.Value;
            }
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(dict)).RootElement;
    }
}

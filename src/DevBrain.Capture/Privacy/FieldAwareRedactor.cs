namespace DevBrain.Capture.Privacy;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public class FieldAwareRedactor
{
    private static readonly string[] SensitiveFileSuffixes =
        [".env", "secret", "credential", "password", ".pem", ".key"];

    private static readonly Regex ExportPattern =
        new(@"(?:export|set)\s+\w+=\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Redact(string? toolName, string metadataJson)
    {
        if (toolName is null || metadataJson is "{}" or "")
            return metadataJson;

        try
        {
            var node = JsonNode.Parse(metadataJson);
            if (node is null) return metadataJson;

            var toolInput = node["tool_input"];
            if (toolInput is null) return metadataJson;

            var filePath = toolInput["file_path"]?.GetValue<string>();

            switch (toolName)
            {
                case "Write" when filePath is not null && IsSensitiveFile(filePath):
                    toolInput["content"] = "[REDACTED:sensitive-file]";
                    break;

                case "Edit" when filePath is not null && IsSensitiveFile(filePath):
                    if (toolInput["old_string"] is not null)
                        toolInput["old_string"] = "[REDACTED:sensitive-file]";
                    if (toolInput["new_string"] is not null)
                        toolInput["new_string"] = "[REDACTED:sensitive-file]";
                    break;

                case "Bash":
                    var command = toolInput["command"]?.GetValue<string>();
                    if (command is not null && ExportPattern.IsMatch(command))
                    {
                        var redacted = Regex.Replace(command,
                            @"((?:export|set)\s+\w+=)\S+",
                            "$1[REDACTED:secret]",
                            RegexOptions.IgnoreCase);
                        toolInput["command"] = redacted;
                    }
                    break;
            }

            return node.ToJsonString();
        }
        catch
        {
            return metadataJson;
        }
    }

    private static bool IsSensitiveFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        foreach (var suffix in SensitiveFileSuffixes)
        {
            if (fileName.Contains(suffix))
                return true;
        }
        return false;
    }
}

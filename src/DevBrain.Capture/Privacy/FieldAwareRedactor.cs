namespace DevBrain.Capture.Privacy;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public class FieldAwareRedactor
{
    private static readonly string[] SensitiveFileSuffixes =
        [".env", "secret", "credential", "password", ".pem", ".key"];

    private static readonly Regex EnvAssignPattern =
        new(@"(?:export|set)\s+\w+=\S+|^\w+=\S+\s+\w|docker\s+run\s+.*-e\s+\w+=\S+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public string Redact(string? toolName, string metadataJson)
    {
        if (toolName is null || metadataJson is "{}" or "")
            return metadataJson;

        try
        {
            var node = JsonNode.Parse(metadataJson);
            if (node is null) return metadataJson;

            var toolInput = node["tool_input"];
            var toolOutput = node["tool_output"];
            var filePath = toolInput?["file_path"]?.GetValue<string>()
                        ?? toolInput?["path"]?.GetValue<string>();

            switch (toolName)
            {
                case "Write" when filePath is not null && IsSensitiveFile(filePath):
                    if (toolInput?["content"] is not null)
                        toolInput["content"] = "[REDACTED:sensitive-file]";
                    break;

                case "Edit" when filePath is not null && IsSensitiveFile(filePath):
                    if (toolInput?["old_string"] is not null)
                        toolInput["old_string"] = "[REDACTED:sensitive-file]";
                    if (toolInput?["new_string"] is not null)
                        toolInput["new_string"] = "[REDACTED:sensitive-file]";
                    break;

                case "Read" when filePath is not null && IsSensitiveFile(filePath):
                    // Redact the output — it contains the file contents
                    if (toolOutput is not null)
                        node["tool_output"] = "[REDACTED:sensitive-file]";
                    break;

                case "Grep" when filePath is not null && IsSensitiveFile(filePath):
                    if (toolOutput is not null)
                        node["tool_output"] = "[REDACTED:sensitive-file]";
                    break;

                case "Bash":
                    var command = toolInput?["command"]?.GetValue<string>();
                    if (command is not null && EnvAssignPattern.IsMatch(command))
                    {
                        var redacted = Regex.Replace(command,
                            @"((?:export|set|docker\s+run\s+.*-e)\s+\w+=)\S+",
                            "$1[REDACTED:secret]",
                            RegexOptions.IgnoreCase);
                        // Also catch inline VAR=value command
                        redacted = Regex.Replace(redacted,
                            @"^(\w+=)\S+(\s+\w)",
                            "$1[REDACTED:secret]$2",
                            RegexOptions.Multiline);
                        toolInput!["command"] = redacted;
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

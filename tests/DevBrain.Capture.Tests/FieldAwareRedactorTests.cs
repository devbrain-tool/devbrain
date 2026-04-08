namespace DevBrain.Capture.Tests;

using System.Text.Json;
using DevBrain.Capture.Privacy;

public class FieldAwareRedactorTests
{
    private readonly FieldAwareRedactor _redactor = new();

    [Fact]
    public void Write_EnvFile_RedactsContent()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            tool_input = new { file_path = "/project/.env", content = "DB_PASSWORD=hunter2" }
        });

        var result = _redactor.Redact("Write", metadata);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("[REDACTED:sensitive-file]",
            doc.RootElement.GetProperty("tool_input").GetProperty("content").GetString());
    }

    [Fact]
    public void Write_NormalFile_PreservesContent()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            tool_input = new { file_path = "/project/src/main.ts", content = "console.log('hello')" }
        });

        var result = _redactor.Redact("Write", metadata);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("console.log('hello')",
            doc.RootElement.GetProperty("tool_input").GetProperty("content").GetString());
    }

    [Fact]
    public void Bash_ExportCommand_RedactsValues()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            tool_input = new { command = "export API_KEY=myverysecretvalue123" }
        });

        var result = _redactor.Redact("Bash", metadata);

        Assert.Contains("[REDACTED:secret]", result);
        Assert.DoesNotContain("myverysecretvalue123", result);
    }

    [Fact]
    public void Edit_CredentialFile_RedactsStrings()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            tool_input = new
            {
                file_path = "/project/credentials.json",
                old_string = "old-secret",
                new_string = "new-secret"
            }
        });

        var result = _redactor.Redact("Edit", metadata);
        var doc = JsonDocument.Parse(result);
        var input = doc.RootElement.GetProperty("tool_input");

        Assert.Equal("[REDACTED:sensitive-file]", input.GetProperty("old_string").GetString());
        Assert.Equal("[REDACTED:sensitive-file]", input.GetProperty("new_string").GetString());
    }

    [Fact]
    public void NonToolEvent_PassesThrough()
    {
        var metadata = JsonSerializer.Serialize(new { prompt = "Fix the auth bug" });

        var result = _redactor.Redact(null, metadata);

        Assert.Equal(metadata, result);
    }
}

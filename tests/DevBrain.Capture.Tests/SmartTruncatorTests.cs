namespace DevBrain.Capture.Tests;

using System.Text.Json;
using DevBrain.Capture.Truncation;

public class SmartTruncatorTests
{
    [Fact]
    public void Bash_TruncatesOutput_To8KB()
    {
        var largeOutput = new string('x', 16_000);
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new { command = "npm test" })).RootElement;
        var output = JsonDocument.Parse(JsonSerializer.Serialize(new { stdout = largeOutput, exit_code = 0 })).RootElement;

        var result = SmartTruncator.Truncate("Bash", input, output);

        var stdout = result.Output.GetProperty("stdout").GetString()!;
        Assert.True(stdout.Length <= 8192 + 30);
        Assert.Contains("[truncated at 8KB]", stdout);
    }

    [Fact]
    public void Bash_PreservesInput_Under4KB()
    {
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new { command = "ls -la" })).RootElement;
        var output = JsonDocument.Parse(JsonSerializer.Serialize(new { stdout = "file.txt", exit_code = 0 })).RootElement;

        var result = SmartTruncator.Truncate("Bash", input, output);

        Assert.Equal("ls -la", result.Input.GetProperty("command").GetString());
    }

    [Fact]
    public void Write_SkipsContent_KeepsFilePath()
    {
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            file_path = "/src/main.ts",
            content = new string('x', 50_000)
        })).RootElement;

        var result = SmartTruncator.Truncate("Write", input, default);

        Assert.Equal("/src/main.ts", result.Input.GetProperty("file_path").GetString());
        Assert.False(result.Input.TryGetProperty("content", out _));
    }

    [Fact]
    public void Edit_TruncatesStrings_To4KB()
    {
        var largeString = new string('y', 10_000);
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            file_path = "/src/main.ts",
            old_string = largeString,
            new_string = largeString
        })).RootElement;

        var result = SmartTruncator.Truncate("Edit", input, default);

        var oldStr = result.Input.GetProperty("old_string").GetString()!;
        Assert.True(oldStr.Length <= 4096 + 30);
        Assert.Contains("[truncated at 4KB]", oldStr);
    }

    [Fact]
    public void Read_KeepsOnlyFilePath()
    {
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            file_path = "/src/main.ts",
            offset = 10,
            limit = 50
        })).RootElement;

        var result = SmartTruncator.Truncate("Read", input, default);

        Assert.Equal("/src/main.ts", result.Input.GetProperty("file_path").GetString());
    }

    [Fact]
    public void SmallPayload_NotTruncated()
    {
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new { command = "echo hi" })).RootElement;
        var output = JsonDocument.Parse(JsonSerializer.Serialize(new { stdout = "hi", exit_code = 0 })).RootElement;

        var result = SmartTruncator.Truncate("Bash", input, output);

        Assert.Equal("echo hi", result.Input.GetProperty("command").GetString());
        Assert.Equal("hi", result.Output.GetProperty("stdout").GetString());
    }

    [Fact]
    public void UnknownTool_UsesDefaults()
    {
        var largeOutput = new string('b', 8000);
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new { data = "small" })).RootElement;
        var output = JsonDocument.Parse(JsonSerializer.Serialize(new { result = largeOutput })).RootElement;

        var result = SmartTruncator.Truncate("mcp__custom__tool", input, output);

        var resultStr = result.Output.GetProperty("result").GetString()!;
        Assert.True(resultStr.Length <= 4096 + 30);
    }
}

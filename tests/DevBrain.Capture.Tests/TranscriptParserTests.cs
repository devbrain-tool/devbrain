namespace DevBrain.Capture.Tests;

using DevBrain.Capture.Transcript;

public class TranscriptParserTests
{
    [Fact]
    public void ParseLastTurn_ExtractsTokenMetrics()
    {
        var jsonl = """
        {"type":"user","content":"hello"}
        {"type":"assistant","content":"hi","usage":{"input_tokens":100,"output_tokens":50,"cache_read_input_tokens":80,"cache_creation_input_tokens":20},"model":"claude-sonnet-4-6","latency_ms":1200}
        """;

        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, jsonl);

        try
        {
            var result = TranscriptParser.ParseLastTurn(tmpFile);

            Assert.NotNull(result);
            Assert.Equal(100, result!.TokensIn);
            Assert.Equal(50, result.TokensOut);
            Assert.Equal(80, result.CacheReadTokens);
            Assert.Equal(20, result.CacheWriteTokens);
            Assert.Equal(1200, result.LatencyMs);
            Assert.Equal("claude-sonnet-4-6", result.Model);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ParseLastTurn_ReturnsNull_ForEmptyFile()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var result = TranscriptParser.ParseLastTurn(tmpFile);
            Assert.Null(result);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ParseSessionAggregates_ComputesTotals()
    {
        var jsonl = """
        {"type":"assistant","usage":{"input_tokens":100,"output_tokens":50},"model":"claude-sonnet-4-6","tool_use":{"name":"Bash"}}
        {"type":"assistant","usage":{"input_tokens":200,"output_tokens":100},"model":"claude-sonnet-4-6","tool_use":{"name":"Edit"}}
        {"type":"assistant","usage":{"input_tokens":150,"output_tokens":75},"model":"claude-sonnet-4-6","tool_use":{"name":"Bash"}}
        """;

        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, jsonl);

        try
        {
            var result = TranscriptParser.ParseSessionAggregates(tmpFile);

            Assert.Equal(450, result.TotalTokensIn);
            Assert.Equal(225, result.TotalTokensOut);
            Assert.Equal(3, result.TotalTurns);
            Assert.Contains("claude-sonnet-4-6", result.ModelsUsed);
            Assert.Equal(2, result.ToolUsage["Bash"]);
            Assert.Equal(1, result.ToolUsage["Edit"]);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ParseLastTurn_ReturnsNull_ForNonexistentFile()
    {
        var result = TranscriptParser.ParseLastTurn("/nonexistent/file.jsonl");
        Assert.Null(result);
    }
}

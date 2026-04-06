using DevBrain.Core.Enums;
using DevBrain.Core.Models;
using DevBrain.Llm;

namespace DevBrain.Llm.Tests;

public class LlmTaskQueueTests
{
    private static LlmTask MakeTask(LlmPreference pref) => new()
    {
        AgentName = "test",
        Priority = Priority.Normal,
        Type = LlmTaskType.Summarization,
        Prompt = "hello",
        Preference = pref
    };

    private static Task<LlmResult> LocalSuccess(LlmTask t) =>
        Task.FromResult(new LlmResult { TaskId = t.Id, Success = true, Content = "local", Provider = "ollama" });

    private static Task<LlmResult> CloudSuccess(LlmTask t) =>
        Task.FromResult(new LlmResult { TaskId = t.Id, Success = true, Content = "cloud", Provider = "anthropic" });

    [Fact]
    public async Task PreferLocal_RoutesToLocal_WhenAvailable()
    {
        var queue = new LlmTaskQueue(LocalSuccess, CloudSuccess, () => true, () => true, 100);
        var task = MakeTask(LlmPreference.PreferLocal);

        var result = await queue.Submit(task);

        Assert.True(result.Success);
        Assert.Equal("local", result.Content);
    }

    [Fact]
    public async Task PreferLocal_FallsBackToCloud_WhenLocalUnavailable()
    {
        var queue = new LlmTaskQueue(LocalSuccess, CloudSuccess, () => false, () => true, 100);
        var task = MakeTask(LlmPreference.PreferLocal);

        var result = await queue.Submit(task);

        Assert.True(result.Success);
        Assert.Equal("cloud", result.Content);
    }

    [Fact]
    public async Task Cloud_RejectsWhenQuotaExceeded()
    {
        var queue = new LlmTaskQueue(LocalSuccess, CloudSuccess, () => true, () => true, 0);
        var task = MakeTask(LlmPreference.Cloud);

        var result = await queue.Submit(task);

        Assert.False(result.Success);
        Assert.Contains("quota exceeded", result.Error);
    }

    [Fact]
    public async Task Local_DoesNotFallbackToCloud()
    {
        var queue = new LlmTaskQueue(
            _ => Task.FromResult(new LlmResult { TaskId = "x", Success = false, Error = "fail" }),
            CloudSuccess,
            () => false,
            () => true,
            100);
        var task = MakeTask(LlmPreference.Local);

        var result = await queue.Submit(task);

        Assert.False(result.Success);
        Assert.NotEqual("cloud", result.Content);
    }
}

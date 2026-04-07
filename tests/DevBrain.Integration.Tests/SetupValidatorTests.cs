namespace DevBrain.Integration.Tests;

using DevBrain.Api.Setup;
using DevBrain.Core.Models;

public class SetupValidatorTests
{
    [Fact]
    public async Task RunAllChecks_ReturnsAllNineChecks()
    {
        var settings = new Settings { Daemon = new DaemonSettings { Port = 37800 } };
        var validator = new SetupValidator(settings);

        var status = await validator.RunAllChecks();

        Assert.Equal(9, status.Checks.Count);
        var ids = status.Checks.Select(c => c.Id).ToList();
        Assert.Contains("claude-cli", ids);
        Assert.Contains("claude-settings", ids);
        Assert.Contains("claude-hook", ids);
        Assert.Contains("claude-roundtrip", ids);
        Assert.Contains("gh-cli", ids);
        Assert.Contains("gh-copilot", ids);
        Assert.Contains("copilot-wrappers", ids);
        Assert.Contains("copilot-roundtrip", ids);
        Assert.Contains("ollama", ids);
    }

    [Fact]
    public async Task RunAllChecks_SummaryCounts()
    {
        var settings = new Settings { Daemon = new DaemonSettings { Port = 37800 } };
        var validator = new SetupValidator(settings);

        var status = await validator.RunAllChecks();

        var summary = status.Summary;
        Assert.Equal(status.Checks.Count,
            summary.Pass + summary.Fail + summary.Warn + summary.Skip);
    }

    [Fact]
    public async Task RunAllChecks_SkipsDependentsOnFailure()
    {
        var settings = new Settings { Daemon = new DaemonSettings { Port = 99999 } };
        var validator = new SetupValidator(settings);

        var status = await validator.RunAllChecks();

        var roundtrip = status.Checks.First(c => c.Id == "claude-roundtrip");
        Assert.True(roundtrip.Status == "skip" || roundtrip.Status == "fail");
    }

    [Fact]
    public async Task Fix_ReturnsFailureForNonFixableCheck()
    {
        var settings = new Settings { Daemon = new DaemonSettings { Port = 37800 } };
        var validator = new SetupValidator(settings);

        var result = await validator.Fix("claude-cli");

        Assert.False(result.Success);
        Assert.Contains("not auto-fixable", result.Detail);
    }

    [Fact]
    public async Task Fix_ReturnsFailureForUnknownCheck()
    {
        var settings = new Settings { Daemon = new DaemonSettings { Port = 37800 } };
        var validator = new SetupValidator(settings);

        var result = await validator.Fix("nonexistent");

        Assert.False(result.Success);
        Assert.Contains("Unknown", result.Detail);
    }
}

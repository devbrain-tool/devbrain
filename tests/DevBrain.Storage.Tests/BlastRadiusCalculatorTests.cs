using DevBrain.Core.Enums;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Storage.Tests;

public class BlastRadiusCalculatorTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteObservationStore _obsStore = null!;
    private SqliteGraphStore _graphStore = null!;
    private SqliteDeadEndStore _deadEndStore = null!;
    private BlastRadiusCalculator _calculator = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _obsStore = new SqliteObservationStore(_connection);
        _graphStore = new SqliteGraphStore(_connection);
        _deadEndStore = new SqliteDeadEndStore(_connection);
        var chainBuilder = new DecisionChainBuilder(_graphStore, _obsStore);
        _calculator = new BlastRadiusCalculator(_graphStore, _deadEndStore, chainBuilder);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Calculate_FindsAffectedFiles()
    {
        // Source file -> decision -> references another file
        var sourceFile = await _graphStore.AddNode("File", "src/Auth.cs");
        var decision = await _graphStore.AddNode("Decision", "Add JWT auth");
        var affectedFile = await _graphStore.AddNode("File", "src/Config.cs");

        await _graphStore.AddEdge(decision.Id, sourceFile.Id, "references");
        await _graphStore.AddEdge(decision.Id, affectedFile.Id, "references");

        var result = await _calculator.Calculate("src/Auth.cs");

        Assert.Equal("src/Auth.cs", result.SourceFile);
        Assert.Single(result.AffectedFiles);
        Assert.Equal("src/Config.cs", result.AffectedFiles[0].FilePath);
        Assert.True(result.AffectedFiles[0].RiskScore > 0);
    }

    [Fact]
    public async Task Calculate_ExcludesSourceFileFromAffected()
    {
        var sourceFile = await _graphStore.AddNode("File", "src/App.cs");
        var decision = await _graphStore.AddNode("Decision", "Refactor");

        await _graphStore.AddEdge(decision.Id, sourceFile.Id, "references");

        var result = await _calculator.Calculate("src/App.cs");

        Assert.Empty(result.AffectedFiles);
    }

    [Fact]
    public async Task Calculate_IncludesDeadEndsAtRisk()
    {
        var sourceFile = await _graphStore.AddNode("File", "src/Search.cs");
        var decision = await _graphStore.AddNode("Decision", "Use FTS");

        await _graphStore.AddEdge(decision.Id, sourceFile.Id, "references");

        await _deadEndStore.Add(new DeadEnd
        {
            Id = "de-1", Project = "proj",
            Description = "FTS tokenizer issue",
            Approach = "Default tokenizer", Reason = "No CJK support",
            FilesInvolved = ["src/Search.cs"],
            DetectedAt = DateTime.UtcNow.AddDays(-5)
        });

        var result = await _calculator.Calculate("src/Search.cs");

        Assert.Single(result.DeadEndsAtRisk);
        Assert.Equal("de-1", result.DeadEndsAtRisk[0]);
    }

    [Fact]
    public async Task Calculate_ReturnsEmptyForUnknownFile()
    {
        var result = await _calculator.Calculate("src/Unknown.cs");

        Assert.Equal("src/Unknown.cs", result.SourceFile);
        Assert.Empty(result.AffectedFiles);
        Assert.Empty(result.DeadEndsAtRisk);
    }

    [Fact]
    public async Task Calculate_FollsCausalChainToFindDistantFiles()
    {
        // src/A.cs -> dec1 --caused_by--> dec2 -> src/B.cs
        var fileA = await _graphStore.AddNode("File", "src/A.cs");
        var fileB = await _graphStore.AddNode("File", "src/B.cs");
        var dec1 = await _graphStore.AddNode("Decision", "Decision about A");
        var dec2 = await _graphStore.AddNode("Decision", "Decision about B");

        await _graphStore.AddEdge(dec1.Id, fileA.Id, "references");
        await _graphStore.AddEdge(dec2.Id, fileB.Id, "references");
        await _graphStore.AddEdge(dec2.Id, dec1.Id, "caused_by");

        var result = await _calculator.Calculate("src/A.cs");

        Assert.Single(result.AffectedFiles);
        Assert.Equal("src/B.cs", result.AffectedFiles[0].FilePath);
    }

    [Fact]
    public void ComputeRiskScore_ShortChainHigherRisk()
    {
        var shortChain = BlastRadiusCalculator.ComputeRiskScore(1, 0, 1.0);
        var longChain = BlastRadiusCalculator.ComputeRiskScore(5, 0, 1.0);

        Assert.True(shortChain > longChain);
    }

    [Fact]
    public void ComputeRiskScore_DeadEndsAmplifyRisk()
    {
        var noDeadEnds = BlastRadiusCalculator.ComputeRiskScore(2, 0, 1.0);
        var withDeadEnds = BlastRadiusCalculator.ComputeRiskScore(2, 3, 1.0);

        Assert.True(withDeadEnds > noDeadEnds);
    }

    [Fact]
    public void ComputeRiskScore_RecencyDecayReducesRisk()
    {
        var recent = BlastRadiusCalculator.ComputeRiskScore(2, 0, 1.0);
        var stale = BlastRadiusCalculator.ComputeRiskScore(2, 0, 0.2);

        Assert.True(recent > stale);
    }
}

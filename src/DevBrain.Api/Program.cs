using DevBrain.Agents;
using DevBrain.Api;
using DevBrain.Api.Endpoints;
using DevBrain.Capture;
using DevBrain.Capture.Pipeline;
using DevBrain.Core;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Llm;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

// ── Load settings ────────────────────────────────────────────────────────────
var settingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".devbrain", "settings.toml");
var settings = SettingsLoader.LoadFromFile(settingsPath);

var dataPath = SettingsLoader.ResolveDataPath(settings.Daemon.DataPath);
Directory.CreateDirectory(dataPath);

// ── SQLite ────────────────────────────────────────────────────────────────────
var dbPath = Path.Combine(dataPath, "devbrain.db");
// SQLite in WAL mode (set by SchemaManager) with Cache=Shared allows concurrent reads
// and serialized writes. Microsoft.Data.Sqlite handles locking internally.
var connStr = $"Data Source={dbPath};Cache=Shared";
var connection = new SqliteConnection(connStr);
connection.Open();
SchemaManager.Initialize(connection);

var observationStore = new SqliteObservationStore(connection);
var graphStore = new SqliteGraphStore(connection);

// ── Vector store (placeholder) ───────────────────────────────────────────────
var vectorStore = new NullVectorStore();

// ── LLM clients ──────────────────────────────────────────────────────────────
var ollamaHttp = new HttpClient
{
    BaseAddress = new Uri(settings.Llm.Local.Endpoint),
    Timeout = TimeSpan.FromSeconds(120)
};
var ollama = new OllamaClient(ollamaHttp, settings.Llm.Local.Model);

var anthropicApiKey = Environment.GetEnvironmentVariable(settings.Llm.Cloud.ApiKeyEnv) ?? "";
var anthropicHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
var anthropic = new AnthropicClient(anthropicHttp, anthropicApiKey, settings.Llm.Cloud.Model);

var healthMonitor = new LlmHealthMonitor(ollama, anthropic);
var embeddingService = new EmbeddingService(ollama);

var llmService = new LlmTaskQueue(
    localHandler: task => ollama.Generate(task),
    cloudHandler: task => anthropic.Generate(task),
    isLocalAvailable: () => healthMonitor.IsLocalAvailable,
    isCloudAvailable: () => healthMonitor.IsCloudAvailable,
    maxDailyCloudRequests: settings.Llm.Cloud.MaxDailyRequests,
    embedHandler: (text, ct) => embeddingService.Embed(text, ct));

// ── Event bus & capture pipeline ─────────────────────────────────────────────
var eventBus = new EventBus();
var threadResolver = new ThreadResolver(TimeSpan.FromHours(settings.Capture.ThreadGapHours));
var normalizer = new Normalizer();
var enricher = new Enricher(threadResolver);
var tagger = new Tagger(llmService);
var privacyFilter = new PrivacyFilter();
var writer = new Writer(observationStore, vectorStore, obs => eventBus.Publish(obs));
var pipeline = new PipelineOrchestrator(normalizer, enricher, tagger, privacyFilter, writer);

// ── Agents ───────────────────────────────────────────────────────────────────
var agentContext = new AgentContext(observationStore, graphStore, vectorStore, llmService, settings);
var agents = new IIntelligenceAgent[]
{
    new LinkerAgent(),
    new DeadEndAgent(),
    new BriefingAgent(),
    new CompressionAgent()
};

// ── ASP.NET Core host ────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<IObservationStore>(observationStore);
builder.Services.AddSingleton<IGraphStore>(graphStore);
builder.Services.AddSingleton<IVectorStore>(vectorStore);
builder.Services.AddSingleton<ILlmService>(llmService);
builder.Services.AddSingleton(llmService); // concrete type for ResetDailyCounter
builder.Services.AddSingleton(eventBus);
builder.Services.AddSingleton(agentContext);
builder.Services.AddSingleton(healthMonitor);
builder.Services.AddSingleton(connection); // for raw SQL queries in endpoints

foreach (var agent in agents)
{
    builder.Services.AddSingleton<IIntelligenceAgent>(agent);
}

builder.Services.AddSingleton<AgentScheduler>(sp =>
{
    var registeredAgents = sp.GetServices<IIntelligenceAgent>();
    var logger = sp.GetRequiredService<ILogger<AgentScheduler>>();
    return new AgentScheduler(registeredAgents, agentContext, eventBus, logger);
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentScheduler>());

builder.WebHost.UseUrls($"http://127.0.0.1:{settings.Daemon.Port}");

var app = builder.Build();

// ── Static files for dashboard ───────────────────────────────────────────────
app.UseStaticFiles();

// ── Map API endpoints ────────────────────────────────────────────────────────
app.MapHealthEndpoints();
app.MapObservationEndpoints();
app.MapSearchEndpoints();
app.MapBriefingEndpoints();
app.MapGraphEndpoints();
app.MapAgentEndpoints();
app.MapSettingsEndpoints();
app.MapAdminEndpoints();
app.MapThreadEndpoints();
app.MapDeadEndEndpoints();
app.MapContextEndpoints();

// Dashboard SPA fallback
app.MapFallbackToFile("index.html");

// ── Lifecycle: PID file, pipeline start/stop ─────────────────────────────────
var pidFilePath = Path.Combine(dataPath, "daemon.pid");
var cts = new CancellationTokenSource();

// Start the capture pipeline before registering lifecycle hooks so closures can capture the variables
var (pipelineInput, pipelineTask) = pipeline.Start(cts.Token);

app.Lifetime.ApplicationStarted.Register(() =>
{
    File.WriteAllText(pidFilePath, Environment.ProcessId.ToString());

    // Initial LLM health check
    _ = healthMonitor.CheckAll();

    // I7: Recurring LLM health check every 30 seconds
    _ = Task.Run(async () =>
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!cts.Token.IsCancellationRequested)
        {
            try { await timer.WaitForNextTickAsync(cts.Token); }
            catch (OperationCanceledException) { break; }

            try { await healthMonitor.CheckAll(cts.Token); }
            catch { /* health check failure is non-fatal */ }
        }
    });

    // O2: Reset daily cloud LLM counter at midnight
    _ = Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextMidnight = now.Date.AddDays(1);
            var delay = nextMidnight - now;

            try { await Task.Delay(delay, cts.Token); }
            catch (OperationCanceledException) { break; }

            llmService.ResetDailyCounter();
        }
    });
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    // C1: Signal pipeline to drain first, then cancel, then close connections
    pipelineInput.Complete();
    pipelineTask.Wait(TimeSpan.FromSeconds(5));

    cts.Cancel();

    if (File.Exists(pidFilePath))
        File.Delete(pidFilePath);

    connection.Close();
    connection.Dispose();
    ollamaHttp.Dispose();
    anthropicHttp.Dispose();
});

app.Run();

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
var connection = new SqliteConnection($"Data Source={dbPath}");
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
builder.Services.AddSingleton(eventBus);
builder.Services.AddSingleton(agentContext);
builder.Services.AddSingleton(healthMonitor);

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

// Dashboard SPA fallback
app.MapFallbackToFile("index.html");

// ── Lifecycle: PID file, pipeline start/stop ─────────────────────────────────
var pidFilePath = Path.Combine(dataPath, "daemon.pid");
var cts = new CancellationTokenSource();

app.Lifetime.ApplicationStarted.Register(() =>
{
    File.WriteAllText(pidFilePath, Environment.ProcessId.ToString());

    // Initial LLM health check
    _ = healthMonitor.CheckAll();
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    cts.Cancel();

    if (File.Exists(pidFilePath))
        File.Delete(pidFilePath);

    connection.Close();
    connection.Dispose();
    ollamaHttp.Dispose();
    anthropicHttp.Dispose();
});

// Start the capture pipeline
var (pipelineInput, pipelineTask) = pipeline.Start(cts.Token);

app.Run();

namespace DevBrain.Agents;

using System.Collections.Concurrent;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class AgentScheduler : BackgroundService
{
    private readonly IReadOnlyList<IIntelligenceAgent> _agents;
    private readonly AgentContext _ctx;
    private readonly EventBus _eventBus;
    private readonly ILogger<AgentScheduler> _logger;
    private readonly ConcurrentQueue<Observation> _eventBuffer = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastRunTimes = new();
    private readonly SemaphoreSlim _concurrency = new(3);
    private DateTime _lastObservationTime = DateTime.UtcNow;

    public AgentScheduler(
        IEnumerable<IIntelligenceAgent> agents,
        AgentContext ctx,
        EventBus eventBus,
        ILogger<AgentScheduler> logger)
    {
        _agents = agents.ToList();
        _ctx = ctx;
        _eventBus = eventBus;
        _logger = logger;

        _eventBus.Subscribe(obs =>
        {
            _eventBuffer.Enqueue(obs);
            _lastObservationTime = DateTime.UtcNow;
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAgents(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in agent scheduler loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task DispatchAgents(CancellationToken ct)
    {
        var tasks = new List<Task>();

        // Drain event buffer
        var hasEvents = !_eventBuffer.IsEmpty;

        foreach (var agent in _agents)
        {
            var shouldRun = agent.Schedule switch
            {
                AgentSchedule.OnEvent onEvent => hasEvents,
                AgentSchedule.Cron cron => IsCronDue(agent.Name),
                AgentSchedule.Idle idle => IsIdle(idle.After),
                _ => false
            };

            if (!shouldRun)
                continue;

            tasks.Add(RunAgentWithThrottle(agent, ct));
        }

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);

        // Clear the buffer after event agents have run
        if (hasEvents)
        {
            while (_eventBuffer.TryDequeue(out _)) { }
        }
    }

    private bool IsCronDue(string agentName)
    {
        if (_lastRunTimes.TryGetValue(agentName, out var lastRun))
        {
            return (DateTime.UtcNow - lastRun).TotalHours >= 1;
        }
        return true;
    }

    private bool IsIdle(TimeSpan after)
    {
        return (DateTime.UtcNow - _lastObservationTime) >= after;
    }

    private async Task RunAgentWithThrottle(IIntelligenceAgent agent, CancellationToken ct)
    {
        await _concurrency.WaitAsync(ct);
        try
        {
            _logger.LogInformation("Starting agent {AgentName}", agent.Name);

            var results = await agent.Run(_ctx, ct);

            _lastRunTimes[agent.Name] = DateTime.UtcNow;

            _logger.LogInformation("Agent {AgentName} completed with {Count} outputs",
                agent.Name, results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent {AgentName} failed", agent.Name);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    public IReadOnlyDictionary<string, DateTime> GetLastRunTimes()
    {
        return new Dictionary<string, DateTime>(_lastRunTimes);
    }
}

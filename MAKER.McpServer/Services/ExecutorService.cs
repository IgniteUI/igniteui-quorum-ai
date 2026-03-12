using System.Text.Json;
using System.Threading.Channels;
using MAKER.AI.Models;
using MAKER.Configuration;

namespace MAKER.McpServer.Services;

public record SseEvent(string Type, string Data);

public class ExecutorService(IConfiguration configuration)
{
    private List<MCPServerInfo>? _mcpServers;
    private string? _format;
    private readonly object _lock = new();

    public List<MCPServerInfo> GetMcpServers()
    {
        lock (_lock)
        {
            _mcpServers ??= BuildConfig().McpServers;
            return [.. _mcpServers];
        }
    }

    public void AddMcpServer(MCPServerInfo server)
    {
        lock (_lock)
        {
            _mcpServers ??= BuildConfig().McpServers;
            _mcpServers.Add(server);
        }
    }

    public bool RemoveMcpServer(string name)
    {
        lock (_lock)
        {
            _mcpServers ??= BuildConfig().McpServers;
            return _mcpServers.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }

    public Executor Create()
    {
        var config = BuildConfig();
        lock (_lock)
        {
            if (_mcpServers != null)
                config.McpServers = [.. _mcpServers];
        }
        return new(config, GetFormat());
    }

    public Executor CreateWithSseEvents(ChannelWriter<SseEvent> writer)
    {
        var executor = Create();
        WireEvents(executor, evt => writer.TryWrite(evt));
        return executor;
    }

    public Executor CreateWithProgress(IProgress<string>? progress)
    {
        var executor = Create();
        if (progress != null)
            WireEvents(executor, evt => progress.Report(JsonSerializer.Serialize(evt)));
        return executor;
    }

    public string GetConfiguredFormat()
    {
        lock (_lock)
        {
            return _format ?? GetDefaultFormat();
        }
    }

    public void SetFormat(string format)
    {
        lock (_lock)
        {
            _format = string.IsNullOrWhiteSpace(format) ? null : format.Trim();
        }
    }

    private string GetFormat()
    {
        lock (_lock)
        {
            if (_format != null) return _format;
        }
        return GetDefaultFormat();
    }

    private string GetDefaultFormat()
    {
        var value = configuration.GetValue<string>("Executor:Format")?.Trim();
        return string.IsNullOrWhiteSpace(value) ? "plaintext" : value;
    }

    private static void WireEvents(Executor executor, Action<SseEvent> emit)
    {
        executor.OnStepsProposed += steps =>
            emit(new SseEvent("steps_proposed", JsonSerializer.Serialize(steps)));

        executor.OnStepsAdded += (proposed, all) =>
            emit(new SseEvent("steps_added", JsonSerializer.Serialize(new { proposed, all })));

        executor.OnStepsRejected += ex =>
            emit(new SseEvent("steps_rejected", JsonSerializer.Serialize(new { reasons = ex.RejectionReasons })));

        executor.OnPlanVoteChanged += state =>
            emit(new SseEvent("plan_vote", JsonSerializer.Serialize(state)));

        executor.OnExecutionStarted += (batch, completed) =>
            emit(new SseEvent("execution_started", JsonSerializer.Serialize(new { batch, completed })));

        executor.OnStateChanged += state =>
            emit(new SseEvent("state_changed", JsonSerializer.Serialize(new { state })));

        executor.OnExecutionVoteChanged += state =>
            emit(new SseEvent("execution_vote", JsonSerializer.Serialize(state)));
    }

    private ExecutorConfig BuildConfig() =>
        ExecutorConfig.FromConfiguration(configuration.GetSection("Executor"));
}

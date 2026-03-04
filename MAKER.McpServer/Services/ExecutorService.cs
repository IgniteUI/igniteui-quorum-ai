using System.Text.Json;
using System.Threading.Channels;
using MAKER.Configuration;

namespace MAKER.McpServer.Services;

public record SseEvent(string Type, string Data);

public class ExecutorService(IConfiguration configuration)
{
    public Executor Create() => new(BuildConfig(), "plaintext");

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

    private static void WireEvents(Executor executor, Action<SseEvent> emit)
    {
        executor.OnStepsProposed = steps =>
            emit(new SseEvent("steps_proposed", JsonSerializer.Serialize(steps)));

        executor.OnStepsAdded = (proposed, all) =>
            emit(new SseEvent("steps_added", JsonSerializer.Serialize(new { proposed, all })));

        executor.OnStepsRejected = ex =>
            emit(new SseEvent("steps_rejected", JsonSerializer.Serialize(new { reasons = ex.RejectionReasons })));

        executor.OnPlanVoteChanged = state =>
            emit(new SseEvent("plan_vote", JsonSerializer.Serialize(state)));

        executor.OnExecutionStarted = (batch, completed) =>
            emit(new SseEvent("execution_started", JsonSerializer.Serialize(new { batch, completed })));

        executor.OnStateChanged = state =>
            emit(new SseEvent("state_changed", JsonSerializer.Serialize(new { state })));

        executor.OnExecutionVoteChanged = state =>
            emit(new SseEvent("execution_vote", JsonSerializer.Serialize(state)));
    }

    private ExecutorConfig BuildConfig() =>
        ExecutorConfig.FromConfiguration(configuration.GetSection("Executor"));
}

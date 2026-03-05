using System.Text.Json;
using System.Threading.Channels;
using MAKER.AI.Models;
using MAKER.McpServer.Services;

namespace MAKER.McpServer.Api;

public static class MakerApiEndpoints
{
    public static void MapMakerApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/plan", HandlePlan);
        app.MapPost("/api/execute", HandleExecute);
        app.MapPost("/api/plan-and-execute", HandlePlanAndExecute);
    }

    private static Task HandlePlan(HttpContext ctx, PlanRequest req, ExecutorService svc, CancellationToken ct) =>
        StreamSse(ctx, svc, ct, async (executor, emit, token) =>
        {
            var steps = await executor.Plan(req.Prompt, req.BatchSize, req.K, cancellationToken: token);
            emit(new SseEvent("complete", JsonSerializer.Serialize(steps)));
        });

    private static Task HandleExecute(HttpContext ctx, ExecuteRequest req, ExecutorService svc, CancellationToken ct) =>
        StreamSse(ctx, svc, ct, async (executor, emit, token) =>
        {
            var steps = JsonSerializer.Deserialize<List<Step>>(req.StepsJson)
                ?? throw new ArgumentException("Invalid steps JSON");

            var result = await executor.Execute(steps, req.Prompt, req.BatchSize, req.K, cancellationToken: token);
            emit(new SseEvent("complete", JsonSerializer.Serialize(new { result })));
        });

    private static Task HandlePlanAndExecute(HttpContext ctx, PlanRequest req, ExecutorService svc, CancellationToken ct) =>
        StreamSse(ctx, svc, ct, async (executor, emit, token) =>
        {
            emit(new SseEvent("phase", "Planning..."));
            var steps = await executor.Plan(req.Prompt, req.BatchSize, req.K, cancellationToken: token);

            emit(new SseEvent("phase", $"Executing {steps.Count} steps..."));
            var result = await executor.Execute(steps, req.Prompt, req.BatchSize, req.K, cancellationToken: token);

            emit(new SseEvent("complete", JsonSerializer.Serialize(new { steps, result })));
        });

    // -------------------------------------------------------------------------

    private static async Task StreamSse(
        HttpContext ctx,
        ExecutorService svc,
        CancellationToken ct,
        Func<Executor, Action<SseEvent>, CancellationToken, Task> run)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.Append("Cache-Control", "no-cache");
        ctx.Response.Headers.Append("X-Accel-Buffering", "no");

        var channel = Channel.CreateUnbounded<SseEvent>(
            new UnboundedChannelOptions { SingleWriter = false });

        var executor = svc.CreateWithSseEvents(channel.Writer);

        // Run executor in the background; complete the channel when finished
        _ = Task.Run(async () =>
        {
            try
            {
                await run(executor, evt => channel.Writer.TryWrite(evt), ct);
            }
            catch (Exception ex)
            {
                channel.Writer.TryWrite(new SseEvent("error",
                    JsonSerializer.Serialize(new { message = ex.Message })));
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        });

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            await ctx.Response.WriteAsync($"event: {evt.Type}\ndata: {evt.Data}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
}

public record PlanRequest(string Prompt, int BatchSize = 2, int K = 10);
public record ExecuteRequest(string Prompt, string StepsJson, int BatchSize = 2, int K = 10);

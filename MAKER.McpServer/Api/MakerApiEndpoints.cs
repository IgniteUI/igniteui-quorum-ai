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

        app.MapGet("/api/mcp-servers", HandleGetMcpServers);
        app.MapPost("/api/mcp-servers", HandleAddMcpServer);
        app.MapDelete("/api/mcp-servers/{name}", HandleRemoveMcpServer);

        app.MapGet("/api/format", HandleGetFormat);
        app.MapPut("/api/format", HandleSetFormat);
    }

    private static Task HandlePlan(HttpContext ctx, PlanRequest req, ExecutorService svc, CancellationToken ct) =>
        StreamSse(ctx, svc, ct, async (executor, emit, token) =>
        {
            var steps = await executor.Plan(req.Prompt, req.BatchSize, req.K, req.MaxSteps, cancellationToken: token);
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
            var steps = await executor.Plan(req.Prompt, req.BatchSize, req.K, req.MaxSteps, cancellationToken: token);

            emit(new SseEvent("phase", $"Executing {steps.Count} steps..."));
            var result = await executor.Execute(steps, req.Prompt, req.BatchSize, req.K, cancellationToken: token);

            emit(new SseEvent("complete", JsonSerializer.Serialize(new { steps, result })));
        });

    private static IResult HandleGetMcpServers(ExecutorService svc) =>
        Results.Ok(svc.GetMcpServers());

    private static IResult HandleAddMcpServer(McpServerRequest req, ExecutorService svc)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Url))
            return Results.BadRequest(new { message = "Name and Url are required." });

        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri))
            return Results.BadRequest(new { message = "Invalid URL." });

        svc.AddMcpServer(new MCPServerInfo
        {
            Name = req.Name.Trim(),
            Description = req.Description?.Trim() ?? string.Empty,
            Url = uri,
            ApiKey = string.IsNullOrWhiteSpace(req.ApiKey) ? null : req.ApiKey.Trim()
        });
        return Results.Ok(svc.GetMcpServers());
    }

    private static IResult HandleRemoveMcpServer(string name, ExecutorService svc) =>
        svc.RemoveMcpServer(name) ? Results.Ok(svc.GetMcpServers()) : Results.NotFound();

    private static IResult HandleGetFormat(ExecutorService svc) =>
        Results.Ok(new { format = svc.GetConfiguredFormat() });

    private static IResult HandleSetFormat(FormatRequest req, ExecutorService svc)
    {
        if (string.IsNullOrWhiteSpace(req.Format))
            return Results.BadRequest(new { message = "Format is required." });

        svc.SetFormat(req.Format);
        return Results.Ok(new { format = svc.GetConfiguredFormat() });
    }

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

public record PlanRequest(string Prompt, int BatchSize = 2, int K = 10, int MaxSteps = 10);
public record ExecuteRequest(string Prompt, string StepsJson, int BatchSize = 2, int K = 10);
public record McpServerRequest(string Name, string Url, string? Description = null, string? ApiKey = null);
public record FormatRequest(string Format);

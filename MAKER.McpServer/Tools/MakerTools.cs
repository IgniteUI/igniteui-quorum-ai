using System.ComponentModel;
using System.Text.Json;
using MAKER.AI.Models;
using MAKER.McpServer.Services;
using ModelContextProtocol.Server;

namespace MAKER.McpServer.Tools;

[McpServerToolType]
public class MakerTools(ExecutorService executorService)
{
    [McpServerTool(Name = "maker_plan")]
    [Description("Generate a plan as a sequence of steps for a given task using MAKER's multi-model voting system.")]
    public async Task<string> Plan(
        [Description("The task or goal to create a plan for")] string prompt,
        [Description("Number of steps to generate per batch (default: 2)")] int batchSize = 2,
        [Description("Voting consensus threshold — higher requires more agreement (default: 10)")] int k = 10,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var executor = executorService.CreateWithProgress(progress);
        var steps = await executor.Plan(prompt, batchSize, k);
        return JsonSerializer.Serialize(steps, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool(Name = "maker_execute")]
    [Description("Execute a sequence of steps produced by maker_plan and return the final result.")]
    public async Task<string> Execute(
        [Description("JSON array of steps produced by maker_plan")] string stepsJson,
        [Description("The original task or goal")] string prompt,
        [Description("Number of steps to execute per batch (default: 2)")] int batchSize = 2,
        [Description("Voting consensus threshold (default: 10)")] int k = 10,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var steps = JsonSerializer.Deserialize<List<Step>>(stepsJson)
            ?? throw new ArgumentException("Invalid steps JSON");

        var executor = executorService.CreateWithProgress(progress);
        return await executor.Execute(steps, prompt, batchSize, k);
    }

    [McpServerTool(Name = "maker_plan_and_execute")]
    [Description("Plan and fully execute a task in one call using MAKER's multi-model voting system.")]
    public async Task<string> PlanAndExecute(
        [Description("The task or goal to plan and execute")] string prompt,
        [Description("Number of steps per batch (default: 2)")] int batchSize = 2,
        [Description("Voting consensus threshold (default: 10)")] int k = 10,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var executor = executorService.CreateWithProgress(progress);

        progress?.Report(JsonSerializer.Serialize(new SseEvent("phase", "Planning...")));
        var steps = await executor.Plan(prompt, batchSize, k);

        progress?.Report(JsonSerializer.Serialize(new SseEvent("phase", $"Executing {steps.Count} steps...")));
        return await executor.Execute(steps, prompt, batchSize, k);
    }
}

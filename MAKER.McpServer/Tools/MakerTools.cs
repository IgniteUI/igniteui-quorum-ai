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
    [Description("Generate a plan as a sequence of steps for a given task using MAKER's multi-model voting system. Note: MAKER cannot read files or access external resources. All required context, code, and data must be provided directly in the prompt.")]
    public async Task<string> Plan(
        [Description("The task or goal to create a plan for. MAKER cannot read files, so include all necessary content, code, and context inline in this prompt.")] string prompt,
        [Description("Number of steps to generate per batch (default: 2)")] int batchSize = 2,
        [Description("Voting consensus threshold — higher requires more agreement (default: 6)")] int k = 6,
        [Description("Maximum number of steps in the plan (default: 10)")] int maxSteps = 10,
            [Description("Desired output format, e.g. 'plaintext', 'markdown', 'html', or a custom JSON schema (optional, uses server default if omitted)")] string? format = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var executor = executorService.CreateWithProgress(progress);
                if (!string.IsNullOrWhiteSpace(format))
                    executor.Format = format;
                var steps = await executor.Plan(prompt, batchSize, k, maxSteps, cancellationToken: cancellationToken);
                return JsonSerializer.Serialize(steps, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return FormatError("maker_plan", ex);
        }
    }

    [McpServerTool(Name = "maker_execute")]
    [Description("Execute a sequence of steps produced by maker_plan and return the final result. Note: MAKER cannot read files or access external resources. All required context, code, and data must be provided directly in the prompt.")]
    public async Task<string> Execute(
        [Description("JSON array of steps produced by maker_plan")] string stepsJson,
        [Description("The original task or goal. MAKER cannot read files, so include all necessary content, code, and context inline in this prompt.")] string prompt,
        [Description("Number of steps to execute per batch (default: 2)")] int batchSize = 2,
        [Description("Voting consensus threshold (default: 6)")] int k = 6,
        [Description("Desired output format, e.g. 'plaintext', 'markdown', 'html', or a custom JSON schema (optional, uses server default if omitted)")] string? format = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var steps = JsonSerializer.Deserialize<List<Step>>(stepsJson)
                ?? throw new ArgumentException("Invalid steps JSON");

            var executor = executorService.CreateWithProgress(progress);
            if (!string.IsNullOrWhiteSpace(format))
                executor.Format = format;
            return await executor.Execute(steps, prompt, batchSize, k, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            return FormatError("maker_execute", ex);
        }
    }

    [McpServerTool(Name = "maker_plan_and_execute")]
    [Description("Plan and fully execute a task in one call using MAKER's multi-model voting system. Note: MAKER cannot read files or access external resources. All required context, code, and data must be provided directly in the prompt.")]
    public async Task<string> PlanAndExecute(
        [Description("The task or goal to plan and execute. MAKER cannot read files, so include all necessary content, code, and context inline in this prompt.")] string prompt,
        [Description("Number of steps per batch (default: 2)")] int batchSize = 2,
        [Description("Voting consensus threshold (default: 6)")] int k = 6,
        [Description("Maximum number of steps in the plan (default: 10)")] int maxSteps = 10,
            [Description("Desired output format, e.g. 'plaintext', 'markdown', 'html', or a custom JSON schema (optional, uses server default if omitted)")] string? format = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var executor = executorService.CreateWithProgress(progress);
                if (!string.IsNullOrWhiteSpace(format))
                    executor.Format = format;

                progress?.Report(JsonSerializer.Serialize(new SseEvent("phase", "Planning...")));
                var steps = await executor.Plan(prompt, batchSize, k, maxSteps, cancellationToken: cancellationToken);

            progress?.Report(JsonSerializer.Serialize(new SseEvent("phase", $"Executing {steps.Count} steps...")));
            return await executor.Execute(steps, prompt, batchSize, k, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            return FormatError("maker_plan_and_execute", ex);
        }
    }

    private static string FormatError(string tool, Exception ex)
    {
        var messages = new List<string>();
        var current = ex;
        while (current != null)
        {
            var msg = current.Message?.Trim();
            if (!string.IsNullOrEmpty(msg) && !messages.Contains(msg))
                messages.Add(msg);
            current = current.InnerException;
        }

        var detail = string.Join(" → ", messages);
        return $"MAKER ERROR [{tool}]: {detail}";
    }
}

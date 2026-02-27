using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MAKER.McpServer.Api;
using MAKER.McpServer.Services;
using MAKER.McpServer.Tools;

Directory.SetCurrentDirectory(FindSolutionRoot());

var isStdio = args.Contains("--stdio");

if (isStdio)
{
    var host = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });

    // All logs must go to stderr — stdout is reserved for the MCP STDIO protocol
    host.Logging.ClearProviders();
    host.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    host.Services.AddSingleton<ExecutorService>();
    host.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<MakerTools>();

    await host.Build().RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

builder.Services.AddSingleton<ExecutorService>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<MakerTools>();

var app = builder.Build();

app.UseCors();
app.MapMcp("/mcp");
app.MapMakerApi();

await app.RunAsync();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static string FindSolutionRoot()
{
    // Walk up from the assembly directory, then from the working directory,
    // looking for the repo root that contains MAKER/MAKER.csproj.
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MAKER", "MAKER.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
    }

    return AppContext.BaseDirectory; // standalone binary: instructions are co-located
}

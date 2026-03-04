var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/config", (IConfiguration config) => new
{
    McpServerUrl = config["McpServerUrl"] ?? "http://localhost:5000",
    Executor = new
    {
        Format = config["Executor:Format"] ?? "plaintext",
        Clients = new
        {
            Planning        = new { Provider = config["Executor:Clients:Planning:Provider"],        Model = config["Executor:Clients:Planning:Model"] },
            PlanVoting      = new { Provider = config["Executor:Clients:PlanVoting:Provider"],      Model = config["Executor:Clients:PlanVoting:Model"] },
            Execution       = new { Provider = config["Executor:Clients:Execution:Provider"],       Model = config["Executor:Clients:Execution:Model"] },
            ExecutionVoting = new { Provider = config["Executor:Clients:ExecutionVoting:Provider"], Model = config["Executor:Clients:ExecutionVoting:Model"] }
        }
    }
});

app.Run();

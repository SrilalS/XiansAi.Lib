using DotNetEnv;
using Microsoft.Extensions.Logging;
using Xians.Lib.Agents.Core;

// -----------------------------------------------------------------------------
// Two Supervisors example
//
// One agent with two built-in supervisor workflows, each with its own
// OnUserChatMessage listener. DefineSupervisor() always uses the name
// "Supervisor Workflow", so a second supervisor must be registered with
// DefineBuiltIn and a distinct name.
// -----------------------------------------------------------------------------

Env.Load();

var serverUrl = Environment.GetEnvironmentVariable("XIANS_SERVER_URL")
    ?? throw new InvalidOperationException("XIANS_SERVER_URL environment variable is not set");
var xiansApiKey = Environment.GetEnvironmentVariable("XIANS_API_KEY")
    ?? throw new InvalidOperationException("XIANS_API_KEY environment variable is not set");

var xiansPlatform = await XiansPlatform.InitializeAsync(new()
{
    ServerUrl = serverUrl,
    ApiKey = xiansApiKey,
    ConsoleLogLevel = LogLevel.Information,
    ServerLogLevel = LogLevel.Information
});

var xiansAgent = xiansPlatform.Agents.Register(new()
{
    Name = "Two Supervisors Agent",
    Description = "A single agent with two supervisor workflows, each with its own chat listener",
    SamplePrompts =
    [
        "What products do you sell?",
        "I need help",
        "Talk to sales",
        "Talk to support"
    ],
    IsTemplate = true
});

var salesSupervisor = xiansAgent.Workflows.DefineBuiltIn("Sales Supervisor");
salesSupervisor.OnUserChatMessage(async (context) =>
{
    var userMessage = context.Message.Text ?? string.Empty;
    Console.WriteLine($"Sales Supervisor received: {userMessage}");
    await context.ReplyAsync($"[Sales Supervisor] You said: {userMessage}");
});

var supportSupervisor = xiansAgent.Workflows.DefineBuiltIn("Support Supervisor");
supportSupervisor.OnUserChatMessage(async (context) =>
{
    var userMessage = context.Message.Text ?? string.Empty;
    Console.WriteLine($"Support Supervisor received: {userMessage}");
    await context.ReplyAsync($"[Support Supervisor] You said: {userMessage}");
});

Console.WriteLine("Starting Two Supervisors Agent...");
Console.WriteLine("  • Sales Supervisor — chat listener");
Console.WriteLine("  • Support Supervisor — chat listener");
Console.WriteLine("Press Ctrl+C to stop.");

await xiansAgent.RunAllAsync();

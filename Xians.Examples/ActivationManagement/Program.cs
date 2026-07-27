using DotNetEnv;
using Microsoft.Extensions.Logging;
using Xians.Examples.ActivationManagement;
using Xians.Examples.ActivationManagement.ManagerAgent;
using Xians.Examples.ActivationManagement.TargetAgent;
using Xians.Lib.Agents.Core;

// -----------------------------------------------------------------------------
// Activation Management example
//
// Deploys two agents in the same tenant:
//   1. Activation Target Agent  — has an activable Heartbeat Workflow.
//   2. Activation Manager Agent — uses agent.Tenant.Agent(...) to:
//        ExistsAsync → CreateActivationAsync → ActivateAsync →
//        GetActivationStatusAsync / ActivationExistsAsync → ListActivationsAsync →
//        DeactivateAsync
//
// "Remove" is DeactivateAsync: the Agent API has no delete-activation endpoint yet.
//
// On startup the manager's lifecycle workflow is started automatically. You can
// also re-run it by invoking the manager agent's Default webhook.
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
    ServerLogLevel = LogLevel.Information,
});

// Register both agents (target first so its definitions exist before the manager activates it).
var targetAgent = TargetAgentSetup.Setup(xiansPlatform);
var managerAgent = ManagerAgentSetup.Setup(xiansPlatform);

// Upload definitions so the server knows about both agents before we create activations.
Console.WriteLine("Uploading workflow definitions...");
await targetAgent.UploadWorkflowDefinitionsAsync();
await managerAgent.UploadWorkflowDefinitionsAsync();
Console.WriteLine(
    $"Uploaded definitions for '{targetAgent.Name}' and '{managerAgent.Name}'.");

// Start both agents' Temporal workers, then kick off the lifecycle demo.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var workersTask = Task.WhenAll(
    targetAgent.RunAllAsync(cts.Token),
    managerAgent.RunAllAsync(cts.Token));

// Give workers a moment to connect to Temporal before starting the lifecycle workflow.
await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);

Console.WriteLine(
    $"Starting ActivationLifecycleWorkflow on '{managerAgent.Name}' "
    + $"(target='{targetAgent.Name}', activation='{Constants.DemoActivationName}')...");

try
{
    var demoKey = Guid.NewGuid().ToString("N");
    var result = await XiansContext.Workflows.ExecuteAsync<ActivationLifecycleWorkflow, ActivationLifecycleResult>(
        args: [],
        uniqueKey: demoKey);

    Console.WriteLine();
    Console.WriteLine("=== Activation lifecycle result ===");
    Console.WriteLine($"  AgentExists:                      {result.AgentExists}");
    Console.WriteLine($"  CreatedNew:                       {result.CreatedNew}");
    Console.WriteLine($"  ActivationId:                     {result.ActivationId}");
    Console.WriteLine($"  Activated:                        {result.Activated}");
    Console.WriteLine($"  StatusAfterActivate:              {result.StatusAfterActivate}");
    Console.WriteLine($"  ActivationExistsAfterActivate:    {result.ActivationExistsAfterActivate}");
    Console.WriteLine($"  ListedCount:                      {result.ListedCount}");
    Console.WriteLine($"  ListedNames:                      {string.Join(", ", result.ListedNames)}");
    Console.WriteLine($"  Deactivated:                      {result.Deactivated}");
    Console.WriteLine($"  StatusAfterDeactivate:            {result.StatusAfterDeactivate}");
    Console.WriteLine($"  ActivationExistsAfterDeactivate:  {result.ActivationExistsAfterDeactivate}");
    Console.WriteLine();
    Console.WriteLine(
        "Demo finished. Agents are still running — invoke the manager's Default webhook to re-run, "
        + "or Ctrl+C to exit.");
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.WriteLine("Cancelled before the demo completed.");
}
catch (Exception ex)
{
    Console.WriteLine($"Lifecycle demo failed: {ex}");
    Console.WriteLine("Workers will keep running; Ctrl+C to exit.");
}

try
{
    await workersTask;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.WriteLine("Agents stopped.");
}

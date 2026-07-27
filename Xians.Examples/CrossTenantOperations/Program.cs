using DotNetEnv;
using Microsoft.Extensions.Logging;
using Xians.Examples.RouterProcessor;
using Xians.Examples.RouterProcessor.ProcessorAgent;
using Xians.Examples.RouterProcessor.RouterAgent;
using Xians.Lib.Agents.Core;

// -----------------------------------------------------------------------------
// Router → Processor cross-tenant sample
//
// Deploys two agents:
//   1. Processor Agent  (IsTemplate = true)  — Integrator webhook that "processes" a payload.
//   2. Router Agent     (IsTemplate = false) — Integrator webhook that starts a custom workflow.
//
// When the Router's webhook is invoked with { tenantId, payload }:
//   - RouteToProcessorWorkflow runs an activity that uses the Xians Admin API to:
//       1. Resolve exactly one Processor Agent activation in that tenant (any name).
//       2. Ensure a webhook with a particular name exists (create if missing).
//       3. Invoke that webhook with the payload.
//
// Requires:
//   XIANS_SERVER_URL, XIANS_API_KEY, XIANS_ADMIN_TOKEN
//
// See README.md for the full walkthrough (Router webhook first without Processor
// activated, then deploy/activate Processor and call again).
// -----------------------------------------------------------------------------

Env.Load();

var serverUrl = Environment.GetEnvironmentVariable("XIANS_SERVER_URL")
    ?? throw new InvalidOperationException("XIANS_SERVER_URL environment variable is not set");
var xiansApiKey = Environment.GetEnvironmentVariable("XIANS_API_KEY")
    ?? throw new InvalidOperationException("XIANS_API_KEY environment variable is not set");
_ = Environment.GetEnvironmentVariable("XIANS_ADMIN_TOKEN")
    ?? throw new InvalidOperationException("XIANS_ADMIN_TOKEN environment variable is not set");

var xiansPlatform = await XiansPlatform.InitializeAsync(new()
{
    ServerUrl = serverUrl,
    ApiKey = xiansApiKey,
    ServerLogLevel = LogLevel.Information,
});

var processorAgent = ProcessorAgentSetup.Setup(xiansPlatform);
var routerAgent = RouterAgentSetup.Setup(xiansPlatform);

Console.WriteLine("Uploading workflow definitions...");
await processorAgent.UploadWorkflowDefinitionsAsync();
await routerAgent.UploadWorkflowDefinitionsAsync();
Console.WriteLine(
    $"Uploaded definitions for '{processorAgent.Name}' and '{routerAgent.Name}'.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine();
Console.WriteLine("=== Agents running ===");
Console.WriteLine($"  Router Agent:    {routerAgent.Name} (IsTemplate=false)");
Console.WriteLine($"  Processor Agent: {processorAgent.Name} (IsTemplate=true)");
Console.WriteLine();
Console.WriteLine("Next steps (see README.md):");
Console.WriteLine("  1. Activate Router Agent and create its Integrator webhook.");
Console.WriteLine("  2. Call that webhook with { tenantId, payload } (expect: not activated).");
Console.WriteLine("  3. Deploy + activate Processor Agent (exactly one activation, any name) on that tenant.");
Console.WriteLine("  4. Call the Router webhook again (expect: ProcessPayload created + invoked).");
Console.WriteLine();
Console.WriteLine("Example body:");
Console.WriteLine(
    """  { "tenantId": "<tenant-id>", "payload": { "orderId": "ORD-123", "amount": 42.5 } }""");
Console.WriteLine("Ctrl+C to stop.");
Console.WriteLine();

try
{
    await Task.WhenAll(
        routerAgent.RunAllAsync(cts.Token),
        processorAgent.RunAllAsync(cts.Token));
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.WriteLine("Agents stopped.");
}

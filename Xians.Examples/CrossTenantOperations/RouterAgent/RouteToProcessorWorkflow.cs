using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;

namespace Xians.Examples.RouterProcessor.RouterAgent;

/// <summary>
/// Custom Temporal workflow started by the Router Agent's Default webhook.
/// Takes the tenant-id + payload from the webhook and routes to Processor Agent
/// via the Xians Admin API (executed inside an activity).
/// </summary>
[Description("Routes a webhook payload to Processor Agent for a given tenant via the Admin API")]
[Workflow("Router Agent:Route To Processor Workflow")]
public class RouteToProcessorWorkflow
{
    [WorkflowRun]
    public async Task<RouteResult> RunAsync(string tenantId, string payload)
    {
        var activityOptions = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(2),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                MaximumAttempts = 3
            }
        };

        Workflow.Logger.LogInformation(
            "RouteToProcessorWorkflow started for tenant '{TenantId}' (payload length={Length}).",
            tenantId,
            payload?.Length ?? 0);

        var result = await Workflow.ExecuteActivityAsync(
            (RouteToProcessorActivities a) => a.RouteAsync(tenantId, payload ?? string.Empty),
            activityOptions);

        Workflow.Logger.LogInformation(
            "Route complete. Activated={Activated}, WebhookCreated={WebhookCreated}, "
            + "Invoked={Invoked}, Status={Status}, Message={Message}",
            result.Activated,
            result.WebhookCreated,
            result.Invoked,
            result.ProcessorStatusCode,
            result.Message);

        return result;
    }
}

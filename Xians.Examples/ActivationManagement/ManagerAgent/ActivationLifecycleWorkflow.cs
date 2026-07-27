using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;

namespace Xians.Examples.ActivationManagement.ManagerAgent;

/// <summary>
/// Orchestrates the cross-agent activation SDK demo:
/// create/activate/check/list, wait briefly so the target heartbeat can run, then deactivate.
/// </summary>
[Description("Exercises create/activate/check/list/deactivate against another agent via agent.Tenant.Agent(...)")]
[Workflow("Activation Manager Agent:Activation Lifecycle Workflow")]
public class ActivationLifecycleWorkflow
{
    /// <summary>
    /// How long to leave the target activation running (and heartbeating) before deactivating.
    /// </summary>
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(60);

    [WorkflowRun]
    public async Task<ActivationLifecycleResult> RunAsync()
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
            "ActivationLifecycleWorkflow started — managing activations of '{Target}'.",
            Constants.TargetAgentName);

        var result = await Workflow.ExecuteActivityAsync(
            (ActivationLifecycleActivities a) => a.RunLifecycleAsync(),
            activityOptions);

        Workflow.Logger.LogInformation(
            "Leaving activation '{Name}' active for {Delay} so the target Heartbeat Workflow can run.",
            result.ActivationName,
            ActiveWindow);
        await Workflow.DelayAsync(ActiveWindow);

        result = await Workflow.ExecuteActivityAsync(
            (ActivationLifecycleActivities a) => a.DeactivateDemoActivationAsync(result),
            activityOptions);

        Workflow.Logger.LogInformation(
            "Lifecycle complete. AgentExists={AgentExists}, CreatedNew={CreatedNew}, "
            + "Activated={Activated}, Deactivated={Deactivated}, "
            + "StatusAfterActivate={StatusAfterActivate}, StatusAfterDeactivate={StatusAfterDeactivate}",
            result.AgentExists,
            result.CreatedNew,
            result.Activated,
            result.Deactivated,
            result.StatusAfterActivate,
            result.StatusAfterDeactivate);

        return result;
    }
}

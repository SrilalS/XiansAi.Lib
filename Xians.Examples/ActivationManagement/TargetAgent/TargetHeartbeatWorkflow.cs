using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;

namespace Xians.Examples.ActivationManagement.TargetAgent;

/// <summary>
/// Long-running activable workflow started when an activation of
/// <see cref="Constants.TargetAgentName"/> is activated. It heartbeats until the
/// activation is deactivated (workflow cancelled) or the worker stops.
/// </summary>
[Description("Heartbeat workflow started by activating the Activation Target Agent")]
[Workflow("Activation Target Agent:Heartbeat Workflow")]
public class TargetHeartbeatWorkflow
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    [WorkflowRun]
    public async Task RunAsync()
    {
        var beat = 0;
        Workflow.Logger.LogInformation(
            "TargetHeartbeatWorkflow started under activation (workflow id={WorkflowId}).",
            Workflow.Info.WorkflowId);

        while (true)
        {
            beat++;
            Workflow.Logger.LogInformation("Heartbeat #{Beat} from Activation Target Agent", beat);
            await Workflow.DelayAsync(HeartbeatInterval);
        }
    }
}

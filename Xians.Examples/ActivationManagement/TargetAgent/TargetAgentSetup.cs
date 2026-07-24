using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Workflows.Models;

namespace Xians.Examples.ActivationManagement.TargetAgent;

/// <summary>
/// Registers the target agent whose activations the manager will create / activate / deactivate.
/// </summary>
internal static class TargetAgentSetup
{
    public static XiansAgent Setup(XiansPlatform platform)
    {
        var agent = platform.Agents.Register(new()
        {
            Name = Constants.TargetAgentName,
            Description = "Target agent used by the Activation Management example. "
                + "Its activable Heartbeat Workflow is started when an activation is activated.",
            Summary = "Worker agent managed via the cross-agent activation SDK.",
            Version = "1.0.0",
            Author = "99x",
            Category = "Examples",
            IsTemplate = false
        });

        // Activable = true so ActivateAsync on the server starts this workflow under the activation name.
        agent.Workflows.DefineCustom<TargetHeartbeatWorkflow>(new WorkflowOptions { Activable = true });

        return agent;
    }
}

using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Workflows.Models;

namespace Xians.Examples.ActivationManagement.ManagerAgent;

/// <summary>
/// Registers the manager agent that owns the lifecycle workflow/activities which call
/// <c>agent.Tenant.Agent(target).ExistsAsync / CreateActivationAsync / ActivateAsync / ...</c>.
/// </summary>
internal static class ManagerAgentSetup
{
    public static XiansAgent Setup(XiansPlatform platform)
    {
        var agent = platform.Agents.Register(new()
        {
            Name = Constants.ManagerAgentName,
            Description = "Demonstrates the cross-agent activation SDK: check agent existence, "
                + "create/list/activate/deactivate activations of another agent in the same tenant.",
            Summary = "Manager agent that drives activation lifecycle against a target agent.",
            Version = "1.0.0",
            Author = "99x",
            Category = "Examples",
            IsTemplate = false
        });

        // Lifecycle workflow is started on demand (from Program or the Integrator webhook) - not activable.
        var lifecycle = agent.Workflows.DefineCustom<ActivationLifecycleWorkflow>(
            new WorkflowOptions { Activable = false });
        lifecycle.AddActivity(new ActivationLifecycleActivities());

        // Optional: re-run the demo by invoking the manager's Default webhook.
        var integrator = agent.Workflows.DefineIntegrator();
        integrator.OnWebhook(async (context) =>
        {
            var key = Guid.NewGuid().ToString("N");
            Console.WriteLine($"Webhook received — starting ActivationLifecycleWorkflow (key={key}).");

            await XiansContext.Workflows.StartAsync<ActivationLifecycleWorkflow>(
                args: [],
                uniqueKey: key);

            context.Respond(new
            {
                message = "Activation lifecycle workflow started. Watch manager/target agent logs.",
                workflowKey = key
            });
        });

        return agent;
    }
}

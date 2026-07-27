using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;

namespace Xians.Examples.RouterProcessor.ProcessorAgent;

/// <summary>
/// Registers the template Processor Agent. Tenants create exactly one activation (any name);
/// the Router then ensures/invokes a named webhook that lands here.
/// </summary>
internal static class ProcessorAgentSetup
{
    public static XiansAgent Setup(XiansPlatform platform)
    {
        var agent = platform.Agents.Register(new()
        {
            Name = Constants.ProcessorAgentName,
            Description = "Template agent that receives routed payloads via a named Integrator webhook. "
                + "Each tenant should have exactly one activation (any name).",
            Summary = "Template processor that handles payloads routed from Router Agent.",
            Version = "1.0.0",
            Author = "99x",
            Category = "Examples",
            IsTemplate = true
        });

        var integrator = agent.Workflows.DefineIntegrator();
        integrator.OnWebhook(async (context) =>
        {
            try
            {
                var webhookName = context.Webhook.Name;
                var tenantId = context.Webhook.TenantId;
                var payloadLength = context.Webhook.Payload?.Length ?? 0;

                Console.WriteLine(
                    $"$$$$$$$$ [Processor Agent] Webhook '{webhookName}' received "
                    + $"(tenant={tenantId}, payload length={payloadLength}).");

                // Avoid echoing the raw payload verbatim in case it contains sensitive data.
                context.Respond(new
                {
                    processed = true,
                    agent = Constants.ProcessorAgentName,
                    webhookName,
                    tenantId,
                    payloadLength,
                    message = "Payload processed by Processor Agent."
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Processor Agent] Error handling webhook: {ex}");
                context.Response = WebhookResponse.InternalServerError(
                    "Processor Agent failed to handle webhook. See server logs.");
            }

            await Task.CompletedTask;
        });

        return agent;
    }
}

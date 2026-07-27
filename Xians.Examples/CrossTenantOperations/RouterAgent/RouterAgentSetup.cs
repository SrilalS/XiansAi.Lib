using System.Text.Json;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Workflows.Models;

namespace Xians.Examples.RouterProcessor.RouterAgent;

/// <summary>
/// Registers the non-template Router Agent: inbound webhook starts
/// <see cref="RouteToProcessorWorkflow"/>, which uses the Admin API to reach Processor Agent.
/// </summary>
internal static class RouterAgentSetup
{
    public static XiansAgent Setup(XiansPlatform platform)
    {
        var agent = platform.Agents.Register(new()
        {
            Name = Constants.RouterAgentName,
            Description = "Non-template router that accepts a webhook payload containing a tenant-id, "
                + "then uses the Xians Admin API to check Processor Agent activation, ensure a named "
                + "webhook, and invoke it with the payload.",
            Summary = "Cross-tenant router from webhook → Processor Agent via Admin API.",
            Version = "1.0.0",
            Author = "99x",
            Category = "Examples",
            IsTemplate = false
        });

        // Custom workflow is started on demand by the webhook — not activable.
        var routeWorkflow = agent.Workflows.DefineCustom<RouteToProcessorWorkflow>(
            new WorkflowOptions { Activable = false });
        routeWorkflow.AddActivity(new RouteToProcessorActivities());

        var integrator = agent.Workflows.DefineIntegrator();
        integrator.OnWebhook(async (context) =>
        {
            try
            {
                Console.WriteLine(
                    $"[Router Agent] Webhook '{context.Webhook.Name}' received "
                    + $"(payload length={context.Webhook.Payload?.Length ?? 0}).");

                if (!TryParseRouteRequest(context.Webhook.Payload, out var tenantId, out var payload, out var error))
                {
                    context.Response = WebhookResponse.BadRequest(error);
                    return;
                }

                var workflowKey = Guid.NewGuid().ToString("N");
                Console.WriteLine(
                    $"[Router Agent] Starting RouteToProcessorWorkflow "
                    + $"(tenant={tenantId}, key={workflowKey}).");

                // Fire-and-forget: Admin API + Processor invoke can exceed the webhook response window.
                // OnWebhook runs inside an activity, so Guid.NewGuid() is fine here.
                await XiansContext.Workflows.StartAsync<RouteToProcessorWorkflow>(
                    args: [tenantId, payload],
                    uniqueKey: workflowKey);

                context.Respond(new
                {
                    message = "RouteToProcessorWorkflow started. Watch Router/Processor agent logs.",
                    tenantId,
                    workflowKey
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Router Agent] Error processing webhook: {ex}");
                context.Response = WebhookResponse.InternalServerError(
                    "Failed to process webhook. See server logs for details.");
            }
        });

        return agent;
    }

    /// <summary>
    /// Expects JSON: <c>{ "tenantId": "...", "payload": { ... } }</c>.
    /// Also accepts <c>tenant-id</c> as an alternate property name.
    /// The nested <c>payload</c> (or the whole body when nested payload is absent) is forwarded.
    /// </summary>
    internal static bool TryParseRouteRequest(
        string? rawPayload,
        out string tenantId,
        out string payload,
        out string error)
    {
        tenantId = string.Empty;
        payload = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            error = "Webhook payload is required. Expected JSON: { \"tenantId\": \"...\", \"payload\": { ... } }.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Webhook payload must be a JSON object.";
                return false;
            }

            if (!TryGetStringProperty(root, "tenantId", out tenantId)
                && !TryGetStringProperty(root, "tenant-id", out tenantId))
            {
                error = "Missing required property 'tenantId' (or 'tenant-id').";
                return false;
            }

            if (root.TryGetProperty("payload", out var nested))
            {
                payload = nested.ValueKind == JsonValueKind.String
                    ? nested.GetString() ?? string.Empty
                    : nested.GetRawText();
            }
            else
            {
                // Forward the original body when nested payload is omitted.
                payload = rawPayload;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON payload: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetStringProperty(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var prop))
            return false;

        if (prop.ValueKind != JsonValueKind.String)
            return false;

        value = prop.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}

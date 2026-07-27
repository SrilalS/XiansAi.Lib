using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Xians.Examples.RouterProcessor.Admin;

namespace Xians.Examples.RouterProcessor.RouterAgent;

/// <summary>
/// Activities that call the Xians Admin API (HTTP I/O — must not run in deterministic workflow code).
/// Sequence: resolve single Processor activation → ensure named webhook → invoke webhook with payload.
/// </summary>
public class RouteToProcessorActivities
{
    [Activity]
    public async Task<RouteResult> RouteAsync(string tenantId, string payload)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        var result = new RouteResult
        {
            TenantId = tenantId,
            TargetAgent = Constants.ProcessorAgentName,
            WebhookName = Constants.ProcessorWebhookName
        };

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            result.Message = "tenantId is required.";
            return result;
        }

        using var admin = XiansAdminApiClient.FromEnvironment();

        // 1) Resolve Processor Agent activation for this tenant.
        //    Any activation name is fine — but there must be exactly one.
        var (status, activation, count) = await admin.ResolveSingleActivationAsync(
            tenantId,
            Constants.ProcessorAgentName);

        result.ActivationCount = count;
        logger.LogInformation(
            "[1/3] Processor activation resolve: tenant={TenantId}, agent={Agent}, status={Status}, count={Count}",
            tenantId,
            Constants.ProcessorAgentName,
            status,
            count);

        switch (status)
        {
            case ActivationResolveStatus.None:
                result.Activated = false;
                result.Message =
                    $"Processor Agent has no activation in tenant '{tenantId}'. "
                    + "Deploy and activate it, then retry.";
                return result;

            case ActivationResolveStatus.Ambiguous:
                result.Activated = false;
                result.Message =
                    $"Processor Agent has {count} activations in tenant '{tenantId}'. "
                    + "Expected exactly one; cannot choose which to route to.";
                return result;

            case ActivationResolveStatus.Single:
                break;
        }

        if (activation == null || string.IsNullOrWhiteSpace(activation.Name))
        {
            result.Activated = false;
            result.Message = "Resolved activation was missing a name.";
            return result;
        }

        if (!activation.IsActive)
        {
            result.Activated = false;
            result.ActivationName = activation.Name;
            result.ActivationId = activation.Id;
            result.Message =
                $"Processor Agent activation '{activation.Name}' exists in tenant '{tenantId}' "
                + "but is not active. Activate it, then retry.";
            return result;
        }

        result.Activated = true;
        result.ActivationName = activation.Name;
        result.ActivationId = activation.Id;

        // 2) Ensure the named webhook exists on that activation (create via Admin API when missing).
        //    Keep webhookUrl local — do not put it on RouteResult (credential leak into history).
        var (webhook, created) = await admin.EnsureWebhookAsync(
            tenantId,
            Constants.ProcessorAgentName,
            activation.Name,
            Constants.ProcessorWebhookName,
            workflowName: Constants.ProcessorWorkflowName);

        result.WebhookExisted = !created;
        result.WebhookCreated = created;
        result.WebhookId = webhook.Id;
        logger.LogInformation(
            "[2/3] Webhook ensure: activation={Activation}, name={WebhookName}, id={Id}, created={Created}",
            activation.Name,
            Constants.ProcessorWebhookName,
            webhook.Id,
            created);

        if (string.IsNullOrWhiteSpace(webhook.WebhookUrl))
        {
            result.Message =
                $"Webhook '{Constants.ProcessorWebhookName}' has no webhookUrl; cannot invoke.";
            return result;
        }

        // 3) Invoke the Processor webhook with the payload.
        var (statusCode, body) = await admin.InvokeWebhookAsync(webhook.WebhookUrl, payload);
        result.Invoked = true;
        result.ProcessorStatusCode = statusCode;
        result.ProcessorResponseBody = Truncate(body, 500);
        result.Message = statusCode is >= 200 and < 300
            ? "Payload routed to Processor Agent successfully."
            : $"Processor webhook returned HTTP {statusCode}.";

        logger.LogInformation(
            "[3/3] Invoked Processor webhook: status={Status}, responseLength={Length}",
            statusCode,
            body?.Length ?? 0);

        return result;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..max] + "…";
    }
}

/// <summary>Serializable outcome of the Router → Processor route (safe for workflow history).</summary>
public class RouteResult
{
    public string TenantId { get; set; } = string.Empty;
    public string TargetAgent { get; set; } = string.Empty;
    public string ActivationName { get; set; } = string.Empty;
    public string WebhookName { get; set; } = string.Empty;

    public bool Activated { get; set; }
    public string? ActivationId { get; set; }
    public int ActivationCount { get; set; }

    public bool WebhookExisted { get; set; }
    public bool WebhookCreated { get; set; }
    public string? WebhookId { get; set; }

    public bool Invoked { get; set; }
    public int? ProcessorStatusCode { get; set; }
    public string? ProcessorResponseBody { get; set; }

    public string? Message { get; set; }
}

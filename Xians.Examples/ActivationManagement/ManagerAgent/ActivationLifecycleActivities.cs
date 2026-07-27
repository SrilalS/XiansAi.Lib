using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Xians.Lib.Agents.Core;
using Xians.Lib.Temporal.Workflows.Activations;

namespace Xians.Examples.ActivationManagement.ManagerAgent;

/// <summary>
/// Activities that exercise the cross-agent activation SDK against
/// <see cref="Constants.TargetAgentName"/>. HTTP-backed SDK calls must run in activities,
/// not in deterministic workflow code.
/// </summary>
public class ActivationLifecycleActivities
{
    /// <summary>
    /// Full lifecycle: agent exists → create → activate → status/exists/list → wait → deactivate.
    /// There is no Agent-API delete-activation endpoint yet, so "remove" means deactivate.
    /// </summary>
    [Activity]
    public async Task<ActivationLifecycleResult> RunLifecycleAsync()
    {
        var logger = ActivityExecutionContext.Current.Logger;
        var manager = XiansContext.CurrentAgent;
        var target = manager.Tenant.Agent(Constants.TargetAgentName);
        var result = new ActivationLifecycleResult
        {
            TargetAgentName = Constants.TargetAgentName,
            ActivationName = Constants.DemoActivationName
        };

        // 1) Does the target agent exist in this tenant?
        result.AgentExists = await target.ExistsAsync();
        logger.LogInformation(
            "[1/7] ExistsAsync('{Agent}') => {Exists}",
            Constants.TargetAgentName,
            result.AgentExists);

        if (!result.AgentExists)
        {
            throw new InvalidOperationException(
                $"Target agent '{Constants.TargetAgentName}' was not found in the tenant. "
                + "Ensure its workflow definitions were uploaded before running the demo.");
        }

        // 2) Create activation (or reuse if a previous run left it behind).
        var existing = (await target.ListActivationsAsync())
            .FirstOrDefault(a => string.Equals(a.Name, Constants.DemoActivationName, StringComparison.Ordinal));

        if (existing != null)
        {
            logger.LogInformation(
                "[2/7] Activation '{Name}' already exists (id={Id}, active={Active}) — reusing.",
                existing.Name,
                existing.Id,
                existing.IsActive);
            result.ActivationId = existing.Id;
            result.CreatedNew = false;

            // Ensure a clean start: deactivate if still active from a previous run.
            if (existing.IsActive)
            {
                logger.LogInformation("Deactivating leftover active activation before re-activate.");
                existing = await existing.DeactivateAsync();
            }
        }
        else
        {
            var created = await target.CreateActivationAsync(
                name: Constants.DemoActivationName,
                description: "Created by Activation Management example");
            logger.LogInformation(
                "[2/7] CreateActivationAsync('{Name}') => id={Id}, active={Active}",
                created.Name,
                created.Id,
                created.IsActive);
            result.ActivationId = created.Id;
            result.CreatedNew = true;
            existing = created;
        }

        // 3) Activate — starts the target's activable Heartbeat Workflow under this activation.
        var activated = await existing.ActivateAsync();
        result.Activated = activated.IsActive;
        logger.LogInformation(
            "[3/7] ActivateAsync(id={Id}) => IsActive={Active}, workflowIds=[{WorkflowIds}]",
            activated.Id,
            activated.IsActive,
            string.Join(", ", activated.WorkflowIds ?? []));

        // 4) Status check (Active / NotFound / Deactivated).
        var status = await target.GetActivationStatusAsync(Constants.DemoActivationName);
        result.StatusAfterActivate = status.ToString();
        logger.LogInformation(
            "[4/7] GetActivationStatusAsync('{Name}') => {Status}",
            Constants.DemoActivationName,
            status);

        // 5) Convenience bool.
        result.ActivationExistsAfterActivate = await target.ActivationExistsAsync(Constants.DemoActivationName);
        logger.LogInformation(
            "[5/7] ActivationExistsAsync('{Name}') => {Exists}",
            Constants.DemoActivationName,
            result.ActivationExistsAfterActivate);

        if (status != ActivationCheckStatus.Active || !result.ActivationExistsAfterActivate)
        {
            throw new InvalidOperationException(
                $"Expected activation '{Constants.DemoActivationName}' to be Active after ActivateAsync, "
                + $"but status={status}, exists={result.ActivationExistsAfterActivate}.");
        }

        // 6) List activations for the target agent.
        var listed = await target.ListActivationsAsync();
        result.ListedCount = listed.Count;
        result.ListedNames = listed.Select(a => a.Name).ToList();
        logger.LogInformation(
            "[6/7] ListActivationsAsync() => {Count} activation(s): {Names}",
            listed.Count,
            string.Join(", ", result.ListedNames));

        return result;
    }

    /// <summary>
    /// Deactivates the demo activation (the available "remove" step on the Agent API).
    /// </summary>
    [Activity]
    public async Task<ActivationLifecycleResult> DeactivateDemoActivationAsync(ActivationLifecycleResult prior)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        var manager = XiansContext.CurrentAgent;
        var target = manager.Tenant.Agent(Constants.TargetAgentName);

        if (string.IsNullOrWhiteSpace(prior.ActivationId))
        {
            throw new InvalidOperationException("No activation id to deactivate.");
        }

        var deactivated = await target.DeactivateAsync(prior.ActivationId);
        prior.Deactivated = !deactivated.IsActive;
        logger.LogInformation(
            "[7/7] DeactivateAsync(id={Id}) => IsActive={Active}",
            deactivated.Id,
            deactivated.IsActive);

        var status = await target.GetActivationStatusAsync(Constants.DemoActivationName);
        prior.StatusAfterDeactivate = status.ToString();
        prior.ActivationExistsAfterDeactivate = await target.ActivationExistsAsync(Constants.DemoActivationName);
        logger.LogInformation(
            "After deactivate: GetActivationStatusAsync => {Status}, ActivationExistsAsync => {Exists}",
            status,
            prior.ActivationExistsAfterDeactivate);

        return prior;
    }
}

/// <summary>Serializable outcome of the activation lifecycle demo.</summary>
public class ActivationLifecycleResult
{
    public string TargetAgentName { get; set; } = string.Empty;
    public string ActivationName { get; set; } = string.Empty;
    public string? ActivationId { get; set; }

    public bool AgentExists { get; set; }
    public bool CreatedNew { get; set; }
    public bool Activated { get; set; }
    public bool Deactivated { get; set; }

    public string? StatusAfterActivate { get; set; }
    public string? StatusAfterDeactivate { get; set; }
    public bool ActivationExistsAfterActivate { get; set; }
    public bool ActivationExistsAfterDeactivate { get; set; }

    public int ListedCount { get; set; }
    public List<string> ListedNames { get; set; } = new();
}

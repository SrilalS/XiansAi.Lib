namespace Xians.Lib.Agents.Core;

/// <summary>
/// Entry point for inspecting other agents in the current tenant from a calling
/// <see cref="XiansAgent"/>. Obtained via <see cref="XiansAgent.Tenant"/>.
/// </summary>
/// <example>
/// <code>
/// var other = agent.Tenant.Agent("Fraud Detection Agent");
/// if (await other.ExistsAsync() &amp;&amp; await other.ActivationExistsAsync("fraud-eu"))
/// {
///     // Target agent and activation are available in this tenant.
/// }
/// </code>
/// </example>
public class TenantAgents
{
    private readonly XiansAgent _owner;

    internal TenantAgents(XiansAgent owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// Returns a reference to another agent in the current tenant by name.
    /// The target agent does not need to be registered in this process.
    /// </summary>
    /// <param name="agentName">The name of the target agent.</param>
    /// <returns>An <see cref="AgentReference"/> that can check agent/activation existence and status.</returns>
    public AgentReference Agent(string agentName) => new(_owner, agentName);
}

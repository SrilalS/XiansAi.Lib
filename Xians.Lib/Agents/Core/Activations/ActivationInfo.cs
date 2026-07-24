using System.Text.Json.Serialization;

namespace Xians.Lib.Agents.Core.Activations;

/// <summary>
/// A tenant activation for an agent, returned by list/create/activate/deactivate on
/// <see cref="AgentReference"/>. When obtained from those APIs the instance is bound to its
/// owning <see cref="AgentReference"/> so <see cref="ActivateAsync"/> / <see cref="DeactivateAsync"/>
/// can be called directly on the handle.
/// </summary>
public class ActivationInfo
{
    private AgentReference? _boundReference;

    /// <summary>Unique activation id (Mongo ObjectId). Used by activate/deactivate.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable activation name (idPostfix).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Owning agent name.</summary>
    [JsonPropertyName("agentName")]
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Optional description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Optional participant id the activation runs as.</summary>
    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; set; }

    /// <summary>User/system that created the activation.</summary>
    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>When the activation was created (UTC).</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Tenant that owns the activation.</summary>
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Workflow configuration attached to the activation, if any.</summary>
    [JsonPropertyName("workflowConfiguration")]
    public ActivationWorkflowConfig? WorkflowConfiguration { get; set; }

    /// <summary>Temporal workflow ids started under this activation.</summary>
    [JsonPropertyName("workflowIds")]
    public List<string> WorkflowIds { get; set; } = new();

    /// <summary>
    /// Stored active flag from the server. Prefer <see cref="IsActive"/> for the effective status.
    /// </summary>
    [JsonPropertyName("active")]
    public bool? Active { get; set; }

    /// <summary>
    /// Whether the activation is currently active. Uses <see cref="Active"/> when set; otherwise
    /// falls back to <see cref="ActivatedAt"/> / <see cref="DeactivatedAt"/> (legacy documents).
    /// </summary>
    [JsonIgnore]
    public bool IsActive => Active ?? (ActivatedAt.HasValue && !DeactivatedAt.HasValue);

    /// <summary>When the activation was last activated (UTC).</summary>
    [JsonPropertyName("activatedAt")]
    public DateTime? ActivatedAt { get; set; }

    /// <summary>When the activation was last deactivated (UTC).</summary>
    [JsonPropertyName("deactivatedAt")]
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>
    /// Binds this instance to an <see cref="AgentReference"/> so handle methods can call the server.
    /// Called by the SDK after deserialization; not for external use.
    /// </summary>
    internal void Bind(AgentReference reference)
    {
        _boundReference = reference ?? throw new ArgumentNullException(nameof(reference));
    }

    /// <summary>
    /// Activates this activation (starts its workflows) in the current tenant.
    /// </summary>
    /// <param name="workflowConfiguration">Optional workflow configuration override for this activate call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated activation returned by the server.</returns>
    public Task<ActivationInfo> ActivateAsync(
        IEnumerable<WorkflowConfig>? workflowConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        EnsureBound();
        return _boundReference!.ActivateAsync(Id, workflowConfiguration, cancellationToken);
    }

    /// <summary>
    /// Deactivates this activation (cancels its workflows) in the current tenant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated activation returned by the server.</returns>
    public Task<ActivationInfo> DeactivateAsync(CancellationToken cancellationToken = default)
    {
        EnsureBound();
        return _boundReference!.DeactivateAsync(Id, cancellationToken);
    }

    private void EnsureBound()
    {
        if (_boundReference == null)
        {
            throw new InvalidOperationException(
                "This ActivationInfo is not bound to an AgentReference. " +
                "Obtain it via AgentReference.ListActivationsAsync / CreateActivationAsync / ActivateAsync / DeactivateAsync.");
        }
    }
}

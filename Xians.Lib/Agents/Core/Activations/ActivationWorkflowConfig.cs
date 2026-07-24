using System.Text.Json.Serialization;

namespace Xians.Lib.Agents.Core.Activations;

/// <summary>
/// A single named input value for a workflow configuration on an activation.
/// Mirrors the server's <c>WorkflowInput</c>.
/// </summary>
public class WorkflowInputValue
{
    /// <summary>Input parameter name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>Input parameter value (serialized as a string on the server).</summary>
    [JsonPropertyName("value")]
    public required string Value { get; set; }
}

/// <summary>
/// Configuration for a single workflow type within an activation
/// (workflow type + ordered inputs). Mirrors the server's <c>WorkflowConfiguration</c>.
/// </summary>
public class WorkflowConfig
{
    /// <summary>The workflow type (e.g. <c>AgentName:Workflow Name</c>).</summary>
    [JsonPropertyName("workflowType")]
    public required string WorkflowType { get; set; }

    /// <summary>Ordered input values for the workflow.</summary>
    [JsonPropertyName("inputs")]
    public List<WorkflowInputValue> Inputs { get; set; } = new();
}

/// <summary>
/// Container for multiple workflow configurations on an activation.
/// Mirrors the server's <c>ActivationWorkflowConfiguration</c>.
/// </summary>
public class ActivationWorkflowConfig
{
    /// <summary>Workflow configurations belonging to the activation.</summary>
    [JsonPropertyName("workflows")]
    public List<WorkflowConfig> Workflows { get; set; } = new();
}

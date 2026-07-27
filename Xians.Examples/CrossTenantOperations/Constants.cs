namespace Xians.Examples.RouterProcessor;

/// <summary>Shared names used by the Router and Processor agents.</summary>
internal static class Constants
{
    /// <summary>Non-template router agent that receives the inbound webhook.</summary>
    public const string RouterAgentName = "Router Agent";

    /// <summary>Template (system-scoped) agent that processes tenant payloads.</summary>
    public const string ProcessorAgentName = "Processor Agent";

    /// <summary>Particular webhook name the Router ensures/creates and then invokes.</summary>
    public const string ProcessorWebhookName = "ProcessPayload";

    /// <summary>Workflow the Processor webhook delivers to (Integrator).</summary>
    public const string ProcessorWorkflowName = "Integrator Workflow";
}

using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using Xians.Lib.Agents.Core;
using Xians.Lib.Temporal;
using Xians.Lib.Temporal.Workflows.Messaging;
using Xians.Lib.Temporal.Workflows.Messaging.Models;

namespace Xians.Lib.Agents.Messaging;

/// <summary>
/// Activity executor for messaging operations.
/// Handles context-aware execution of message activities.
/// Eliminates duplication of Workflow.InWorkflow checks in CurrentMessage and UserMessaging.
/// </summary>
internal class MessageActivityExecutor : ContextAwareActivityExecutor<MessageActivities, MessageService>
{
    private readonly XiansAgent _agent;

    public MessageActivityExecutor(XiansAgent agent, ILogger logger)
        : base(logger)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    }

    protected override MessageService CreateService()
    {
        if (_agent.HttpService == null)
        {
            throw new InvalidOperationException(
                "Message service is not available. Ensure HTTP service is configured for the agent.");
        }

        var logger = Common.Infrastructure.LoggerFactory.CreateLogger<MessageService>();
        return new MessageService(_agent.HttpService.Client, logger);
    }

    /// <summary>
    /// Sends a message using context-aware execution.
    /// </summary>
    public async Task SendMessageAsync(SendMessageRequest request)
    {
        await ExecuteAsync(
            act => act.SendMessageAsync(request),
            svc => svc.SendAsync(request),
            operationName: "SendMessage");
    }

    /// <summary>
    /// Gets message history using context-aware execution.
    /// </summary>
    public async Task<List<DbMessage>> GetHistoryAsync(GetMessageHistoryRequest request)
    {
        return await ExecuteAsync(
            act => act.GetMessageHistoryAsync(request),
            svc => svc.GetHistoryAsync(request),
            operationName: "GetMessageHistory");
    }

    /// <summary>
    /// Gets the last task ID using context-aware execution.
    /// </summary>
    public async Task<string?> GetLastTaskIdAsync(GetLastTaskIdRequest request)
    {
        return await ExecuteAsync(
            act => act.GetLastTaskIdAsync(request),
            svc => svc.GetLastTaskIdAsync(request),
            operationName: "GetLastTaskId");
    }

    /// <summary>
    /// Sends files using direct HTTP. Never executed as a Temporal activity because file bytes
    /// cannot pass through Temporal payloads.
    /// </summary>
    public async Task SendFileAsync(SendFileRequest request)
    {
        if (Workflow.InWorkflow)
        {
            throw new InvalidOperationException(
                "SendFileAsync cannot run inside Temporal workflow code because file bytes " +
                "cannot pass through Temporal payloads. Call it from a message handler " +
                "(OnUserChatMessage, OnFileUpload, …) or from an activity.");
        }

        if (_agent.HttpService == null)
        {
            throw new InvalidOperationException(
                "Message service is not available. Ensure HTTP service is configured for the agent.");
        }

        var logger = Common.Infrastructure.LoggerFactory.CreateLogger<FileSendService>();
        var service = new FileSendService(_agent.HttpService.Client, logger);
        await service.SendAsync(request);
    }
}


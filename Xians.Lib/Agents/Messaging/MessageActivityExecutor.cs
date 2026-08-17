using Microsoft.Extensions.Logging;
using Temporalio.Exceptions;
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
    /// <summary>
    /// Base64 characters of inline file content allowed in a single SendFile activity argument.
    /// Activity arguments are persisted in workflow history, which Temporal caps at its blob size
    /// limit (2 MB by default); the remainder is headroom for the other request fields.
    /// </summary>
    private const int MaxInlineContentLengthFromWorkflow = 1_500_000;

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
    /// Sends files using context-aware execution.
    /// From workflow code the request travels to the activity as a Temporal payload, so inline
    /// file bytes are trimmed and size-checked first.
    /// </summary>
    public async Task SendFileAsync(SendFileRequest request)
    {
        var inWorkflow = Workflow.InWorkflow;
        var effectiveRequest = inWorkflow ? PrepareForActivityPayload(request) : request;

        await ExecuteAsync(
            act => act.SendFileAsync(effectiveRequest),
            svc => svc.SendFileAsync(effectiveRequest),
            options: inWorkflow ? MessageActivityOptions.GetStandardOptions(request.WorkflowType) : null,
            operationName: "SendFile");
    }

    /// <summary>
    /// Returns a copy of the request that is safe to serialize into workflow history: files the
    /// platform already stores travel as references (their bytes would be re-uploaded for nothing),
    /// and the remaining inline content is bounded.
    /// </summary>
    internal static SendFileRequest PrepareForActivityPayload(SendFileRequest request)
    {
        var files = new List<UploadedFile>(request.Files.Count);
        long inlineContentLength = 0;

        foreach (var file in request.Files)
        {
            if (!string.IsNullOrEmpty(file.FileId))
            {
                files.Add(new UploadedFile(null, file.FileName, file.ContentType, file.FileSize, file.FileId));
                continue;
            }

            inlineContentLength += file.Content.Length;
            files.Add(file);
        }

        if (inlineContentLength > MaxInlineContentLengthFromWorkflow)
        {
            // A FailureException fails the workflow with this message instead of suspending it
            // behind endless workflow task retries.
            throw new ApplicationFailureException(
                $"Cannot send {inlineContentLength} base64 characters of file content from workflow code. " +
                $"Activity arguments are stored in Temporal history and are limited to " +
                $"{MaxInlineContentLengthFromWorkflow} characters. Send larger files from a message handler " +
                "(OnUserChatMessage, OnFileUpload, …) or from your own activity, where the bytes go " +
                "straight to the platform over HTTP.",
                nonRetryable: true);
        }

        return new SendFileRequest
        {
            ParticipantId = request.ParticipantId,
            WorkflowId = request.WorkflowId,
            WorkflowType = request.WorkflowType,
            RequestId = request.RequestId,
            Scope = request.Scope,
            Authorization = request.Authorization,
            Text = request.Text,
            ThreadId = request.ThreadId,
            Hint = request.Hint,
            Origin = request.Origin,
            TaskId = request.TaskId,
            TenantId = request.TenantId,
            Files = files
        };
    }
}


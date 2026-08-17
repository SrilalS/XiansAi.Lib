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
    /// </summary>
    /// <remarks>
    /// Outside workflow code this is a single service call: upload then post, over HTTP.
    /// <para>
    /// From workflow code the upload and the outbound message run as two separate activities. The
    /// upload result is recorded in workflow history, so when posting the message fails and Temporal
    /// retries, the already-stored bytes are reused rather than uploaded a second time. A combined
    /// activity would leave an orphaned copy of every byte behind on each attempt.
    /// </para>
    /// </remarks>
    public async Task SendFileAsync(SendFileRequest request)
    {
        if (!Workflow.InWorkflow)
        {
            await ExecuteAsync(
                act => act.SendFileAsync(request),
                svc => svc.SendFileAsync(request),
                operationName: "SendFile");
            return;
        }

        var prepared = PrepareForActivityPayload(request);
        var options = MessageActivityOptions.GetStandardOptions(request.WorkflowType);

        var toPost = prepared;
        if (prepared.Files.Any(file => string.IsNullOrEmpty(file.FileId)))
        {
            var references = await ExecuteAsync(
                act => act.UploadFilesAsync(prepared),
                svc => svc.UploadFilesAsync(prepared),
                options,
                operationName: "UploadFiles");

            toPost = WithFiles(prepared, references);
        }

        await ExecuteAsync(
            act => act.SendFileAsync(toPost),
            svc => svc.SendFileAsync(toPost),
            options,
            operationName: "SendFile");
    }

    /// <summary>
    /// Returns a copy of the request that is safe to serialize into workflow history: files the
    /// platform already stores travel as references (their bytes would be re-uploaded for nothing),
    /// and the remaining inline content is bounded.
    /// </summary>
    /// <remarks>
    /// Also runs the full attachment validation. Doing it here, in workflow code, turns bad input
    /// into an immediate workflow failure with an actionable message. Left to the activity it would
    /// instead burn the whole retry budget re-attempting a send that can never succeed.
    /// </remarks>
    internal static SendFileRequest PrepareForActivityPayload(SendFileRequest request)
    {
        try
        {
            FileSendService.ValidateFiles(request.Files);
        }
        catch (ArgumentException ex)
        {
            throw new ApplicationFailureException(
                ex.Message,
                errorType: nameof(ArgumentException),
                nonRetryable: true);
        }

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

        return WithFiles(request, files);
    }

    private static SendFileRequest WithFiles(SendFileRequest request, IReadOnlyList<UploadedFile> files)
    {
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


using Xians.Lib.Agents.Messaging;
using Xians.Lib.Common;

namespace Xians.Lib.Agents.Core;

/// <summary>
/// Helper for proactive user messaging operations.
/// For A2A (Agent-to-Agent) communication, use XiansContext.A2A instead.
/// </summary>
public class MessagingHelper
{

    /// <summary>
    /// Sends a chat message to a participant from the current workflow.
    /// If participantId is not provided, uses the participant ID from the current workflow context.
    /// Wrapper around UserMessaging.SendChatAsync for convenience.
    /// </summary>
    /// <param name="text">The message text to send.</param>
    /// <param name="data">Optional data object to send with the message.</param>
    /// <param name="scope">Optional scope for the message.</param>
    /// <param name="hint">Optional hint for message processing.</param>
    /// <param name="taskId">Optional task ID to associate with the message.</param>
    /// <param name="participantId">Optional participant (user) ID to send the message to. If null, uses the current workflow context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not in workflow or activity context.</exception>
    public async Task SendChatAsync(
        string text, 
        object? data = null, 
        string? scope = null, 
        string? hint = null,
        string? taskId = null,
        string? participantId = null)
    {
        participantId ??= XiansContext.GetParticipantId();
        await UserMessaging.SendChatAsync(participantId, text, data, scope, hint, taskId);
    }

    /// <summary>
    /// Sends a data message to a participant from the current workflow.
    /// If participantId is not provided, uses the participant ID from the current workflow context.
    /// Wrapper around UserMessaging.SendDataAsync for convenience.
    /// </summary>
    /// <param name="text">The text content to accompany the data.</param>
    /// <param name="data">The data object to send.</param>
    /// <param name="scope">Optional scope for the message.</param>
    /// <param name="hint">Optional hint for message processing.</param>
    /// <param name="taskId">Optional task ID to associate with the message.</param>
    /// <param name="participantId">Optional participant (user) ID to send the data to. If null, uses the current workflow context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not in workflow or activity context.</exception>
    public async Task SendDataAsync(
        string text, 
        object data, 
        string? scope = null, 
        string? hint = null,
        string? taskId = null,
        string? participantId = null)
    {
        participantId ??= XiansContext.GetParticipantId();
        await UserMessaging.SendDataAsync(participantId, text, data, scope, hint, taskId);
    }

    /// <summary>
    /// Sends one or more files to a participant from the current workflow.
    /// If participantId is not provided, uses the participant ID from the current workflow context.
    /// Wrapper around UserMessaging.SendFileAsync for convenience.
    /// </summary>
    /// <param name="files">The files to send (maximum 5). Files carrying a FileId are forwarded by reference.</param>
    /// <param name="text">Optional caption shown with the files.</param>
    /// <param name="scope">Optional scope for the message.</param>
    /// <param name="hint">Optional hint for message processing.</param>
    /// <param name="taskId">Optional task ID to associate with the message.</param>
    /// <param name="participantId">Optional participant (user) ID to send the files to. If null, uses the current workflow context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// From workflow code the send runs as a Temporal activity, so the bytes of new files are
    /// serialized into workflow history and are limited to roughly 1 MB in total. Files that already
    /// live on the platform are passed by reference and are not affected.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when not in workflow or activity context.</exception>
    public async Task SendFileAsync(
        IReadOnlyList<UploadedFile> files,
        string? text = null,
        string? scope = null,
        string? hint = null,
        string? taskId = null,
        string? participantId = null)
    {
        participantId ??= XiansContext.GetParticipantId();
        await UserMessaging.SendFileAsync(participantId, files, text, scope, hint, taskId);
    }

    /// <summary>
    /// Sends one or more files to a participant while impersonating the Supervisor Workflow, so they
    /// appear in the user's chat conversation.
    /// If participantId is not provided, uses the participant ID from the current workflow context.
    /// Convenience wrapper for UserMessaging.SendFileAsWorkflowAsync with WorkflowConstants.WorkflowTypes.Supervisor.
    /// </summary>
    /// <param name="files">The files to send (maximum 5). Files carrying a FileId are forwarded by reference.</param>
    /// <param name="text">Optional caption shown with the files.</param>
    /// <param name="scope">Optional scope for the message.</param>
    /// <param name="hint">Optional hint for message processing.</param>
    /// <param name="taskId">Optional task ID to associate with the message.</param>
    /// <param name="participantId">Optional participant (user) ID to send the files to. If null, uses the current workflow context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not in workflow or activity context.</exception>
    public async Task SendFileAsSupervisorAsync(
        IReadOnlyList<UploadedFile> files,
        string? text = null,
        string? scope = null,
        string? hint = null,
        string? taskId = null,
        string? participantId = null)
    {
        participantId ??= XiansContext.GetParticipantId();
        await UserMessaging.SendFileAsWorkflowAsync(
            WorkflowConstants.WorkflowTypes.Supervisor, participantId, files, text, scope, hint, taskId);
    }

    /// <summary>
    /// Sends a message while impersonating a different workflow.
    /// If participantId is not provided, uses the participant ID from the current workflow context.
    /// Useful for sending messages from background workflows as if they came from the main chat workflow.
    /// </summary>
    /// <param name="builtinWorkflowName">The builtinWorkflow name to impersonate (not the full workflow type).</param>
    /// <param name="text">The message text to send.</param>
    /// <param name="data">Optional data object to send with the message.</param>
    /// <param name="scope">Optional scope for the message.</param>
    /// <param name="hint">Optional hint for message processing.</param>
    /// <param name="taskId">Optional task ID to associate with the message.</param>
    /// <param name="participantId">Optional participant (user) ID to send the message to. If null, uses the current workflow context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not in workflow or activity context.</exception>
    public async Task SendChatAsWorkflowAsync(
        string builtinWorkflowName,
        string text, 
        object? data = null, 
        string? scope = null, 
        string? hint = null,
        string? taskId = null,
        string? participantId = null)
    {
        participantId ??= XiansContext.GetParticipantId();
        await UserMessaging.SendChatAsWorkflowAsync(builtinWorkflowName, participantId, text, data, scope, hint, taskId);
    }

    /// <summary>
    /// Sends a data message while impersonating a different workflow.
    /// If participantId is not provided, uses the participant ID from the current workflow context.
    /// Useful for sending data messages from background workflows as if they came from the main chat workflow.
    /// </summary>
    /// <param name="builtinWorkflowName">The builtinWorkflow name to impersonate (not the full workflow type).</param>
    /// <param name="text">The text content to accompany the data.</param>
    /// <param name="data">The data object to send.</param>
    /// <param name="scope">Optional scope for the message.</param>
    /// <param name="hint">Optional hint for message processing.</param>
    /// <param name="taskId">Optional task ID to associate with the message.</param>
    /// <param name="participantId">Optional participant (user) ID to send the data to. If null, uses the current workflow context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not in workflow or activity context.</exception>
    public async Task SendDataAsWorkflowAsync(
        string builtinWorkflowName,
        string text, 
        object data, 
        string? scope = null, 
        string? hint = null,
        string? taskId = null,
        string? participantId = null)
    {
        participantId ??= XiansContext.GetParticipantId();
        await UserMessaging.SendDataAsWorkflowAsync(builtinWorkflowName, participantId, text, data, scope, hint, taskId);
    }

    /// <summary>
    /// Sends a message to a participant while impersonating the Supervisor Workflow.
    /// If participantId is not provided, uses the participant ID from the current workflow context.
    /// Convenience wrapper for SendChatAsWorkflowAsync with WorkflowConstants.WorkflowTypes.Supervisor.
    /// </summary>
    /// <param name="text">The message text to send.</param>
    /// <param name="data">Optional data object to send with the message.</param>
    /// <param name="scope">Optional scope for the message.</param>
    /// <param name="hint">Optional hint for message processing.</param>
    /// <param name="taskId">Optional task ID to associate with the message.</param>
    /// <param name="participantId">Optional participant (user) ID to send the message to. If null, uses the current workflow context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not in workflow or activity context.</exception>
    public async Task SendChatAsSupervisorAsync(
        string text, 
        object? data = null, 
        string? scope = null, 
        string? hint = null,
        string? taskId = null,
        string? participantId = null)
    {
        participantId ??= XiansContext.GetParticipantId();
        await UserMessaging.SendChatAsWorkflowAsync(WorkflowConstants.WorkflowTypes.Supervisor, participantId, text, data, scope, hint, taskId);
    }

    /// <summary>
    /// Sends a reasoning message to a participant from the current workflow.
    /// If participantId is not provided, uses the participant ID from the current workflow context.
    /// Wrapper around UserMessaging.SendReasoningAsync for convenience.
    /// </summary>
    /// <param name="text">The reasoning content to send.</param>
    /// <param name="data">Optional data object to send with the message.</param>
    /// <param name="scope">Optional scope for the message.</param>
    /// <param name="hint">Optional hint for message processing.</param>
    /// <param name="taskId">Optional task ID to associate with the message.</param>
    /// <param name="participantId">Optional participant (user) ID to send the message to. If null, uses the current workflow context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not in workflow or activity context.</exception>
    public async Task SendReasoningMessageAsSupervisorAsync(
        string text,
        object? data = null,
        string? scope = null,
        string? hint = null,
        string? taskId = null,
        string? participantId = null)
    {
        participantId ??= XiansContext.GetParticipantId();
        await UserMessaging.SendReasoningAsync(WorkflowConstants.WorkflowTypes.Supervisor, participantId, text, data, scope, hint, taskId);
    }

    /// <summary>
    /// Sends a tool message to a participant from the current workflow.
    /// If participantId is not provided, uses the participant ID from the current workflow context.
    /// Wrapper around UserMessaging.SendToolAsync for convenience.
    /// </summary>
    /// <param name="text">The tool message content to send.</param>
    /// <param name="data">Optional data object to send with the message.</param>
    /// <param name="scope">Optional scope for the message.</param>
    /// <param name="hint">Optional hint for message processing.</param>
    /// <param name="taskId">Optional task ID to associate with the message.</param>
    /// <param name="participantId">Optional participant (user) ID to send the message to. If null, uses the current workflow context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not in workflow or activity context.</exception>
    public async Task SendToolCallMessageAsSupervisorAsync(
        string text,
        object? data = null,
        string? scope = null,
        string? hint = null,
        string? taskId = null,
        string? participantId = null)
    {
        participantId ??= XiansContext.GetParticipantId();
        await UserMessaging.SendToolCallAsync(WorkflowConstants.WorkflowTypes.Supervisor, participantId, text, data, scope, hint, taskId);
    }

    /// <summary>
    /// Sends a data message to a participant while impersonating the Supervisor Workflow.
    /// If participantId is not provided, uses the participant ID from the current workflow context.
    /// Convenience wrapper for SendDataAsWorkflowAsync with WorkflowConstants.WorkflowTypes.Supervisor.
    /// </summary>
    /// <param name="text">The text content to accompany the data.</param>
    /// <param name="data">The data object to send.</param>
    /// <param name="scope">Optional scope for the message.</param>
    /// <param name="hint">Optional hint for message processing.</param>
    /// <param name="taskId">Optional task ID to associate with the message.</param>
    /// <param name="participantId">Optional participant (user) ID to send the data to. If null, uses the current workflow context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not in workflow or activity context.</exception>
    public async Task SendDataAsSupervisorAsync(
        string text, 
        object data, 
        string? scope = null, 
        string? hint = null,
        string? taskId = null,
        string? participantId = null)
    {
        participantId ??= XiansContext.GetParticipantId();
        await UserMessaging.SendDataAsWorkflowAsync(WorkflowConstants.WorkflowTypes.Supervisor, participantId, text, data, scope, hint, taskId);
    }
}


using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xians.Lib.Common;
using Xians.Lib.Temporal.Workflows.Messaging.Models;

namespace Xians.Lib.Agents.Messaging;

/// <summary>
/// Uploads file bytes to the platform (GridFS) then posts an outbound File message with references only.
/// Runs on the HTTP path only: either directly from a handler or inside the SendFile activity.
/// </summary>
internal class FileSendService
{
    private const int MaxFiles = 5;
    private const long MaxFileSizeBytes = 10L * 1024 * 1024;
    private const long MaxTotalSizeBytes = 20L * 1024 * 1024;
    private const string DefaultContentType = "application/octet-stream";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly MessageService _messageService;

    public FileSendService(HttpClient httpClient, ILogger logger, MessageService? messageService = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _messageService = messageService ?? new MessageService(httpClient, logger);
    }

    public async Task SendAsync(SendFileRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFiles(request.Files);

        var refs = await ResolveReferencesAsync(request, cancellationToken);

        var sendRequest = new SendMessageRequest
        {
            ParticipantId = request.ParticipantId,
            WorkflowId = request.WorkflowId,
            WorkflowType = request.WorkflowType,
            RequestId = request.RequestId,
            Scope = request.Scope,
            Authorization = request.Authorization,
            Text = request.Text ?? string.Empty,
            ThreadId = request.ThreadId,
            Hint = request.Hint,
            Origin = request.Origin,
            TaskId = request.TaskId,
            TenantId = request.TenantId,
            Type = "File",
            Data = new { files = refs }
        };

        await _messageService.SendAsync(sendRequest, cancellationToken);
    }

    private static void ValidateFiles(IReadOnlyList<UploadedFile>? files)
    {
        if (files == null || files.Count == 0)
        {
            throw new ArgumentException("files must be a non-empty array", nameof(files));
        }

        if (files.Count > MaxFiles)
        {
            throw new ArgumentException($"A maximum of {MaxFiles} files can be sent per message", nameof(files));
        }

        long totalBytes = 0;
        foreach (var file in files)
        {
            if (file == null)
            {
                throw new ArgumentException("Each file must be a valid UploadedFile", nameof(files));
            }

            if (string.IsNullOrWhiteSpace(file.FileName))
            {
                throw new ArgumentException("Each file must include a fileName", nameof(files));
            }

            if (string.IsNullOrEmpty(file.FileId) && string.IsNullOrEmpty(file.Content))
            {
                throw new ArgumentException("Each file must include content or a fileId", nameof(files));
            }

            var bytes = EstimateSize(file);
            if (bytes > MaxFileSizeBytes)
            {
                throw new ArgumentException($"File \"{file.FileName}\" exceeds the 10MB per-file limit", nameof(files));
            }

            totalBytes += bytes;
        }

        if (totalBytes > MaxTotalSizeBytes)
        {
            throw new ArgumentException("Combined attachments exceed the 20MB per-message limit", nameof(files));
        }
    }

    private static long EstimateSize(UploadedFile file)
    {
        if (!string.IsNullOrEmpty(file.Content))
        {
            return EstimateBase64Bytes(file.Content);
        }

        return file.FileSize ?? 0;
    }

    private static long EstimateBase64Bytes(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return 0;
        var length = base64.Length;
        var padding = base64.EndsWith("==") ? 2 : base64.EndsWith("=") ? 1 : 0;
        return (length * 3L / 4L) - padding;
    }

    private async Task<List<OutboundFileRef>> ResolveReferencesAsync(
        SendFileRequest request,
        CancellationToken cancellationToken)
    {
        var files = request.Files;
        var refs = new OutboundFileRef[files.Count];
        var pendingIndexes = new List<int>();
        var pendingUploads = new List<UploadFilePayload>();

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            if (!string.IsNullOrEmpty(file.FileId))
            {
                refs[i] = ToRef(file);
                continue;
            }

            pendingIndexes.Add(i);
            pendingUploads.Add(new UploadFilePayload
            {
                Content = file.Content,
                FileName = file.FileName!,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? DefaultContentType : file.ContentType,
                FileSize = file.FileSize ?? EstimateBase64Bytes(file.Content)
            });
        }

        if (pendingUploads.Count > 0)
        {
            var uploaded = await UploadAsync(request.ParticipantId, request.TenantId, pendingUploads, cancellationToken);
            if (uploaded.Count != pendingUploads.Count)
            {
                throw new HttpRequestException(
                    $"File upload returned {uploaded.Count} references but {pendingUploads.Count} files were sent.");
            }

            for (var i = 0; i < pendingIndexes.Count; i++)
            {
                refs[pendingIndexes[i]] = uploaded[i];
            }
        }

        return refs.ToList();
    }

    private async Task<List<OutboundFileRef>> UploadAsync(
        string participantId,
        string tenantId,
        List<UploadFilePayload> files,
        CancellationToken cancellationToken)
    {
        var endpoint = WorkflowConstants.ApiEndpoints.Files;
        _logger.LogDebug("Uploading {Count} file(s) to {Endpoint}", files.Count, endpoint);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = JsonContent.Create(new { participantId, files });
        if (!string.IsNullOrEmpty(tenantId))
        {
            httpRequest.Headers.TryAddWithoutValidation(WorkflowConstants.Headers.TenantId, tenantId);
        }

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "File upload failed: StatusCode={StatusCode}, Error={Error}",
                response.StatusCode,
                body);
            throw new HttpRequestException($"Failed to upload files. Status: {response.StatusCode}");
        }

        var parsed = JsonSerializer.Deserialize<UploadFilesResponse>(body, JsonOptions);
        if (parsed?.Files == null || parsed.Files.Count == 0)
        {
            throw new HttpRequestException("File upload succeeded but returned no file references.");
        }

        return parsed.Files;
    }

    private static OutboundFileRef ToRef(UploadedFile file)
    {
        return new OutboundFileRef
        {
            FileId = file.FileId!,
            FileName = file.FileName ?? "uploaded-file",
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? DefaultContentType : file.ContentType,
            FileSize = file.FileSize ?? 0
        };
    }

    private sealed class UploadFilePayload
    {
        public required string Content { get; set; }
        public required string FileName { get; set; }
        public required string ContentType { get; set; }
        public long FileSize { get; set; }
    }

    private sealed class UploadFilesResponse
    {
        public List<OutboundFileRef> Files { get; set; } = new();
    }

    private sealed class OutboundFileRef
    {
        public required string FileId { get; set; }
        public required string FileName { get; set; }
        public string ContentType { get; set; } = DefaultContentType;
        public long FileSize { get; set; }
    }
}

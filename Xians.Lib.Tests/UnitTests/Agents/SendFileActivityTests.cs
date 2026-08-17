using System.Net;
using System.Text;
using System.Text.Json;
using Temporalio.Converters;
using Temporalio.Exceptions;
using Temporalio.Testing;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Temporal.Workflows.Messaging;
using Xians.Lib.Temporal.Workflows.Messaging.Models;

namespace Xians.Lib.Tests.UnitTests.Agents;

/// <summary>
/// Unit tests for sending files from workflow code, where the send is routed through the
/// SendFile Temporal activity instead of a direct HTTP call.
///
/// dotnet test --filter "FullyQualifiedName~SendFileActivityTests"
/// </summary>
public class SendFileActivityTests
{
    [Fact]
    public async Task Request_SurvivesTemporalPayloadRoundTrip()
    {
        var bytes = Encoding.UTF8.GetBytes("hello from workflow");
        var request = CreateRequest(UploadedFile.FromBytes(bytes, "hello.txt", "text/plain"));

        var roundTripped = await RoundTripAsync(request);

        Assert.Equal("user@example.com", roundTripped.ParticipantId);
        Assert.Equal("test-tenant", roundTripped.TenantId);
        Assert.Equal("Here is the file", roundTripped.Text);

        var file = Assert.Single(roundTripped.Files);
        Assert.Equal(Convert.ToBase64String(bytes), file.Content);
        Assert.Equal("hello.txt", file.FileName);
        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal(bytes.Length, file.FileSize);
        Assert.Null(file.FileId);
    }

    [Fact]
    public async Task Request_SurvivesTemporalPayloadRoundTrip_ForReferenceOnlyFiles()
    {
        var request = CreateRequest(new UploadedFile(null, "stored.pdf", "application/pdf", 42, "grid-1"));

        var file = Assert.Single((await RoundTripAsync(request)).Files);

        Assert.Equal("grid-1", file.FileId);
        Assert.Equal(string.Empty, file.Content);
        Assert.True(file.IsReference);
    }

    [Fact]
    public void PrepareForActivityPayload_DropsBytesOfAlreadyStoredFiles()
    {
        var stored = new UploadedFile(
            content: Convert.ToBase64String(Encoding.UTF8.GetBytes("already uploaded")),
            fileName: "stored.pdf",
            contentType: "application/pdf",
            fileSize: 16,
            fileId: "grid-1");

        var prepared = MessageActivityExecutor.PrepareForActivityPayload(CreateRequest(stored));

        var file = Assert.Single(prepared.Files);
        Assert.Equal("grid-1", file.FileId);
        Assert.Equal(string.Empty, file.Content);
        Assert.Equal("stored.pdf", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(16, file.FileSize);
    }

    [Fact]
    public void PrepareForActivityPayload_KeepsBytesOfNewFiles_AndCopiesRouting()
    {
        var bytes = Encoding.UTF8.GetBytes("fresh bytes");
        var request = CreateRequest(UploadedFile.FromBytes(bytes, "fresh.txt", "text/plain"));
        request.ThreadId = "thread-9";
        request.TaskId = "task-9";

        var prepared = MessageActivityExecutor.PrepareForActivityPayload(request);

        Assert.Equal(Convert.ToBase64String(bytes), Assert.Single(prepared.Files).Content);
        Assert.Equal(request.WorkflowId, prepared.WorkflowId);
        Assert.Equal(request.WorkflowType, prepared.WorkflowType);
        Assert.Equal(request.RequestId, prepared.RequestId);
        Assert.Equal(request.TenantId, prepared.TenantId);
        Assert.Equal("thread-9", prepared.ThreadId);
        Assert.Equal("task-9", prepared.TaskId);
    }

    [Fact]
    public void PrepareForActivityPayload_OversizeInlineContent_FailsWorkflowNonRetryably()
    {
        var oversized = new UploadedFile(new string('A', 1_500_001), "huge.bin", "application/octet-stream", null, null);

        var ex = Assert.Throws<ApplicationFailureException>(
            () => MessageActivityExecutor.PrepareForActivityPayload(CreateRequest(oversized)));

        Assert.True(ex.NonRetryable);
        Assert.Contains("workflow code", ex.Message);
    }

    [Fact]
    public void PrepareForActivityPayload_OversizeContentOfStoredFiles_IsAllowed()
    {
        var stored = new UploadedFile(new string('A', 1_500_001), "huge.bin", "application/octet-stream", null, "grid-1");

        var prepared = MessageActivityExecutor.PrepareForActivityPayload(CreateRequest(stored));

        Assert.Equal(string.Empty, Assert.Single(prepared.Files).Content);
    }

    [Fact]
    public async Task Activity_UploadsThenPostsOutboundFileMessage()
    {
        var bytes = Encoding.UTF8.GetBytes("hello from workflow");
        var handler = new RecordingHandler((request, _) =>
        {
            if (IsUpload(request))
            {
                return Json(new
                {
                    files = new[]
                    {
                        new { fileId = "grid-1", fileName = "hello.txt", contentType = "text/plain", fileSize = bytes.Length }
                    }
                });
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"thread-1\"") };
        });

        var activities = new MessageActivities(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var request = CreateRequest(UploadedFile.FromBytes(bytes, "hello.txt", "text/plain"));

        await new ActivityEnvironment().RunAsync(() => activities.SendFileAsync(request));

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(IsUpload(handler.Requests[0].Request));
        Assert.True(IsOutboundFile(handler.Requests[1].Request));

        using var outbound = JsonDocument.Parse(handler.Requests[1].Body);
        var sentFile = outbound.RootElement.GetProperty("data").GetProperty("files")[0];
        Assert.Equal("grid-1", sentFile.GetProperty("fileId").GetString());
        Assert.False(sentFile.TryGetProperty("content", out _));
    }

    private static async Task<SendFileRequest> RoundTripAsync(SendFileRequest request)
    {
        var payload = await DataConverter.Default.ToPayloadAsync(request);
        return await DataConverter.Default.ToValueAsync<SendFileRequest>(payload);
    }

    private static SendFileRequest CreateRequest(params UploadedFile[] files)
    {
        return new SendFileRequest
        {
            ParticipantId = "user@example.com",
            WorkflowId = "test-tenant:FileAgent:Supervisor Workflow:act-1",
            WorkflowType = "FileAgent:Supervisor Workflow",
            RequestId = "req-1",
            TenantId = "test-tenant",
            Text = "Here is the file",
            Files = files
        };
    }

    private static bool IsUpload(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.TrimEnd('/').Equals("/api/agent/files", StringComparison.OrdinalIgnoreCase);

    private static bool IsOutboundFile(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.Contains("/outbound/file", StringComparison.OrdinalIgnoreCase);

    private static HttpResponseMessage Json(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return _responder(request, body);
        }
    }
}

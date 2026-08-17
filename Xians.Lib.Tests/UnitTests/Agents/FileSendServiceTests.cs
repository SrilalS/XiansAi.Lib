using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Common;
using Xians.Lib.Common.Models;
using Xians.Lib.Temporal.Workflows.Messaging.Models;

namespace Xians.Lib.Tests.UnitTests.Agents;

/// <summary>
/// Unit tests for agent → user file send orchestration (upload then outbound File message).
///
/// dotnet test --filter "FullyQualifiedName~FileSendServiceTests"
/// </summary>
public class FileSendServiceTests
{
    [Fact]
    public async Task SendAsync_UploadsThenPostsOutboundWithRefsOnly()
    {
        var bytes = Encoding.UTF8.GetBytes("hello from agent");
        var file = UploadedFile.FromBytes(bytes, "hello.txt", "text/plain");
        var handler = new RecordingHandler((request, body) =>
        {
            if (IsUpload(request))
            {
                return Json(HttpStatusCode.OK, new
                {
                    files = new[]
                    {
                        new { fileId = "grid-1", fileName = "hello.txt", contentType = "text/plain", fileSize = bytes.Length }
                    }
                });
            }

            if (IsOutboundFile(request))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("\"thread-1\"")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);
        await service.SendAsync(CreateRequest(file));

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(IsUpload(handler.Requests[0].Request));
        Assert.True(IsOutboundFile(handler.Requests[1].Request));

        using var uploadDoc = JsonDocument.Parse(handler.Requests[0].Body);
        var uploaded = uploadDoc.RootElement.GetProperty("files")[0];
        Assert.Equal(Convert.ToBase64String(bytes), uploaded.GetProperty("content").GetString());
        Assert.Equal("hello.txt", uploaded.GetProperty("fileName").GetString());
        Assert.Equal("user@example.com", uploadDoc.RootElement.GetProperty("participantId").GetString());
        Assert.Equal("test-tenant", handler.Requests[0].Request.Headers.GetValues(WorkflowConstants.Headers.TenantId).Single());

        using var outboundDoc = JsonDocument.Parse(handler.Requests[1].Body);
        var outboundFile = outboundDoc.RootElement.GetProperty("data").GetProperty("files")[0];
        Assert.Equal("grid-1", outboundFile.GetProperty("fileId").GetString());
        Assert.Equal("hello.txt", outboundFile.GetProperty("fileName").GetString());
        Assert.False(outboundFile.TryGetProperty("content", out _));
        Assert.Equal("Here is the file", outboundDoc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendAsync_WhenFileIdSet_SkipsUpload()
    {
        var file = new UploadedFile(
            content: Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            fileName: "already-stored.pdf",
            contentType: "application/pdf",
            fileSize: 3,
            fileId: "existing-id");

        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("\"thread-1\"")
        });

        await CreateService(handler).SendAsync(CreateRequest(file));

        Assert.Single(handler.Requests);
        Assert.True(IsOutboundFile(handler.Requests[0].Request));
        Assert.Contains("existing-id", handler.Requests[0].Body);
        Assert.DoesNotContain("\"content\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task SendAsync_PreservesOrder_WhenMixingExistingAndNewFiles()
    {
        var first = new UploadedFile(null, "a.pdf", "application/pdf", 10, "id-a");
        var second = UploadedFile.FromBytes(Encoding.UTF8.GetBytes("new"), "b.txt", "text/plain");
        var third = new UploadedFile(null, "c.pdf", "application/pdf", 10, "id-c");

        var handler = new RecordingHandler((request, _) =>
        {
            if (IsUpload(request))
            {
                return Json(HttpStatusCode.OK, new
                {
                    files = new[]
                    {
                        new { fileId = "id-b", fileName = "b.txt", contentType = "text/plain", fileSize = 3 }
                    }
                });
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"t\"") };
        });

        await CreateService(handler).SendAsync(CreateRequest(first, second, third));

        using var outboundDoc = JsonDocument.Parse(handler.Requests.Last().Body);
        var files = outboundDoc.RootElement.GetProperty("data").GetProperty("files");
        Assert.Equal(3, files.GetArrayLength());
        Assert.Equal("id-a", files[0].GetProperty("fileId").GetString());
        Assert.Equal("id-b", files[1].GetProperty("fileId").GetString());
        Assert.Equal("id-c", files[2].GetProperty("fileId").GetString());
    }

    [Fact]
    public async Task SendAsync_EmptyList_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService(new RecordingHandler()).SendAsync(CreateRequest()));
        Assert.Contains("non-empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_MoreThanFiveFiles_Throws()
    {
        var files = Enumerable.Range(0, 6)
            .Select(i => UploadedFile.FromBytes(new byte[] { 1 }, $"f{i}.bin"))
            .ToArray();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService(new RecordingHandler()).SendAsync(CreateRequest(files)));
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public async Task SendAsync_MissingFileName_Throws()
    {
        var file = new UploadedFile(Convert.ToBase64String(new byte[] { 1 }), fileName: null, "text/plain", 1, null);
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService(new RecordingHandler()).SendAsync(CreateRequest(file)));
        Assert.Contains("fileName", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A file name is frequently one the agent did not choose (forwarding a client upload), and it
    /// travels to the storage API and into every client that renders the outbound message.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config")]
    [InlineData("reports/quarterly.pdf")]
    [InlineData("reports\\quarterly.pdf")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("report\r\nContent-Type: text/html")]
    [InlineData("report\0.pdf")]
    public async Task SendAsync_UnsafeFileName_ThrowsWithoutUploading(string fileName)
    {
        var file = new UploadedFile(Convert.ToBase64String(new byte[] { 1 }), fileName, "application/pdf", 1, null);
        var handler = new RecordingHandler();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService(handler).SendAsync(CreateRequest(file)));

        Assert.Contains("not valid", ex.Message);
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// The rules must not depend on the operating system the worker happens to run on, so they are
    /// not derived from Path.GetInvalidFileNameChars.
    /// </summary>
    [Theory]
    [InlineData("quarterly report (final).pdf")]
    [InlineData("réport-2026_v2.pdf")]
    [InlineData("data:snapshot?v=2*.csv")]
    [InlineData("archive.tar.gz")]
    public void ValidateFileName_AllowsPlainNames(string fileName)
    {
        FileSendService.ValidateFileName(fileName, "files");
    }

    [Fact]
    public async Task SendAsync_OverlongFileName_Throws()
    {
        var fileName = new string('a', FileSendService.MaxFileNameLength + 1) + ".pdf";
        var file = new UploadedFile(Convert.ToBase64String(new byte[] { 1 }), fileName, "application/pdf", 1, null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService(new RecordingHandler()).SendAsync(CreateRequest(file)));
        Assert.Contains(FileSendService.MaxFileNameLength.ToString(), ex.Message);
    }

    [Fact]
    public async Task SendAsync_ReferencedFileWithUnsafeName_Throws()
    {
        var file = new UploadedFile(null, "../../escape.pdf", "application/pdf", 10, "existing-id");

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService(new RecordingHandler()).SendAsync(CreateRequest(file)));
    }

    /// <summary>
    /// Control characters would otherwise let a crafted name forge extra lines in any log the
    /// exception is written to.
    /// </summary>
    [Fact]
    public void ValidateFileName_DoesNotEchoControlCharacters()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => FileSendService.ValidateFileName("report\r\nFAKE LOG LINE.pdf", "files"));

        Assert.DoesNotContain("\r", ex.Message);
        Assert.DoesNotContain("\n", ex.Message);
        Assert.Contains("FAKE LOG LINE.pdf", ex.Message);
    }

    [Fact]
    public void FromBytes_UnsafeFileName_ThrowsAtConstruction()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => UploadedFile.FromBytes([1, 2, 3], "../../etc/passwd", "application/pdf"));
        Assert.Equal("fileName", ex.ParamName);
    }

    [Fact]
    public async Task SendAsync_OversizeFile_Throws()
    {
        var oversized = new UploadedFile(new string('A', 13_981_016), "huge.bin", "application/octet-stream", null, null);
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService(new RecordingHandler()).SendAsync(CreateRequest(oversized)));
        Assert.Contains("10MB", ex.Message);
    }

    [Fact]
    public async Task SendAsync_CombinedSizeOverLimit_Throws()
    {
        var files = Enumerable.Range(0, 3)
            .Select(i => new UploadedFile(null, $"f{i}.bin", "application/octet-stream", 8L * 1024 * 1024, $"id-{i}"))
            .ToArray();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService(new RecordingHandler()).SendAsync(CreateRequest(files)));
        Assert.Contains("20MB", ex.Message);
    }

    [Fact]
    public void MessageType_File_IsValid()
    {
        Assert.True(MessageTypeExtensions.IsValidMessageType("File"));
        Assert.True(MessageTypeExtensions.IsValidMessageType("Tool"));
        Assert.Equal(MessageType.File, MessageTypeExtensions.ParseMessageType("file"));
    }

    private static FileSendService CreateService(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        return new FileSendService(httpClient, NullLogger.Instance);
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
        request.Method == HttpMethod.Post
        && request.RequestUri!.AbsolutePath.TrimEnd('/').Equals("/api/agent/files", StringComparison.OrdinalIgnoreCase);

    private static bool IsOutboundFile(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post
        && request.RequestUri!.AbsolutePath.Contains("/outbound/file", StringComparison.OrdinalIgnoreCase);

    private static HttpResponseMessage Json(HttpStatusCode status, object payload) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, string, HttpResponseMessage>? responder = null)
        {
            _responder = responder ?? ((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
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

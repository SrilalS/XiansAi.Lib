using Moq;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Http;
using Xians.Lib.Temporal;

namespace Xians.Lib.Tests.UnitTests.Agents;

/// <summary>
/// ReplyAsync(text, files) should send one File message (caption + attachments), not a Chat then a File.
///
/// dotnet test --filter "FullyQualifiedName~UserMessageContextReplyFileTests"
/// </summary>
[Collection("Sequential")]
public class UserMessageContextReplyFileTests : IDisposable
{
    public UserMessageContextReplyFileTests()
    {
        XiansContext.CleanupForTests();

        var http = new HttpClient { BaseAddress = new Uri("http://localhost") };
        var httpService = new Mock<IHttpClientService>();
        httpService.Setup(x => x.Client).Returns(http);
        var temporal = new Mock<ITemporalClientService>();
        temporal.Setup(x => x.IsConnectionHealthy()).Returns(true);

        _ = new XiansAgent(
            "test-agent",
            false,
            null, null, null, null, null, null, null,
            temporal.Object,
            httpService.Object,
            new XiansOptions
            {
                ApiKey = TestUtilities.TestCertificateGenerator.GenerateTestCertificateBase64("test-tenant", "test-user"),
                ServerUrl = "http://localhost",
                LocalMode = true
            },
            null);
    }

    public void Dispose() => XiansContext.CleanupForTests();

    [Fact]
    public async Task ReplyAsync_WithFile_SendsFileWithCaption()
    {
        var context = new RecordingContext();
        var file = UploadedFile.FromBytes([1, 2, 3], "schedule.pdf", "application/pdf");

        await context.ReplyAsync("Here is the schedule.", file);

        Assert.Null(context.ChatText);
        Assert.Equal("Here is the schedule.", context.FileText);
        Assert.Single(context.Files!);
        Assert.Equal("schedule.pdf", context.Files![0].FileName);
    }

    [Fact]
    public async Task ReplyAsync_WithFileList_SendsFilesWithCaption()
    {
        var context = new RecordingContext();
        var files = new[]
        {
            UploadedFile.FromBytes([1], "a.pdf", "application/pdf"),
            UploadedFile.FromBytes([2], "b.pdf", "application/pdf")
        };

        await context.ReplyAsync("Two reports.", files);

        Assert.Null(context.ChatText);
        Assert.Equal("Two reports.", context.FileText);
        Assert.Equal(2, context.Files!.Count);
    }

    [Fact]
    public async Task ReplyAsync_WithNullOrEmptyFiles_SendsChatOnly()
    {
        var context = new RecordingContext();

        await context.ReplyAsync("Just text.", files: null);
        Assert.Equal("Just text.", context.ChatText);
        Assert.Null(context.Files);

        context.Reset();
        await context.ReplyAsync("Still just text.", Array.Empty<UploadedFile>());
        Assert.Equal("Still just text.", context.ChatText);
        Assert.Null(context.Files);
    }

    private sealed class RecordingContext : UserMessageContext
    {
        public string? ChatText { get; private set; }
        public string? FileText { get; private set; }
        public IReadOnlyList<UploadedFile>? Files { get; private set; }

        public RecordingContext()
            : base("hi", "user@example.com", "req-1", null, null, null, "test-tenant")
        {
        }

        public void Reset()
        {
            ChatText = null;
            FileText = null;
            Files = null;
        }

        public override Task ReplyAsync(string text)
        {
            ChatText = text;
            return Task.CompletedTask;
        }

        public override Task SendFileAsync(IReadOnlyList<UploadedFile> files, string? text = null)
        {
            Files = files;
            FileText = text;
            return Task.CompletedTask;
        }
    }
}

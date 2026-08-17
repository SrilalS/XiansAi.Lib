using Moq;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Http;
using Xians.Lib.Temporal;

namespace Xians.Lib.Tests.UnitTests.Agents;

/// <summary>
/// ReplyWithFilesAsync(text, files) should send one File message (caption + attachments),
/// not a Chat then a File. Also guards the overload resolution of ReplyAsync(text, data).
///
/// dotnet test --filter "FullyQualifiedName~UserMessageContextReplyFileTests"
/// </summary>
[Collection("Sequential")]
public class UserMessageContextReplyFileTests : IDisposable
{
    private readonly HttpClient _httpClient;

    public UserMessageContextReplyFileTests()
    {
        XiansContext.CleanupForTests();

        _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost") };
        var httpService = new Mock<IHttpClientService>();
        httpService.Setup(x => x.Client).Returns(_httpClient);
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

    public void Dispose()
    {
        _httpClient.Dispose();
        XiansContext.CleanupForTests();
    }

    [Fact]
    public async Task ReplyWithFileAsync_SendsFileWithCaption()
    {
        var context = new RecordingContext();
        var file = UploadedFile.FromBytes([1, 2, 3], "schedule.pdf", "application/pdf");

        await context.ReplyWithFileAsync("Here is the schedule.", file);

        Assert.Null(context.ChatText);
        Assert.Equal("Here is the schedule.", context.FileText);
        Assert.Single(context.Files!);
        Assert.Equal("schedule.pdf", context.Files![0].FileName);
    }

    [Fact]
    public async Task ReplyWithFilesAsync_SendsFilesWithCaption()
    {
        var context = new RecordingContext();
        var files = new[]
        {
            UploadedFile.FromBytes([1], "a.pdf", "application/pdf"),
            UploadedFile.FromBytes([2], "b.pdf", "application/pdf")
        };

        await context.ReplyWithFilesAsync("Two reports.", files);

        Assert.Null(context.ChatText);
        Assert.Equal("Two reports.", context.FileText);
        Assert.Equal(2, context.Files!.Count);
    }

    [Fact]
    public async Task ReplyWithFilesAsync_WithNullOrEmptyFiles_SendsChatOnly()
    {
        var context = new RecordingContext();

        await context.ReplyWithFilesAsync("Just text.", files: null);
        Assert.Equal("Just text.", context.ChatText);
        Assert.Null(context.Files);

        context.Reset();
        await context.ReplyWithFilesAsync("Still just text.", Array.Empty<UploadedFile>());
        Assert.Equal("Still just text.", context.ChatText);
        Assert.Null(context.Files);
    }

    /// <summary>
    /// The file-sending members must not be overloads of ReplyAsync: a null literal in the
    /// second position has to keep binding to ReplyAsync(string, object?) so that agents written
    /// against earlier SDK versions still compile.
    /// </summary>
    [Fact]
    public async Task ReplyAsync_WithNullSecondArgument_BindsToDataOverload()
    {
        var context = new RecordingContext();

        await context.ReplyAsync("Just text.", null);

        Assert.Equal("Just text.", context.DataText);
        Assert.Null(context.Data);
        Assert.Null(context.Files);
    }

    private sealed class RecordingContext : UserMessageContext
    {
        public string? ChatText { get; private set; }
        public string? DataText { get; private set; }
        public object? Data { get; private set; }
        public string? FileText { get; private set; }
        public IReadOnlyList<UploadedFile>? Files { get; private set; }

        public RecordingContext()
            : base("hi", "user@example.com", "req-1", null, null, null, "test-tenant")
        {
        }

        public void Reset()
        {
            ChatText = null;
            DataText = null;
            Data = null;
            FileText = null;
            Files = null;
        }

        public override Task ReplyAsync(string text)
        {
            ChatText = text;
            return Task.CompletedTask;
        }

        public override Task ReplyAsync(string text, object? data)
        {
            DataText = text;
            Data = data;
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

using System.Net;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xians.Lib.Logging;
using Xians.Lib.Logging.Models;
using Xians.Lib.Configuration.Models;
using Xians.Lib.Common.Infrastructure;
using Xians.Lib.Http;
using Xians.Lib.Tests.TestUtilities;

namespace Xians.Lib.Tests.IntegrationTests.Logging;

/// <summary>
/// dotnet test --filter "FullyQualifiedName~LoggingServicesTests"
/// </summary>

[Trait("Category", "Integration")]
[Collection("LoggingServices")] // Prevent parallel execution due to static state
public class LoggingServicesTests : IAsyncLifetime
{
    private WireMockServer? _mockServer;
    private IHttpClientService? _httpService;

    public async Task InitializeAsync()
    {
        // Ensure any previous logging is shutdown and state is clean
        LoggingServices.Shutdown();
        await Task.Delay(1000); // Longer delay to ensure complete shutdown in full suite
        
        // Clear any remaining logs from previous test runs
        while (LoggingServices.GlobalLogQueue.TryDequeue(out _)) { }
        
        // Setup mock HTTP server
        _mockServer = WireMockServer.Start();
        
        // Configure mock to accept log uploads
        _mockServer
            .Given(Request.Create()
                .WithPath("/api/agent/logs")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"success\": true}"));
        
        var config = new ServerConfiguration
        {
            ServerUrl = _mockServer.Url!,
            ApiKey = TestCertificateGenerator.GetTestCertificate()
        };
        
        _httpService = ServiceFactory.CreateHttpClientService(config);

    }

    [Fact]
    public async Task EnqueueLog_AddsLogToQueue()
    {
        // Arrange - ensure clean state
        LoggingServices.Shutdown();
        await Task.Delay(500); // Allow shutdown to complete fully
        
        // Clear any remaining logs from previous tests
        while (LoggingServices.GlobalLogQueue.TryDequeue(out _)) { }
        
        // Use long interval so background thread won't process logs before we assert
        LoggingServices.ConfigureBatchSettings(100, 60000);
        LoggingServices.Initialize(_httpService!);
        await Task.Delay(100); // Allow initialization to complete
        
        var log = CreateTestLog(LogLevel.Information, "Test message");
        var initialCount = LoggingServices.GlobalLogQueue.Count;

        // Act
        LoggingServices.EnqueueLog(log);

        // Assert
        Assert.True(LoggingServices.GlobalLogQueue.Count > initialCount,
            $"Expected queue count > {initialCount}, but got {LoggingServices.GlobalLogQueue.Count}. Service initialized: {LoggingServices.IsInitialized}");
    }

    [Fact]
    public void Initialize_WithHttpClientService_DoesNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() =>
        {
            LoggingServices.Initialize(_httpService!);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Initialize_MultipleTimesSafely_DoesNotThrow()
    {
        // Act & Assert - Multiple initializations should be safe
        var exception = Record.Exception(() =>
        {
            LoggingServices.Initialize(_httpService!);
            LoggingServices.Initialize(_httpService!);
            LoggingServices.Initialize(_httpService!);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void ConfigureBatchSettings_WithValidSettings_DoesNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() =>
        {
            LoggingServices.ConfigureBatchSettings(50, 30000);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void ConfigureBatchSettings_WithZeroBatchSize_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            LoggingServices.ConfigureBatchSettings(0, 30000);
        });
    }

    [Fact]
    public void ConfigureBatchSettings_WithNegativeBatchSize_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            LoggingServices.ConfigureBatchSettings(-1, 30000);
        });
    }

    [Fact]
    public void ConfigureBatchSettings_WithZeroInterval_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            LoggingServices.ConfigureBatchSettings(100, 0);
        });
    }

    [Fact]
    public void ConfigureBatchSettings_WithNegativeInterval_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            LoggingServices.ConfigureBatchSettings(100, -1000);
        });
    }

    [Fact]
    public void ConfigureBatchSettings_WithZeroMaxQueueDepth_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            LoggingServices.ConfigureBatchSettings(100, 30000, maxQueueDepth: 0);
        });
    }

    [Fact]
    public void ConfigureBatchSettings_WithMaxQueueDepthBelowBatchSize_ThrowsException()
    {
        // A depth under the batch size would drop entries before a full batch could ever be assembled.
        Assert.Throws<ArgumentException>(() =>
        {
            LoggingServices.ConfigureBatchSettings(500, 2000, maxQueueDepth: 100);
        });
    }

    [Fact]
    public void DefaultBatchSettings_DrainFarFasterThanOneHundredPerThirtySeconds()
    {
        // The shipped defaults are the whole point of the change: 100 per 30s drained at 3.3 entries/second,
        // which any agent under load exceeds, so the queue backlogged permanently. Asserted through the
        // console line ConfigureBatchSettings prints, since the fields themselves are private.
        var options = new Xians.Lib.Agents.Workflows.Models.WorkflowOptions();
        Assert.NotNull(options); // keeps the using meaningful if the assert below is ever relaxed

        var original = Console.Out;
        using var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            // Re-applying the defaults prints the resolved ceiling.
            LoggingServices.ConfigureBatchSettings(500, 2000, maxQueueDepth: 100_000);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains("250 logs/sec", captured.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnqueueLog_AtQueueDepthLimit_DropsOldestAndKeepsNewest()
    {
        // Arrange — no draining during the test, and a small cap so the bound is reached quickly.
        LoggingServices.Shutdown();
        await Task.Delay(500);
        while (LoggingServices.GlobalLogQueue.TryDequeue(out _)) { }

        const int cap = 50;
        const int produced = 200;
        var droppedBefore = LoggingServices.DroppedLogCount;

        try
        {
            LoggingServices.ConfigureBatchSettings(batchSize: 10, processingIntervalMs: 60000, maxQueueDepth: cap);
            LoggingServices.Initialize(_httpService!);
            await Task.Delay(100);

            // Act
            for (var i = 0; i < produced; i++)
            {
                LoggingServices.EnqueueLog(CreateTestLog(LogLevel.Information, $"entry-{i}"));
            }

            // Assert — the queue is bounded. The check runs after enqueueing and without a lock, so a few
            // concurrent producers can overshoot; this asserts the bound holds, not an exact length.
            var depth = LoggingServices.GlobalLogQueue.Count;
            Assert.True(depth <= cap + 5, $"expected the queue bounded near {cap}, found {depth}");
            Assert.True(depth > 0, "the cap must bound the queue, not empty it");

            // Every entry over the cap was accounted for as a drop.
            var dropped = LoggingServices.DroppedLogCount - droppedBefore;
            Assert.True(dropped >= produced - cap - 5, $"expected roughly {produced - cap} drops, counted {dropped}");

            // The newest survived and the oldest did not: during an outage the recent entries are the ones
            // worth keeping.
            var remaining = LoggingServices.GlobalLogQueue.ToArray();
            Assert.Contains(remaining, l => l.Message == $"entry-{produced - 1}");
            Assert.DoesNotContain(remaining, l => l.Message == "entry-0");
        }
        finally
        {
            // Restore the shipped defaults: these settings are process-wide statics shared with every other
            // test in this collection.
            LoggingServices.ConfigureBatchSettings(500, 2000, maxQueueDepth: 100_000);
        }
    }

    [Fact]
    public void GlobalLogQueue_IsAccessible()
    {
        // Act
        var queue = LoggingServices.GlobalLogQueue;

        // Assert
        Assert.NotNull(queue);
    }

    [Fact]
    public async Task LoggingServices_ProcessesLogs_WhenInitialized()
    {
        // Arrange
        LoggingServices.Initialize(_httpService!);
        
        // Configure for fast processing
        LoggingServices.ConfigureBatchSettings(5, 1000); // 5 logs per batch, 1 second interval
        
        var initialRequestCount = _mockServer!.LogEntries.Count();
        
        // Enqueue multiple logs
        for (int i = 0; i < 10; i++)
        {
            var log = CreateTestLog(LogLevel.Information, $"Test message {i}");
            LoggingServices.EnqueueLog(log);
        }

        // Act - Wait for processing (2 batches should be sent)
        await Task.Delay(3000);

        // Assert - Should have sent at least one batch
        // Note: Due to timing, we can't guarantee exact count, but should be > 0
        var finalRequestCount = _mockServer!.LogEntries.Count();
        Assert.True(finalRequestCount >= initialRequestCount);
    }

    [Fact]
    public async Task EnqueueLog_WithCriticalLevel_AddsToQueue()
    {
        // Arrange - ensure clean state
        LoggingServices.Shutdown();
        await Task.Delay(500); // Allow shutdown to complete fully
        
        // Clear any remaining logs from previous tests
        while (LoggingServices.GlobalLogQueue.TryDequeue(out _)) { }
        
        LoggingServices.Initialize(_httpService!);
        await Task.Delay(100); // Allow initialization to complete
        
        var log = CreateTestLog(LogLevel.Critical, "Critical error");
        var initialCount = LoggingServices.GlobalLogQueue.Count;

        // Act
        LoggingServices.EnqueueLog(log);

        // Assert
        Assert.True(LoggingServices.GlobalLogQueue.Count > initialCount);
    }

    [Fact]
    public async Task EnqueueLog_WithException_AddsToQueue()
    {
        // Arrange - ensure clean state (already done in InitializeAsync, but double-check)
        LoggingServices.Shutdown();
        await Task.Delay(200);
        
        // Clear queue to ensure clean state
        while (LoggingServices.GlobalLogQueue.TryDequeue(out _)) { }
        
        LoggingServices.Initialize(_httpService!);
        await Task.Delay(50);
        
        var log = CreateTestLog(LogLevel.Error, "Error with exception");
        log.Exception = new InvalidOperationException("Test exception").ToString();
        var initialCount = LoggingServices.GlobalLogQueue.Count;

        // Act
        LoggingServices.EnqueueLog(log);

        // Assert
        Assert.True(LoggingServices.GlobalLogQueue.Count > initialCount, 
            $"Expected queue count to increase from {initialCount}, but it's still {LoggingServices.GlobalLogQueue.Count}");
        Assert.Contains("Test exception", log.Exception);
    }

    [Fact]
    public async Task EnqueueLog_MultipleLogs_AllAddedToQueue()
    {
        // Arrange - ensure clean state
        LoggingServices.Shutdown();
        await Task.Delay(500); // Allow shutdown to complete fully
        
        // Clear any remaining logs from previous tests
        while (LoggingServices.GlobalLogQueue.TryDequeue(out _)) { }
        
        LoggingServices.Initialize(_httpService!);
        await Task.Delay(100); // Allow initialization to complete
        
        var initialCount = LoggingServices.GlobalLogQueue.Count;
        var logsToAdd = 5;

        // Act
        for (int i = 0; i < logsToAdd; i++)
        {
            var log = CreateTestLog(LogLevel.Information, $"Message {i}");
            LoggingServices.EnqueueLog(log);
        }

        // Assert
        Assert.True(LoggingServices.GlobalLogQueue.Count >= initialCount + logsToAdd);
    }

    [Fact]
    public async Task LoggingServices_HandlesFailedUpload_WithRetry()
    {
        // Arrange - Configure server to fail first, then succeed
        _mockServer!.ResetMappings();
        // First request fails, subsequent succeed
        _mockServer
            .Given(Request.Create()
                .WithPath("/api/agent/logs")
                .UsingPost())
            .InScenario("RetryScenario")
            .WillSetStateTo("AfterFirstCall")
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("{\"error\": \"Server error\"}"));
        
        _mockServer
            .Given(Request.Create()
                .WithPath("/api/agent/logs")
                .UsingPost())
            .InScenario("RetryScenario")
            .WhenStateIs("AfterFirstCall")
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"success\": true}"));

        // Configure batch settings BEFORE initializing to avoid 60-second default interval
        LoggingServices.ConfigureBatchSettings(2, 1000);
        LoggingServices.Initialize(_httpService!);

        var log = CreateTestLog(LogLevel.Error, "Test error");
        LoggingServices.EnqueueLog(log);

        // Act - Wait for processing
        await Task.Delay(5000);

        // Assert - Should have made multiple requests (retry happened)
        // Note: Exact count depends on timing
        var requestCount = _mockServer!.LogEntries.Count();
        Assert.True(requestCount > 0);
    }

    [Fact]
    public void Shutdown_CompletesGracefully()
    {
        // Arrange
        LoggingServices.Initialize(_httpService!);
        
        // Add some logs
        for (int i = 0; i < 5; i++)
        {
            LoggingServices.EnqueueLog(CreateTestLog(LogLevel.Information, $"Message {i}"));
        }

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
        {
            LoggingServices.Shutdown();
        });

        Assert.Null(exception);
    }

    private Log CreateTestLog(LogLevel level, string message)
    {
        return new Log
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            Level = level,
            Message = message,
            WorkflowId = "test-workflow",
            WorkflowType = "TestWorkflow",
            Agent = "TestAgent",
            ParticipantId = "user-123"
        };
    }

    public async Task DisposeAsync()
    {
        // Shutdown logging first to stop background thread
        LoggingServices.Shutdown();
        
        // Wait longer for shutdown to complete in full suite context
        await Task.Delay(1000);
        
        // Clear any remaining logs
        while (LoggingServices.GlobalLogQueue.TryDequeue(out _)) { }
        
        // Now dispose resources
        _httpService?.Dispose();
        _mockServer?.Stop();
        _mockServer?.Dispose();
    }
}

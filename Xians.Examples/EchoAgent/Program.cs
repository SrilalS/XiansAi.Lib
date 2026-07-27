using Xians.Lib.Agents.Core;
using DotNetEnv;
using Microsoft.Extensions.Logging;

// Load environment variables from .env file
Env.Load();

// Get required configuration from environment variables
var serverUrl = Environment.GetEnvironmentVariable("XIANS_SERVER_URL")
    ?? throw new InvalidOperationException("XIANS_SERVER_URL environment variable is not set");

var xiansApiKey = Environment.GetEnvironmentVariable("XIANS_API_KEY")
    ?? throw new InvalidOperationException("XIANS_API_KEY environment variable is not set");

// Initialize Xians Platform
var xiansPlatform = await XiansPlatform.InitializeAsync(new()
{
    ServerUrl = serverUrl,
    ApiKey = xiansApiKey,
    ConsoleLogLevel = LogLevel.Information,
    ServerLogLevel = LogLevel.Information
});

// Register the Echo Agent
var echoAgent = xiansPlatform.Agents.Register(new()
{
    Name = "Echo Agent",
    Description = "A simple conversational agent that echoes back user messages",
    SamplePrompts = [
        "Hello, Echo Agent!",
        "How are you?",
        "Tell me something",
        "What's your name?",
        "Echo this message back to me"
    ],
    IsTemplate = true
});

Console.WriteLine($"Registered agent: {echoAgent.Name}");

// Define a conversational workflow for the agent
var conversationalWorkflow = echoAgent.Workflows.DefineSupervisor();

// Handle incoming user messages with echo response
conversationalWorkflow.OnUserChatMessage(async (context) =>
{
    var userMessage = context.Message.Text ?? string.Empty;
    
    // Echo the user message back with a prefix
    var echoResponse = $"Echo: {userMessage}";
    
    await context.ReplyAsync(echoResponse);
    
    Console.WriteLine($"User: {userMessage}");
    Console.WriteLine($"Agent: {echoResponse}");
});

// Define a webhook workflow for external integrations
var webhookWorkflow = echoAgent.Workflows.DefineIntegrator();
webhookWorkflow.OnWebhook((context) =>
{
    Console.WriteLine($"Received webhook: {context.Webhook.Name}");
    context.Respond(new { status = "success", message = "Webhook received" });
});

Console.WriteLine("Starting Echo Agent...");
Console.WriteLine("Press Ctrl+C to stop the agent.");

// Start the agent and all workflows
try
{
    await echoAgent.RunAllAsync();
}
catch (OperationCanceledException)
{
    Console.WriteLine("Echo Agent stopped.");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

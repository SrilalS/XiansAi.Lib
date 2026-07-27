# Echo Agent - Quick Start Guide

## What is the Echo Agent?

The Echo Agent is a minimal example of a conversational agent built with Xians. It demonstrates the core concepts needed to create an agent that responds to user messages.

**Key Features:**
- Simple echo response logic
- Conversational workflow setup
- Webhook support
- Easy to understand and extend

## Project Files

```
EchoAgent/
├── Program.cs           # Main entry point - agent setup and message handling
├── EchoAgent.csproj     # Project configuration and dependencies
├── .env                 # Environment configuration (not tracked in git)
├── README.md            # Detailed documentation
└── QUICKSTART.md        # This file
```

## How It Works

### Message Flow

1. **User sends a message** → Platform receives it
2. **OnUserChatMessage handler** → Triggered
3. **Echo logic** → Prefixes the message with "Echo: "
4. **Reply sent** → Message echoed back to user

### Code Walkthrough

```csharp
// Handle incoming user messages with echo response
conversationalWorkflow.OnUserChatMessage(async (context) =>
{
    var userMessage = context.Message.Text ?? string.Empty;
    var echoResponse = $"Echo: {userMessage}";
    await context.ReplyAsync(echoResponse);
});
```

This simple handler:
- Gets the user's message text from `context.Message.Text`
- Creates an echo response by prefixing it with "Echo: "
- Sends the response using `context.ReplyAsync()`

## Running the Agent

### Setup

1. **Configure environment variables** in `.env`:
   ```env
   XIANS_SERVER_URL=http://localhost:8000
   XIANS_API_KEY=your_api_key_here
   ```

2. **Navigate to the project**:
   ```bash
   cd Xians.Examples/EchoAgent
   ```

3. **Build the project**:
   ```bash
   dotnet build
   ```

### Run

```bash
dotnet run
```

Expected output:
```
Registered agent: Echo Agent
Starting Echo Agent...
Press Ctrl+C to stop the agent.
```

## Testing the Agent

Once running, you can test it by:

1. **Via the Xians UI**: Send a message through the platform's chat interface
2. **Via API**: Send a POST request to the agent's message endpoint
3. **Sample prompts**: Try the built-in sample prompts:
   - "Hello, Echo Agent!"
   - "How are you?"
   - "Tell me something"
   - "What's your name?"
   - "Echo this message back to me"

## Extending the Agent

### Add Custom Response Logic

Modify the `OnUserChatMessage` handler to add more sophisticated logic:

```csharp
conversationalWorkflow.OnUserChatMessage(async (context) =>
{
    var userMessage = context.Message.Text ?? string.Empty;
    
    // Add custom logic here
    var echoResponse = $"You said: {userMessage}";
    
    await context.ReplyAsync(echoResponse);
});
```

### Add Tools/Actions

Inject tools into the workflow:

```csharp
conversationalWorkflow.OnUserChatMessage(async (context) =>
{
    // Get timestamp
    var timestamp = DateTime.UtcNow.ToString("O");
    
    var echoResponse = $"[{timestamp}] Echo: {context.Message.Text}";
    await context.ReplyAsync(echoResponse);
});
```

### Connect to External Services

Integrate with APIs, databases, or AI models:

```csharp
conversationalWorkflow.OnUserChatMessage(async (context) =>
{
    var userMessage = context.Message.Text ?? string.Empty;
    
    // Call external API, process with ML model, query database, etc.
    // var processedResult = await ExternalService.ProcessAsync(userMessage);
    
    var echoResponse = $"Echo: {userMessage}";
    await context.ReplyAsync(echoResponse);
});
```

## Troubleshooting

### Build fails
- Ensure .NET 10.0 or later is installed: `dotnet --version`
- Check that Xians.Lib is properly referenced
- Try: `dotnet clean` then `dotnet build`

### Agent won't start
- Verify `.env` file exists and has correct credentials
- Check XIANS_SERVER_URL is accessible
- Ensure XIANS_API_KEY is valid
- Check console logs for detailed error messages

### No response received
- Verify the agent is running (check console output)
- Check that messages are being sent to the correct agent
- Review server logs for any issues

## Next Steps

1. **Study the SimpleAgent example** to see more complex patterns
2. **Add knowledge resources** - See KnowledgeAccess example
3. **Implement tools** - See LeadDiscoveryAgent for tool integration
4. **Create multi-agent workflows** - See MultiAgentOrchastration example

## Documentation

- Full documentation in [README.md](README.md)
- Xians Lib examples in `Xians.Examples/`
- Xians Platform docs: Check your server's documentation

---

Happy coding! 🚀

# Echo Agent Example

A simple conversational agent that echoes back user messages.

## Overview

The Echo Agent is a basic example of a Xians conversational agent that demonstrates:
- Agent registration with the Xians Platform
- Handling user chat messages
- Webhook integration support
- Simple echo response logic

## Features

- **Conversational Workflow**: Responds to user messages by echoing them back with a prefix
- **Webhook Support**: Accepts external webhook requests
- **Sample Prompts**: Provides example prompts to test the agent
- **Simple Architecture**: Minimal code to understand the fundamentals of agent creation

## Getting Started

### Prerequisites

1. **Xians Server**: Ensure you have a running Xians server
2. **.NET Runtime**: This project requires .NET 10.0 or later
3. **Environment Variables**: Configure the `.env` file with your server credentials

### Configuration

Update the `.env` file with your Xians Platform credentials:

```env
XIANS_SERVER_URL=http://localhost:8000  # Your Xians server URL
XIANS_API_KEY=your_api_key_here         # Your API key for the Xians platform
```

### Running the Agent

```bash
# Navigate to the project directory
cd Xians.Examples/EchoAgent

# Run the agent
dotnet run
```

The agent will start and listen for incoming messages. You should see:
```
Registered agent: Echo Agent
Starting Echo Agent...
Press Ctrl+C to stop the agent.
```

### Using the Agent

Once running, you can:

1. **Send Chat Messages**: The agent will echo back any message you send with the prefix "Echo: "
2. **Trigger Webhooks**: Send HTTP requests to the webhook endpoint
3. **View Sample Prompts**: The agent provides 5 sample prompts to get started

## Project Structure

- **Program.cs**: Main entry point that sets up and runs the agent
- **EchoAgent.csproj**: Project configuration and dependencies
- **.env**: Environment configuration file (not committed to version control)

## Architecture

The agent consists of two main workflows:

1. **Supervisor Workflow**: Handles conversational messages from users
   - Receives user input
   - Echoes the message back
   - Logs the interaction

2. **Integrator Workflow**: Handles external webhook requests
   - Accepts webhook events
   - Returns a success response

## Extending the Agent

To modify the echo behavior, edit the `OnUserChatMessage` handler in `Program.cs`:

```csharp
conversationalWorkflow.OnUserChatMessage(async (context) =>
{
    var userMessage = context.UserMessage.Content ?? string.Empty;
    var echoResponse = $"Echo: {userMessage}";
    await context.ReplyAsync(echoResponse);
});
```

## Dependencies

- **Xians.Lib**: Core Xians agent library
- **DotNetEnv**: Environment variable management

## License

Part of the Xians Platform examples.

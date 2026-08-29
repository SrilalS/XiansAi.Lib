# Temporal Worker Registration for Default Workflows

This document explains how Temporal workers are automatically created and registered when running agents with default workflows.

## Overview

When `agent.RunAllAsync()` is called on an agent that has defined built-in workflows via `DefineBuiltIn()`, the system automatically:

1. Creates Temporal client connections
2. Registers the `BuiltinWorkflow` class with Temporal workers
3. Sets up proper task queue naming based on agent scope
4. Starts the specified number of workers for each workflow
5. Handles graceful shutdown on cancellation

## Architecture

### Component Flow

```
XiansPlatform.InitializeAsync()
  ├─> Creates TemporalClientService (fetches config from server)
  ├─> Creates HttpClientService
  └─> Passes services to AgentCollection
       └─> Register() creates XiansAgent with services
            └─> XiansAgent contains WorkflowCollection
                 └─> DefineBuiltIn() creates XiansWorkflow instances
                      └─> RunAllAsync() starts Temporal workers
```

### Task Queue Naming

Task queues are named based on the agent's scope:

- **System-scoped agents**: `{WorkflowType}`
  - Example: `MyAgent : Default Workflow : Conversational`
  
- **Non-system-scoped agents**: `{TenantId}:{WorkflowType}`
  - Example: `tenant-123:MyAgent : Default Workflow : Conversational`

This naming convention matches the pattern from `XiansAi.Lib.Src/Temporal/WorkerService.cs`.

## Usage Example

```csharp
using Xians.Lib.Agents;

// Initialize platform with Temporal configuration
var xiansPlatform = await XiansPlatform.InitializeAsync(new XiansOptions
{
    ServerUrl = "https://api.example.com",
    ApiKey = "your-api-key",
    TenantId = "your-tenant-id"  // Required for non-system-scoped agents
});

// Register an agent
var agent = xiansPlatform.Agents.Register(new XiansAgentRegistration
{
    Name = "MyAgent",
    SystemScoped = false
});

// Define default workflows (these will be registered with Temporal workers)
var conversationalWorkflow = await agent.Workflows.DefineBuiltIn(
    name: "Conversational", 
    workers: 3  // Creates 3 concurrent workers
);

var webhooksWorkflow = await agent.Workflows.DefineBuiltIn(
    name: "Webhooks", 
    workers: 1
);

// Run all workflows - this creates and starts Temporal workers
// Workers will listen on their respective task queues
await agent.RunAllAsync();  // Blocks until cancelled (Ctrl+C)
```

## Configuration Options

### Temporal Configuration

You can provide Temporal configuration in two ways:

#### Option 1: Fetch from Server (Recommended)

```csharp
var options = new XiansOptions
{
    ServerUrl = "https://api.example.com",
    ApiKey = "your-api-key",
    TenantId = "your-tenant-id"
    // TemporalConfiguration is null - will be fetched from server
};
```

The system will fetch Temporal settings from `/api/agent/settings/flowserver`.

#### Option 2: Provide Explicitly

```csharp
var options = new XiansOptions
{
    ServerUrl = "https://api.example.com",
    ApiKey = "your-api-key",
    TenantId = "your-tenant-id",
    TemporalConfiguration = new TemporalConfiguration
    {
        ServerUrl = "temporal.example.com:7233",
        Namespace = "production",
        CertificateBase64 = "cert-base64-string",
        PrivateKeyBase64 = "key-base64-string"
    }
};
```

### Worker Configuration

Each workflow can be configured with a specific number of workers:

```csharp
// Single worker (default)
await agent.Workflows.DefineBuiltIn(name: "Processing", workers: 1);

// Multiple workers for concurrent processing
await agent.Workflows.DefineBuiltIn(name: "HighThroughput", workers: 10);
```

### Sticky Cache Size (`MaxCachedWorkflows`)

Each worker keeps recently-executed workflow executions in a **sticky cache**, so a task for an execution
already in the cache continues from where it left off instead of replaying its history. This is normally the
largest consumer of a worker's memory.

```csharp
agent.Workflows.DefineCustom<MyWorkflow>(new WorkflowOptions
{
    MaxConcurrent = 100,
    MaxCachedWorkflows = 500   // default
});
```

**Why the default is 500 and not Temporal's 10,000.** An agent process runs *one worker per defined
workflow*, so Temporal's per-worker default lets a handful of definitions retain tens of thousands of
executions between them — more than a container-sized memory limit allows. A cached execution is mostly
native memory owned by the SDK core, so it does not appear in a managed heap dump.

**The cache does not shrink on its own.** Eviction is LRU *by count only*: there is no TTL and no
memory-pressure eviction, and an execution terminated server-side keeps its entry until enough newer
executions push it out. So this number is what a worker's steady-state footprint is bounded by, not a
high-water mark it recovers from.

**Tuning.** Lowering it trades memory for CPU and server load — an evicted execution is replayed from
history the next time a task arrives for it. Raise it for a worker with few, long-lived workflows; lower it
for one facing many short-lived executions under a tight memory limit. Values below `MaxConcurrent` are
raised to it, since a cache smaller than the number of tasks in flight would thrash on every one of them.

## Worker Lifecycle

### Startup

1. `RunAllAsync()` is called
2. For each workflow:
   - Temporal client is obtained from `TemporalClientService`
   - Task queue name is determined based on scope
   - `TemporalWorkerOptions` is created with:
     - Task queue name
     - Logger factory
     - Workflow registration (BuiltinWorkflow for built-in workflows)
   - Workers are created and started concurrently

### Execution

- Workers run in separate tasks
- All workers execute in parallel using `Task.WhenAll()`
- Each worker listens on its designated task queue
- Workers process workflow executions as they arrive

### Shutdown

- Cancellation token triggers graceful shutdown (Ctrl+C)
- All worker tasks are cancelled
- Workers are disposed in the finally block
- Logs confirm shutdown completion

## Implementation Details

### BuiltinWorkflow Registration

For built-in workflows, the system registers the `BuiltinWorkflow` class:

```csharp
// From XiansWorkflow.RunAsync()
if (_isBuiltIn)
{
    workerOptions.AddWorkflow<BuiltinWorkflow>();
}
```

This matches the pattern from the previous library version where built-in workflows use a dynamic workflow implementation.

### Cancellation Token Handling

If no cancellation token is provided to `RunAllAsync()`, the system automatically sets up Ctrl+C handling:

```csharp
if (cancellationToken == default)
{
    var tokenSource = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        tokenSource.Cancel();
        eventArgs.Cancel = true;
    };
    cancellationToken = tokenSource.Token;
}
```

### Error Handling

- Worker creation failures are logged and propagated
- Individual worker errors are caught, logged, and rethrown
- Worker disposal errors are caught and logged as warnings
- Temporal connection failures trigger automatic retry (configured in TemporalConfiguration)

## Logging

The implementation provides detailed logging at multiple levels:

- **Information**: Worker startup, shutdown, and major lifecycle events
- **Warning**: Worker disposal errors, connection health issues
- **Error**: Worker execution errors, Temporal connection failures
- **Debug**: Detailed connection attempts, worker creation steps

Example log output:

```
[12:34:56] info: Starting workflow 'MyAgent : Default Workflow : Conversational' for agent 'MyAgent' with 3 worker(s)
[12:34:56] info: Task queue for workflow 'MyAgent : Default Workflow : Conversational': tenant-123:MyAgent : Default Workflow : Conversational
[12:34:57] info: Worker 1/3 for 'MyAgent : Default Workflow : Conversational' on queue 'tenant-123:MyAgent : Default Workflow : Conversational' created and ready to run
[12:34:57] info: Worker 2/3 for 'MyAgent : Default Workflow : Conversational' on queue 'tenant-123:MyAgent : Default Workflow : Conversational' created and ready to run
[12:34:57] info: Worker 3/3 for 'MyAgent : Default Workflow : Conversational' on queue 'tenant-123:MyAgent : Default Workflow : Conversational' created and ready to run
```

## Comparison with Previous Implementation

This implementation maintains compatibility with the previous `XiansAi.Lib.Src` version:

| Aspect | XiansAi.Lib.Src | Xians.Lib (New) |
|--------|-----------------|-----------------|
| Task Queue Naming | `{TenantId}:{WorkflowType}` or `{WorkflowType}` | ✅ Same |
| Worker Creation | `TemporalWorker` in loop | ✅ Same |
| Concurrent Workers | `Task.WhenAll()` | ✅ Same |
| Cancellation Handling | Ctrl+C handler | ✅ Same |
| Built-in Workflow | Dynamic workflow | ✅ Same (BuiltinWorkflow) |
| Configuration Source | Environment variables | Server API (more flexible) |

## Troubleshooting

### "TenantId is required for non-system-scoped agents"

**Solution**: Set `TenantId` in `XiansOptions`:

```csharp
var options = new XiansOptions
{
    ServerUrl = serverUrl,
    ApiKey = apiKey,
    TenantId = "your-tenant-id"  // Add this
};
```

### "Temporal service is not configured"

**Solution**: Use `InitializeAsync()` instead of `Initialize()` to ensure Temporal service is created:

```csharp
// ❌ Wrong (if Initialize doesn't set up Temporal)
var platform = XiansPlatform.Initialize(options);

// ✅ Correct
var platform = await XiansPlatform.InitializeAsync(options);
```

### Workers not receiving tasks

**Checklist**:
1. Verify task queue name matches between workflow starter and worker
2. Check that Temporal namespace is correct
3. Ensure TenantId is consistent
4. Verify workflow type string matches

## See Also

- [Getting Started Guide](GettingStarted.md)
- [Temporal Client Documentation](TemporalClient.md)
- [Configuration Reference](Configuration.md)
- [HTTP Client Documentation](HttpClient.md)


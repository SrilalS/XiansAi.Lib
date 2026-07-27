# Router → Processor (cross-tenant)

This example shows a **Router Agent** that receives a webhook with a `tenantId` + payload, then uses the **Xians Admin API** to route that payload to a **Processor Agent** activation in the given tenant.

| Agent | Type | Role |
|-------|------|------|
| **Router Agent** | Not a template (`IsTemplate = false`) | Owns an inbound webhook; starts a custom workflow that talks to the Admin API |
| **Processor Agent** | Template (`IsTemplate = true`) | Deployed/activated per tenant; receives the routed payload on a named webhook |

## Prerequisites

1. A running Xians server
2. An **agent certificate** (`XIANS_API_KEY`) for the tenant that hosts the Router Agent
3. An **Admin API token** (`XIANS_ADMIN_TOKEN`) with permission to inspect activations and manage webhooks across tenants

## Setup

```bash
cd Xians.Examples/RouterProcessor
cp env.template .env
```

Edit `.env`:

```env
XIANS_SERVER_URL=http://localhost:5005
XIANS_API_KEY=<your-agent-certificate>
XIANS_ADMIN_TOKEN=<your-admin-api-token>
```

## Run the workers

```bash
dotnet run
```

Both agents start Temporal workers and upload their workflow definitions. Leave this process running while you walk through the steps below.

## Walkthrough

### 1. Activate Router Agent and create its webhook

In the portal (or Admin API), for the **Router Agent**’s tenant:

1. Create/activate an activation for **Router Agent** (so its Integrator Workflow is running).
2. Create a builtin webhook with default options.

Copy the webhook URL. You will call this URL in the next steps.

### 2. Call the Router webhook (Processor not activated yet)

Pick a **target tenant id** that does **not** yet have Processor Agent activated. POST:

```bash
curl -X POST "<ROUTER_WEBHOOK_URL>" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "<target-tenant-id>",
    "payload": {
      "orderId": "ORD-123",
      "amount": 42.5
    }
  }'
```

**Expected result:** the Router starts `RouteToProcessorWorkflow`. The activity lists Processor Agent activations for `<target-tenant-id>` via the Admin API. Because none exist, routing stops — the payload is **not** processed. Check the Router Agent logs for a message like *Processor Agent has no activation in tenant…*.

### 3. Deploy and activate Processor Agent on that tenant

Still for `<target-tenant-id>`:

1. Deploy the **Processor Agent** template to the tenant (portal / Admin API template deploy).
2. Create **exactly one** activation for Processor Agent (any name) and **activate** it.

> The Router does not care about the activation name. It requires **exactly one** Processor Agent activation in the tenant: zero → skip; more than one → error.

Keep `dotnet run` running so the Processor Agent worker can handle the activation.

### 4. Call the Router webhook again

Use the same curl as in step 2 (same `tenantId` and payload).

**Expected result:**

1. Admin API finds exactly one Processor Agent activation (and it is active).
2. Router looks for a webhook named **`ProcessPayload`** on that activation.
3. If it does not exist, the Admin API **creates** it (Integrator Workflow / `ProcessPayload`).
4. Router **invokes** that webhook with the nested `payload`.
5. Processor Agent’s Integrator handler runs and logs that the payload was processed.

Watch both agents’ console output for the success path.

## Payload shape

```json
{
  "tenantId": "<xians-tenant-id>",
  "payload": { }
}
```

| Field | Required | Notes |
|-------|----------|--------|
| `tenantId` | Yes | Real Xians tenant id (also accepts `tenant-id`) |
| `payload` | Recommended | Forwarded to Processor Agent. If omitted, the whole body is forwarded |

## Names used by this example

These are defined in `Constants.cs`:

| Constant | Value |
|----------|--------|
| Router agent | `Router Agent` |
| Processor agent | `Processor Agent` |
| Processor activation | Any name — must be **exactly one** per tenant |
| Processor webhook | `ProcessPayload` |
| Processor workflow | `Integrator Workflow` |

## What the custom workflow does

`RouteToProcessorWorkflow` (via an activity) calls the Admin API:

1. `GET /api/v1/admin/tenants/{tenantId}/agentActivations?agentName=Processor Agent`
   - **0** activations → stop (not activated)
   - **1** activation → use it (any name; must be active)
   - **2+** activations → stop with an ambiguity error
2. `GET .../webhooks?agentName=Processor Agent&activationName=<resolved>` — does `ProcessPayload` exist?
3. If missing → `POST .../webhooks` to create it.
4. `POST {ServerUrl}{webhookUrl}` with the payload body to invoke Processor Agent.

The webhook URL (which embeds a callable credential) is kept inside the activity and is not written into workflow history.

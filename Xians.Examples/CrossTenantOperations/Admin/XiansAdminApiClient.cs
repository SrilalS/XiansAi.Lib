using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xians.Examples.RouterProcessor.Admin;

/// <summary>
/// Thin Admin API client for cross-tenant activation and webhook operations.
/// Auth: <c>Authorization: Bearer {XIANS_ADMIN_TOKEN}</c>.
/// Base URL: <c>XIANS_SERVER_URL</c>.
/// </summary>
public sealed class XiansAdminApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly string _serverUrl;

    public XiansAdminApiClient(string serverUrl, string adminToken)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new ArgumentException("Server URL is required.", nameof(serverUrl));
        if (string.IsNullOrWhiteSpace(adminToken))
            throw new ArgumentException("Admin token is required.", nameof(adminToken));

        _serverUrl = serverUrl.TrimEnd('/');
        var token = adminToken.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token["Bearer ".Length..].Trim();

        _http = new HttpClient { BaseAddress = new Uri(_serverUrl + "/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Builds a client from <c>XIANS_SERVER_URL</c> and <c>XIANS_ADMIN_TOKEN</c> environment variables.
    /// </summary>
    public static XiansAdminApiClient FromEnvironment()
    {
        var serverUrl = Environment.GetEnvironmentVariable("XIANS_SERVER_URL")
            ?? throw new InvalidOperationException("XIANS_SERVER_URL is not set");
        var adminToken = Environment.GetEnvironmentVariable("XIANS_ADMIN_TOKEN")
            ?? throw new InvalidOperationException("XIANS_ADMIN_TOKEN is not set");
        return new XiansAdminApiClient(serverUrl, adminToken);
    }

    public string ServerUrl => _serverUrl;

    // -------------------------------------------------------------------------
    // Activations
    // -------------------------------------------------------------------------

    public async Task<List<AdminActivation>> ListActivationsAsync(
        string tenantId,
        string? agentName = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/v1/admin/tenants/{Uri.EscapeDataString(tenantId)}/agentActivations";
        if (!string.IsNullOrWhiteSpace(agentName))
            url += $"?agentName={Uri.EscapeDataString(agentName)}";

        using var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "list activations");

        return await ReadListAsync<AdminActivation>(response, cancellationToken);
    }

    /// <summary>
    /// Resolves the Processor-style activation for an agent in a tenant:
    /// none → <see cref="ActivationResolveStatus.None"/>;
    /// exactly one → <see cref="ActivationResolveStatus.Single"/> (any activation name);
    /// more than one → <see cref="ActivationResolveStatus.Ambiguous"/>.
    /// </summary>
    public async Task<(ActivationResolveStatus Status, AdminActivation? Activation, int Count)>
        ResolveSingleActivationAsync(
            string tenantId,
            string agentName,
            CancellationToken cancellationToken = default)
    {
        var activations = await ListActivationsAsync(tenantId, agentName, cancellationToken);
        var forAgent = activations
            .Where(a => string.Equals(a.AgentName, agentName, StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrEmpty(a.AgentName))
            .ToList();

        // If the list endpoint is already filtered by agentName, AgentName may be empty on items.
        if (forAgent.Count == 0 && activations.Count > 0 && !string.IsNullOrWhiteSpace(agentName))
            forAgent = activations;

        return forAgent.Count switch
        {
            0 => (ActivationResolveStatus.None, null, 0),
            1 => (ActivationResolveStatus.Single, forAgent[0], 1),
            _ => (ActivationResolveStatus.Ambiguous, null, forAgent.Count)
        };
    }

    // -------------------------------------------------------------------------
    // Webhooks
    // -------------------------------------------------------------------------

    public async Task<List<AdminWebhook>> ListWebhooksAsync(
        string tenantId,
        string? agentName = null,
        string? activationName = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(agentName))
            query.Add($"agentName={Uri.EscapeDataString(agentName)}");
        if (!string.IsNullOrWhiteSpace(activationName))
            query.Add($"activationName={Uri.EscapeDataString(activationName)}");

        var url = $"api/v1/admin/tenants/{Uri.EscapeDataString(tenantId)}/webhooks";
        if (query.Count > 0)
            url += "?" + string.Join("&", query);

        using var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "list webhooks");

        return await ReadListAsync<AdminWebhook>(response, cancellationToken);
    }

    /// <summary>
    /// Finds a webhook whose <see cref="AdminWebhook.WebhookName"/> (or <see cref="AdminWebhook.Name"/>)
    /// matches <paramref name="webhookName"/>.
    /// </summary>
    public async Task<AdminWebhook?> FindWebhookAsync(
        string tenantId,
        string agentName,
        string activationName,
        string webhookName,
        CancellationToken cancellationToken = default)
    {
        var webhooks = await ListWebhooksAsync(tenantId, agentName, activationName, cancellationToken);
        return webhooks.FirstOrDefault(w =>
            string.Equals(w.WebhookName, webhookName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(w.Name, webhookName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AdminWebhook> CreateWebhookAsync(
        string tenantId,
        string agentName,
        string activationName,
        string webhookName,
        string? workflowName = null,
        string? name = null,
        int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/v1/admin/tenants/{Uri.EscapeDataString(tenantId)}/webhooks";
        var body = new
        {
            agentName,
            activationName,
            webhookName,
            workflowName,
            name = name ?? webhookName,
            timeoutInSeconds = timeoutSeconds
        };

        using var response = await _http.PostAsJsonAsync(url, body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "create webhook");

        var created = await response.Content.ReadFromJsonAsync<AdminWebhook>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Admin API returned an empty create-webhook response.");
        return created;
    }

    /// <summary>
    /// Ensures a webhook with the given name exists; creates it when missing.
    /// Returns the webhook and whether it was newly created.
    /// </summary>
    public async Task<(AdminWebhook Webhook, bool Created)> EnsureWebhookAsync(
        string tenantId,
        string agentName,
        string activationName,
        string webhookName,
        string? workflowName = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindWebhookAsync(
            tenantId, agentName, activationName, webhookName, cancellationToken);
        if (existing != null)
            return (existing, false);

        var created = await CreateWebhookAsync(
            tenantId,
            agentName,
            activationName,
            webhookName,
            workflowName: workflowName,
            cancellationToken: cancellationToken);
        return (created, true);
    }

    /// <summary>
    /// Invokes a builtin webhook by POSTing <paramref name="payloadJson"/> to its URL.
    /// <paramref name="webhookUrl"/> may be absolute or relative to <see cref="ServerUrl"/>.
    /// The webhook URL embeds a callable credential — do not log or persist it into workflow history.
    /// </summary>
    public async Task<(int StatusCode, string Body)> InvokeWebhookAsync(
        string webhookUrl,
        string? payloadJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            throw new ArgumentException("Webhook URL is required.", nameof(webhookUrl));

        var absolute = webhookUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? webhookUrl
            : _serverUrl + (webhookUrl.StartsWith('/') ? webhookUrl : "/" + webhookUrl);

        // Use a plain HttpClient (no Bearer header) — builtin webhooks authenticate via apikey in the URL.
        using var invokeClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        using var content = new StringContent(
            string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            Encoding.UTF8,
            "application/json");

        using var response = await invokeClient.PostAsync(absolute, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return ((int)response.StatusCode, body);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"Admin API {operation} failed. Status: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }

    /// <summary>
    /// Reads a JSON array, or a common envelope shape like <c>{ "items": [...] }</c> /
    /// <c>{ "webhooks": [...] }</c> / <c>{ "activations": [...] }</c>.
    /// </summary>
    private static async Task<List<T>> ReadListAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.Deserialize<List<T>>(JsonOptions) ?? new List<T>();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in new[] { "webhooks", "activations", "items", "data", "results" })
            {
                if (root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    return arr.Deserialize<List<T>>(JsonOptions) ?? new List<T>();
                }
            }

            // Single object masquerading as a list endpoint — wrap it.
            var single = root.Deserialize<T>(JsonOptions);
            if (single != null)
                return new List<T> { single };
        }

        return new List<T>();
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Outcome of resolving a single agent activation in a tenant.</summary>
public enum ActivationResolveStatus
{
    /// <summary>No activations for the agent in the tenant.</summary>
    None = 0,

    /// <summary>Exactly one activation — use it (any name).</summary>
    Single = 1,

    /// <summary>More than one activation — cannot choose.</summary>
    Ambiguous = 2
}

/// <summary>Permissive admin activation model (matches Agent Activation Admin API).</summary>
public class AdminActivation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("agentName")]
    public string AgentName { get; set; } = string.Empty;

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("active")]
    public bool? Active { get; set; }

    [JsonPropertyName("activatedAt")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("deactivatedAt")]
    public DateTime? DeactivatedAt { get; set; }

    [JsonIgnore]
    public bool IsActive => Active ?? (ActivatedAt.HasValue && !DeactivatedAt.HasValue);
}

/// <summary>Permissive admin webhook / app-integration model.</summary>
public class AdminWebhook
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("agentName")]
    public string AgentName { get; set; } = string.Empty;

    [JsonPropertyName("activationName")]
    public string ActivationName { get; set; } = string.Empty;

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    /// <summary>
    /// Relative (or absolute) URL used to invoke the webhook. Embeds a callable credential —
    /// do not log or persist into Temporal workflow history.
    /// </summary>
    [JsonPropertyName("webhookUrl")]
    public string WebhookUrl { get; set; } = string.Empty;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("configuration")]
    public Dictionary<string, JsonElement>? Configuration { get; set; }

    /// <summary>Webhook name/scope from configuration (used when triggering).</summary>
    [JsonIgnore]
    public string? WebhookName => GetConfigString("webhookName") ?? Name;

    private string? GetConfigString(string key)
    {
        if (Configuration == null || !Configuration.TryGetValue(key, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.ToString()
        };
    }
}

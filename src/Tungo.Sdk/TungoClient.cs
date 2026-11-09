using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Tungo.Sdk;

public sealed record SSEEvent(string Event, string Raw);

public class TungoException : Exception
{
    public int Status { get; }
    public string Code { get; }

    public TungoException(int status, string code, string message) : base(message)
    {
        Status = status;
        Code = code;
    }
}

public static class TungoMoney
{
    public static long ToTiyn(double amount) => (long)Math.Round(amount * 100.0, MidpointRounding.AwayFromZero);
    public static double ToTenge(long tiyn) => tiyn / 100.0;

    public static string FormatTiyn(long tiyn, string currency = "KZT")
    {
        var symbol = (currency ?? "KZT").Trim().ToUpperInvariant() switch
        {
            "" or "KZT" => "₸",
            "USD" => "$",
            "EUR" => "€",
            "RUB" => "₽",
            var other => other,
        };
        var neg = tiyn < 0;
        var absVal = Math.Abs(tiyn);
        var wholeStr = (absVal / 100).ToString(CultureInfo.InvariantCulture);
        var frac = absVal % 100;
        var sb = new StringBuilder();
        for (var i = 0; i < wholeStr.Length; i++)
        {
            if (i > 0 && (wholeStr.Length - i) % 3 == 0) sb.Append(' ');
            sb.Append(wholeStr[i]);
        }
        return string.Create(CultureInfo.InvariantCulture, $"{(neg ? "-" : "")}{sb},{frac:00} {symbol}");
    }
}

public static class TungoWebhooks
{
    public static string SignHookPayload(string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string VerifyWebhookSignature(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        string secret,
        long? nowUnix = null,
        long toleranceSec = 300)
    {
        var norm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in headers) norm[kv.Key] = kv.Value;

        var msgId = norm.GetValueOrDefault("svix-id") ?? norm.GetValueOrDefault("webhook-id") ?? throw new TungoException(0, "webhook_error", "Missing svix-id");
        var msgTs = norm.GetValueOrDefault("svix-timestamp") ?? norm.GetValueOrDefault("webhook-timestamp") ?? throw new TungoException(0, "webhook_error", "Missing svix-timestamp");
        var msgSig = norm.GetValueOrDefault("svix-signature") ?? norm.GetValueOrDefault("webhook-signature") ?? throw new TungoException(0, "webhook_error", "Missing svix-signature");

        var ts = long.Parse(msgTs, CultureInfo.InvariantCulture);
        if (nowUnix.HasValue && toleranceSec > 0 && Math.Abs(nowUnix.Value - ts) > toleranceSec)
            throw new TungoException(0, "webhook_error", "Webhook timestamp outside tolerance");

        var cleanSecret = secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret[6..] : secret;
        var keyBytes = Convert.FromBase64String(cleanSecret);
        using var hmac = new HMACSHA256(keyBytes);
        var expectedB64 = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{msgId}.{msgTs}.{rawBody}")));

        foreach (var part in msgSig.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("v1,", StringComparison.Ordinal) &&
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedB64), Encoding.UTF8.GetBytes(part[3..])))
            {
                return rawBody;
            }
        }
        throw new TungoException(0, "webhook_error", "Invalid svix-signature");
    }
}

public sealed class TungoClient
{
    public string? ApiKey { get; }
    public string BaseUrl { get; }
    public string? WorkspaceId { get; }
    public string? CompanyId { get; }

    public WorkspacesResource Workspaces { get; }
    public CompaniesResource Companies { get; }
    public PlatformResource Platform { get; }
    public ApiKeysResource ApiKeys { get; }
    public WebhooksResource Webhooks { get; }
    public ChatResource Chat { get; }
    public ProposalsResource Proposals { get; }
    public FeedResource Feed { get; }
    public BatchesResource Batches { get; }
    public EsfResource Esf { get; }
    public FormsResource Forms { get; }
    public OnecResource Onec { get; }
    public AutomationsResource Automations { get; }

    public TungoClient(
        string? apiKey = null,
        string? baseUrl = null,
        string? workspaceId = null,
        string? companyId = null)
    {
        ApiKey = apiKey ?? Environment.GetEnvironmentVariable("TUNGO_API_KEY");
        BaseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("TUNGO_BASE_URL") ?? "https://api.tungo.ai").TrimEnd('/');
        WorkspaceId = workspaceId ?? Environment.GetEnvironmentVariable("TUNGO_WORKSPACE_ID");
        CompanyId = companyId ?? Environment.GetEnvironmentVariable("TUNGO_COMPANY_ID");
        Workspaces = new WorkspacesResource(this);
        Companies = new CompaniesResource(this);
        Platform = new PlatformResource(this);
        ApiKeys = new ApiKeysResource(this);
        Webhooks = new WebhooksResource(this);
        Chat = new ChatResource(this);
        Proposals = new ProposalsResource(this);
        Feed = new FeedResource(this);
        Batches = new BatchesResource(this);
        Esf = new EsfResource(this);
        Forms = new FormsResource(this);
        Onec = new OnecResource(this);
        Automations = new AutomationsResource(this);
    }

    public string ResolveCompanyId(string? explicitCo = null) =>
        !string.IsNullOrEmpty(explicitCo) ? explicitCo :
        !string.IsNullOrEmpty(CompanyId) ? CompanyId :
        throw new TungoException(0, "missing_company_id", "Не указан CompanyId");

    public string ResolveWorkspaceId(string? explicitWs = null) =>
        !string.IsNullOrEmpty(explicitWs) ? explicitWs :
        !string.IsNullOrEmpty(WorkspaceId) ? WorkspaceId :
        throw new TungoException(0, "missing_workspace_id", "Не указан WorkspaceId");

    /// <summary>Быстрый синхронный запрос к ИИ-бухгалтеру (stream = false).</summary>
    public Task<JsonNode?> AskAsync(string prompt, string? companyId = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["message"] = prompt, ["stream"] = false };
        var q = companyId is not null ? new Dictionary<string, string> { ["co"] = companyId } : null;
        return Chat.CreateAsync(body, q, ct);
    }

    public Task<JsonNode?> RequestAsync(
        string method,
        string path,
        Dictionary<string, string> query,
        JsonObject? body,
        string auth,
        bool idempotent,
        bool retrySafe,
        CancellationToken ct = default) => Task.FromResult<JsonNode?>(new JsonObject());

    public Task<IReadOnlyList<SSEEvent>> RequestStreamAsync(
        string method,
        string path,
        Dictionary<string, string> query,
        JsonObject? body,
        string auth,
        CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SSEEvent>>(Array.Empty<SSEEvent>());
}

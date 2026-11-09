using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
    public string? Hint { get; }
    public string? RequestId { get; }

    public TungoException(int status, string code, string message, string? hint = null, string? requestId = null) : base(message)
    {
        Status = status;
        Code = code;
        Hint = hint;
        RequestId = requestId;
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

    /// <summary>Проверяет контрольный разряд 12-значного БИН/ИИН Республики Казахстан (модуль 11, 2 прохода).</summary>
    public static bool IsValidBinIin(string value)
    {
        var s = (value ?? string.Empty).Trim();
        if (s.Length != 12) return false;
        var digits = new int[12];
        for (var i = 0; i < 12; i++)
        {
            var d = s[i] - '0';
            if (d < 0 || d > 9) return false;
            digits[i] = d;
        }
        int[] w1 = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        var sum1 = 0;
        for (var i = 0; i < 11; i++) sum1 += digits[i] * w1[i];
        var rem = sum1 % 11;
        if (rem == 10)
        {
            int[] w2 = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 1, 2 };
            var sum2 = 0;
            for (var i = 0; i < 11; i++) sum2 += digits[i] * w2[i];
            rem = sum2 % 11;
        }
        return rem < 10 && rem == digits[11];
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

    public static bool VerifyHookSignature(string rawBody, string? signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(secret)) return false;
        var expected = SignHookPayload(rawBody, secret);
        var candidate = signatureHeader.Trim();
        if (!candidate.StartsWith("sha256=", StringComparison.Ordinal))
            candidate = "sha256=" + candidate;
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(candidate);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
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
            if (part.StartsWith("v1,", StringComparison.Ordinal))
            {
                var sigBytes = Encoding.UTF8.GetBytes(part[3..]);
                var expBytes = Encoding.UTF8.GetBytes(expectedB64);
                if (sigBytes.Length == expBytes.Length && CryptographicOperations.FixedTimeEquals(expBytes, sigBytes))
                {
                    return rawBody;
                }
            }
        }
        throw new TungoException(0, "webhook_error", "Invalid svix-signature");
    }
}

public sealed class TungoClient
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    public string? ApiKey { get; }
    public string BaseUrl { get; }
    public string? WorkspaceId { get; }
    public string? CompanyId { get; }
    public int MaxRetries { get; }

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
        string? companyId = null,
        int maxRetries = 3)
    {
        ApiKey = apiKey ?? Environment.GetEnvironmentVariable("TUNGO_API_KEY");
        BaseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("TUNGO_BASE_URL") ?? "https://api.tungo.kz").TrimEnd('/');
        WorkspaceId = workspaceId ?? Environment.GetEnvironmentVariable("TUNGO_WORKSPACE_ID");
        CompanyId = companyId ?? Environment.GetEnvironmentVariable("TUNGO_COMPANY_ID");
        MaxRetries = Math.Max(0, maxRetries);
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
    public Task<JsonNode?> AskAsync(string prompt, string? companyId = null, string? dialogId = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["message"] = prompt, ["stream"] = false };
        if (!string.IsNullOrEmpty(dialogId)) body["dialog_id"] = dialogId;
        var q = companyId is not null ? new Dictionary<string, string> { ["co"] = companyId } : null;
        return Chat.CreateAsync(body, q, ct);
    }

    /// <summary>Ожидает завершения запуска сценария автоматизации (state in done | error | cancelled | skipped).</summary>
    public async Task<JsonNode?> WaitForRunAsync(string runId, string? companyId = null, int pollIntervalMs = 1000, int timeoutMs = 120_000, CancellationToken ct = default)
    {
        var q = companyId is not null ? new Dictionary<string, string> { ["co"] = companyId } : null;
        var start = Environment.TickCount64;
        while (true)
        {
            var run = await Automations.GetRunAsync(runId, q, ct).ConfigureAwait(false);
            var state = run?["state"]?.GetValue<string>() ?? "";
            if (state is "done" or "error" or "cancelled" or "skipped") return run;
            if (Environment.TickCount64 - start >= timeoutMs)
                throw new TungoException(408, "timeout", $"WaitForRunAsync превысил таймаут {timeoutMs} мс для {runId}");
            await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Ожидает завершения пакетной задачи (running == false).</summary>
    public async Task<JsonNode?> WaitForBatchAsync(string batchId, string? companyId = null, int pollIntervalMs = 1500, int timeoutMs = 300_000, CancellationToken ct = default)
    {
        var q = companyId is not null ? new Dictionary<string, string> { ["co"] = companyId } : null;
        var start = Environment.TickCount64;
        while (true)
        {
            var job = await Batches.GetAsync(batchId, q, ct).ConfigureAwait(false);
            var running = job?["running"]?.GetValue<bool>() ?? false;
            if (!running) return job;
            if (Environment.TickCount64 - start >= timeoutMs)
                throw new TungoException(408, "timeout", $"WaitForBatchAsync превысил таймаут {timeoutMs} мс для {batchId}");
            await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
        }
    }

    public static IReadOnlyList<SSEEvent> ParseSse(string raw)
    {
        var normalized = (raw ?? string.Empty).Replace("\r\n", "\n");
        var events = new List<SSEEvent>();
        foreach (var frame in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(frame)) continue;
            var ev = "message";
            var dataLines = new List<string>();
            foreach (var line in frame.Split('\n'))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith(':')) continue;
                if (line.StartsWith("event:", StringComparison.Ordinal))
                    ev = line[6..].Trim();
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var v = line[5..];
                    if (v.StartsWith(' ')) v = v[1..];
                    dataLines.Add(v);
                }
            }
            if (dataLines.Count > 0)
                events.Add(new SSEEvent(ev, string.Join("\n", dataLines)));
        }
        return events;
    }

    private string BuildUrl(string path, Dictionary<string, string> query)
    {
        if (query is null || query.Count == 0) return BaseUrl + path;
        var qs = string.Join("&", query.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{BaseUrl}{path}?{qs}";
    }

    public async Task<JsonNode?> RequestAsync(
        string method,
        string path,
        Dictionary<string, string> query,
        JsonObject? body,
        string auth,
        bool idempotent,
        bool retrySafe,
        CancellationToken ct = default)
    {
        var url = BuildUrl(path, query);
        var idempKey = idempotent ? Guid.NewGuid().ToString() : null;
        var canRetry = retrySafe || idempotent || method is "GET" or "HEAD" or "OPTIONS";
        var limit = canRetry ? MaxRetries : 0;

        for (var attempt = 0; attempt <= limit; attempt++)
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.UserAgent.ParseAdd("tungo-sdk-dotnet/0.1.0");
            if (auth != "none" && !string.IsNullOrEmpty(ApiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            if (idempKey is not null)
                req.Headers.TryAddWithoutValidation("Idempotency-Key", idempKey);
            if (body is not null)
                req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var resp = await SharedHttp.SendAsync(req, ct).ConfigureAwait(false);
            var status = (int)resp.StatusCode;
            var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (status >= 200 && status < 300)
            {
                return string.IsNullOrWhiteSpace(raw) ? new JsonObject() : JsonNode.Parse(raw);
            }
            if (attempt < limit && status is 429 or 502 or 503 or 504)
            {
                await Task.Delay(300 * (1 << attempt), ct).ConfigureAwait(false);
                continue;
            }
            var code = "api_error";
            var msg = raw;
            string? hint = null;
            string? reqId = null;
            try
            {
                var parsed = JsonNode.Parse(raw)?.AsObject();
                reqId = parsed?["request_id"]?.GetValue<string>();
                var errObj = parsed?["error"]?.AsObject() ?? parsed;
                code = errObj?["code"]?.GetValue<string>() ?? code;
                msg = errObj?["message"]?.GetValue<string>() ?? msg;
                hint = errObj?["hint"]?.GetValue<string>() ?? parsed?["hint"]?.GetValue<string>();
            }
            catch { }
            throw new TungoException(status, code, msg, hint, reqId);
        }
        return new JsonObject();
    }

    public async Task<IReadOnlyList<SSEEvent>> RequestStreamAsync(
        string method,
        string path,
        Dictionary<string, string> query,
        JsonObject? body,
        string auth,
        CancellationToken ct = default)
    {
        var url = BuildUrl(path, query);
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.UserAgent.ParseAdd("tungo-sdk-dotnet/0.1.0");
        if (auth != "none" && !string.IsNullOrEmpty(ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        if (body is not null)
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var resp = await SharedHttp.SendAsync(req, ct).ConfigureAwait(false);
        var status = (int)resp.StatusCode;
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (status < 200 || status >= 300)
            throw new TungoException(status, "api_error", raw);
        return ParseSse(raw);
    }
}

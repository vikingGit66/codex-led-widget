using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexQuotaWidgetLite;

public sealed record QuotaWindow(
    double UsedPercent,
    double RemainingPercent,
    int? WindowDurationMins,
    DateTimeOffset? ResetsAt
);

public sealed record DailyUsageBucket(DateOnly Date, long Tokens);

public sealed class QuotaSnapshot
{
    public string LimitId { get; init; } = "codex";
    public string LimitName { get; init; } = "Codex";
    public string PlanType { get; init; } = "unknown";
    public string? ReachedType { get; init; }
    public JsonElement? Credits { get; init; }
    public QuotaWindow? Primary { get; init; }
    public QuotaWindow? Secondary { get; init; }
    public QuotaWindow? Weekly { get; init; }
    public IReadOnlyList<DailyUsageBucket>? DailyUsage { get; init; }
    public double? RemainingPercent => Weekly?.RemainingPercent;
    public double? UsedPercent => Weekly?.UsedPercent;
    public DateTimeOffset? ResetsAt => Weekly?.ResetsAt;
    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class QuotaService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private QuotaSnapshot? cached;

    public async Task<QuotaSnapshot> GetAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && IsFresh(cached)) return cached!;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!force && IsFresh(cached)) return cached!;
            cached = await CodexQuotaClient.ReadAsync(cancellationToken);
            return cached;
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsFresh(QuotaSnapshot? snapshot) =>
        snapshot is not null && DateTimeOffset.UtcNow - snapshot.FetchedAt < TimeSpan.FromSeconds(60);
}

internal static class CodexQuotaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ResolveCodexPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("app-server");
        process.StartInfo.ArgumentList.Add("--listen");
        process.StartInfo.ArgumentList.Add("stdio://");
        process.Start();
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            await SendAsync(process, 1, "initialize", new
            {
                clientInfo = new { name = "codex-led-widget-lite", title = "Codex LED Widget Lite", version = "0.1.0" },
                capabilities = (object?)null
            }, timeout.Token);

            var result = await SendAsync(process, 2, "account/rateLimits/read", null, timeout.Token);
            var limitsById = Property(result, "rateLimitsByLimitId");
            var snapshot =
                Property(limitsById, "codex") ??
                Property(result, "rateLimits") ??
                FirstObjectProperty(limitsById) ??
                throw new InvalidOperationException("Codex did not return a rate-limit snapshot.");
            JsonElement? usage = null;
            try
            {
                using var usageTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                usageTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                usage = await SendAsync(process, 3, "account/usage/read", null, usageTimeout.Token);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Older Codex versions and some auth modes do not expose daily usage.
            }
            return Normalize(snapshot, usage);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            var detail = stderr.IsCompletedSuccessfully ? stderr.Result.Trim() : "";
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? error.Message : detail, error);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static async Task<JsonElement> SendAsync(
        Process process, int id, string method, object? parameters, CancellationToken cancellationToken)
    {
        var payload = parameters is null
            ? JsonSerializer.Serialize(new { id, method }, JsonOptions)
            : JsonSerializer.Serialize(new { id, method, @params = parameters }, JsonOptions);
        await process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);

        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null) throw new InvalidOperationException("Codex app-server ended before responding.");
            if (line.Length == 0) continue;
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var responseId = Property(root, "id");
            if (responseId is null || responseId.Value.ValueKind != JsonValueKind.Number ||
                responseId.Value.GetInt32() != id) continue;
            var error = Property(root, "error");
            if (error is not null && error.Value.ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException(Text(error, "message") ?? error.Value.ToString());
            return Property(root, "result")?.Clone() ??
                throw new InvalidOperationException("Codex returned an empty response.");
        }
    }

    private static QuotaSnapshot Normalize(JsonElement snapshot, JsonElement? usage)
    {
        var primary = NormalizeWindow(Property(snapshot, "primary"));
        var secondary = NormalizeWindow(Property(snapshot, "secondary"));
        var weekly = new[] { secondary, primary }
            .FirstOrDefault(window => window?.WindowDurationMins >= 6 * 24 * 60)
            ?? (secondary?.WindowDurationMins is null ? secondary : null);

        return new QuotaSnapshot
        {
            LimitId = Text(snapshot, "limitId") ?? "codex",
            LimitName = Text(snapshot, "limitName") ?? "Codex",
            PlanType = Text(snapshot, "planType") ?? "unknown",
            ReachedType = Text(snapshot, "rateLimitReachedType"),
            Credits = Property(snapshot, "credits")?.Clone(),
            Primary = primary,
            Secondary = secondary,
            Weekly = weekly,
            DailyUsage = NormalizeDailyUsage(usage),
            FetchedAt = DateTimeOffset.UtcNow
        };
    }

    private static IReadOnlyList<DailyUsageBucket>? NormalizeDailyUsage(JsonElement? usage)
    {
        var buckets = Property(usage, "dailyUsageBuckets");
        if (buckets is not { ValueKind: JsonValueKind.Array }) return null;
        var results = new List<DailyUsageBucket>();
        foreach (var bucket in buckets.Value.EnumerateArray())
        {
            var dateText = Text(bucket, "startDate");
            var tokens = Number(bucket, "tokens");
            if (DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) && tokens is >= 0)
                results.Add(new DailyUsageBucket(date, (long)tokens.Value));
        }
        return results;
    }

    private static QuotaWindow? NormalizeWindow(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object) return null;
        var used = Math.Clamp(Number(element, "usedPercent") ?? 0, 0, 100);
        var duration = Number(element, "windowDurationMins");
        var resetSeconds = Number(element, "resetsAt");
        DateTimeOffset? reset = resetSeconds is > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)resetSeconds.Value)
            : null;
        return new QuotaWindow(used, 100 - used, duration is null ? null : (int)duration, reset);
    }

    private static JsonElement? Property(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } && element.Value.TryGetProperty(name, out var value)
            ? value
            : null;

    private static JsonElement? FirstObjectProperty(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Object }
            ? element.Value.EnumerateObject().Select(property => (JsonElement?)property.Value).FirstOrDefault()
            : null;

    private static string? Text(JsonElement? element, string name)
    {
        var value = Property(element, name);
        return value is { ValueKind: JsonValueKind.String } ? value.Value.GetString() : null;
    }

    private static double? Number(JsonElement? element, string name)
    {
        var value = Property(element, name);
        return value is { ValueKind: JsonValueKind.Number } ? value.Value.GetDouble() : null;
    }

    private static string ResolveCodexPath()
    {
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);

        var binDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        candidates.Add(Path.Combine(binDir, "codex.exe"));
        if (Directory.Exists(binDir))
        {
            candidates.AddRange(Directory.EnumerateDirectories(binDir)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Select(directory => Path.Combine(directory, "codex.exe")));
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory) ||
                directory.Contains(@"WindowsApps\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)) continue;
            candidates.Add(Path.Combine(directory, "codex.exe"));
        }

        return candidates.FirstOrDefault(File.Exists) ??
            throw new FileNotFoundException("Could not find codex.exe. Set CODEX_CLI_PATH to its location.");
    }
}

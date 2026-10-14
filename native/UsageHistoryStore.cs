using System.Text.Json;

namespace CodexQuotaWidgetLite;

internal sealed record HourlyQuotaSample(
    DateTimeOffset SampledAt, double UsedPercent, DateTimeOffset? ResetsAt)
{
    public DateTimeOffset? QuotaDayStart { get; init; }
}

internal sealed class UsageHistoryStore
{
    private readonly string path;
    private readonly List<HourlyQuotaSample> samples = new();

    public UsageHistoryStore(string? historyPath = null)
    {
        path = historyPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaWidgetLite", "usage-history.json");
        try
        {
            var saved = JsonSerializer.Deserialize<List<HourlyQuotaSample>>(File.ReadAllText(path));
            if (saved is not null) samples.AddRange(saved);
        }
        catch (IOException) { }
        catch (JsonException) { }
        Prune();
    }

    public void Record(QuotaSnapshot snapshot)
    {
        if (snapshot.Weekly is null) return;
        var now = snapshot.FetchedAt.ToLocalTime();
        var dayStart = QuotaPaceCalculator.Evaluate(snapshot.Weekly, snapshot.FetchedAt).DayStart;
        var index = samples.FindLastIndex(item =>
            item.SampledAt.ToLocalTime().Date == now.Date &&
            item.SampledAt.ToLocalTime().Hour == now.Hour &&
            item.ResetsAt == snapshot.Weekly.ResetsAt &&
            item.QuotaDayStart == dayStart);
        var value = new HourlyQuotaSample(now, snapshot.Weekly.UsedPercent,
            snapshot.Weekly.ResetsAt) { QuotaDayStart = dayStart };
        if (index >= 0) samples[index] = value;
        else samples.Add(value);
        Prune();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(samples));
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public double? TodayUsed(QuotaSnapshot snapshot, DateTimeOffset now)
    {
        var weekly = snapshot.Weekly;
        if (weekly is null) return null;
        var dayStart = QuotaPaceCalculator.Evaluate(weekly, now).DayStart;
        var baseline = dayStart is null ? null : samples
            .Where(item => item.ResetsAt == weekly.ResetsAt &&
                item.SampledAt <= dayStart &&
                item.SampledAt >= dayStart.Value.AddMinutes(-3))
            .LastOrDefault();
        var measured = QuotaPaceCalculator.TodayUsed(weekly, snapshot.FetchedAt, now, baseline?.UsedPercent);
        // A fresh install may not have a sample exactly at the quota-day boundary.
        return measured ?? (snapshot.FetchedAt >= dayStart ? weekly.UsedPercent : null);
    }

    public HourlyQuotaSample? SampleForHour(DateOnly date, int hour) =>
        samples.LastOrDefault(item =>
            DateOnly.FromDateTime(item.SampledAt.ToLocalTime().DateTime) == date &&
            item.SampledAt.ToLocalTime().Hour == hour);

    private void Prune()
    {
        var earliest = DateTimeOffset.Now.AddDays(-8);
        samples.RemoveAll(item => item.SampledAt < earliest);
        samples.Sort((left, right) => left.SampledAt.CompareTo(right.SampledAt));
    }
}

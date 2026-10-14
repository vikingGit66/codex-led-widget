namespace CodexQuotaWidgetLite;

public enum QuotaPaceLevel
{
    Unknown,
    OnBudget,
    NearBudget,
    BudgetUsed,
    OverBudget
}

public sealed record QuotaPace(
    double? ElapsedPercent,
    double? BudgetPercent,
    double? BalancePercent,
    QuotaPaceLevel Level,
    DateTimeOffset? DayStart);

public static class QuotaPaceCalculator
{
    private const double DailyAllowancePercent = 20;
    private const double NearBudgetMarginPercent = 5;

    public static QuotaPace Evaluate(QuotaWindow? weekly, DateTimeOffset now)
    {
        if (weekly?.ResetsAt is null || weekly.WindowDurationMins is not > 0)
            return new QuotaPace(null, null, null, QuotaPaceLevel.Unknown, null);

        var duration = TimeSpan.FromMinutes(weekly.WindowDurationMins.Value);
        var cycleStart = weekly.ResetsAt.Value - duration;
        var elapsed = now - cycleStart;
        if (elapsed < TimeSpan.Zero || elapsed >= duration)
            return new QuotaPace(null, null, null, QuotaPaceLevel.Unknown, null);

        var elapsedPercent = elapsed.TotalMilliseconds / duration.TotalMilliseconds * 100;
        var dayIndex = Math.Clamp((int)Math.Floor(elapsed.TotalDays), 0, 6);
        var dayStart = cycleStart.AddDays(dayIndex);
        var dayNumber = dayIndex + 1;
        var budgetPercent = Math.Min(100, dayNumber * DailyAllowancePercent);
        var balancePercent = budgetPercent - weekly.UsedPercent;
        var level = balancePercent < 0
            ? QuotaPaceLevel.OverBudget
            : balancePercent == 0
                ? QuotaPaceLevel.BudgetUsed
                : balancePercent <= NearBudgetMarginPercent
                    ? QuotaPaceLevel.NearBudget
                    : QuotaPaceLevel.OnBudget;
        return new QuotaPace(elapsedPercent, budgetPercent, balancePercent, level, dayStart);
    }

    public static double? TodayUsed(QuotaWindow? weekly, DateTimeOffset fetchedAt,
        DateTimeOffset now, double? dayStartUsedPercent)
    {
        var pace = Evaluate(weekly, now);
        if (weekly?.ResetsAt is null || weekly.WindowDurationMins is not > 0 ||
            pace.DayStart is null || fetchedAt < pace.DayStart)
            return null;

        var cycleStart = weekly.ResetsAt.Value -
            TimeSpan.FromMinutes(weekly.WindowDurationMins.Value);
        if (pace.DayStart == cycleStart) return weekly.UsedPercent;
        if (dayStartUsedPercent is null || weekly.UsedPercent < dayStartUsedPercent)
            return null;
        return weekly.UsedPercent - dayStartUsedPercent;
    }
}

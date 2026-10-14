using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CodexQuotaWidgetLite;

var cycleStart = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
var cycleReset = cycleStart.AddDays(7);
CheckPace(10, cycleStart.AddHours(23), 20, 10, QuotaPaceLevel.OnBudget);
CheckPace(15, cycleStart.AddHours(23), 20, 5, QuotaPaceLevel.NearBudget);
CheckPace(20, cycleStart.AddHours(23), 20, 0, QuotaPaceLevel.BudgetUsed);
CheckPace(10, cycleStart.AddHours(25), 40, 30, QuotaPaceLevel.OnBudget);
CheckPace(39, cycleStart.AddHours(30), 40, 1, QuotaPaceLevel.NearBudget);
CheckPace(40, cycleStart.AddHours(30), 40, 0, QuotaPaceLevel.BudgetUsed);
CheckPace(41, cycleStart.AddHours(30), 40, -1, QuotaPaceLevel.OverBudget);
CheckPace(99, cycleStart.AddDays(5), 100, 1, QuotaPaceLevel.NearBudget);
Console.WriteLine("Quota pace: day-one 10% unused rolls over to 30% available on day two.");
var firstDayUsage = new QuotaWindow(32, 68, 10080, cycleReset);
var secondDayUsage = new QuotaWindow(40, 60, 10080, cycleReset);
if (QuotaPaceCalculator.TodayUsed(firstDayUsage, cycleStart.AddHours(8),
        cycleStart.AddHours(8), null) != 32 ||
    QuotaPaceCalculator.TodayUsed(secondDayUsage, cycleStart.AddHours(30),
        cycleStart.AddHours(30), 10) != 30 ||
    QuotaPaceCalculator.TodayUsed(secondDayUsage, cycleStart.AddHours(30),
        cycleStart.AddHours(30), null) is not null ||
    QuotaPaceCalculator.TodayUsed(secondDayUsage, cycleStart.AddHours(23),
        cycleStart.AddHours(25), 10) is not null)
    throw new InvalidOperationException("Today-used calculation did not respect the quota-day baseline.");
Console.WriteLine("Today used: first-day total and second-day baseline passed.");

var historyType = typeof(QuotaService).Assembly.GetType(
    "CodexQuotaWidgetLite.UsageHistoryStore", true)!;
var isolatedHistoryPath = Path.Combine(Path.GetTempPath(),
    "codex-quota-history-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    var history = Activator.CreateInstance(historyType, new object?[] { isolatedHistoryPath })!;
    var record = historyType.GetMethod("Record")!;
    var todayUsed = historyType.GetMethod("TodayUsed")!;
    var start = new DateTimeOffset(2026, 9, 14, 1, 3, 0, TimeSpan.Zero);
    var reset = start.AddDays(7);
    var before = new QuotaSnapshot
    {
        Weekly = new QuotaWindow(10, 90, 10080, reset),
        FetchedAt = start.AddDays(1).AddMinutes(-1)
    };
    var after = new QuotaSnapshot
    {
        Weekly = new QuotaWindow(12, 88, 10080, reset),
        FetchedAt = start.AddDays(1).AddMinutes(1)
    };
    record.Invoke(history, new object[] { before });
    record.Invoke(history, new object[] { after });
    var measured = (double?)todayUsed.Invoke(history, new object[] { after, after.FetchedAt });
    if (measured != 2)
        throw new InvalidOperationException("History did not retain the sample before quota-day rollover.");
}
finally
{
    if (File.Exists(isolatedHistoryPath)) File.Delete(isolatedHistoryPath);
    if (File.Exists(isolatedHistoryPath + ".tmp")) File.Delete(isolatedHistoryPath + ".tmp");
}
Console.WriteLine("History: quota-day boundary samples were preserved.");


void CheckPace(double used, DateTimeOffset now, double expectedBudget,
    double expectedBalance, QuotaPaceLevel expectedLevel)
{
    var weekly = new QuotaWindow(used, 100 - used, 10080, cycleReset);
    var pace = QuotaPaceCalculator.Evaluate(weekly, now);
    if (pace.BudgetPercent != expectedBudget ||
        Math.Abs((pace.BalancePercent ?? double.NaN) - expectedBalance) > 0.0001 ||
        pace.Level != expectedLevel)
        throw new InvalidOperationException(
            $"Pace at {now:u}, used {used}%: expected budget {expectedBudget}%, " +
            $"balance {expectedBalance}%, {expectedLevel}; got " +
            $"{pace.BudgetPercent}%, {pace.BalancePercent}%, {pace.Level}.");
}

var quota = await new QuotaService().GetAsync();
if (quota.Weekly is null)
    throw new InvalidOperationException("No seven-day quota was selected.");
if (quota.RemainingPercent is < 0 or > 100)
    throw new InvalidOperationException("Remaining quota is outside 0–100%.");
Console.WriteLine($"Codex RPC: weekly window {quota.Weekly.WindowDurationMins} minutes; quota normalized.");
if (quota.DailyUsage is { } daily && daily.Any(bucket => bucket.Tokens < 0))
    throw new InvalidOperationException("Daily token usage contained a negative value.");
Console.WriteLine($"Daily usage buckets: {quota.DailyUsage?.Count.ToString() ?? "unavailable"}.");

Exception? layoutFailure = null;
var uiThread = new Thread(() =>
{
    try
    {
        var formType = typeof(QuotaService).Assembly.GetType("CodexQuotaWidgetLite.QuotaForm", true)!;
        using var form = (Form)Activator.CreateInstance(formType)!;
        var fullScreenField = formType.GetField("fullScreen", BindingFlags.Instance | BindingFlags.NonPublic)!;
        fullScreenField.SetValue(form, true);

        CheckLayout(new Size(1920, 1080));
        CheckLayout(new Size(1280, 720));
        using (var highDpiBitmap = new Bitmap(660, 460))
        {
            highDpiBitmap.SetResolution(192, 192);
            using var graphics = Graphics.FromImage(highDpiBitmap);
            var heroFont = (Font)formType.GetField("hero", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            foreach (var text in new[] { "100%", "99.9%", "--%" })
            {
                var metric = graphics.MeasureString(text, heroFont);
                if (metric.Width > 266 || metric.Height > 95)
                    throw new InvalidOperationException(
                        $"A 200% DPI metric would overlap its card: {text} is {metric.Width:0.#}x{metric.Height:0.#}.");
            }
        }

        fullScreenField.SetValue(form, false);
        form.ClientSize = new Size(660, 460);
        var normalButton = form.Controls.OfType<Button>().First();
        if (normalButton.Width != 34 || normalButton.Left != 350)
            throw new InvalidOperationException("Normal window layout was not restored.");

        formType.GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, quota);
        var trendField = formType.GetField("trendMode", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var languageField = formType.GetField("english", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var preview = Environment.GetEnvironmentVariable("CODEX_WIDGET_PREVIEW");
        foreach (var isEnglish in new[] { false, true })
        foreach (var isTrend in new[] { true, false })
        {
            languageField.SetValue(form, isEnglish);
            trendField.SetValue(form, isTrend);
            fullScreenField.SetValue(form, false);
            form.ClientSize = new Size(660, 460);
            using (var bitmap = new Bitmap(660, 460))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, 660, 460));
                if (!string.IsNullOrWhiteSpace(preview))
                    bitmap.Save(PreviewPath(false));
            }
            if (!string.IsNullOrWhiteSpace(preview))
            {
                fullScreenField.SetValue(form, true);
                form.ClientSize = new Size(1920, 1080);
                using var fullBitmap = new Bitmap(1920, 1080);
                form.DrawToBitmap(fullBitmap, new Rectangle(0, 0, 1920, 1080));
                fullBitmap.Save(PreviewPath(true));
                if (!isTrend)
                {
                    using var highDpiBitmap = new Bitmap(1920, 1080);
                    highDpiBitmap.SetResolution(192, 192);
                    form.DrawToBitmap(highDpiBitmap, new Rectangle(0, 0, 1920, 1080));
                    highDpiBitmap.Save(PreviewPath(true).Replace(".png", ".dpi192.png"));
                }
            }

            string PreviewPath(bool fullscreen)
            {
                var suffix = (isTrend ? "" : ".home") +
                    (isEnglish ? ".en" : "") +
                    (fullscreen ? ".full" : "") + ".png";
                return Path.ChangeExtension(preview!, suffix);
            }
        }

        void CheckLayout(Size viewport)
        {
            form.ClientSize = viewport;
            var expectedScale = Math.Min(viewport.Width / 660f, viewport.Height / 460f);
            var expectedWidth = (int)Math.Round(34 * expectedScale);
            var buttons = form.Controls.OfType<Button>().OrderBy(button => button.Left).ToArray();
            if (buttons.Length != 7 || buttons[0].Width != expectedWidth ||
                buttons[0].Left < 0 || buttons[^1].Right > viewport.Width)
                throw new InvalidOperationException($"Fullscreen controls do not fit {viewport}.");
        }
    }
    catch (Exception error)
    {
        layoutFailure = error;
    }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
uiThread.Join();
if (layoutFailure is not null) throw layoutFailure;
Console.WriteLine("Native fullscreen scaling: 1920x1080, 1280x720, and window restore passed.");

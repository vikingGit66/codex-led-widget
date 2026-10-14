using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CodexQuotaWidgetLite;

internal sealed class QuotaForm : Form
{
    private const int WidgetWidth = 660;
    private const int WidgetHeight = 460;
    private static readonly Color Background = Color.FromArgb(7, 10, 15);
    private static readonly Color Panel = Color.FromArgb(19, 28, 39);
    private static readonly Color TextColor = Color.FromArgb(243, 249, 255);
    private static readonly Color Muted = Color.FromArgb(155, 178, 197);
    private static readonly Color Cyan = Color.FromArgb(96, 221, 255);
    private static readonly Color Green = Color.FromArgb(85, 240, 139);
    private static readonly Color Yellow = Color.FromArgb(255, 214, 102);
    private static readonly Color Red = Color.FromArgb(255, 82, 109);

    private readonly QuotaService quotaService = new();
    private readonly UsageHistoryStore history = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 60_000 };
    private readonly System.Windows.Forms.Timer clockTimer = new() { Interval = 1_000 };
    private readonly ToolTip tooltips = new();
    private readonly NotifyIcon tray;
    private Icon? trayPercentIcon;
    private readonly Button trendButton;
    private readonly Button languageButton;
    private readonly Button pinButton;
    private readonly Button fullscreenButton;
    private readonly Button refreshButton;
    private readonly Button hideButton;
    private readonly Button closeButton;
    private readonly Font regular = new("Microsoft YaHei UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font small = new("Microsoft YaHei UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font medium = new("Microsoft YaHei UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font hero = new("Segoe UI", 65, FontStyle.Bold, GraphicsUnit.Pixel);
    private Font? scaledButtonFont;
    private float appliedButtonScale = 1f;

    private QuotaSnapshot? snapshot;
    private string? error;
    private bool loading;
    private bool english;
    private bool pinned = true;
    private bool fullScreen;
    private bool trendMode;
    private DateOnly selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private bool restoreFullScreen;
    private Rectangle normalBounds;
    private DateTimeOffset nextRefreshAt;

    public QuotaForm()
    {
        Text = "Codex Quota Widget Lite";
        Icon = new Icon(typeof(QuotaForm).Assembly.GetManifestResourceStream("CodexQuotaWidgetLite.CodexQuotaWidgetLite.ico")!);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(WidgetWidth, WidgetHeight);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Background;
        ForeColor = TextColor;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        LoadPreferences();

        var workArea = Screen.PrimaryScreen!.WorkingArea;
        Bounds = new Rectangle(workArea.Right - WidgetWidth - 24, workArea.Top + 24,
            WidgetWidth, WidgetHeight);
        normalBounds = Bounds;
        TopMost = pinned;

        trendButton = MakeButton("图", () => { trendMode = !trendMode; UpdateButtons(); Invalidate(); });
        languageButton = MakeButton("EN", () => { english = !english; SavePreferences(); UpdateButtons(); Invalidate(); });
        pinButton = MakeButton("P", () => { pinned = !pinned; TopMost = pinned || fullScreen; SavePreferences(); UpdateButtons(); });
        fullscreenButton = MakeButton("□", () => SetFullScreen(!fullScreen));
        refreshButton = MakeButton("↻", async () => await RefreshQuotaAsync());
        hideButton = MakeButton("–", MinimizeToTray);
        closeButton = MakeButton("×", Close);
        UpdateButtons();
        LayoutButtons();
        Resize += (_, _) => { LayoutButtons(); Invalidate(); };

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) => ToggleVisibility());
        menu.Items.Add("刷新额度", null, async (_, _) => await RefreshQuotaAsync());
        menu.Items.Add("用量趋势", null, (_, _) => { trendMode = true; Show(); Activate(); UpdateButtons(); Invalidate(); });
        menu.Items.Add("全屏 / 退出全屏", null, (_, _) => SetFullScreen(!fullScreen));
        menu.Items.Add("置顶 / 取消置顶", null, (_, _) =>
        {
            pinned = !pinned;
            TopMost = pinned || fullScreen;
            SavePreferences();
            UpdateButtons();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Close());
        tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Codex Quota Widget Lite",
            Visible = true,
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => ToggleVisibility();
        UpdateTrayPercentIcon();

        refreshTimer.Tick += async (_, _) => await RefreshQuotaAsync();
        clockTimer.Tick += (_, _) => { if (Visible) Invalidate(); };
        refreshTimer.Start();
        clockTimer.Start();
        Shown += async (_, _) =>
        {
            if (restoreFullScreen) SetFullScreen(true);
            await RefreshQuotaAsync();
        };
        KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Escape && fullScreen) SetFullScreen(false);
        };
    }

    private Button MakeButton(string caption, Action action)
    {
        var button = new Button
        {
            Text = caption,
            Width = 34,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 47, 58),
            ForeColor = TextColor,
            Font = medium,
            TabStop = false
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(58, 79, 92);
        button.Click += (_, _) => action();
        Controls.Add(button);
        return button;
    }

    private void LayoutButtons()
    {
        var scale = WidgetScale();
        var origin = WidgetOrigin();
        var buttons = new[] { trendButton, languageButton, pinButton, fullscreenButton, refreshButton, hideButton, closeButton };
        if (Math.Abs(appliedButtonScale - scale) > 0.001f)
        {
            var oldFont = scaledButtonFont;
            scaledButtonFont = Math.Abs(scale - 1f) < 0.001f
                ? null : new Font("Microsoft YaHei UI", 15f * scale,
                    FontStyle.Bold, GraphicsUnit.Pixel);
            foreach (var button in buttons) button.Font = scaledButtonFont ?? medium;
            oldFont?.Dispose();
            appliedButtonScale = scale;
        }
        for (var index = 0; index < buttons.Length; index++)
        {
            buttons[index].Bounds = new Rectangle(
                (int)Math.Round(origin.X + (350 + index * 40) * scale),
                (int)Math.Round(origin.Y + 18 * scale),
                (int)Math.Round(34 * scale),
                (int)Math.Round(32 * scale));
        }
    }

    private float WidgetScale() =>
        fullScreen ? Math.Max(0.1f, Math.Min(ClientSize.Width / (float)WidgetWidth,
            ClientSize.Height / (float)WidgetHeight)) : 1f;

    private PointF WidgetOrigin()
    {
        if (!fullScreen) return PointF.Empty;
        var scale = WidgetScale();
        return new PointF((ClientSize.Width - WidgetWidth * scale) / 2f,
            (ClientSize.Height - WidgetHeight * scale) / 2f);
    }

    private void UpdateButtons()
    {
        trendButton.Text = trendMode ? Label("返", "‹") : Label("图", "∿");
        tooltips.SetToolTip(trendButton, trendMode ? Label("返回额度", "Back to quota") : Label("用量趋势", "Usage trends"));
        trendButton.BackColor = trendMode ? Color.FromArgb(40, 90, 85) : Color.FromArgb(35, 47, 58);
        languageButton.Text = english ? "中" : "EN";
        tooltips.SetToolTip(languageButton, english ? "中文" : "English");
        tooltips.SetToolTip(pinButton, pinned ? Label("取消置顶", "Unpin") : Label("置顶", "Pin"));
        tooltips.SetToolTip(fullscreenButton, fullScreen ? Label("退出全屏", "Exit fullscreen")
            : Label("全屏（遮挡任务栏）", "Fullscreen (cover taskbar)"));
        tooltips.SetToolTip(refreshButton, Label("刷新", "Refresh"));
        tooltips.SetToolTip(hideButton, Label("隐藏到托盘", "Hide to tray"));
        tooltips.SetToolTip(closeButton, Label("退出", "Exit"));
        pinButton.BackColor = pinned ? Color.FromArgb(40, 90, 85) : Color.FromArgb(35, 47, 58);
        fullscreenButton.BackColor = fullScreen ? Color.FromArgb(40, 90, 85) : Color.FromArgb(35, 47, 58);
    }

    private string Label(string zh, string en) => english ? en : zh;

    private void SetFullScreen(bool enabled)
    {
        if (fullScreen == enabled) return;
        if (enabled) normalBounds = Bounds;
        fullScreen = enabled;
        Bounds = enabled ? Screen.FromControl(this).Bounds : normalBounds;
        TopMost = enabled || pinned;
        if (enabled)
            SetWindowPos(Handle, new IntPtr(-1), Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, 0x0040);
        LayoutButtons();
        UpdateButtons();
        SavePreferences();
        Invalidate();
    }

    private void ToggleVisibility()
    {
        if (Visible) MinimizeToTray();
        else { Show(); Activate(); }
    }

    private void MinimizeToTray()
    {
        var workArea = Screen.PrimaryScreen?.WorkingArea;
        if (workArea is not null)
            Location = new Point(workArea.Value.Right - Width - 16, workArea.Value.Bottom - Height - 16);
        UpdateTrayPercentIcon();
        Hide();
    }

    private void UpdateTrayPercentIcon()
    {
        var percent = snapshot?.Weekly?.RemainingPercent;
        var label = percent is null ? "--" : Math.Clamp((int)Math.Round(percent.Value), 0, 100).ToString();
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var circle = new SolidBrush(Color.FromArgb(35, 47, 58)))
        using (var textBrush = new SolidBrush(StatusColor()))
        using (var font = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            graphics.Clear(Color.FromArgb(7, 10, 15));
            graphics.FillEllipse(circle, 0, 0, 32, 32);
            graphics.DrawString(label, font, textBrush, new RectangleF(0, -1, 32, 34), format);
        }
        var handle = bitmap.GetHicon();
        using var source = Icon.FromHandle(handle);
        var next = new Icon(source, new Size(32, 32));
        DestroyIcon(handle);
        var previous = trayPercentIcon;
        trayPercentIcon = next;
        tray.Icon = next;
        previous?.Dispose();
        tray.Text = percent is null ? "Codex Quota" : $"剩余 {label}%";
    }
    private async Task RefreshQuotaAsync()
    {
        if (loading) return;
        loading = true;
        error = null;
        Invalidate();
        try
        {
            snapshot = await quotaService.GetAsync(force: true);
            history.Record(snapshot);
            UpdateTrayPercentIcon();
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            loading = false;
            nextRefreshAt = DateTimeOffset.Now.AddMinutes(1);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var g = eventArgs.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Background);
        var origin = WidgetOrigin();
        var scale = WidgetScale();
        using var layoutTransform = new Matrix(scale, 0, 0, scale, origin.X, origin.Y);
        g.Transform = layoutTransform;

        FillPanel(g, new Rectangle(0, 0, WidgetWidth, WidgetHeight), Background, 24);
        using (var border = new Pen(Color.FromArgb(64, 91, 112)))
        using (var outline = Rounded(new Rectangle(1, 1, WidgetWidth - 3, WidgetHeight - 3), 24))
            g.DrawPath(border, outline);

        using (var light = new SolidBrush(StatusColor()))
            g.FillEllipse(light, 21, 26, 16, 16);
        Draw(g, "Codex " + Label("额度监控", "Quota Monitor"), medium, TextColor, 47, 19);
        Draw(g, loading ? Label("正在读取本机额度", "Reading local quota")
            : error is not null ? Label("读取失败", "Read failed")
            : snapshot?.Weekly is null ? Label("没有 7 天额度数据", "No seven-day quota")
            : Label("7 天额度已同步", "Seven-day quota synced"), small, Muted, 47, 42);

        if (trendMode) PaintTrends(g);
        else PaintHome(g);

        using (var dot = new SolidBrush(StatusColor()))
            g.FillEllipse(dot, 21, 437, 8, 8);
        var status = error is not null ? error
            : snapshot is null ? Label("正在读取 Codex 额度...", "Reading Codex quota...")
            : Label("上次更新", "Updated") + " " + snapshot.FetchedAt.ToLocalTime().ToString("HH:mm:ss");
        DrawFitted(g, status, small, Muted, new RectangleF(39, 427, 420, 22));
        Draw(g, Label("下次刷新", "Next refresh") + " " +
            (nextRefreshAt == default ? "--" : nextRefreshAt.ToString("HH:mm:ss")),
            small, Muted, 480, 427);
    }

    private void PaintHome(Graphics g)
    {
        FillPanel(g, new Rectangle(20, 76, 304, 192), Panel, 17);
        FillPanel(g, new Rectangle(336, 76, 304, 192), Panel, 17);
        FillPanel(g, new Rectangle(20, 280, 620, 134), Panel, 17);
        PaintHomeRemaining(g);
        PaintHomeToday(g);
        PaintHomeCurve(g);
    }

    private void PaintHomeRemaining(Graphics g)
    {
        var weekly = snapshot?.Weekly;
        Draw(g, Label("7 天剩余额度", "7d quota left"), small, Muted, 39, 90);
        Draw(g, weekly is null ? "--%" : $"{weekly.RemainingPercent:0}%",
            hero, StatusColor(), 39, 108);
        Bar(g, new Rectangle(39, 216, 266, 8), weekly?.RemainingPercent ?? 0, StatusColor());
        var reset = weekly?.ResetsAt;
        var detail = Label("重置", "Reset") + " " + Countdown(reset) + " · " +
            (reset?.ToLocalTime().ToString("MM/dd HH:mm") ?? "--");
        DrawFitted(g, detail, small, Muted, new RectangleF(39, 238, 266, 18));
    }

    private void PaintHomeToday(Graphics g)
    {
        var now = DateTimeOffset.UtcNow;
        var weekly = snapshot?.Weekly;
        var pace = QuotaPaceCalculator.Evaluate(weekly, now);
        var todayUsed = snapshot is null ? null : history.TodayUsed(snapshot, now);
        var metricColor = todayUsed is null ? Muted : pace.Level switch
        {
            QuotaPaceLevel.OverBudget or QuotaPaceLevel.BudgetUsed => Red,
            QuotaPaceLevel.NearBudget => Yellow,
            _ => Cyan
        };
        Draw(g, Label("今日已用", "Used today"), small, Muted, 355, 90);
        if (pace.DayStart is not null)
            DrawFitted(g, Label("起点", "Since") + " " +
                pace.DayStart.Value.ToLocalTime().ToString("HH:mm"),
                small, Muted, new RectangleF(521, 90, 99, 18));
        Draw(g, todayUsed is null ? "--%" : $"{todayUsed:0.#}%",
            hero, metricColor, 355, 108);
        Bar(g, new Rectangle(355, 216, 266, 8), todayUsed ?? 0, metricColor);

        string detail;
        if (todayUsed is null)
            detail = Label("缺少额度数据", "Quota data unavailable");
        else if (pace.BalancePercent is < 0)
            detail = Label("超额", "Over") + " " +
                $"{Math.Abs(pace.BalancePercent.Value):0.#}% · " +
                Label("预算", "Budget") + " " + $"{pace.BudgetPercent:0.#}%";
        else
            detail = Label("可用", "Available") + " " +
                $"{pace.BalancePercent:0.#}% · " +
                Label("预算", "Budget") + " " + $"{pace.BudgetPercent:0.#}%";
        DrawFitted(g, detail, small, Muted, new RectangleF(355, 238, 266, 18));
    }

    private void PaintHomeCurve(Graphics g)
    {
        Draw(g, Label("近 7 天每日 token 趋势", "Daily token trend · 7 days"),
            medium, TextColor, 38, 290);
        Draw(g, Label("官方数据", "Codex data"), small, Muted, 545, 296);
        if (snapshot?.DailyUsage is null)
        {
            Center(g, Label("Codex 暂未提供每日用量", "Daily usage unavailable"),
                regular, Muted, new Rectangle(38, 322, 584, 55));
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var first = today.AddDays(-6);
        var values = Enumerable.Range(0, 7)
            .Select(day => snapshot.DailyUsage.FirstOrDefault(item => item.Date == first.AddDays(day))?.Tokens)
            .ToArray();
        var maximum = Math.Max(1, values.Where(value => value.HasValue)
            .Select(value => value!.Value).DefaultIfEmpty(0).Max());
        using (var guide = new Pen(Color.FromArgb(53, 72, 86)))
            g.DrawLine(guide, 62, 378, 611, 378);
        using var line = new Pen(Green, 2.5f);
        using var dot = new SolidBrush(Green);
        PointF? previous = null;
        for (var day = 0; day < 7; day++)
        {
            var x = 76 + day * 84;
            if (values[day] is long tokens)
            {
                var point = new PointF(x, 370 - (float)(tokens / (double)maximum * 43));
                if (previous is not null) g.DrawLine(line, previous.Value, point);
                g.FillEllipse(dot, point.X - 3.5f, point.Y - 3.5f, 7, 7);
                previous = point;
            }
            else
            {
                Center(g, "—", regular, Muted, new Rectangle(x - 16, 349, 32, 20));
                previous = null;
            }
            Draw(g, first.AddDays(day).ToString("MM/dd"), small,
                first.AddDays(day) == today ? Cyan : Muted, x - 17, 386);
        }
    }

    private void PaintTrends(Graphics g)
    {
        FillPanel(g, new Rectangle(20, 76, 620, 153), Panel, 17);
        FillPanel(g, new Rectangle(20, 240, 620, 174), Panel, 17);
        PaintDailyTrend(g);
        PaintHourlyTrend(g);
    }

    private void PaintDailyTrend(Graphics g)
    {
        Draw(g, Label("近 7 天每日 token 用量", "Daily tokens · last 7 days"), medium, TextColor, 38, 85);
        Draw(g, Label("官方数据", "Codex data"), small, Muted, 545, 90);
        if (snapshot?.DailyUsage is null)
        {
            Center(g, Label("Codex 暂未提供每日用量", "Daily usage unavailable"),
                regular, Muted, new Rectangle(38, 124, 584, 60));
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var first = today.AddDays(-6);
        var values = new long?[7];
        for (var day = 0; day < 7; day++)
        {
            var bucket = snapshot.DailyUsage.FirstOrDefault(item => item.Date == first.AddDays(day));
            values[day] = bucket?.Tokens;
        }
        var maximum = Math.Max(1, values.Where(value => value.HasValue)
            .Select(value => value!.Value).DefaultIfEmpty(0).Max());
        using (var guide = new Pen(Color.FromArgb(53, 72, 86)))
            g.DrawLine(guide, 63, 185, 612, 185);
        for (var day = 0; day < 7; day++)
        {
            var date = first.AddDays(day);
            var x = 74 + day * 78;
            if (values[day] is long tokens)
            {
                var height = tokens == 0 ? 0 : Math.Max(3, (int)Math.Round(43.0 * tokens / maximum));
                if (height > 0)
                    FillPanel(g, new Rectangle(x, 185 - height, 39, height),
                        date == selectedDate ? Cyan : Green, 4);
                Center(g, FormatTokens(tokens), small, TextColor,
                    new Rectangle(x - 16, 121, 70, 18));
            }
            else
                Center(g, "—", regular, Muted, new Rectangle(x, 151, 39, 25));
            Draw(g, date.ToString("MM/dd"), small, date == selectedDate ? Cyan : Muted, x - 2, 190);
        }
    }

    private void PaintHourlyTrend(Graphics g)
    {
        Draw(g, selectedDate.ToString("MM/dd") + " " +
            Label("逐小时 7 天额度已用", "Hourly 7d quota used"), medium, TextColor, 38, 249);
        Draw(g, Label("本机采样", "Local samples"), small, Muted, 545, 254);
        var values = Enumerable.Range(0, 24)
            .Select(hour => history.SampleForHour(selectedDate, hour)).ToArray();
        if (!values.Any(value => value is not null))
        {
            Center(g, Label("此日暂无采样；保持程序运行后逐小时累积",
                    "No samples yet; keep the app running"),
                regular, Muted, new Rectangle(38, 292, 584, 65));
            return;
        }

        var maximum = Math.Min(100, Math.Max(10, Math.Ceiling(
            values.Where(value => value is not null).Max(value => value!.UsedPercent) / 10) * 10));
        using (var guide = new Pen(Color.FromArgb(53, 72, 86)))
        {
            g.DrawLine(guide, 58, 370, 614, 370);
            g.DrawLine(guide, 58, 294, 58, 370);
            g.DrawLine(guide, 58, 332, 614, 332);
        }
        Draw(g, maximum.ToString("0") + "%", small, Muted, 59, 278);
        Draw(g, (maximum / 2).ToString("0") + "%", small, Muted, 28, 322);
        using var line = new Pen(Cyan, 2.5f);
        using var dot = new SolidBrush(Cyan);
        PointF? previous = null;
        for (var hour = 0; hour < 24; hour++)
        {
            if (values[hour] is not { } sample)
            {
                previous = null;
                continue;
            }
            var point = new PointF(64 + hour * 23.5f,
                370 - (float)(sample.UsedPercent / maximum * 69));
            if (previous is not null) g.DrawLine(line, previous.Value, point);
            g.FillEllipse(dot, point.X - 3, point.Y - 3, 6, 6);
            previous = point;
        }
        foreach (var hour in new[] { 0, 6, 12, 18, 23 })
            Draw(g, hour.ToString("00"), small, Muted, 55 + hour * 23.5f, 378);
        Draw(g, Label("缺失时段不估算", "Missing hours are not estimated"), small, Muted, 429, 275);
    }

    private static string FormatTokens(long value) => value >= 1_000_000
        ? (value / 1_000_000d).ToString("0.#") + "M"
        : value >= 1_000 ? (value / 1_000d).ToString("0.#") + "K" : value.ToString();

    private string Countdown(DateTimeOffset? reset)
    {
        if (reset is null) return "--";
        var remaining = reset.Value - DateTimeOffset.UtcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return remaining.TotalDays >= 1 ? $"{(int)remaining.TotalDays}d {remaining.Hours}h"
            : remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
            : $"{remaining.Minutes}m";
    }

    private Color StatusColor()
    {
        var remaining = snapshot?.Weekly?.RemainingPercent;
        if (error is not null || remaining is null || remaining < 20) return Red;
        if (remaining < 35) return Yellow;
        return Green;
    }

    private static void FillPanel(Graphics g, Rectangle bounds, Color color, int radius)
    {
        using var path = Rounded(bounds, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        if (radius < 1 || rectangle.Width < 2 || rectangle.Height < 2)
        {
            path.AddRectangle(rectangle);
            return path;
        }
        var arc = radius * 2;
        path.AddArc(rectangle.X, rectangle.Y, arc, arc, 180, 90);
        path.AddArc(rectangle.Right - arc, rectangle.Y, arc, arc, 270, 90);
        path.AddArc(rectangle.Right - arc, rectangle.Bottom - arc, arc, arc, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - arc, arc, arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void Bar(Graphics g, Rectangle bounds, double percent, Color color)
    {
        FillPanel(g, bounds, Color.FromArgb(45, 59, 72), bounds.Height / 2);
        var width = (int)Math.Round(bounds.Width * Math.Clamp(percent, 0, 100) / 100);
        if (width > 0) FillPanel(g, new Rectangle(bounds.X, bounds.Y, width, bounds.Height),
            color, Math.Min(bounds.Height / 2, width / 2));
    }

    private static void Draw(Graphics g, string text, Font font, Color color, float x, float y)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y);
    }

    private static void DrawFitted(Graphics g, string text, Font font, Color color, RectangleF bounds)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(text, font, brush, bounds, format);
    }

    private static void Center(Graphics g, string text, Font font, Color color, Rectangle bounds)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, bounds, format);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            var origin = WidgetOrigin();
            var scale = WidgetScale();
            var x = (e.X - origin.X) / scale;
            var y = (e.Y - origin.Y) / scale;
            if (!trendMode && x >= 20 && x <= 640 && y >= 280 && y <= 414)
            {
                trendMode = true;
                UpdateButtons();
                Invalidate();
                return;
            }
            if (trendMode && y >= 120 && y <= 220 && x >= 66 && x < 612)
            {
                var day = Math.Clamp((int)((x - 66) / 78), 0, 6);
                selectedDate = DateOnly.FromDateTime(DateTime.Today).AddDays(day - 6);
                Invalidate();
                return;
            }
        }
        if (e.Button == MouseButtons.Left && e.Y - WidgetOrigin().Y < 68 && !fullScreen)
        {
            ReleaseCapture();
            SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
            normalBounds = Bounds;
        }
    }

    private string PreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexQuotaWidgetLite", "settings.json");

    private void LoadPreferences()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath));
            if (settings is null) return;
            english = settings.English;
            pinned = settings.Pinned;
            restoreFullScreen = settings.FullScreen;
        }
        catch (IOException) { }
        catch (JsonException) { }
    }

    private void SavePreferences()
    {
        var path = PreferencesPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Preferences(english, pinned, fullScreen)));
    }

    private sealed record Preferences(bool English, bool Pinned, bool FullScreen);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        refreshTimer.Stop();
        clockTimer.Stop();
        tray.Visible = false;
        tray.Dispose();
        trayPercentIcon?.Dispose();
        tooltips.Dispose();
        regular.Dispose();
        small.Dispose();
        medium.Dispose();
        hero.Dispose();
        scaledButtonFont?.Dispose();
        base.OnFormClosed(e);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int width, int height, uint flags);
}

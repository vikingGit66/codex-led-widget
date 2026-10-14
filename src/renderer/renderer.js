const AUTO_REFRESH_MS = 60 * 1000;
const WIDGET_WIDTH = 660;
const WIDGET_HEIGHT = 460;
const DAILY_ALERT_THRESHOLD = 20;

const state = {
  language: localStorage.getItem("codexQuotaLanguage") || "zh",
  quota: null,
  alwaysOnTop: true,
  fullScreen: false,
  loading: false,
  nextRefreshAt: null,
  refreshTimer: null
};

const i18n = {
  zh: {
    brand: "Codex 额度监控",
    loading: "正在读取本机额度",
    ready: "额度已同步",
    empty: "额度已用完",
    warning: "额度偏低",
    dailyAlert: "日预算已用尽",
    dailyAlertOver: "已超日预算",
    error: "读取失败",
    sync: "同步中",
    healthy: "运行良好",
    caution: "注意节奏",
    alert: "红色警报",
    remaining: "剩余额度",
    secondary: "7天窗口",
    used: "已用",
    resetTime: "重置时间",
    currentUpdate: "本次更新",
    nextReset: "下次重置",
    plan: "计划",
    paceTitle: "7天额度节奏 · 20%/天",
    resetCountdown: "重置倒计时",
    quotaUsedPace: "额度已用",
    timePassedPace: "时间已过",
    paceLimit: "累计预算",
    dailyStatusSafe: "节奏正常",
    dailyStatusWarn: "接近日预算",
    dailyStatusAlert: "日预算已用尽",
    dailyStatusOver: "已超日预算",
    budgetAvailable: "可用",
    budgetOver: "超额",
    dailyHintSafe: "已用额度未达到今日累计预算",
    dailyHintWarn: "剩余日预算不足 5 个百分点",
    dailyHintAlert: "已达到今日累计预算",
    refresh: "刷新",
    hide: "隐藏",
    exit: "退出",
    pinOn: "取消置顶",
    pinOff: "置顶",
    fullscreenOn: "退出全屏",
    fullscreenOff: "全屏（遮挡任务栏）",
    statusLoading: "正在读取 Codex 额度...",
    statusReady: "上次更新",
    nextRefresh: "下次刷新",
    statusNoData: "没有可显示的额度数据",
    statusError: "无法读取 Codex 额度",
    unavailable: "不可用",
    unknown: "未知",
    now: "刚刚",
    languageButton: "EN"
  },
  en: {
    brand: "Codex Quota Monitor",
    loading: "Reading local quota",
    ready: "Quota synced",
    empty: "Quota exhausted",
    warning: "Quota is low",
    dailyAlert: "Daily budget used",
    dailyAlertOver: "Over daily budget",
    error: "Read failed",
    sync: "Syncing",
    healthy: "Healthy",
    caution: "Watch pace",
    alert: "Red alert",
    remaining: "Remaining",
    secondary: "7d window",
    used: "Used",
    resetTime: "Reset time",
    currentUpdate: "Updated",
    nextReset: "Next reset",
    plan: "Plan",
    paceTitle: "7d quota pace · 20%/day",
    resetCountdown: "Reset countdown",
    quotaUsedPace: "Quota used",
    timePassedPace: "Time passed",
    paceLimit: "Cumulative budget",
    dailyStatusSafe: "On budget",
    dailyStatusWarn: "Near daily budget",
    dailyStatusAlert: "Daily budget used",
    dailyStatusOver: "Over daily budget",
    budgetAvailable: "Available",
    budgetOver: "Over",
    dailyHintSafe: "Usage is below today's cumulative budget",
    dailyHintWarn: "Less than 5 percentage points of budget remain",
    dailyHintAlert: "Today's cumulative budget has been reached",
    refresh: "Refresh",
    hide: "Hide",
    exit: "Quit",
    pinOn: "Unpin",
    pinOff: "Pin",
    fullscreenOn: "Exit fullscreen",
    fullscreenOff: "Fullscreen (cover taskbar)",
    statusLoading: "Reading Codex quota...",
    statusReady: "Updated",
    nextRefresh: "Next refresh",
    statusNoData: "No quota data available",
    statusError: "Unable to read Codex quota",
    unavailable: "N/A",
    unknown: "Unknown",
    now: "just now",
    languageButton: "中"
  }
};

const $ = (id) => document.getElementById(id);

const elements = {
  body: document.body,
  widget: $("widget"),
  trafficLight: $("trafficLight"),
  brandName: $("brandName"),
  stateText: $("stateText"),
  healthPill: $("healthPill"),
  langBtn: $("langBtn"),
  pinBtn: $("pinBtn"),
  fullscreenBtn: $("fullscreenBtn"),
  refreshBtn: $("refreshBtn"),
  minimizeBtn: $("minimizeBtn"),
  closeBtn: $("closeBtn"),
  liquidFill: $("liquidFill"),
  remaining: $("remaining"),
  remainingLabel: $("remainingLabel"),
  currentUpdateLabel: $("currentUpdateLabel"),
  currentUpdateText: $("currentUpdateText"),
  nextResetLabel: $("nextResetLabel"),
  nextResetText: $("nextResetText"),
  planLabel: $("planLabel"),
  planText: $("planText"),
  secondaryLabel: $("secondaryLabel"),
  secondaryRemaining: $("secondaryRemaining"),
  secondaryBar: $("secondaryBar"),
  secondaryUsedLabel: $("secondaryUsedLabel"),
  secondaryUsed: $("secondaryUsed"),
  secondaryResetLabel: $("secondaryResetLabel"),
  secondaryReset: $("secondaryReset"),
  dailyRiskCard: $("dailyRiskCard"),
  paceLabel: $("paceLabel"),
  secondaryCountdownLabel: $("secondaryCountdownLabel"),
  secondaryCountdown: $("secondaryCountdown"),
  quotaUsedPaceLabel: $("quotaUsedPaceLabel"),
  quotaUsedPace: $("quotaUsedPace"),
  quotaUsedPaceBar: $("quotaUsedPaceBar"),
  timePassedPaceLabel: $("timePassedPaceLabel"),
  timePassedPace: $("timePassedPace"),
  timePassedPaceBar: $("timePassedPaceBar"),
  paceLimitLabel: $("paceLimitLabel"),
  paceLimit: $("paceLimit"),
  paceLimitBar: $("paceLimitBar"),
  dailyStatus: $("dailyStatus"),
  dailyHint: $("dailyHint"),
  statusDot: $("statusDot"),
  statusText: $("statusText"),
  nextRefreshText: $("nextRefreshText")
};

function t(key) {
  return i18n[state.language][key] || key;
}

function clampPercent(value) {
  if (!Number.isFinite(value)) return null;
  return Math.max(0, Math.min(100, Math.round(value)));
}

function setVisualState(visualState, riskState = "safe") {
  elements.body.dataset.state = visualState;
  elements.body.dataset.risk = riskState;

  for (const element of [elements.trafficLight, elements.statusDot]) {
    element.classList.remove("loading", "good", "warn", "empty", "error");
    element.classList.add(visualState);
  }

  elements.liquidFill.classList.remove("warn", "empty", "error", "alert");
  if (visualState === "warn" || visualState === "empty" || visualState === "error") {
    elements.liquidFill.classList.add(visualState);
  }
}

function visualStateForWeeklyRemaining(percent) {
  if (percent === null || percent === undefined || Number.isNaN(percent)) return "error";
  if (percent < 20) return "empty";
  if (percent < 35) return "warn";
  return "good";
}

function setPercent(percent) {
  const safePercent = clampPercent(percent);
  if (safePercent === null) {
    elements.remaining.textContent = "--%";
    elements.liquidFill.style.height = "8%";
    return;
  }

  elements.remaining.textContent = `${safePercent}%`;
  elements.liquidFill.style.height = `${Math.max(8, safePercent)}%`;
}

function formatPercent(value) {
  const percent = clampPercent(value);
  return percent === null ? "--%" : `${percent}%`;
}

function formatBudgetBalance(value) {
  return Number.isFinite(value) ? (Math.round(Math.abs(value) * 10) / 10) + "%" : "--%";
}

function formatDateTime(value) {
  if (!value) return "--";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "--";

  return new Intl.DateTimeFormat(state.language === "zh" ? "zh-CN" : "en-US", {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit"
  }).format(date);
}

function formatTime(value) {
  if (!value) return "--";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "--";

  return new Intl.DateTimeFormat(state.language === "zh" ? "zh-CN" : "en-US", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  }).format(date);
}

function formatDurationUntil(value) {
  if (!value) return "--";
  const target = new Date(value).getTime();
  if (Number.isNaN(target)) return "--";

  const totalMinutes = Math.max(0, Math.ceil((target - Date.now()) / 60000));
  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor((totalMinutes % 1440) / 60);
  const minutes = totalMinutes % 60;

  if (state.language === "zh") {
    if (days > 0) return `${days}天 ${hours}小时`;
    if (hours > 0) return `${hours}小时 ${minutes}分`;
    return `${minutes}分钟`;
  }

  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m`;
}

function getWeeklyWindow(quota) {
  if (!quota) return null;
  return [quota.weekly, quota.secondary, quota.primary].find(
    (window) => Number(window?.windowDurationMins) >= 6 * 24 * 60
  ) || (quota.secondary?.windowDurationMins == null ? quota.secondary : null);
}

function getNextReset(quota) {
  const value = getWeeklyWindow(quota)?.resetsAt;
  if (!value) return null;
  const date = new Date(value);
  return !Number.isNaN(date.getTime()) && date.getTime() > Date.now() ? date : null;
}

function calculateWeeklyPace(quota) {
  const source = getWeeklyWindow(quota);
  if (!source || !source.resetsAt || !source.windowDurationMins) {
    return null;
  }

  if (Number(source.windowDurationMins) < 24 * 60) {
    return null;
  }

  const resetAt = new Date(source.resetsAt).getTime();
  if (Number.isNaN(resetAt)) return null;

  const windowMs = Number(source.windowDurationMins) * 60 * 1000;
  const startAt = resetAt - windowMs;
  const elapsedMs = Math.max(0, Math.min(Date.now() - startAt, windowMs));
  const dayNumber = Math.min(7, Math.floor(elapsedMs / 86400000) + 1);
  const timePassedPercent = (elapsedMs / windowMs) * 100;
  const safeLimitPercent = Math.min(100, dayNumber * DAILY_ALERT_THRESHOLD);
  const usedPercent = Number(source.usedPercent || 0);

  if (!Number.isFinite(usedPercent)) return null;

  const budgetBalancePercent = safeLimitPercent - usedPercent;
  const risk =
    budgetBalancePercent <= 0
      ? "alert"
      : budgetBalancePercent <= 5
        ? "warn"
        : "safe";

  return {
    risk,
    usedPercent,
    timePassedPercent,
    safeLimitPercent,
    budgetBalancePercent,
    resetsAt: source.resetsAt
  };
}

function renderCopy() {
  elements.brandName.textContent = t("brand");
  elements.remainingLabel.textContent = t("remaining");
  elements.currentUpdateLabel.textContent = t("currentUpdate");
  elements.nextResetLabel.textContent = t("nextReset");
  elements.planLabel.textContent = t("plan");
  elements.secondaryLabel.textContent = t("secondary");
  elements.secondaryUsedLabel.textContent = t("used");
  elements.secondaryResetLabel.textContent = t("resetTime");
  elements.paceLabel.textContent = t("paceTitle");
  elements.secondaryCountdownLabel.textContent = t("resetCountdown");
  elements.quotaUsedPaceLabel.textContent = t("quotaUsedPace");
  elements.timePassedPaceLabel.textContent = t("timePassedPace");
  elements.paceLimitLabel.textContent = t("paceLimit");
  elements.langBtn.textContent = t("languageButton");
  elements.refreshBtn.title = t("refresh");
  elements.refreshBtn.setAttribute("aria-label", t("refresh"));
  elements.minimizeBtn.title = t("hide");
  elements.minimizeBtn.setAttribute("aria-label", t("hide"));
  elements.closeBtn.title = t("exit");
  elements.closeBtn.setAttribute("aria-label", t("exit"));
  renderPin();
  renderFullScreen();
}

function renderPin() {
  const label = state.alwaysOnTop ? t("pinOn") : t("pinOff");
  elements.pinBtn.classList.toggle("active", state.alwaysOnTop);
  elements.pinBtn.title = label;
  elements.pinBtn.setAttribute("aria-label", label);
}

function updateWidgetScale() {
  const scale = state.fullScreen
    ? Math.max(0.1, Math.min(window.innerWidth / WIDGET_WIDTH, window.innerHeight / WIDGET_HEIGHT))
    : 1;
  elements.widget.style.setProperty("--widget-scale", String(scale));
}

function renderFullScreen() {
  const label = state.fullScreen ? t("fullscreenOn") : t("fullscreenOff");
  elements.fullscreenBtn.classList.toggle("active", state.fullScreen);
  elements.fullscreenBtn.title = label;
  elements.fullscreenBtn.setAttribute("aria-label", label);
  elements.fullscreenBtn.setAttribute("aria-pressed", String(state.fullScreen));
  elements.body.dataset.fullscreen = String(state.fullScreen);
  updateWidgetScale();
}

function renderWindow(prefix, windowInfo) {
  const remainingElement = elements[`${prefix}Remaining`];
  const usedElement = elements[`${prefix}Used`];
  const resetElement = elements[`${prefix}Reset`];
  const barElement = elements[`${prefix}Bar`];

  if (!windowInfo) {
    remainingElement.textContent = "--";
    usedElement.textContent = "--";
    resetElement.textContent = "--";
    barElement.style.width = "0%";
    return;
  }

  remainingElement.textContent = formatPercent(windowInfo.remainingPercent);
  usedElement.textContent = formatPercent(windowInfo.usedPercent);
  resetElement.textContent = formatDateTime(windowInfo.resetsAt);
  barElement.style.width = `${clampPercent(windowInfo.usedPercent) ?? 0}%`;
}

function renderWeeklyPace(pace) {
  const risk = pace?.risk || "safe";
  elements.dailyRiskCard.dataset.risk = risk;

  if (!pace) {
    elements.dailyStatus.textContent = t("statusNoData");
    elements.dailyHint.textContent = `${t("paceTitle")} --`;
    elements.secondaryCountdown.textContent = "--";
    elements.quotaUsedPace.textContent = "--";
    elements.timePassedPace.textContent = "--";
    elements.paceLimit.textContent = "--";
    elements.quotaUsedPaceBar.style.width = "0%";
    elements.timePassedPaceBar.style.width = "0%";
    elements.paceLimitBar.style.width = "0%";
    return;
  }

  elements.secondaryCountdown.textContent = formatDurationUntil(pace.resetsAt);
  elements.quotaUsedPace.textContent = formatPercent(pace.usedPercent);
  elements.timePassedPace.textContent = formatPercent(pace.timePassedPercent);
  elements.paceLimit.textContent = formatPercent(pace.safeLimitPercent);
  elements.quotaUsedPaceBar.style.width = `${clampPercent(pace.usedPercent) ?? 0}%`;
  elements.timePassedPaceBar.style.width = `${clampPercent(pace.timePassedPercent) ?? 0}%`;
  elements.paceLimitBar.style.width = `${clampPercent(pace.safeLimitPercent) ?? 0}%`;

  const overBudget = pace.budgetBalancePercent < 0;
  const hint = t(overBudget ? "budgetOver" : "budgetAvailable") + " " +
    formatBudgetBalance(pace.budgetBalancePercent);

  if (risk === "alert") {
    elements.dailyStatus.textContent = t(overBudget ? "dailyStatusOver" : "dailyStatusAlert");
    elements.dailyHint.textContent = hint;
    return;
  }

  if (risk === "warn") {
    elements.dailyStatus.textContent = t("dailyStatusWarn");
    elements.dailyHint.textContent = hint;
    return;
  }

  elements.dailyStatus.textContent = t("dailyStatusSafe");
  elements.dailyHint.textContent = hint;
}

function renderQuota() {
  renderCopy();

  if (state.loading) {
    setVisualState("loading");
    elements.healthPill.textContent = t("sync");
    elements.stateText.textContent = t("loading");
    elements.statusText.textContent = t("statusLoading");
    renderNextRefresh();
    return;
  }

  const quota = state.quota;
  if (!quota) {
    setVisualState("error");
    setPercent(null);
    renderWindow("secondary", null);
    renderWeeklyPace(null);
    elements.healthPill.textContent = t("error");
    elements.stateText.textContent = t("error");
    elements.nextResetText.textContent = "--";
    elements.currentUpdateText.textContent = "--";
    elements.planText.textContent = t("unknown");
    elements.statusText.textContent = t("statusNoData");
    renderNextRefresh();
    return;
  }

  const pace = calculateWeeklyPace(quota);
  const weekly = getWeeklyWindow(quota);
  const percent = weekly?.remainingPercent ?? null;
  const visualState = visualStateForWeeklyRemaining(percent);

  setVisualState(visualState, pace?.risk || "safe");
  setPercent(percent);
  renderWindow("secondary", weekly);
  renderWeeklyPace(pace);

  const nextReset = getNextReset(quota);
  elements.currentUpdateText.textContent = formatDateTime(quota.fetchedAt);
  elements.nextResetText.textContent = nextReset ? formatDateTime(nextReset.toISOString()) : "--";
  elements.planText.textContent = quota.planType || quota.limitName || t("unknown");

  if (visualState === "error") {
    elements.healthPill.textContent = t("error");
    elements.stateText.textContent = t("statusNoData");
  } else if (visualState === "empty") {
    elements.healthPill.textContent = t("alert");
    elements.stateText.textContent = t("empty");
  } else if (pace?.risk === "alert") {
    elements.healthPill.textContent = t("alert");
    elements.stateText.textContent = t(pace.budgetBalancePercent < 0 ? "dailyAlertOver" : "dailyAlert");
  } else if (visualState === "warn") {
    elements.healthPill.textContent = t("caution");
    elements.stateText.textContent = t("warning");
  } else {
    elements.healthPill.textContent = t("healthy");
    elements.stateText.textContent = t("ready");
  }

  elements.statusText.textContent = `${t("statusReady")} ${formatTime(quota.fetchedAt)}`;
  renderNextRefresh();
}

function renderNextRefresh() {
  elements.nextRefreshText.textContent = `${t("nextRefresh")} ${formatTime(state.nextRefreshAt)}`;
  const weeklyReset = getWeeklyWindow(state.quota)?.resetsAt;
  if (weeklyReset && elements.secondaryCountdown) {
    elements.secondaryCountdown.textContent = formatDurationUntil(weeklyReset);
  }
}

function renderError(error) {
  state.loading = false;
  state.quota = null;
  renderQuota();
  elements.statusText.textContent = `${t("statusError")}: ${error.message || error}`;
}

function scheduleNextRefresh() {
  if (state.refreshTimer) {
    clearTimeout(state.refreshTimer);
  }
  state.nextRefreshAt = new Date(Date.now() + AUTO_REFRESH_MS).toISOString();
  renderNextRefresh();
  state.refreshTimer = setTimeout(refreshQuota, AUTO_REFRESH_MS);
}

async function refreshQuota() {
  if (!window.codexQuota || state.loading) return;

  state.loading = true;
  renderQuota();

  try {
    state.quota = await window.codexQuota.getQuota();
    state.loading = false;
    scheduleNextRefresh();
    renderQuota();
  } catch (error) {
    scheduleNextRefresh();
    renderError(error);
  }
}

async function init() {
  window.addEventListener("resize", updateWidgetScale);
  renderCopy();
  setInterval(renderNextRefresh, 1000);

  if (window.codexQuota?.getAlwaysOnTop) {
    state.alwaysOnTop = await window.codexQuota.getAlwaysOnTop();
    renderPin();
  }

  elements.langBtn.addEventListener("click", () => {
    state.language = state.language === "zh" ? "en" : "zh";
    localStorage.setItem("codexQuotaLanguage", state.language);
    renderQuota();
  });

  elements.pinBtn.addEventListener("click", async () => {
    state.alwaysOnTop = await window.codexQuota.setAlwaysOnTop(!state.alwaysOnTop);
    renderPin();
  });

  elements.fullscreenBtn.addEventListener("click", async () => {
    state.fullScreen = await window.codexQuota.setFullScreen(!state.fullScreen);
    localStorage.setItem("codexQuotaFullScreen", String(state.fullScreen));
    renderFullScreen();
  });

  document.addEventListener("keydown", async (event) => {
    if (event.key === "Escape" && state.fullScreen) {
      state.fullScreen = await window.codexQuota.setFullScreen(false);
      localStorage.setItem("codexQuotaFullScreen", "false");
      renderFullScreen();
    }
  });

  elements.refreshBtn.addEventListener("click", refreshQuota);
  elements.minimizeBtn.addEventListener("click", () => window.codexQuota?.minimize());
  elements.closeBtn.addEventListener("click", () => window.codexQuota?.close());
  window.codexQuota?.onRefresh(refreshQuota);
  window.codexQuota?.onFullScreenChanged((value) => {
    state.fullScreen = value;
    localStorage.setItem("codexQuotaFullScreen", String(value));
    renderFullScreen();
  });
  window.codexQuota?.onAlwaysOnTopChanged((value) => {
    state.alwaysOnTop = value;
    renderPin();
  });

  if (window.codexQuota?.getFullScreen) {
    state.fullScreen = await window.codexQuota.getFullScreen();
    if (!state.fullScreen && localStorage.getItem("codexQuotaFullScreen") === "true") {
      state.fullScreen = await window.codexQuota.setFullScreen(true);
    }
    renderFullScreen();
  }

  refreshQuota();
}

document.addEventListener("DOMContentLoaded", init);

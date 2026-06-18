const AUTO_REFRESH_MS = 60 * 1000;
const DAILY_ALERT_THRESHOLD = 20;

const state = {
  language: localStorage.getItem("codexQuotaLanguage") || "zh",
  quota: null,
  alwaysOnTop: true,
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
    dailyAlert: "7天节奏过快",
    error: "读取失败",
    sync: "同步中",
    healthy: "运行良好",
    caution: "注意节奏",
    alert: "红色警报",
    remaining: "剩余额度",
    primary: "5小时窗口",
    secondary: "7天窗口",
    used: "已用",
    resetTime: "重置时间",
    currentUpdate: "本次更新",
    nextReset: "下次重置",
    plan: "计划",
    paceTitle: "7天额度节奏",
    resetCountdown: "重置倒计时",
    quotaUsedPace: "额度已用",
    timePassedPace: "时间已过",
    paceLimit: "安全上限",
    dailyStatusSafe: "节奏稳定",
    dailyStatusWarn: "接近安全上限",
    dailyStatusAlert: "已超过安全上限",
    dailyHintSafe: "实际用量仍在时间节奏内",
    dailyHintWarn: "建议放慢高强度调用",
    dailyHintAlert: "实际用量已跑在时间前面",
    refresh: "刷新",
    hide: "隐藏",
    exit: "退出",
    pinOn: "取消置顶",
    pinOff: "置顶",
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
    dailyAlert: "7d pace is high",
    error: "Read failed",
    sync: "Syncing",
    healthy: "Healthy",
    caution: "Watch pace",
    alert: "Red alert",
    remaining: "Remaining",
    primary: "5h window",
    secondary: "7d window",
    used: "Used",
    resetTime: "Reset time",
    currentUpdate: "Updated",
    nextReset: "Next reset",
    plan: "Plan",
    paceTitle: "7d quota pace",
    resetCountdown: "Reset countdown",
    quotaUsedPace: "Quota used",
    timePassedPace: "Time passed",
    paceLimit: "Safe limit",
    dailyStatusSafe: "Pace is stable",
    dailyStatusWarn: "Near the safe limit",
    dailyStatusAlert: "Above the safe limit",
    dailyHintSafe: "Actual usage is within the time pace",
    dailyHintWarn: "Consider slowing heavy requests",
    dailyHintAlert: "Actual usage is ahead of schedule",
    refresh: "Refresh",
    hide: "Hide",
    exit: "Quit",
    pinOn: "Unpin",
    pinOff: "Pin",
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
  trafficLight: $("trafficLight"),
  brandName: $("brandName"),
  stateText: $("stateText"),
  healthPill: $("healthPill"),
  langBtn: $("langBtn"),
  pinBtn: $("pinBtn"),
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
  primaryLabel: $("primaryLabel"),
  primaryRemaining: $("primaryRemaining"),
  primaryBar: $("primaryBar"),
  primaryUsedLabel: $("primaryUsedLabel"),
  primaryUsed: $("primaryUsed"),
  primaryResetLabel: $("primaryResetLabel"),
  primaryReset: $("primaryReset"),
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

function visualStateForPrimaryRemaining(percent) {
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

function getNextReset(quota) {
  const dates = [quota?.primary?.resetsAt, quota?.secondary?.resetsAt]
    .map((value) => (value ? new Date(value) : null))
    .filter((date) => date && !Number.isNaN(date.getTime()) && date.getTime() > Date.now())
    .sort((left, right) => left.getTime() - right.getTime());

  return dates[0] || null;
}

function calculateWeeklyPace(quota) {
  const source = quota?.secondary;
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
  const elapsedDays = Math.max(elapsedMs / 86400000, 1 / 24);
  const timePassedPercent = (elapsedMs / windowMs) * 100;
  const safeLimitPercent = Math.min(100, elapsedDays * DAILY_ALERT_THRESHOLD);
  const usedPercent = Number(source.usedPercent || 0);
  const dailyRate = Number(source.usedPercent || 0) / elapsedDays;

  if (!Number.isFinite(dailyRate)) return null;

  const risk =
    dailyRate > DAILY_ALERT_THRESHOLD
      ? "alert"
      : dailyRate >= DAILY_ALERT_THRESHOLD * 0.75
        ? "warn"
        : "safe";

  return {
    risk,
    dailyRate,
    usedPercent,
    timePassedPercent,
    safeLimitPercent,
    resetsAt: source.resetsAt
  };
}

function renderCopy() {
  elements.brandName.textContent = t("brand");
  elements.remainingLabel.textContent = t("remaining");
  elements.currentUpdateLabel.textContent = t("currentUpdate");
  elements.nextResetLabel.textContent = t("nextReset");
  elements.planLabel.textContent = t("plan");
  elements.primaryLabel.textContent = t("primary");
  elements.secondaryLabel.textContent = t("secondary");
  elements.primaryUsedLabel.textContent = t("used");
  elements.secondaryUsedLabel.textContent = t("used");
  elements.primaryResetLabel.textContent = t("resetTime");
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
}

function renderPin() {
  const label = state.alwaysOnTop ? t("pinOn") : t("pinOff");
  elements.pinBtn.classList.toggle("active", state.alwaysOnTop);
  elements.pinBtn.title = label;
  elements.pinBtn.setAttribute("aria-label", label);
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

  const hint = `${t("quotaUsedPace")} ${formatPercent(pace.usedPercent)} / ${t("paceLimit")} ${formatPercent(
    pace.safeLimitPercent
  )}`;

  if (risk === "alert") {
    elements.dailyStatus.textContent = t("dailyStatusAlert");
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
    renderWindow("primary", null);
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
  const percent = quota.primary?.remainingPercent ?? quota.remainingPercent;
  const visualState = visualStateForPrimaryRemaining(percent);

  setVisualState(visualState, pace?.risk || "safe");
  setPercent(percent);
  renderWindow("primary", quota.primary);
  renderWindow("secondary", quota.secondary);
  renderWeeklyPace(pace);

  const nextReset = getNextReset(quota);
  elements.currentUpdateText.textContent = formatDateTime(quota.fetchedAt);
  elements.nextResetText.textContent = nextReset ? formatDateTime(nextReset.toISOString()) : "--";
  elements.planText.textContent = quota.planType || quota.limitName || t("unknown");

  if (visualState === "empty") {
    elements.healthPill.textContent = t("alert");
    elements.stateText.textContent = t("empty");
  } else if (pace?.risk === "alert") {
    elements.healthPill.textContent = t("alert");
    elements.stateText.textContent = t("dailyAlert");
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
  if (state.quota?.secondary?.resetsAt && elements.secondaryCountdown) {
    elements.secondaryCountdown.textContent = formatDurationUntil(state.quota.secondary.resetsAt);
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

  elements.refreshBtn.addEventListener("click", refreshQuota);
  elements.minimizeBtn.addEventListener("click", () => window.codexQuota?.minimize());
  elements.closeBtn.addEventListener("click", () => window.codexQuota?.close());
  window.codexQuota?.onRefresh(refreshQuota);
  window.codexQuota?.onAlwaysOnTopChanged((value) => {
    state.alwaysOnTop = value;
    renderPin();
  });

  refreshQuota();
}

document.addEventListener("DOMContentLoaded", init);

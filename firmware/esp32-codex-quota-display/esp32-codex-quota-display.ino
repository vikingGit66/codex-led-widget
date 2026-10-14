#include <WiFi.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <time.h>

// Install ArduinoJson from Arduino IDE Library Manager.
// Board target: ESP32 Dev Module or your matching ESP32 board.

const char *WIFI_SSID = "YOUR_WIFI_SSID";
const char *WIFI_PASSWORD = "YOUR_WIFI_PASSWORD";

// Run the desktop widget on the same LAN, then replace the IP below.
// Health check: http://PC_LAN_IP:34280/health
const char *DATA_URL = "http://192.168.1.100:34280/quota";

constexpr uint32_t FETCH_INTERVAL_MS = 60UL * 1000UL;
constexpr uint32_t HTTP_TIMEOUT_MS = 10000UL;
constexpr uint32_t WIFI_CONNECT_TIMEOUT_MS = 15000UL;

// DC32480S035 TTL UART. ESP32 TX -> screen RX, ESP32 RX -> screen TX.
constexpr int SCREEN_RX_PIN = 16;
constexpr int SCREEN_TX_PIN = 17;
constexpr uint32_t SCREEN_BAUD = 115200;

constexpr int LOCAL_TIME_OFFSET_SECONDS = 8 * 3600;
constexpr int DAILY_ALERT_THRESHOLD = 20;
constexpr int PRIMARY_REMAINING_ALERT = 20;
constexpr int PRIMARY_REMAINING_WARN = 35;

HardwareSerial ScreenSerial(2);

struct QuotaWindow {
  bool valid = false;
  int usedPercent = 0;
  int remainingPercent = 0;
  int windowDurationMins = 0;
  int64_t resetEpoch = 0;
};

struct WeeklyPace {
  bool valid = false;
  int timePassedPercent = 0;
  int safeLimitPercent = 0;
  float dailyRate = 0.0f;
  int risk = 0;
};

struct QuotaSnapshot {
  String planType = "unknown";
  String limitName = "Codex";
  int64_t fetchedEpoch = 0;
  QuotaWindow primary;
  QuotaWindow secondary;
};

bool firstFetch = true;
uint32_t lastFetchAt = 0;

void setup() {
  Serial.begin(115200);
  ScreenSerial.begin(SCREEN_BAUD, SERIAL_8N1, SCREEN_RX_PIN, SCREEN_TX_PIN);

  delay(300);
  Serial.println();
  Serial.println("Codex quota ESP32 display bridge");
  sendBootFrame();

  WiFi.mode(WIFI_STA);
  configTime(LOCAL_TIME_OFFSET_SECONDS, 0, "ntp.aliyun.com", "pool.ntp.org", "time.google.com");
  connectWiFi();
}

void loop() {
  const uint32_t now = millis();
  if (firstFetch || now - lastFetchAt >= FETCH_INTERVAL_MS) {
    firstFetch = false;
    lastFetchAt = now;
    fetchAndSendQuota();
  }

  delay(50);
}

void connectWiFi() {
  if (WiFi.status() == WL_CONNECTED) return;

  Serial.printf("Connecting WiFi: %s\n", WIFI_SSID);
  WiFi.disconnect(true);
  delay(100);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

  const uint32_t startedAt = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - startedAt < WIFI_CONNECT_TIMEOUT_MS) {
    delay(300);
    Serial.print(".");
  }
  Serial.println();

  if (WiFi.status() == WL_CONNECTED) {
    Serial.printf("WiFi connected, IP=%s\n", WiFi.localIP().toString().c_str());
  } else {
    Serial.println("WiFi connection failed");
  }
}

void fetchAndSendQuota() {
  connectWiFi();

  if (WiFi.status() != WL_CONNECTED) {
    sendErrorFrame("WIFI", "WiFi disconnected");
    return;
  }

  HTTPClient http;
  http.setTimeout(HTTP_TIMEOUT_MS);

  Serial.printf("GET %s\n", DATA_URL);
  if (!http.begin(DATA_URL)) {
    sendErrorFrame("HTTP", "http.begin failed");
    return;
  }

  const int code = http.GET();
  if (code != HTTP_CODE_OK) {
    String message = "HTTP ";
    message += code;
    if (code > 0) {
      message += " ";
      message += http.getString();
    }
    http.end();
    sendErrorFrame("HTTP", message);
    return;
  }

  const String payload = http.getString();
  http.end();

  QuotaSnapshot snapshot;
  if (!parseQuota(payload, snapshot)) {
    sendErrorFrame("JSON", "Invalid quota JSON");
    return;
  }

  sendQuotaFrame(snapshot);
}

bool parseQuota(const String &payload, QuotaSnapshot &snapshot) {
  StaticJsonDocument<4096> doc;
  DeserializationError error = deserializeJson(doc, payload);
  if (error) {
    Serial.printf("JSON parse failed: %s\n", error.c_str());
    return false;
  }

  snapshot.planType = String(doc["planType"] | "unknown");
  snapshot.limitName = String(doc["limitName"] | "Codex");
  snapshot.fetchedEpoch = parseIsoUtc(doc["fetchedAt"] | "");
  if (snapshot.fetchedEpoch <= 0) {
    snapshot.fetchedEpoch = parseIsoUtc(doc["servedAt"] | "");
  }

  snapshot.primary = parseQuotaWindow(doc["primary"].as<JsonObject>());
  snapshot.secondary = parseQuotaWindow(doc["secondary"].as<JsonObject>());

  return snapshot.primary.valid || snapshot.secondary.valid;
}

QuotaWindow parseQuotaWindow(JsonObject source) {
  QuotaWindow window;
  if (source.isNull()) return window;

  window.valid = true;
  window.usedPercent = clampPercent(source["usedPercent"] | 0);
  window.remainingPercent = clampPercent(source["remainingPercent"] | (100 - window.usedPercent));
  window.windowDurationMins = source["windowDurationMins"] | 0;
  window.resetEpoch = parseIsoUtc(source["resetsAt"] | "");
  return window;
}

void sendQuotaFrame(const QuotaSnapshot &snapshot) {
  const int64_t nowEpoch = currentEpoch(snapshot.fetchedEpoch);
  const WeeklyPace pace = calculateWeeklyPace(snapshot.secondary, nowEpoch);
  const int primaryRisk = calculatePrimaryRisk(snapshot.primary);
  const int overallRisk = max(primaryRisk, pace.risk);

  String frame;
  frame.reserve(320);
  frame += "$CQ";
  appendField(frame, "PR", snapshot.primary.valid ? snapshot.primary.remainingPercent : 0);
  appendField(frame, "PU", snapshot.primary.valid ? snapshot.primary.usedPercent : 0);
  appendField(frame, "PCT", countdownUntil(snapshot.primary.resetEpoch, nowEpoch));
  appendField(frame, "PRT", formatLocalDateTime(snapshot.primary.resetEpoch));
  appendField(frame, "SR", snapshot.secondary.valid ? snapshot.secondary.remainingPercent : 0);
  appendField(frame, "SU", snapshot.secondary.valid ? snapshot.secondary.usedPercent : 0);
  appendField(frame, "SCT", countdownUntil(snapshot.secondary.resetEpoch, nowEpoch));
  appendField(frame, "SRT", formatLocalDateTime(snapshot.secondary.resetEpoch));
  appendField(frame, "TP", pace.valid ? pace.timePassedPercent : 0);
  appendField(frame, "SAFE", pace.valid ? pace.safeLimitPercent : 0);
  appendField(frame, "DR", formatFloat(pace.valid ? pace.dailyRate : 0.0f, 1));
  appendField(frame, "R5", primaryRisk);
  appendField(frame, "R7", pace.valid ? pace.risk : 0);
  appendField(frame, "RISK", overallRisk);
  appendField(frame, "PLAN", snapshot.planType.length() ? snapshot.planType : snapshot.limitName);
  appendField(frame, "UP", formatLocalDateTime(snapshot.fetchedEpoch));
  appendField(frame, "IP", WiFi.localIP().toString());
  frame += "#";

  sendFrame(frame);
}

WeeklyPace calculateWeeklyPace(const QuotaWindow &window, int64_t nowEpoch) {
  WeeklyPace pace;
  if (!window.valid || window.windowDurationMins < 24 * 60 || window.resetEpoch <= 0 || nowEpoch <= 0) {
    return pace;
  }

  const int64_t windowSeconds = (int64_t)window.windowDurationMins * 60;
  const int64_t startEpoch = window.resetEpoch - windowSeconds;
  const int64_t elapsedSeconds = constrainInt64(nowEpoch - startEpoch, 0, windowSeconds);
  const float elapsedDays = max((float)elapsedSeconds / 86400.0f, 1.0f / 24.0f);

  pace.valid = true;
  pace.timePassedPercent = clampPercent((int)round(((float)elapsedSeconds / (float)windowSeconds) * 100.0f));
  pace.safeLimitPercent = clampPercent((int)round(elapsedDays * DAILY_ALERT_THRESHOLD));
  pace.dailyRate = (float)window.usedPercent / elapsedDays;

  if (window.usedPercent > pace.safeLimitPercent || pace.dailyRate > DAILY_ALERT_THRESHOLD) {
    pace.risk = 2;
  } else if (pace.dailyRate >= DAILY_ALERT_THRESHOLD * 0.75f ||
             window.usedPercent >= (int)round(pace.safeLimitPercent * 0.85f)) {
    pace.risk = 1;
  } else {
    pace.risk = 0;
  }

  return pace;
}

int calculatePrimaryRisk(const QuotaWindow &window) {
  if (!window.valid) return 0;
  if (window.remainingPercent < PRIMARY_REMAINING_ALERT) return 2;
  if (window.remainingPercent < PRIMARY_REMAINING_WARN) return 1;
  return 0;
}

void sendBootFrame() {
  String frame = "$CQ;BOOT=1;MSG=ESP32 started;IP=0.0.0.0#";
  sendFrame(frame);
}

void sendErrorFrame(const String &code, const String &message) {
  String frame;
  frame.reserve(160);
  frame += "$CQ";
  appendField(frame, "ERR", code);
  appendField(frame, "MSG", message);
  appendField(frame, "IP", WiFi.status() == WL_CONNECTED ? WiFi.localIP().toString() : String("0.0.0.0"));
  frame += "#";

  sendFrame(frame);
}

void sendFrame(const String &frame) {
  ScreenSerial.println(frame);
  Serial.println(frame);
}

void appendField(String &frame, const char *key, const String &value) {
  frame += ";";
  frame += key;
  frame += "=";
  frame += sanitizeField(value);
}

void appendField(String &frame, const char *key, int value) {
  appendField(frame, key, String(value));
}

String sanitizeField(String value) {
  for (size_t i = 0; i < value.length(); i++) {
    const char c = value.charAt(i);
    if (c == ';' || c == '#' || c == '\r' || c == '\n' || c == '=') {
      value.setCharAt(i, '_');
    }
  }
  return value;
}

String formatFloat(float value, int decimals) {
  char buffer[16];
  dtostrf(value, 0, decimals, buffer);
  return String(buffer);
}

int clampPercent(int value) {
  if (value < 0) return 0;
  if (value > 100) return 100;
  return value;
}

int64_t constrainInt64(int64_t value, int64_t low, int64_t high) {
  if (value < low) return low;
  if (value > high) return high;
  return value;
}

int64_t currentEpoch(int64_t fallbackEpoch) {
  const time_t now = time(nullptr);
  if ((int64_t)now > 1700000000LL) return (int64_t)now;
  return fallbackEpoch > 0 ? fallbackEpoch : 0;
}

String countdownUntil(int64_t targetEpoch, int64_t nowEpoch) {
  if (targetEpoch <= 0 || nowEpoch <= 0) return "--";
  return formatCountdown(targetEpoch - nowEpoch);
}

String formatCountdown(int64_t seconds) {
  if (seconds <= 0) return "0m";

  const int64_t days = seconds / 86400;
  seconds %= 86400;
  const int64_t hours = seconds / 3600;
  seconds %= 3600;
  const int64_t minutes = (seconds + 59) / 60;

  char buffer[24];
  if (days > 0) {
    snprintf(buffer, sizeof(buffer), "%lldd %lldh", days, hours);
  } else if (hours > 0) {
    snprintf(buffer, sizeof(buffer), "%lldh %lldm", hours, minutes);
  } else {
    snprintf(buffer, sizeof(buffer), "%lldm", minutes);
  }
  return String(buffer);
}

String formatLocalDateTime(int64_t epoch) {
  if (epoch <= 0) return "--";

  const time_t localEpoch = (time_t)(epoch + LOCAL_TIME_OFFSET_SECONDS);
  struct tm timeInfo;
  gmtime_r(&localEpoch, &timeInfo);

  char buffer[18];
  snprintf(
    buffer,
    sizeof(buffer),
    "%02d/%02d %02d:%02d",
    timeInfo.tm_mon + 1,
    timeInfo.tm_mday,
    timeInfo.tm_hour,
    timeInfo.tm_min
  );
  return String(buffer);
}

int64_t parseIsoUtc(const char *iso) {
  if (!iso || !iso[0]) return 0;

  int year = 0;
  int month = 0;
  int day = 0;
  int hour = 0;
  int minute = 0;
  int second = 0;

  if (sscanf(iso, "%d-%d-%dT%d:%d:%d", &year, &month, &day, &hour, &minute, &second) != 6) {
    return 0;
  }

  const int64_t days = daysFromCivil(year, month, day);
  return days * 86400LL + hour * 3600LL + minute * 60LL + second;
}

// Howard Hinnant civil calendar conversion. Returns days since 1970-01-01 UTC.
int64_t daysFromCivil(int year, unsigned month, unsigned day) {
  year -= month <= 2;
  const int shiftedMonth = (int)month + (month > 2 ? -3 : 9);
  const int era = (year >= 0 ? year : year - 399) / 400;
  const unsigned yoe = (unsigned)(year - era * 400);
  const unsigned doy = (153 * (unsigned)shiftedMonth + 2) / 5 + day - 1;
  const unsigned doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
  return era * 146097LL + (int64_t)doe - 719468LL;
}

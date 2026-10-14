# ESP32 + DC32480S035 Codex quota display

这个目录用于把桌面端 Codex 额度同步到独立串口屏：

1. 桌面端 Electron 应用读取 Codex 额度。
2. 桌面端开启局域网接口：`http://电脑IP:34280/quota`。
3. ESP32 通过 WiFi 每 60 秒请求一次 JSON。
4. ESP32 计算 5 小时剩余额度、7 天额度节奏、重置倒计时和风险等级。
5. ESP32 通过 UART TTL 给大彩 DC32480S035 发送一帧 ASCII 数据。
6. 串口屏 Lua 解析数据并刷新文本、进度条和红色警报图标。

## 目录

- `esp32-codex-quota-display/esp32-codex-quota-display.ino`：ESP32 Arduino 工程。
- `dacai-dc32480s035/codex_quota_screen.lua`：大彩 VisualTFT/Lua 屏端模板。

## 电脑端局域网接口

桌面应用启动后会监听：

```text
http://电脑IP:34280/quota
http://电脑IP:34280/health
```

`/quota` 返回字段示例：

```json
{
  "ok": true,
  "planType": "prolite",
  "primary": {
    "usedPercent": 13,
    "remainingPercent": 87,
    "windowDurationMins": 300,
    "resetsAt": "2026-06-19T06:37:00.000Z"
  },
  "secondary": {
    "usedPercent": 26,
    "remainingPercent": 74,
    "windowDurationMins": 10080,
    "resetsAt": "2026-06-25T10:28:00.000Z"
  },
  "fetchedAt": "2026-06-19T02:16:00.000Z"
}
```

如果 ESP32 访问不到，请检查：

- 电脑和 ESP32 是否在同一个 WiFi/LAN。
- Windows 防火墙是否允许此应用的局域网访问。
- `http://电脑IP:34280/health` 能否在手机或另一台电脑浏览器打开。
- 端口冲突时可设置环境变量 `CODEX_QUOTA_LAN_PORT`。
- 不想开启局域网接口时可设置 `CODEX_QUOTA_LAN_ENABLED=false`。

## ESP32 配置

打开 `.ino` 后先改这三项：

```cpp
const char *WIFI_SSID = "YOUR_WIFI_SSID";
const char *WIFI_PASSWORD = "YOUR_WIFI_PASSWORD";
const char *DATA_URL = "http://192.168.1.100:34280/quota";
```

Arduino IDE 需要安装：

- ESP32 board package
- ArduinoJson

默认串口：

```cpp
SCREEN_RX_PIN = 16
SCREEN_TX_PIN = 17
SCREEN_BAUD = 115200
```

## DC32480S035 接线

按 TTL 串口接线：

```text
ESP32 GND      -> 屏 GND
ESP32 GPIO17 TX2 -> 屏 RX
ESP32 GPIO16 RX2 -> 屏 TX
屏 VCC         -> 按屏规格供电
```

注意：

- ESP32 和屏必须共地。
- DC32480S035 如果是 TTL 版本，可直接接 ESP32 UART。
- 如果买到 RS232/RS485 版本，需要电平转换模块，不能直接接 ESP32。
- 屏的背光电流通常比 ESP32 大，不建议只靠 ESP32 小板 3.3V 供电。

## UART 数据帧

ESP32 发给屏的是 ASCII 帧，便于串口助手调试：

```text
$CQ;PR=87;PU=13;PCT=2h 11m;PRT=06/19 06:37;SR=74;SU=26;SCT=5d 8h;SRT=06/25 10:28;TP=21;SAFE=29;DR=17.8;R5=0;R7=1;RISK=1;PLAN=prolite;UP=06/19 02:16;IP=192.168.1.23#
```

字段含义：

| 字段 | 含义 |
| --- | --- |
| `PR` / `PU` | 5 小时窗口剩余/已用百分比 |
| `PCT` / `PRT` | 5 小时窗口重置倒计时/重置时间 |
| `SR` / `SU` | 7 天窗口剩余/已用百分比 |
| `SCT` / `SRT` | 7 天窗口重置倒计时/重置时间 |
| `TP` | 7 天窗口时间已过百分比 |
| `SAFE` | 按每天 20% 计算出的当前安全上限 |
| `DR` | 当前 7 天额度折算的日均消耗速率 |
| `R5` | 5 小时风险：0 正常，1 提醒，2 红色 |
| `R7` | 7 天风险：0 正常，1 提醒，2 红色 |
| `RISK` | 总风险：取 `R5` 和 `R7` 的较高值 |
| `PLAN` | 当前计划 |
| `UP` | 本次更新时间 |
| `IP` | ESP32 当前 IP |

## 告警逻辑

- 5 小时剩余额度低于 `20%`：红色警报。
- 5 小时剩余额度低于 `35%`：黄色提醒。
- 7 天窗口按每天 `20%` 做安全线：`SAFE = 已过天数 * 20%`。
- 7 天已用超过 `SAFE`，或日均消耗 `DR > 20%/天`：红色警报。
- 7 天日均消耗接近 `15%/天`，或已用接近安全线：黄色提醒。

## 大彩屏项目

真实大彩工程已经放在：

```text
2.display/Project/Project.tftprj
```

当前工程使用 `Screen0`，Lua 默认 `SCREEN_ID = 0`。页面已经使用纯文本控件实现 320x480 看板，便于 VisualTFT 稳定打开和下载。

| 控件 ID | 用途 |
| --- | --- |
| 20 | 顶部状态文本 |
| 21 | 5 小时剩余百分比 |
| 22 | 5 小时已用百分比 |
| 23 | 5 小时重置倒计时 |
| 24 | 5 小时重置时间 |
| 25 | 7 天剩余百分比 |
| 26 | 7 天已用百分比 |
| 27 | 7 天重置倒计时 |
| 28 | 7 天重置时间 |
| 29 | 7 天额度已用条形进度 |
| 30 | 7 天时间已过条形进度 |
| 31 | 7 天安全上限条形进度 |
| 32 | 7 天状态文本 |
| 33 | 7 天提示文本 |
| 34 | 计划文本 |
| 35 | 本次更新时间 |
| 36 | ESP32 IP |
| 37 | 日均消耗 |
| 38/39/40 | 红/黄/绿状态点 |
| 41 | 最近一帧调试文本 |

如果你后续在 VisualTFT 里重新排版控件，只需要同步修改 `main.lua` 顶部的 `C = { ... }` 映射。

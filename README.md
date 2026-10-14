# HHY-Windows-Codex额度指示器-LED

## 项目介绍

Windows Codex 额度指示器，包含 Electron 桌面端、.NET 原生轻量版，以及 ESP32/串口屏独立显示方案。桌面端获取额度，局域网接口提供 JSON，ESP32 读取并通过串口驱动屏幕。

## 主要功能

- Electron 桌面显示与刷新额度。
- .NET 原生轻量版打包。
- 局域网 GET /quota 提供额度，GET /health 检查服务。
- ESP32 默认每 60 秒取一次 JSON，串口屏解析并显示。
- 桌面设置和固件各自实现额度节奏、重置时间及提示逻辑。

## 运行环境

| 组件 | 配置 |
|---|---|
| 桌面 | Windows；Node.js/npm；原生版需 .NET |
| 局域网 | 默认 0.0.0.0:34280 |
| ESP32 | Arduino 工程，依赖 ArduinoJson |
| 串口屏 | 大彩 DC32480S035，VisualTFT/Lua |
| ESP32 屏幕 UART | 默认 RX16/TX17，实际见固件配置 |

![项目资料图](assets/1.png)

## 软件设计

| 路径 | 职责 |
|---|---|
| src/main/quota-service.js | 额度获取 |
| src/main/lan-quota-server.js | HTTP /quota 与 /health |
| src/renderer | 桌面界面 |
| native | 原生轻量版 |
| firmware/esp32-codex-quota-display | ESP32 拉取和屏幕通信 |
| firmware/dacai-dc32480s035 | 屏端 Lua 模板 |
| 2.display | 串口屏工程资料 |

源码与资料入口：

- [package.json](package.json)
- [src/main/lan-quota-server.js](src/main/lan-quota-server.js)
- [firmware/README.md](firmware/README.md)
- [1.docs/BUILD_AND_IMPLEMENTATION.md](1.docs/BUILD_AND_IMPLEMENTATION.md)

## 安装与运行

在仓库根目录：

```powershell
npm install
npm start
npm run build
# 原生轻量版
npm run build:lite
```

`package.json` 的轻量版命令使用 .NET 发布到 dist/lite-standalone。ESP32 固件用 Arduino IDE 打开同名 .ino，安装 ESP32 支持包与 ArduinoJson；屏端工程按 VisualTFT/Lua 环境处理。

## 使用说明

1. 启动桌面应用，检查额度读取。
2. 在同一局域网用浏览器访问 `http://电脑IP:34280/health` 与 `/quota`。
3. 在 ESP32 固件中配置 Wi-Fi 和 DATA_URL，核对屏幕 UART 引脚。
4. 检查屏端 Lua 解析格式与 ESP32 发出的 ASCII 帧一致。
5. 端口可由 CODEX_QUOTA_LAN_PORT 指定；CODEX_QUOTA_LAN_ENABLED=false 可关闭局域网接口。

## 目录结构

| 目录 | 内容 |
|---|---|
| [1.docs/](1.docs/) | 说明与器件资料 |
| [2.display/](2.display/) | 串口屏工程 |
| [src/](src/) | Electron 源码 |
| [native/](native/) | .NET 原生实现 |
| [firmware/](firmware/) | 设备固件与屏幕脚本 |
| [tests/](tests/) | 现有测试 |
| [assets/](assets/) | 界面素材 |

## 已知问题

- 桌面端与独立屏幕的额度计算口径需分别核对，不默认认为两者完全相同。
- 局域网接口不可达时先检查网络、端口占用及应用监听状态。
- 仓库目录仍保留原名 HHY_codex-led-widget，程序运行路径保持不变。

原创代码及配套文档采用 [MIT 许可证](LICENSE)，适用范围见 [许可说明](LICENSE_SCOPE.md)。

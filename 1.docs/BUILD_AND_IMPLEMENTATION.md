# Codex LED Widget 打包与实现说明

本文档说明本项目如何打包成可运行的 Windows 软件、核心技术实现、功能点，以及本次修改内容。

## 1. 项目定位

Codex LED Widget 是一个 Windows 桌面悬浮小组件，用于读取本机 Codex 的额度状态，并用红 / 黄 / 绿灯和百分比展示剩余额度。

项目技术栈：

- Electron
- JavaScript
- HTML / CSS
- electron-builder

应用不内置 Codex，也不上传额度数据。它只调用本机已经安装并登录的 Codex CLI，通过本地 `app-server` stdio 协议读取额度信息。

## 2. 运行条件

开发 / 打包电脑需要：

- Windows 10 / Windows 11 x64
- Node.js 和 npm
- 可以访问 npm registry 与 GitHub release 下载 Electron 运行时

最终用户电脑需要：

- Windows 10 / Windows 11 x64
- 已安装 Codex
- Codex 已在本机登录

最终 exe 是便携版，用户不需要安装 Node.js、npm 或 `node_modules`。

## 3. 安装依赖

第一次拉取项目后，在项目根目录执行：

```powershell
npm install
```

这会安装：

- `electron`
- `electron-builder`
- 以及它们的依赖

本次已生成 `package-lock.json`，后续同环境安装依赖会更稳定。

## 4. 本地开发运行

开发模式运行：

```powershell
npm run dev
```

或：

```powershell
npm start
```

这两个脚本都会执行：

```powershell
electron .
```

Electron 入口是：

```txt
src/main/main.js
```

## 5. 打包命令

打包 Windows 便携版 exe：

```powershell
npm run build
```

对应 `package.json` 中的脚本：

```json
"build": "set CSC_IDENTITY_AUTO_DISCOVERY=false&& electron-builder --win portable"
```

打包完成后，产物在：

```txt
dist/Codex-Quota-Widget-0.1.0-win-x64.exe
```

当前已验证的最终产物路径：

```txt
E:\product\@gitee\HHY_codex-led-widget\dist\Codex-Quota-Widget-0.1.0-win-x64.exe
```

解包目录也会生成：

```txt
dist/win-unpacked/
```

其中可直接运行的程序是：

```txt
dist/win-unpacked/Codex Quota Widget.exe
```

## 6. 打包配置要点

打包配置位于 `package.json` 的 `build` 字段：

```json
"build": {
  "appId": "cn.codex.quota.widget",
  "productName": "Codex Quota Widget",
  "directories": {
    "output": "dist"
  },
  "files": [
    "src/**/*",
    "package.json"
  ],
  "win": {
    "signAndEditExecutable": false,
    "target": [
      {
        "target": "portable",
        "arch": ["x64"]
      }
    ]
  },
  "portable": {
    "artifactName": "Codex-Quota-Widget-${version}-win-x64.exe"
  }
}
```

关键点：

- `target: portable`：生成单文件便携版 exe。
- `arch: x64`：面向 64 位 Windows。
- `signAndEditExecutable: false`：不做代码签名。
- `CSC_IDENTITY_AUTO_DISCOVERY=false`：关闭自动查找签名证书，避免无证书环境打包失败。
- `files` 只包含 `src/**/*` 和 `package.json`，因此渲染层 JS/CSS 必须放在 `src` 目录下。

注意：当前没有代码签名，所以用户第一次打开 exe 时，Windows 可能提示未知发布者。选择“更多信息” -> “仍要运行”即可。

## 7. 功能实现说明

### 7.1 主进程

主进程文件：

```txt
src/main/main.js
```

负责：

- 创建无边框透明悬浮窗口。
- 设置窗口大小、位置、置顶状态。
- 创建系统托盘菜单。
- 注册 IPC 接口给渲染进程调用。
- 调用额度服务读取 Codex 额度。

主要 IPC：

- `quota:get`：读取 Codex 额度。
- `window:minimize`：隐藏窗口。
- `window:close`：退出应用。
- `window:alwaysOnTop:get`：读取置顶状态。
- `window:alwaysOnTop:set`：设置置顶状态。
- `external:openCodex`：打开本机 Codex CLI 路径。

### 7.2 预加载脚本

预加载文件：

```txt
src/main/preload.js
```

负责通过 `contextBridge` 暴露安全 API 给页面使用：

```js
window.codexQuota.getQuota()
window.codexQuota.minimize()
window.codexQuota.close()
window.codexQuota.getAlwaysOnTop()
window.codexQuota.setAlwaysOnTop(value)
window.codexQuota.openCodex()
```

渲染页面不能直接访问 Node.js，只能通过这些白名单 API 与主进程通信。

### 7.3 额度读取服务

额度服务文件：

```txt
src/main/quota-service.js
```

核心流程：

1. 解析本机 Codex CLI 的真实路径。
2. 启动：

   ```txt
   codex app-server --listen stdio://
   ```

3. 通过 stdio 发送初始化请求：

   ```txt
   initialize
   ```

4. 读取额度：

   ```txt
   account/rateLimits/read
   ```

5. 将返回数据标准化为前端好显示的结构：

   - `primary`
   - `secondary`
   - `remainingPercent`
   - `usedPercent`
   - `resetsAt`
   - `planType`
   - `fetchedAt`

### 7.4 Codex CLI 路径解析

本次修复的关键点在这里。

旧逻辑只查：

```txt
%LOCALAPPDATA%\OpenAI\Codex\bin\codex.exe
```

但当前 Codex 实际路径是类似：

```txt
%LOCALAPPDATA%\OpenAI\Codex\bin\f1c7ee7a13db5fed\codex.exe
```

同时，WindowsApps 里的 Codex 路径可能存在，但从 Electron / Node 子进程启动会报：

```txt
spawn EPERM
```

因此现在解析顺序是：

1. `CODEX_CLI_PATH` 环境变量。
2. 旧版固定路径 `%LOCALAPPDATA%\OpenAI\Codex\bin\codex.exe`。
3. 新版哈希目录路径 `%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe`。
4. PATH 中的 `codex.exe` / `codex.cmd` / `codex.bat`。
5. 跳过 WindowsApps 包路径，避免 `EPERM`。
6. 找不到时才回退到 `codex`。

### 7.5 渲染界面

页面文件：

```txt
src/renderer/index.html
src/renderer/renderer.js
src/renderer/styles.css
```

`renderer.js` 负责：

- 调用 `window.codexQuota.getQuota()` 读取额度。
- 根据剩余百分比设置状态：
  - 绿色：额度大于等于 10%。
  - 黄色：额度小于 10% 且大于 0。
  - 红色：额度为 0 或读取失败。
- 展示：
  - 剩余百分比。
  - 5 小时窗口。
  - 7 天窗口。
  - 计划类型。
  - 最近更新时间。
- 支持中英文切换。
- 支持刷新、隐藏、退出、置顶 / 取消置顶。

`styles.css` 负责：

- 透明玻璃拟态窗口。
- 液体百分比球形仪表。
- 红 / 黄 / 绿状态灯。
- 标题栏按钮样式。
- 额度卡片与底部状态栏布局。

## 8. 本次修复的问题

截图中的问题表现：

```txt
读取失败
无法读取 Codex 额度: Error invoking remote method 'quota:get'
```

排查结果：

- 应用已能启动，说明打包本身成功。
- 失败点在主进程 `quota:get`。
- 旧代码无法找到真实 Codex CLI。
- 回退到 WindowsApps 里的 Codex 路径后，Node 子进程启动失败：

  ```txt
  spawn EPERM
  ```

- 找到本机真实可执行文件：

  ```txt
  %LOCALAPPDATA%\OpenAI\Codex\bin\f1c7ee7a13db5fed\codex.exe
  ```

- 使用真实路径后，额度读取成功。

## 9. 本次我修改了哪些内容

### 修改：`src/main/quota-service.js`

新增 / 调整：

- 导出 `resolveCodexPath()`。
- 新增 `findLocalCodexBinaries()`，自动扫描：

  ```txt
  %LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe
  ```

- 新增 `findPathCodexBinaries()`，从 PATH 中查找 Codex。
- 新增 `isWindowsAppsPackagePath()`，跳过 WindowsApps 包路径。
- 新增 `getMtimeMs()`，多个本地 Codex 版本时优先使用最新的。

目的：

- 修复其他机器 / 当前机器上 Codex CLI 安装路径变化导致的读取失败。
- 避免 WindowsApps 路径触发 `spawn EPERM`。

### 修改：`src/main/main.js`

新增：

```js
const { getQuota, resolveCodexPath } = require("./quota-service");
```

调整：

```js
shell.openPath(resolveCodexPath());
```

目的：

- “打开 Codex”相关逻辑也使用同一套真实路径解析。

### 新增：`src/renderer/renderer.js`

实现：

- 额度读取。
- 状态更新。
- 中英文切换。
- 置顶按钮。
- 刷新按钮。
- 隐藏 / 退出按钮。
- 错误状态展示。

### 新增：`src/renderer/styles.css`

实现：

- 小组件完整 UI 样式。
- 玻璃拟态背景。
- 液体仪表动画。
- 状态灯颜色。
- 额度卡片布局。
- 按钮交互样式。

### 新增：`package-lock.json`

目的：

- 锁定依赖版本。
- 保证后续 `npm install` 更稳定。

### 生成：`dist/`

生成打包产物：

```txt
dist/Codex-Quota-Widget-0.1.0-win-x64.exe
dist/win-unpacked/
```

`dist/` 已在 `.gitignore` 中，不会进入源码提交。

## 10. 验证记录

已完成验证：

- `node --check src/main/main.js`
- `node --check src/main/quota-service.js`
- `node --check src/main/preload.js`
- `node --check src/renderer/renderer.js`
- 修复后真实调用 `getQuota()` 成功返回额度数据。
- `npm run build` 打包成功。
- 重新启动最终便携 exe 后，界面正常显示绿色状态和剩余额度。
- `npm audit --omit=dev`：`0 vulnerabilities`。

## 11. 常见问题

### 其他电脑可以直接运行 exe 吗？

可以。把下面这个文件复制过去即可：

```txt
dist/Codex-Quota-Widget-0.1.0-win-x64.exe
```

但对方电脑必须已经安装并登录 Codex。

### 其他电脑双击打不开怎么办？

优先发解压版运行包：

```txt
dist/Codex-Quota-Widget-0.1.0-win-x64-unpacked.zip
```

对方操作步骤：

1. 解压 zip。
2. 进入：

   ```txt
   win-unpacked/
   ```

3. 双击运行：

   ```txt
   Codex Quota Widget.exe
   ```

如果单文件便携版 `.exe` 打不开，常见原因是 Windows SmartScreen、杀毒软件或公司电脑策略拦截了自解压便携程序。解压版不需要运行自解压壳，通常更容易打开。

如果仍然打不开，继续检查：

- 是否是 Windows 10 / Windows 11 x64。
- 是否被 Windows 安全中心隔离。
- 右键 exe -> 属性 -> 如果看到“解除锁定”，勾选后应用。
- 不要放在网络盘、微信临时目录、压缩包内直接运行，先解压到桌面或普通英文路径。
- 如果弹出“未知发布者”，选择“更多信息” -> “仍要运行”。
- 如果能打开窗口但显示读取失败，说明程序已运行，问题是那台电脑没有安装 / 登录 Codex。

### 没安装 Codex 会怎样？

应用窗口可以打开，但额度读取会失败。

### 为什么第一次运行会提示未知发布者？

因为当前 exe 没有代码签名。这不影响运行，但 Windows 可能弹出 SmartScreen 提示。

### 如果某台电脑仍然读不到额度怎么办？

优先检查：

1. Codex 是否已安装。
2. Codex 是否已登录。
3. 是否存在真实 CLI：

   ```txt
   %LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe
   ```

4. 如果路径特殊，可以设置环境变量：

   ```txt
   CODEX_CLI_PATH
   ```

   指向真实的 `codex.exe`。

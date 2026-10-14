const { app, BrowserWindow, ipcMain, shell, Tray, Menu, nativeImage, screen } = require("electron");
const path = require("node:path");
const { getQuota, resolveCodexPath } = require("./quota-service");
const { createLanQuotaServer } = require("./lan-quota-server");

let mainWindow;
let tray;
let isAlwaysOnTop = true;
let isFullScreen = false;
let lanQuotaServer;

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 660,
    height: 460,
    minWidth: 660,
    minHeight: 460,
    frame: false,
    transparent: true,
    resizable: false,
    fullscreenable: true,
    alwaysOnTop: isAlwaysOnTop,
    skipTaskbar: false,
    show: false,
    backgroundColor: "#00000000",
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  mainWindow.on("enter-full-screen", () => syncFullScreen(true));
  mainWindow.on("leave-full-screen", () => syncFullScreen(false));
  mainWindow.loadFile(path.join(__dirname, "../renderer/index.html"));
  mainWindow.once("ready-to-show", () => {
    mainWindow.show();
    placeWindowTopRight();
  });
}

function placeWindowTopRight() {
  if (!mainWindow || mainWindow.isFullScreen()) return;
  const display = screen.getPrimaryDisplay();
  const { width, height } = mainWindow.getBounds();
  const { workArea } = display;
  mainWindow.setBounds({
    x: workArea.x + workArea.width - width - 24,
    y: workArea.y + 24,
    width,
    height
  });
}

function createTray() {
  const icon = nativeImage.createFromDataURL(
    "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAK0lEQVR42mNk+M9Qz0AEYBxVSFUBCzAyMjL8Z2BgYJjFqIGjBo4aOAIAgV4EfpO0k7EAAAAASUVORK5CYII="
  );
  tray = new Tray(icon);
  tray.setToolTip("Codex Quota Widget");
  rebuildTrayMenu();
  tray.on("click", toggleWindow);
}

function rebuildTrayMenu() {
  if (!tray) return;
  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: "显示/隐藏", click: toggleWindow },
      { label: "刷新额度", click: () => mainWindow?.webContents.send("quota:refresh") },
      {
        label: isFullScreen ? "退出全屏" : "全屏（遮挡任务栏）",
        click: () => setFullScreen(!isFullScreen)
      },
      {
        label: isAlwaysOnTop ? "取消置顶" : "置顶",
        click: () => setAlwaysOnTop(!isAlwaysOnTop)
      },
      { type: "separator" },
      { label: "退出", click: () => app.quit() }
    ])
  );
}

function setAlwaysOnTop(value) {
  isAlwaysOnTop = Boolean(value);
  if (mainWindow) {
    mainWindow.setAlwaysOnTop(isAlwaysOnTop || isFullScreen, isFullScreen ? "screen-saver" : "normal");
    mainWindow.webContents.send("window:alwaysOnTopChanged", isAlwaysOnTop);
  }
  rebuildTrayMenu();
  return isAlwaysOnTop;
}

function syncFullScreen(value) {
  isFullScreen = Boolean(value);
  if (mainWindow) {
    mainWindow.setAlwaysOnTop(isFullScreen || isAlwaysOnTop, isFullScreen ? "screen-saver" : "normal");
    mainWindow.webContents.send("window:fullScreenChanged", isFullScreen);
  }
  rebuildTrayMenu();
  return isFullScreen;
}

function setFullScreen(value) {
  if (!mainWindow) return isFullScreen;
  const next = Boolean(value);
  if (mainWindow.isFullScreen() !== next) mainWindow.setFullScreen(next);
  return syncFullScreen(next);
}

function toggleWindow() {
  if (!mainWindow) return;
  if (mainWindow.isVisible()) {
    mainWindow.hide();
  } else {
    mainWindow.show();
    mainWindow.focus();
  }
}

app.whenReady().then(() => {
  createWindow();
  createTray();
  startLanQuotaServer();

  ipcMain.handle("quota:get", async () => getQuota());
  ipcMain.handle("window:minimize", () => mainWindow?.hide());
  ipcMain.handle("window:close", () => app.quit());
  ipcMain.handle("window:alwaysOnTop:get", () => isAlwaysOnTop);
  ipcMain.handle("window:alwaysOnTop:set", (_event, value) => setAlwaysOnTop(value));
  ipcMain.handle("window:fullScreen:get", () => mainWindow?.isFullScreen() || false);
  ipcMain.handle("window:fullScreen:set", (_event, value) => setFullScreen(value));
  ipcMain.handle("external:openCodex", () => {
    shell.openPath(resolveCodexPath());
  });

  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

function startLanQuotaServer() {
  if (process.env.CODEX_QUOTA_LAN_ENABLED === "false") return;

  lanQuotaServer = createLanQuotaServer({ getQuota });
  lanQuotaServer.start();
}

app.on("before-quit", () => {
  lanQuotaServer?.stop();
});

app.on("window-all-closed", (event) => {
  event.preventDefault();
});

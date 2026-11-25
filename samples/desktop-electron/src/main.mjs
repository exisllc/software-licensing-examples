// Main process: owns licensing. The SDK needs Node APIs (hardware ID, file system), so it runs
// here, never in the renderer. The window talks to it through the small API in preload.cjs.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { app, BrowserWindow, ipcMain, shell, dialog } from 'electron';
import { LicenseManager, loadSettings } from './license.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
let license;
let settings;

app.whenReady().then(() => {
  try {
    settings = loadSettings(path.join(here, '..'));
  } catch (err) {
    dialog.showErrorBox('Acme Sketch: configuration', err.message);
    app.quit();
    return;
  }
  license = new LicenseManager(settings, {
    appVersion: app.getVersion(),                                        // from package.json
    cacheFile: path.join(app.getPath('userData'), 'license-cache.json'), // per-user app data
  });

  ipcMain.handle('license:check', () => license.check());
  ipcMain.handle('license:activate', (_e, key, email) => license.activate(String(key), String(email)));
  ipcMain.handle('license:deactivate', () => license.deactivate());
  ipcMain.handle('license:buy', () => settings.buyUrl && shell.openExternal(settings.buyUrl));

  const win = new BrowserWindow({
    width: 820, height: 560, title: `Acme Sketch ${app.getVersion()}`,
    webPreferences: { preload: path.join(here, 'preload.cjs'), contextIsolation: true, sandbox: true },
  });
  win.loadFile(path.join(here, 'index.html'));
});

app.on('window-all-closed', () => app.quit());

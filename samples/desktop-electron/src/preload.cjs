// Exposes four licensing calls to the window, nothing else. The access token stays in the main process.
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('license', {
  check: () => ipcRenderer.invoke('license:check'),
  activate: (key, email) => ipcRenderer.invoke('license:activate', key, email),
  deactivate: () => ipcRenderer.invoke('license:deactivate'),
  buy: () => ipcRenderer.invoke('license:buy'),
});

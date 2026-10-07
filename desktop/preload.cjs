'use strict';
const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('cfcSetup',Object.freeze({read:()=>ipcRenderer.invoke('setup:read'),folder:()=>ipcRenderer.invoke('setup:folder'),connect:config=>ipcRenderer.invoke('setup:connect',config)}));

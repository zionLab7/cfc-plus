'use strict';
const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('cfcLocalPortals',Object.freeze({
 devices:()=>ipcRenderer.invoke('portals:devices'),
 open:(profileId,portal,schoolId)=>ipcRenderer.invoke('portals:open',{profileId,portal,schoolId})
}));

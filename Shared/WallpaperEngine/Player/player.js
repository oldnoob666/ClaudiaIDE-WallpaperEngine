'use strict';
// Independent playback of WE's current selection; no desktop screen capture.
const target = document.getElementById('wallpaper');
const status = document.getElementById('status');
let scene = null, video = null, current = '', busy = false, paused = false;
function report(text) {
  status.textContent = text || '';
  if (window.chrome?.webview) window.chrome.webview.postMessage({ kind: 'wallpaper-status', text: text || 'playing', current });
}
function release() {
  if (scene) { scene.destroy(); scene = null; }
  if (video) { video.pause(); video.removeAttribute('src'); video.load(); video.remove(); video = null; }
  target.replaceChildren();
}
window.wallpaperControl = {
  opacity(value) { target.style.opacity = Math.max(0, Math.min(1, Number(value) || 0)); },
  pause(value) {
    paused = !!value;
    if (scene) paused ? scene.pause() : scene.resume();
    if (video) paused ? video.pause() : video.play().catch(() => {});
  },
  diagnostics() { return { current, paused, scene: scene?.stats || null, video: video ? { time: video.currentTime, ready: video.readyState, paused: video.paused } : null, error: status.textContent }; }
};
async function refresh() {
  if (busy || document.hidden || paused) return;
  busy = true;
  try {
    const response = await fetch('state', { cache: 'no-store', headers: { 'X-ClaudiaIDE': '1' } });
    if (!response.ok) throw new Error('壁纸状态读取失败：' + response.status);
    const selected = await response.json();
    if (!selected.id) { if (current) { release(); current = ''; } report(selected.error); return; }
    if (selected.id === current) return;
    release(); current = selected.id;
    report('正在加载：' + selected.title);
    if (selected.type === 'video') {
      video = document.createElement('video');
      video.muted = true; video.loop = true; video.autoplay = true; video.playsInline = true;
      video.src = selected.source + selected.entry.split('/').map(encodeURIComponent).join('/');
      video.addEventListener('playing', () => report(''));
      video.addEventListener('error', () => report('此视频无法播放；请检查视频编码。'));
      target.append(video); await video.play();
    } else {
      scene = WebWallGL.createScene(target, {
        fit: 'cover', fps: 30, volume: 0,
        onReady: () => report(''),
        onError: error => report('场景渲染失败：' + String(error?.message || error).slice(0, 200))
      });
      await scene.load(WebWallGL.httpSource(selected.source));
    }
    if (paused) window.wallpaperControl.pause(true);
  } catch (error) { report(error.message || String(error)); current = ''; }
  finally { busy = false; }
}
document.addEventListener('visibilitychange', () => {
  if (document.hidden) { if (scene) scene.pause(); if (video) video.pause(); }
  else { window.wallpaperControl.pause(paused); refresh(); }
});
window.addEventListener('pagehide', release);
setInterval(refresh, 2000);
refresh();

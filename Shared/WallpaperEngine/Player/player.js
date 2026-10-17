'use strict';
// Independent playback of WE's current selection; no desktop screen capture.
const target = document.getElementById('wallpaper');
const status = document.getElementById('status');
let scene = null, video = null, current = '', busy = false, paused = false;
let generation = 0, rendererTask = null;
function ready(value) {
  target.style.visibility = value ? 'visible' : 'hidden';
  if (window.chrome?.webview) window.chrome.webview.postMessage({ kind: 'wallpaper-ready', ready: value });
}
function renderer() {
  if (!rendererTask) rendererTask = new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = 'webwallgl.global.min.js';
    script.onload = () => resolve(window.WebWallGL);
    script.onerror = () => { rendererTask = null; script.remove(); reject(new Error('场景播放器加载失败')); };
    document.head.append(script);
  });
  return rendererTask;
}
function report(text) {
  status.textContent = text || '';
  if (window.chrome?.webview) window.chrome.webview.postMessage({ kind: 'wallpaper-status', text: text || 'playing', current });
}
function release() {
  ++generation;
  ready(false);
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
    const version = generation;
    const firstFrame = () => { if (version === generation) { ready(true); report(''); } };
    report('正在加载：' + selected.title);
    if (selected.type === 'video') {
      video = document.createElement('video');
      video.muted = true; video.loop = true; video.autoplay = true; video.playsInline = true;
      video.src = selected.source + selected.entry.split('/').map(encodeURIComponent).join('/');
      const playingVideo = video;
      video.addEventListener('playing', () => {
        if (version !== generation) return;
        if (playingVideo.requestVideoFrameCallback) playingVideo.requestVideoFrameCallback(firstFrame);
        else requestAnimationFrame(() => requestAnimationFrame(firstFrame));
      });
      video.addEventListener('error', () => { if (version === generation) { ready(false); report('此视频无法播放；请检查视频编码。'); } });
      target.append(video); await video.play();
    } else {
      const engine = await renderer();
      if (version !== generation) return;
      scene = engine.createScene(target, {
        fit: 'cover', fps: 30, volume: 0,
        // WebWallGL onReady runs after its first rendered frame.
        onReady: firstFrame,
        onError: error => { if (version === generation) { ready(false); report('场景渲染失败：' + String(error?.message || error).slice(0, 200)); } }
      });
      await scene.load(engine.httpSource(selected.source));
    }
    if (paused) window.wallpaperControl.pause(true);
  } catch (error) { release(); report(error.message || String(error)); current = ''; }
  finally { busy = false; }
}
document.addEventListener('visibilitychange', () => {
  if (document.hidden) { if (scene) scene.pause(); if (video) video.pause(); }
  else { window.wallpaperControl.pause(paused); refresh(); }
});
window.addEventListener('pagehide', release);
setInterval(refresh, 2000);
refresh();

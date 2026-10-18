const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const playerPath = path.resolve(__dirname, '../../Shared/WallpaperEngine/Player');

function harness(type = 'scene') {
  const messages = [], events = {}, intervals = [], scenes = [], videos = [], scripts = [];
  const target = { style: {}, replaceChildren() {}, append() {} }, status = {};
  let selection = { id: 'one', type, title: 'test', source: '/asset/one/', entry: 'clip.mp4' };
  const context = {
    console, setInterval(callback) { intervals.push(callback); },
    requestAnimationFrame(callback) { callback(); },
    fetch: async () => ({ ok: true, json: async () => selection }),
    document: {
      hidden: false, getElementById: id => id === 'wallpaper' ? target : status,
      addEventListener(name, callback) { events[name] = callback; },
      head: { append(script) { scripts.push(script); } },
      createElement(tag) {
        if (tag === 'script') return {};
        const handlers = {};
        const video = { style: {}, addEventListener(name, callback) { handlers[name] = callback; },
          fire(name) { handlers[name]?.(); }, play: async () => {}, pause() {}, load() {}, remove() {}, removeAttribute() {},
          requestVideoFrameCallback(callback) { this.frame = callback; }
        };
        videos.push(video); return video;
      }
    },
    chrome: { webview: { postMessage: message => messages.push(message) } },
    addEventListener(name, callback) { events[name] = callback; }
  };
  const renderer = {
    httpSource: source => source,
    createScene(_, options) {
      const scene = { options, load: async () => {}, destroy() { this.destroyed = true; }, pause() { this.paused = true; }, resume() { this.paused = false; } };
      scenes.push(scene); return scene;
    }
  };
  context.window = context;
  vm.runInNewContext(fs.readFileSync(path.join(playerPath, 'player.js'), 'utf8'), context);
  return { context, messages, events, intervals, scenes, videos, scripts, target,
    rendererReady() { context.WebWallGL = renderer; scripts[0]?.onload(); },
    select(next) { selection = next; }, async settle() { for (let i = 0; i < 15; i++) await Promise.resolve(); }
  };
}

test('scene renderer is not a blocking dependency for videos or initial state fetch', async () => {
  const html = fs.readFileSync(path.join(playerPath, 'player.html'), 'utf8');
  assert.doesNotMatch(html, /<script\s+src="webwallgl/);
  const h = harness('video'); await h.settle();
  assert.equal(h.videos.length, 1);
  assert.equal(h.scripts.length, 0, 'videos never load WebWallGL');
});

test('scene first frame controls readiness; creation/loading alone cannot expose a frame', async () => {
  const h = harness(); await h.settle();
  assert.equal(h.scripts.length, 1, 'renderer loads asynchronously after state fetch');
  h.rendererReady(); await h.settle();
  assert.equal(h.scenes.length, 1);
  assert.equal(h.messages.some(m => m.kind === 'wallpaper-ready' && m.ready), false);
  h.scenes[0].options.onReady();
  assert.equal(h.messages.some(m => m.kind === 'wallpaper-ready' && m.ready), true);
});

test('video waits for a decoded frame; stale callbacks cannot reveal a different wallpaper', async () => {
  const h = harness('video'); await h.settle();
  h.videos[0].fire('playing');
  assert.equal(h.messages.some(m => m.kind === 'wallpaper-ready' && m.ready), false);
  h.videos[0].frame();
  assert.equal(h.messages.filter(m => m.kind === 'wallpaper-ready' && m.ready).length, 1);
  h.select({ id: 'two', type: 'video', title: 'next', source: '/asset/two/', entry: 'next.mp4' });
  await h.intervals[0](); await h.settle();
  const before = h.messages.length;
  h.videos[0].fire('playing'); h.videos[0].frame();
  assert.equal(h.messages.slice(before).some(m => m.kind === 'wallpaper-ready' && m.ready), false);
  assert.equal(h.target.style.visibility, 'hidden');
  h.videos[1].fire('playing'); h.videos[1].frame();
  assert.equal(h.target.style.visibility, 'visible');
});

test('page disposal destroys scene and invalidates a delayed first-frame callback', async () => {
  const h = harness(); await h.settle(); h.rendererReady(); await h.settle();
  h.events.pagehide();
  assert.equal(h.scenes[0].destroyed, true);
  const before = h.messages.length;
  h.scenes[0].options.onReady();
  assert.equal(h.messages.slice(before).some(m => m.kind === 'wallpaper-ready' && m.ready), false);
});

test('missing selection is reported as an actionable error to the native diagnostics window', async () => {
  const h = harness('video'); await h.settle();
  h.select({ id: '', error: '请重新应用壁纸', code: 'selection-missing' });
  await h.intervals[0](); await h.settle();
  assert.ok(h.messages.some(m => m.kind === 'wallpaper-status' && m.error === true && m.text.includes('请重新应用壁纸')));
});

test('video decode and scene render failures are marked as errors, loading is not', async () => {
  const video = harness('video'); await video.settle();
  assert.equal(video.messages.some(m => m.error === true), false);
  video.videos[0].fire('error');
  assert.ok(video.messages.some(m => m.error === true && m.text.includes('视频')));
  const scene = harness(); await scene.settle(); scene.rendererReady(); await scene.settle();
  scene.scenes[0].options.onError(new Error('unsupported effect'));
  assert.ok(scene.messages.some(m => m.error === true && m.text.includes('更换')));
});

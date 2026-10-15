# Execution ledger

Plan: docs/superpowers/plans/2026-10-15-wallpaper-engine.md

Ruling: The user approved the in-chat implementation design with “做吧”. Preserve the requested scope and execute inline rather than introducing another approval round.

Workspace: isolated fresh clone at work/ClaudiaIDE, branch codex/wallpaper-engine; no existing user source checkout modified.

Pre-flight: Task 2 consumes Task 1's WallpaperProject/WallpaperHost. Task 3 consumes the built VSIX and all tests. Baseline build running before edits.

## Outcome

Implemented mode 6, persisted directory/monitor, an editor composition background, opacity/pause/disposal, bundled WebWallGL 3.0.0 and MIT license, bounded loopback assets and video ranges. No runtime Node/CDN.

22 resolver/HTTP checks pass; actual net472 startup/provider probe passes. Real standalone playback probe passed scene frames, source switch, advancing video, pause and WPF composition. VS DTE verified opening our demo, edit/save, persisted Light/Dark mode after restart, hide/show. The installed editor produced scene frames around 30 FPS and advancing video; actual WE scene→video→scene commands were followed. Original scene restored.

## Review and corrections

Independent read-only reviewer found: saved mode 6 lacked an active startup provider; ordinary image could remain under dynamic content; nested videos lost their subdirectory. Fixed all three, with failing-then-passing startup and nested-video regression checks.

Actual desktop testing disproved config-only live detection: WE delays config saves. Added the official read-only getWallpaper command while WE runs and saved configuration only when stopped. The native helper reads an isolated hidden console because WE bypasses redirected stdout. WE CLI can lose Chinese leaf-name encoding; project.json supplies the real asset name. Query failures retain the last source; directory/monitor changes clear cache. Installed-VS live switching with a Chinese video asset passed.

## Delivery

Final version 3.1.52.4 installed through official VSIXInstaller, exit 0. Preserve original 3.1.52 rollback VSIX. Deliver VSIX, complete source, patch, reference analysis, usage/rollback, logs and screenshots in outputs/ClaudiaIDE. No publication or push.

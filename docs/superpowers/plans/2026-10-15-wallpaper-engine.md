# ClaudiaIDE Wallpaper Engine Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan inline, then a fresh review.

**Goal:** Add dynamic Wallpaper Engine backgrounds inside the existing ClaudiaIDE extension.

**Architecture:** Query running WE using the official read-only CLI (saved per-user configuration when stopped), serve that project's assets through a loopback host, and render a local WebWallGL page in an editor background composition control. Keep existing settings and ordinary image modes.

**Tech Stack:** C# net472, WPF, WebView2CompositionControl, bundled WebWallGL 3.0.0, VS 2022 VSIX.

**Spec:** ../../wallpaper-design.md

## Global Constraints

- Add enum value 6 without changing values 0–5.
- No Node or network/CDN requirement at runtime; loopback only.
- Do not execute application wallpapers; filesystem access is confined to the selected project.
- Use current Windows user's WE profile and Monitor0 by default; no recent-history selection.
- Dispose browser and listeners when views close; preserve text input and existing image settings.

## Review Focus

- Encoded traversal/symlinks must not expose unrelated local files.
- Chinese paths and a packaged scene with a scene.json declaration must resolve correctly.
- Missing/malformed configuration must retain an honest no-wallpaper state, not select recent history.
- Switching between ordinary images and dynamic mode must restore normal behavior.
- Multiple tabs must not leak processes/listeners or restart the same wallpaper on every layout event.

### Task 1: Current wallpaper resolution and bounded file serving

Files: Shared/WallpaperEngine/WallpaperProject.cs, WallpaperHost.cs; tests/WallpaperEngine.Tests.

Produces: WallpaperProject.Resolve(installDirectory, userName, monitorName); SafeAssetPath(root, relative).

- [x] Write and run failing behavior tests for profile selection, missing files, scene package fallback, traversal.
- [x] Implement resolution and loopback serving, run the full tests.

### Task 2: ClaudiaIDE settings and dynamic rendering

Files: Setting.cs, ClaudiaIdeOptionPageGrid.cs, ClaudiaIDE.cs, ClaudiaIDEFactory.cs, new WallpaperBackground.cs, packaged renderer.

Consumes Task 1's resolver and host. Produces dynamic mode 6 with the existing opacity/visibility controls.

- [x] Bundle pinned WebWallGL and license; add composition dependency and settings fields.
- [x] Integrate a per-view background with deterministic disposal and current-source polling.
- [x] Build VSIX and validate package completeness; run resolver tests again.

### Task 3: Real environment validation and deliverables

- [x] Verify real scene/video rendering, switching, and input without changing user's final chosen wallpaper.
- [x] Review complete change; fix material issues and rerun checks.
- [x] Package source, patch, VSIX, usage and rollback instructions under outputs; install via VSIXInstaller if validation passes.

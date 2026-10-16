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

## 2026-10-16: startup delay and terminal flashing fix (3.1.52.5)

User reported a blank new project for ~30 seconds and repeated flashing terminal windows. Installed .4 logs showed query failures from 12:31:45 through 12:32:17 before scene selection at 12:32:18; scene playback followed at 12:32:19. The delay was before rendering. The old helper's AllocConsole followed by ShowWindow(0) triggered five console/Windows Terminal SHOW events across three launches.

Replaced real console allocation with ConPTY. The broker starts with CreateNoWindow; a console client inside ConPTY provides the parent console WE's GUI CLI expects for AttachConsole. Fake direct-console and real WE queries return correctly with zero window SHOW events; real queries took 109–123ms. No PowerShell process is used by the extension.

First state now serves saved configuration immediately. Live queries run outside the state/asset lock, publish only for the current settings generation, retain usable state on failure, and share one in-flight flag across reconfiguration. Slow-query regression failed before the change and passes after. Independent review caught a reconfiguration overlap; reproduced it with a failing check and fixed it.

31 resolver/HTTP/concurrency checks and net472 startup probe pass. Installed .5 through VSIXInstaller (exit0). Fresh WallpaperSmoke console project: Program.cs opened12:41:55, player12:41:56, playing12:41:58 (~3 seconds after opening). Scene ~30 FPS. Global console/terminal SHOW watcher around VS startup ran45 seconds with zero events. User-owned WE process was left running; wallpaper selection unchanged in this repair.

## 2026-10-16: pin dynamic wallpaper to viewport (3.1.52.6)

User screenshot/video showed the wallpaper scrolling out of the editor. Original static mode uses the editor host Background VisualBrush. The dynamic branch instead called Update->Resize on each layout, resetting a ViewportRelative adornment to Canvas(0,0) in text coordinates. VS uses ViewportLeft/ViewportTop as the visible text-coordinate origin.

Set browser Canvas.Left/Top to current ViewportLeft/ViewportTop; preserve the same player instance and viewport dimensions. Added opt-in placement diagnostics alongside existing stats. Regression probe invokes production Resize and checks actual WPF-transformed bounds under a simulated text layer: baseline passed, scrolling300px failed at y=-300 before the fix; after the fix, vertical, horizontal, resize and return cases all pass.

Installed .6 through official VSIXInstaller (exit0), after saving user's Program.cs edits. Actual editor commands scrolled15+80 lines down: viewportTop255 and1615, browser origin approximately(0,-0.333) due to layout rounding, full viewport coverage. Horizontal test viewportLeft9976.75, browser origin approximately(-0.082,0). Animation remained ~30 FPS. Restored original document/caret at line34; actual viewportTop328, browser origin(0,0). Independent focused review found no material issue. Screenshot diagnostics from hidden tabs can be blank; placement logs are the validation evidence for this fix.

## 2026-10-16: cover the editor gutter (3.1.52.9)

User requested a continuous wallpaper under the black left line-number/breakpoint gutter. Factory now uses public IVsEditorAdaptersFactoryService to supply the IWpfTextViewHost. The same browser is rehomed from the text-only clipping adornment to the common Panel ancestor of that view and its Left margin. WallpaperSurface spans the pane, stays behind text/glyphs, sizes with the pane, and leaves input enabled on editor/margins.

Surface saves/restores background brushes/bindings along text/gutter paths and uses the public IWpfTextView.Background property to clear the editor's own drawing background. Initial .8 visual check exposed that the editor CLR background is not necessarily a public Background DP; added a failing regression and corrected it in .9. Reviewer flagged strong retention of recycled gutter visuals; detached controls are now restored/pruned each resize (failing-then-passing regression).

ViewportProbe passes original scroll/resize cases, full gutter+text bounds, explicit view background transparency, preserved glyph input, detached marker restoration and disposal restoration. Actual .9 VS screenshot shows a continuous scene behind line numbers and code. Pane1080.67x604 remains at(0,0) while text viewportTop340; animation ~30 FPS. PNG alpha89 confirms the original35% opacity is retained. Hidden views no longer overwrite diagnostic captures; capture uses pane target and native DPI. Installed through official installer, exit0. Saved original file edits and restored caret line20. Intermediate local .7/.8 builds are superseded by .9; not delivered.

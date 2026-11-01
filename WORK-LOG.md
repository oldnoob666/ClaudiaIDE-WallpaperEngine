# Execution ledger

## 2026-10-17: fix multi-tab visibility-walk crash (3.1.52.12)

User reported VS exiting when switching script tabs after installing .10. Windows Application event 1026 recorded ArgumentOutOfRangeException at Grid.GetVisualChild -> InvalidateForceInheritPropertyOnChildren -> OnIsVisibleChanged, coinciding with player release. The .10 implementation removed the earlier background child synchronously inside the text view's inherited IsVisibleChanged callback while WPF was traversing a cached child count. The original probe placed text last and missed this layout. Adding a trailing scrollbar/margin sibling reproduced the exact exception before the fix.

Lifecycle events now coalesce work on DispatcherPriority.Background, outside the current WPF property traversal. Unload immediately invalidates old callbacks but defers native/visual teardown; close also defers teardown. Switching to ordinary image mode awaits that cleanup before replacement styling. Review identified a settings-change race across this new await; cleanup now checks instance identity and reroutes by the current mode before applying static styling.

The regression with trailing siblings passes after the fix, including 60 alternating switches and 100 same-turn visibility changes. Existing resolver/HTTP, player script, viewport/gutter and startup checks remain covered. Installed .12 through the official installer, exit 0. Real VS 2022 smoke used an independent project with 12 script files, 24 keyboard switches visiting all 12, mouse tab clicks and close actions; VS remained responsive, wallpaper restored, and no new Application 1000/1026 crash events appeared during the test. No user Unity project was edited. Focused independent review found no remaining important issue.

## 2026-10-17: visible-view lifecycle and first-frame reveal (3.1.52.10)

Hidden views previously allocated WebView2 immediately and only paused playback on a timer. Players now exist only for loaded, visible editors in non-minimized windows with enabled, nonzero-opacity backgrounds. Visibility/unload/window events dispose native controls immediately, stop polling and restore editor/gutter brushes. Returning recreates a player using the shared environment; browser identity guards reject stale initialization continuations. Visible split panes remain independent.

White startup frames were possible because transparency was configured after native initialization and opacity applied on a later timer tick. The control now starts transparent inside a concealed WPF container; original editor brushes remain until a decoded video frame or WebWallGL's first rendered frame. Reveal uses the configured opacity immediately. Videos skip WebWallGL entirely; scenes load it asynchronously after selection. Cold initialization and complex scene assets still have real startup costs; no zero-latency claim.

Before implementation, the lifecycle regression failed because an unloaded/hidden view already owned a browser. Four new player checks also failed against the old page. Final checks: all 31 resolver/HTTP/concurrency tests, four player tests, seven viewport/gutter checks and the startup-provider check pass. Real WebView2 lifecycle probe passes allocation/release, rapid switching during initialization, native disposal, selection change while hidden, split views, minimize/restore, unload/reload and final close. Its last run measured about 1.0s for the initial scene and 0.10–0.20s for later video rebuilds; this is a WPF integration probe, not a new installed-VS benchmark. Computer Use inspection confirmed visible scene composition beneath text and gutter; WPF RenderTargetBitmap alone does not capture the GPU composition reliably.

Independent read-only review found no important defects. Release build succeeds with pre-existing dependency-audit and VS thread-analysis warnings. Existing installed VS extension was not replaced during this task; a new VSIX is built for installation. Source publication does not create a GitHub Release.

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

## 2026-10-20：脚本切换保留与 60 秒休眠

用户批准在频繁切换脚本时保留已加载的独立播放器，连续 60 秒不切换后释放隐藏标签，仅保留所有可见编辑区。实现按 UI Dispatcher 共用倒计时，使用 Stopwatch 计算经过时间；不同脚本获得焦点及标签显示时重新计时，布局和设置更新不延时。隐藏和临时卸载的开放标签保留播放器，停止布局及诊断轮询；关闭、最小化、隐藏窗口或禁用背景仍及时释放。保留 WPF 调度后拆卸、首帧保护与异步实例身份检查。

真实 WebView2 测试发现 Chromium 会在 visibilitychange 后自行暂停静音视频，即使网页没有调用 pause。编辑区通过文档初始化脚本启用内部 keepAlive，并仅在隐藏、未显式暂停且 generation 仍匹配时恢复这种暂停；释放的旧视频不会复活。场景不再因网页可见性事件被主动暂停，但后台帧率仍受 WebView2 调度影响。

只读审查指出：已卸载的缓存标签没有 Window.GetWindow，切换到共享背景时会漏释放独立播放器。先用 --ide 探针复现超时，再改为按已观察窗口判断共享归属；回归通过。分屏创建仅重置计时，不覆盖最后实际获焦脚本，避免吞掉后续焦点切换。再次只读复核未发现确定的剩余缺陷。

验证：
- 旧实现先在“初始化中隐藏仍保留相同播放器”检查失败，新增网页保留/暂停恢复检查也先失败后通过。
- 独立播放器真实场景/视频 LifecycleProbe：115 项通过。包含 60 次双向切换、隐藏视频实际推进、卸载重载复用、35 秒后切换分屏重置全局期限、原隐藏标签超过 60 秒仍保留、最后一次切换 60 秒后释放、所有可见分屏保留、原生销毁与切回恢复。
- 共享播放器 LifecycleProbe --ide：24 项通过，含已卸载缓存标签切共享及时释放。
- 配置与路径检查 47 项、Node 播放器测试 10 项、StartupProbe 6 项、ViewportProbe 7 项、ToolWindowProbe 25 项全部通过。
- Release 构建及 git diff --check 通过。构建保留已有 MessagePack 3.1.7 NU1902、探针程序集版本及线程分析警告，未扩展本次依赖修改范围。
- VSIX 内 ClaudiaIDE.dll、WallpaperEngine/player.js 与本次构建/源文件的 SHA-256 分别一致。安装包 SHA-256：B2971D56ABD4452835C08BEC9A2B32C0B59523AD422EF5356BFB6002B9280AB2。

原生探针输出在工作区 verification/sleep-final、verification/sleep-shared-final；其他检查日志为 verification/sleep-*.log。以上为 WPF + 真实 WebView2 运行时验证，不等同于完整 VS 内安装、标签拖拽与 UI 冒烟测试。本次交付本地源码及构建包，未进行安装或发布。

## 2026-10-20：切换标签黑屏修复与 3.1.52.16 安装核验

用户提供的 60 FPS 录屏出现 8 次代码区深色遮挡，每次约 0.43–0.80 秒；底部输出区仍显示壁纸，恢复时刻固定在每秒同一相位。结合共享背景每秒执行 ClearEditor/Refresh 的实现，修复编辑器重新显示后等待周期透明化的问题。

RegisterView 现在订阅视图加载、显示、获得焦点及背景画刷变化，在 Render 前的 DataBind 调度合并更新编辑器与祖先背景。此路径只更新画刷；播放器及视觉子元素的创建、移动、销毁仍走原有 Background 调度。已透明的编辑区不重复写入背景属性；视图关闭时对称解绑，禁用及销毁共享背景时取消待执行更新，保留每秒轮询兜底。

回归探针停用周期计时器，直接在 CompositionTarget.Rendering 检查标签显示后的第一帧；旧实现先失败，修复后通过。另覆盖背景重置、焦点切换、新视图加载、共享播放器身份保持及最新主题画刷还原。只读代码审查未发现确定的剩余实质缺陷。

验证与交付记录：

- 独立播放器及真实 60 秒休眠：115 项通过；共享播放器及首帧透明化：29 项通过。
- 配置/路径检查 47 项、Node 播放器测试 10 项、StartupProbe 6 项、ViewportProbe 7 项、ToolWindowProbe 25 项通过。推送前重新运行配置/Node 检查及 Release 构建，结果通过。
- CRLF 感知的 git diff --check 通过；VSIX 保留现有依赖及分析器警告，未引入依赖升级。
- 官方 VSIXInstaller 升级到 3.1.52.16，退出码 0；安装目录内 DLL、播放器脚本与构建一致，原 config.txt 哈希保持一致，升级前已备份扩展文件。
- 在用户打开的 WallpaperSwitchSmoke 项目中，鼠标切换 Script09、Script11、Script12、Script06，观察到切换快照中壁纸保持可见。该检查不是逐帧录屏测量，不声称覆盖所有时序。用户随后自行测试并确认“已经没问题了”。
- 3.1.52.16 安装包 SHA-256：2C39CF908859855B210D46F61A0EC878D04950B54E2CE23FC8330BF6DB4E6567。

用户确认后要求推送 GitHub；源码继续使用 publish 远端的 codex/tool-window-transparency 测试分支。该操作发布源码提交，未创建新的 GitHub Release 或上传安装包。

## 2026-11-01：灯泡菜单卡死修复与 3.1.52.17

开启工具窗口背景透明时，仅展开代码修复灯泡菜单就可能使 VS 界面无法操作，不需要执行 Copilot 修复。用户确认卡住后，现场主线程采样反复落在共享背景刷新、恢复编辑器背景及 WpfTextView 背景设置路径。

根因是弹窗预览通过 WPF 逻辑树继承主窗口，但其视觉元素不在壁纸覆盖的根面板内。共享刷新按窗口归属清除预览背景，透明层清理又按视觉树范围恢复背景；两次 BackgroundBrushChanged 持续触发 DataBind 更新，使输入和绘制饥饿。ClearEditor 现在先检查视觉树归属，范围外的预览不透明化，移出的已跟踪视图仍由原有清理逻辑恢复。

真实 WPF Popup 回归首先在旧代码上复现输入无法执行，再验证修复后五项检查通过。配置/路径/HTTP 47 项、播放器脚本 10 项、启动 6 项、视口 7 项、工具窗口与鼠标命中 25 项、共享背景与首帧透明化 29 项、独立播放器及真实 60 秒休眠 115 项，共 244 项检查通过。

已通过官方 VSIXInstaller 安装 3.1.52.17，安装器退出码 0；安装 DLL 与构建一致，原配置文件保持一致。用户实际再次点击灯泡后确认“恢复正常，没有卡住”，随后明确要求直接发布 GitHub 正式版。正式包复用该已安装、已验证的同一份 VSIX；首页、安装教程、功能和实现说明同步更新为 3.1.52.17。

VSIX SHA-256：`1C64844661E2F3ACF892C7376CB4004938C0EB721893925867D845B594792CE1`。已有依赖、程序集解析及线程分析器警告保留，本次未升级依赖。

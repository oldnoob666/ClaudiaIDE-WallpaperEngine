# 可见编辑区资源生命周期（3.1.52.12）

## 3.1.52.12：修复标签切换崩溃

3.1.52.10 在 IsVisibleChanged 回调中同步移除背景层。VS 的编辑器根面板还有位于文本视图之后的滚动条等兄弟控件；WPF 正在按原有子元素数量传播可见性时，移除较早的子元素会使遍历索引越界，导致 Grid.GetVisualChild / InvalidateForceInheritPropertyOnChildren 未处理异常并退出 VS。

生命周期事件和布局/设置更新现在仅排队，合并到 Dispatcher 的下一次 Background 调度中执行创建、移除及销毁。先等待当前属性遍历退出，再读取最终可见状态；不依赖一秒轮询。卸载时立即使旧异步回调失效；关闭视图也延后移除背景层，避免在树拆卸过程中重入。

切换到普通图片时，先等待延迟清理及旧画刷还原完成，再应用替换模式的样式；如果等待期间设置再次变化，重新按当前模式分派，并避免旧操作清空新的播放器引用。

回归测试增加文本区后方的兄弟滚动条：旧版稳定复现与用户 Windows 崩溃日志相同的索引越界。修复后继续验证多次切换和同一轮调度中的可见性合并，保留隐藏播放器释放与首帧遮蔽功能。

## 原因与改动

旧版在每个视图创建时立即构造并初始化 WebView2；隐藏标签仅通过每秒脚本调用暂停，浏览器、页面和场景纹理继续保留。新版根据 WPF Loaded、Unloaded、IsVisibleChanged 以及所属窗口的 StateChanged 创建或销毁播放器。隐藏视图保留轻量事件订阅以便恢复，不保留播放控件和定时器。

每个可见编辑区独立播放，分屏中不要求键盘焦点。隐藏、最小化、禁用背景或透明度为零时，在当前 WPF 属性遍历结束后的调度中销毁 WebView2、移除背景层并恢复编辑器/边栏画刷。切回后重新创建页面、读取当前选择，播放进度重新开始。同一 VS 进程仍共享 WebView2 环境和缓存；释放播放器不表示共享浏览器进程立即退出或系统内存立刻归零。

初始化任务在等待共享环境和原生控制器之后，都会核对播放器身份；快速切换时旧任务无法重新挂回视图。已销毁页面的首帧回调也会被忽略。

## 首次加载与闪白

旧版在 EnsureCoreWebView2Async 完成后才设置透明背景，而且页面透明度要等首次定时更新。初始化阶段存在白色默认背景及透明度尚未生效的窗口；HTML 同步加载 WebWallGL 也会阻塞视频页面读取壁纸状态。

新版在控件插入视图及原生控制器创建前设置透明背景，并用普通 WPF 容器保持零透明度。等待期间保持原编辑器主题画刷。视频通过 requestVideoFrameCallback（旧运行时回退为两个动画帧）确认首帧，场景使用 WebWallGL 的首个渲染帧回调；之后才接入完整背景层并应用当前透明度。后续布局继续复用原有固定位置及行号栏覆盖逻辑。

页面先读取状态，仅场景按需异步加载 WebWallGL，视频不加载该脚本。共享浏览器环境不反复初始化，移除常规的每秒透明度/暂停脚本调用。冷启动仍包括 WebView2 控制器创建、素材读取、解码或场景渲染；这些成本无法完全消除。日志新增 controller 和 First frame 耗时，可用于区分浏览器初始化与素材等待。

## 验证

常规自动检查：

```powershell
dotnet run --project tests/WallpaperEngine.Tests
node --test tests/Player.Tests/player.test.cjs
MSBuild.exe tests/ViewportProbe/ViewportProbe.csproj /restore /p:Configuration=Release /v:minimal /nologo
& tests/ViewportProbe/bin/Release/net472/ViewportProbe.exe
```

生命周期探针需要 Windows、WebView2 和本机可播放的真实场景/视频，运行生产 WallpaperBackground 和 WallpaperSurface，通过 IWpfTextView 代理提供编辑器几何；不是完整 VS 自动化。它只读给定素材，在输出目录写独立配置，不改变 Wallpaper Engine 的选择。Node.js 仅用于开发测试，不是插件运行依赖。

```powershell
MSBuild.exe tests/LifecycleProbe/LifecycleProbe.csproj /restore /p:Configuration=Release /v:minimal /nologo
& tests/LifecycleProbe/bin/Release/net472/LifecycleProbe.exe '<输出目录>' '<场景的完整入口路径>' '<视频的完整入口路径>'
```

结果在输出目录的 lifecycle.log。可追加 --inspect，让场景和视频各停留 20 秒供人工观察。探针覆盖隐藏初始不分配、初始化中隐藏、快速返回、首帧前遮蔽、原生销毁、恢复时跟随变化、可见分屏、隐藏命令、最小化、卸载重载及最终关闭。ViewportProbe 覆盖滚动位置、边栏、输入和画刷还原；播放器脚本测试覆盖按需加载及过期回调。

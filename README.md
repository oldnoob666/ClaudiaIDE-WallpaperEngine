# ClaudiaIDE-WallpaperEngine

让 Visual Studio 2022 的编辑器背景跟随 Wallpaper Engine 当前壁纸，支持视频和部分动态场景。

这是基于 [buchizo/ClaudiaIDE](https://github.com/buchizo/ClaudiaIDE) Release3.1.52 的**非官方衍生版**，由 [oldnoob666](https://github.com/oldnoob666) 维护。原始背景功能来自 ClaudiaIDE；本仓库增加 Wallpaper Engine 联动、动态播放、滚动固定和左侧行号/断点栏覆盖。与 ClaudiaIDE 原作者、Wallpaper Engine 官方没有隶属关系。

## 功能

- 跟随指定显示器的 Wallpaper Engine 当前选择；切换壁纸后通常数秒内更新。
- 视频静音循环播放，动态场景通过 WebWallGL 渲染。
- 背景覆盖代码区及左侧行号、断点栏；上下、左右滚动代码时背景保持原位。
- 支持透明度、浅色/深色主题设置和 `Ctrl+Shift+Alt+C` 显示/隐藏。
- 保留 ClaudiaIDE 原有图片和幻灯片模式；新建受支持的文本编辑视图也会显示背景。
- 只为可见的编辑区加载壁纸；隐藏标签、最小化 VS、隐藏背景或关闭视图时释放对应播放器，切回后自动重建。分屏中可见的编辑区均可播放。
- 加载期间保留编辑器主题底色；首帧就绪后按设置的透明度显示背景，避免初始化白底闪现。

## 安装与使用

目前仓库提供源码，尚未发布 GitHub Release 安装包。按下方步骤构建，双击生成的 `ClaudiaIDE.vsix`，按安装器提示关闭 Visual Studio 并完成安装。

需要 Windows、Visual Studio 2022、[WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)、已安装的 Wallpaper Engine 以及本机可读取的壁纸素材。当前只验证了 Windows x64 / VS 2022；仓库保留的 VS 2019 工程不属于此次联动功能的验证范围。

本版保留 ClaudiaIDE 扩展标识，按替换版使用。安装器若拒绝覆盖现有版本，先在扩展管理器卸载原版，再安装本版；不要同时启用两个版本。

1. 在 Wallpaper Engine 中应用一张视频或场景壁纸。
2. 在 VS 中打开“扩展 → ClaudiaIDE → 打开设置”，确认 Wallpaper Engine 安装目录和显示器名称（例如 `Monitor0`）。自动发现失败时手动指定 Steam 库中的 `steamapps\common\wallpaper_engine` 目录。
3. 点击“扩展 → ClaudiaIDE → 跟随 Wallpaper Engine”，启用浅色和深色主题下的联动模式。
4. 打开代码/文本文件，在 ClaudiaIDE 设置中调整不透明度。切回背景来源 `Single` 并保存即可恢复普通图片。

Wallpaper Engine 未运行时，使用其配置文件最后保存的选择。各编辑视图播放同一素材，但播放进度不与桌面逐帧同步。

## 从源码构建

安装 Visual Studio 2022 的“Visual Studio 扩展开发”工作负载及 .NET Framework 4.7.2 targeting pack。测试工程另需 .NET 9 SDK。在 **Developer PowerShell for VS 2022** 中进入源码根目录：

```powershell
MSBuild.exe ClaudiaIDE\ClaudiaIDE.csproj /restore /p:Configuration=Release /v:minimal /nologo
dotnet run --project tests\WallpaperEngine.Tests
```

安装包位于 `ClaudiaIDE\bin\Release\net472\ClaudiaIDE.vsix`。构建会还原 NuGet 依赖；播放器和查询工具随包分发，运行时不需要 Node.js、npm 或 CDN。

## 已知限制

- WebWallGL 是第三方场景渲染器，并非所有工坊场景、脚本或交互效果都兼容；不执行应用类壁纸。
- 每个可见编辑视图有自己的播放器，隐藏标签不保留播放器。可见分屏及多个 VS 进程仍会增加内存和 GPU 占用；同一进程共享浏览器环境和缓存，因此全部标签隐藏后进程内存也不会归零。
- 隐藏后重新打开需要重建播放器，播放进度不保留。首次启动仍需初始化 WebView2 和读取素材，复杂场景可能需要数秒；详见 [资源生命周期与加载说明](docs/runtime-lifecycle.md)。
- 动态背景覆盖受支持的文本编辑器窗格，不覆盖解决方案资源管理器等整个 VS 界面。
- 日志位于 `%LOCALAPPDATA%\ClaudiaIDE\WallpaperEngine.log`，WebView2 数据位于 `%LOCALAPPDATA%\ClaudiaIDE\WebView2`。

详细使用和排查见 [联动说明](docs/wallpaper-engine.md)，实现参考见 [参考项目拆解](docs/参考项目拆解.md)。`WORK-LOG.md` 和设计文档保留开发过程中的历史验证记录，文中“本机”结果不代表所有环境都已验证。

## 来源与许可证

- **ClaudiaIDE**：原作者 [buchizo](https://github.com/buchizo)，基线提交 `23d555d0a817c3deee34961bff18412c9e26b5b0`。保留原提交历史、署名和 [Ms-PL 许可证](LICENSE)，原始说明见 [README.upstream.md](README.upstream.md)。本仓库对 ClaudiaIDE 的新增和修改代码同样按 Ms-PL 分发。
- **WebWallGL 3.0.0**：由 [oneincase/webwallgl](https://github.com/oneincase/webwallgl) 提供场景渲染，按 [MIT 许可证](Shared/WallpaperEngine/Player/WebWallGL-LICENSE.txt) 分发。
- **设计参考**：[elysia395/dsh-wallpaper-engine](https://github.com/elysia395/dsh-wallpaper-engine)，参考提交 `01fbb8b8e66fd7e405e9c878d18dbd456187f111`，用于理解壁纸素材读取及浏览器播放方式。

其他 NuGet 依赖遵循各自许可证。仓库不包含 Wallpaper Engine 程序或用户的工坊壁纸素材；这些素材的权利归各自作者。上游图片、图标及原有声明随源代码保留，软件许可证不授予商标权。GitHub 的 Sponsor 链接沿用上游配置，支持的是原作者 buchizo。


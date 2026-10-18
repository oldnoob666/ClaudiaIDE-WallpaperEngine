# ClaudiaIDE Wallpaper Engine 联动版

非官方联动版 3.1.52.13，基于 ClaudiaIDE Release3.1.52，面向 Visual Studio 2022。保留原扩展身份，作为原版的替换版使用；未发布到 Marketplace。[下载稳定安装包](https://github.com/oldnoob666/ClaudiaIDE-WallpaperEngine/releases/tag/v3.1.52.13)，新用户请先阅读[安装与常见问题](quick-start.md)。许可证说明见仓库首页。

## 配置与使用

- 启用窗口检查环境通过后，可将浅色/深色主题均设为 `WallpaperEngine` 模式；保留已有非零不透明度，零值恢复为 `0.35`。
- 安装目录留空时自动定位；失败时选择你的 Steam 库中 `steamapps\common\wallpaper_engine` 的实际目录，并从当前用户的显示器列表选择要跟随的一项。
- 动态背景显示在代码编辑区。`Ctrl+Shift+Alt+C` 显示/隐藏背景。
- 菜单“扩展 → ClaudiaIDE → 启用 Wallpaper Engine 动态背景…”可检测环境、重新启用联动、查看最近播放问题和打开日志。
- 菜单“扩展 → ClaudiaIDE → 打开设置”可调整透明度、目录和显示器；背景来源选 `Single` 并保存可恢复普通图片。

启动 Wallpaper Engine 并切换壁纸，编辑区通常约 2–4 秒跟随。Wallpaper Engine 未运行时，显示配置文件最后保存的选择。扩展只执行查询命令。

壁纸连续覆盖行号、断点栏和代码区，固定在编辑器底层，上下、左右滚动只移动代码；背景不随文档移走。文字位于背景上方。视频静音循环播放，场景目标 30 FPS。只为可见的编辑区创建播放器；隐藏标签、卸载视图、最小化 VS、隐藏背景或透明度设为零时，在当前 WPF 属性遍历结束后的调度中释放浏览器并停止该视图的定时更新，恢复可见后重新加载当前壁纸。分屏中可见的编辑区分别播放，进度不保留。

首次加载期间保持编辑器主题底色，收到视频解码帧或场景首帧就绪信号后，按当前透明度显示背景。视频无需加载 WebWallGL；场景按需异步加载该渲染器。冷启动及复杂素材仍可能需要数秒，不能保证瞬时出现。更多原因和验证方法见 [资源生命周期说明](runtime-lifecycle.md)。

## 实现与验证

本版借鉴 DSH 读取 Wallpaper Engine 素材、通过 HTML video / WebWallGL 独立播放的方法，将它接入 ClaudiaIDE 编辑器背景，并增加官方命令检测桌面实时选择。分析见同目录《参考项目拆解.md》。

本机实测：31 项配置解析、路径保护、HTTP 范围和中文/嵌套路径检查通过；保存动态模式后的启动检查通过。真实场景“明日方舟终末地_佩丽卡_时钟”在 VS 编辑区约 30 FPS；真实视频“世界很温柔—《龙族》上杉绘梨衣.mp4”从约 0.3 秒推进到 20.3 秒。已安装扩展跟随 Wallpaper Engine 实际执行场景→视频→场景切换，编辑、保存、隐藏/显示正常。

这是同一素材的独立播放，进度不与桌面逐帧同步。WebWallGL 是第三方实现，未保证全部工坊场景、特殊脚本或交互效果兼容。应用类壁纸不执行。外部目录若被官方 CLI 返回成乱码，可能无法定位；本机工坊数字目录及中文视频文件名已验证。

## 本地验证产物和排查

下面的文件名指开发期间生成的本地交付物，不表示仓库已附带这些文件或已发布 Release。源码构建产物位置见下节。

- `ClaudiaIDE-WallpaperEngine-3.1.52.9.vsix`：安装包。
- `ClaudiaIDE-WallpaperEngine-source.zip`：完整源码；`ClaudiaIDE-WallpaperEngine.patch`：相对官方基线的补丁。
- `VS编辑区实测.png`：真实 VS 截图；`动态背景叠加验证.png`：独立 WPF 验证截图。
- `实际联动验证.log`、`播放验证.log`、`自动检查.log`：验证记录。
- 日志：`%LOCALAPPDATA%\ClaudiaIDE\WallpaperEngine.log`；浏览器数据：`%LOCALAPPDATA%\ClaudiaIDE\WebView2`。

没有背景时，先确认是在代码/文本编辑区、显示器名称正确，再看日志中的加载/渲染错误。单个场景不兼容时可换视频验证。

## 源码构建

需要 VS 2022 扩展开发组件、.NET Framework 4.7.2 targeting pack；自动检查工程另外使用 .NET 9 SDK。

源码根目录执行：

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' ClaudiaIDE\ClaudiaIDE.csproj /restore /p:Configuration=Release /v:minimal /nologo
dotnet run --project tests\WallpaperEngine.Tests
```

安装包在 `ClaudiaIDE\bin\Release\net472\ClaudiaIDE.vsix`。运行时需要 WebView2 Runtime，不需要 Node、npm 或 CDN。包内包含播放器、查询工具和 WebWallGL MIT 许可证。

## 回退

恢复图片：将 ClaudiaIDE 背景来源改为 `Single` 并保存。

恢复官方版：在 Visual Studio 扩展管理器卸载本地联动版，按提示关闭 VS 完成卸载，再运行保留的 `ClaudiaIDE-3.1.52.vsix`。官方版本号更低，需先卸载联动版。未修改 Wallpaper Engine 程序文件或壁纸素材。

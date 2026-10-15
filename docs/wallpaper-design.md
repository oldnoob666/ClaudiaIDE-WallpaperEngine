# ClaudiaIDE 动态壁纸联动

用户已确认：扩展现有 ClaudiaIDE；视频、场景保留动画；自动跟随 Wallpaper Engine 切换。继续使用 Visual Studio 2022 和现有图片模式。

## 实现

给现有背景来源枚举尾部增加 WallpaperEngine，保留已保存的 0–5 编号。添加安装目录（留空自动探测）和显示器（Monitor0）选项。Wallpaper Engine 运行时每两秒通过官方只读命令 `-control getWallpaper -monitor 0` 查询当前选择；未运行时读取 config.json 中当前用户的 general.wallpaperconfig.selectedwallpapers。不使用 recent 历史。实际切换时配置文件会延迟保存，因此不能仅依赖 config.json。

官方命令直接写 Windows 控制台，普通 stdout 重定向无法得到结果。随包的 WallpaperQuery.exe 在独立隐藏控制台中读取结果，再以 UTF-8 返回主扩展。实际素材名以 project.json 为准，兼容本机 CLI 返回中文文件名损坏的情况；查询超时保留上次成功来源。

共享本地素材服务只监听 127.0.0.1，按当前壁纸项目目录提供资源，使用随机会话路径和目录围栏。独立壁纸页面使用本地打包的 WebWallGL 3.0.0 实时渲染；视频直接循环播放。固定源文件和依赖随 VSIX 分发，运行时不依赖 Node 或 CDN。

在 ClaudiaIDE 原有视图生命周期内增加 WebView2CompositionControl 背景层，位于文字后方、不截获编辑器输入，接入原透明度和隐藏开关。设置变化时复用实例；关闭视图时释放。网页/场景第三方内容与管理接口隔离；应用类壁纸不执行。

联动指换源跟随，不保证独立视频播放器与桌面逐帧同步；WebWallGL 对具体场景的兼容性需实测。先验证本机真实场景和视频，再安装本机扩展版本。原版安装包保留，修改版保留相同扩展身份、提升补丁版本以便标准 VSIX 安装器升级和回退。

## 验收

- 配置解析测试：只认当前用户和显示器；忽略最近历史；非法/缺失文件不导致 VS 崩溃。
- 素材路径测试：支持中文和空格、scene.json 声明但 scene.pkg 实存，拒绝越界路径和应用类型。
- 构建 VS 2022 VSIX，检查依赖和引擎均在包内。
- 本机实际场景渲染和视频播放验证；切换壁纸后自动换源；输入和滚动正常；切回普通图片模式正常。

## 参考

DSH 插件参考提交：01fbb8b8e66fd7e405e9c878d18dbd456187f111。
ClaudiaIDE 基线：Release3.1.52 / 23d555d0a817c3deee34961bff18412c9e26b5b0。
WebWallGL 来源：https://github.com/oneincase/webwallgl （MIT）。

#if VS_LATEST
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.VisualStudio.Shell;

namespace ClaudiaIDE.WallpaperEngine
{
    internal sealed class WallpaperSetupWindow : Window
    {
        private readonly TextBox directory = new TextBox { MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly ComboBox monitor = new ComboBox { MinHeight = 28, MinWidth = 150 };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 23 };
        private readonly TextBlock runtimeStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock playbackStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed };
        private readonly Button enable = new Button { Content = "启用当前壁纸", MinWidth = 130, Padding = new Thickness(14, 7, 14, 7), IsEnabled = false };
        private readonly Action<string, string> apply;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private WallpaperSetupResult detected;
        private bool detecting, updating, closed;

        public WallpaperSetupWindow(string currentDirectory, string currentMonitor, Action<string, string> apply)
        {
            this.apply = apply;
            Title = "Wallpaper Engine 动态背景 · 启用与诊断";
            Width = 650; SizeToContent = SizeToContent.Height; MinWidth = 540; MaxHeight = 820;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.WhiteSmoke; Foreground = Brushes.Black; FontSize = 14;
            var body = new StackPanel { Margin = new Thickness(26) };
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            body.Children.Add(new TextBlock { Text = "让编辑器跟随你的桌面壁纸", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            body.Children.Add(new TextBlock { Text = "先在 Wallpaper Engine 中应用壁纸，再检测并启用。", Margin = new Thickness(0, 0, 0, 20), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = "Wallpaper Engine 安装目录", Margin = new Thickness(0, 0, 0, 6) });
            directory.Text = currentDirectory ?? "";
            AutomationProperties.SetName(directory, "Wallpaper Engine 安装目录");
            body.Children.Add(directory);
            var pathButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 16) };
            body.Children.Add(pathButtons);
            AddButton(pathButtons, "选择目录…", Browse);
            AddButton(pathButtons, "自动查找", () => { if (!detecting) { directory.Text = ""; Run(DetectAsync); } });
            AddButton(pathButtons, "重新检测", () => Run(DetectAsync));
            var monitorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            monitorRow.Children.Add(new TextBlock { Text = "跟随的显示器", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) });
            monitor.Items.Add(currentMonitor ?? "Monitor0"); monitor.SelectedIndex = 0;
            AutomationProperties.SetName(monitor, "跟随的显示器");
            monitorRow.Children.Add(monitor); body.Children.Add(monitorRow);
            body.Children.Add(runtimeStatus);
            body.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(232, 239, 247)), Padding = new Thickness(14), Margin = new Thickness(0, 12, 0, 10), Child = status });
            body.Children.Add(playbackStatus);
            body.Children.Add(new TextBlock { Text = "视频和部分场景可用；应用程序类壁纸不支持。各编辑区独立播放，进度不与桌面同步。", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) });
            body.Children.Add(new TextBlock { Text = "启用会保存深浅主题设置。当前解决方案若有独立配置，可再用「保存解决方案设置」保存。", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
            var links = new WrapPanel(); body.Children.Add(links);
            AddButton(links, "安装与常见问题", () => Open("https://github.com/oldnoob666/ClaudiaIDE-WallpaperEngine/blob/main/docs/quick-start.md"));
            AddButton(links, "WebView2 下载", () => Open("https://developer.microsoft.com/microsoft-edge/webview2/#download-section"));
            AddButton(links, "打开日志", OpenLog);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            body.Children.Add(actions); actions.Children.Add(enable);
            var close = new Button { Content = "关闭", IsCancel = true, MinWidth = 80, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7) };
            actions.Children.Add(close);
            close.Click += (s, e) => Close();
            enable.Click += (s, e) => Run(EnableAsync);
            directory.TextChanged += (s, e) => { if (!updating) { detected = null; enable.IsEnabled = false; status.Text = "目录已改变，请点击「重新检测」。"; } };
            monitor.SelectionChanged += (s, e) => { if (!updating && !detecting) Run(DetectAsync); };
            timer.Tick += (s, e) => playbackStatus.Text = WallpaperDiagnostics.LastError == "" ? "" : "最近一次播放问题：" + WallpaperDiagnostics.LastError;
            Loaded += (s, e) => { timer.Start(); Run(DetectAsync); };
            Closed += (s, e) => { closed = true; timer.Stop(); };
        }

        private async Task EnableAsync()
        {
            // Recheck edited paths, runtime, and selection immediately before saving.
            await DetectAsync();
            if (closed || !enable.IsEnabled || detected?.CanEnable != true) return;
            try
            {
                WallpaperDiagnostics.Clear();
                apply(detected.Directory, detected.Monitor);
                status.Text = "已启用联动。关闭此窗口并打开代码文件查看；切换 Wallpaper Engine 壁纸后通常数秒内更新。透明度可在 ClaudiaIDE 设置中调整。";
            }
            catch (Exception error)
            {
                WallpaperDiagnostics.Report("保存背景设置失败：" + error.Message);
                status.Text = "未能保存设置，请查看日志后重试。";
            }
        }

        private void Run(Func<Task> action)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () => {
                try { await action(); }
                catch (Exception error)
                {
                    WallpaperDiagnostics.Report("启用窗口操作失败：" + error.Message);
                    if (!closed) status.Text = "操作未完成，请重新检测或查看日志。";
                }
            }).FileAndForget("claudiaide/setup");
        }

        private static void AddButton(Panel panel, string text, Action action)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 5) };
            button.Click += (s, e) => action(); panel.Children.Add(button);
        }

        private void Browse()
        {
            if (detecting) return;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "选择包含 config.json 的 wallpaper_engine 安装目录", SelectedPath = directory.Text, ShowNewFolderButton = false })
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { directory.Text = dialog.SelectedPath; Run(DetectAsync); }
        }

        private async Task DetectAsync()
        {
            if (detecting || closed) return;
            detecting = true; enable.IsEnabled = false; directory.IsEnabled = monitor.IsEnabled = false;
            status.Text = "正在检测安装目录、显示器和当前壁纸…";
            var path = directory.Text.Trim();
            var preferredMonitor = monitor.SelectedItem as string ?? "Monitor0";
            try
            {
                var outcome = await Task.Run(() => {
                    var found = path == "" ? WallpaperHost.DiscoverInstall() : path;
                    var check = WallpaperSetup.Inspect(found, Environment.UserName, preferredMonitor);
                    if (check.Monitors.Contains(check.Monitor))
                        check = WallpaperHost.Instance.InspectCurrent(found, Environment.UserName, check.Monitor);
                    string runtime;
                    try { runtime = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
                    catch (WebView2RuntimeNotFoundException) { runtime = ""; }
                    return new { Check = check, Runtime = runtime };
                });
                if (closed) return;
                updating = true;
                detected = outcome.Check;
                directory.Text = detected.Directory;
                monitor.Items.Clear();
                foreach (var name in detected.Monitors) monitor.Items.Add(name);
                monitor.SelectedItem = detected.Monitor;
                runtimeStatus.Text = outcome.Runtime == "" ? "✕ 未检测到 WebView2 Runtime，请通过下方入口安装后重启 Visual Studio。" : "✓ 已检测到 WebView2 Runtime";
                status.Text = detected.Message;
                enable.IsEnabled = detected.CanEnable && outcome.Runtime != "";
            }
            catch (Exception error)
            {
                if (!closed)
                {
                    status.Text = "检测未完成，请确认目录可读并重新检测。可点击「打开日志」查看详细原因。";
                    WallpaperDiagnostics.Report("环境检测失败：" + error.Message);
                }
            }
            finally
            {
                updating = false; detecting = false;
                if (!closed) directory.IsEnabled = monitor.IsEnabled = true;
            }
        }

        private void OpenLog()
        {
            try
            {
                Directory.CreateDirectory(WallpaperDiagnostics.LogDirectory);
                var file = Path.Combine(WallpaperDiagnostics.LogDirectory, "WallpaperEngine.log");
                if (!File.Exists(file)) File.WriteAllText(file, "尚无播放日志。请启用背景并打开代码文件后再查看。" + Environment.NewLine);
                Open(file);
            }
            catch (Exception error) { status.Text = "无法打开日志：" + error.Message; }
        }

        private void Open(string destination)
        {
            try { Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true }); }
            catch (Exception error) { status.Text = "无法打开链接或文件：" + error.Message; }
        }
    }
}
#endif

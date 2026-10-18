#if VS_LATEST
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using ClaudiaIDE.Settings;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ClaudiaIDE.WallpaperEngine
{
    internal sealed class WallpaperBackground : IDisposable
    {
        // Accessed only on the editor dispatcher; all views share the environment.
        private static Task<CoreWebView2Environment> environmentTask;
        private readonly IWpfTextView view;
        private readonly IAdornmentLayer layer;
        private WebView2CompositionControl browser;
        private FrameworkElement visual;
        private readonly Func<IWpfTextViewHost> hostProvider;
        private WallpaperSurface surface;
        private readonly DispatcherTimer timer;
        private Task initialize;
        private Window observedWindow;
        private DispatcherOperation pendingUpdate;
        private bool releasePending;
        private bool configured;
        private bool ready;
        private bool disposed;
        private Task disposal = Task.CompletedTask;
        private int ticks;
        private double opacity;

        public WallpaperBackground(IWpfTextView view, Func<IWpfTextViewHost> hostProvider = null)
        {
            this.view = view;
            this.hostProvider = hostProvider;
            layer = view.GetAdornmentLayer("ClaudiaIDE.WallpaperEngine");
            view.ViewportWidthChanged += OnResize;
            view.ViewportHeightChanged += OnResize;
            view.VisualElement.IsVisibleChanged += OnVisibilityChanged;
            view.VisualElement.Loaded += OnLoaded;
            view.VisualElement.Unloaded += OnUnloaded;
            timer = new DispatcherTimer(DispatcherPriority.Background, view.VisualElement.Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += OnTick;
            IdeWallpaperBackground.Changed += QueueUpdate;
        }

        private bool IsCurrent(WebView2CompositionControl player) => !disposed && !releasePending && ReferenceEquals(browser, player);

        private void QueueUpdate()
        {
            if (disposed || pendingUpdate != null) return;
            // IsVisibleChanged runs inside WPF's inherited-property walk. It
            // must never add/remove visual children while that walk enumerates
            // the editor grid (VS has further margins after the text view).
            pendingUpdate = view.VisualElement.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
                pendingUpdate = null;
                if (disposed) return;
                if (releasePending) { releasePending = false; ReleasePlayer(); }
                UpdateVisibility();
                Resize();
            }));
        }

        private void ObserveWindow()
        {
            var window = Window.GetWindow(view.VisualElement);
            if (window == observedWindow) return;
            if (observedWindow != null) observedWindow.StateChanged -= OnWindowStateChanged;
            observedWindow = window;
            if (observedWindow != null) observedWindow.StateChanged += OnWindowStateChanged;
        }

        private void UpdateVisibility()
        {
            if (disposed) return;
            ObserveWindow();
            if (IdeWallpaperBackground.Owns(view.VisualElement))
            {
                ReleasePlayer();
                IdeWallpaperBackground.ClearEditor(view);
                return;
            }
            var visible = configured && opacity > 0 && !view.IsClosed
                && view.VisualElement.IsLoaded && view.VisualElement.IsVisible
                && observedWindow != null && observedWindow.WindowState != WindowState.Minimized;
            if (!visible) { ReleasePlayer(); return; }
            if (browser == null)
            {
                // Set transparency before insertion and native controller creation.
                // Keep the themed editor intact until the first decoded/rendered frame.
                var player = new WebView2CompositionControl {
                    IsHitTestVisible = false, Focusable = false,
                    DefaultBackgroundColor = System.Drawing.Color.Transparent
                };
                browser = player;
                // CompositionControl shadows Opacity as read-only. Apply it to
                // a normal WPF parent, which also conceals the loading surface.
                var container = new Grid { IsHitTestVisible = false, Focusable = false, Opacity = 0 };
                container.Children.Add(player);
                visual = container;
                layer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, this, container, null);
                Resize();
                initialize = InitializeAsync(player);
                timer.Start();
            }
            visual.Opacity = ready ? opacity : 0;
        }

        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => QueueUpdate();
        private void OnLoaded(object sender, RoutedEventArgs e) => QueueUpdate();
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            releasePending = true;
            QueueUpdate();
        }
        private void OnWindowStateChanged(object sender, EventArgs e) => QueueUpdate();

        private async Task InitializeAsync(WebView2CompositionControl player)
        {
            try
            {
                var started = System.Diagnostics.Stopwatch.StartNew();
                var recordedFirstFrame = false;
                var environment = await GetEnvironmentAsync();
                if (!IsCurrent(player)) return;
                await player.EnsureCoreWebView2Async(environment);
                if (!IsCurrent(player)) return;
                player.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                player.CoreWebView2.Settings.IsStatusBarEnabled = false;
                player.CoreWebView2.NewWindowRequested += (s, e) => e.Handled = true;
                player.CoreWebView2.NavigationStarting += (s, e) => {
                    if (!e.Uri.StartsWith(WallpaperHost.Instance.BaseUrl, StringComparison.Ordinal)) e.Cancel = true;
                };
                player.CoreWebView2.WebMessageReceived += (s, e) => {
                    if (!IsCurrent(player) || e.Source != WallpaperHost.Instance.PlayerUrl) return;
                    Newtonsoft.Json.Linq.JObject message;
                    try { message = Newtonsoft.Json.Linq.JObject.Parse(e.WebMessageAsJson); }
                    catch (Newtonsoft.Json.JsonException) { return; }
                    if ((string)message["kind"] == "wallpaper-ready")
                    {
                        ready = (bool?)message["ready"] == true;
                        Resize();
                        visual.Opacity = ready ? opacity : 0;
                        if (ready && !recordedFirstFrame)
                        {
                            recordedFirstFrame = true;
                            Log("First frame: " + started.ElapsedMilliseconds + "ms");
                        }
                    }
                    else if ((string)message["kind"] == "wallpaper-status" && (bool?)message["error"] == true)
                        WallpaperDiagnostics.Report((string)message["text"]);
                    else Log(e.WebMessageAsJson);
                };
                player.CoreWebView2.ProcessFailed += (s, e) => {
                    if (IsCurrent(player)) WallpaperDiagnostics.Report("WebView2 播放器进程异常（" + e.ProcessFailedKind + "）。请关闭后重新启用背景；仍失败时尝试其他壁纸并查看日志。");
                };
                player.CoreWebView2.Navigate(WallpaperHost.Instance.PlayerUrl);
                Log("Player: " + WallpaperHost.Instance.PlayerUrl + "; controller=" + started.ElapsedMilliseconds + "ms");
            }
            catch (Exception error)
            {
                if (IsCurrent(player))
                {
                    WallpaperDiagnostics.Report("播放器初始化失败。请打开「启用 Wallpaper Engine 动态背景」检查 WebView2 Runtime，安装或修复后重启 Visual Studio。");
                    Log("Initialize failed: " + error);
                }
            }
        }

        public void Update(Setting setting)
        {
            if (disposed) return;
            WallpaperHost.Instance.Configure(setting.WallpaperEngineDirectory, setting.WallpaperEngineMonitor);
            opacity = setting.IsHidden ? 0 : Math.Max(0, Math.Min(1, setting.Opacity));
            configured = true;
            QueueUpdate();
        }

        internal static Task<CoreWebView2Environment> GetEnvironmentAsync()
        {
            if (environmentTask == null || environmentTask.IsFaulted || environmentTask.IsCanceled)
                environmentTask = CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudiaIDE", "WebView2"));
            return environmentTask;
        }

        private void Resize()
        {
            if (browser == null) return;
            if (ready && surface == null && hostProvider != null)
            {
                try
                {
                    var host = hostProvider();
                    Panel root = null;
                    var gutter = host?.GetTextViewMargin(PredefinedMarginNames.Left)?.VisualElement;
                    if (root == null && gutter != null)
                    {
                        var common = gutter.FindCommonVisualAncestor(view.VisualElement);
                        for (var item = common; item != null; item = VisualTreeHelper.GetParent(item))
                            if (item is Panel panel) { root = panel; break; }
                    }
                    if (root != null)
                    {
                        layer.RemoveAdornmentsByTag(this);
                        surface = new WallpaperSurface(root, view, gutter, visual);
                        Log("Editor surface: " + root.GetType().FullName + "; gutter=" + gutter?.GetType().FullName);
                    }
                }
                catch (ArgumentException) { } // The adapter may not exist during TextViewCreated.
            }
            if (surface != null) { surface.Resize(); return; }
            visual.Width = Math.Max(1, view.ViewportWidth);
            visual.Height = Math.Max(1, view.ViewportHeight);
            // Adornment coordinates are text coordinates. Refreshing on a
            // layout must retain the viewport offset, rather than reset it.
            Canvas.SetLeft(visual, view.ViewportLeft);
            Canvas.SetTop(visual, view.ViewportTop);
        }
        private void OnResize(object sender, EventArgs e) => QueueUpdate();

        private void OnTick(object sender, EventArgs e) => Microsoft.VisualStudio.Threading.TplExtensions.Forget(UpdatePlaybackAsync());

        private async Task UpdatePlaybackAsync()
        {
            UpdateVisibility();
            var player = browser;
            if (disposed || player == null || initialize == null || !initialize.IsCompleted || player.CoreWebView2 == null) return;
            try
            {
                Resize(); // Also attaches when the editor host becomes available after view creation.
                if (++ticks % 10 == 0 && Environment.GetEnvironmentVariable("CLAUDIA_WALLPAPER_DIAGNOSTICS") == "1")
                {
                    var diagnostics = await player.ExecuteScriptAsync("window.wallpaperControl?.diagnostics()");
                    if (!IsCurrent(player)) return;
                    Log(diagnostics);
                    var bounds = player.TransformToAncestor(surface?.Root ?? view.VisualElement).TransformBounds(new Rect(player.RenderSize));
                    Log("Placement: " + Newtonsoft.Json.JsonConvert.SerializeObject(new {
                        viewportLeft = view.ViewportLeft, viewportTop = view.ViewportTop,
                        viewportWidth = view.ViewportWidth, viewportHeight = view.ViewportHeight,
                        left = bounds.Left, top = bounds.Top, width = bounds.Width, height = bounds.Height,
                        includesGutter = surface != null
                    }));
                }
                if (ticks == 20 && view.VisualElement.IsVisible && Environment.GetEnvironmentVariable("CLAUDIA_WALLPAPER_DIAGNOSTICS") == "1")
                {
                    FrameworkElement target = surface?.Root ?? view.VisualElement;
                    var dpi = VisualTreeHelper.GetDpi(target);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Max(1, Math.Ceiling(target.ActualWidth * dpi.DpiScaleX)), (int)Math.Max(1, Math.Ceiling(target.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(target);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using (var file = File.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudiaIDE", "editor-check.png"))) png.Save(file);
                }
            }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException || error is System.Runtime.InteropServices.COMException)
            {
                if (!disposed) Log("Playback update failed: " + error.Message);
            }
        }

        private void ReleasePlayer()
        {
            timer.Stop();
            var player = browser;
            if (player == null) return;
            browser = null; // Invalidate every continuation before native disposal.
            initialize = null;
            ready = false;
            ticks = 0;
            layer.RemoveAdornmentsByTag(this);
            surface?.Dispose();
            surface = null;
            visual = null;
            player.Dispose();
            Log("Player released: editor hidden, unloaded, or background disabled");
        }

        internal static void Log(string message)
        {
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudiaIDE");
                Directory.CreateDirectory(directory);
                var file = Path.Combine(directory, "WallpaperEngine.log");
                if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.WriteAllText(file, "");
                File.AppendAllText(file, DateTime.Now.ToString("s") + " " + message + System.Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public Task DisposeAsync()
        {
            Dispose();
            return disposal;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            IdeWallpaperBackground.Changed -= QueueUpdate;
            IdeWallpaperBackground.RestoreEditor(view);
            timer.Stop();
            pendingUpdate?.Abort();
            pendingUpdate = null;
            view.ViewportWidthChanged -= OnResize;
            view.ViewportHeightChanged -= OnResize;
            view.VisualElement.IsVisibleChanged -= OnVisibilityChanged;
            view.VisualElement.Loaded -= OnLoaded;
            view.VisualElement.Unloaded -= OnUnloaded;
            if (observedWindow != null) observedWindow.StateChanged -= OnWindowStateChanged;
            observedWindow = null;
            // View.Closed may also arrive during VS's visual-tree teardown.
            // Invalidate callbacks now; detach/dispose only after the walk.
            disposal = view.VisualElement.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ReleasePlayer)).Task;
            timer.Tick -= OnTick;
        }
    }
}
#endif

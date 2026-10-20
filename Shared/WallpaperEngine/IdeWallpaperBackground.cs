#if VS_LATEST
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudiaIDE.Settings;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace ClaudiaIDE.WallpaperEngine
{
    // One composition player for the main docked workspace. Floating windows
    // keep their own editor players; no transparent top-level HWND is required.
    internal sealed class IdeWallpaperBackground : IDisposable
    {
        private static IdeWallpaperBackground current;
        private static readonly List<WeakReference<IWpfTextView>> views = new List<WeakReference<IWpfTextView>>();
        internal static event Action Changed;
        private readonly Window window;
        private readonly Grid root;
        private readonly DispatcherTimer timer;
        private DispatcherOperation pending;
        private DispatcherOperation pendingTransparency;
        private WebView2CompositionControl browser;
        private Grid visual;
        private ToolWindowTransparency transparency;
        private bool enabled, ready, disposed;
        private double opacity;

        public IdeWallpaperBackground(Window window, Grid root)
        {
            this.window = window;
            this.root = root;
            current = this;
            timer = new DispatcherTimer(DispatcherPriority.Background, root.Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += OnTick;
            window.StateChanged += OnChanged;
            window.IsVisibleChanged += OnVisibilityChanged;
            window.Closed += OnClosed;
            root.SizeChanged += OnSizeChanged;
        }

        internal static bool Owns(FrameworkElement element) => OwnsWindow(Window.GetWindow(element));
        internal static bool OwnsWindow(Window window) => current != null && current.enabled && !current.disposed
            && window == current.window;
        internal static void ClearEditor(IWpfTextView view) => current?.transparency?.ClearEditor(view);
        internal static void RestoreEditor(IWpfTextView view) => current?.transparency?.RestoreEditor(view);
        internal static void RegisterView(IWpfTextView view)
        {
            foreach (var reference in views)
                if (reference.TryGetTarget(out var registered) && ReferenceEquals(registered, view)) return;
            views.Add(new WeakReference<IWpfTextView>(view));
            view.VisualElement.Loaded += OnEditorLoaded;
            view.VisualElement.IsVisibleChanged += OnEditorVisibilityChanged;
            view.GotAggregateFocus += OnEditorChanged;
            view.BackgroundBrushChanged += OnEditorChanged;
            view.Closed += OnEditorClosed;
            current?.QueueTransparencyRefresh();
            if (Environment.GetEnvironmentVariable("CLAUDIA_WALLPAPER_DIAGNOSTICS") == "1")
                WallpaperBackground.Log("Shared background text view: " + view.TextBuffer.ContentType.TypeName + "; interactive=" + view.Roles.Contains(PredefinedTextViewRoles.Interactive));
        }

        private static void OnEditorLoaded(object sender, RoutedEventArgs e) => current?.QueueTransparencyRefresh();
        private static void OnEditorChanged(object sender, EventArgs e) => current?.QueueTransparencyRefresh();
        private static void OnEditorVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) current?.QueueTransparencyRefresh();
        }
        private static void OnEditorClosed(object sender, EventArgs e)
        {
            var view = (IWpfTextView)sender;
            view.VisualElement.Loaded -= OnEditorLoaded;
            view.VisualElement.IsVisibleChanged -= OnEditorVisibilityChanged;
            view.GotAggregateFocus -= OnEditorChanged;
            view.BackgroundBrushChanged -= OnEditorChanged;
            view.Closed -= OnEditorClosed;
            current?.transparency?.RestoreEditor(view);
            views.RemoveAll(reference => !reference.TryGetTarget(out var registered) || ReferenceEquals(registered, view));
        }

        public void Update(Setting setting)
        {
            opacity = setting.IsHidden ? 0 : Math.Max(0, Math.Min(1, setting.Opacity));
            var next = setting.TransparentToolWindows && setting.ImageBackgroundType == ImageBackgroundType.WallpaperEngine && opacity > 0;
            var changed = enabled != next;
            enabled = next;
            if (enabled) WallpaperHost.Instance.Configure(setting.WallpaperEngineDirectory, setting.WallpaperEngineMonitor);
            QueueUpdate();
            if (changed) Changed?.Invoke();
        }

        private bool IsCurrent(WebView2CompositionControl player) => !disposed && enabled && ReferenceEquals(player, browser);
        private void QueueTransparencyRefresh()
        {
            if (disposed || !enabled || !ready || transparency == null || pendingTransparency != null) return;
            // Brush updates must beat the next Render operation. Keep native
            // creation/disposal and visual child changes on the existing deferred path.
            pendingTransparency = root.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() => {
                pendingTransparency = null;
                RefreshTransparency();
            }));
        }

        private void RefreshTransparency()
        {
            if (disposed || !enabled || !ready || transparency == null || !root.IsLoaded
                || !window.IsVisible || window.WindowState == WindowState.Minimized) return;
            for (var i = views.Count - 1; i >= 0; i--)
            {
                if (!views[i].TryGetTarget(out var view) || view.IsClosed) { views.RemoveAt(i); continue; }
                if (Window.GetWindow(view.VisualElement) == window) transparency.ClearEditor(view);
            }
            transparency.Refresh();
        }

        private void QueueUpdate()
        {
            if (disposed || pending != null) return;
            // Shell visibility notifications can run inside inherited-property
            // traversal. All visual insertion/removal happens after that walk.
            pending = root.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
                pending = null;
                if (disposed) return;
                UpdateSurface();
            }));
        }

        private void UpdateSurface()
        {
            if (!enabled || !window.IsVisible || window.WindowState == WindowState.Minimized || !root.IsLoaded)
            {
                ReleasePlayer();
                return;
            }
            if (browser == null)
            {
                browser = new WebView2CompositionControl { IsHitTestVisible = false, Focusable = false, DefaultBackgroundColor = System.Drawing.Color.Transparent };
                visual = new Grid { IsHitTestVisible = false, Focusable = false, Opacity = 0 };
                visual.Children.Add(browser);
                Panel.SetZIndex(visual, int.MinValue);
                root.Children.Insert(0, visual);
                Microsoft.VisualStudio.Threading.TplExtensions.Forget(InitializeAsync(browser));
                timer.Start();
            }
            Grid.SetRowSpan(visual, Math.Max(1, root.RowDefinitions.Count));
            Grid.SetColumnSpan(visual, Math.Max(1, root.ColumnDefinitions.Count));
            if (ready)
            {
                if (transparency == null)
                {
                    transparency = new ToolWindowTransparency(root, visual);
                    Changed?.Invoke();
                }
                RefreshTransparency();
            }
            else if (transparency != null)
            {
                transparency.Dispose(); transparency = null;
                Changed?.Invoke();
            }
            visual.Opacity = ready ? opacity : 0;
        }

        private async Task InitializeAsync(WebView2CompositionControl player)
        {
            try
            {
                var environment = await WallpaperBackground.GetEnvironmentAsync();
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
                    JObject message;
                    try { message = JObject.Parse(e.WebMessageAsJson); }
                    catch (Newtonsoft.Json.JsonException) { return; }
                    if ((string)message["kind"] == "wallpaper-ready")
                    {
                        ready = (bool?)message["ready"] == true;
                        QueueUpdate();
                    }
                    else if ((string)message["kind"] == "wallpaper-status" && (bool?)message["error"] == true)
                        WallpaperDiagnostics.Report((string)message["text"]);
                };
                player.CoreWebView2.ProcessFailed += (s, e) => {
                    if (!IsCurrent(player)) return;
                    ready = false; QueueUpdate();
                    WallpaperDiagnostics.Report("主窗口壁纸播放器异常。请关闭后重新打开「工具窗口背景透明」。");
                };
                player.CoreWebView2.Navigate(WallpaperHost.Instance.PlayerUrl);
            }
            catch (Exception error)
            {
                if (!IsCurrent(player)) return;
                WallpaperBackground.Log("IDE wallpaper initialization failed: " + error);
                WallpaperDiagnostics.Report("主窗口壁纸初始化失败。请检查 WebView2 Runtime，或关闭「工具窗口背景透明」使用编辑区背景。");
            }
        }

        private void OnTick(object sender, EventArgs e) => QueueUpdate();

        private void ReleasePlayer()
        {
            timer.Stop();
            pendingTransparency?.Abort(); pendingTransparency = null;
            var player = browser;
            browser = null; ready = false;
            transparency?.Dispose(); transparency = null;
            if (visual != null) root.Children.Remove(visual);
            visual = null;
            player?.Dispose();
        }

        private void OnChanged(object sender, EventArgs e) => QueueUpdate();
        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => QueueUpdate();
        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueUpdate();
        private void OnClosed(object sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; enabled = false;
            if (current == this) current = null;
            timer.Stop(); timer.Tick -= OnTick;
            pending?.Abort(); pending = null;
            pendingTransparency?.Abort(); pendingTransparency = null;
            window.StateChanged -= OnChanged;
            window.IsVisibleChanged -= OnVisibilityChanged;
            window.Closed -= OnClosed;
            root.SizeChanged -= OnSizeChanged;
            root.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ReleasePlayer));
            Changed?.Invoke();
        }
    }

    // Output and other read-only text panes need their public IWpfTextView
    // background cleared too, even when they have no Document role/adornment.
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [ContentType("BuildOutput")]
    [TextViewRole(PredefinedTextViewRoles.Interactive)]
    internal sealed class ToolWindowTextViewListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView) => IdeWallpaperBackground.RegisterView(textView);
    }
}
#endif

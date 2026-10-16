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
        private readonly WebView2CompositionControl browser;
        private readonly Func<IWpfTextViewHost> hostProvider;
        private WallpaperSurface surface;
        private readonly DispatcherTimer timer;
        private readonly Task initialize;
        private bool disposed;
        private bool? paused;
        private bool updating;
        private int ticks;
        private double opacity;

        public WallpaperBackground(IWpfTextView view, Func<IWpfTextViewHost> hostProvider = null)
        {
            this.view = view;
            this.hostProvider = hostProvider;
            layer = view.GetAdornmentLayer("ClaudiaIDE.WallpaperEngine");
            browser = new WebView2CompositionControl { IsHitTestVisible = false, Focusable = false };
            layer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, this, browser, null);
            Resize();
            view.ViewportWidthChanged += OnResize;
            view.ViewportHeightChanged += OnResize;
            initialize = InitializeAsync();
            timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick, view.VisualElement.Dispatcher);
        }

        private async Task InitializeAsync()
        {
            try
            {
                if (environmentTask == null)
                    environmentTask = CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudiaIDE", "WebView2"));
                var environment = await environmentTask;
                if (disposed) return;
                await browser.EnsureCoreWebView2Async(environment);
                if (disposed) return;
                browser.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                browser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                browser.CoreWebView2.NewWindowRequested += (s, e) => e.Handled = true;
                browser.CoreWebView2.NavigationStarting += (s, e) => {
                    if (!e.Uri.StartsWith(WallpaperHost.Instance.BaseUrl, StringComparison.Ordinal)) e.Cancel = true;
                };
                browser.CoreWebView2.WebMessageReceived += (s, e) => {
                    if (e.Source == WallpaperHost.Instance.PlayerUrl) Log(e.WebMessageAsJson);
                };
                browser.CoreWebView2.ProcessFailed += (s, e) => Log("WebView2 process failed: " + e.ProcessFailedKind);
                browser.CoreWebView2.Navigate(WallpaperHost.Instance.PlayerUrl);
                Log("Player: " + WallpaperHost.Instance.PlayerUrl);
            }
            catch (Exception error) { Log("Initialize failed: " + error); }
        }

        public void Update(Setting setting)
        {
            WallpaperHost.Instance.Configure(setting.WallpaperEngineDirectory, setting.WallpaperEngineMonitor);
            opacity = setting.IsHidden ? 0 : Math.Max(0, Math.Min(1, setting.Opacity));
            Resize();
        }

        private void Resize()
        {
            if (surface == null && hostProvider != null)
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
                        surface = new WallpaperSurface(root, view, gutter, browser);
                        Log("Editor surface: " + root.GetType().FullName + "; gutter=" + gutter?.GetType().FullName);
                    }
                }
                catch (ArgumentException) { } // The adapter may not exist during TextViewCreated.
            }
            if (surface != null) { surface.Resize(); return; }
            browser.Width = Math.Max(1, view.ViewportWidth);
            browser.Height = Math.Max(1, view.ViewportHeight);
            // Adornment coordinates are text coordinates. Refreshing on a
            // layout must retain the viewport offset, rather than reset it.
            Canvas.SetLeft(browser, view.ViewportLeft);
            Canvas.SetTop(browser, view.ViewportTop);
        }
        private void OnResize(object sender, EventArgs e) => Resize();

        private void OnTick(object sender, EventArgs e) => Microsoft.VisualStudio.Threading.TplExtensions.Forget(UpdatePlaybackAsync());

        private async Task UpdatePlaybackAsync()
        {
            if (disposed || updating || !initialize.IsCompleted || browser.CoreWebView2 == null) return;
            updating = true;
            try
            {
                var window = Window.GetWindow(view.VisualElement);
                Resize(); // Also attaches when the editor host becomes available after view creation.
                await browser.ExecuteScriptAsync("window.wallpaperControl?.opacity(" + opacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
                var shouldPause = opacity <= 0 || !view.VisualElement.IsVisible || window?.WindowState == WindowState.Minimized;
                if (paused != shouldPause)
                {
                    await browser.ExecuteScriptAsync("window.wallpaperControl?.pause(" + (shouldPause ? "true" : "false") + ")");
                    paused = shouldPause;
                }
                if (++ticks % 10 == 0 && Environment.GetEnvironmentVariable("CLAUDIA_WALLPAPER_DIAGNOSTICS") == "1")
                {
                    Log(await browser.ExecuteScriptAsync("window.wallpaperControl?.diagnostics()"));
                    var bounds = browser.TransformToAncestor(surface?.Root ?? view.VisualElement).TransformBounds(new Rect(browser.RenderSize));
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
            finally { updating = false; }
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

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer.Stop();
            view.ViewportWidthChanged -= OnResize;
            view.ViewportHeightChanged -= OnResize;
            layer.RemoveAdornmentsByTag(this);
            surface?.Dispose();
            browser.Dispose();
        }
    }
}
#endif

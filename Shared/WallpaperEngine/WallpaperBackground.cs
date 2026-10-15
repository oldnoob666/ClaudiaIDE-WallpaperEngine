#if VS_LATEST
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
        private readonly DispatcherTimer timer;
        private readonly Task initialize;
        private bool disposed;
        private bool? paused;
        private bool updating;
        private int ticks;
        private double opacity;

        public WallpaperBackground(IWpfTextView view)
        {
            this.view = view;
            layer = view.GetAdornmentLayer("ClaudiaIDE.WallpaperEngine");
            browser = new WebView2CompositionControl { IsHitTestVisible = false, Focusable = false };
            Resize();
            layer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, this, browser, null);
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
            browser.Width = Math.Max(1, view.ViewportWidth);
            browser.Height = Math.Max(1, view.ViewportHeight);
            Canvas.SetLeft(browser, 0);
            Canvas.SetTop(browser, 0);
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
                await browser.ExecuteScriptAsync("window.wallpaperControl?.opacity(" + opacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
                var shouldPause = opacity <= 0 || !view.VisualElement.IsVisible || window?.WindowState == WindowState.Minimized;
                if (paused != shouldPause)
                {
                    await browser.ExecuteScriptAsync("window.wallpaperControl?.pause(" + (shouldPause ? "true" : "false") + ")");
                    paused = shouldPause;
                }
                if (++ticks % 10 == 0 && Environment.GetEnvironmentVariable("CLAUDIA_WALLPAPER_DIAGNOSTICS") == "1")
                    Log(await browser.ExecuteScriptAsync("window.wallpaperControl?.diagnostics()"));
                if (ticks == 20 && Environment.GetEnvironmentVariable("CLAUDIA_WALLPAPER_DIAGNOSTICS") == "1")
                {
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Max(1, view.VisualElement.ActualWidth), (int)Math.Max(1, view.VisualElement.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(view.VisualElement);
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
            browser.Dispose();
        }
    }
}
#endif

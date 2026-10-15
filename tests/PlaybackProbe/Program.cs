using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudiaIDE.WallpaperEngine;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static string output;
    private static void Log(string text) => File.AppendAllText(Path.Combine(output, "playback.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine);
    [STAThread]
    private static void Main(string[] args)
    {
        output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var app = new Application();
        var browser = new WebView2CompositionControl { IsHitTestVisible = false };
        var grid = new Grid(); grid.Children.Add(browser);
        var text = new TextBox { Text = "ClaudiaIDE 动态壁纸测试\n\nint answer = 42;\n// 文字层位于动态背景上方", Background = Brushes.Transparent, Foreground = Brushes.White, FontSize = 24, BorderThickness = new Thickness(0), Margin = new Thickness(30) };
        grid.Children.Add(text);
        var window = new Window { Title = "ClaudiaIDE 动态壁纸播放验证", Width = 1120, Height = 720, Content = grid, Background = Brushes.Black };
        window.Loaded += async (s, e) => {
            try { await Run(browser, grid, args[1], args[2]); }
            catch (Exception error) { Log("FAIL " + error); Environment.ExitCode = 1; }
            finally { browser.Dispose(); window.Close(); }
        };
        app.Run(window);
    }
    private static async Task Run(WebView2CompositionControl browser, Grid grid, string sceneEntry, string videoEntry)
    {
        var fixture = Path.Combine(output, "we-fixture"); Directory.CreateDirectory(fixture);
        void Select(string entry) => File.WriteAllText(Path.Combine(fixture, "config.json"), new JObject {
            [Environment.UserName] = new JObject { ["general"] = new JObject { ["wallpaperconfig"] = new JObject { ["selectedwallpapers"] = new JObject { ["Monitor0"] = new JObject { ["file"] = entry } } } } }
        }.ToString());
        Select(sceneEntry);
        using (var host = new WallpaperHost(Path.GetFullPath("Shared/WallpaperEngine/Player")))
        {
            host.Configure(fixture, "Monitor0");
            await browser.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, Path.Combine(output, "browser-data")));
            browser.CoreWebView2.WebMessageReceived += (s, e) => Log(e.WebMessageAsJson);
            browser.CoreWebView2.Navigate(host.PlayerUrl);
            Log("Host: " + host.PlayerUrl);
            for (int i=0; i<18; i++) { await Task.Delay(2000); Log("Scene " + await browser.ExecuteScriptAsync("window.wallpaperControl?.diagnostics()")); }
            using (var file = File.Create(Path.Combine(output, "scene.png"))) await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            var sceneStats = await browser.ExecuteScriptAsync("window.wallpaperControl?.diagnostics()");
            var parsed = JObject.Parse(sceneStats);
            if ((string)parsed["current"] == "" || !string.IsNullOrEmpty((string)parsed["error"]) || (double?)parsed["scene"]?["fps"] <= 0) throw new Exception("Scene did not produce live frames: " + sceneStats);
            Select(videoEntry);
            await Task.Delay(8000);
            var before = JObject.Parse(await browser.ExecuteScriptAsync("window.wallpaperControl.diagnostics()"));
            await Task.Delay(3000);
            var after = JObject.Parse(await browser.ExecuteScriptAsync("window.wallpaperControl.diagnostics()"));
            Log("Video before: " + before.ToString(Newtonsoft.Json.Formatting.None));
            Log("Video after: " + after.ToString(Newtonsoft.Json.Formatting.None));
            if ((double?)after["video"]?["time"] <= (double?)before["video"]?["time"] || after["video"] == null) throw new Exception("Video playback did not advance after source switch");
            using (var file = File.Create(Path.Combine(output, "video.png"))) await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            var bitmap = new RenderTargetBitmap((int)grid.ActualWidth, (int)grid.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, "composition.png"))) png.Save(file);
            await browser.ExecuteScriptAsync("window.wallpaperControl.pause(true)");
            var paused = JObject.Parse(await browser.ExecuteScriptAsync("window.wallpaperControl.diagnostics()"));
            if ((bool?)paused["video"]?["paused"] != true) throw new Exception("Pause was not applied");
            Log("PASS scene frames, automatic video switch, advancing playback, pause, WPF composition capture");
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClaudiaIDE.Settings;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static readonly Type BackgroundType = typeof(ClaudiaIDE.ClaudiaIDE).Assembly.GetType("ClaudiaIDE.WallpaperEngine.WallpaperBackground", true);
    private static string output;
    private static void Check(bool value, string description)
    {
        File.AppendAllText(Path.Combine(output, "lifecycle.log"), (value ? "PASS " : "FAIL ") + description + Environment.NewLine);
        if (!value) throw new Exception(description);
    }
    private static object Field(object target, string name) => BackgroundType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static WebView2CompositionControl Browser(object background) => (WebView2CompositionControl)Field(background, "browser");
    private static FrameworkElement Visual(object background) => (FrameworkElement)Field(background, "visual");
    private static void Update(object background, Setting setting) => BackgroundType.GetMethod("Update").Invoke(background, new object[] { setting });

    private sealed class Proxy : RealProxy
    {
        private readonly Func<IMethodCallMessage, object> invoke;
        public Proxy(Type type, Func<IMethodCallMessage, object> invoke) : base(type) { this.invoke = invoke; }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            return new ReturnMessage(invoke(call), null, 0, call.LogicalCallContext, call);
        }
    }

    private sealed class Editor
    {
        public readonly Grid Root = new Grid { Background = Brushes.Black };
        public readonly Grid Text = new Grid { Background = Brushes.Black };
        public readonly Grid Gutter = new Grid { Background = Brushes.Black, Width = 60 };
        private readonly Canvas layer = new Canvas();
        public readonly object Background;
        public Brush ViewBackground = Brushes.Black;
        public Editor()
        {
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            Root.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(Text, 1); Root.Children.Add(Gutter); Root.Children.Add(Text); Text.Children.Add(layer);
            // VS has sibling margins/scrollbars after the text view. Removing
            // the earlier wallpaper child during inherited visibility walks
            // invalidates WPF's cached visual-child count for this grid.
            var trailingMargin = new Border { Width = 12, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.DimGray };
            Grid.SetColumn(trailingMargin, 1); Root.Children.Add(trailingMargin);
            Text.Children.Add(new TextBlock { Text = "// wallpaper stays behind editor text\nclass Example\n{\n    int answer = 42;\n}", FontSize = 18, Foreground = Brushes.White, Margin = new Thickness(12) });
            Gutter.Children.Add(new TextBlock { Text = "1\n2\n3\n4\n5", FontSize = 18, Foreground = Brushes.LightGray, Margin = new Thickness(10, 12, 0, 0) });
            var adornment = new Proxy(typeof(IAdornmentLayer), call => {
                if (call.MethodName == "AddAdornment") { layer.Children.Add((UIElement)call.Args[3]); return true; }
                if (call.MethodName == "RemoveAdornmentsByTag") layer.Children.Clear();
                return null;
            }).GetTransparentProxy();
            var view = (IWpfTextView)new Proxy(typeof(IWpfTextView), call => {
                switch (call.MethodName)
                {
                    case "GetAdornmentLayer": return adornment;
                    case "get_VisualElement": return Text;
                    case "get_ViewportWidth": return 500d;
                    case "get_ViewportHeight": return 400d;
                    case "get_ViewportLeft": case "get_ViewportTop": return 0d;
                    case "get_IsClosed": return false;
                    case "get_Background": return ViewBackground;
                    case "set_Background": ViewBackground = (Brush)call.Args[0]; return null;
                    default: return null; // Event subscriptions need no simulated events: WPF raises visibility.
                }
            }).GetTransparentProxy();
            var margin = new Proxy(typeof(IWpfTextViewMargin), call => call.MethodName == "get_VisualElement" ? Gutter : null).GetTransparentProxy();
            var host = (IWpfTextViewHost)new Proxy(typeof(IWpfTextViewHost), call => call.MethodName == "GetTextViewMargin" ? margin : null).GetTransparentProxy();
            Background = Activator.CreateInstance(BackgroundType, new object[] { view, (Func<IWpfTextViewHost>)(() => host) });
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "lifecycle.log"), "");
        var app = new Application();
        Window window = null;
        Editor first = null, second = null;
        try
        {
            first = new Editor();
            Check(Browser(first.Background) == null, "unloaded/hidden view never creates a browser");
            if (args.Length < 3) return;
            var fixture = Path.Combine(output, "fixture"); Directory.CreateDirectory(fixture);
            void Select(string entry) => File.WriteAllText(Path.Combine(fixture, "config.json"), new JObject {
                [Environment.UserName] = new JObject { ["general"] = new JObject { ["wallpaperconfig"] = new JObject { ["selectedwallpapers"] = new JObject { ["Monitor0"] = new JObject { ["file"] = entry } } } } }
            }.ToString());
            Select(args[1]);
            var setting = new Setting { WallpaperEngineDirectory = fixture, WallpaperEngineMonitor = "Monitor0", Opacity = 0.35 };
            Update(first.Background, setting);
            Check(Browser(first.Background) == null, "settings update of a hidden view stays resource free");
            second = new Editor(); Update(second.Background, setting);
            var panels = new Grid(); panels.ColumnDefinitions.Add(new ColumnDefinition()); panels.ColumnDefinitions.Add(new ColumnDefinition());
            panels.Children.Add(first.Root); Grid.SetColumn(second.Root, 1); panels.Children.Add(second.Root); second.Root.Visibility = Visibility.Collapsed;
            window = new Window { Title = "ClaudiaIDE lifecycle regression", Width = 1040, Height = 520, Content = panels, Background = Brushes.Black, ShowActivated = false };
            window.Loaded += async (sender, e) => {
                try
                {
                    await Until(() => Task.FromResult(Browser(first.Background) != null), "child editor loaded event starts player");
                    var abandoned = Browser(first.Background);
                    Check(abandoned != null, "visible editor starts its player");
                    Check(Visual(first.Background).Opacity == 0 && abandoned.DefaultBackgroundColor.A == 0, "browser is transparent and concealed before asynchronous initialization");
                    Check(first.ViewBackground == Brushes.Black && first.Gutter.Background == Brushes.Black, "original theme stays intact until first frame");
                    first.Root.Visibility = Visibility.Collapsed;
                    await Until(() => Task.FromResult(Browser(first.Background) == null), "hide during initialization releases player after visibility walk");
                    first.Root.Visibility = Visibility.Visible;
                    await Until(() => Task.FromResult(Browser(first.Background) != null), "return schedules fresh player outside visibility walk");
                    Check(Browser(first.Background) != null && Browser(first.Background) != abandoned, "rapid return creates a fresh player");
                    await Ready(first.Background, "initial scene");
                    if (args.Length > 3 && args[3] == "--inspect") await Task.Delay(20000);
                    Check(Browser(second.Background) == null, "hidden tab never initializes alongside active editor");
                    var released = Browser(first.Background);
                    first.Root.Visibility = Visibility.Collapsed;
                    await Until(() => Task.FromResult(Browser(first.Background) == null), "hide with trailing scrollbar sibling completes without WPF child-index corruption");
                    Check(Browser(first.Background) == null && first.ViewBackground == Brushes.Black, "hidden tab releases browser and restores editor brushes");
                    Check(!((System.Windows.Threading.DispatcherTimer)Field(first.Background, "timer")).IsEnabled, "hidden tab has no playback polling timer");
                    bool disposed = false;
                    try { await released.ExecuteScriptAsync("1"); }
                    catch (InvalidOperationException) { disposed = true; }
                    Check(disposed, "released native WebView rejects script execution");
                    Select(args[2]);
                    second.Root.Visibility = Visibility.Visible;
                    await Ready(second.Background, "restored video");
                    await Until(async () => {
                        var state = JObject.Parse(await Browser(second.Background).ExecuteScriptAsync("window.wallpaperControl.diagnostics()"));
                        return state["video"] is JObject clip && (double?)clip["time"] > 0.2;
                    }, "current wallpaper video advances after changing selection while hidden");
                    if (args.Length > 3 && args[3] == "--inspect") await Task.Delay(20000);
                    first.Root.Visibility = Visibility.Visible;
                    await Ready(first.Background, "split editor");
                    Check(Browser(first.Background) != null && Browser(second.Background) != null, "two visible split editors both retain playback");
                    setting.IsHidden = true; Update(first.Background, setting); Update(second.Background, setting);
                    await Until(() => Task.FromResult(Browser(first.Background) == null && Browser(second.Background) == null), "hide command drains queued lifecycle work");
                    Check(Browser(first.Background) == null && Browser(second.Background) == null, "hide command releases all visible players");
                    setting.IsHidden = false; Update(first.Background, setting); Update(second.Background, setting);
                    await Ready(first.Background, "show command");
                    window.WindowState = WindowState.Minimized;
                    await Until(() => Task.FromResult(Browser(first.Background) == null && Browser(second.Background) == null), "minimize releases players on next dispatcher turn");
                    window.WindowState = WindowState.Normal;
                    await Ready(first.Background, "window restore");
                    panels.Children.Remove(first.Root);
                    await Until(() => Task.FromResult(Browser(first.Background) == null), "unloaded editor releases player");
                    panels.Children.Add(first.Root);
                    await Ready(first.Background, "reloaded editor");
                    for (int i = 0; i < 30; i++)
                    {
                        first.Root.Visibility = Visibility.Collapsed; second.Root.Visibility = Visibility.Visible;
                        await Until(() => Task.FromResult(Browser(first.Background) == null && Browser(second.Background) != null), "stress switch to second " + i);
                        second.Root.Visibility = Visibility.Collapsed; first.Root.Visibility = Visibility.Visible;
                        await Until(() => Task.FromResult(Browser(second.Background) == null && Browser(first.Background) != null), "stress switch to first " + i);
                    }
                    await Ready(first.Background, "after rapid switching");
                    var retained = Browser(first.Background);
                    for (int i = 0; i < 100; i++) { first.Root.Visibility = Visibility.Collapsed; first.Root.Visibility = Visibility.Visible; }
                    await Ready(first.Background, "coalesced visibility changes");
                    Check(Browser(first.Background) == retained, "same-turn visibility changes coalesce without browser churn");
                    await (Task)BackgroundType.GetMethod("DisposeAsync").Invoke(first.Background, null);
                    Check(first.ViewBackground == Brushes.Black && first.Gutter.Background == Brushes.Black, "mode transition awaits old brush restoration");
                    first.ViewBackground = Brushes.DarkBlue; first.Gutter.Background = Brushes.DarkBlue;
                    first.Root.Visibility = Visibility.Collapsed; first.Root.Visibility = Visibility.Visible;
                    await Until(() => Task.FromResult(Browser(first.Background) == null), "close drains deferred player disposal");
                    Check(Browser(first.Background) == null, "disposed editor cannot resurrect from a visibility event");
                    await Task.Delay(50);
                    Check(first.ViewBackground == Brushes.DarkBlue && first.Gutter.Background == Brushes.DarkBlue, "completed disposal cannot overwrite replacement-mode brushes");
                }
                catch (Exception error) { File.AppendAllText(Path.Combine(output, "lifecycle.log"), error + Environment.NewLine); Environment.ExitCode = 1; }
                finally { window.Close(); }
            };
            app.Run(window);
        }
        catch (Exception error) { File.AppendAllText(Path.Combine(output, "lifecycle.log"), error + Environment.NewLine); Environment.ExitCode = 1; }
        finally { ((IDisposable)first?.Background)?.Dispose(); ((IDisposable)second?.Background)?.Dispose(); }
    }

    private static async Task Ready(object background, string label)
    {
        var clock = Stopwatch.StartNew();
        await Until(() => {
            var browser = Browser(background);
            if (browser == null) return Task.FromResult(false);
            var ready = (bool)Field(background, "ready");
            if (!ready && Visual(background).Opacity != 0) throw new Exception("Unready frame exposed: " + label);
            return Task.FromResult(ready);
        }, label + " produces a first frame");
        Check(Math.Abs(Visual(background).Opacity - 0.35) < 0.001, label + " applies opacity before first visible frame");
        File.AppendAllText(Path.Combine(output, "lifecycle.log"), label + " first frame: " + clock.ElapsedMilliseconds + "ms" + Environment.NewLine);
    }
    private static async Task Until(Func<Task<bool>> condition, string label)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 20000)
        {
            if (await condition()) { Check(true, label); return; }
            await Task.Delay(25);
        }
        Check(false, "timeout: " + label);
    }
}

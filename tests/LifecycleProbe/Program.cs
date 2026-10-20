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
            if (call.MethodName == "GetHashCode") return new ReturnMessage(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this), null, 0, call.LogicalCallContext, call);
            if (call.MethodName == "Equals") return new ReturnMessage(ReferenceEquals(GetTransparentProxy(), call.Args[0]), null, 0, call.LogicalCallContext, call);
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
        public readonly IWpfTextView View;
        public Brush ViewBackground = Brushes.Black;
        private EventHandler gotFocus, viewportChanged, closed;
        private EventHandler<BackgroundBrushChangedEventArgs> backgroundChanged;
        private bool isClosed;
        public void Focus() => gotFocus?.Invoke(View, EventArgs.Empty);
        public void Resize() => viewportChanged?.Invoke(View, EventArgs.Empty);
        public void Close() { isClosed = true; closed?.Invoke(View, EventArgs.Empty); }
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
                    case "get_IsClosed": return isClosed;
                    case "get_Background": return ViewBackground;
                    case "set_Background":
                        if (ViewBackground != (Brush)call.Args[0])
                        {
                            ViewBackground = (Brush)call.Args[0];
                            backgroundChanged?.Invoke(View, new BackgroundBrushChangedEventArgs(ViewBackground));
                        }
                        return null;
                    case "add_BackgroundBrushChanged": backgroundChanged += (EventHandler<BackgroundBrushChangedEventArgs>)call.Args[0]; return null;
                    case "remove_BackgroundBrushChanged": backgroundChanged -= (EventHandler<BackgroundBrushChangedEventArgs>)call.Args[0]; return null;
                    case "add_Closed": closed += (EventHandler)call.Args[0]; return null;
                    case "remove_Closed": closed -= (EventHandler)call.Args[0]; return null;
                    case "add_GotAggregateFocus": gotFocus += (EventHandler)call.Args[0]; return null;
                    case "remove_GotAggregateFocus": gotFocus -= (EventHandler)call.Args[0]; return null;
                    case "add_ViewportWidthChanged": viewportChanged += (EventHandler)call.Args[0]; return null;
                    case "remove_ViewportWidthChanged": viewportChanged -= (EventHandler)call.Args[0]; return null;
                    default: return null; // Event subscriptions need no simulated events: WPF raises visibility.
                }
            }).GetTransparentProxy();
            View = view;
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
        if (args.Length > 3 && args[3] == "--ide") { RunIde(args); return; }
        var app = new Application();
        Window window = null;
        Editor first = null, second = null, third = null;
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
                    await Task.Delay(150);
                    Check(Browser(first.Background) == abandoned, "hide during initialization retains the same player during the switching period");
                    first.Root.Visibility = Visibility.Visible;
                    await Until(() => Task.FromResult(Browser(first.Background) != null), "return retains player outside visibility walk");
                    Check(Browser(first.Background) == abandoned, "rapid return reuses the initializing player");
                    await Ready(first.Background, "initial scene");
                    if (args.Length > 3 && args[3] == "--inspect") await Task.Delay(20000);
                    Check(Browser(second.Background) == null, "hidden tab never initializes alongside active editor");
                    var cached = Browser(first.Background);
                    first.Root.Visibility = Visibility.Collapsed;
                    await Task.Delay(150);
                    Check(Browser(first.Background) == cached, "hide with trailing scrollbar sibling retains player without WPF child-index corruption");
                    Check(!((System.Windows.Threading.DispatcherTimer)Field(first.Background, "timer")).IsEnabled, "cached hidden tab stops layout and diagnostics polling");
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
                    Check(Browser(first.Background) == cached, "return to a ready tab reuses its native browser");
                    var hiddenVideo = Browser(second.Background);
                    await hiddenVideo.ExecuteScriptAsync("window.switchEvents = []; for (const name of ['pause', 'play', 'playing', 'visibilitychange']) document.addEventListener(name, e => window.switchEvents.push({ name, hidden: document.hidden, time: performance.now() }), true)");
                    second.Root.Visibility = Visibility.Collapsed;
                    var videoBeforeHide = JObject.Parse(await hiddenVideo.ExecuteScriptAsync("window.wallpaperControl.diagnostics()"));
                    await Task.Delay(1500);
                    var videoWhileHidden = JObject.Parse(await hiddenVideo.ExecuteScriptAsync("window.wallpaperControl.diagnostics()"));
                    File.AppendAllText(Path.Combine(output, "lifecycle.log"), "Hidden video: " + await hiddenVideo.ExecuteScriptAsync("({ keepAlive: window.claudiaKeepAlive, hidden: document.hidden, events: window.switchEvents, state: window.wallpaperControl.diagnostics() })") + Environment.NewLine);
                    Check((bool?)videoWhileHidden["video"]?["paused"] == false, "cached hidden video is not paused by page visibility");
                    Check((double?)videoWhileHidden["video"]?["time"] > (double?)videoBeforeHide["video"]?["time"] + 0.3, "cached hidden native video keeps advancing");
                    second.Root.Visibility = Visibility.Visible;
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
                    var beforeUnload = Browser(first.Background);
                    panels.Children.Remove(first.Root);
                    await Task.Delay(150);
                    Check(Browser(first.Background) == beforeUnload, "temporary tab unload retains player during switching period");
                    panels.Children.Add(first.Root);
                    await Ready(first.Background, "reloaded editor");
                    Check(Browser(first.Background) == beforeUnload, "reloaded tab reuses player");
                    var otherRetained = Browser(second.Background);
                    for (int i = 0; i < 30; i++)
                    {
                        first.Root.Visibility = Visibility.Collapsed; second.Root.Visibility = Visibility.Visible;
                        await Task.Delay(25);
                        Check(Browser(first.Background) == beforeUnload && Browser(second.Background) == otherRetained, "stress switch to second reuses both players " + i);
                        second.Root.Visibility = Visibility.Collapsed; first.Root.Visibility = Visibility.Visible;
                        await Task.Delay(25);
                        Check(Browser(first.Background) == beforeUnload && Browser(second.Background) == otherRetained, "stress switch to first reuses both players " + i);
                    }
                    await Ready(first.Background, "after rapid switching");
                    var retained = Browser(first.Background);
                    for (int i = 0; i < 100; i++) { first.Root.Visibility = Visibility.Collapsed; first.Root.Visibility = Visibility.Visible; }
                    await Ready(first.Background, "coalesced visibility changes");
                    Check(Browser(first.Background) == retained, "same-turn visibility changes coalesce without browser churn");
                    second.Root.Visibility = Visibility.Visible;
                    second.Focus();
                    third = new Editor(); Update(third.Background, setting);
                    Grid.SetColumn(third.Root, 1); panels.Children.Add(third.Root);
                    await Ready(third.Background, "second visible split for idle expiry");
                    first.Root.Visibility = Visibility.Collapsed;
                    var secondBeforeSleep = Browser(second.Background);
                    var thirdBeforeSleep = Browser(third.Background);
                    File.AppendAllText(Path.Combine(output, "lifecycle.log"), "Waiting 35 seconds before switching focused split editor" + Environment.NewLine);
                    await Task.Delay(35000);
                    third.Focus();
                    // Layout and settings updates must not extend the inactivity deadline.
                    for (var i = 0; i < 15; i++)
                    {
                        first.Resize(); second.Resize(); third.Resize(); Update(third.Background, setting);
                        await Task.Delay(2000);
                    }
                    Check(Browser(first.Background) == retained, "switching focused split resets the global deadline for an older hidden tab beyond 60 seconds");
                    await Task.Delay(28000);
                    Check(Browser(first.Background) == retained, "hidden player remains alive just before 60 seconds since the last switch");
                    await Until(() => Task.FromResult(Browser(first.Background) == null), "60 seconds without switching releases hidden player despite layout and settings activity");
                    Check(first.ViewBackground == Brushes.Black && first.Gutter.Background == Brushes.Black, "sleep restores hidden editor brushes");
                    Check(Browser(second.Background) == secondBeforeSleep && Browser(third.Background) == thirdBeforeSleep, "sleep retains both visible split players");
                    bool disposed = false;
                    try { await retained.ExecuteScriptAsync("1"); }
                    catch (InvalidOperationException) { disposed = true; }
                    Check(disposed, "sleep disposes native WebView instead of merely pausing it");
                    first.Root.Visibility = Visibility.Visible;
                    await Ready(first.Background, "return after sleep");
                    Check(Browser(first.Background) != retained, "return after sleep creates a new player");
                    first.Root.Visibility = Visibility.Collapsed;
                    await (Task)BackgroundType.GetMethod("DisposeAsync").Invoke(first.Background, null);
                    Check(Browser(first.Background) == null, "closing a cached hidden tab disposes it without waiting for sleep");
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
        finally { ((IDisposable)first?.Background)?.Dispose(); ((IDisposable)second?.Background)?.Dispose(); ((IDisposable)third?.Background)?.Dispose(); }
    }

    private static void RunIde(string[] args)
    {
        var app = new Application();
        var fixture = Path.Combine(output, "fixture"); Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "config.json"), new JObject {
            [Environment.UserName] = new JObject { ["general"] = new JObject { ["wallpaperconfig"] = new JObject { ["selectedwallpapers"] = new JObject { ["Monitor0"] = new JObject { ["file"] = args[1] } } } } }
        }.ToString());
        var setting = new Setting { ImageBackgroundType = ImageBackgroundType.WallpaperEngine, WallpaperEngineDirectory = fixture, WallpaperEngineMonitor = "Monitor0", Opacity = 0.35 };
        typeof(Setting).GetProperty("TransparentToolWindows").SetValue(setting, true);
        var root = new Grid { Background = Brushes.Black };
        var tool = new Grid { Background = Brushes.DarkGray };
        root.Children.Add(tool);
        var window = new Window { Title = "ClaudiaIDE tool window regression", Width = 1000, Height = 600, Content = root, ShowActivated = false };
        var type = BackgroundType.Assembly.GetType("ClaudiaIDE.WallpaperEngine.IdeWallpaperBackground", true);
        var ide = Activator.CreateInstance(type, window, root);
        object FieldIde(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ide);
        void UpdateIde() => type.GetMethod("Update").Invoke(ide, new object[] { setting });
        async Task ReadyIde(string label) => await Until(() => Task.FromResult((bool)FieldIde("ready") && ((Grid)FieldIde("visual"))?.Opacity == 0.35), label);
        Editor editor = null, newlyOpened = null;
        UpdateIde();
        Check(FieldIde("browser") == null, "unloaded main window creates no player");
        window.Loaded += async (s, e) => {
            try
            {
                UpdateIde();
                await Until(() => Task.FromResult(FieldIde("browser") != null), "loaded main window starts one shared player");
                Check(tool.Background == Brushes.DarkGray, "tool pane stays opaque while the first frame loads");
                await ReadyIde("main window plays wallpaper without an open document");
                Check(tool.Background == Brushes.Transparent && root.Children.Count == 2, "tool window has wallpaper behind a transparent background");
                editor = new Editor(); root.Children.Add(editor.Root); Update(editor.Background, setting);
                type.GetMethod("RegisterView", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { editor.View });
                await Until(() => Task.FromResult(editor.ViewBackground == Brushes.Transparent), "new editor joins shared background");
                Check(Browser(editor.Background) == null, "shared mode does not allocate a duplicate editor player");
                // Disable the fallback poll so a one-second repair cannot hide a black first frame.
                ((System.Windows.Threading.DispatcherTimer)FieldIde("timer")).Stop();
                var sharedPlayer = FieldIde("browser");
                editor.Root.Visibility = Visibility.Collapsed;
                editor.Root.Background = Brushes.Black; editor.Text.Background = Brushes.Black;
                editor.View.Background = Brushes.Black;
                var firstFrame = AtNextFrame(() => editor.Root.Background == Brushes.Transparent
                    && editor.Text.Background == Brushes.Transparent && editor.ViewBackground == Brushes.Transparent);
                editor.Root.Visibility = Visibility.Visible;
                Check(await firstFrame, "switching tabs clears editor and ancestor backgrounds before the first rendered frame without polling");
                editor.Root.Background = Brushes.DarkBlue; editor.Text.Background = Brushes.DarkBlue;
                firstFrame = AtNextFrame(() => editor.Root.Background == Brushes.Transparent
                    && editor.Text.Background == Brushes.Transparent && editor.ViewBackground == Brushes.Transparent);
                editor.View.Background = Brushes.DarkBlue;
                Check(await firstFrame, "editor background reset is repaired before rendering without waiting for the timer");
                editor.Root.Background = Brushes.Black;
                firstFrame = AtNextFrame(() => editor.Root.Background == Brushes.Transparent);
                editor.Focus();
                Check(await firstFrame, "editor focus refreshes an opaque ancestor before rendering");
                newlyOpened = new Editor();
                type.GetMethod("RegisterView", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { newlyOpened.View });
                firstFrame = AtNextFrame(() => newlyOpened.Root.Background == Brushes.Transparent
                    && newlyOpened.ViewBackground == Brushes.Transparent);
                root.Children.Add(newlyOpened.Root);
                Check(await firstFrame, "newly loaded text view is transparent on its first frame without polling");
                Check(ReferenceEquals(sharedPlayer, FieldIde("browser")), "tab transparency updates retain the same shared native player");
                newlyOpened.Close(); root.Children.Remove(newlyOpened.Root);
                setting.IsHidden = true; UpdateIde(); Update(editor.Background, setting);
                await Until(() => Task.FromResult(FieldIde("browser") == null), "hide command releases shared player");
                Check(tool.Background == Brushes.DarkGray && editor.ViewBackground == Brushes.DarkBlue, "hide command restores tool background and the editor's latest theme brush");
                setting.IsHidden = false; UpdateIde(); Update(editor.Background, setting);
                await ReadyIde("show command restores shared player");
                window.WindowState = WindowState.Minimized;
                await Until(() => Task.FromResult(FieldIde("browser") == null), "minimize releases the shared player");
                Check(tool.Background == Brushes.DarkGray, "minimized window restores tool brushes");
                window.WindowState = WindowState.Normal;
                await ReadyIde("restore window rebuilds shared wallpaper");
                typeof(Setting).GetProperty("TransparentToolWindows").SetValue(setting, false); UpdateIde(); Update(editor.Background, setting);
                await Until(() => Task.FromResult(FieldIde("browser") == null && Browser(editor.Background) != null), "disabling transparency returns to editor-only playback");
                Check(tool.Background == Brushes.DarkGray, "disabling transparency restores opaque tool pane");
                await Ready(editor.Background, "editor-only after disabling transparency");
                var cachedEditor = Browser(editor.Background);
                root.Children.Remove(editor.Root);
                await Task.Delay(150);
                Check(Browser(editor.Background) == cachedEditor, "unloaded open editor stays cached before switching to shared mode");
                typeof(Setting).GetProperty("TransparentToolWindows").SetValue(setting, true); UpdateIde(); Update(editor.Background, setting);
                await ReadyIde("re-enabling transparency restores shared wallpaper");
                await Until(() => Task.FromResult(Browser(editor.Background) == null), "switching to shared mode releases cached unloaded editor immediately");
                root.Children.Add(editor.Root);
                await Until(() => Task.FromResult(Browser(editor.Background) == null && editor.ViewBackground == Brushes.Transparent), "re-enabling releases editor player and preserves shared transparency");
                for (var i = 0; i < 30; i++)
                {
                    window.Visibility = Visibility.Hidden; window.Visibility = Visibility.Visible;
                    typeof(Setting).GetProperty("TransparentToolWindows").SetValue(setting, false); UpdateIde();
                    typeof(Setting).GetProperty("TransparentToolWindows").SetValue(setting, true); UpdateIde();
                }
                await ReadyIde("rapid shell visibility and switch changes remain stable");
                ((IDisposable)ide).Dispose();
                await Until(() => Task.FromResult(FieldIde("browser") == null), "disposing shared background drains deferred native cleanup");
                Check(tool.Background == Brushes.DarkGray, "disposing shared background restores tool pane");
            }
            catch (Exception error) { File.AppendAllText(Path.Combine(output, "lifecycle.log"), error + Environment.NewLine); Environment.ExitCode = 1; }
            finally { ((IDisposable)editor?.Background)?.Dispose(); ((IDisposable)newlyOpened?.Background)?.Dispose(); ((IDisposable)ide).Dispose(); window.Close(); }
        };
        app.Run(window);
    }

    private static async Task<bool> AtNextFrame(Func<bool> condition)
    {
        var frame = new TaskCompletionSource<bool>();
        EventHandler handler = (s, e) => frame.TrySetResult(condition());
        CompositionTarget.Rendering += handler;
        try { return await Task.WhenAny(frame.Task, Task.Delay(2000)) == frame.Task && await frame.Task; }
        finally { CompositionTarget.Rendering -= handler; }
    }

    private static async Task Ready(object background, string label)
    {
        var clock = Stopwatch.StartNew();
        await Until(() => {
            var browser = Browser(background);
            if (browser == null) return Task.FromResult(false);
            var ready = (bool)Field(background, "ready");
            if (!ready && Visual(background).Opacity != 0) throw new Exception("Unready frame exposed: " + label);
            return Task.FromResult(ready && Math.Abs(Visual(background).Opacity - 0.35) < 0.001);
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

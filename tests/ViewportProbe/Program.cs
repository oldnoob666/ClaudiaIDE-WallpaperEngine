using System;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.Web.WebView2.Wpf;

internal static class Program
{
    // The VS layer uses text coordinates; translating it by the viewport's
    // origin reproduces the editor's documented rendering coordinate system.
    private sealed class View : RealProxy
    {
        internal double Left, Top, Width = 900, Height = 600;
        internal FrameworkElement Element;
        internal Brush Background = Brushes.Black;
        internal View() : base(typeof(IWpfTextView)) { }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            object value;
            switch (call.MethodName)
            {
                case "get_ViewportLeft": value = Left; break;
                case "get_ViewportTop": value = Top; break;
                case "get_ViewportWidth": value = Width; break;
                case "get_ViewportHeight": value = Height; break;
                case "get_VisualElement": value = Element; break;
                case "get_Background": value = Background; break;
                case "set_Background": Background = (Brush)call.Args[0]; value = null; break;
                default: throw new NotSupportedException(call.MethodName);
            }
            return new ReturnMessage(value, null, 0, call.LogicalCallContext, call);
        }
    }

    private sealed class Proxy : RealProxy
    {
        private readonly Func<IMethodCallMessage, object> invoke;
        internal Proxy(Type type, Func<IMethodCallMessage, object> invoke) : base(type) { this.invoke = invoke; }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            return new ReturnMessage(invoke(call), null, 0, call.LogicalCallContext, call);
        }
    }

    [STAThread]
    private static int Main()
    {
        var view = new View { Left = 0, Top = 0 };
        var root = new Grid(); var layer = new Canvas(); root.Children.Add(layer);
        using (var browser = new WebView2CompositionControl())
        {
            layer.Children.Add(browser);
            var type = typeof(ClaudiaIDE.ClaudiaIDE).Assembly.GetType("ClaudiaIDE.WallpaperEngine.WallpaperBackground", true);
            // Exercise production placement without launching a renderer or VS.
            var background = FormatterServices.GetUninitializedObject(type);
            type.GetField("view", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(background, view.GetTransparentProxy());
            type.GetField("browser", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(background, browser);
            type.GetField("visual", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(background, browser);
            type.GetField("ready", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(background, true);
            var place = type.GetMethod("Resize", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var position in new[] { new Point(0, 0), new Point(0, 300), new Point(180, 2400), new Point(0, 0) })
            {
                view.Left = position.X; view.Top = position.Y;
                if (position.Y > 1000) { view.Width = 1200; view.Height = 720; }
                layer.RenderTransform = new TranslateTransform(-view.Left, -view.Top);
                place.Invoke(background, null);
                root.Measure(new Size(view.Width, view.Height));
                root.Arrange(new Rect(0, 0, view.Width, view.Height));
                root.UpdateLayout();
                var actual = browser.TransformToAncestor(root).TransformBounds(new Rect(browser.RenderSize));
                if (Math.Abs(actual.Left) > 0.01 || Math.Abs(actual.Top) > 0.01 || Math.Abs(actual.Width - view.Width) > 0.01 || Math.Abs(actual.Height - view.Height) > 0.01)
                {
                    Console.Error.WriteLine("FAIL viewport=" + position + " background bounds=" + actual);
                    return 1;
                }
                Console.WriteLine("PASS viewport=" + position + " background bounds=" + actual);
            }
            layer.Children.Clear();

            var hostRoot = new Grid { Background = Brushes.Black };
            hostRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            hostRoot.ColumnDefinitions.Add(new ColumnDefinition());
            var margin = new Grid { Background = Brushes.Black };
            var glyph = new Button { Content = "breakpoint", IsHitTestVisible = true };
            var transientMarker = new Border { Background = Brushes.Red }; margin.Children.Add(transientMarker);
            margin.Children.Add(glyph); hostRoot.Children.Add(margin);
            var textArea = new Grid { Background = Brushes.Black }; Grid.SetColumn(textArea, 1); hostRoot.Children.Add(textArea);
            var textLayer = new Canvas(); textArea.Children.Add(textLayer); textLayer.Children.Add(browser);
            view.Element = textArea; view.Left = 180; view.Top = 2400; view.Width = 900; view.Height = 600;
            textLayer.RenderTransform = new TranslateTransform(-view.Left, -view.Top);
            var marginProxy = new Proxy(typeof(IWpfTextViewMargin), call => call.MethodName == "get_VisualElement" ? margin : null).GetTransparentProxy();
            var host = (IWpfTextViewHost)new Proxy(typeof(IWpfTextViewHost), call => call.MethodName == "get_HostControl" ? hostRoot : call.MethodName == "GetTextViewMargin" ? marginProxy : null).GetTransparentProxy();
            var layerProxy = new Proxy(typeof(IAdornmentLayer), call => { if (call.MethodName == "RemoveAdornmentsByTag") textLayer.Children.Remove(browser); return null; }).GetTransparentProxy();
            type.GetField("layer", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(background, layerProxy);
            type.GetField("hostProvider", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(background, (Func<IWpfTextViewHost>)(() => host));
            place.Invoke(background, null);
            hostRoot.Measure(new Size(1000, 600)); hostRoot.Arrange(new Rect(0, 0, 1000, 600)); hostRoot.UpdateLayout();
            var full = browser.TransformToAncestor(hostRoot).TransformBounds(new Rect(browser.RenderSize));
            if (Math.Abs(full.Left) > 0.01 || Math.Abs(full.Top) > 0.01 || Math.Abs(full.Width - 1000) > 0.01 || Math.Abs(full.Height - 600) > 0.01)
            { Console.Error.WriteLine("FAIL full editor including gutter background bounds=" + full); return 1; }
            if (!glyph.IsHitTestVisible || !glyph.IsEnabled || browser.IsHitTestVisible || margin.Background != Brushes.Transparent || view.Background != Brushes.Transparent)
            { Console.Error.WriteLine("FAIL gutter transparency or input preservation"); return 1; }
            Console.WriteLine("PASS wallpaper covers gutter+text; glyph input preserved");
            margin.Children.Remove(transientMarker); place.Invoke(background, null);
            if (transientMarker.Background != Brushes.Red)
            { Console.Error.WriteLine("FAIL detached gutter visuals are not restored/released"); return 1; }
            Console.WriteLine("PASS detached gutter visuals restored and released");
            var surface = type.GetField("surface", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(background) as IDisposable;
            surface?.Dispose();
            if (margin.Background != Brushes.Black || textArea.Background != Brushes.Black || view.Background != Brushes.Black)
            { Console.Error.WriteLine("FAIL original gutter/editor backgrounds not restored"); return 1; }
            Console.WriteLine("PASS original editor brushes restored on disposal");
        }
        return 0;
    }
}

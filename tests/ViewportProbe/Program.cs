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
                default: throw new NotSupportedException(call.MethodName);
            }
            return new ReturnMessage(value, null, 0, call.LogicalCallContext, call);
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
        }
        return 0;
    }
}

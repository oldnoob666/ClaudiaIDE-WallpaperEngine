using System;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ClaudiaIDE.Options;
using ClaudiaIDE.Settings;

internal static class Program
{
    private static int checks;
    private static readonly Assembly Extension = typeof(Setting).Assembly;
    private static void Check(bool value, string text)
    {
        if (!value) throw new Exception("FAIL " + text);
        checks++;
        Console.WriteLine("PASS " + text);
    }
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name).Invoke(target, args);

    private static void CheckEditorMouseHit(Type type)
    {
        var host = new Grid { Width = 500, Height = 200, Background = Brushes.Black };
        var wallpaper = new Grid { IsHitTestVisible = false };
        var editor = new TextBox { Text = "public class Example\n{\n    int value = 42;\n}", FontSize = 16 };
        // VS draws caret/selection/adornments on canvases above the text.
        // A null background lets blank pixels pass mouse hits to the editor.
        var adornments = new Canvas();
        host.Children.Add(wallpaper); host.Children.Add(editor); host.Children.Add(adornments);
        var window = new Window { Content = host, Width = 520, Height = 240, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        try
        {
            host.Measure(new Size(500, 200)); host.Arrange(new Rect(0, 0, 500, 200)); host.UpdateLayout();
            void Render() => host.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
            Render();
            bool HitsEditor()
            {
                var hit = host.InputHitTest(new Point(100, 55)) as Visual;
                return hit != null && (hit == editor || hit.IsDescendantOf(editor));
            }
            Check(HitsEditor(), "code receives mouse hits before enabling tool window transparency");
            using (var transparency = (IDisposable)Activator.CreateInstance(type, host, wallpaper))
            {
                Call(transparency, "Refresh");
                Render();
                var hit = host.InputHitTest(new Point(100, 55));
                Check(HitsEditor(), "transparent mode preserves code mouse hits through empty adornments; actual=" + hit?.GetType().Name);
                Check(adornments.Background == null, "click-through adornment background remains null");
                Call(transparency, "Refresh");
                Render();
                Check(HitsEditor(), "periodic transparency refresh does not intercept code clicks");
            }
            Check(HitsEditor(), "disabling transparency retains code mouse hits");
        }
        finally { window.Close(); }
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            var option = typeof(ClaudiaIdeOptionPageGrid).GetProperty("TransparentToolWindows");
            Check(option != null, "settings expose the tool window transparency switch");
            Check(!(bool)typeof(Setting).GetProperty("TransparentToolWindows").GetValue(new Setting()), "existing users keep opaque tool windows by default");
            var listener = Extension.GetType("ClaudiaIDE.WallpaperEngine.ToolWindowTextViewListener", true);
            Check(listener.GetCustomAttributesData().Any(a => a.AttributeType.Name == "TextViewRoleAttribute"), "tool text listener supplies the role metadata required by VS composition");
            var type = Extension.GetType("ClaudiaIDE.WallpaperEngine.ToolWindowTransparency", true);
            CheckEditorMouseHit(type);
            var root = new Grid { Background = Brushes.Black };
            var wallpaper = new Grid { Background = Brushes.Red };
            var pane = new Grid { Background = Brushes.Gray };
            var inherited = new Border();
            var bound = new Border();
            var source = new Border { Background = Brushes.DarkBlue };
            BindingOperations.SetBinding(bound, Border.BackgroundProperty, new Binding("Background") { Source = source });
            var dynamic = new Border();
            root.Resources["PaneBrush"] = Brushes.DarkGreen;
            dynamic.SetResourceReference(Border.BackgroundProperty, "PaneBrush");
            var selected = new TreeViewItem { IsSelected = true, Background = Brushes.Blue };
            var selectionBorder = new Border { Background = Brushes.Blue };
            selected.Header = selectionBorder;
            var label = new TextBlock { Text = "Solution Explorer", Foreground = Brushes.White, Background = Brushes.Yellow };
            var button = new Button { Background = Brushes.Purple };
            var tabs = new TabControl();
            var tree = new TreeView();
            var parent = new TreeViewItem { Header = "Expanded", IsExpanded = true };
            var child = new TreeViewItem { Header = "Child", Background = Brushes.Gray };
            parent.Items.Add(child); tree.Items.Add(parent);
            var tabContent = new Grid { Background = Brushes.Gray };
            var tab = new TabItem { Header = "Output", Background = Brushes.Blue, Content = tabContent };
            tabs.Items.Add(tab);
            pane.Children.Add(inherited); pane.Children.Add(bound); pane.Children.Add(dynamic);
            pane.Children.Add(selected); pane.Children.Add(label); pane.Children.Add(button);
            pane.Children.Add(tabs);
            pane.Children.Add(tree);
            root.Children.Add(wallpaper); root.Children.Add(pane);
            root.Measure(new Size(800, 600)); root.Arrange(new Rect(0, 0, 800, 600)); root.UpdateLayout();

            var transparency = Activator.CreateInstance(type, root, wallpaper);
            Call(transparency, "Refresh");
            Check(pane.Background == Brushes.Transparent && bound.Background == Brushes.Transparent && dynamic.Background == Brushes.Transparent, "tool pane backgrounds become transparent");
            Check(root.Background == Brushes.Black && wallpaper.Background == Brushes.Red, "theme backdrop and wallpaper remain intact");
            Check(selected.Background == Brushes.Blue && selectionBorder.Background == Brushes.Blue, "selected tree row highlight remains visible");
            Check(tab.Background == Brushes.Blue && tabContent.Background == Brushes.Transparent, "tab header highlight is preserved while selected content becomes transparent");
            Check(child.Background == Brushes.Transparent, "expanded tree rows do not leave opaque blocks over the wallpaper");
            parent.IsSelected = true;
            Call(transparency, "Refresh");
            Check(child.Background == Brushes.Transparent, "selecting an expanded parent preserves transparency of its unselected children");
            parent.IsSelected = false;
            child.IsSelected = true;
            Call(transparency, "Refresh");
            Check(child.Background == Brushes.Gray, "selecting a previously cleared row restores its highlight background");
            child.IsSelected = false;
            Call(transparency, "Refresh");
            Check(child.Background == Brushes.Transparent, "deselecting a tree row resumes background transparency");
            Check(label.Foreground == Brushes.White && label.Background == Brushes.Yellow && button.Background == Brushes.Purple, "text and interactive controls retain their appearance");
            source.Background = Brushes.Orange;
            root.Resources["PaneBrush"] = Brushes.DarkRed;
            Call(transparency, "Refresh");
            var added = new Grid { Background = Brushes.AliceBlue };
            pane.Children.Add(added);
            Call(transparency, "Refresh");
            Check(added.Background == Brushes.Transparent, "newly opened tool panes become transparent");
            pane.Children.Remove(added);
            Call(transparency, "Refresh");
            Check(added.Background == Brushes.AliceBlue, "detached panes restore their background immediately");
            ((IDisposable)transparency).Dispose();
            Check(pane.Background == Brushes.Gray, "disabling restores original local brush");
            Check(BindingOperations.IsDataBound(bound, Border.BackgroundProperty) && bound.Background == Brushes.Orange, "disabling restores the live binding and its current value");
            Check(dynamic.Background == Brushes.DarkRed, "disabling resolves the current theme resource rather than an old color");
            Check(inherited.ReadLocalValue(Border.BackgroundProperty) == DependencyProperty.UnsetValue, "disabling preserves unset property values");
            source.Background = Brushes.Pink;
            root.Resources["PaneBrush"] = Brushes.Teal;
            Check(bound.Background == Brushes.Pink && dynamic.Background == Brushes.Teal, "bindings and theme resources still update after restoring");
            for (var i = 0; i < 20; i++)
            {
                transparency = Activator.CreateInstance(type, root, wallpaper);
                Call(transparency, "Refresh");
                ((IDisposable)transparency).Dispose();
            }
            Check(pane.Background == Brushes.Gray && dynamic.Background == Brushes.Teal, "repeated toggles do not corrupt original backgrounds");
            Console.WriteLine("Tool window checks passed: " + checks);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

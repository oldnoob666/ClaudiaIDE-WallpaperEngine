#if VS_LATEST
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Editor;

namespace ClaudiaIDE.WallpaperEngine
{
    // One non-interactive player behind the entire editor pane, including its
    // left gutter. It lives outside the text view's clipping/scrolling surface.
    internal sealed class WallpaperSurface : IDisposable
    {
        private sealed class Background
        {
            public DependencyObject Element;
            public DependencyProperty Property;
            public object Local, Effective;
            public BindingBase Binding;
        }

        private readonly FrameworkElement browser;
        private readonly FrameworkElement text;
        private readonly IWpfTextView view;
        private readonly Brush textBackground;
        private readonly FrameworkElement gutter;
        private readonly Dictionary<DependencyObject, Background> originals = new Dictionary<DependencyObject, Background>();
        public Panel Root { get; }

        public WallpaperSurface(Panel root, IWpfTextView view, FrameworkElement gutter, FrameworkElement browser)
        {
            Root = root; this.view = view; this.text = view.VisualElement; this.textBackground = view.Background;
            this.gutter = gutter; this.browser = browser;
            browser.IsHitTestVisible = false; browser.Focusable = false;
            Panel.SetZIndex(browser, int.MinValue);
            if (root is Grid grid)
            {
                Grid.SetRowSpan(browser, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumnSpan(browser, Math.Max(1, grid.ColumnDefinitions.Count));
            }
            root.Children.Insert(0, browser);
            root.SizeChanged += OnSizeChanged;
            Resize();
        }

        public void Resize()
        {
            // Glyph/marker visuals are recycled while an editor stays open.
            // Restore detached controls now, rather than retain them until close.
            var detached = new List<Background>();
            foreach (var saved in originals.Values)
                if (!(saved.Element is Visual visual) || !visual.IsDescendantOf(Root)) detached.Add(saved);
            foreach (var saved in detached) { Restore(saved); originals.Remove(saved.Element); }
            browser.Width = Math.Max(1, Root.ActualWidth);
            browser.Height = Math.Max(1, Root.ActualHeight);
            Canvas.SetLeft(browser, 0); Canvas.SetTop(browser, 0);
            for (DependencyObject item = text; item != null && item != Root; item = VisualTreeHelper.GetParent(item)) ClearBackground(item);
            // The editor's drawing background is a public view property, not
            // necessarily a Background DP on its FrameworkElement.
            if (view.Background != Brushes.Transparent) view.Background = Brushes.Transparent;
            if (gutter != null)
            {
                for (DependencyObject item = gutter; item != null && item != Root; item = VisualTreeHelper.GetParent(item)) ClearBackground(item);
                ClearGutter(gutter);
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Resize();

        private void ClearGutter(DependencyObject item)
        {
            if (item == text || item == browser) return;
            ClearBackground(item);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) ClearGutter(VisualTreeHelper.GetChild(item, i));
        }

        private void ClearBackground(DependencyObject element)
        {
            if (element == Root || element is TextBlock) return;
            var field = element.GetType().GetField("BackgroundProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            var property = field?.GetValue(null) as DependencyProperty;
            if (property == null || property.PropertyType != typeof(Brush)) return;
            if (!originals.ContainsKey(element)) originals[element] = new Background {
                Element = element, Property = property, Local = element.ReadLocalValue(property),
                Effective = element.GetValue(property), Binding = BindingOperations.GetBindingBase(element, property)
            };
            element.SetCurrentValue(property, Brushes.Transparent);
        }

        public void Dispose()
        {
            Root.SizeChanged -= OnSizeChanged;
            Root.Children.Remove(browser);
            foreach (var saved in originals.Values) Restore(saved);
            originals.Clear();
            if (view.Background == Brushes.Transparent && textBackground != null) view.Background = textBackground;
        }

        private static void Restore(Background saved)
        {
            if (saved.Binding != null) BindingOperations.SetBinding(saved.Element, saved.Property, saved.Binding);
            else if (saved.Local == DependencyProperty.UnsetValue) saved.Element.ClearValue(saved.Property);
            else if (saved.Local == null || saved.Local is Brush) saved.Element.SetValue(saved.Property, saved.Local);
            else saved.Element.SetCurrentValue(saved.Property, saved.Effective); // Preserve dynamic-resource expressions.
        }
    }
}
#endif

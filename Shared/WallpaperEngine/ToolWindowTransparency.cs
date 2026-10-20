#if VS_LATEST
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Editor;

namespace ClaudiaIDE.WallpaperEngine
{
    // Only background brushes change. Text, selection adornments, input controls
    // and the shell's command bars retain their normal rendering and hit testing.
    internal sealed class ToolWindowTransparency : IDisposable
    {
        private sealed class Background
        {
            public DependencyObject Element;
            public DependencyProperty Property;
            public object Local;
            public BindingBase Binding;
        }

        private readonly Panel root;
        private readonly FrameworkElement wallpaper;
        private readonly Dictionary<DependencyObject, Background> originals = new Dictionary<DependencyObject, Background>();
        private readonly Dictionary<IWpfTextView, Brush> editors = new Dictionary<IWpfTextView, Brush>();

        public ToolWindowTransparency(Panel root, FrameworkElement wallpaper)
        {
            this.root = root;
            this.wallpaper = wallpaper;
        }

        public void Refresh()
        {
            var detached = new List<Background>();
            foreach (var saved in originals.Values)
                if (!(saved.Element is Visual visual) || !visual.IsDescendantOf(root)) detached.Add(saved);
            foreach (var saved in detached) { Restore(saved); originals.Remove(saved.Element); }
            var closed = new List<IWpfTextView>();
            foreach (var view in editors.Keys)
                if (view.IsClosed || !view.VisualElement.IsDescendantOf(root)) closed.Add(view);
            foreach (var view in closed) RestoreEditor(view);
            Walk(root);
        }

        public void ClearEditor(IWpfTextView view)
        {
            if (view.IsClosed || view.Background == Brushes.Transparent) return;
            editors[view] = view.Background;
            view.Background = Brushes.Transparent;
        }

        public void RestoreEditor(IWpfTextView view)
        {
            if (!editors.TryGetValue(view, out var brush)) return;
            if (!view.IsClosed && view.Background == Brushes.Transparent) view.Background = brush;
            editors.Remove(view);
        }

        private void Walk(DependencyObject item)
        {
            if (item == wallpaper || item is TextBlock || item is ButtonBase || item is TextBoxBase
                || item is ComboBox || item is MenuBase || item is MenuItem || item is ToolBar
                || item is StatusBar || item is TabItem || item is Thumb) return;
            if (item is TreeViewItem tree && (tree.IsSelected || tree.IsMouseOver))
            {
                RestoreUnder(tree);
                // A selected expanded node also owns its child rows. Keep the
                // header's highlight, but continue clearing ordinary children.
                WalkNestedItems(tree);
                return;
            }
            if ((item is ListBoxItem row && (row.IsSelected || row.IsMouseOver))
                || (item is DataGridRow gridRow && (gridRow.IsSelected || gridRow.IsMouseOver))
                || (item is DataGridCell cell && (cell.IsSelected || cell.IsEditing || cell.IsMouseOver)))
            {
                RestoreUnder((Visual)item);
                return;
            }
            if (item != root)
            {
                var field = item.GetType().GetField("BackgroundProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                var property = field?.GetValue(null) as DependencyProperty;
                // Null is already visually transparent and has no hit surface.
                // Painting it Transparent would make editor adornment canvases
                // intercept clicks that must reach the text view below them.
                if (property?.PropertyType == typeof(Brush) && item.GetValue(property) is Brush brush && brush != Brushes.Transparent)
                {
                    if (!originals.ContainsKey(item)) originals[item] = new Background {
                        Element = item, Property = property, Local = item.ReadLocalValue(property),
                        Binding = BindingOperations.GetBindingBase(item, property)
                    };
                    item.SetCurrentValue(property, Brushes.Transparent);
                }
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) Walk(VisualTreeHelper.GetChild(item, i));
        }

        private void RestoreUnder(Visual row)
        {
            var savedRows = new List<Background>();
            foreach (var saved in originals.Values)
                if (saved.Element is Visual visual && (visual == row || visual.IsDescendantOf(row))) savedRows.Add(saved);
            foreach (var saved in savedRows) { Restore(saved); originals.Remove(saved.Element); }
        }

        private void WalkNestedItems(DependencyObject item)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
            {
                var child = VisualTreeHelper.GetChild(item, i);
                if (child is ItemsPresenter || child is TreeViewItem) Walk(child);
                else WalkNestedItems(child);
            }
        }

        private static void Restore(Background saved)
        {
            if (saved.Binding != null) BindingOperations.SetBinding(saved.Element, saved.Property, saved.Binding);
            else if (saved.Local == DependencyProperty.UnsetValue) saved.Element.ClearValue(saved.Property);
            else if (saved.Local == null || saved.Local is Brush) saved.Element.SetValue(saved.Property, saved.Local);
            // SetCurrentValue preserved the resource expression. Re-evaluate it
            // against today's theme, rather than restoring a cached theme color.
            else saved.Element.SetValue(saved.Property, saved.Local);
        }

        public void Dispose()
        {
            foreach (var saved in originals.Values) Restore(saved);
            originals.Clear();
            foreach (var view in new List<IWpfTextView>(editors.Keys)) RestoreEditor(view);
        }
    }
}
#endif

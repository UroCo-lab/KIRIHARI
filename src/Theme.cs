using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Kirihari
{
    internal static class Theme
    {
        static readonly Dictionary<string, SolidColorBrush> Palette = new Dictionary<string, SolidColorBrush>();
        public static readonly Brush Ink = Brush("#172C2A"), Muted = Brush("#536963"), Edge = Brush("#D9E3DF"),
            Paper = Brush("#FFFFFF"), Background = Brush("#EDF2EF"), Soft = Brush("#F5F8F6"),
            Teal = Brush("#0A6251"), Mint = Brush("#DCF2E8"), Coral = Brush("#B03D26");
        public static readonly FontFamily Font = new FontFamily("Segoe UI, Yu Gothic UI, Meiryo");
        public static Brush Brush(string color)
        {
            if (string.IsNullOrEmpty(color)) return Brushes.Transparent;
            SolidColorBrush brush;
            if (Palette.TryGetValue(color, out brush)) return brush;
            try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
            catch (FormatException) { return Brushes.Transparent; }
            brush.Freeze();
            if (Palette.Count < 256) Palette[color] = brush;
            return brush;
        }
        public static TextBlock Text(string value, double size = 13, Brush foreground = null, bool bold = false)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = foreground ?? Ink,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        }
        public static TextBlock Wrap(string value, double size = 13, Brush foreground = null)
        {
            var t = Text(value, size, foreground);
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }
        public static Button Button(string value, Action click, bool primary = false)
        {
            var b = new Button { Content = value, MinHeight = 31, Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(9, 4, 9, 4), Background = primary ? Teal : Paper,
                Foreground = primary ? Paper : Ink, BorderBrush = primary ? Teal : Edge,
                BorderThickness = new Thickness(1), FontSize = 12, FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, Template = ButtonTemplate() };
            b.Click += delegate { click(); };
            return b;
        }
        static ControlTemplate template;
        static ControlTemplate ButtonTemplate()
        {
            if (template != null) return template;
            var border = new FrameworkElementFactory(typeof(Border), "buttonBorder");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            foreach (var pair in new[] {
                Tuple.Create(Border.BackgroundProperty, Control.BackgroundProperty),
                Tuple.Create(Border.BorderBrushProperty, Control.BorderBrushProperty),
                Tuple.Create(Border.BorderThicknessProperty, Control.BorderThicknessProperty),
                Tuple.Create(Border.PaddingProperty, Control.PaddingProperty) })
                border.SetBinding(pair.Item1, new Binding { RelativeSource = RelativeSource.TemplatedParent, Path = new PropertyPath(pair.Item2) });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(FrameworkElement.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, false);
            border.AppendChild(presenter);
            template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#36A589"), "buttonBorder"));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, .72, "buttonBorder"));
            template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45, "buttonBorder"));
            template.Triggers.Add(disabled);
            return template;
        }
        public static Border Box(UIElement child, Brush background, Thickness padding, double radius = 8)
        {
            return new Border { Child = child, Background = background, Padding = padding, CornerRadius = new CornerRadius(radius),
                BorderBrush = Edge, BorderThickness = new Thickness(1) };
        }
        public static StackPanel Row(params UIElement[] children)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var child in children) row.Children.Add(child);
            return row;
        }
        public static void Add(Grid grid, UIElement item, int row, int column)
        {
            Grid.SetRow(item, row);
            Grid.SetColumn(item, column);
            grid.Children.Add(item);
        }
        public static TextBox Input(object value, double width)
        {
            return new TextBox { Text = Convert.ToString(value, CultureInfo.CurrentCulture), Width = width, MinHeight = 27,
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 1, 4, 1),
                BorderBrush = Edge, Margin = new Thickness(0, 2, 5, 2) };
        }
        public static ComboBox Options(IEnumerable<string> values, string selected, double width)
        {
            var box = new ComboBox { Width = width, MinHeight = 27, Margin = new Thickness(0, 2, 5, 2) };
            foreach (string value in values) box.Items.Add(value);
            box.SelectedItem = selected;
            return box;
        }
        public static string Clock(double seconds, bool precise = false)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return (t.TotalHours >= 1 ? ((long)t.TotalHours).ToString("00") + ":" : "") + t.Minutes.ToString("00") + ":" +
                t.Seconds.ToString("00") + (precise ? "." + (t.Milliseconds / 10).ToString("00") : "");
        }
        public static void DrawText(DrawingContext dc, string value, Point point, double size, Brush brush,
            bool bold = false, double maxWidth = 2000, double maxHeight = 2000, TextAlignment alignment = TextAlignment.Left)
        {
            if (maxWidth < 2 || maxHeight < size || string.IsNullOrEmpty(value)) return;
            var ft = new FormattedText(value, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight,
                new Typeface(Font, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                size, brush, Application.Current.MainWindow == null ? 1 : VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip) { MaxTextWidth = maxWidth, MaxTextHeight = maxHeight, Trimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = alignment };
            dc.DrawText(ft, point);
        }
    }
}

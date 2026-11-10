using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Kirihari
{
    internal sealed class Sheet
    {
        readonly Window window;
        readonly StackPanel body = new StackPanel(), buttons = Theme.Row();
        readonly TextBlock error;
        public Sheet(Editor owner, string title, string description)
        {
            owner.SuspendForDialog();
            window = new Window { Owner = owner, Title = "KIRIHARI · " + title, Width = 475, MinWidth = 430,
                MaxHeight = Math.Min(SystemParameters.WorkArea.Height - 26, 710), SizeToContent = SizeToContent.Height,
                Background = Theme.Paper, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = Theme.Font, FontSize = 13 };
            var layout = new DockPanel { Margin = new Thickness(20, 15, 20, 14) };
            window.Content = layout;
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(Theme.Text(title, 20, Theme.Ink, true));
            head.Children.Add(Theme.Wrap(description, 12, Theme.Muted));
            DockPanel.SetDock(head, Dock.Top); layout.Children.Add(head);
            var tail = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
            error = Theme.Wrap("", 12, Theme.Coral); tail.Children.Add(error);
            buttons.HorizontalAlignment = HorizontalAlignment.Right; tail.Children.Add(buttons);
            DockPanel.SetDock(tail, Dock.Bottom); layout.Children.Add(tail);
            layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        public void Add(string label, FrameworkElement value)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(139) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            Theme.Add(row, Theme.Wrap(label, 12, Theme.Muted), 0, 0);
            value.HorizontalAlignment = HorizontalAlignment.Left;
            Theme.Add(row, value, 0, 1);
            body.Children.Add(row);
        }
        public TextBox Field(string label, object value, double width = 124)
        {
            var field = Theme.Input(value, width);
            Add(label, field); return field;
        }
        public ComboBox Choice(string label, IEnumerable<string> values, string value, double width = 146)
        {
            var field = Theme.Options(values, value, width);
            Add(label, field); return field;
        }
        public Swatch Color(string label, string value, bool allowClear)
        {
            var color = new Swatch(value, allowClear);
            Add(label, color.Panel); return color;
        }
        public void Open(string accept, Action apply, string cancel = "キャンセル")
        {
            var close = Theme.Button(cancel, () => window.Close()); close.IsCancel = true; buttons.Children.Add(close);
            if (apply != null)
            {
                var applyButton = Theme.Button(accept, () =>
                {
                    try { apply(); window.DialogResult = true; }
                    catch (Exception e)
                    {
                        if (!Editor.UserError(e)) throw;
                        error.Text = e.Message;
                    }
                }, true);
                applyButton.IsDefault = true; buttons.Children.Add(applyButton);
            }
            window.ShowDialog();
        }
        public sealed class Swatch
        {
            readonly Button button;
            public readonly StackPanel Panel;
            public string Value { get; private set; }
            public Swatch(string value, bool allowClear)
            {
                Value = value ?? "#000000";
                button = Theme.Button("", Choose);
                var palette = Theme.Button("薄い背景", () => { Value = "#C8112423"; Update(); });
                var clear = Theme.Button("透明", () => { Value = "#00000000"; Update(); });
                Panel = allowClear ? Theme.Row(button, palette, clear) : Theme.Row(button);
                Update();
            }
            void Update()
            {
                button.Content = "色を選ぶ " + Value;
                button.BorderBrush = Theme.Brush(Value);
            }
            void Choose()
            {
                var color = Theme.Brush(Value) as SolidColorBrush;
                using (var dialog = new Forms.ColorDialog { FullOpen = true })
                {
                    if (color != null) dialog.Color = System.Drawing.Color.FromArgb(color.Color.R, color.Color.G, color.Color.B);
                    if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                    Value = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2");
                    Update();
                }
            }
        }
    }
}

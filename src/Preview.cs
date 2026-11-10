using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kirihari
{
    internal sealed class Preview : FrameworkElement
    {
        readonly Editor editor;
        readonly Dictionary<string, BitmapSource> images = new Dictionary<string, BitmapSource>();
        readonly List<ObjectHit> hits = new List<ObjectHit>();
        Rect video;
        Caption drag;
        Point down;
        double initialX, initialY, initialWidth, initialHeight, initialAngle, pointerAngle;
        string operation, snapshot;
        static readonly Brush Dark = Theme.Brush("#0B1C20"), Line = Theme.Brush("#5DD0B1"),
            Dim = Theme.Brush("#A1C2C3"), ToolFill = Theme.Brush("#345458");
        static readonly Brush SampleBackground = Gradient("#24484A", "#102629", "#0C1B20");
        static readonly Brush SampleDisc = Gradient("#98AC91", "#43675C", "#284347");
        static readonly Brush SampleTool = Gradient("#C0D1BF", "#6E9E91", "#305355");
        static readonly Pen GridLine = new Pen(Theme.Brush("#2B494A"), .65);
        public Preview(Editor editor)
        {
            this.editor = editor;
            ClipToBounds = true;
            Cursor = Cursors.Arrow;
        }

        static Brush Gradient(params string[] colors)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(.1, 0), EndPoint = new Point(.85, 1) };
            for (int i = 0; i < colors.Length; i++)
                brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colors[i]), i / (double)(colors.Length - 1)));
            brush.Freeze();
            return brush;
        }
        protected override void OnRender(DrawingContext dc)
        {
            hits.Clear();
            var p = editor.P;
            if (ActualWidth < 5 || ActualHeight < 5) return;
            var outer = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(Dark, null, outer);
            double scale = Math.Min(ActualWidth / p.Source.Width, ActualHeight / p.Source.Height);
            double width = p.Source.Width * scale, height = p.Source.Height * scale;
            video = new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
            dc.PushClip(new RectangleGeometry(video));
            var part = editor.CurrentPart;
            bool blank = part != null && part.Kind == "blank";
            if (blank)
            {
                var effect = p.Effects.First(e => e.Id == part.Target);
                dc.DrawRectangle(Theme.Brush(effect.Color ?? "#000000"), null, video);
            }
            else if (p.Source.Demo) Sample(dc, editor.CursorFrame / p.Source.Fps);
            else if (editor.MediaReady) dc.DrawVideo(editor.Media, video);
            else
            {
                dc.DrawRectangle(SampleBackground, null, video);
                Theme.DrawText(dc, "映像プレビューを利用できません", new Point(video.Left + 20, video.Top + 26), 16, Dim);
                Theme.DrawText(dc, "この状態でも編集位置とテロップを試せます", new Point(video.Left + 20, video.Top + 54), 12, Dim);
            }

            if (editor.CutMode)
            {
                foreach (var c in p.Captions.OrderBy(c => c.Track).ThenBy(c => c.Layer))
                {
                    if (editor.Map.Active(c, part, editor.OutputTime)) DrawObject(dc, c);
                }
            }
            dc.Pop();
            if (part != null && part.Kind != "video")
            {
                string label = (part.Kind == "freeze" ? "停止" : "ブランク") + "  残り " +
                    Math.Max(0, part.To - editor.OutputTime).ToString("0.0") + " 秒";
                var tag = new Rect(video.Right - 157, video.Top + 10, 146, 25);
                dc.DrawRoundedRectangle(Theme.Brush("#EA0F2829"), null, tag, 4, 4);
                Theme.DrawText(dc, label, new Point(tag.Left + 9, tag.Top + 4), 12, Theme.Paper, true);
            }
        }

        void Sample(DrawingContext dc, double time)
        {
            dc.PushTransform(new TranslateTransform(video.X, video.Y));
            dc.PushTransform(new ScaleTransform(video.Width / 960, video.Height / 540));
            dc.DrawRectangle(SampleBackground, null, new Rect(0, 0, 960, 540));
            for (int x = 24; x < 960; x += 32)
                dc.DrawLine(GridLine, new Point(x, 0), new Point(x, 540));
            for (int y = 16; y < 540; y += 32)
                dc.DrawLine(GridLine, new Point(0, y), new Point(960, y));

            dc.DrawEllipse(Theme.Brush("#70101C20"), null, new Point(439, 323), 181, 168);
            var center = new Point(420, 289);
            dc.DrawEllipse(ToolFill, new Pen(Dim, 2), center, 165, 165);
            dc.DrawEllipse(SampleDisc, new Pen(Theme.Brush("#182C2B"), 5), center, 146, 146);
            dc.DrawEllipse(Theme.Brush("#223C3C"), new Pen(Dim, 1), center, 123, 123);
            dc.DrawEllipse(Theme.Brush("#162B30"), new Pen(Theme.Brush("#4E7B74"), 1), center, 105, 105);
            dc.PushTransform(new RotateTransform(time * 8, center.X, center.Y));
            for (int i = 0; i < 32; i++)
            {
                double a = i * Math.PI / 16;
                var from = new Point(center.X + 127 * Math.Cos(a), center.Y + 127 * Math.Sin(a));
                var to = new Point(center.X + 141 * Math.Cos(a), center.Y + 141 * Math.Sin(a));
                dc.DrawLine(new Pen(i % 4 == 0 ? Line : Theme.Brush("#BBC7A3"), i % 4 == 0 ? 2.8 : 1), from, to);
            }
            dc.DrawEllipse(SampleDisc, new Pen(Line, 1.2), center, 74, 74);
            dc.DrawRectangle(Theme.Brush("#B4C8A4"), null, new Rect(center.X - 23, center.Y - 23, 46, 46));
            dc.DrawRectangle(Theme.Brush("#344A43"), new Pen(Dim, 1), new Rect(center.X - 15, center.Y - 15, 30, 30));
            dc.Pop();
            var tip = new Point(510 + Math.Sin(time * .72) * 60, 247 + Math.Sin(time * .52) * 34);
            dc.PushTransform(new TranslateTransform(tip.X, tip.Y));
            dc.PushTransform(new RotateTransform(-23));
            dc.DrawRoundedRectangle(Theme.Brush("#6B0B151A"), null, new Rect(12, -13, 500, 69), 20, 20);
            dc.DrawRoundedRectangle(SampleTool, new Pen(Dim, 1), new Rect(74, -29, 450, 58), 16, 16);
            dc.DrawRoundedRectangle(SampleTool, new Pen(Dim, 1), new Rect(21, -16, 90, 32), 9, 9);
            dc.DrawRoundedRectangle(Dim, null, new Rect(-20, -4, 51, 8), 3, 3);
            dc.DrawRectangle(Theme.Brush("#284748"), null, new Rect(170, -29, 14, 58));
            dc.DrawRectangle(Theme.Brush("#284748"), null, new Rect(193, -29, 7, 58));
            dc.DrawRectangle(Line, null, new Rect(237, -7, 25, 14));
            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.Pop();

            double u = Math.Max(.65, Math.Min(1.15, video.Width / 670));
            var point = new Point(video.X + 16, video.Y + 14);
            dc.DrawRectangle(Line, null, new Rect(point.X, point.Y + 5, 4, 30 * u));
            Theme.DrawText(dc, "光学ステージ", new Point(point.X + 13, point.Y), 19 * u, Theme.Paper, true);
            Theme.DrawText(dc, "CALIBRATION  /  架空サンプル", new Point(point.X + 13, point.Y + 23 * u), 10 * u, Dim, true);
            Theme.DrawText(dc, "SAMPLE 01   •   " + Theme.Clock(time, true),
                new Point(video.X + 18, video.Bottom - 26), 10 * u, Dim, true);
        }

        sealed class ObjectHit { public Caption Caption; public Rect Box; public RotateTransform Rotate; }
        void DrawObject(DrawingContext dc, Caption c)
        {
            double x = video.X + c.X * video.Width, y = video.Y + c.Y * video.Height;
            var r = new Rect(x, y, c.Width * video.Width, c.Height * video.Height);
            var rotate = new RotateTransform(c.Angle, r.X + r.Width / 2, r.Y + r.Height / 2);
            hits.Add(new ObjectHit { Caption = c, Box = r, Rotate = rotate });
            dc.PushTransform(rotate);
            dc.PushOpacity(c.Opacity);
            var color = Theme.Brush(c.Color);
            var pen = new Pen(color, Math.Max(2, video.Width / 240));
            var background = Theme.Brush(c.Background);
            switch (c.Kind)
            {
                case "text":
                    dc.DrawRoundedRectangle(background, null, r, 4, 4);
                    double size = c.FontSize * video.Width / 960;
                    Theme.DrawText(dc, c.Text, new Point(r.X + 6, r.Y + 3), size, color, true, r.Width - 12, r.Height - 3);
                    break;
                case "rect": dc.DrawRectangle(background, pen, r); break;
                case "ellipse": dc.DrawEllipse(background, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2); break;
                case "line":
                case "arrow":
                    var a = new Point(r.X, r.Y + r.Height * .75);
                    var b = new Point(r.Right, r.Y + r.Height * .25);
                    dc.DrawLine(pen, a, b);
                    if (c.Kind == "arrow")
                    {
                        var direction = b - a;
                        direction.Normalize();
                        var normal = new Vector(direction.Y, -direction.X);
                        var arrow = new StreamGeometry();
                        using (var g = arrow.Open())
                        {
                            g.BeginFigure(b, true, true);
                            g.LineTo(b - direction * (pen.Thickness * 4) + normal * (pen.Thickness * 2.2), true, false);
                            g.LineTo(b - direction * (pen.Thickness * 4) - normal * (pen.Thickness * 2.2), true, false);
                        }
                        dc.DrawGeometry(color, null, arrow);
                    }
                    break;
                case "image":
                    var image = Image(c.Image);
                    if (image != null)
                    {
                        double scale = Math.Min(r.Width / image.PixelWidth, r.Height / image.PixelHeight);
                        double w = image.PixelWidth * scale, h = image.PixelHeight * scale;
                        dc.DrawImage(image, new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h));
                    }
                    else
                    {
                        dc.DrawRectangle(background, pen, r);
                        Theme.DrawText(dc, "画像を開けません", new Point(r.X + 5, r.Y + 5), 12, color, true, r.Width - 8);
                    }
                    break;
            }
            dc.Pop();
            if (editor.Selected == c.Id && !editor.Playing)
            {
                var selection = new Pen(Theme.Brush("#9AFFF0"), 1) { DashStyle = DashStyles.Dash };
                dc.DrawRectangle(null, selection, r);
                dc.DrawLine(selection, new Point(r.X + r.Width / 2, r.Top), RotateHandle(r));
                foreach (var point in new[] { new Point(r.Right, r.Bottom), RotateHandle(r) })
                    dc.DrawEllipse(Theme.Paper, new Pen(Theme.Teal, 1.4), point, 4, 4);
            }
            dc.Pop();
        }

        BitmapSource Image(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            BitmapSource bitmap;
            if (images.TryGetValue(path, out bitmap)) return bitmap;
            try
            {
                using (var stream = File.OpenRead(Files.Local(path, AppDomain.CurrentDomain.BaseDirectory)))
                {
                    var b = new BitmapImage();
                    b.BeginInit();
                    b.StreamSource = stream;
                    b.DecodePixelWidth = 1280;
                    b.CacheOption = BitmapCacheOption.OnLoad;
                    b.EndInit();
                    b.Freeze();
                    bitmap = b;
                }
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is ArgumentException || e is UnauthorizedAccessException || e is NotSupportedException ||
                    e is System.Runtime.InteropServices.COMException)) throw;
            }
            if (images.Count >= 10) images.Clear();
            images[path] = bitmap;
            return bitmap;
        }

        static Point RotateHandle(Rect r) { return new Point(r.X + r.Width / 2, r.Top - 15); }
        static double Angle(Point center, Point point)
        {
            return Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI;
        }
        Point center;
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (!Begin(e.GetPosition(this))) return;
            CaptureMouse();
            e.Handled = true;
        }
        bool Begin(Point point)
        {
            if (editor.Playing) return false;
            down = point;
            var hit = hits.LastOrDefault(h =>
            {
                Point p = h.Rotate.Inverse.Transform(down);
                return h.Box.Contains(p) || (h.Caption.Id == editor.Selected &&
                    ((p - RotateHandle(h.Box)).Length <= 8 || (p - new Point(h.Box.Right, h.Box.Bottom)).Length <= 8));
            });
            if (hit == null) return false;
            drag = hit.Caption;
            snapshot = Files.Json(editor.P);
            Point local = hit.Rotate.Inverse.Transform(down);
            operation = (local - RotateHandle(hit.Box)).Length <= 8 ? "rotate" :
                (local - new Point(hit.Box.Right, hit.Box.Bottom)).Length <= 8 ? "size" : "move";
            center = new Point(hit.Box.X + hit.Box.Width / 2, hit.Box.Y + hit.Box.Height / 2);
            pointerAngle = Angle(center, down);
            initialX = drag.X; initialY = drag.Y; initialWidth = drag.Width; initialHeight = drag.Height; initialAngle = drag.Angle;
            editor.Select(drag.Id, false);
            return true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag != null && IsMouseCaptured) DragTo(e.GetPosition(this));
        }
        void DragTo(Point point)
        {
            var delta = point - down;
            if (operation == "move")
            {
                drag.X = Clamp(initialX + delta.X / video.Width, 0, Math.Max(0, 1 - drag.Width));
                drag.Y = Clamp(initialY + delta.Y / video.Height, 0, Math.Max(0, 1 - drag.Height));
            }
            else if (operation == "size")
            {
                var local = new RotateTransform(-initialAngle).Transform(new Point(delta.X, delta.Y));
                drag.Width = Clamp(initialWidth + local.X / video.Width, .035, 1 - drag.X);
                drag.Height = Clamp(initialHeight + local.Y / video.Height, .035, 1 - drag.Y);
            }
            else drag.Angle = (initialAngle + Angle(center, point) - pointerAngle) % 360;
            InvalidateVisual();
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { Finish(); ReleaseMouseCapture(); }
        protected override void OnLostMouseCapture(MouseEventArgs e) { Finish(); }
        void Finish()
        {
            if (drag == null) return;
            drag = null;
            editor.Commit(snapshot);
        }
        internal void QaTransform(Caption c, string kind)
        {
            var hit = hits.First(h => h.Caption == c);
            var r = hit.Box;
            Point start = kind == "size" ? r.BottomRight : kind == "rotate" ? RotateHandle(r) :
                new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            start = hit.Rotate.Transform(start);
            if (!Begin(start)) throw new InvalidOperationException("Caption handle could not be selected.");
            DragTo(start + new Vector(18, 9));
            Finish();
        }
        static double Clamp(double n, double min, double max) { return Math.Max(min, Math.Min(max, n)); }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Kirihari
{
    internal sealed class Timeline : FrameworkElement
    {
        public const int Ruler = 25, Row = 25, Pad = 12;
        readonly Editor editor;
        readonly List<Hit> hits = new List<Hit>();
        string drag;
        int anchor, fixedEdge;
        Point pressed;
        string snapshot;
        bool moved;
        public int? RangeStart, RangeEnd;
        public int[] CaptionTracks
        {
            get
            {
                return editor.HideEmpty
                    ? editor.P.Captions.Select(c => c.Track).Distinct().OrderBy(n => n).DefaultIfEmpty(1).ToArray()
                    : Enumerable.Range(1, editor.P.Tracks).ToArray();
            }
        }
        public int Rows { get { return 3 + CaptionTracks.Length; } }
        public Timeline(Editor editor)
        {
            this.editor = editor;
            Cursor = Cursors.Cross;
            ClipToBounds = true;
            Focusable = true;
            ToolTip = "カット段の空いているところをドラッグすると、その範囲を残せます。";
        }

        public double X(double frame) { return Pad + frame / editor.P.Source.Frames * Math.Max(1, ActualWidth - 2 * Pad); }
        int Frame(double x)
        {
            int step = editor.Fine ? 1 : Math.Max(1, (int)Math.Round(editor.P.Source.Fps));
            var frame = (x - Pad) * editor.P.Source.Frames / Math.Max(1, ActualWidth - 2 * Pad);
            if (x >= ActualWidth - Pad) return editor.P.Source.Frames;
            return Math.Max(0, Math.Min(editor.P.Source.Frames, (int)Math.Round(frame / step) * step));
        }
        Rect Bar(int start, int end, int track, int padding = 4)
        {
            return new Rect(X(start), Ruler + track * Row + padding, Math.Max(2, X(end) - X(start)), Row - 2 * padding);
        }
        sealed class Hit { public Rect Box; public string Kind, Id; }
        void Region(Rect box, string kind, string id)
        {
            hits.Add(new Hit { Box = box, Kind = kind, Id = id });
        }
        static void Box(DrawingContext dc, Rect r, Brush fill, Brush edge = null, double radius = 4)
        {
            dc.DrawRoundedRectangle(fill, edge == null ? null : new Pen(edge, 1), r, radius, radius);
        }
        static readonly Brush Original = Theme.Brush("#DDE7E7"), Green = Theme.Brush("#187866"),
            Yellow = Theme.Brush("#F8D88A"), Violet = Theme.Brush("#E9DEF8"), Slate = Theme.Brush("#C6D5D3"),
            Playhead = Theme.Brush("#DA5D40"), CaptionColor = Theme.Brush("#DBDDF8");

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            hits.Clear();
            if (ActualWidth <= Pad * 2) return;
            var p = editor.P;
            double bottom = Ruler + Rows * Row;
            dc.DrawRectangle(Theme.Soft, null, new Rect(0, 0, ActualWidth, ActualHeight));
            for (int i = 0; i < Rows; i++)
            {
                double y = Ruler + i * Row;
                dc.DrawRectangle(i % 2 == 0 ? Theme.Paper : Theme.Soft, null, new Rect(0, y, ActualWidth, Row));
                dc.DrawLine(new Pen(Theme.Edge, 1), new Point(0, y), new Point(ActualWidth, y));
            }
            var tickChoices = new[] { .1, .2, .5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600, 86400 };
            var tick = tickChoices.FirstOrDefault(s => s * (ActualWidth - Pad * 2) / p.Source.Duration >= 64);
            if (tick == 0) tick = 86400;
            for (double s = 0; s <= p.Source.Duration + .00001; s += tick)
            {
                double x = X(p.Source.Frame(s));
                dc.DrawLine(new Pen(Theme.Edge, 1), new Point(x, Ruler - 6), new Point(x, bottom));
                Theme.DrawText(dc, Theme.Clock(s, tick < 1), new Point(Math.Min(x + 4, ActualWidth - (tick < 1 ? 50 : 37)), 5), 10, Theme.Muted);
            }
            var original = Bar(0, p.Source.Frames, 0);
            Box(dc, original, Original);
            Theme.DrawText(dc, p.Source.Demo ? "元の映像 • 架空サンプル" : "元の映像", new Point(original.X + 8, original.Y + 2),
                10, Theme.Ink, true, original.Width - 12);
            var cut = Bar(0, p.Source.Frames, 1);
            Box(dc, cut, Theme.Brush("#E9EFEC"));
            Theme.DrawText(dc, "削除", new Point(cut.X + 7, cut.Y + 2), 10, Theme.Muted);
            foreach (var c in p.Clips.OrderBy(c => c.Start))
            {
                bool selected = editor.Selected == c.Id;
                var rect = Bar(c.Start, c.End, 1);
                Box(dc, rect, selected ? Theme.Teal : Green, selected ? Theme.Brush("#052D25") : null);
                if (!p.Effects.Any(e => e.ClipId == c.Id && e.Kind == "speed" && e.Start <= c.Start && e.End > c.Start))
                    Theme.DrawText(dc, "残す", new Point(rect.X + 11, rect.Y + 2), 10, Theme.Paper, true, rect.Width - 20, 15);
                Region(rect, "clip", c.Id);
                if (!selected) Handles(dc, rect, "clip", c.Id, Theme.Paper);
            }
            var captionTracks = CaptionTracks;
            foreach (var c in p.Captions.OrderBy(c => c.Layer))
            {
                var clip = p.Clips.FirstOrDefault(x => x.Id == c.Target);
                var effect = p.Effects.FirstOrDefault(x => x.Id == c.Target);
                int start = clip == null ? (effect == null ? 0 : effect.Start) : Math.Max(0, Math.Min(p.Source.Frames, clip.Origin + c.Start));
                int end = clip == null ? start : Math.Max(start, Math.Min(p.Source.Frames, clip.Origin + c.End));
                var r = Bar(start, end, Array.IndexOf(captionTracks, c.Track) + 2);
                if (clip == null) r.Width = Math.Min(78, Math.Max(15, ActualWidth - r.X));
                bool warning = editor.Map.Warning(c) != null;
                Box(dc, r, warning ? Yellow : CaptionColor, editor.Selected == c.Id ? Theme.Brush("#6655A0") : null);
                Theme.DrawText(dc, (warning ? "⚠ " : "") + (c.Kind == "text" ? c.Text : Editor.CaptionLabel(c.Kind)),
                    new Point(r.X + 8, r.Y + 2), 10, Theme.Ink, false, r.Width - 14, 15);
                Region(r, "caption", c.Id);
                if (clip != null) Handles(dc, r, "caption", c.Id, Theme.Brush("#6F63A1"));
            }
            var sound = Bar(0, p.Source.Frames, Rows - 1);
            Box(dc, sound, p.Sound ? Slate : Theme.Brush("#EBEFEE"));
            Theme.DrawText(dc, p.Sound ? (p.Source.Audio ? "音声あり" : "元動画に音声なし") : "音声なし",
                new Point(sound.X + 8, sound.Y + 2), 10, Theme.Muted);
            foreach (var e in p.Effects.OrderBy(e => e.Kind == "speed" ? 0 : 1).ThenBy(e => editor.Selected == e.Id ? 1 : 0))
            {
                var rect = e.Kind == "speed" ? Bar(e.Start, e.End, 1) : Bar(e.Start, e.Start, 1, 0);
                if (e.Kind != "speed")
                {
                    rect.Width = 88;
                    rect.X = Math.Max(0, Math.Min(ActualWidth - rect.Width, rect.X - 5));
                    rect.Height = 19;
                    dc.DrawLine(new Pen(Theme.Ink, 1), new Point(X(e.Start), rect.Bottom - 1), new Point(X(e.Start), rect.Bottom + 7));
                }
                bool valid = editor.Map.EffectActive(e);
                Box(dc, rect, !valid ? Yellow : e.Kind == "blank" ? Yellow : Violet,
                    editor.Selected == e.Id ? Theme.Brush("#684D9C") : null);
                string label = e.Kind == "speed" ? "» 速度 ×" + e.Rate.ToString("0.##") :
                    (e.Kind == "blank" ? "▣ 空白 " : "Ⅱ 停止 ") + (e.Frames / p.Source.Fps).ToString("0.##") + "秒";
                Theme.DrawText(dc, (valid ? "" : "⚠ ") + label, new Point(rect.X + 7, rect.Y + 1), 10, Theme.Ink, true, rect.Width - 10, 15);
                Region(rect, e.Kind, e.Id);
                if (e.Kind == "speed" && editor.Selected == e.Id) Handles(dc, rect, "speed", e.Id, Theme.Brush("#684D9C"));
            }
            var selectedClip = p.Clips.FirstOrDefault(c => c.Id == editor.Selected);
            if (selectedClip != null)
                Handles(dc, Bar(selectedClip.Start, selectedClip.End, 1), "clip", selectedClip.Id, Theme.Paper);
            if (RangeStart.HasValue && RangeEnd.HasValue && RangeStart != RangeEnd)
            {
                var range = Bar(Math.Min(RangeStart.Value, RangeEnd.Value), Math.Max(RangeStart.Value, RangeEnd.Value), 1, 1);
                Box(dc, range, Theme.Brush("#3365AFED"), Theme.Brush("#3578A0"), 2);
            }
            double cursor = X(editor.CursorFrame);
            dc.DrawLine(new Pen(Playhead, 1.5), new Point(cursor, 20), new Point(cursor, bottom));
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(new Point(cursor - 5, 18), true, true);
                g.LineTo(new Point(cursor + 5, 18), true, false);
                g.LineTo(new Point(cursor, 26), true, false);
            }
            dc.DrawGeometry(Playhead, null, head);
        }

        void Handles(DrawingContext dc, Rect box, string kind, string id, Brush fill)
        {
            double width = Math.Min(7, box.Width / 2);
            if (width < 2) return;
            var left = new Rect(box.Left, box.Top, width, box.Height);
            var right = new Rect(box.Right - width, box.Top, width, box.Height);
            if (box.Width > 12)
            {
                dc.DrawLine(new Pen(fill, 1.4), new Point(left.X + 3, left.Y + 5), new Point(left.X + 3, left.Bottom - 5));
                dc.DrawLine(new Pen(fill, 1.4), new Point(right.Right - 3, right.Y + 5), new Point(right.Right - 3, right.Bottom - 5));
            }
            Region(left, kind + "L", id);
            Region(right, kind + "R", id);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            pressed = e.GetPosition(this);
            var hit = hits.LastOrDefault(h => h.Box.Contains(pressed));
            Begin(pressed, hit);
            CaptureMouse();
            e.Handled = true;
        }
        void Begin(Point point, Hit hit)
        {
            snapshot = Files.Json(editor.P);
            anchor = Frame(point.X);
            moved = false;
            RangeStart = RangeEnd = null;
            if (hit != null)
            {
                drag = hit.Kind;
                editor.Select(hit.Id, true);
                if (drag == "clipL" || drag == "clipR") editor.EdgeLeft = drag == "clipL";
                if (drag == "speedL" || drag == "speedR") editor.EdgeLeft = drag == "speedL";
                if (drag == "captionL" || drag == "captionR") editor.EdgeLeft = drag == "captionL";
                if (drag == "clip" || drag == "clipL" || drag == "clipR" || drag == "speedL" || drag == "speedR")
                    editor.SeekFrame(anchor);
            }
            else
            {
                editor.Select(null, false);
                drag = point.Y >= Ruler + Row && point.Y < Ruler + Row * 2 ? "range" : "seek";
                editor.SeekFrame(anchor);
                if (drag == "range")
                {
                    var p = editor.P;
                    fixedEdge = p.Clips.Where(c => c.Start >= anchor).Select(c => c.Start).DefaultIfEmpty(p.Source.Frames).Min();
                }
            }
            editor.Refresh();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!IsMouseCaptured || drag == null) return;
            DragTo(e.GetPosition(this));
        }
        void DragTo(Point point)
        {
            if (Math.Abs(point.X - pressed.X) < 3 && !moved) return;
            moved = true;
            int frame = Frame(point.X);
            if (drag == "range")
            {
                int left = editor.P.Clips.Where(c => c.End <= anchor).Select(c => c.End).DefaultIfEmpty(0).Max();
                RangeStart = anchor;
                RangeEnd = Math.Max(left, Math.Min(fixedEdge, frame));
                InvalidateVisual();
            }
            else if (drag == "seek") editor.SeekFrame(frame);
            else if (drag.Length > 1 && (drag.EndsWith("L") || drag.EndsWith("R")))
                editor.MoveBoundary(drag.EndsWith("L"), frame);
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (drag == null) return;
            DragTo(e.GetPosition(this));
            Finish();
            ReleaseMouseCapture();
            e.Handled = true;
        }
        void Finish()
        {
            if (drag == "range" && RangeStart.HasValue && RangeEnd.HasValue && RangeStart != RangeEnd)
            {
                var clip = editor.P.AddClip(Math.Min(RangeStart.Value, RangeEnd.Value), Math.Max(RangeStart.Value, RangeEnd.Value));
                editor.Select(clip.Id, false);
                editor.SeekFrame(clip.Start);
            }
            RangeStart = RangeEnd = null;
            drag = null;
            editor.Commit(snapshot);
        }
        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            if (drag == null) return;
            drag = null;
            RangeStart = RangeEnd = null;
            editor.Commit(snapshot);
        }
        internal void QaGesture(Point from, Point to)
        {
            pressed = from;
            Begin(from, hits.LastOrDefault(h => h.Box.Contains(from)));
            DragTo(to);
            Finish();
        }
    }
}

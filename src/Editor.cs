using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;


namespace Kirihari
{
    internal sealed partial class Editor : Window
    {
        internal Project P { get; private set; }
        internal TimeMap Map { get; private set; }
        internal string Selected;
        internal bool HideEmpty, Fine, CutMode, Playing, EdgeLeft = true;
        internal double CursorFrame { get; private set; }
        internal double OutputTime { get; private set; }
        internal Part CurrentPart { get { return CutMode && !inGap ? Map.At(OutputTime) : null; } }
        internal MediaPlayer Media = new MediaPlayer();
        internal bool MediaReady;
        string mediaNotice;
        bool inGap;
        string projectPath, savedJson;
        bool previewOnly, loop, compactLayout;
        int zoom = 1, lastRows = 4;
        double editorHeight = 206;
        double originalSeconds, loopFrom, loopTo;
        History history = new History();
        readonly Stopwatch clock = new Stopwatch();
        readonly DispatcherTimer playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        readonly DispatcherTimer autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        string autoJson, autoStatus = "";
        readonly Grid root = new Grid(), top = new Grid();
        readonly Border side;
        readonly StackPanel info = new StackPanel(), tracks = new StackPanel();
        readonly TextBlock title, meta, total, originalTimeLabel, cutTimeLabel, status, transportMode, cursorMode;
        readonly Button undo, redo, play, originalMode, cutMode, loopButton, fineButton, moreTracks, hideButton;
        readonly StackPanel context;
        readonly Timeline timeline;
        readonly Preview preview;
        readonly ScrollViewer timelineScroll;
        readonly RowDefinition editRow, topRow;
        readonly ColumnDefinition sidebarWidth;
        const string DefaultHint = "カット段の空いているところをドラッグすると、その範囲が残ります。";

        internal Editor(bool qa = false)
        {
            Title = "KIRIHARI  •  動画編集の試作";
            AppIcon.Apply(this);
            Width = 1380;
            Height = 850;
            MinWidth = 880;
            MinHeight = 510;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (!qa) WindowState = WindowState.Maximized;
            FontFamily = Theme.Font;
            FontSize = 13;
            Background = Theme.Background;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            P = Project.Demo(false);
            savedJson = Files.Json(P);
            Map = new TimeMap(P);
            Media.ScrubbingEnabled = true;
            playbackTimer.Tick += delegate { PlaybackTick(); };
            autoTimer.Tick += delegate { AutoSave(); };
            if (!qa) autoTimer.Start();

            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            topRow = new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 145 };
            root.RowDefinitions.Add(topRow);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            editRow = new RowDefinition { Height = new GridLength(editorHeight), MinHeight = 139 };
            root.RowDefinitions.Add(editRow);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            root.Background = Theme.Background;
            Content = root;

            var header = new Grid { Margin = new Thickness(15, 0, 12, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var logo = new Border { Background = Theme.Teal, CornerRadius = new CornerRadius(8),
                Width = 34, Height = 34, Child = Theme.Text("K", 19, Theme.Paper, true) };
            ((TextBlock)logo.Child).HorizontalAlignment = HorizontalAlignment.Center;
            var brand = new StackPanel { Margin = new Thickness(8, 0, 15, 0), VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(Theme.Text("KIRIHARI", 21, Theme.Ink, true));
            brand.Children.Add(Theme.Text("動画を切って、説明を貼る", 10, Theme.Muted));
            Theme.Add(header, Theme.Row(logo, brand), 0, 0);
            var heading = new StackPanel { Margin = new Thickness(13, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            title = Theme.Text("", 15, Theme.Ink, true); title.TextTrimming = TextTrimming.CharacterEllipsis;
            meta = Theme.Text("", 11, Theme.Muted); meta.TextTrimming = TextTrimming.CharacterEllipsis;
            heading.Children.Add(title); heading.Children.Add(meta);
            Theme.Add(header, new Border { Child = heading, BorderBrush = Theme.Edge, BorderThickness = new Thickness(1, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center }, 0, 1);
            undo = Theme.Button("元に戻す", Undo);
            redo = Theme.Button("やり直す", Redo);
            Button save = null;
            save = Theme.Button("保存 ▾", () => SaveMenu(save));
            Theme.Add(header, Theme.Row(undo, redo, Theme.Button("動画を選ぶ", OpenVideo),
                save, Theme.Button("書き出し", ShowExport, true)), 0, 2);
            Theme.Add(root, new Border { Background = Theme.Paper, BorderBrush = Theme.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = header }, 0, 0);

            top.Margin = new Thickness(12, 10, 12, 0);
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 425 });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            sidebarWidth = new ColumnDefinition { Width = new GridLength(294), MinWidth = 225, MaxWidth = 470 };
            top.ColumnDefinitions.Add(sidebarWidth);
            var previewArea = new Grid();
            previewArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            previewArea.RowDefinitions.Add(new RowDefinition());
            previewArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(45) });
            var previewHead = new DockPanel { Margin = new Thickness(9, 0, 8, 0), LastChildFill = true };
            var full = Theme.Button("全画面", Fullscreen);
            full.MinHeight = 24; full.Padding = new Thickness(7, 1, 7, 1);
            DockPanel.SetDock(full, Dock.Right); previewHead.Children.Add(full);
            transportMode = Theme.Text("", 11, Theme.Muted, true); transportMode.Margin = new Thickness(9, 0, 0, 0);
            previewHead.Children.Add(Theme.Row(Theme.Text("プレビュー", 13, Theme.Ink, true), transportMode));
            Theme.Add(previewArea, previewHead, 0, 0);
            preview = new Preview(this);
            Theme.Add(previewArea, preview, 1, 0);
            var transport = new DockPanel { Margin = new Thickness(8, 0, 7, 0) };
            originalTimeLabel = Theme.Text("", 11, Theme.Muted);
            cutTimeLabel = Theme.Text("", 11, Theme.Teal, true);
            var clockPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            clockPanel.Children.Add(originalTimeLabel); clockPanel.Children.Add(cutTimeLabel);
            DockPanel.SetDock(clockPanel, Dock.Right); transport.Children.Add(clockPanel);
            play = Theme.Button("▶ 再生", TogglePlay, true);
            play.MinWidth = 67;
            originalMode = Theme.Button("元動画", () => SetMode(false));
            cutMode = Theme.Button("カット後", () => SetMode(true));
            cursorMode = Theme.Text("", 11, Theme.Muted);
            cursorMode.Margin = new Thickness(4, 0, 0, 0);
            transport.Children.Add(Theme.Row(play, originalMode, cutMode, Theme.Button("◀", () => Step(-1, false)),
                Theme.Button("▶", () => Step(1, false)), cursorMode));
            Theme.Add(previewArea, transport, 2, 0);
            Theme.Add(top, Theme.Box(previewArea, Theme.Paper, new Thickness(0)), 0, 0);
            var vertical = new GridSplitter { Width = 8, HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, Background = Theme.Background, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ToolTip = "左右の幅を変える" };
            Theme.Add(top, vertical, 0, 1);
            side = Theme.Box(new ScrollViewer { Content = info, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, Theme.Paper, new Thickness(13, 10, 12, 9));
            Theme.Add(top, side, 0, 2);
            Theme.Add(root, top, 1, 0);

            var horizontal = new GridSplitter { Height = 8, HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, Background = Theme.Background, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ToolTip = "編集欄の高さを変える" };
            Theme.Add(root, horizontal, 2, 0);
            var lower = new Grid { Margin = new Thickness(10, 4, 10, 3) };
            lower.RowDefinitions.Add(new RowDefinition { Height = new GridLength(37) });
            lower.RowDefinitions.Add(new RowDefinition { Height = new GridLength(31) });
            lower.RowDefinitions.Add(new RowDefinition());
            var tools = new DockPanel { LastChildFill = true };
            fineButton = Theme.Button("微調整", () => { Fine = !Fine; Refresh(); });
            loopButton = Theme.Button("境界ループ", ToggleLoop);
            var zoomOut = Theme.Button("−", () => Zoom(zoom / 2)); zoomOut.ToolTip = "タイムラインを縮小";
            var zoomIn = Theme.Button("＋", () => Zoom(zoom * 2)); zoomIn.ToolTip = "タイムラインを拡大";
            var zoomReset = Theme.Button("全体", () => Zoom(1)); zoomReset.ToolTip = "タイムラインの全体を表示";
            var rightTools = Theme.Row(fineButton, loopButton, zoomOut, zoomIn, zoomReset);
            DockPanel.SetDock(rightTools, Dock.Right); tools.Children.Add(rightTools);
            Button captions = null;
            captions = Theme.Button("＋ テロップ ▾", () => CaptionMenu(captions));
            moreTracks = Theme.Button("＋ 段", AddTrack);
            hideButton = Theme.Button("空の段を隠す", () => { HideEmpty = !HideEmpty; Refresh(); });
            tools.Children.Add(Theme.Row(Theme.Button("＋ 残す範囲", AddRange, true), Theme.Button("＋ ブランク", AddBlank),
                Theme.Button("＋ 停止", AddFreeze), Theme.Button("再生速度", Speed), captions, moreTracks, hideButton));
            Theme.Add(lower, tools, 0, 0);
            context = Theme.Row();
            var contextView = new ScrollViewer { Content = context, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Theme.Add(lower, new Border { Background = Theme.Soft, BorderBrush = Theme.Edge, BorderThickness = new Thickness(0, 1, 0, 0),
                Child = contextView }, 1, 0);
            var grid = new Grid { VerticalAlignment = VerticalAlignment.Top };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            timeline = new Timeline(this) { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
            timelineScroll = new ScrollViewer { Content = timeline, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, Focusable = false };
            timelineScroll.SizeChanged += delegate { TimelineSize(); };
            Theme.Add(grid, new Border { Child = tracks, Background = Theme.Soft, BorderBrush = Theme.Edge,
                BorderThickness = new Thickness(0, 0, 1, 0) }, 0, 0);
            Theme.Add(grid, timelineScroll, 0, 1);
            var scrollY = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            scrollY.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                Zoom(e.Delta > 0 ? zoom * 2 : zoom / 2); e.Handled = true;
            };
            Theme.Add(lower, scrollY, 2, 0);
            Theme.Add(root, Theme.Box(lower, Theme.Paper, new Thickness(0)), 3, 0);
            ((FrameworkElement)root.Children[root.Children.Count - 1]).Margin = new Thickness(12, 0, 12, 0);

            total = Theme.Text("", 12, Theme.Teal, true);
            status = Theme.Text("", 11, Theme.Muted); status.TextTrimming = TextTrimming.CharacterEllipsis;
            var footer = new DockPanel { Margin = new Thickness(15, 0, 17, 0) };
            DockPanel.SetDock(total, Dock.Right); footer.Children.Add(total); footer.Children.Add(status);
            Theme.Add(root, footer, 4, 0);
            root.SizeChanged += delegate
            {
                bool compact = root.ActualHeight < 630;
                if (compactLayout == compact) return;
                compactLayout = compact;
                root.RowDefinitions[0].Height = new GridLength(compact ? 50 : 60);
                root.RowDefinitions[4].Height = new GridLength(compact ? 24 : 30);
                previewArea.RowDefinitions[0].Height = new GridLength(compact ? 28 : 32);
                previewArea.RowDefinitions[2].Height = new GridLength(compact ? 37 : 45);
                top.Margin = new Thickness(12, compact ? 6 : 10, 12, 0);
                BuildInfo();
            };
            PreviewKeyDown += Keys;
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!qa && !ConfirmLeave()) { e.Cancel = true; return; }
                playbackTimer.Stop(); autoTimer.Stop(); StopCandidate(); Media.Close();
            };
            Say("操作を試せます  •  架空サンプルを読み込みました");
            Refresh();
        }

        void Keys(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && previewOnly) { Fullscreen(); e.Handled = true; return; }
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                Step(e.Key == Key.Left ? -1 : 1, Fine && Selected != null); e.Handled = true;
            }
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.Z) { Undo(); e.Handled = true; }
            if (e.Key == Key.Y) { Redo(); e.Handled = true; }
            if (e.Key == Key.S) { SaveProject(false); e.Handled = true; }
        }
        void Fullscreen()
        {
            if (!previewOnly) editorHeight = editRow.Height.IsAbsolute ? editRow.Height.Value : editRow.ActualHeight;
            previewOnly = !previewOnly;
            sidebarWidth.MinWidth = previewOnly ? 0 : 225;
            sidebarWidth.Width = new GridLength(previewOnly ? 0 : 294);
            side.Visibility = previewOnly ? Visibility.Collapsed : Visibility.Visible;
            if (previewOnly) { WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; }
            else WindowStyle = WindowStyle.SingleBorderWindow;
            root.RowDefinitions[2].Height = new GridLength(previewOnly ? 0 : 8);
            editRow.MinHeight = previewOnly ? 0 : 139;
            editRow.Height = new GridLength(previewOnly ? 0 : editorHeight);
        }

        internal void Say(string text) { status.Text = text; }
        void Zoom(int level)
        {
            zoom = Math.Max(1, Math.Min(16, level));
            timelineScroll.HorizontalScrollBarVisibility = zoom == 1 ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Visible;
            TimelineSize();
            Dispatcher.BeginInvoke(new Action(() =>
                timelineScroll.ScrollToHorizontalOffset(Math.Max(0, timeline.X(CursorFrame) - timelineScroll.ActualWidth / 2))),
                DispatcherPriority.Loaded);
        }
        void TimelineSize()
        {
            if (timelineScroll.ActualWidth > 0) timeline.Width = Math.Max(26, Math.Floor(timelineScroll.ActualWidth - 1) * zoom);
            timeline.Height = Timeline.Ruler + timeline.Rows * Timeline.Row;
        }

        internal void Commit(string before)
        {
            if (before != Files.Json(P)) loop = false;
            history.Record(before, P);
            Map = new TimeMap(P);
            SnapAfterEdit();
            Refresh();
        }
        void Change(Action action)
        {
            Pause();
            var before = Files.Json(P);
            action();
            Commit(before);
        }
        void SnapAfterEdit()
        {
            var caption = P.Captions.FirstOrDefault(c => c.Id == Selected);
            if (caption != null && CutMode && Map.Warning(caption) == null)
            {
                SeekOutput(Map.SeekTarget(caption.Target, caption.Start));
                return;
            }
            var effect = P.Effects.FirstOrDefault(e => e.Id == Selected && e.Kind != "speed");
            if (effect != null && CutMode && Map.EffectActive(effect))
            {
                SeekOutput(Map.SeekTarget(effect.Id, 0));
                return;
            }
            SeekFrame((int)CursorFrame, false);
        }
        void Undo()
        {
            if (!history.CanUndo) return;
            Pause(); loop = false; P = history.Undo(P); Map = new TimeMap(P); SnapAfterEdit(); Refresh(); Say("ひとつ前の編集に戻しました");
        }
        void Redo()
        {
            if (!history.CanRedo) return;
            Pause(); loop = false; P = history.Redo(P); Map = new TimeMap(P); SnapAfterEdit(); Refresh(); Say("編集をやり直しました");
        }

        internal void Select(string id, bool seek)
        {
            Pause();
            Selected = id;
            if (seek)
            {
                var c = P.Captions.FirstOrDefault(x => x.Id == id);
                var effect = P.Effects.FirstOrDefault(x => x.Id == id);
                if (c != null && Map.Warning(c) == null)
                {
                    CutMode = true; SeekOutput(Map.SeekTarget(c.Target, c.Start));
                }
                if (effect != null)
                {
                    if (effect.Kind != "speed" && Map.EffectActive(effect))
                    {
                        CutMode = true; SeekOutput(Map.SeekTarget(effect.Id, 0));
                    }
                    else SeekFrame(effect.Start);
                }
            }
            Refresh();
        }

        internal void SeekFrame(int frame, bool stop = true)
        {
            if (stop) Pause();
            frame = Math.Max(0, Math.Min(P.Source.Frames, frame));
            CursorFrame = frame;
            originalSeconds = P.Source.Seconds(frame);
            OutputTime = Map.Seek(frame);
            inGap = CutMode && !Map.Parts.Any(p => p.Kind != "blank" &&
                (p.Kind == "freeze" ? p.Start == frame : p.Start <= frame && frame < p.End));
            if (CutMode && Map.Parts.Any(p => p.Kind == "blank" && p.Start == frame)) inGap = false;
            PositionMedia(true);
            RefreshTransport();
        }
        void SeekOutput(double time)
        {
            OutputTime = Math.Max(0, Math.Min(Map.Duration, time));
            inGap = false;
            Part part = Map.At(OutputTime);
            if (part != null) CursorFrame = Map.SourceFrame(part, OutputTime);
            PositionMedia(true);
            RefreshTransport();
        }
        void SetMode(bool cut)
        {
            Pause();
            if (CutMode != cut) loop = false;
            CutMode = cut;
            SeekFrame((int)Math.Floor(CursorFrame + .00001));
            Refresh();
        }

        void TogglePlay()
        {
            if (Playing) { Pause(); return; }
            if (CutMode && Map.Duration <= 0) { Say("先にカット段で残す範囲を選んでください。"); return; }
            if (loop)
            {
                if (CutMode && (OutputTime < loopFrom || OutputTime >= loopTo)) SeekOutput(loopFrom);
                if (!CutMode && (originalSeconds < loopFrom || originalSeconds >= loopTo))
                    SeekFrame((int)P.Source.Frame(loopFrom));
            }
            if (CutMode && OutputTime >= Map.Duration - .000001) SeekOutput(0);
            if (!CutMode && originalSeconds >= P.Source.Duration - .000001) SeekFrame(0);
            Playing = true;
            inGap = false;
            PositionMedia(true);
            clock.Restart(); playbackTimer.Start();
            RefreshTransport();
        }
        void Pause()
        {
            if (!Playing) return;
            Playing = false; playbackTimer.Stop(); clock.Stop();
            if (MediaReady) Media.Pause();
            RefreshTransport();
        }
        Part lastPart;
        bool mediaPlaying;
        void PlaybackTick()
        {
            double delta = clock.Elapsed.TotalSeconds;
            clock.Restart();
            Advance(delta);
        }
        void Advance(double delta)
        {
            if (CutMode)
            {
                OutputTime += delta;
                if (loop && OutputTime >= loopTo) SeekOutput(loopFrom);
                if (OutputTime >= Map.Duration) { Pause(); SeekOutput(Map.Duration); return; }
                var part = CurrentPart;
                CursorFrame = Map.SourceFrame(part, OutputTime);
            }
            else
            {
                originalSeconds += delta;
                if (loop && originalSeconds >= loopTo) { originalSeconds = loopFrom; PositionMedia(true); }
                if (originalSeconds >= P.Source.Duration) { Pause(); SeekFrame(P.Source.Frames); return; }
                CursorFrame = P.Source.Frame(originalSeconds);
                OutputTime = Map.Seek((int)CursorFrame);
            }
            PositionMedia(false);
            RefreshTransport();
            if (zoom > 1)
            {
                double x = timeline.X(CursorFrame);
                if (x < timelineScroll.HorizontalOffset || x > timelineScroll.HorizontalOffset + timelineScroll.ViewportWidth - 20)
                    timelineScroll.ScrollToHorizontalOffset(Math.Max(0, x - 24));
            }
        }
        void PositionMedia(bool seek)
        {
            if (!MediaReady) return;
            var part = CurrentPart;
            if (part != lastPart) seek = true;
            lastPart = part;
            double rate = part != null && part.Kind == "video" ? part.Rate : 1;
            bool run = Playing && (!CutMode || (part != null && part.Kind == "video"));
            double seconds = !CutMode ? originalSeconds : part == null ? P.Source.Seconds((int)CursorFrame) :
                P.Source.Seconds(part.Start) + (part.Kind == "video" ? (OutputTime - part.From) * part.Rate : 0);
            Media.IsMuted = !P.Sound || (CutMode && rate != 1);
            if (seek && (part == null || part.Kind != "blank"))
                Media.Position = TimeSpan.FromSeconds(Math.Max(0, Math.Min(P.Source.Duration, seconds)));
            if (run && (!mediaPlaying || seek)) { Media.SpeedRatio = rate; Media.Play(); }
            if (!run && (mediaPlaying || seek)) Media.Pause();
            mediaPlaying = run;
        }

        void Step(int direction, bool boundary)
        {
            Pause();
            int step = Fine ? 1 : Math.Max(1, (int)Math.Round(P.Source.Fps));
            if (boundary && (P.Clips.Any(c => c.Id == Selected) || P.Captions.Any(c => c.Id == Selected) ||
                P.Effects.Any(e => e.Id == Selected && e.Kind == "speed")))
            {
                var clip = P.Clips.FirstOrDefault(c => c.Id == Selected);
                var effect = P.Effects.FirstOrDefault(x => x.Id == Selected);
                var caption = P.Captions.FirstOrDefault(x => x.Id == Selected);
                var target = caption == null ? null : P.Clips.FirstOrDefault(c => c.Id == caption.Target);
                if (caption != null && target == null) { Say("停止・ブランク内の表示時間は「編集」で調整できます。"); return; }
                int frame = clip != null ? (EdgeLeft ? clip.Start : clip.End) : effect != null ?
                    (EdgeLeft ? effect.Start : effect.End) : target.Origin + (EdgeLeft ? caption.Start : caption.End);
                Change(() => MoveBoundary(EdgeLeft, frame + direction * step));
                return;
            }
            var part = CurrentPart;
            if (part != null && part.Kind != "video")
            {
                SeekOutput(Math.Max(part.From, Math.Min(part.To - .000001, OutputTime + direction * step / P.Source.Fps)));
                return;
            }
            SeekFrame((int)Math.Round(CursorFrame) + direction * step);
        }
        void ToggleLoop()
        {
            Pause();
            if (loop) { loop = false; Refresh(); return; }
            var clip = P.Clips.FirstOrDefault(c => c.Id == Selected);
            if (clip == null) { Say("境界ループは、先に緑の「残す」バーを選んでください。"); return; }
            int frame = EdgeLeft ? clip.Start : clip.End;
            int step = (int)Math.Round(P.Source.Fps);
            int from = Math.Max(0, frame - step), to = Math.Min(P.Source.Frames, frame + step);
            loopFrom = CutMode ? Map.Seek(from) : P.Source.Seconds(from);
            loopTo = CutMode ? Map.Seek(to) : P.Source.Seconds(to);
            if (loopTo <= loopFrom) return;
            loop = true;
            if (CutMode) SeekOutput(loopFrom); else SeekFrame(from);
            TogglePlay(); Refresh();
        }

        void RefreshTransport()
        {
            if (preview == null) return;
            play.Content = Playing ? "Ⅱ 一時停止" : "▶ 再生";
            transportMode.Text = CutMode ? (inGap ? "削除区間 · 再生時に飛ばします" : "カット後の確認") : "元動画を確認";
            var part = CurrentPart;
            originalTimeLabel.Text = part != null && part.Kind == "blank" ? "元動画の位置  ―  （ブランク）" :
                "元動画  " + Theme.Clock(P.Source.Seconds((int)Math.Min(P.Source.Frames, CursorFrame)), true) + "   " +
                Math.Min(P.Source.Frames - 1, (int)CursorFrame) + " F";
            cutTimeLabel.Text = "完成時刻  " + Theme.Clock(OutputTime, true) + " / " + Theme.Clock(Map.Duration, true);
            cursorMode.Text = Fine ? "1フレーム" : "約1秒";
            SetActive(originalMode, !CutMode); SetActive(cutMode, CutMode);
            preview.InvalidateVisual(); timeline.InvalidateVisual();
        }
        static void SetActive(Button button, bool active)
        {
            button.Background = active ? Theme.Mint : Theme.Paper;
            button.Foreground = active ? Theme.Teal : Theme.Ink;
            button.BorderBrush = active ? Theme.Brush("#9ACABB") : Theme.Edge;
        }
        internal void Refresh()
        {
            int rows = timeline.Rows;
            if (rows != lastRows)
            {
                double height = !previewOnly && editRow.Height.IsAbsolute ? editRow.Height.Value : editorHeight;
                editorHeight = Math.Max(139, height + (rows - lastRows) * Timeline.Row);
                if (!previewOnly) editRow.Height = new GridLength(editorHeight);
                lastRows = rows;
            }
            TimelineSize();
            var current = Files.Json(P);
            title.Text = (P.Source.Demo ? "光学ステージ · 架空サンプル" : Path.GetFileName(P.Source.Path)) +
                (savedJson != current ? "  •" : "");
            meta.Text = "UI 試作版  /  ローカル編集";
            total.Text = "完成予定  " + Theme.Clock(Map.Duration, true) + "   •   元動画  " + Theme.Clock(P.Source.Duration);
            undo.IsEnabled = history.CanUndo; redo.IsEnabled = history.CanRedo;
            moreTracks.IsEnabled = P.Tracks < 3;
            hideButton.Content = HideEmpty ? "全段を表示" : "空の段を隠す";
            SetActive(fineButton, Fine); SetActive(loopButton, loop);
            BuildInfo(); BuildTrackLabels(); BuildContext(); RefreshTransport();
        }
        void BuildTrackLabels()
        {
            tracks.Children.Clear();
            LabelTrack("元動画の位置", Timeline.Ruler, Theme.Muted, Theme.Soft);
            LabelTrack("オリジナル", Timeline.Row, Theme.Ink, Theme.Paper);
            LabelTrack("カット", Timeline.Row, Theme.Teal, Theme.Mint);
            foreach (int i in timeline.CaptionTracks) LabelTrack("テロップ " + i, Timeline.Row, Theme.Ink, Theme.Paper);
            var sound = Theme.Button(P.Sound ? "音声あり" : "音声なし", () => Change(() => P.Sound = !P.Sound));
            sound.MinHeight = 0; sound.Height = Timeline.Row; sound.Margin = new Thickness(4, 0, 5, 0);
            sound.ToolTip = "音声あり／なしを切り替える";
            tracks.Children.Add(sound);
        }
        void LabelTrack(string label, double height, Brush color, Brush fill)
        {
            tracks.Children.Add(new Border { Height = height, Background = fill, Padding = new Thickness(8, 0, 0, 0),
                BorderBrush = Theme.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Child = Theme.Text(label, 11, color, true) });
        }

        static MenuItem Item(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += delegate { action(); };
            return item;
        }
        static void Menu(Button button, params MenuItem[] items)
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var item in items) menu.Items.Add(item);
            menu.IsOpen = true;
        }
    }
}

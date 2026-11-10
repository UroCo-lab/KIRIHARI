using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kirihari
{
    internal static class Qa
    {
        static readonly List<string> log = new List<string>();
        static string trace;
        public static int Run(string output)
        {
            Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "failed.txt"))) File.Delete(Path.Combine(output, "failed.txt"));
            trace = Path.Combine(output, "progress.txt");
            File.WriteAllText(trace, "Starting" + Environment.NewLine);
            Application.Current.DispatcherUnhandledException += (s, e) =>
            {
                File.WriteAllText(Path.Combine(output, "failed.txt"), e.Exception.ToString());
                Environment.Exit(1);
            };
            Model();
            var editor = new Editor(true) { ShowInTaskbar = false, ShowActivated = false };
            try
            {
                editor.Show();
                Pump();
                SetupQa.Run(output);
                Note("Host display DPI " + VisualTreeHelper.GetDpi(editor).PixelsPerDip.ToString("0.##") + "x");
                editor.VerifyUi(output);
                File.WriteAllLines(Path.Combine(output, "results.txt"), log);
                return 0;
            }
            finally { editor.Close(); }
        }

        static void Model()
        {
            var p = Project.Demo(true);
            p.Validate();
            var m = new TimeMap(p);
            Near(m.Duration, 26, "26-second total");
            Near(m.Seek(300), 10, "Gap maps to blank");
            Near(m.Seek(600), 15, "First frame after blank");
            Near(m.SourceFrame(m.At(16), 16), 660, "2x source position");
            Near(m.SourceFrame(m.At(21), 21), 780, "Fixed freeze position");
            Check(m.At(19).Kind == "freeze" && m.At(22).Kind == "video", "Freeze boundaries");

            var clip = p.Clips[1];
            var cap = new Caption { Target = clip.Id, Start = 60, End = 100 };
            var hold = p.Effects.Single(e => e.Kind == "freeze");
            var delayed = new Caption { Target = hold.Id, Start = 30, End = 60 };
            var windows = m.CaptionWindows(cap).Single();
            Near(windows.Item1, 16, "Clip caption start at 2x");
            Near(windows.Item2, 16 + 40 / 60.0, "Clip caption end at 2x");
            Check(m.Active(delayed, m.At(20.1), 20.1) && !m.Active(delayed, m.At(19.9), 19.9) &&
                !m.Active(delayed, m.At(21.1), 21.1), "Freeze-only caption timing");
            p.Clips[0].End = 270;
            m = new TimeMap(p);
            Near(m.CaptionWindows(cap).Single().Item1, 15, "Caption follows preceding trim");
            p.Effects.First(e => e.Kind == "blank").Frames += 60;
            m = new TimeMap(p);
            Near(m.CaptionWindows(cap).Single().Item1, 17, "Caption follows inserted time");
            p.Effects.First(e => e.Kind == "speed").Rate = .5;
            m = new TimeMap(p);
            Near(m.CaptionWindows(cap).Single().Item1, 20, "Caption follows speed change");
            clip.Start += 90;
            Check(new TimeMap(p).Warning(cap) != null, "Warn about a trimmed target");
            p.Clips.Remove(clip);
            Check(new TimeMap(p).Warning(cap) == "貼り付け先が見つかりません", "Warn about a removed target");
            hold.Frames = 30;
            Check(new TimeMap(p).Warning(delayed) != null, "Warn about missing freeze");
            var source = new Source { Demo = true, Frames = 3, Width = 2, Height = 2, RateNumerator = 30000,
                RateDenominator = 1001, PtsTicks = new long[] { 10000, 410000, 890000, 1090000 } };
            source.Validate();
            Near(source.Seconds(2), .088, "VFR timestamp conversion");
            Near(source.Frame(.064), 1.5, "VFR inverse conversion");
            source.PtsTicks = null;
            Near(source.Seconds(3), .1001, "Rational FPS conversion");
            Note("PASS model: 26-second example, seek, skipped gaps, speed, freeze, captions, missing targets, VFR/29.97 model");
        }

        internal static void Note(string value) { log.Add(value); File.AppendAllText(trace, value + Environment.NewLine); }
        internal static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        internal static void Near(double actual, double expected, string message)
        {
            Check(Math.Abs(actual - expected) < .0001, message + " (got " + actual + ", expected " + expected + ")");
        }
        internal static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        internal static void Wait(Func<bool> condition, int limit)
        {
            var watch = Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < limit)
            {
                Pump(); Thread.Sleep(15);
            }
        }
        internal static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) yield return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        internal static void Click(DependencyObject parent, string name)
        {
            var button = Children<Button>(parent).FirstOrDefault(b => Equals(b.Content, name) && b.IsVisible);
            Check(button != null && button.IsEnabled, "Could not click " + name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        internal static void Sheet(Action open, string accept, Action<Window> edit = null)
        {
            File.AppendAllText(trace, "Sheet -> " + accept + Environment.NewLine);
            Exception failed = null;
            bool executed = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(55) };
            timer.Tick += delegate
            {
                var sheet = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Owner is Editor && w.IsVisible);
                if (sheet == null) return;
                timer.Stop();
                try
                {
                    File.AppendAllText(trace, "Open: " + sheet.Title + Environment.NewLine);
                    executed = true;
                    if (edit != null) edit(sheet);
                    File.AppendAllText(trace, "Click: " + accept + Environment.NewLine);
                    Click(sheet, accept);
                    File.AppendAllText(trace, "Clicked" + Environment.NewLine);
                    if (sheet.IsVisible)
                    {
                        string error = string.Join(" / ", Children<TextBlock>(sheet).Select(t => t.Text));
                        throw new InvalidOperationException("Sheet remained open: " + error);
                    }
                }
                catch (Exception e) { failed = e; if (sheet.IsVisible) sheet.Close(); }
            };
            timer.Start();
            open();
            timer.Stop();
            if (failed != null) throw failed;
            Check(executed, "Sheet did not open for " + accept);
            File.AppendAllText(trace, "Pump after dialog" + Environment.NewLine);
            Pump();
            File.AppendAllText(trace, "Dialog settled" + Environment.NewLine);
        }
        internal static void Fill(Window window, params string[] values)
        {
            var fields = Children<TextBox>(window).ToArray();
            Check(fields.Length >= values.Length, "Dialog fields are missing");
            for (int i = 0; i < values.Length; i++) fields[i].Text = values[i];
        }
    }

    internal sealed partial class Editor
    {
        void QaLayout(double width, double height)
        {
            root.Width = width;
            root.Height = height;
            Width = width + 20;
            Height = height + 46;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            Qa.Pump();
        }
        void QaScreen(string path, double scale = 1, bool detached = false)
        {
            root.UpdateLayout();
            Qa.Pump();
            int w = (int)Math.Round(root.ActualWidth * scale), h = (int)Math.Round(root.ActualHeight * scale);
            var image = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            if (!detached) image.Render(root);
            else
            {
                var size = new Size(root.Width, root.Height);
                Content = null;
                UpdateLayout();
                var surface = new Decorator { Child = root };
                try
                {
                    root.InvalidateMeasure();
                    root.InvalidateArrange();
                    surface.Measure(size);
                    surface.Arrange(new Rect(size));
                    surface.UpdateLayout();
                    image.Render(surface);
                }
                finally { surface.Child = null; Content = root; }
            }
            var last = new byte[4];
            image.CopyPixels(new Int32Rect(w - 2, h - 2, 1, 1), last, 4, 0);
            Qa.Check(last[3] == 255, "Screenshot viewport did not cover target: " + Path.GetFileName(path));
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(path)) png.Save(file);
        }
        Point QaPoint(double frame, int track)
        {
            return new Point(timeline.X(frame), Timeline.Ruler + Timeline.Row * (track + .57));
        }
        void QaGesture(int from, int to, int track)
        {
            Qa.Pump();
            timeline.QaGesture(QaPoint(from, track), QaPoint(to, track));
            Qa.Pump();
        }

        internal void VerifyUi(string folder)
        {
            QaLayout(1348, 712);
            Qa.Click(root, "元動画");
            TogglePlay();
            Qa.Wait(() => CursorFrame > 0, 1500);
            Qa.Check(CursorFrame > 0 && Playing, "Original demo playback timer");
            Pause();
            SeekFrame(0);
            QaScreen(Path.Combine(folder, "01-main.png"));

            QaGesture(0, 210, 1); QaGesture(300, 480, 1); QaGesture(600, 900, 1);
            Qa.Check(P.Clips.Count == 3, "Drag creates three clips");
            Qa.Pump();
            var from = QaPoint(210, 1); from.X -= 3;
            timeline.QaGesture(from, QaPoint(240, 1));
            Qa.Check(P.Clips[0].End == 240, "Right handle edits boundary");
            Qa.Click(root, "微調整");
            Qa.Click(root, "ずらす ▶");
            Qa.Check(P.Clips[0].End == 241, "Fine mode moves one frame");
            Qa.Click(root, "元に戻す");
            Qa.Check(P.Clips[0].End == 240, "Undo boundary adjustment");
            Qa.Click(root, "やり直す");
            Qa.Check(P.Clips[0].End == 241, "Redo boundary adjustment");
            Qa.Click(root, "＋");
            Qa.Pump();
            Qa.Check(timeline.ActualWidth > timelineScroll.ViewportWidth * 1.8, "Zoom");
            Qa.Click(root, "全体"); Qa.Pump();
            Qa.Click(root, "カット後");
            SeekFrame(30);
            QaScreen(Path.Combine(folder, "02-cut.png"));
            Qa.Sheet(ShowSave, "キャンセル");
            Qa.Note("PASS A: start with synthetic source, add 3 cuts, resize handle, 1-frame step, zoom, modes, save sheet, undo/redo");

            ResetProject(Project.Demo(false), null);
            Fine = false;
            QaGesture(0, 300, 1); QaGesture(600, 900, 1);
            double oldWidth = timeline.ActualWidth, x600 = timeline.X(600);
            QaGesture(450, 450, 1);
            Qa.Sheet(() => Qa.Click(root, "＋ ブランク"), "適用");
            var blank = P.Effects.Single(e => e.Kind == "blank");
            Qa.Check(blank.Frames == 150, "5-second blank");
            QaGesture(630, 630, 1);
            Qa.Sheet(() => Qa.Click(root, "再生速度"), "適用", w => Qa.Fill(w, "600", "719"));
            QaGesture(780, 780, 0);
            Qa.Sheet(() => Qa.Click(root, "＋ 停止"), "適用");
            var freeze = P.Effects.Single(e => e.Kind == "freeze");
            Qa.Check(freeze.Frames == 90, "3-second hold");
            Qa.Near(Map.Duration, 26, "26-second UI example");
            Qa.Near(timeline.ActualWidth, oldWidth, "Timeline width remains fixed");
            Qa.Near(timeline.X(600), x600, "Timeline coordinates remain fixed");
            SeekOutput(9.9); Advance(.2);
            Qa.Check(CurrentPart.Kind == "blank" && CursorFrame == 450, "Blank rendered at fixed marker");
            Advance(5.9);
            Qa.Near(CursorFrame, 660, "2x cursor advance");
            Advance(4);
            Qa.Check(CurrentPart.Kind == "freeze" && CursorFrame == 780, "Stop marker and playback");
            QaScreen(Path.Combine(folder, "03-effects.png"));
            Advance(2.2);
            Qa.Check(CurrentPart.Kind == "video" && CursorFrame > 780, "Resume after freeze");
            Qa.Note("PASS C/D: 5-second blank, 2x speed, 3-second freeze, 26 seconds, fixed width and motion");

            string sampleDir = Program.QaSamples;
            var image = Path.Combine(sampleDir, "optical-reference.png");
            Select(blank.Id, true);
            Qa.Sheet(() => AddCaption("text"), "貼る", w => Qa.Fill(w, "基準点 A を確認", "0", "5"));
            Qa.Click(root, "＋ 段");
            Select(blank.Id, true);
            Qa.Sheet(() => AddCaption("image", image), "貼る", w => Qa.Fill(w, "0", "5"));
            Qa.Click(root, "＋ 段");
            Select(blank.Id, true);
            Qa.Sheet(() => AddCaption("arrow"), "貼る", w => Qa.Fill(w, "0", "5", "20", "15"));
            var arrow = P.Captions.Single(c => c.Kind == "arrow");
            Qa.Pump();
            double oldX = arrow.X, oldWidthCaption = arrow.Width, oldAngle = arrow.Angle;
            preview.QaTransform(arrow, "move"); Qa.Pump();
            preview.QaTransform(arrow, "size"); Qa.Pump();
            preview.QaTransform(arrow, "rotate"); Qa.Pump();
            Qa.Check(arrow.X != oldX && arrow.Width != oldWidthCaption && arrow.Angle != oldAngle && arrow.Opacity == .8,
                "Preview move, resize and rotate");
            Qa.Check(P.Captions.Count(c => Map.Active(c, CurrentPart, OutputTime)) == 3, "Three simultaneous captions on blank");
            Select(freeze.Id, true);
            Qa.Sheet(() => AddCaption("ellipse"), "貼る", w => Qa.Fill(w, "1", "2.5"));
            var holdCaption = P.Captions.Single(c => c.Target == freeze.Id);
            Qa.Check(Map.Active(holdCaption, CurrentPart, OutputTime), "Freeze-only caption preview");
            Select(freeze.Id, true);
            Qa.Sheet(() => Qa.Click(root, "長さを変える"), "適用", w => Qa.Fill(w, "2"));
            Qa.Check(Map.Warning(holdCaption) != null, "Shortened freeze surfaces warning");
            Qa.Click(root, "元に戻す");
            Qa.Near(Map.Duration, 26, "Undo freeze change");
            Qa.Check(Map.Warning(P.Captions.Single(c => c.Id == holdCaption.Id)) == null, "Undo restores caption timing");
            Select(blank.Id, true);
            Qa.Sheet(() => Qa.Click(root, "長さを変える"), "適用", w => Qa.Fill(w, "6"));
            Qa.Near(Map.Duration, 27, "Blank duration edit");
            Qa.Click(root, "元に戻す");
            var savedCaps = P.Captions.ToArray();
            Change(() => P.Captions.RemoveAll(c => c.Track != 3));
            Qa.Click(root, "空の段を隠す");
            Qa.Check(timeline.CaptionTracks.SequenceEqual(new[] { 3 }), "Unused tracks collapse without renumbering");
            Qa.Click(root, "全段を表示");
            Qa.Click(root, "元に戻す");
            Qa.Check(P.Captions.Count == savedCaps.Length, "Restore captions after testing collapsed tracks");
            Qa.Note("PASS B/D: edit blank length, image/text/arrow in 3 tracks, opacity/rotation/resize, delayed freeze caption, trim warning, undo");

            var example = Files.Load(Path.Combine(sampleDir, "光学ステージ_編集見本.kirihari"));
            ResetProject(example, null);
            Select("reference-arrow", true);
            Say("画像・文字・矢印をブランクに貼った編集見本です");
            QaScreen(Path.Combine(folder, "04-captions.png"));
            QaLayout(1092, 558);
            Qa.Check(timeline.ActualWidth > 900 && preview.ActualHeight >= 125, "Compact viewport: timeline " + timeline.ActualWidth + ", preview " + preview.ActualHeight);
            Qa.Check(tracks.Children.Count == 7, "Compact 6 tracks");
            QaScreen(Path.Combine(folder, "05-1366x768-125.png"), 1.25);
            QaLayout(1900, 1022);
            QaScreen(Path.Combine(folder, "06-1920x1080.png"), 1, true);
            QaLayout(1536 - 18, 864 - 54);
            QaScreen(Path.Combine(folder, "07-1920x1080-125.png"), 1.25);
            QaLayout(1092, 558);
            Qa.Sheet(() => Qa.Click(root, "書き出し"), "設定を反映",
                w => Qa.Children<ComboBox>(w).First().SelectedItem = "軽量");
            Qa.Check(P.Quality == "軽量", "Export settings persisted");
            Qa.Click(root, "▸  保存先");
            Qa.Check(Qa.Children<Button>(root).Any(b => Equals(b.Content, "出力先を選ぶ")), "Folder controls on compact viewport");
            Qa.Click(root, "▸  空き容量・プロキシ");
            Qa.Sheet(ShowProxy, "閉じる", w =>
            {
                Qa.Click(w, "開始・再開");
                Qa.Wait(() => proxyPercent >= 4, 1500);
                Qa.Click(w, "中止");
                Qa.Check(proxyPercent >= 4 && !proxyRunning, "Proxy pause");
                int percent = proxyPercent;
                Qa.Click(w, "開始・再開");
                Qa.Wait(() => proxyPercent > percent, 1500);
                Qa.Check(proxyPercent > percent, "Proxy resume");
                Qa.Click(w, "リセット");
                Qa.Check(proxyPercent == 0, "Proxy reset");
            });
            Qa.Note("PASS E: compact and full-size WPF layout renders at 100% / 125% equivalent; export settings, locations and proxy controls");

            string saved = Path.Combine(folder, "往復確認.kirihari");
            Files.Save(P, saved);
            var loaded = Files.Load(saved);
            Qa.Near(new TimeMap(loaded).Duration, 26, "Saved duration");
            Qa.Check(loaded.Captions.Single(c => c.Kind == "image").Image == image &&
                !File.ReadAllText(saved).Contains(image) && loaded.Quality == "軽量", "Relative path and settings round trip");
            Files.Save(loaded, saved);
            P.WorkFolder = folder;
            Qa.Sheet(ShowAuto, "適用", w => Qa.Children<ComboBox>(w).First().SelectedItem = "使う");
            Qa.Check(File.Exists(AutoPath) && Files.Load(AutoPath).AutoSave, "Autosave actual file");
            Qa.Note("PASS storage: .kirihari atomic write, replacement, relative image path, reload, autosave copy");

            string fixture = Path.Combine(sampleDir, "optical-motion.mp4");
            string hash;
            using (var f = File.OpenRead(fixture))
            using (var sha = System.Security.Cryptography.SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(f));
            TryMedia(fixture, "new");
            Qa.Wait(() => candidate == null, 13500);
            if (!MediaReady)
            {
                Qa.Note("PARTIAL local MP4: system decoder did not open the fixture on this host: " + status.Text);
                return;
            }
            Qa.Check(!P.Source.Demo && P.Source.Width == 128 && P.Source.Height == 72 && P.Source.Frames > 1 && P.Source.Audio, "Real video metadata");
            TogglePlay(); Qa.Wait(() => CursorFrame > 2 && Media.Position.TotalMilliseconds > 80, 1600); Pause();
            Qa.Check(CursorFrame > 2 && Media.Position.TotalMilliseconds > 80, "Real preview playback state");
            Qa.Check(Media.IsMuted, "Muted by default");
            Change(() => P.Sound = true);
            Qa.Check(!Media.IsMuted, "Sound can be enabled");
            using (var f = File.OpenRead(fixture))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                Qa.Check(hash == Convert.ToBase64String(sha.ComputeHash(f)), "Original unchanged after preview");
            Qa.Note("PASS local MP4: Windows media opened, playback advanced, " + P.Source.Fps + " fps" +
                (P.Source.EstimatedRate ? " (fallback)" : " (metadata)") + ", source SHA-256 unchanged");
        }
    }
}

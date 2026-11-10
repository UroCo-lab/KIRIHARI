using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Kirihari
{
    internal sealed partial class Editor
    {
        bool detailOpen, locationsOpen, spaceOpen, warningsOpen;
        string autoKey = Project.Id().Substring(0, 8);
        MediaPlayer candidate;
        DispatcherTimer mediaTimeout;
        int proxyPercent;
        bool proxyRunning;
        const string AppDataFolder = "data";
        static readonly string AppFolder = AppDomain.CurrentDomain.BaseDirectory;
        string Work { get { return P.WorkFolder ?? Path.Combine(AppFolder, AppDataFolder); } }
        string Output { get { return P.OutputFolder ?? Path.Combine(AppFolder, "output"); } }
        string ProxyFolder { get { return Path.Combine(Work, "proxy"); } }
        string AutoPath { get { return projectPath == null ? Path.Combine(Work, "自動保存-" + autoKey + ".kirihari") :
            Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".自動保存.kirihari"); } }

        internal static bool UserError(Exception e)
        {
            return e is IOException || e is UnauthorizedAccessException || e is ArgumentException ||
                e is NotSupportedException || e is InvalidOperationException || e is System.Security.SecurityException;
        }
        internal void SuspendForDialog() { Pause(); }
        void OpenDemo(bool example)
        {
            if (!ConfirmLeave()) return;
            ResetProject(Project.Demo(example), null);
            if (example) SetMode(true);
            Say(example ? "完成26秒の見本を開きました" : "架空サンプルを開きました。カット段をドラッグできます。");
        }
        void ResetProject(Project project, string path)
        {
            Pause();
            StopCandidate();
            Media.Close(); MediaReady = false; mediaPlaying = false; lastPart = null; mediaNotice = null;
            P = project; projectPath = path; savedJson = Files.Json(P);
            Map = new TimeMap(P); history = new History(); CutMode = false; Selected = null; EdgeLeft = true; loop = false;
            proxyPercent = 0; proxyRunning = false;
            autoJson = null; autoStatus = ""; autoKey = Project.Id().Substring(0, 8); originalSeconds = 0; OutputTime = 0; CursorFrame = 0; inGap = false;
            Zoom(1); Refresh();
        }

        bool ConfirmLeave()
        {
            Pause();
            if (Files.Json(P) == savedJson) return true;
            var answer = MessageBox.Show(this, "編集内容を保存しますか？", "KIRIHARI",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && SaveProject(false));
        }
        static OpenFileDialog VideoDialog(string title)
        {
            return new OpenFileDialog { Title = title, Filter = "動画|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm|すべてのファイル|*.*",
                CheckFileExists = true, DereferenceLinks = true };
        }
        void OpenVideo()
        {
            Pause();
            var dialog = VideoDialog("元動画を選ぶ");
            if (dialog.ShowDialog(this) != true || !ConfirmLeave()) return;
            TryMedia(dialog.FileName, "new");
        }
        void Relink()
        {
            Pause();
            var dialog = VideoDialog("元と同じ動画を選ぶ");
            if (dialog.ShowDialog(this) == true) TryMedia(dialog.FileName, "relink");
        }
        void TryMedia(string path, string mode)
        {
            StopCandidate();
            try
            {
                path = Files.Local(path, AppFolder);
                var file = new FileInfo(path);
                if (!file.Exists) throw new IOException("元動画が見つかりません。保存先の「動画を探す」から選び直せます。");
                var reader = new MediaPlayer { ScrubbingEnabled = true, IsMuted = true };
                candidate = reader;
                var current = P;
                var before = mode == "new" ? Files.Json(P) : null;
                reader.MediaOpened += delegate
                {
                    if (candidate != reader) return;
                    if (current != P || (before != null && before != Files.Json(P)))
                    {
                        StopCandidate(); Say("編集中の内容が変わったため、動画の切り替えを中止しました。"); return;
                    }
                    try { AcceptMedia(reader, file, mode); }
                    catch (Exception e)
                    {
                        if (!UserError(e)) throw;
                        StopCandidate(); Say(e.Message);
                    }
                };
                reader.MediaFailed += delegate
                {
                    if (candidate == reader) StopCandidate();
                    else if (Media == reader) { MediaReady = false; Refresh(); }
                    else return;
                    Say("この映像を開けません。Windows が再生できる動画を選んでください。");
                };
                mediaTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
                mediaTimeout.Tick += delegate { StopCandidate(); Say("映像を開くまでに時間がかかっています。ファイルを確認してください。"); };
                mediaTimeout.Start();
                Say("元動画を読み込んでいます…");
                reader.Open(new Uri(path));
                reader.Pause();
            }
            catch (Exception e)
            {
                if (!UserError(e) && !(e is COMException)) throw;
                StopCandidate(); Say(e.Message);
            }
        }
        void StopCandidate()
        {
            if (mediaTimeout != null) { mediaTimeout.Stop(); mediaTimeout = null; }
            if (candidate == null) return;
            var old = candidate; candidate = null; old.Close();
        }
        void AcceptMedia(MediaPlayer reader, FileInfo file, string mode)
        {
            if (!reader.HasVideo || !reader.NaturalDuration.HasTimeSpan ||
                reader.NaturalVideoWidth <= 0 || reader.NaturalVideoHeight <= 0)
                throw new IOException("映像と長さを取得できる動画ファイルを選んでください。");
            if (mode != "new" && (reader.NaturalVideoWidth != P.Source.Width || reader.NaturalVideoHeight != P.Source.Height ||
                Math.Abs(reader.NaturalDuration.TimeSpan.TotalSeconds - P.Source.Duration) > Math.Max(.12, 2 / P.Source.Fps)))
                throw new IOException("元動画と長さまたは解像度が異なります。元と同じファイルを選んでください。");
            if (mode == "relink" && P.Source.Bytes != 0 && P.Source.Bytes != file.Length)
                throw new IOException("元動画とファイルサイズが異なります。元と同じファイルを選んでください。");

            var source = ReadSource(reader, file);
            candidate = null; StopCandidate();
            if (mode == "new") ResetProject(Project.Create(source), null);
            Media.Close(); Media = reader; MediaReady = true; mediaPlaying = false; lastPart = null; mediaNotice = null;
            if (mode == "relink") Change(() => P.Source.Path = file.FullName);
            PositionMedia(true); Refresh();
            Say(mode == "relink" ? "元動画をつなぎ直しました" : source.EstimatedRate ?
                "動画を開きました。フレームレートが取得できず、30 fps の目安で表示します。" :
                "動画を開きました。カット段をドラッグすると、その範囲が残ります。");
        }
        static Source ReadSource(MediaPlayer media, FileInfo file)
        {
            int rate = ShellRate(file);
            var source = new Source { Path = file.FullName, Bytes = file.Length, Audio = media.HasAudio,
                Width = media.NaturalVideoWidth, Height = media.NaturalVideoHeight,
                RateNumerator = rate == 29970 ? 30000 : rate == 23976 ? 24000 : rate == 59940 ? 60000 : rate <= 0 ? 30 : rate,
                RateDenominator = rate == 29970 || rate == 23976 || rate == 59940 ? 1001 : rate <= 0 ? 1 : 1000,
                EstimatedRate = rate <= 0 };
            double frames = Math.Round(source.Fps * media.NaturalDuration.TimeSpan.TotalSeconds);
            if (frames < 1 || frames > 100000000) throw new IOException("この動画の長さは試作版の範囲外です。");
            source.Frames = (int)frames;
            return source;
        }
        static int ShellRate(FileInfo file)
        {
            object shell = null, folder = null, item = null;
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) return 0;
                shell = Activator.CreateInstance(type);
                folder = ComInvoke(shell, "NameSpace", file.DirectoryName);
                if (folder == null) return 0;
                item = ComInvoke(folder, "ParseName", file.Name);
                if (item == null) return 0;
                int rate = Convert.ToInt32(ComInvoke(item, "ExtendedProperty", "System.Video.FrameRate"));
                return rate >= 1000 && rate <= 1000000 ? rate : 0;
            }
            catch (Exception e)
            {
                if (e is COMException || e is TargetInvocationException || e is InvalidCastException ||
                    e is MissingMethodException || e is OverflowException || e is FormatException) return 0;
                throw;
            }
            finally
            {
                foreach (var o in new[] { item, folder, shell })
                    if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
            }
        }
        static object ComInvoke(object target, string method, object arg)
        {
            return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, new[] { arg });
        }

        void SaveMenu(Button button)
        {
            Menu(button, Item("編集を保存…", ShowSave), Item("名前を付けて保存…", () => SaveProject(true)),
                Item("編集ファイルを開く…", OpenProject), Item("自動保存の設定…", ShowAuto));
        }
        void ShowSave()
        {
            var sheet = new Sheet(this, "編集を保存", "カット範囲やテロップなどを .kirihari ファイルに保存します。");
            sheet.Add("元動画", Theme.Wrap(P.Source.Demo ? "光学ステージ · 架空サンプル" : Path.GetFileName(P.Source.Path), 12, Theme.Muted));
            sheet.Add("保存先", Theme.Wrap(projectPath ?? "未指定 · 保存時に選択", 12, Theme.Muted));
            sheet.Add("完成予定", Theme.Text(Theme.Clock(Map.Duration, true), 14, Theme.Teal, true));
            sheet.Open("保存先へ進む", () => { if (!SaveProject(false)) throw new ArgumentException("保存先を選ぶと保存できます。"); });
        }
        bool SaveProject(bool choose)
        {
            Pause();
            string path = projectPath;
            try
            {
                if (choose || path == null)
                {
                    var dialog = new SaveFileDialog { Title = "編集ファイルの保存先", Filter = "KIRIHARI 編集ファイル|*.kirihari",
                        DefaultExt = ".kirihari", AddExtension = true, OverwritePrompt = true,
                        FileName = P.Source.Demo ? "光学ステージ.kirihari" : Path.GetFileNameWithoutExtension(P.Source.Path) + ".kirihari",
                        InitialDirectory = Directory.Exists(Work) ? Work : AppFolder };
                    if (dialog.ShowDialog(this) != true) return false;
                    path = Files.Local(dialog.FileName, AppFolder);
                }
                Files.Save(P, path);
                projectPath = path; savedJson = Files.Json(P); autoJson = null;
                Refresh(); Say("編集ファイルを保存しました");
                return true;
            }
            catch (Exception e)
            {
                if (!UserError(e)) throw;
                MessageBox.Show(this, "編集を保存できません。保存先を確認してください。\n" + e.Message,
                    "KIRIHARI", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        void OpenProject()
        {
            Pause();
            var dialog = new OpenFileDialog { Title = "編集ファイルを開く", Filter = "KIRIHARI 編集ファイル|*.kirihari" };
            if (dialog.ShowDialog(this) != true || !ConfirmLeave()) return;
            try
            {
                string path = Files.Local(dialog.FileName, AppFolder);
                var loaded = Files.Load(path);
                ResetProject(loaded, path);
                if (!P.Source.Demo && File.Exists(P.Source.Path)) TryMedia(P.Source.Path, "existing");
                else if (!P.Source.Demo) { locationsOpen = true; BuildInfo(); Say("元動画が見つかりません。保存先の「動画を探す」から選び直せます。"); }
                else Say("編集ファイルを開きました");
            }
            catch (Exception e)
            {
                if (!UserError(e) && !(e is NullReferenceException)) throw;
                Say("編集ファイルを開けません。内容と保存先を確認してください。");
            }
        }
        void ShowAuto()
        {
            var sheet = new Sheet(this, "自動保存", "変更があるときに、編集ファイルのコピーを保存できます。");
            var use = sheet.Choice("自動保存", new[] { "使う", "使わない" }, P.AutoSave ? "使う" : "使わない");
            sheet.Add("コピーの場所", Theme.Wrap(AutoPath, 11, Theme.Muted));
            sheet.Add("状態", Theme.Wrap(string.IsNullOrEmpty(autoStatus) ? "まだコピーを保存していません" : autoStatus, 12, Theme.Muted));
            sheet.Open("適用", () =>
            {
                Change(() => P.AutoSave = use.SelectedIndex == 0);
                if (P.AutoSave) AutoSave();
                else { autoStatus = "自動保存は停止中です"; BuildInfo(); }
                Say(autoStatus);
            });
        }
        void AutoSave()
        {
            if (!P.AutoSave || Playing) return;
            var current = Files.Json(P);
            if (current == savedJson || current == autoJson) return;
            string previous = autoStatus;
            try
            {
                string path = Files.Local(AutoPath, AppFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                Files.Save(P, path);
                autoJson = current; autoStatus = "自動保存  " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception e)
            {
                if (!UserError(e)) throw;
                autoStatus = "自動保存できません。書き込める場所を指定してください。";
            }
            if (autoStatus != previous) { BuildInfo(); Say(autoStatus); }
        }

        void BuildInfo()
        {
            info.Children.Clear();
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, compactLayout ? 4 : 8) };
            var collapse = Theme.Button(sidebarWidth.Width.Value <= 225 ? "広げる" : "幅を狭める", () =>
            {
                sidebarWidth.Width = new GridLength(sidebarWidth.Width.Value <= 225 ? 294 : 225);
                BuildInfo();
            });
            collapse.MinHeight = 25; collapse.Margin = new Thickness(0);
            DockPanel.SetDock(collapse, Dock.Right); header.Children.Add(collapse);
            header.Children.Add(Theme.Text("動画と保存先", 15, Theme.Ink, true)); info.Children.Add(header);

            var badge = new StackPanel();
            if (!compactLayout) badge.Children.Add(Theme.Text("元動画", 10, Theme.Muted, true));
            badge.Children.Add(Theme.Wrap(P.Source.Demo ? "光学ステージ（架空）" : Path.GetFileName(P.Source.Path), 13, Theme.Ink));
            badge.Children.Add(Theme.Text(Theme.Clock(P.Source.Duration) + "   /   " + P.Source.Width + " × " + P.Source.Height, 11, Theme.Muted));
            var card = Theme.Box(badge, Theme.Soft, new Thickness(9, compactLayout ? 3 : 6, 8, compactLayout ? 3 : 7), 6);
            card.Margin = new Thickness(0, 0, 0, 4);
            info.Children.Add(card);
            var edit = new StackPanel();
            if (compactLayout)
                edit.Children.Add(Theme.Text("完成予定   " + Theme.Clock(Map.Duration, true), 15, Theme.Teal, true));
            else
            {
                edit.Children.Add(Theme.Text("完成予定", 10, Theme.Teal, true));
                edit.Children.Add(Theme.Text(Theme.Clock(Map.Duration, true), 21, Theme.Teal, true));
            }
            edit.Children.Add(Theme.Wrap(P.Clips.Count + " 区間を採用  •  " + (P.Sound ? "音声あり" : "音声なし"), 11, Theme.Teal));
            var summary = Theme.Box(edit, Theme.Mint, new Thickness(9, compactLayout ? 3 : 5, 8, compactLayout ? 3 : 6), 6);
            summary.Margin = new Thickness(0, 0, 0, 5);
            info.Children.Add(summary);

            Section("元動画の詳細", detailOpen, () => { detailOpen = !detailOpen; BuildInfo(); }, () =>
            {
                Pair("サイズ", P.Source.Demo ? "架空映像" : SizeText(P.Source.Bytes));
                Pair("フレームレート", P.Source.Fps.ToString("0.###") + " fps" + (P.Source.EstimatedRate ? "（仮）" : ""));
                Pair("元動画の音声", P.Source.Audio ? "あり" : "なし");
                if (mediaNotice != null) Pair("映像", mediaNotice);
            });
            Section("保存先", locationsOpen, () => { locationsOpen = !locationsOpen; BuildInfo(); }, () =>
            {
                Pair("元動画", P.Source.Demo ? "アプリ内の架空サンプル" : P.Source.Path);
                if (!P.Source.Demo) InfoButton("動画を探す", Relink);
                Pair("編集ファイル", projectPath ?? "未保存");
                Pair("作業フォルダ", Work); InfoButton("作業フォルダを選ぶ", () => PickFolder(false));
                Pair("プロキシ", ProxyFolder);
                Pair("完成動画", Output); InfoButton("出力先を選ぶ", () => PickFolder(true));
            });
            Section("空き容量・プロキシ", spaceOpen, () => { spaceOpen = !spaceOpen; BuildInfo(); }, () =>
            {
                Pair("作業先の空き", FreeText(Work));
                Pair("出力先の空き", FreeText(Output));
                Pair("プロキシ使用容量", "0 B（試作）");
                Pair("推定出力サイズ", "約 " + SizeText(Estimate(P.Quality)));
                var free = Free(Output);
                if (free.HasValue && free.Value < Estimate(P.Quality)) Pair("⚠ 容量不足", "出力先の空きが足りません。");
                InfoButton("プロキシ管理（模擬）", ShowProxy);
            });
            int warnings = P.Captions.Count(c => Map.Warning(c) != null);
            if (warnings > 0) Section("⚠ テロップの確認  " + warnings + " 件", warningsOpen, () =>
            {
                warningsOpen = !warningsOpen; BuildInfo();
            }, () =>
            {
                foreach (var c in P.Captions.Where(c => Map.Warning(c) != null))
                {
                    Pair("テロップ " + c.Track + " · " + CaptionLabel(c.Kind), Map.Warning(c));
                    InfoButton("このテロップを選ぶ", () => Select(c.Id, false));
                }
            });
            if (P.AutoSave && !string.IsNullOrEmpty(autoStatus))
            {
                var note = Theme.Wrap(autoStatus, 11, Theme.Muted); note.Margin = new Thickness(2, 5, 0, 0); info.Children.Add(note);
            }
        }

        void Section(string label, bool expanded, Action toggle, Action content)
        {
            var button = Theme.Button((expanded ? "▾  " : "▸  ") + label, toggle);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.MinHeight = 25; button.Padding = new Thickness(7, 1, 7, 1);
            button.Margin = new Thickness(0, 2, 0, 0);
            info.Children.Add(button);
            if (expanded) content();
        }
        void Pair(string label, string value)
        {
            var item = new StackPanel { Margin = new Thickness(4, 4, 0, 4) };
            item.Children.Add(Theme.Text(label, 11, Theme.Teal, true));
            item.Children.Add(Theme.Wrap(value ?? "未指定", 11, Theme.Muted)); info.Children.Add(item);
        }
        void InfoButton(string label, Action action)
        {
            var b = Theme.Button(label, action); b.HorizontalAlignment = HorizontalAlignment.Left;
            b.MinHeight = 25; b.Margin = new Thickness(4, 0, 0, 4); info.Children.Add(b);
        }
        void PickFolder(bool output)
        {
            using (var dialog = new Forms.FolderBrowserDialog { Description = output ? "完成動画の出力先を選ぶ" : "作業フォルダを選ぶ",
                SelectedPath = Directory.Exists(output ? Output : Work) ? (output ? Output : Work) : AppFolder,
                ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                try
                {
                    var local = Files.Local(dialog.SelectedPath, AppFolder);
                    Change(() => { if (output) P.OutputFolder = local; else P.WorkFolder = local; });
                    autoJson = null; Say(output ? "出力先を設定しました" : "作業フォルダを設定しました");
                }
                catch (Exception e) { if (!UserError(e)) throw; Say(e.Message); }
            }
        }
        static long? Free(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Files.Local(path, AppFolder))).AvailableFreeSpace; }
            catch (Exception e) { if (!UserError(e)) throw; return null; }
        }
        static string FreeText(string path) { var n = Free(path); return n.HasValue ? SizeText(n.Value) : "取得できません"; }
        static string SizeText(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024.0).ToString("0.#") + " KiB";
            if (bytes < 1073741824) return (bytes / 1048576.0).ToString("0.#") + " MiB";
            return (bytes / 1073741824.0).ToString("0.#") + " GiB";
        }
        long Estimate(string quality)
        {
            double bits = quality == "軽量" ? 2000000 : quality == "標準" ? 4000000 : quality == "高画質" ? 8000000 :
                P.Source.Demo ? 12000000 : P.Source.Bytes * 8 / P.Source.Duration;
            return (long)(Map.Duration * (bits + (P.Sound && P.Source.Audio ? 128000 : 0)) / 8);
        }
        string FormatText(string quality)
        {
            int w = quality == "高画質" ? 1920 : quality == "標準" ? 1280 : quality == "軽量" ? 960 : P.Source.Width;
            int h = quality == "高画質" ? 1080 : quality == "標準" ? 720 : quality == "軽量" ? 540 : P.Source.Height;
            double scale = Math.Min(1, Math.Min(w / (double)P.Source.Width, h / (double)P.Source.Height));
            double fps = quality == "元画質優先" ? P.Source.Fps : Math.Min(P.Source.Fps, 30);
            return ((int)Math.Floor(P.Source.Width * scale)) + " × " + ((int)Math.Floor(P.Source.Height * scale)) +
                "  /  " + fps.ToString("0.##") + " fps";
        }
        void ShowExport()
        {
            var sheet = new Sheet(this, "書き出し設定", "UI試作版です。完成動画は生成されません。画質・音声・出力先を確認して設定を保存できます。");
            var quality = sheet.Choice("画質", new[] { "元画質優先", "高画質", "標準", "軽量" }, P.Quality);
            var sound = sheet.Choice("音声", new[] { "なし", "あり" }, P.Sound ? "あり" : "なし");
            sheet.Add("完成予定", Theme.Text(Theme.Clock(Map.Duration, true), 15, Theme.Teal, true));
            sheet.Add("形式", Theme.Text("MP4（H.264）予定", 12, Theme.Ink));
            var details = Theme.Wrap("", 12, Theme.Muted); sheet.Add("解像度と容量の目安", details);
            var folder = Theme.Wrap(Output, 11, Theme.Muted); sheet.Add("出力先", folder);
            var pick = Theme.Button("出力先を選ぶ", () => { PickFolder(true); folder.Text = Output; });
            sheet.Add("", pick);
            Action update = () =>
            {
                var q = (string)quality.SelectedItem;
                var estimate = Estimate(q) + (long)(Map.Duration * (P.Source.Audio ?
                    ((sound.SelectedIndex == 1 ? 1 : 0) - (P.Sound ? 1 : 0)) * 16000 : 0));
                var free = Free(Output);
                details.Text = FormatText(q) + "\n約 " + SizeText(estimate) +
                    (free.HasValue && free.Value < estimate ? "  ⚠ 空き容量不足" : "");
            };
            quality.SelectionChanged += delegate { update(); }; sound.SelectionChanged += delegate { update(); };
            pick.Click += delegate { update(); }; update();
            sheet.Open("設定を反映", () =>
            {
                Change(() => { P.Quality = (string)quality.SelectedItem; P.Sound = sound.SelectedIndex == 1; });
                Say("書き出し設定を反映しました（試作）");
            });
        }
        void ShowProxy()
        {
            var sheet = new Sheet(this, "プロキシ管理（模擬）", "低解像度の編集用コピーを作る画面です。ここでは進捗だけを試せます。");
            sheet.Add("保存予定の場所", Theme.Wrap(ProxyFolder, 11, Theme.Muted));
            var progress = new ProgressBar { Width = 195, Height = 14, Minimum = 0, Maximum = 100 };
            var state = Theme.Text("", 11, Theme.Muted);
            sheet.Add("模擬進捗", Theme.Row(progress, state));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            Action redraw = () =>
            {
                progress.Value = proxyPercent;
                state.Text = "   " + proxyPercent + "%";
                if (proxyPercent == 100) { proxyRunning = false; timer.Stop(); state.Text += "（模擬完了）"; }
            };
            timer.Tick += delegate { if (proxyRunning) proxyPercent = Math.Min(100, proxyPercent + 2); redraw(); };
            sheet.Add("操作", Theme.Row(Theme.Button("開始・再開", () => { if (proxyPercent == 100) proxyPercent = 0; proxyRunning = true; timer.Start(); redraw(); }),
                Theme.Button("中止", () => { proxyRunning = false; timer.Stop(); state.Text += "（中止）"; }),
                Theme.Button("リセット", () => { proxyRunning = false; timer.Stop(); proxyPercent = 0; redraw(); })));
            redraw(); if (proxyRunning) timer.Start();
            sheet.Open("", null, "閉じる"); timer.Stop(); proxyRunning = false;
            Say("プロキシの模擬進捗  " + proxyPercent + "%");
        }
    }
}

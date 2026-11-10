using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Kirihari
{
    internal sealed class SetupWindow : Window
    {
        readonly string home;
        readonly TextBlock framework = Theme.Wrap(""), webview = Theme.Wrap(""), ffmpeg = Theme.Wrap("");
        readonly TextBlock status = Theme.Wrap("", 13), intro;
        readonly Button prepare, start, choose, refresh;
        readonly ProgressBar progress = new ProgressBar { Height = 5, Margin = new Thickness(0, 14, 0, 10), Visibility = Visibility.Collapsed };
        CancellationTokenSource cancellation;
        bool busy, reopenRequired;
        internal bool Accepted;

        internal SetupWindow(string home)
        {
            this.home = home;
            Title = "KIRIHARI - はじめの準備";
            AppIcon.Apply(this);
            Width = 650; Height = Math.Min(670, SystemParameters.WorkArea.Height * .94); MinWidth = 520; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.Background; FontFamily = Theme.Font;
            var root = new StackPanel { Margin = new Thickness(28) };
            Content = new ScrollViewer { Content = root, Background = Theme.Background, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(Theme.Text("KIRIHARIへようこそ", 25, Theme.Teal, true));
            intro = Theme.Wrap("最初の1回だけ、動画編集に必要な部品を準備します。\n入っている部品はそのまま使えます。", 14);
            intro.Margin = new Thickness(0, 10, 0, 20); root.Children.Add(intro);
            AddRow(root, "1  アプリを動かす部品", ".NET Framework 4.8以降", framework);
            AddRow(root, "2  編集画面を表示する部品", "Microsoft WebView2", webview);
            AddRow(root, "3  動画を読み込み・保存する部品", "FFmpeg", ffmpeg);
            var note = Theme.Wrap("「必要な部品を準備」を押すと、MicrosoftとGyanの配布元から不足分をダウンロードします。使用許諾やWindowsの確認画面が出たら、内容を確認して進めてください。", 12, Theme.Muted);
            note.Margin = new Thickness(0, 12, 0, 12); root.Children.Add(note);
            prepare = Theme.Button("必要な部品を準備", async () => await Prepare(), true);
            start = Theme.Button("KIRIHARIを始める", Finish, true);
            var actions = new WrapPanel(); actions.Children.Add(prepare); actions.Children.Add(start); root.Children.Add(actions);
            root.Children.Add(progress); root.Children.Add(status);
            choose = Theme.Button("すでにあるFFmpegを選ぶ", ChooseTool);
            refresh = Theme.Button("もう一度確認", Refresh);
            var more = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            more.Children.Add(choose); more.Children.Add(refresh);
            more.Children.Add(Theme.Button("手動で準備する方法", () => Safe(() => Setup.Open(Path.Combine(home, "docs", "SETUP.md")))));
            root.Children.Add(more);
            Closing += OnClosing;
            Loaded += delegate { Refresh(); };
        }
        static void AddRow(StackPanel root, string title, string name, TextBlock state)
        {
            var row = new StackPanel();
            row.Children.Add(Theme.Text(title, 14, Theme.Ink, true));
            var details = Theme.Wrap(name, 11, Theme.Muted); details.Margin = new Thickness(0, 3, 0, 4);
            row.Children.Add(details); row.Children.Add(state);
            var box = Theme.Box(row, Theme.Paper, new Thickness(13), 7);
            box.Margin = new Thickness(0, 0, 0, 8); root.Children.Add(box);
        }
        void Safe(Action action)
        {
            try { action(); }
            catch (Exception e) { status.Text = "準備を進められませんでした。\n" + e.Message; }
        }
        void Refresh()
        {
            Safe(() => {
                bool dotnet = Setup.FrameworkReady(Setup.FrameworkRelease()), web = Setup.WebViewReady();
                var tool = MediaTool.FindConfigured(home);
                UpdateState(dotnet, web, tool != null);
            });
        }
        internal void UpdateState(bool dotnet, bool web, bool hasTool)
        {
            framework.Text = dotnet ? "準備できています" : "準備が必要です";
            webview.Text = web ? "準備できています" : "導入または更新が必要です";
            ffmpeg.Text = hasTool ? "準備できています" : "準備が必要です（約110 MBのダウンロード）";
            bool ready = dotnet && web && hasTool;
            start.IsEnabled = !busy && (reopenRequired || ready);
            start.Content = reopenRequired ? "終了して開き直す" : "KIRIHARIを始める";
            prepare.IsEnabled = !busy && !reopenRequired && !ready;
            choose.IsEnabled = !busy && !reopenRequired;
            refresh.IsEnabled = !busy;
            if (!busy && !reopenRequired && ready) status.Text = "準備完了です。次回からこの画面は表示しません。";
            else if (status.Text == "準備完了です。次回からこの画面は表示しません。") status.Text = "必要な部品を準備してください。";
        }
        void ChooseTool()
        {
            Safe(() => {
                var picker = new OpenFileDialog { Title = "ffmpeg.exe を選択", Filter = "FFmpeg|ffmpeg.exe", CheckFileExists = true };
                if (picker.ShowDialog(this) != true) return;
                string folder = Path.GetDirectoryName(picker.FileName);
                Setup.SaveTool(home, folder);
                status.Text = "FFmpegを指定しました。"; Refresh();
            });
        }
        void Report(string message) { Dispatcher.Invoke(new Action(() => status.Text = message)); }
        async Task Prepare()
        {
            if (busy) return;
            busy = true; cancellation = new CancellationTokenSource();
            progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true;
            Refresh();
            try
            {
                if (!Setup.FrameworkReady(Setup.FrameworkRelease()))
                {
                    Report(".NET Frameworkを準備します…");
                    int code = await Setup.InstallMicrosoft(home, true, Report, cancellation.Token);
                    if (code == 3010 || code == 1641)
                    {
                        reopenRequired = true;
                        Report(".NETの準備を続けるにはPCの再起動が必要です。作業を保存して、Windowsから再起動してください。");
                        return;
                    }
                    if (code != 0 || !Setup.FrameworkReady(Setup.FrameworkRelease()))
                        throw new IOException(".NETの導入が完了していません（終了コード " + code + "）。中止した場合はもう一度お試しください。");
                    reopenRequired = true;
                    Report(".NETを導入しました。いったん終了し、KIRIHARI.exeを開き直してください。残りの準備を続けられます。");
                    return;
                }
                cancellation.Token.ThrowIfCancellationRequested();
                if (!Setup.WebViewReady())
                {
                    Report("WebView2を準備します…");
                    int code = await Setup.InstallMicrosoft(home, false, Report, cancellation.Token);
                    if (code == 3010 || code == 1641)
                    {
                        reopenRequired = true;
                        Report("WebView2の準備を続けるにはPCの再起動が必要です。Windowsから再起動してください。"); return;
                    }
                    if (code != 0 || !Setup.WebViewReady())
                        throw new IOException("WebView2の導入が完了していません（終了コード " + code + "）。もう一度お試しください。");
                }
                cancellation.Token.ThrowIfCancellationRequested();
                if (MediaTool.FindConfigured(home) == null)
                    await Setup.InstallFfmpeg(home, Report, cancellation.Token);
                Report("準備ができました。");
            }
            catch (OperationCanceledException) { Report("準備を中止しました。あとから再開できます。"); }
            catch (Exception e) { Report("準備を完了できませんでした。接続を確認して、もう一度お試しください。\n" + e.Message); }
            finally
            {
                busy = false; cancellation.Dispose(); cancellation = null;
                progress.IsIndeterminate = false; progress.Visibility = Visibility.Collapsed;
                Refresh();
            }
        }
        void Finish()
        {
            if (reopenRequired) { Close(); return; }
            Safe(() => { Setup.Complete(home); Accepted = true; Close(); });
        }
        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!busy) return;
            e.Cancel = true;
            cancellation.Cancel();
            status.Text = "準備を中止しています。Microsoftのインストール画面が開いている場合は、その画面で中止してください。";
        }
    }
}

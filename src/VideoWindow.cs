using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace Kirihari
{
    internal sealed partial class VideoWindow : Window
    {
        const string AppHost = "kirihari.local", MediaHost = "kirihari-media.local";
        const string Page = "https://" + AppHost + "/index.html";
        const string Videos = "動画|*.mp4;*.mov;*.mkv;*.avi;*.m4v;*.webm;*.wmv;*.mts;*.m2ts|すべてのファイル|*.*";
        const int DwmCaptionColor = 35, DwmTextColor = 36;
        readonly string home = AppDomain.CurrentDomain.BaseDirectory;
        readonly WebView2 browser = new WebView2();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly string qaFolder;
        CancellationTokenSource job;
        MediaTool tool;
        VideoSource source;
        string work, projectFile;
        string pendingProjectName;
        string qaOpenedManual;
        string lastExport, qaOpenedExport;
        readonly Dictionary<string, VideoSource> assets = new Dictionary<string, VideoSource>();
        readonly Dictionary<string, string> mediaFiles = new Dictionary<string, string>();
        bool editorReady, closeAllowed;

        internal VideoWindow(string qaFolder = null)
        {
            this.qaFolder = qaFolder;
            Title = "KIRIHARI";
            AppIcon.Apply(this);
            Width = Math.Min(1440, SystemParameters.WorkArea.Width * .94);
            Height = Math.Min(1000, SystemParameters.WorkArea.Height * .93);
            MinWidth = 550; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.Brush("#11151D");
            Content = browser;
            if (qaFolder != null) { ShowInTaskbar = false; ShowActivated = false; Opacity = 0.01; }
            SourceInitialized += delegate { SetTitleBarTheme("#191d26", "#f0f2f6"); };
            Loaded += async delegate { await Initialize(); };
            Closing += ConfirmClose;
            Closed += delegate { MarkSessionClean(); lifetime.Cancel(); browser.Dispose(); };
        }

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref uint value, int size);

        static bool TryCaptionColor(string hex, out uint color)
        {
            color = 0;
            uint rgb;
            if (hex == null || hex.Length != 7 || hex[0] != '#' ||
                !uint.TryParse(hex.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb)) return false;
            color = ((rgb & 0xff) << 16) | (rgb & 0xff00) | (rgb >> 16);
            return true;
        }

        void SetTitleBarTheme(string background, string foreground)
        {
            uint caption, text;
            if (!TryCaptionColor(background, out caption) || !TryCaptionColor(foreground, out text)) return;
            var window = new WindowInteropHelper(this).Handle;
            // Older versions of Windows keep their system title bar when caption colors are unsupported.
            if (window != IntPtr.Zero && DwmSetWindowAttribute(window, DwmCaptionColor, ref caption, sizeof(uint)) == 0 &&
                DwmSetWindowAttribute(window, DwmTextColor, ref text, sizeof(uint)) == 0 && qaFolder != null)
            {
                qaTitleBarBackground = caption;
                qaTitleBarForeground = text;
            }
        }

        void ConfirmClose(object sender, CancelEventArgs e)
        {
            if (closeAllowed || !editorReady) return;
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (lifetime.IsCancellationRequested) return;
                try
                {
                    var result = await browser.CoreWebView2.ExecuteScriptAsync(
                        "typeof window.kirihariRequestClose === 'function' && (window.kirihariRequestClose(), true)");
                    if (result == "true" || lifetime.IsCancellationRequested) return;
                }
                catch (Exception)
                {
                    if (lifetime.IsCancellationRequested) return;
                }
                if (MessageBox.Show(this, "編集内容の保存状況を確認できませんでした。保存せずに閉じますか？", "KIRIHARI",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                closeAllowed = true;
                Close();
            }));
        }

        async Task Initialize()
        {
            try
            {
                tool = await Task.Run(() => qaFolder == null ? MediaTool.FindConfigured(home) : MediaTool.Find(home));
                string arguments = "--disable-background-networking --disable-component-update --disable-sync";
                if (qaFolder != null) arguments += " --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --disable-background-timer-throttling";
                var options = new CoreWebView2EnvironmentOptions(arguments);
                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(qaFolder ?? Path.Combine(home, "data"), "webview"), options);
                await browser.EnsureCoreWebView2Async(env);
                var core = browser.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsStatusBarEnabled = false;
                core.SetVirtualHostNameToFolderMapping(AppHost, Path.Combine(home, "ui"), CoreWebView2HostResourceAccessKind.Deny);
                core.AddWebResourceRequestedFilter("https://" + MediaHost + "/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += ServeMedia;
                core.NavigationStarting += (sender, e) => { if (e.Uri != Page && !(closeAllowed && e.Uri == "about:blank")) e.Cancel = true; };
                core.NewWindowRequested += (sender, e) => { e.Handled = true; };
                core.WebMessageReceived += Receive;
                if (qaFolder != null) await core.AddScriptToExecuteOnDocumentCreatedAsync(@"window.qaErrors=[];
                    addEventListener('error',e=>qaErrors.push(e.message));
                    addEventListener('unhandledrejection',e=>qaErrors.push(String(e.reason)));
                    new MutationObserver(() => {
                        if (!window.qaManualSwitch)
                            document.querySelector('[data-close-edit-dialog][data-purpose=switch][open] [data-close-edit-discard]')?.click();
                    }).observe(document,{subtree:true,attributes:true,attributeFilter:['open']});");
                if (qaFolder != null) core.NavigationCompleted += async (sender, e) =>
                {
                    if (closeAllowed || windowQaStarted) return;
                    windowQaStarted = true;
                    await VerifyWindow(e.IsSuccess);
                };
                core.Navigate(Page);
            }
            catch (Exception e)
            {
                if (qaFolder != null) { FailWindowTest(e); return; }
                MessageBox.Show(this, "編集画面を起動できません。Microsoft Edge WebView2 ランタイムと、アプリのフォルダへの書き込み権限を確認してください。\n\n" +
                    e.Message, "KIRIHARI", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        async void Receive(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (e.Source != Page) return;
            int id = 0;
            string command = null;
            try
            {
                var input = MediaTool.Json().Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
                id = Convert.ToInt32(input["id"]);
                command = (string)input["command"];
                TraceQaSave(command, id, "start");
                if (command == "dropMedia")
                    input["files"] = e.AdditionalObjects.OfType<CoreWebView2File>().Select(file => file.Path).ToArray();
                object value = await Dispatch(command, input);
                TraceQaSave(command, id, "ok");
                Post(new { id = id, ok = true, value = value });
            }
            catch (OperationCanceledException) { Post(new { id = id, ok = false, error = "中止しました。" }); }
            catch (Exception ex)
            {
                TraceQaSave(command, id, ex.GetType().Name + " 0x" + ex.HResult.ToString("X8") + " " + ex.Message);
                Post(new { id = id, ok = false, error = ex.Message });
            }
        }

        void Post(object message)
        {
            if (!lifetime.IsCancellationRequested && browser.CoreWebView2 != null)
                browser.CoreWebView2.PostWebMessageAsJson(MediaTool.Json().Serialize(message));
        }

        static string Text(Dictionary<string, object> input, string key)
        {
            object value;
            return input.TryGetValue(key, out value) ? value as string : null;
        }

        async Task<object> Dispatch(string command, Dictionary<string, object> input)
        {
            if (command == "closeWindow")
            {
                closeAllowed = true;
                await CancelProxyForClose();
                var core = browser.CoreWebView2;
                var unloaded = new TaskCompletionSource<bool>();
                EventHandler<CoreWebView2NavigationCompletedEventArgs> onUnloaded = (sender, e) => unloaded.TrySetResult(e.IsSuccess);
                core.NavigationCompleted += onUnloaded;
                try
                {
                    // Release outstanding large media requests before disposing the control.
                    core.Navigate("about:blank");
                    await Task.WhenAny(unloaded.Task, Task.Delay(2000));
                }
                finally { core.NavigationCompleted -= onUnloaded; }
                await Dispatcher.InvokeAsync(new Action(Close));
                return null;
            }
            if (command == "titleBarTheme")
            {
                SetTitleBarTheme(Text(input, "background"), Text(input, "foreground"));
                return null;
            }
            if (command == "proxyCancel")
            {
                if (proxyJob != null && Text(input, "token") == proxyToken) proxyJob.Cancel();
                return null;
            }
            if (command == "proxyCatalog") return ProxyCatalog(GetAsset(Text(input, "asset")));
            if (command == "proxyBytes") return await Task.Run(() => ProxyBytes());
            if (command == "cancel") { if (job != null) job.Cancel(); return null; }
            if (command == "confirmRelink")
            {
                string choice = Text(input, "choice");
                if (relinkDecision != null && Text(input, "token") == relinkToken &&
                    (choice == "use" || choice == "retry" || choice == "cancel"))
                    relinkDecision.TrySetResult(choice);
                return null;
            }
            if (job != null) throw new IOException("動画処理が終わるまでお待ちください。");
            switch (command)
            {
                case "hello":
                    editorReady = true;
                    return new { output = DefaultFolder("output"), tool = tool != null, projects = RecentProjects(), recoveries = RecentRecoveries(), qa = qaFolder != null };
                case "folderChoices":
                    return FolderChoices(Text(input, "kind"));
                case "configureFolder":
                    return ConfigureFolder(Text(input, "kind"), Text(input, "action") == "reset");
                case "outputFolder":
                    string outputMode = Text(input, "mode");
                    return outputMode == "browse"
                        ? PickFolder("完成動画の出力先を選ぶ", Text(input, "folder"))
                        : InitialFolder("output", outputMode);
                case "recoverProject":
                    return await RecoverProject(Text(input, "recoveryId"));
                case "dismissRecovery":
                    MarkSessionClean(RecoveryFolder(Text(input, "recoveryId")));
                    return RecentRecoveries();
                case "openManual":
                    string manual = Manuals.Resolve(home, Text(input, "kind"));
                    if (qaFolder == null) Manuals.Open(manual);
                    else qaOpenedManual = manual;
                    return true;
                case "chooseTool":
                    var pickTool = new OpenFileDialog { Title = "ffmpeg.exe を選択", Filter = "FFmpeg|ffmpeg.exe", CheckFileExists = true };
                    if (pickTool.ShowDialog(this) != true) return null;
                    string location = Path.GetDirectoryName(Files.Local(pickTool.FileName, home));
                    tool = await Task.Run(() => Setup.SaveTool(home, location, lifetime.Token));
                    return true;
                case "setup":
                    var setup = new SetupWindow(home) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                    setup.ShowDialog();
                    tool = MediaTool.FindConfigured(home);
                    return tool != null;
                case "newProject":
                    MarkSessionClean();
                    assets.Clear(); mediaFiles.Clear(); source = null; projectFile = null; archiveFolder = null; pendingProjectName = null;
                    return null;
                case "pickMedia":
                    var mediaPicker = new OpenFileDialog { Title = "メディアプールに追加", Filter = Videos, CheckFileExists = true, Multiselect = true };
                    SetPickerFolder(mediaPicker, "media", Text(input, "mode"));
                    if (qaFolder != null) return await AddMedia(new[] { Path.Combine(Program.QaSamples, "cat-motion.mp4"), Path.Combine(Program.QaSamples, "optical-motion.mp4") });
                    return mediaPicker.ShowDialog(this) == true ? await AddMedia(mediaPicker.FileNames) : null;
                case "dropMedia":
                    return await AddMedia((string[])input["files"]);
                case "startProject":
                    var chosen = GetAsset(Text(input, "asset"));
                    string name = (Text(input, "name") ?? "").Trim();
                    if (name.Length == 0) name = "新規プロジェクト";
                    string safe = SafeProjectName(name);
                    var nextSession = StartSession(chosen);
                    pendingProjectName = safe;
                    return nextSession;
                case "recentProject":
                    string id = Text(input, "projectId");
                    if (string.IsNullOrWhiteSpace(id) || id != Path.GetFileName(id) || Path.GetExtension(id) != ".kirihari")
                        throw new IOException("プロジェクトを選んでください。");
                    return await OpenProject(Path.Combine(ProjectsRoot, id));
                case "openVideo":
                    var video = new OpenFileDialog { Title = "元動画を選ぶ", Filter = Videos, CheckFileExists = true };
                    SetPickerFolder(video, "media", Text(input, "mode"));
                    return video.ShowDialog(this) == true ? await OpenVideo(video.FileName) : null;
                case "openPractice":
                    string practice = Path.Combine(SampleFolder, "cat-motion-short.mp4");
                    if (!File.Exists(practice)) throw new IOException("練習動画が見つかりません。配布ZIPをすべて展開してください。");
                    return await OpenVideo(practice, false);
                case "proxyBuild":
                    return await BuildProxy(GetAsset(Text(input, "asset")), ProxyProfile.Get(Text(input, "level")),
                        Text(input, "action") == "replace", Text(input, "token"));
                case "proxyDelete":
                    return await DeleteProxy(GetAsset(Text(input, "asset")), ProxyProfile.Get(Text(input, "level")));
                case "overlay":
                    RequireSource();
                    var png = Convert.FromBase64String(Text(input, "png") ?? "");
                    if (png.Length < 8 || png.Length > 16000000 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71)
                        throw new IOException("書き出し用スライド画像が無効です。");
                    Directory.CreateDirectory(work);
                    File.WriteAllBytes(MediaTool.ImageFile(work, Text(input, "slide")), png);
                    return null;
                case "exportInfo":
                    RequireSource();
                    return MediaTool.DescribeExport(source, MediaTool.Json().ConvertToType<VideoExport>(input["settings"]), assets);
                case "export":
                    RequireSource();
                    var settings = MediaTool.Json().ConvertToType<VideoExport>(input["settings"]);
                    using (job = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        try
                        {
                            lastExport = null;
                            lastExport = await Task.Run(() => tool.Export(source, settings, work, job.Token,
                                fraction => Dispatcher.BeginInvoke(new Action(() => Post(new { progress = fraction }))), assets));
                            RememberFolder("output", lastExport);
                            return lastExport;
                        }
                        finally { job = null; }
                    }
                case "openExport":
                    if (lastExport == null || !File.Exists(lastExport)) throw new IOException("完成動画が見つかりません。書き出しを完了してから開いてください。");
                    string exportKind = Text(input, "kind");
                    if (exportKind != "video" && exportKind != "folder") throw new IOException("開く対象を確認できません。");
                    string destinationPath = exportKind == "video" ? lastExport : Path.GetDirectoryName(lastExport);
                    if (qaFolder == null) Setup.Open(destinationPath);
                    else qaOpenedExport = destinationPath;
                    return true;
                case "save":
                    RequireSource();
                    string saveMode = Text(input, "mode");
                    if (projectFile != null && saveMode == null)
                    {
                        string currentPath = projectFile;
                        await SaveProjectText(currentPath, ProjectJson(input, source.path));
                        RememberFolder("save", currentPath);
                        return currentPath;
                    }
                    if (pendingProjectName != null && saveMode == null)
                    {
                        Directory.CreateDirectory(ProjectsRoot);
                        string managedPath = MediaTool.Available(ProjectsRoot, pendingProjectName, ".kirihari");
                        await SaveProjectText(managedPath, ProjectJson(input, source.path), overwrite: false);
                        projectFile = managedPath;
                        pendingProjectName = null;
                        RememberFolder("save", managedPath);
                        return managedPath;
                    }
                    var file = new SaveFileDialog { Title = "編集プロジェクトを保存", Filter = "KIRIHARI|*.kirihari",
                        FileName = projectFile == null ? Path.GetFileNameWithoutExtension(source.path) : Path.GetFileNameWithoutExtension(projectFile),
                        DefaultExt = ".kirihari", AddExtension = true };
                    SetPickerFolder(file, "save", saveMode ?? "project");
                    if (qaFolder != null)
                    {
                        string saved = saveMode == null ? Path.Combine(qaFolder, "確認.kirihari")
                            : Path.Combine(file.InitialDirectory, "場所の確認.kirihari");
                        await SaveProjectText(saved, ProjectJson(input, source.path));
                        RememberFolder("save", saved);
                        if (saveMode != null) { projectFile = saved; pendingProjectName = null; }
                        return saved;
                    }
                    if (file.ShowDialog(this) != true) return null;
                    string savedPath = Files.Local(file.FileName, home);
                    await SaveProjectText(savedPath, ProjectJson(input, source.path));
                    projectFile = savedPath;
                    pendingProjectName = null;
                    RememberFolder("save", savedPath);
                    return savedPath;
                case "draft":
                    RequireSource();
                    Directory.CreateDirectory(work);
                    SaveText(Path.Combine(work, "自動保存.kirihari"), ProjectJson(input, source.path));
                    return null;
                case "openProject":
                    var project = new OpenFileDialog { Title = "編集プロジェクトを開く", Filter = "KIRIHARI|*.kirihari;*.zip" };
                    SetPickerFolder(project, "project", Text(input, "mode"));
                    if (qaFolder != null) return await OpenProject(qaProjectToOpen ?? Path.Combine(qaFolder, "確認.zip"));
                    return project.ShowDialog(this) == true ? await OpenProject(project.FileName) : null;
                case "zip":
                    RequireSource();
                    var zip = new SaveFileDialog { Title = "元動画を含むプロジェクトをZIPで保存",
                        Filter = "ZIP|*.zip", DefaultExt = ".zip", AddExtension = true,
                        FileName = DateTime.Today.ToString("yyyyMMdd") + "_" + Path.GetFileNameWithoutExtension(source.path) };
                    SetPickerFolder(zip, "save", Text(input, "mode"));
                    if (qaFolder == null && zip.ShowDialog(this) != true) return null;
                    string destination = qaFolder == null ? Files.Local(zip.FileName, home) : Path.Combine(qaFolder, "確認.zip");
                    if (!Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("素材を含むZIPは、拡張子を .zip にして保存してください。");
                    var entries = ArchiveEntries(EditingAssets((Dictionary<string, object>)input["project"]));
                    string contents = ProjectJson(input, entries[source.id], entries);
                    using (job = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        try { await Task.Run(() => CreateArchive(destination, entries, contents, job.Token)); }
                        finally { job = null; }
                    }
                    RememberFolder("save", destination);
                    return destination;
                case "deleteWork":
                    RequireSource();
                    string expected = Path.GetFullPath(WorkRoot) + Path.DirectorySeparatorChar;
                    if (!Path.GetFullPath(work).StartsWith(expected, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("作業フォルダを確認できません。");
                    if (Directory.Exists(work))
                    {
                        if ((File.GetAttributes(work) & FileAttributes.ReparsePoint) != 0) throw new IOException("作業フォルダはリンクです。");
                        await Task.Run(() => MediaTool.DeleteWork(work));
                    }
                    return source.url;
                default: throw new IOException("不明な操作です。");
            }
        }

        void RequireSource()
        {
            if (tool == null) throw new IOException("設定メニューからFFmpegを指定してください。");
            if (source == null) throw new IOException("最初に動画を開いてください。");
        }
        string WorkRoot { get { return Path.Combine(qaFolder ?? Path.Combine(home, "data"), "work"); } }
        string ProjectsRoot { get { return Path.Combine(qaFolder ?? Path.Combine(home, "data"), "projects"); } }
        object RecentProjects()
        {
            if (!Directory.Exists(ProjectsRoot)) return new object[0];
            return new DirectoryInfo(ProjectsRoot).EnumerateFiles("*.kirihari").OrderByDescending(file => file.LastWriteTime)
                .Take(50).Select(file => new { id = file.Name, name = Path.GetFileNameWithoutExtension(file.Name),
                    updated = file.LastWriteTime.ToString("yyyy/MM/dd HH:mm") }).ToArray();
        }
        static string SafeProjectName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = new string(name.Take(60).Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
            return string.IsNullOrWhiteSpace(safe) ? "プロジェクト" : safe;
        }
        VideoSource GetAsset(string id)
        {
            VideoSource asset;
            if (id == null || !assets.TryGetValue(id, out asset)) throw new IOException("動画素材が見つかりません。");
            return asset;
        }
        async Task<object> AddMedia(string[] files)
        {
            if (tool == null) throw new IOException("FFmpegを指定してから素材を開いてください。");
            var added = new List<VideoSource>();
            var errors = new List<string>();
            using (job = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                try
                {
                    foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string path = Files.Local(file, home);
                            var existing = assets.Values.FirstOrDefault(asset => string.Equals(asset.path, path, StringComparison.OrdinalIgnoreCase));
                            if (existing != null) { added.Add(existing); continue; }
                            if (assets.Count >= 100) { errors.Add("メディアプールには100本まで登録できます。"); break; }
                            var item = await Task.Run(() => tool.Read(path, job.Token));
                            added.Add(RegisterAsset(item, Project.Id()));
                        }
                        catch (IOException error) { errors.Add(Path.GetFileName(file) + "  " + error.Message); }
                        catch (UnauthorizedAccessException error) { errors.Add(Path.GetFileName(file) + "  " + error.Message); }
                    }
                }
                finally { job = null; }
            }
            if (added.Count != 0) RememberFolder("media", added[0].path);
            return new { added = added, errors = errors };
        }
        async Task<object> OpenVideo(string file, bool remember = true)
        {
            if (tool == null) throw new IOException("設定メニューからFFmpegを指定してください。");
            string path = Files.Local(file, home);
            VideoSource next;
            using (job = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                try { next = await Task.Run(() => tool.Read(path, job.Token)); }
                finally { job = null; }
            }
            assets.Clear(); mediaFiles.Clear(); projectFile = null; archiveFolder = null;
            if (remember) RememberFolder("media", path);
            return StartSession(RegisterAsset(next, Project.Id()));
        }
        object StartSession(VideoSource next, Dictionary<string, VideoSource> loaded = null)
        {
            string name = DateTime.Today.ToString("yyyyMMdd") + "_" + Path.GetFileNameWithoutExtension(next.path);
            string nextWork = MediaTool.Available(WorkRoot, name, "");
            // Reserve this session's directory so another running instance uses its own files.
            for (int attempt = 0; ; attempt++)
            {
                Directory.CreateDirectory(nextWork);
                try
                {
                    using (File.Open(Path.Combine(nextWork, ".kirihari-session"), FileMode.CreateNew)) { }
                    break;
                }
                catch (IOException)
                {
                    if (attempt == 3) throw;
                    nextWork = Path.Combine(WorkRoot, name + "_" + Project.Id().Substring(0, 8));
                }
            }
            BeginRecoverySession(nextWork);
            MarkSessionClean();
            if (loaded != null)
            {
                assets.Clear(); mediaFiles.Clear();
                foreach (var asset in loaded) RegisterAsset(asset.Value, asset.Key);
            }
            pendingProjectName = null;
            lastExport = null;
            work = nextWork;
            source = next;
            return new { media = next, assets = assets.Values.ToArray(), work = work, size = MediaTool.OutputSize(next) };
        }
        VideoSource RegisterAsset(VideoSource asset, string id)
        {
            asset.id = id;
            asset.url = MapMedia(asset.path);
            asset.proxyKey = ProxyKey(asset.path);
            RefreshProxies(asset);
            assets.Add(id, asset);
            return asset;
        }
        string MapMedia(string path)
        {
            string url = "https://" + MediaHost + "/" + Project.Id() + Path.GetExtension(path).ToLowerInvariant();
            mediaFiles.Add(url, path);
            return url;
        }
        void ServeMedia(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var environment = browser.CoreWebView2.Environment;
            string mediaPath;
            if (!mediaFiles.TryGetValue(e.Request.Uri, out mediaPath) || e.Request.Method != "GET")
            {
                e.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            FileStream file = null;
            try
            {
                file = new FileStream(mediaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long start = 0, end = file.Length - 1, total = file.Length;
                bool partial = e.Request.Headers.Contains("Range");
                if (partial)
                {
                    string header = e.Request.Headers.GetHeader("Range");
                    var pair = header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) ? header.Substring(6).Split('-') : new string[0];
                    long parsedEnd;
                    if (pair.Length != 2 || !long.TryParse(pair[0], out start) || start < 0 || start >= total ||
                        (pair[1].Length != 0 && (!long.TryParse(pair[1], out parsedEnd) || parsedEnd < start)))
                    {
                        e.Response = environment.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", "Content-Range: bytes */" + total);
                        return;
                    }
                    if (long.TryParse(pair[1], out parsedEnd)) end = Math.Min(end, parsedEnd);
                }
                string mime;
                switch (Path.GetExtension(mediaPath).ToLowerInvariant())
                {
                    case ".mp4": case ".m4v": mime = "video/mp4"; break;
                    case ".mov": mime = "video/quicktime"; break;
                    case ".webm": mime = "video/webm"; break;
                    default: mime = "application/octet-stream"; break;
                }
                // WebView2 may buffer an intercepted response before handing it to the video decoder.
                // Serve large originals in bounded byte ranges so seeking never allocates the whole file.
                const long responseLimit = 8 * 1024 * 1024;
                if (end - start + 1 > responseLimit)
                {
                    end = start + responseLimit - 1;
                    partial = true;
                }
                long length = end - start + 1;
                string headers = "Content-Type: " + mime + "\r\nContent-Length: " + length.ToString(CultureInfo.InvariantCulture) +
                    "\r\nAccept-Ranges: bytes\r\nCache-Control: no-store\r\nCross-Origin-Resource-Policy: cross-origin";
                if (partial) headers += "\r\nContent-Range: bytes " + start + "-" + end + "/" + total;
                e.Response = environment.CreateWebResourceResponse(new MediaStream(file, start, length),
                    partial ? 206 : 200, partial ? "Partial Content" : "OK", headers);
                file = null;
            }
            catch (IOException) { e.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", ""); }
            catch (UnauthorizedAccessException) { e.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", ""); }
            finally { if (file != null) file.Dispose(); }
        }

        sealed class MediaStream : Stream
        {
            readonly FileStream file;
            readonly long offset, length;
            long position;
            public MediaStream(FileStream file, long offset, long length) { this.file = file; this.offset = offset; this.length = length; }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return length; } }
            public override long Position { get { return position; } set { Seek(value, SeekOrigin.Begin); } }
            public override int Read(byte[] buffer, int index, int count)
            {
                file.Position = offset + position;
                int read = file.Read(buffer, index, (int)Math.Min(count, Math.Max(0, length - position)));
                position += read;
                return read;
            }
            public override long Seek(long value, SeekOrigin origin)
            {
                long next = origin == SeekOrigin.Begin ? value : (origin == SeekOrigin.Current ? position : length) + value;
                if (next < 0) throw new IOException("Invalid media stream offset.");
                return position = next;
            }
            public override void Flush() { }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int index, int count) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { if (disposing) file.Dispose(); base.Dispose(disposing); }
        }
        string ProjectJson(Dictionary<string, object> input, string path, Dictionary<string, string> paths = null)
        {
            var project = (Dictionary<string, object>)input["project"];
            CheckProjectLimits(project);
            var included = paths == null ? EditingAssets(project) : paths.Keys.Select(GetAsset).ToArray();
            project["sourcePath"] = path;
            project["baseMediaId"] = source.id;
            project["assets"] = paths ?? included.ToDictionary(asset => asset.id, asset => asset.path);
            project["assetBytes"] = included.ToDictionary(asset => asset.id, asset => asset.bytes);
            project["assetFingerprints"] = included.ToDictionary(asset => asset.id, asset => asset.fingerprint);
            return MediaTool.Json().Serialize(project);
        }

        VideoSource[] EditingAssets(Dictionary<string, object> project)
        {
            object value;
            if (!project.TryGetValue("pool", out value)) return assets.Values.ToArray();
            var pool = MediaTool.Json().ConvertToType<string[]>(value);
            if (pool == null || !pool.Contains(source.id)) throw new IOException("編集内容の元動画が見つかりません。");
            return pool.Distinct().Select(GetAsset).ToArray();
        }
        void ProtectSource(string path)
        {
            if (assets.Values.Any(asset => string.Equals(path, asset.path, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("元動画とは別の名前を指定してください。");
        }
        async Task SaveProjectText(string path, string contents, bool overwrite = true)
        {
            const int sharingViolation = unchecked((int)0x80070020);
            const int lockViolation = unchecked((int)0x80070021);
            for (int retry = 0; ; retry++)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                try { SaveText(path, contents, overwrite); return; }
                catch (IOException ex)
                {
                    if (ex.HResult != sharingViolation && ex.HResult != lockViolation) throw;
                    if (qaFolder != null) qaSaveConflicts++;
                    if (retry == 3)
                        throw new IOException("保存先のファイルが使用中です。ファイルを閉じて再試行するか、別名で保存してください。", ex);
                }
                await Task.Delay(150 * (retry + 1), lifetime.Token);
            }
        }
        void SaveText(string path, string contents, bool overwrite = true)
        {
            ProtectSource(path);
            string temp = Path.Combine(Path.GetDirectoryName(path), "." + Project.Id() + ".tmp");
            try
            {
                File.WriteAllText(temp, contents, new UTF8Encoding(false));
                if (overwrite) Replace(temp, path);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        static void Replace(string temp, string path)
        {
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        string PickMissingAsset(string missing)
        {
            if (qaFolder != null)
            {
                string replacement;
                return qaRelinkPaths.TryGetValue(missing, out replacement) ? replacement : null;
            }
            var picker = new OpenFileDialog { Title = "見つからない素材を選び直す — " + Path.GetFileName(missing),
                Filter = Videos, CheckFileExists = true, FileName = Path.GetFileName(missing) };
            SetPickerFolder(picker, "media", null);
            return picker.ShowDialog(this) == true ? Files.Local(picker.FileName, home) : null;
        }

        static void ValidateRelinkedCuts(Dictionary<string, object> project, Dictionary<string, VideoSource> loaded)
        {
            string baseId = Text(project, "baseMediaId");
            double fps = loaded[baseId].fps;
            var json = MediaTool.Json();
            object saved;
            var originals = project.TryGetValue("originals", out saved) ? json.ConvertToType<string[]>(saved) : null;
            var offsets = new Dictionary<long, string>();
            long offset = 0;
            foreach (string id in (originals ?? new[] { baseId }).Take(2000))
            {
                VideoSource media;
                if (id == null || !loaded.TryGetValue(id, out media)) continue;
                offsets.Add(offset, id);
                offset += (long)Math.Max(1, Math.Round(media.duration * fps, MidpointRounding.AwayFromZero));
            }
            if (offsets.Count == 0) offsets.Add(0, baseId);
            if (!project.TryGetValue("ranges", out saved) || saved == null) return;
            foreach (var cut in json.ConvertToType<Dictionary<string, object>[]>(saved).Where(item => item != null).Take(2000))
            {
                string id = Text(cut, "mediaId") ?? baseId;
                object origin, inserted, start, end;
                long at = cut.TryGetValue("mediaOrigin", out origin) ? Convert.ToInt64(origin) : 0;
                string original;
                VideoSource media;
                if (!loaded.TryGetValue(id, out media) || !cut.TryGetValue("start", out start) || !cut.TryGetValue("end", out end)) continue;
                if (Convert.ToInt64(end) - at > Math.Max(1, Math.Round(media.duration * fps, MidpointRounding.AwayFromZero)) ||
                    (cut.TryGetValue("inserted", out inserted) && inserted is bool && !(bool)inserted &&
                        (!offsets.TryGetValue(at, out original) || original != id)))
                    throw new IOException("選んだ動画では保存済みのカットを再現できません。元と同じ動画を選び直してください。");
            }
        }
        static Dictionary<string, string> ProjectAssets(Dictionary<string, object> project)
        {
            object value;
            if (!project.TryGetValue("assets", out value))
            {
                string id = Project.Id();
                project["baseMediaId"] = id;
                return new Dictionary<string, string> { { id, Text(project, "sourcePath") } };
            }
            var raw = value as Dictionary<string, object>;
            if (raw == null || raw.Count == 0 || raw.Count > 100 || !raw.ContainsKey(Text(project, "baseMediaId") ?? ""))
                throw new IOException("編集ファイルの動画素材が無効です。");
            var paths = new Dictionary<string, string>();
            foreach (var asset in raw)
            {
                Guid id;
                string file = asset.Value as string;
                if (!Guid.TryParseExact(asset.Key, "N", out id) || string.IsNullOrWhiteSpace(file))
                    throw new IOException("編集ファイルの動画素材が無効です。");
                paths.Add(asset.Key, file);
            }
            return paths;
        }
        static Dictionary<string, object> ReadProject(string content)
        {
            var project = MediaTool.Json().Deserialize<Dictionary<string, object>>(content);
            string format = project == null ? null : Text(project, "format");
            if (format != "kirihari-video-1" && format != "kirihari-video-2" && format != "kirihari-video-3" && format != "kirihari-video-4")
                throw new IOException("この編集ファイル形式には対応していません。旧試作版は --classic で起動できます。");
            CheckProjectLimits(project);
            return project;
        }
    }
}

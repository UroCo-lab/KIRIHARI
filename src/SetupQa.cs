using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kirihari
{
    internal static class SetupQa
    {
        internal static void RunDownload(string folder)
        {
            Directory.CreateDirectory(folder);
            string home = Path.Combine(folder, "app-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(home);
            Action<string> log = message => File.AppendAllText(Path.Combine(folder, "download-progress.txt"), message + Environment.NewLine);
            Task.Run(async () => {
                // Download and validate Microsoft's signatures, without executing installers.
                foreach (bool framework in new[] { true, false })
                {
                    string file = Path.Combine(folder, (framework ? "framework-" : "webview-") + Guid.NewGuid().ToString("N") + ".exe");
                    await Setup.Download(framework ? Setup.FrameworkUrl : Setup.WebViewUrl, file, 20L * 1024 * 1024, log, CancellationToken.None);
                    Setup.VerifyMicrosoft(file);
                }
                await Setup.InstallFfmpeg(home, log, CancellationToken.None);
            }).GetAwaiter().GetResult();
            var tool = MediaTool.FindConfigured(home);
            Qa.Check(tool != null, "Downloaded FFmpeg is configured");
            MediaQa.Run(folder, tool);
            File.WriteAllText(Path.Combine(folder, "setup-download-results.txt"),
                "PASS: pinned FFmpeg download/hash/extraction/execution/persistence and video QA; both Microsoft installer signatures verified; no Microsoft installer executed.\n");
        }
        internal static void Run(string output)
        {
            Qa.Check(!Setup.FrameworkReady(0) && !Setup.FrameworkReady(528039) && Setup.FrameworkReady(528040) && Setup.FrameworkReady(533320),
                "Setup detects .NET 4.8 and later at the official registry boundary");
            Qa.Check(!Setup.WebViewVersionReady(null) && !Setup.WebViewVersionReady("0.0.0.0") &&
                !Setup.WebViewVersionReady("bad") && !Setup.WebViewVersionReady("131.0.2903.39") &&
                Setup.WebViewVersionReady("131.0.2903.40") && Setup.WebViewVersionReady("156.0.1.2"), "Setup detects WebView2 versions");
            Qa.Check(Setup.ShouldShow(false, true) && Setup.ShouldShow(false, false) && Setup.ShouldShow(true, false) &&
                !Setup.ShouldShow(true, true), "Setup appears on first use and repairs missing dependencies");
            string root = Path.Combine(output, "setup", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string archive = Path.Combine(root, "test.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                foreach (var name in new[] { "../../bin/ffmpeg.exe", "build/bin/ffprobe.exe", "build/LICENSE", "build/README.txt" })
                    using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("test fixture");
            string hash;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(archive)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            string target = Path.Combine(root, "unpacked");
            bool rejected = false;
            try { Setup.ExtractFfmpeg(archive, target, new string('0', 64), CancellationToken.None); }
            catch (IOException) { rejected = true; }
            Qa.Check(rejected && !Directory.Exists(target), "Wrong FFmpeg hash is rejected before extraction");
            Setup.ExtractFfmpeg(archive, target, hash, CancellationToken.None);
            Qa.Check(File.Exists(Path.Combine(target, "bin", "ffmpeg.exe")) &&
                File.Exists(Path.Combine(target, "bin", "ffprobe.exe")) && !File.Exists(Path.Combine(output, "bin", "ffmpeg.exe")),
                "Archive paths cannot escape the fixed extraction targets");
            rejected = false;
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                try { Setup.ExtractFfmpeg(archive, Path.Combine(root, "cancelled"), hash, cancel.Token); }
                catch (OperationCanceledException) { rejected = true; }
            }
            Qa.Check(rejected && !Directory.Exists(Path.Combine(root, "cancelled")), "Cancelled extraction writes no tools");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) zip.GetEntry("build/bin/ffprobe.exe").Delete();
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(archive)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            rejected = false;
            try { Setup.ExtractFfmpeg(archive, Path.Combine(root, "missing"), hash, CancellationToken.None); }
            catch (IOException) { rejected = true; }
            Qa.Check(rejected, "Incomplete FFmpeg archive is rejected");
            rejected = false;
            try { Setup.VerifyMicrosoft(Path.Combine(target, "bin", "ffmpeg.exe")); }
            catch (IOException) { rejected = true; }
            Qa.Check(rejected, "Unsigned Microsoft installer is rejected before execution");
            string home = Path.Combine(root, "app");
            string invalidBin = Path.Combine(target, "bin");
            string config = Path.Combine(home, "data", "ffmpeg.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(config));
            File.WriteAllText(config, invalidBin);
            string path = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", invalidBin);
                Qa.Check(MediaTool.FindConfigured(home) == null && MediaTool.Find(home) == null,
                    "FFmpeg files that cannot launch do not satisfy setup, even when configured or on PATH");
            }
            finally { Environment.SetEnvironmentVariable("PATH", path); }
            var available = MediaTool.Find(home);
            if (available != null)
            {
                Qa.Check(MediaTool.FindConfigured(home).Encoder == available.Encoder,
                    "An invalid configured tool does not hide a working tool");
                Setup.SaveTool(home, Path.GetDirectoryName(available.Encoder));
                string saved = File.ReadAllText(config);
                rejected = false;
                try { Setup.SaveTool(home, invalidBin); }
                catch (IOException) { rejected = true; }
                Qa.Check(rejected && File.ReadAllText(config) == saved && MediaTool.FindConfigured(home).Encoder == available.Encoder,
                    "Selecting an unusable pair leaves the previous tool configured");
                if (Setup.FrameworkReady(Setup.FrameworkRelease()) && Setup.WebViewReady())
                {
                    Setup.Complete(home);
                    Qa.Check(File.Exists(Setup.Marker(home)) && !Setup.ShouldShow(true, Setup.Ready(home)),
                        "Successful setup persists completion and skips the next tutorial");
                }
            }
            var window = new SetupWindow(AppDomain.CurrentDomain.BaseDirectory) { ShowActivated = false, ShowInTaskbar = false };
            Qa.Check(window.Icon != null && window.Icon.Width == 256, "Setup window uses the supplied embedded application icon");
            try
            {
                window.Show(); Qa.Pump();
                window.UpdateState(true, true, false);
                window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output, "setup-window.png"))) png.Save(stream);
                Qa.Check(!window.Accepted, "Viewing and closing setup does not mark it complete");
            }
            finally { window.Close(); }
            Qa.Note("PASS setup: invalid FFmpeg rejection, dependency detection, first-run/recovery logic, checked archive extraction, traversal resistance, cancellation, incomplete archive, unsigned installer rejection, native WPF setup rendering. No installer was run.");
            if (available != null) Qa.Note("PASS setup: working FFmpeg fallback, persistence, and preservation after a failed manual selection.");
        }
    }
}

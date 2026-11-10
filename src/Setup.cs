using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Kirihari
{
    // No WebView2 types here: this must work before the editor's dependencies load.
    internal static class Setup
    {
        internal const string FfmpegVersion = "9.0.2";
        internal const string FfmpegUrl = "https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip";
        internal const string FfmpegAlternateUrl = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip";
        internal const string FfmpegHash = "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba";
        internal const string WebViewUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
        internal const string FrameworkUrl = "https://go.microsoft.com/fwlink/?LinkId=2085155";
        internal static bool FrameworkReady(int release) { return release >= 528040; }
        internal static bool WebViewVersionReady(string value)
        {
            Version version;
            return Version.TryParse(value, out version) && version >= new Version(131, 0, 2903, 40);
        }
        internal static int FrameworkRelease()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                return key == null ? 0 : Convert.ToInt32(key.GetValue("Release", 0));
        }
        internal static bool WebViewReady()
        {
            const string path = @"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (var key = machine.OpenSubKey(path))
                if (key != null && WebViewVersionReady(key.GetValue("pv") as string)) return true;
            using (var key = Registry.CurrentUser.OpenSubKey(path))
                return key != null && WebViewVersionReady(key.GetValue("pv") as string);
        }
        internal static string Marker(string home) { return Path.Combine(home, "data", "setup-v1.txt"); }
        internal static bool Ready(string home)
        {
            return FrameworkReady(FrameworkRelease()) && WebViewReady() && MediaTool.FindConfigured(home) != null;
        }
        internal static bool ShouldShow(bool completed, bool ready) { return !completed || !ready; }
        internal static void Complete(string home)
        {
            if (!Ready(home)) throw new IOException("まだ準備が必要な部品があります。");
            Directory.CreateDirectory(Path.Combine(home, "data"));
            File.WriteAllText(Marker(home), "1\n");
        }
        internal static MediaTool SaveTool(string home, string folder, CancellationToken cancellation = default(CancellationToken))
        {
            folder = Files.Local(folder, home);
            var tool = MediaTool.Verify(folder);
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(home, "data"));
            File.WriteAllText(Path.Combine(home, "data", "ffmpeg.txt"), folder, new System.Text.UTF8Encoding(false));
            return tool;
        }
        internal static void Open(string address)
        {
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }

        internal static async Task Download(string address, string destination, long maximumBytes,
            Action<string> progress, CancellationToken cancellation)
        {
            // Keep certificate validation enabled; Windows selects TLS 1.2 on older .NET 4.x.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(address);
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.UserAgent = "KIRIHARI-Setup/1.0";
            using (cancellation.Register(request.Abort))
            {
                try
                {
                    using (var response = (HttpWebResponse)await request.GetResponseAsync())
                    {
                        if (response.ResponseUri.Scheme != "https") throw new IOException("安全な配布先に接続できませんでした。");
                        if (response.ContentLength > maximumBytes) throw new IOException("取得するファイルが想定より大きいため中止しました。");
                        using (var input = response.GetResponseStream())
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920];
                            long bytes = 0, lastMb = -1;
                            int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation)) > 0)
                            {
                                bytes += read;
                                if (bytes > maximumBytes) throw new IOException("取得するファイルが想定より大きいため中止しました。");
                                await output.WriteAsync(buffer, 0, read, cancellation);
                                long mb = bytes / (1024 * 1024);
                                if (mb != lastMb)
                                {
                                    lastMb = mb;
                                    progress("ダウンロード中 " + mb + " MB" + (response.ContentLength > 0 ? " / " + (response.ContentLength / 1048576) + " MB" : ""));
                                }
                            }
                        }
                    }
                }
                catch (WebException) { cancellation.ThrowIfCancellationRequested(); throw; }
            }
        }
        internal static void CheckHash(string file, string expected)
        {
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(file))
                if (!BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("FFmpegの確認番号が一致しません。ファイルは実行していません。もう一度お試しください。");
        }
        internal static void ExtractFfmpeg(string zip, string destination, string hash, CancellationToken cancellation)
        {
            CheckHash(zip, hash);
            using (var archive = ZipFile.OpenRead(zip))
            {
                // Extract fixed names only. Never use archive paths as output paths.
                var names = new[] { "ffmpeg.exe", "ffprobe.exe", "LICENSE", "README.txt" };
                foreach (string name in names)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string suffix = (name.EndsWith(".exe") ? "/bin/" : "/") + name;
                    var entries = archive.Entries.Where(e => e.FullName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (entries.Length != 1 || entries[0].Length < 1 || entries[0].Length > 350L * 1024 * 1024)
                        throw new IOException("FFmpegのZIPに必要なファイルがありません。手動での指定も利用できます。");
                    string target = Path.Combine(destination, name.EndsWith(".exe") ? "bin" : "", name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entries[0].Open())
                    using (var output = new FileStream(target, FileMode.CreateNew)) input.CopyTo(output);
                }
            }
        }
        internal static async Task InstallFfmpeg(string home, Action<string> progress, CancellationToken cancellation)
        {
            string cache = Path.Combine(home, "data", "setup", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cache);
            string zip = Path.Combine(cache, "ffmpeg.zip");
            string target = Path.Combine(home, "tools", "ffmpeg", FfmpegVersion + "-" + Guid.NewGuid().ToString("N"));
            bool installed = false;
            try
            {
                bool alternate = false;
                try { await Download(FfmpegUrl, zip, 200L * 1024 * 1024, progress, cancellation); }
                catch (WebException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (File.Exists(zip)) File.Delete(zip);
                    progress("別の配布先で取得をやり直します…");
                    alternate = true;
                }
                if (alternate) await Download(FfmpegAlternateUrl, zip, 200L * 1024 * 1024, progress, cancellation);
                progress("FFmpegを確認しています…");
                // Use a new, unregistered folder. Moving freshly executed .exe files can
                // fail on Windows while antivirus or the loader still holds a handle.
                await Task.Run(() => ExtractFfmpeg(zip, target, FfmpegHash, cancellation), cancellation);
                cancellation.ThrowIfCancellationRequested();
                File.WriteAllText(Path.Combine(target, "SOURCE.txt"), FfmpegUrl + "\nSHA256: " + FfmpegHash +
                    "\nhttps://www.gyan.dev/ffmpeg/builds/\nhttps://github.com/FFmpeg/FFmpeg/commit/946fcce07b\n");
                await Task.Run(() => SaveTool(home, Path.Combine(target, "bin"), cancellation), cancellation);
                installed = true;
            }
            finally { TryDeleteCache(cache); if (!installed) TryDeleteCache(target); }
        }
        internal static async Task<int> InstallMicrosoft(string home, bool framework, Action<string> progress, CancellationToken cancellation)
        {
            string cache = Path.Combine(home, "data", "setup", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cache);
            string file = Path.Combine(cache, framework ? "ndp48-web.exe" : "MicrosoftEdgeWebview2Setup.exe");
            try
            {
                await Download(framework ? FrameworkUrl : WebViewUrl, file, 20L * 1024 * 1024, progress, cancellation);
                progress("Microsoftの署名を確認しています…");
                await Task.Run(() => VerifyMicrosoft(file), cancellation);
                cancellation.ThrowIfCancellationRequested();
                progress("Microsoftのインストール画面で案内に沿って進めてください。");
                // No silent consent or automatic restart. The user sees Microsoft's installer.
                using (var process = Process.Start(new ProcessStartInfo(file, framework ? "/norestart" : "/install") { UseShellExecute = true }))
                {
                    await Task.Run(() => process.WaitForExit());
                    return process.ExitCode;
                }
            }
            finally { TryDeleteCache(cache); }
        }
        static void TryDeleteCache(string folder)
        {
            // Only the unique folder created by this invocation is ever removed.
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TrustFile { public uint size; public string path; public IntPtr file, subject; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TrustData
        {
            public uint size; public IntPtr policy, sip; public uint ui, revocation, choice;
            public IntPtr file; public uint action; public IntPtr state, url; public uint flags, context;
        }
        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
        internal static void VerifyMicrosoft(string file)
        {
            var info = new TrustFile { size = (uint)Marshal.SizeOf(typeof(TrustFile)), path = file };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                var data = new TrustData { size = (uint)Marshal.SizeOf(typeof(TrustData)), ui = 2, choice = 1, file = ptr };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0)
                    throw new IOException("Microsoftの署名を確認できませんでした。インストーラーは実行していません。");
                using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(file)))
                    if (certificate.GetNameInfo(X509NameType.SimpleName, false) != "Microsoft Corporation")
                        throw new IOException("Microsoftのインストーラーではないため実行しません。");
            }
            finally { Marshal.DestroyStructure(ptr, typeof(TrustFile)); Marshal.FreeHGlobal(ptr); }
        }
    }
}

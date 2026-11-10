using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kirihari
{
    internal sealed partial class VideoWindow
    {
        public sealed class ProxyManifest
        {
            public string path, fingerprint, recipe, generation;
            public string timeBase, audioCodec;
            public long sourceBytes, sourceTicks, bytes, ticks;
            public double fps;
            public int width, height;
        }

        CancellationTokenSource proxyJob;
        Task proxyTask;
        string proxyToken;
        string ProxyRoot { get { return Path.Combine(qaFolder ?? Path.Combine(home, "data"), "proxies"); } }

        static string ProxyKey(string path)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))).Replace("-", "");
        }

        string ProxyFolder(VideoSource asset, ProxyProfile profile)
        {
            return Path.Combine(ProxyRoot, "v2", asset.proxyKey, profile.Level);
        }

        static ProxyManifest ReadProxyManifest(string folder)
        {
            string path = Path.Combine(folder, "current.json");
            if (!File.Exists(path)) return null;
            try
            {
                if (new FileInfo(path).Length > 65536) return null;
                var manifest = MediaTool.Json().Deserialize<ProxyManifest>(File.ReadAllText(path));
                Guid generation;
                return manifest != null && Guid.TryParseExact(manifest.generation, "N", out generation) ? manifest : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        static bool ProxySourceCurrent(VideoSource asset)
        {
            var file = new FileInfo(asset.path);
            return file.Exists && file.Length == asset.bytes && file.LastWriteTimeUtc.Ticks == asset.modifiedTicks;
        }

        static bool ProxyMatches(VideoSource asset, ProxyProfile profile, ProxyManifest manifest, FileInfo file)
        {
            var size = profile.Size(asset);
            return manifest.recipe == profile.Recipe && manifest.sourceBytes == asset.bytes &&
                manifest.sourceTicks == asset.modifiedTicks && manifest.fingerprint == asset.fingerprint &&
                string.Equals(manifest.path, asset.path, StringComparison.OrdinalIgnoreCase) &&
                manifest.width == size[0] && manifest.height == size[1] &&
                file.Exists && file.Length > 0 && file.Length == manifest.bytes && file.LastWriteTimeUtc.Ticks == manifest.ticks;
        }

        void RefreshProxies(VideoSource asset)
        {
            var variants = new Dictionary<string, ProxyVariant>();
            bool current = ProxySourceCurrent(asset);
            foreach (string level in ProxyProfile.Levels)
            {
                var profile = ProxyProfile.Get(level);
                string folder = ProxyFolder(asset, profile);
                var manifest = ReadProxyManifest(folder);
                var size = profile.Size(asset);
                var item = new ProxyVariant { state = "none", width = size[0], height = size[1] };
                if (manifest != null)
                {
                    string path = Path.Combine(folder, manifest.generation + ".mp4");
                    var file = new FileInfo(path);
                    item.bytes = file.Exists ? file.Length : 0;
                    item.state = current && ProxyMatches(asset, profile, manifest, file) ? "ready" : "stale";
                    if (item.state == "ready")
                    {
                        ProxyVariant prior;
                        item.file = path;
                        item.url = asset.proxies != null && asset.proxies.TryGetValue(level, out prior) &&
                            prior.file == path && prior.url != null && mediaFiles.ContainsKey(prior.url) ? prior.url : MapMedia(path);
                    }
                }
                ProxyVariant old;
                if (asset.proxies != null && asset.proxies.TryGetValue(level, out old) && old.url != null && old.url != item.url)
                    mediaFiles.Remove(old.url);
                variants.Add(level, item);
            }
            asset.proxies = variants;
        }

        long ProxyBytes()
        {
            if (!Directory.Exists(ProxyRoot)) return 0;
            return Directory.EnumerateFiles(ProxyRoot, "*.mp4", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".tmp.mp4", StringComparison.OrdinalIgnoreCase))
                .Sum(path => { var file = new FileInfo(path); return file.Exists ? file.Length : 0L; });
        }

        object ProxyCatalog(VideoSource asset)
        {
            RefreshProxies(asset);
            return new { variants = asset.proxies, current = ProxySourceCurrent(asset) };
        }

        static FileStream LockProxyFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            try { return File.Open(Path.Combine(folder, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) { throw new IOException("この画質のプロキシは別の処理で使用中です。", ex); }
        }

        static void CleanProxyVersions(string folder, string keep)
        {
            foreach (string path in Directory.EnumerateFiles(folder, "*.mp4"))
            {
                Guid id;
                if (path == keep || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out id)) continue;
                try { File.Delete(path); }
                catch (IOException) { } // A different process may still be previewing this generation.
                catch (UnauthorizedAccessException) { }
            }
        }

        void GenerateProxy(MediaTool encoder, VideoSource asset, ProxyProfile profile, bool replace, CancellationToken cancel,
            Action<double> progress)
        {
            string folder = ProxyFolder(asset, profile);
            using (LockProxyFolder(folder))
            using (var original = File.OpenRead(asset.path))
            {
                if (!ProxySourceCurrent(asset) || MediaTool.MediaFingerprint(original, cancel) != asset.fingerprint)
                    throw new IOException("元動画が変更されました。プロジェクトを開き直してからプロキシを作成してください。");
                var old = ReadProxyManifest(folder);
                if (!replace && old != null && ProxyMatches(asset, profile, old,
                    new FileInfo(Path.Combine(folder, old.generation + ".mp4")))) return;
                CheckArchiveSpace(folder, 16L * 1024 * 1024);
                string id = Project.Id();
                string temporary = Path.Combine(folder, id + ".tmp.mp4");
                string completed = Path.Combine(folder, id + ".mp4");
                string index = Path.Combine(folder, id + ".tmp.json");
                bool committed = false;
                try
                {
                    var output = encoder.MakePreview(asset, profile, temporary, cancel, progress);
                    cancel.ThrowIfCancellationRequested();
                    File.Move(temporary, completed);
                    var manifest = new ProxyManifest { path = asset.path, fingerprint = asset.fingerprint,
                        sourceBytes = asset.bytes, sourceTicks = asset.modifiedTicks, recipe = profile.Recipe, generation = id,
                        bytes = output.bytes, ticks = output.modifiedTicks, width = output.width, height = output.height,
                        timeBase = asset.timeBase, fps = asset.fps, audioCodec = asset.audioCodec };
                    File.WriteAllText(index, MediaTool.Json().Serialize(manifest), new UTF8Encoding(false));
                    cancel.ThrowIfCancellationRequested();
                    Replace(index, Path.Combine(folder, "current.json"));
                    committed = true;
                    CleanProxyVersions(folder, completed);
                }
                finally
                {
                    DeleteArchiveFile(temporary);
                    DeleteArchiveFile(index);
                    if (!committed) DeleteArchiveFile(completed);
                }
            }
        }

        async Task<object> BuildProxy(VideoSource asset, ProxyProfile profile, bool replace, string token)
        {
            if (proxyJob != null) throw new IOException("別のプロキシを作成中です。");
            if (tool == null) throw new IOException("FFmpegを指定してから編集用コピーを作成してください。");
            var encoder = tool;
            using (proxyJob = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                proxyToken = token;
                var cancel = proxyJob.Token;
                int percent = -1;
                try
                {
                    proxyTask = Task.Run(() => GenerateProxy(encoder, asset, profile, replace, cancel, seconds =>
                    {
                        int value = (int)Math.Max(0, Math.Min(99, seconds / asset.duration * 100));
                        if (value == percent) return;
                        percent = value;
                        Dispatcher.BeginInvoke(new Action(() => Post(new { proxyToken = token, proxyProgress = value })));
                    }));
                    await proxyTask;
                    VideoSource active;
                    return !closeAllowed && assets.TryGetValue(asset.id, out active) && active == asset ? ProxyCatalog(asset) : null;
                }
                finally { proxyTask = null; proxyJob = null; proxyToken = null; }
            }
        }

        async Task CancelProxyForClose()
        {
            var running = proxyTask;
            if (running == null) return;
            proxyJob.Cancel();
            try { await running; }
            catch (Exception) { } // The generation request reports its own outcome.
        }

        async Task<object> DeleteProxy(VideoSource asset, ProxyProfile profile)
        {
            string folder = ProxyFolder(asset, profile);
            await Task.Run(() =>
            {
                if (!Directory.Exists(folder)) return;
                using (LockProxyFolder(folder))
                {
                    var manifest = ReadProxyManifest(folder);
                    if (manifest != null) DeleteArchiveFile(Path.Combine(folder, manifest.generation + ".mp4"));
                    DeleteArchiveFile(Path.Combine(folder, "current.json"));
                    CleanProxyVersions(folder, null);
                }
            });
            VideoSource active;
            return assets.TryGetValue(asset.id, out active) && active == asset ? ProxyCatalog(asset) : null;
        }
    }
}

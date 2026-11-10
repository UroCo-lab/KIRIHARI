using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kirihari
{
    internal sealed partial class VideoWindow
    {
        const int ManifestLimit = 64000000;
        TaskCompletionSource<string> relinkDecision;
        string relinkToken;

        static void CheckCount(Dictionary<string, object> project, string key, int limit, string label)
        {
            object value;
            if (project.TryGetValue(key, out value) && value is IList && ((IList)value).Count > limit)
                throw new IOException(label + "は" + limit + "件まで扱えます。この編集内容は上限を超えています。");
        }

        static void CheckProjectLimits(Dictionary<string, object> project)
        {
            CheckCount(project, "ranges", MediaTool.CutLimit, "カット");
            CheckCount(project, "pauses", MediaTool.PauseLimit, "静止");
            CheckCount(project, "captions", MediaTool.CaptionLimit, "テロップ");
            CheckCount(project, "originals", 2000, "元動画の配置");
            CheckCount(project, "bookmarks", 1000, "目印");
            object value;
            if (project.TryGetValue("captions", out value) && value is IList)
                foreach (var caption in ((IList)value).OfType<Dictionary<string, object>>())
                    CheckCount(caption, "objects", 300, "テロップ内の図形・文字");
        }

        static Dictionary<string, string> ArchiveEntries(IEnumerable<VideoSource> included)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new Dictionary<string, string>();
            foreach (var asset in included)
            {
                string name = Path.GetFileName(asset.path);
                string entry = "source/" + name;
                for (int number = 2; !used.Add(entry); number++)
                    entry = "source/" + number + "_" + name;
                entries.Add(asset.id, entry);
            }
            return entries;
        }

        static void CheckArchiveSpace(string folder, long bytes)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (new DriveInfo(root).AvailableFreeSpace < bytes)
                throw new IOException("空き容量が不足しています。必要な容量は約 " +
                    Math.Ceiling(bytes / 1048576.0) + " MB です。空き容量を確認してください。");
        }

        Action<string, long> ArchiveProgress(string phase, long total)
        {
            int last = -1;
            string lastName = null;
            return (name, done) =>
            {
                int value = total > 0 ? Math.Min(100, (int)(done * 100.0 / total)) : 0;
                if (name == lastName && value == last) return;
                last = value;
                lastName = name;
                Dispatcher.BeginInvoke(new Action(() =>
                    Post(new { archiveProgress = new { phase = phase, value = value, name = name } })));
            };
        }

        static void CopyArchiveMedia(Stream input, Stream output, long length, byte[] buffer,
            CancellationToken cancel, Action<int> progress)
        {
            long remaining = length;
            while (remaining > 0)
            {
                cancel.ThrowIfCancellationRequested();
                int count = input.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                if (count == 0) throw new IOException("素材の読み込みが途中で終了しました。");
                output.Write(buffer, 0, count);
                remaining -= count;
                progress(count);
            }
            if (input.ReadByte() != -1) throw new IOException("素材のサイズが一致しません。");
        }

        void CreateArchive(string path, Dictionary<string, string> entries, string project, CancellationToken cancel)
        {
            ProtectSource(path);
            long bytes = entries.Keys.Sum(id => GetAsset(id).bytes);
            string directory = Path.GetDirectoryName(path);
            CheckArchiveSpace(directory, checked(bytes + Encoding.UTF8.GetByteCount(project) + 1048576));
            var report = ArchiveProgress("save", bytes);
            string temp = Path.Combine(directory, "." + Project.Id() + ".zip");
            var buffer = new byte[1024 * 1024];
            long done = 0;
            try
            {
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create, Encoding.UTF8))
                {
                    foreach (var entry in entries)
                    {
                        cancel.ThrowIfCancellationRequested();
                        var asset = GetAsset(entry.Key);
                        string name = Path.GetFileName(asset.path);
                        report(name, done);
                        using (var file = File.OpenRead(asset.path))
                        using (var copy = zip.CreateEntry(entry.Value, CompressionLevel.NoCompression).Open())
                        {
                            if (file.Length != asset.bytes || MediaTool.MediaFingerprint(file, cancel) != asset.fingerprint)
                                throw new IOException("登録後に素材の内容が変わりました。素材を読み直してください。");
                            CopyArchiveMedia(file, copy, asset.bytes, buffer, cancel, count => report(name, done += count));
                        }
                    }
                    using (var writer = new StreamWriter(zip.CreateEntry("project.json").Open(), new UTF8Encoding(false)))
                        writer.Write(project);
                }
                cancel.ThrowIfCancellationRequested();
                Replace(temp, path);
            }
            finally { DeleteArchiveFile(temp); }
        }

        static void DeleteArchiveFile(string path)
        {
            for (int attempt = 0; File.Exists(path); attempt++)
            {
                try { File.Delete(path); return; }
                catch (IOException) { if (attempt == 7) throw; Thread.Sleep(60 * (attempt + 1)); }
            }
        }

        Dictionary<string, object> ExtractArchive(string path, string folder,
            out Dictionary<string, string> paths, CancellationToken cancel)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (entries.ContainsKey(entry.FullName))
                        throw new IOException("ZIPに同じ名前の項目が複数あります。");
                    entries.Add(entry.FullName, entry);
                }
                ZipArchiveEntry manifest;
                if (!entries.TryGetValue("project.json", out manifest) || manifest.Length <= 0 || manifest.Length > ManifestLimit)
                    throw new IOException("KIRIHARIのプロジェクトZIPではありません。");
                var buffer = new byte[1024 * 1024];
                Dictionary<string, object> project;
                using (var raw = manifest.Open())
                using (var json = new MemoryStream())
                {
                    CopyArchiveMedia(raw, json, manifest.Length, buffer, cancel, count => { });
                    json.Position = 0;
                    using (var reader = new StreamReader(json)) project = ReadProject(reader.ReadToEnd());
                }
                paths = ProjectAssets(project);
                var expected = AssetMetadata(project, "assetBytes");
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (var asset in paths)
                {
                    string name = Path.GetFileName(asset.Value);
                    ZipArchiveEntry item;
                    if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name != name.TrimEnd(' ', '.') ||
                        name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || asset.Value != "source/" + name ||
                        !used.Add(asset.Value) || !entries.TryGetValue(asset.Value, out item) || item.Length == 0)
                        throw new IOException("ZIPに必要な動画素材がないか、素材名が無効です。");
                    CheckAssetSize(expected, asset.Key, item.Length, "ZIP内の素材のサイズが保存時と異なります。");
                    total = checked(total + item.Length);
                }
                CheckArchiveSpace(folder, checked(total + 1048576));
                var report = ArchiveProgress("open", total);
                long done = 0;
                foreach (string key in paths.Keys.ToArray())
                {
                    cancel.ThrowIfCancellationRequested();
                    var entry = entries[paths[key]];
                    string name = Path.GetFileName(paths[key]);
                    string assetFolder = Path.Combine(folder, key);
                    Directory.CreateDirectory(assetFolder);
                    string video = Path.Combine(assetFolder, name);
                    report(name, done);
                    using (var input = entry.Open())
                    using (var output = File.Open(video, FileMode.CreateNew))
                        CopyArchiveMedia(input, output, entry.Length, buffer, cancel, count => report(name, done += count));
                    paths[key] = video;
                }
                return project;
            }
        }

        static Dictionary<string, object> AssetMetadata(Dictionary<string, object> project, string key)
        {
            object value;
            return project.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        static void CheckAssetSize(Dictionary<string, object> sizes, string key, long length, string message)
        {
            object bytes;
            long expected;
            if (sizes != null && sizes.TryGetValue(key, out bytes) &&
                long.TryParse(Convert.ToString(bytes), out expected) && expected > 0 && length != expected)
                throw new IOException(message);
        }

        async Task<string> ConfirmRelink(VideoSource candidate, string missing, CancellationToken cancel)
        {
            string poster = await Task.Run(() => RelinkPoster(candidate, cancel));
            string url = MapMedia(candidate.path);
            relinkToken = Project.Id();
            var decision = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            relinkDecision = decision;
            try
            {
                Post(new { relinkCandidate = new { token = relinkToken, url = url,
                    name = Path.GetFileName(candidate.path), missing = Path.GetFileName(missing),
                    duration = candidate.duration, width = candidate.width, height = candidate.height, poster = poster } });
                using (cancel.Register(() => decision.TrySetCanceled())) return await decision.Task;
            }
            finally { relinkDecision = null; relinkToken = null; mediaFiles.Remove(url); }
        }

        string RelinkPoster(VideoSource candidate, CancellationToken cancel)
        {
            string directory = qaFolder ?? Path.Combine(home, "data");
            string temp = Path.Combine(directory, "." + Project.Id() + ".jpg");
            try
            {
                Directory.CreateDirectory(directory);
                MediaTool.Run(tool.Encoder, new[] { "-y", "-protocol_whitelist", "file,pipe", "-i", candidate.path,
                    "-frames:v", "1", "-vf", "scale='min(480,iw)':-2", "-q:v", "5", "-update", "1", temp }, cancel, null);
                return "data:image/jpeg;base64," + Convert.ToBase64String(File.ReadAllBytes(temp));
            }
            catch (IOException) { return null; }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        async Task<object> OpenProject(string path, bool remember = true)
        {
            path = Files.Local(path, home);
            if (tool == null) throw new IOException("FFmpegを指定してからプロジェクトを開いてください。");
            bool archive = Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);
            string staged = archive ? Path.Combine(qaFolder ?? Path.Combine(home, "data"), "imports", Project.Id()) : null;
            using (job = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                OpenedProject result = null;
                ExceptionDispatchInfo failure = null;
                try { result = await OpenProjectCore(path, staged, remember, job.Token); }
                catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
                try
                {
                    if (result == null && staged != null)
                        await Task.Run(() => MediaTool.DeleteWork(staged));
                    if (failure != null) failure.Throw();
                    return result;
                }
                finally { job = null; }
            }
        }

        async Task<OpenedProject> OpenProjectCore(string path, string staged, bool remember, CancellationToken cancel)
        {
            bool archive = staged != null;
            Dictionary<string, object> project;
            Dictionary<string, string> paths = null;
            if (archive)
            {
                var extracted = await Task.Run(() =>
                {
                    Dictionary<string, string> videos;
                    var data = ExtractArchive(path, staged, out videos, cancel);
                    return new { data, videos };
                });
                project = extracted.data;
                paths = extracted.videos;
            }
            else
            {
                if (new FileInfo(path).Length > ManifestLimit) throw new IOException("編集ファイルが大きすぎます。");
                project = ReadProject(File.ReadAllText(path));
                paths = ProjectAssets(project);
                foreach (string key in paths.Keys.ToArray())
                    paths[key] = Files.Local(paths[key], Path.GetDirectoryName(path));
            }

            var sizes = AssetMetadata(project, "assetBytes");
            var fingerprints = AssetMetadata(project, "assetFingerprints");
            var loaded = new Dictionary<string, VideoSource>();
            bool relinked = false;
            foreach (string key in paths.Keys.ToArray())
            {
                string missing = paths[key];
                bool replacing = !File.Exists(missing);
                object value;
                string fingerprint = fingerprints != null && fingerprints.TryGetValue(key, out value)
                    ? value as string : null;
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    string video = replacing && !archive ? PickMissingAsset(missing) : missing;
                    if (video == null) return null;
                    if (replacing) CheckAssetSize(sizes, key, new FileInfo(video).Length,
                        "選んだ動画のサイズが保存時と異なります。元と同じ動画を選び直してください。");
                    var candidate = await Task.Run(() => tool.Read(video, cancel));
                    if ((archive || replacing) && !string.IsNullOrEmpty(fingerprint) && fingerprint != candidate.fingerprint)
                        throw new IOException("動画の内容が保存時と異なります。元と同じ動画を指定してください。");
                    if (replacing && string.IsNullOrEmpty(fingerprint))
                    {
                        string choice = await ConfirmRelink(candidate, missing, cancel);
                        if (choice == "retry") continue;
                        if (choice != "use") return null;
                    }
                    loaded.Add(key, candidate);
                    paths[key] = video;
                    relinked |= replacing;
                    break;
                }
            }
            cancel.ThrowIfCancellationRequested();
            if (relinked) ValidateRelinkedCuts(project, loaded);
            object session = StartSession(loaded[Text(project, "baseMediaId")], loaded);
            string directory = Path.GetDirectoryName(path);
            bool sample = string.Equals(directory, SampleFolder, StringComparison.OrdinalIgnoreCase);
            projectFile = archive || sample ? null : path;
            archiveFolder = projectFile == null ? directory : null;
            if (remember) RememberFolder("project", path);
            if (relinked)
            {
                project["assets"] = paths;
                project["sourcePath"] = source.path;
            }
            return new OpenedProject { session = session, project = project, relinked = relinked };
        }
    }
}

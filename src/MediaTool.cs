using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Kirihari
{
    public sealed class VideoSource
    {
        public string id, path, url, codec, proxyHint, proxyKey;
        public Dictionary<string, ProxyVariant> proxies;
        public int width, height, frames;
        public double fps, duration;
        public bool audio;
        public long bytes;
        internal string fingerprint;
        internal string timeBase, audioCodec;
        internal long modifiedTicks;
        internal double formatStart, videoStart, audioStart, audioDuration;
    }

    public sealed class VideoRange { public string id, mediaId; public int start, end, mediaStart; public double? speed; }

    public sealed class VideoPause { public string target; public int frame; public double seconds; }

    public sealed class VideoTransition { public string kind, enter, exit; public double seconds; public double? enterSeconds, exitSeconds; }

    public sealed class VideoSlide
    {
        public string id, target;
        public int start, end;
        public bool pauseOnly;
        public VideoTransition transition;
    }

    public sealed class VideoExport
    {
        public string folder;
        public bool muted;
        public int timelineFrames;
        public VideoRange[] ranges;
        public VideoSlide[] slides;
        public VideoPause[] pauses;
        public VideoOutput output;
    }

    internal sealed partial class MediaTool
    {
        internal const int CutLimit = 2000, PauseLimit = 2000, CaptionLimit = 1000;
        public readonly string Encoder, Probe;
        string filterFileOption;
        string FilterFileOption(CancellationToken cancellation)
        {
            if (filterFileOption == null)
            {
                string help = Run(Encoder, new[] { "-h", "full" }, cancellation, null);
                filterFileOption = help.Contains("-filter_complex_script") ? "-filter_complex_script" : "-/filter_complex";
            }
            return filterFileOption;
        }
        public static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = 64000000, RecursionLimit = 48 };
        }
        MediaTool(string folder)
        {
            Encoder = Path.Combine(folder, "ffmpeg.exe");
            Probe = Path.Combine(folder, "ffprobe.exe");
            if (!File.Exists(Encoder) || !File.Exists(Probe))
                throw new IOException("同じフォルダに ffmpeg.exe と ffprobe.exe が必要です。");
        }
        internal static MediaTool Verify(string folder)
        {
            var tool = new MediaTool(folder);
            try
            {
                VerifyVersion(tool.Encoder, "ffmpeg");
                VerifyVersion(tool.Probe, "ffprobe");
            }
            catch (Win32Exception e) { throw new IOException("このPCでFFmpegまたはFFprobeを起動できませんでした。", e); }
            return tool;
        }
        static void VerifyVersion(string exe, string name)
        {
            uint binaryType;
            if (!GetBinaryType(exe, out binaryType) || (binaryType != 0 && binaryType != 6))
                throw new IOException(name + "のWindows用実行ファイルを確認できませんでした。");
            using (var process = Process.Start(new ProcessStartInfo(exe, "-version") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    throw new IOException(name + "の起動確認が時間切れになりました。");
                }
                if (process.ExitCode != 0 || !output.Wait(1000) ||
                    !output.Result.StartsWith(name + " version ", StringComparison.Ordinal))
                    throw new IOException(name + "のバージョンを確認できませんでした。");
            }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetBinaryType(string path, out uint binaryType);
        public static MediaTool Find(string home)
        {
            var folders = new List<string> {
                Path.Combine(home, "tools", "ffmpeg", "bin"),
                Path.Combine(home, "tools", "ffmpeg"), home
            };
            folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().Trim('"')));
            foreach (string folder in folders)
            {
                try
                {
                    if (Path.IsPathRooted(folder) && File.Exists(Path.Combine(folder, "ffmpeg.exe")) &&
                        File.Exists(Path.Combine(folder, "ffprobe.exe"))) return Verify(folder);
                }
                catch (IOException) { }
                catch (ArgumentException) { }
            }
            return null;
        }
        internal static MediaTool FindConfigured(string home)
        {
            string configured = Path.Combine(home, "data", "ffmpeg.txt");
            if (File.Exists(configured))
            {
                try { return Verify(Files.Local(File.ReadAllText(configured).Trim(), home)); }
                catch (IOException) { }
                catch (ArgumentException) { }
                catch (UnauthorizedAccessException) { }
            }
            return Find(home);
        }
        public VideoSource Read(string path, CancellationToken cancellation)
        {
            var fileInfo = new FileInfo(path);
            long size = fileInfo.Length, modified = fileInfo.LastWriteTimeUtc.Ticks;
            string output = Run(Probe, new[] { "-v", "error", "-protocol_whitelist", "file,pipe", "-show_streams",
                "-show_format", "-of", "json", path }, cancellation, null);
            var json = Json().Deserialize<Dictionary<string, object>>(output);
            var streams = ((System.Collections.IEnumerable)json["streams"]).Cast<Dictionary<string, object>>().ToArray();
            var video = streams.FirstOrDefault(s => Value(s, "codec_type") == "video" &&
                (!(s.ContainsKey("disposition")) || Value((Dictionary<string, object>)s["disposition"], "attached_pic") != "1"));
            if (video == null) throw new IOException("動画トラックが見つかりません。");
            double duration = Number(video, "duration");
            var format = (Dictionary<string, object>)json["format"];
            if (duration <= 0) duration = Number(format, "duration");
            double fps = Rate(Value(video, "avg_frame_rate"));
            if (fps <= 0) fps = Rate(Value(video, "r_frame_rate"));
            int width = (int)Number(video, "width"), height = (int)Number(video, "height");
            double rotation = 0;
            if (video.ContainsKey("tags")) rotation = Number((Dictionary<string, object>)video["tags"], "rotate");
            if (video.ContainsKey("side_data_list"))
                foreach (Dictionary<string, object> item in (System.Collections.IEnumerable)video["side_data_list"])
                    if (item.ContainsKey("rotation")) rotation = Number(item, "rotation");
            if (Math.Abs(rotation % 180) == 90) { int swap = width; width = height; height = swap; }
            if (fps < 1 || fps > 240 || duration <= 0 || duration * fps > 100000000 || width < 2 || height < 2)
                throw new IOException("動画の長さ、解像度、またはフレームレートを取得できませんでした。");
            var audio = streams.FirstOrDefault(s => Value(s, "codec_type") == "audio");
            var source = new VideoSource {
                path = path, fps = fps, duration = duration, width = width, height = height,
                frames = Math.Max(1, (int)Math.Round(duration * fps)), bytes = size, modifiedTicks = modified,
                codec = Value(video, "codec_name"), audio = audio != null, timeBase = Value(video, "time_base"),
                formatStart = Number(format, "start_time"), videoStart = Number(video, "start_time"),
                audioStart = audio == null ? 0 : Number(audio, "start_time"),
                audioDuration = audio == null ? 0 : Number(audio, "duration"), audioCodec = audio == null ? null : Value(audio, "codec_name")
            };
            source.proxyHint = SuggestProxy(source, Value(video, "pix_fmt"));
            using (var file = File.OpenRead(path))
                source.fingerprint = MediaFingerprint(file, cancellation);
            return source;
        }

        internal static string MediaFingerprint(Stream stream, CancellationToken cancel)
        {
            const int sampleSize = 64 * 1024, samples = 17;
            using (var hash = SHA256.Create())
            {
                var sample = new byte[sampleSize];
                long length = stream.Length;
                var size = BitConverter.GetBytes(length);
                hash.TransformBlock(size, 0, size.Length, size, 0);
                for (int index = 0; index < samples; index++)
                {
                    cancel.ThrowIfCancellationRequested();
                    long offset = length <= sampleSize * samples ? (long)sampleSize * index :
                        (length - sampleSize) / (samples - 1) * index +
                        (length - sampleSize) % (samples - 1) * index / (samples - 1);
                    if (offset >= length) break;
                    stream.Position = offset;
                    int expected = (int)Math.Min(sampleSize, length - offset);
                    int read = 0;
                    while (read < expected)
                    {
                        int count = stream.Read(sample, read, expected - read);
                        if (count == 0) throw new IOException("素材の読み込み中にファイルが変更されました。");
                        read += count;
                    }
                    hash.TransformBlock(sample, 0, read, sample, 0);
                }
                hash.TransformFinalBlock(new byte[0], 0, 0);
                stream.Position = 0;
                return "sha256-sample-v1:" + Convert.ToBase64String(hash.Hash);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatus
        {
            public uint length, load;
            public ulong physical, availablePhysical, pageFile, availablePageFile, virtualMemory, availableVirtual, extendedVirtual;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        static double PreviewBudget()
        {
            var memory = new MemoryStatus { length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            double ram = GlobalMemoryStatusEx(ref memory) ? memory.physical / 1073741824.0 : 16;
            int threads = Environment.ProcessorCount;
            if (threads <= 4 || ram < 8) return 2.5;
            if (threads <= 8 || ram < 16) return 4;
            return 6;
        }

        static readonly double previewBudget = PreviewBudget();

        static string SuggestProxy(VideoSource video, string pixelFormat)
        {
            double throughput = (double)video.width * video.height * video.fps / (1920 * 1080 * 30);
            bool demandingCodec = video.codec == "av1" || video.codec == "hevc" || video.codec == "vp9" || video.codec == "prores" ||
                pixelFormat.Contains("10") || pixelFormat.Contains("12") || pixelFormat.Contains("422") || pixelFormat.Contains("444");
            double megabits = video.bytes * 8.0 / video.duration / 1000000;
            bool longVideo = video.duration >= 3600;
            const long gibibyte = 1024L * 1024 * 1024;
            bool largeFile = video.bytes >= 2 * gibibyte;
            if (!longVideo && !largeFile && throughput * (demandingCodec ? 1.5 : 1) < previewBudget && megabits < previewBudget * 30)
                return null;
            var hint = video.width + "×" + video.height + "・" + video.fps.ToString("0.##") + " fps・約 " +
                megabits.ToString("0.#") + " Mbps";
            if (longVideo) hint += "・" + (int)(video.duration / 3600) + "時間" + (int)(video.duration / 60 % 60) + "分";
            if (largeFile) hint += "・" + (video.bytes / (double)gibibyte).ToString("0.#") + " GiB";
            return hint;
        }
        static string Value(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }
        static double Number(Dictionary<string, object> map, string key)
        {
            double result;
            return double.TryParse(Value(map, key), NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                !double.IsNaN(result) && !double.IsInfinity(result) ? result : 0;
        }
        static double Rate(string value)
        {
            var pair = value.Split('/');
            double a, b;
            return pair.Length == 2 && double.TryParse(pair[0], out a) && double.TryParse(pair[1], out b) && b > 0 ? a / b : 0;
        }
        internal static string Num(double value) { return value.ToString("0.#########", CultureInfo.InvariantCulture); }
        public static string ImageFile(string work, string id)
        {
            Guid key;
            if (!Guid.TryParseExact(id, "N", out key)) throw new IOException("スライドの識別子が無効です。");
            return Path.Combine(work, key.ToString("N") + ".png");
        }
        public static int[] OutputSize(VideoSource source)
        {
            double scale = Math.Min(1, Math.Min(1920.0 / Math.Max(source.width, source.height),
                1080.0 / Math.Min(source.width, source.height)));
            return new[] { Math.Max(2, (int)(source.width * scale) / 2 * 2), Math.Max(2, (int)(source.height * scale) / 2 * 2) };
        }
        public string Export(VideoSource source, VideoExport request, string work, CancellationToken cancel, Action<double> progress,
            IDictionary<string, VideoSource> assets = null)
        {
            if (request.ranges == null || request.ranges.Length == 0 || request.ranges.Length > CutLimit)
                throw new IOException("書き出すカットを1つ以上選んでください。");
            var ranges = request.ranges.OrderBy(r => r.start).ToArray();
            int last = 0;
            int timelineFrames = request.timelineFrames > 0 ? request.timelineFrames : source.frames;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var inputs = new List<VideoSource>();
            foreach (var cut in ranges)
            {
                if (cut.start < last || cut.end <= cut.start || cut.end > timelineFrames || !ids.Add(cut.id ?? ""))
                    throw new IOException("カットの範囲が無効です。");
                VideoSource input = source;
                if (cut.mediaId != null && (assets == null || !assets.TryGetValue(cut.mediaId, out input)))
                    throw new IOException("カットの動画素材が見つかりません。");
                int mediaStart = cut.mediaId == null ? cut.start : cut.mediaStart;
                if (mediaStart < 0 || (long)mediaStart + cut.end - cut.start > Math.Max(1, Math.Round(input.duration * source.fps, MidpointRounding.AwayFromZero)))
                    throw new IOException("カットが動画素材の長さを超えています。");
                inputs.Add(input);
                last = cut.end;
            }
            var slides = request.slides ?? new VideoSlide[0];
            if (slides.Length > CaptionLimit || slides.Any(s => s == null || s.start > s.end ||
                (s.start == s.end && !s.pauseOnly) || !ids.Contains(s.target)))
                throw new IOException("スライドの範囲が無効です。");
            var pauses = request.pauses ?? new VideoPause[0];
            var byId = ranges.ToDictionary(cut => cut.id ?? "", StringComparer.Ordinal);
            if (pauses.Length > PauseLimit || pauses.Any(p => {
                VideoRange cut;
                return p == null || p.target == null || !byId.TryGetValue(p.target, out cut) ||
                    p.frame < cut.start || p.frame >= cut.end || double.IsNaN(p.seconds) || p.seconds < .1 || p.seconds > 600;
            }) || pauses.GroupBy(p => new { p.target, p.frame }).Any(group => group.Count() > 1))
                throw new IOException("ポーズの位置または停止時間が無効です。");
            var holdsByCut = pauses.ToLookup(p => p.target);
            if (slides.Any(s => s.pauseOnly && (s.start != s.end || !holdsByCut[s.target].Any(p => p.frame == s.start))))
                throw new IOException("ポーズ中だけ表示するテロップの停止位置が見つかりません。");
            ExportProfile profile = ResolveOutput(source, request.output);
            string folder = Files.Local(request.folder, AppDomain.CurrentDomain.BaseDirectory);
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(work);
            var job = Path.Combine(work, "export-" + Project.Id());
            Directory.CreateDirectory(job);
            string partial = Path.Combine(folder, ".kirihari-" + Project.Id() + ".mp4");
            try
            {
                int[] size = new[] { profile.width, profile.height };
                bool audio = !request.muted && inputs.Any(input => input.audio);
                bool retimedExport = ranges.Any(cut => CutSpeed(cut) != 1);
                long total = ranges.Sum(r => (long)CutBodyFrames(r, CutSpeed(r))) + pauses.Sum(p => (long)PauseFrames(p, source.fps)), done = 0;
                string rate = Num(source.fps);
                var manifest = new StringBuilder("ffconcat version 1.0\n");
                for (int i = 0; i < ranges.Length; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var cut = ranges[i];
                    var input = inputs[i];
                    int mediaStart = cut.mediaId == null ? cut.start : cut.mediaStart;
                    int count = cut.end - cut.start;
                    double speed = CutSpeed(cut);
                    int bodyFrames = CutBodyFrames(cut, speed);
                    var holds = holdsByCut[cut.id].OrderBy(p => p.frame).ToArray();
                    string segment = "part-" + i + ".mp4";
                    string filter = Path.Combine(job, "filters-" + i + ".txt");
                    var args = new List<string> { "-y", "-filter_complex_threads", "1", "-ss", Num(mediaStart / source.fps),
                        "-protocol_whitelist", "file,pipe", "-i", input.path };
                    var graph = new StringBuilder("[0:V:0]setpts=PTS-STARTPTS,fps=" + rate + ":eof_action=pass," +
                        "scale=" + size[0] + ":" + size[1] + ":force_original_aspect_ratio=decrease:force_divisible_by=2," +
                        "pad=" + size[0] + ":" + size[1] + ":(ow-iw)/2:(oh-ih)/2,setsar=1,format=yuv420p," +
                        "tpad=stop_mode=clone:stop_duration=0.1,trim=end_frame=" + count + "[v0]");
                    int overlay = 0;
                    string videoOutput = "v0";
                    int extra = holds.Sum(hold => PauseFrames(hold, source.fps));
                    var retimed = speed == 1 ? null : RetimedParts(cut, holds, speed, source.fps);
                    if (retimed != null)
                        videoOutput = AddRetimedVideo(graph, retimed, rate);
                    else if (holds.Length > 0)
                    {
                        graph.Append(";[").Append(videoOutput).Append(']');
                        int inserted = 0;
                        foreach (var hold in holds)
                        {
                            int frames = PauseFrames(hold, source.fps);
                            graph.Append("loop=loop=").Append(frames).Append(":size=1:start=").Append(hold.frame - cut.start + inserted).Append(',');
                            inserted += frames;
                        }
                        graph.Append("setpts=N/(").Append(rate).Append("*TB)[frozen]");
                        videoOutput = "frozen";
                    }
                    // Freeze the footage first, then compose all layers in their original order.
                    // A zero-width source interval can now cover the inserted hold's full duration.
                    foreach (var slide in slides.Where(s => s.target == cut.id))
                    {
                        double start = CutFrame(Math.Max(0, slide.start - cut.start), speed) +
                            holds.Where(p => p.frame < slide.start).Sum(p => PauseFrames(p, source.fps));
                        double end = (slide.end >= cut.end ? bodyFrames : CutFrame(slide.end - cut.start, speed)) +
                            holds.Where(p => p.frame < slide.end || (slide.pauseOnly && p.frame == slide.end)).Sum(p => PauseFrames(p, source.fps));
                        if (start >= end) continue;
                        string image = ImageFile(work, slide.id);
                        if (!File.Exists(image)) throw new IOException("書き出し用スライド画像が見つかりません。");
                        args.AddRange(new[] { "-loop", "1", "-framerate", rate, "-i", image });
                        AddOverlay(graph, ++overlay, start / source.fps, end / source.fps, slide.transition, videoOutput);
                        videoOutput = "v" + overlay;
                    }
                    string filteredAudio = null;
                    if (audio && input.audio && retimed != null)
                        filteredAudio = AddRetimedAudio(graph, retimed, count / source.fps, source.fps);
                    else if (audio && input.audio && holds.Length > 0)
                    {
                        AddPauseAudio(graph, cut, holds, source.fps);
                        filteredAudio = "heldAudio";
                    }
                    if (audio && !input.audio)
                        args.AddRange(new[] { "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo" });
                    File.WriteAllText(filter, graph.ToString(), new UTF8Encoding(false));
                    args.AddRange(new[] { FilterFileOption(cancel), filter, "-map", "[" + videoOutput + "]" });
                    if (audio)
                    {
                        args.AddRange(new[] { "-map", filteredAudio != null ? "[" + filteredAudio + "]" : input.audio ? "0:a:0" : (overlay + 1) + ":a:0" });
                        if (filteredAudio == null) args.AddRange(new[] { "-af", "aresample=48000:async=1:first_pts=0,apad" });
                        args.AddRange(new[] { "-ac", "2", "-c:a", "aac", "-b:a", profile.audioKbps + "k" });
                    }
                    else args.Add("-an");
                    double duration = (bodyFrames + extra) / source.fps;
                    bool changeFps = Math.Abs(profile.fps - source.fps) > .0000001;
                    EncodingArguments(args, changeFps ? new ExportProfile { crf = 16 } : profile);
                    // A retimed part can contain one frame. Keep DTS in display order across part boundaries.
                    if (retimedExport) args.AddRange(new[] { "-bf", "0" });
                    args.AddRange(new[] { "-t", Num(duration), "-map_metadata", "-1", "-map_chapters", "-1", Path.Combine(job, segment) });
                    long earlier = done;
                    Run(Encoder, args, cancel, s => { if (progress != null) progress(Math.Min(.98, (earlier + s * source.fps) / total * .98)); });
                    manifest.Append("file '").Append(segment).Append("'\nduration ").Append(Num(duration)).Append('\n');
                    done += bodyFrames + extra;
                }
                string list = Path.Combine(job, "parts.txt");
                File.WriteAllText(list, manifest.ToString(), new UTF8Encoding(false));
                var finalArgs = new List<string> { "-y", "-f", "concat", "-safe", "1", "-i", list, "-map", "0" };
                if (Math.Abs(profile.fps - source.fps) > .0000001)
                {
                    finalArgs.AddRange(new[] { "-vf", "fps=" + Num(profile.fps) + ":round=near", "-c:a", "copy" });
                    EncodingArguments(finalArgs, profile);
                }
                else finalArgs.AddRange(new[] { "-c", "copy" });
                finalArgs.AddRange(new[] { "-movflags", "+faststart", partial });
                Run(Encoder, finalArgs, cancel, null);
                cancel.ThrowIfCancellationRequested();
                if (new FileInfo(partial).Length < 256) throw new IOException("完成した動画が空です。");
                string target = Available(folder, Path.GetFileNameWithoutExtension(source.path) + "_編集", ".mp4");
                File.Move(partial, target);
                if (progress != null) progress(1);
                return target;
            }
            finally
            {
                try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
                try { DeleteWork(job); } catch (IOException) { }
            }
        }
        static int PauseFrames(VideoPause pause, double fps)
        {
            return Math.Max(1, (int)Math.Round(pause.seconds * fps, MidpointRounding.AwayFromZero));
        }

        static void AddPauseAudio(StringBuilder graph, VideoRange cut, VideoPause[] holds, double fps)
        {
            // Split the source audio at the frozen frames and insert matching stereo silence.
            int parts = holds.Length + (holds[0].frame > cut.start ? 1 : 0);
            graph.Append(";[0:a:0]aresample=48000:async=1:first_pts=0,aformat=channel_layouts=stereo,apad,atrim=duration=")
                .Append(Num((cut.end - cut.start) / fps));
            if (parts > 1) graph.Append(",asplit=").Append(parts);
            for (int i = 0; i < parts; i++) graph.Append("[audio").Append(i).Append(']');

            var sequence = new StringBuilder();
            int part = 0, start = cut.start;
            for (int i = 0; i <= holds.Length; i++)
            {
                int end = i < holds.Length ? holds[i].frame : cut.end;
                if (start < end)
                {
                    graph.Append(";[audio").Append(part).Append("]atrim=start=").Append(Num((start - cut.start) / fps))
                        .Append(":end=").Append(Num((end - cut.start) / fps)).Append(",asetpts=PTS-STARTPTS[part").Append(part).Append(']');
                    sequence.Append("[part").Append(part++).Append(']');
                }
                if (i == holds.Length) break;
                graph.Append(";anullsrc=r=48000:cl=stereo,atrim=duration=").Append(Num(PauseFrames(holds[i], fps) / fps))
                    .Append("[silence").Append(i).Append(']');
                sequence.Append("[silence").Append(i).Append(']');
                start = end;
            }
            graph.Append(';').Append(sequence).Append("concat=n=").Append(parts + holds.Length).Append(":v=0:a=1[heldAudio]");
        }
        internal static void DeleteWork(string folder)
        {
            for (int attempt = 0; Directory.Exists(folder); attempt++)
            {
                try { Directory.Delete(folder, true); return; }
                catch (IOException) { if (attempt == 7) throw; Thread.Sleep(60 * (attempt + 1)); }
            }
        }
        internal static string Available(string folder, string name, string extension)
        {
            for (int i = 0; i < 10000; i++)
            {
                string path = Path.Combine(folder, name + (i == 0 ? "" : "_" + i) + extension);
                if (!File.Exists(path) && !Directory.Exists(path)) return path;
            }
            throw new IOException("保存先に同じ名前のファイルが多すぎます。");
        }
        static void AddOverlay(StringBuilder graph, int i, double start, double end, VideoTransition transition, string input)
        {
            var motion = transition ?? new VideoTransition();
            var duration = TransitionDurations(motion, end - start);
            graph.Append(";[").Append(i).Append(":v]format=rgba");
            if (motion.kind == "fade") graph.Append(",fade=t=in:st=").Append(Num(start)).Append(":d=").Append(Num(duration[0]))
                .Append(":alpha=1,fade=t=out:st=").Append(Num(end - duration[1])).Append(":d=").Append(Num(duration[1])).Append(":alpha=1");
            graph.Append("[o").Append(i).Append("];[").Append(input).Append("][o").Append(i).Append("]overlay=");
            string x = "0", y = "0";
            if (motion.kind == "slide")
            {
                string enter = "max(0,min(1,(" + Num(start + duration[0]) + "-t)/" + Num(duration[0]) + "))";
                string exit = "max(0,min(1,(t-" + Num(end - duration[1]) + ")/" + Num(duration[1]) + "))";
                x = "W*(" + Axis(motion.enter, "left", "right", enter) + "+" + Axis(motion.exit, "left", "right", exit) + ")";
                y = "H*(" + Axis(motion.enter, "up", "down", enter) + "+" + Axis(motion.exit, "up", "down", exit) + ")";
            }
            graph.Append("x='").Append(x).Append("':y='").Append(y).Append("':shortest=1:enable='gte(t,")
                .Append(Num(start)).Append(")*lt(t,").Append(Num(end)).Append(")'[v").Append(i).Append("]");
        }
        internal static double[] TransitionDurations(VideoTransition transition, double span)
        {
            var motion = transition ?? new VideoTransition();
            double fallback = Finite(motion.seconds) && motion.seconds > 0 ? Math.Max(.01, Math.Min(60, motion.seconds)) : .6;
            double enter = motion.enterSeconds.HasValue && Finite(motion.enterSeconds.Value) && motion.enterSeconds.Value > 0 ? Math.Max(.01, Math.Min(60, motion.enterSeconds.Value)) : fallback;
            double exit = motion.exitSeconds.HasValue && Finite(motion.exitSeconds.Value) && motion.exitSeconds.Value > 0 ? Math.Max(.01, Math.Min(60, motion.exitSeconds.Value)) : fallback;
            double scale = Math.Min(1, Math.Max(.000001, span) / (enter + exit));
            return new[] { enter * scale, exit * scale };
        }
        static string Axis(string direction, string negative, string positive, string value)
        {
            return direction == negative ? "(-1*" + value + ")" : direction == positive ? value : "0";
        }
        internal static string Run(string exe, IEnumerable<string> options, CancellationToken cancel, Action<double> progress,
            Action<string> outputLine = null, bool background = false)
        {
            cancel.ThrowIfCancellationRequested();
            var args = new List<string>();
            if (Path.GetFileName(exe).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            {
                args.AddRange(new[] { "-hide_banner", "-nostdin", "-loglevel", "error" });
                if (progress != null) args.AddRange(new[] { "-progress", "pipe:1", "-nostats" });
            }
            args.AddRange(options);
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using (var process = new Process { StartInfo = new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote))) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8
            } })
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null) return;
                    if (outputLine != null) outputLine(e.Data);
                    else if (progress == null) stdout.AppendLine(e.Data);
                    else if (e.Data.StartsWith("out_time_us=", StringComparison.Ordinal))
                    {
                        long micros;
                        if (long.TryParse(e.Data.Substring(12), out micros)) progress(micros / 1000000.0);
                    }
                };
                process.ErrorDataReceived += (sender, e) => { if (e.Data != null && stderr.Length < 24000) stderr.AppendLine(e.Data); };
                process.Start();
                if (background)
                    try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }
                using (cancel.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                }))
                {
                    process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
                }
                cancel.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new IOException("動画処理に失敗しました。\n" + stderr.ToString().Trim());
                return stdout.ToString();
            }
        }
        static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
}

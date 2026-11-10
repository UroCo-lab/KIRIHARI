using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Kirihari
{
    public sealed class ProxyVariant
    {
        public string state, url;
        public long bytes;
        public int width, height;
        internal string file;
    }

    internal sealed class ProxyProfile
    {
        internal readonly string Level;
        internal readonly int Height, Crf;
        internal static readonly string[] Levels = { "H", "M", "L" };

        ProxyProfile(string level, int height, int crf)
        {
            Level = level; Height = height; Crf = crf;
        }

        internal static ProxyProfile Get(string level)
        {
            switch (level)
            {
                case "H": return new ProxyProfile(level, 720, 21);
                case "M": return new ProxyProfile(level, 480, 24);
                case "L": return new ProxyProfile(level, 360, 27);
                default: throw new IOException("プロキシ画質を選んでください。");
            }
        }

        internal int[] Size(VideoSource asset)
        {
            double ratio = Math.Min(1, Height / (double)asset.height);
            return new[] { Math.Max(2, (int)(asset.width * ratio + 1e-8) / 2 * 2),
                Math.Max(2, (int)(asset.height * ratio + 1e-8) / 2 * 2) };
        }

        internal string Recipe
        {
            get { return "h264-aac-pts-v2/height=" + Height + "/crf=" + Crf + "/gop=0.5s"; }
        }
    }

    internal sealed partial class MediaTool
    {
        string proxyFrameSync;

        internal string ProxyFrameSync(CancellationToken cancel)
        {
            if (proxyFrameSync != null) return proxyFrameSync;
            bool supportsMode = false;
            Run(Encoder, new[] { "-h", "full" }, cancel, null,
                line => { if (line.StartsWith("-fps_mode", StringComparison.Ordinal)) supportsMode = true; }, true);
            return proxyFrameSync = supportsMode ? "-fps_mode" : "-vsync";
        }

        internal VideoSource MakePreview(VideoSource source, ProxyProfile profile, string destination,
            CancellationToken cancel, Action<double> progress)
        {
            var timeBase = VideoTimeBase(source);
            var size = profile.Size(source);
            int keyframes = Math.Max(1, (int)Math.Ceiling(source.fps / 2));
            string sync = ProxyFrameSync(cancel);
            // Allow FFmpeg to conceal damaged blocks. Only publish copies that keep every presentation timestamp.
            var options = new List<string> { "-y", "-copyts", "-start_at_zero", "-filter_threads", "2",
                "-threads", "2", "-protocol_whitelist", "file,pipe", "-i", source.path,
                "-map", "0:V:0", "-map", "0:a:0?", "-map_metadata", "-1", "-map_chapters", "-1",
                "-vf", "scale=" + size[0] + ":" + size[1],
                sync, sync == "-fps_mode" ? "passthrough" : "0", "-enc_time_base:v", timeBase[0] + ":" + timeBase[1],
                "-video_track_timescale", timeBase[1].ToString(CultureInfo.InvariantCulture),
                "-c:v", "libx264", "-threads:v", "2", "-preset", "ultrafast", "-crf",
                profile.Crf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p",
                "-g", keyframes.ToString(CultureInfo.InvariantCulture), "-force_key_frames", "expr:gte(t,n_forced*0.5)",
                "-sc_threshold", "0", "-bf", "0" };
            options.AddRange(source.audioCodec == "aac" ? new[] { "-c:a", "copy" } : new[] { "-c:a", "aac", "-b:a", "128k" });
            options.AddRange(new[] { "-avoid_negative_ts", "disabled", "-movflags", "+faststart", destination });
            Run(Encoder, options, cancel, progress, background: true);
            var output = Read(destination, cancel);
            if (output.codec != "h264" || output.width != size[0] || output.height != size[1] || output.audio != source.audio ||
                Math.Abs(output.duration - source.duration) > Math.Max(.05, 1 / source.fps) ||
                Math.Abs((output.videoStart - output.formatStart) - (source.videoStart - source.formatStart)) > .001)
                throw new IOException("編集用コピーの解像度・長さ・開始時刻を元動画と照合できませんでした。");
            if (source.audio &&
                (Math.Abs((source.audioStart - source.formatStart) - (output.audioStart - output.formatStart)) > .05 ||
                 (source.audioDuration > 0 && Math.Abs(output.audioDuration - source.audioDuration) > .05)))
                throw new IOException("編集用コピーの音声時刻が元動画と一致しませんでした。");
            if (!PreviewTimeline(source, cancel).Matches(PreviewTimeline(output, cancel)))
                throw new IOException("元動画とフレーム時刻が一致しないため、編集用コピーを使えません。元の表示を維持します。");
            return output;
        }

        static int[] VideoTimeBase(VideoSource source)
        {
            string[] parts = source.timeBase.Split('/');
            int numerator, denominator;
            if (parts.Length != 2 || !int.TryParse(parts[0], out numerator) || !int.TryParse(parts[1], out denominator) ||
                numerator <= 0 || denominator <= 0)
                throw new IOException("動画のフレーム時刻の単位を取得できません。");
            return new[] { numerator, denominator };
        }

        struct TimelineSignature
        {
            internal long Count;
            ulong sum, squared;

            internal void Add(long time)
            {
                // B-frame decoding can reorder packets. Compare the multiset of presentation timestamps.
                unchecked
                {
                    ulong hash = (ulong)time + 0x9e3779b97f4a7c15UL;
                    hash = (hash ^ (hash >> 30)) * 0xbf58476d1ce4e5b9UL;
                    hash = (hash ^ (hash >> 27)) * 0x94d049bb133111ebUL;
                    hash ^= hash >> 31;
                    sum += hash;
                    squared += hash * hash;
                    Count++;
                }
            }

            internal bool Matches(TimelineSignature other)
            {
                return Count > 0 && Count == other.Count && sum == other.sum && squared == other.squared;
            }
        }

        TimelineSignature PreviewTimeline(VideoSource source, CancellationToken cancel)
        {
            var timeBase = VideoTimeBase(source);
            double microsPerTick = timeBase[0] * 1000000.0 / timeBase[1];
            double start = Math.Round(source.videoStart * 1000000 / microsPerTick);
            var signature = new TimelineSignature();
            bool missingTime = false;
            Run(Probe, new[] { "-v", "error", "-protocol_whitelist", "file,pipe", "-select_streams", "V:0",
                "-show_packets", "-show_entries", "packet=pts,flags", "-of", "compact=p=0", source.path }, cancel, null,
                line =>
                {
                    if (!line.StartsWith("pts=", StringComparison.Ordinal)) return;
                    int end = line.IndexOf('|');
                    if (end >= 0 && line.IndexOf("|flags=", StringComparison.Ordinal) == end)
                    {
                        int flagsEnd = line.IndexOf('|', end + 7);
                        int discarded = line.IndexOf('D', end + 7);
                        if (discarded >= 0 && (flagsEnd < 0 || discarded < flagsEnd)) return;
                    }
                    long ticks;
                    if (long.TryParse(end < 0 ? line.Substring(4) : line.Substring(4, end - 4),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
                        signature.Add((long)Math.Round((ticks - start) * microsPerTick));
                    else missingTime = true;
                }, true);
            if (missingTime) throw new IOException("時刻のないフレームを含むため、編集用コピーを検証できません。");
            return signature;
        }
    }
}

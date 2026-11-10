using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Kirihari
{
    public sealed class VideoOutput
    {
        public string mode;
        public int width, height, audioKbps;
        public double fps, videoMbps;
    }
    public sealed class ExportProfile
    {
        public string mode;
        public int width, height, audioKbps, crf;
        public double fps, videoMbps;
    }
    internal sealed partial class MediaTool
    {
        internal static ExportProfile ResolveOutput(VideoSource source, VideoOutput value)
        {
            string mode = value == null ? "legacy" : value.mode;
            var result = new ExportProfile { mode = mode, fps = source.fps, audioKbps = 160, crf = 20 };
            int[] size;
            switch (mode)
            {
                case "legacy": size = OutputSize(source); break;
                case "original":
                    size = new[] { source.width / 2 * 2, source.height / 2 * 2 }; result.crf = 18; break;
                case "conference":
                    size = FitSize(source, 1280, 720); result.fps = Math.Min(30, source.fps);
                    result.videoMbps = 3; result.audioKbps = 128; break;
                case "youtube":
                    size = FitSize(source, 1920, 1080); result.fps = Math.Min(60, source.fps);
                    result.videoMbps = result.fps > 30 ? 12 : 8; result.audioKbps = 384; break;
                case "custom":
                    size = new[] { value.width, value.height }; result.fps = value.fps;
                    result.videoMbps = value.videoMbps; result.audioKbps = value.audioKbps;
                    if (!Finite(result.videoMbps) || result.videoMbps < .1 || result.videoMbps > 200 ||
                        result.audioKbps < 64 || result.audioKbps > 384)
                        throw new IOException("映像は0.1〜200 Mbps、音声は64〜384 kbpsで指定してください。");
                    break;
                default: throw new IOException("書き出しモードを選んでください。");
            }
            result.width = size[0]; result.height = size[1];
            if (result.width < 2 || result.height < 2 || result.width > 8192 || result.height > 8192 ||
                (long)result.width * result.height > 33554432 || result.width % 2 != 0 || result.height % 2 != 0)
                throw new IOException("幅・高さは2〜8192の偶数で、合計約3355万画素以内にしてください。");
            if (!Finite(result.fps) || result.fps < 1 || result.fps > 240)
                throw new IOException("fpsは1〜240で指定してください。");
            return result;
        }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static int[] FitSize(VideoSource source, int longer, int shorter)
        {
            double scale = Math.Min(1, Math.Min((double)longer / Math.Max(source.width, source.height),
                (double)shorter / Math.Min(source.width, source.height)));
            return new[] { Math.Max(2, (int)(source.width * scale) / 2 * 2), Math.Max(2, (int)(source.height * scale) / 2 * 2) };
        }
        internal static object DescribeExport(VideoSource source, VideoExport request, IDictionary<string, VideoSource> assets)
        {
            ExportProfile profile = ResolveOutput(source, request.output);
            var cuts = request.ranges ?? new VideoRange[0];
            var holds = request.pauses ?? new VideoPause[0];
            double seconds = (cuts.Sum(c => (double)CutBodyFrames(c, CutSpeed(c))) + holds.Sum(p => (double)PauseFrames(p, source.fps))) / source.fps;
            bool audio = !request.muted && cuts.Any(c => c.mediaId == null ? source.audio : assets.ContainsKey(c.mediaId) && assets[c.mediaId].audio);
            // Constant-quality exports have no target bitrate; source file throughput is only a rough reference.
            double megabits = profile.videoMbps > 0 ? profile.videoMbps : source.bytes * 8.0 / Math.Max(.001, source.duration) / 1000000;
            return new { profile = profile, seconds = seconds, audio = audio,
                estimatedBytes = Math.Max(0, seconds * (megabits * 1000000 + (audio ? profile.audioKbps * 1000 : 0)) / 8 * 1.02),
                variableQuality = profile.videoMbps == 0 };
        }
        static void EncodingArguments(List<string> args, ExportProfile profile)
        {
            args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p" });
            if (profile.videoMbps > 0) args.AddRange(new[] { "-b:v", Num(profile.videoMbps) + "M" });
            else args.AddRange(new[] { "-crf", profile.crf.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
    }
}

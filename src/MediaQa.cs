using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace Kirihari
{
    internal static class MediaQa
    {
        internal static void Run(string directory, MediaTool selectedTool = null)
        {
            Directory.CreateDirectory(directory);
            string app = AppDomain.CurrentDomain.BaseDirectory;
            var tool = selectedTool ?? MediaTool.Find(app);
            Qa.Check(tool != null, "FFmpeg available");
            string sourcePath = Path.Combine(directory, "架空 元動画.mp4");
            File.Copy(Path.Combine(Program.QaSamples, "optical-motion.mp4"), sourcePath, true);
            byte[] sourceBytes = File.ReadAllBytes(sourcePath);
            var cancel = CancellationToken.None;
            string frameSync = MediaTool.Run(tool.Encoder, new[] { "-h", "full" }, cancel, null).Contains("-fps_mode") ? "-fps_mode" : "-vsync";
            var source = tool.Read(sourcePath, cancel);
            Qa.Check(source.frames == 24 && source.audio && source.width == 128, "Source metadata");
            string work = Path.Combine(directory, "作業");
            Directory.CreateDirectory(work);
            string slide = Project.Id();
            using (var overlay = new Bitmap(source.width, source.height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(overlay)) graphics.FillRectangle(Brushes.Magenta, 24, 14, 78, 42);
                overlay.Save(MediaTool.ImageFile(work, slide), ImageFormat.Png);
            }
            var settings = new VideoExport {
                folder = directory, muted = true,
                ranges = new[] { new VideoRange { id = "a", start = 0, end = 7 }, new VideoRange { id = "b", start = 12, end = 22 } },
                slides = new[] { new VideoSlide { id = slide, target = "b", start = 12, end = 22,
                    transition = new VideoTransition { kind = "fade", seconds = .25 } } }
            };
            string muted = tool.Export(source, settings, work, cancel, null);
            var result = tool.Read(muted, cancel);
            Qa.Check(result.codec == "h264" && result.frames == 17 && !result.audio, "17 H.264 frames, no audio stream");
            string frame = Path.Combine(directory, "合成確認.png");
            MediaTool.Run(tool.Encoder, new[] { "-y", "-ss", "1", "-i", muted, "-frames:v", "1", frame }, cancel, null);
            using (var picture = new Bitmap(frame))
            {
                var pixel = picture.GetPixel(64, 35);
                Qa.Check(pixel.R > 200 && pixel.B > 200 && pixel.G < 60, "Overlay appears in the encoded frame");
            }
            settings.muted = false;
            settings.slides[0].transition = new VideoTransition { kind = "slide", enter = "left", exit = "up", seconds = .25 };
            string withAudio = tool.Export(source, settings, work, cancel, null);
            result = tool.Read(withAudio, cancel);
            Qa.Check(result.frames == 17 && result.audio, "Multi-cut MP4 retains audio when requested");
            settings.pauses = new[] { new VideoPause { target = "b", frame = 12, seconds = .5 },
                new VideoPause { target = "b", frame = 16, seconds = .75 } };
            settings.slides = new VideoSlide[0]; // Check frozen footage independently of animated overlays.
            string paused = tool.Export(source, settings, work, cancel, null);
            result = tool.Read(paused, cancel);
            int firstHold = (int)Math.Round(.5 * source.fps);
            int secondHold = (int)Math.Round(.75 * source.fps);
            Qa.Check(result.frames == 17 + firstHold + secondHold && result.audio, "Both freezes extend the audio and video export");
            string heldFrames = Path.Combine(directory, "freeze-%d.png");
            MediaTool.Run(tool.Encoder, new[] { "-y", "-i", paused, "-vf",
                "select=eq(n\\,7)+eq(n\\," + (7 + firstHold - 1) + ")+eq(n\\," + (7 + firstHold + 4) +
                ")+eq(n\\," + (7 + firstHold + 4 + secondHold - 1) + ")", frameSync, "vfr", heldFrames }, cancel, null);
            Qa.Check(FrameDifference(directory, 1, 2) < 3 && FrameDifference(directory, 3, 4) < 3 &&
                FrameDifference(directory, 1, 3) > .1, "Each hold freezes its own distinct footage frame");
            string silence = Path.Combine(directory, "freeze-audio.pcm");
            MediaTool.Run(tool.Encoder, new[] { "-y", "-ss", "0.75", "-i", paused, "-t", "0.2",
                "-map", "0:a:0", "-f", "s16le", silence }, cancel, null);
            byte[] samples = File.ReadAllBytes(silence);
            int peak = 0;
            for (int i = 0; i < samples.Length - 1; i += 2)
                peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(samples, i)));
            Qa.Check(samples.Length > 1000 && peak < 30, "The audio is silent during the first freeze");
            settings.pauses = null;
            settings.ranges = new[] { new VideoRange { id = "one", start = 23, end = 24 } };
            settings.slides = new VideoSlide[0];
            string single = tool.Export(source, settings, work, cancel, null);
            Qa.Check(tool.Read(single, cancel).frames == 1, "Last-frame-only cut is playable");
            settings.ranges = new[] { new VideoRange { id = "hold", start = 0, end = 24 } };
            settings.pauses = new[] { new VideoPause { target = "hold", frame = 12, seconds = .5 } };
            settings.slides = new[] { new VideoSlide { id = slide, target = "hold", start = 12, end = 12, pauseOnly = true } };
            string onlyPause = tool.Export(source, settings, work, cancel, null);
            Qa.Check(tool.Read(onlyPause, cancel).frames == 30, "Pause-only caption retains the six-frame hold");
            for (int i = 0; i < 3; i++)
            {
                string probeFrame = Path.Combine(directory, "pause-caption-" + i + ".png");
                MediaTool.Run(tool.Encoder, new[] { "-y", "-ss", MediaTool.Num(new[] { .75, 1.25, 1.75 }[i]), "-i", onlyPause, "-frames:v", "1", probeFrame }, cancel, null);
                using (var picture = new Bitmap(probeFrame))
                {
                    Color pixel = picture.GetPixel(64, 35);
                    bool magenta = pixel.R > 200 && pixel.B > 200 && pixel.G < 60;
                    Qa.Check(magenta == (i == 1), "Pause-only caption is absent before/after and present during the hold");
                }
            }
            settings.slides = new VideoSlide[0];
            settings.ranges = new[] { new VideoRange { id = "front", start = 0, end = 6 }, new VideoRange { id = "hold", start = 12, end = 24 } };
            settings.output = new VideoOutput { mode = "custom", width = 256, height = 144, fps = 24, videoMbps = 1, audioKbps = 128 };
            string custom = tool.Export(source, settings, work, cancel, null);
            var customVideo = tool.Read(custom, cancel);
            Qa.Check(customVideo.width == 256 && customVideo.height == 144 && Math.Abs(customVideo.fps - 24) < .001 && customVideo.frames == 48 && customVideo.audio,
                "Custom size, fps and audio survive cuts/hold and concatenation");
            var high = new VideoSource { width = 3840, height = 2160, fps = 59.94 };
            var original = MediaTool.ResolveOutput(high, new VideoOutput { mode = "original" });
            var conference = MediaTool.ResolveOutput(high, new VideoOutput { mode = "conference" });
            var youtube = MediaTool.ResolveOutput(high, new VideoOutput { mode = "youtube" });
            Qa.Check(original.width == 3840 && original.fps == 59.94 && conference.width == 1280 && conference.fps == 30 &&
                youtube.width == 1920 && youtube.fps == 59.94 && youtube.videoMbps == 12, "Preset geometry, fps limits and high-fps bitrate");
            bool rejected = false;
            try { MediaTool.ResolveOutput(source, new VideoOutput { mode = "custom", width = 255, height = 144, fps = 24, videoMbps = 1, audioKbps = 128 }); }
            catch (IOException) { rejected = true; }
            Qa.Check(rejected, "Odd output dimensions are rejected natively");
            string highPath = Path.Combine(directory, "元サイズ確認.mp4");
            MediaTool.Run(tool.Encoder, new[] { "-y", "-f", "lavfi", "-i", "testsrc2=s=2560x1440:r=30000/1001",
                "-frames:v", "2", "-an", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "35", highPath }, cancel, null);
            var highSource = tool.Read(highPath, cancel);
            string highOutput = tool.Export(highSource, new VideoExport { folder = directory, muted = true,
                ranges = new[] { new VideoRange { id = "original", start = 0, end = 2 } },
                output = new VideoOutput { mode = "original" } }, work, cancel, null);
            var highResult = tool.Read(highOutput, cancel);
            Qa.Check(highResult.width == 2560 && highResult.height == 1440 && highResult.frames == 2 && Math.Abs(highResult.fps - 30000.0 / 1001) < .0001,
                "Original mode exports above-FHD resolution and retains fractional fps");
            settings.ranges = new[] { new VideoRange { id = "effect", start = 0, end = 24 } };
            settings.output = null; settings.pauses = null; settings.muted = true;
            foreach (string kind in new[] { "fade", "slide" })
            {
                settings.slides = new[] { new VideoSlide { id = slide, target = "effect", start = 0, end = 24,
                    transition = new VideoTransition { kind = kind, enter = "left", exit = "right", enterSeconds = .1, exitSeconds = .8 } } };
                string fastInSlowOut = tool.Export(source,settings,work,cancel,null);
                settings.slides[0].transition.enterSeconds = .8; settings.slides[0].transition.exitSeconds = .1;
                string slowInFastOut = tool.Export(source,settings,work,cancel,null);
                int a = TransitionPixelDistance(tool,fastInSlowOut,.25,Path.Combine(directory,kind+"-fast-in.png"));
                int b = TransitionPixelDistance(tool,slowInFastOut,.25,Path.Combine(directory,kind+"-slow-in.png"));
                int c = TransitionPixelDistance(tool,fastInSlowOut,1.75,Path.Combine(directory,kind+"-slow-out.png"));
                int d = TransitionPixelDistance(tool,slowInFastOut,1.75,Path.Combine(directory,kind+"-fast-out.png"));
                Qa.Check(a+50<b && d+50<c,kind+" has independently fast/slow entry and exit in the encoded MP4");
            }
            double[] shortened = MediaTool.TransitionDurations(new VideoTransition { enterSeconds = .2, exitSeconds = .8 },.5);
            Qa.Check(Math.Abs(shortened[0]-.1)<.000001 && Math.Abs(shortened[1]-.4)<.000001,"Short transitions retain the entry/exit duration ratio");
            VerifyCutSpeeds(tool, source, directory, work, slide);
            VerifyProxyTimestamps(tool, directory, cancel);
            VerifyDamagedProxy(tool, directory, cancel);
            Qa.Check(System.Linq.Enumerable.SequenceEqual(sourceBytes, File.ReadAllBytes(sourcePath)), "Original bytes preserved");
            File.WriteAllLines(Path.Combine(directory, "video-results.txt"), new[] {
                "PASS: recoverable H.264 damage stops strict decoding but the proxy retains every timestamp and decodes in full.",
                "PASS: proxy H/M/L sizing with no upscale, 30/60 fps packet timestamps and B-frame VFR timing, delayed AAC audio and full decode.",
                "PASS: metadata, multiple cuts, H.264 MP4, audio mute and retention, fade and slide filters, overlay pixels, two freeze frames, silence during a freeze, last-frame export, unchanged source.",
                "PASS: pause-only overlay timing before/during/after hold; custom size/fps/audio output, original/HD/FHD preset resolution and rate validation.",
                "PASS: independent .1/.8-second entry and exit in fade/slide MP4 pixels, reversed duration comparison and proportional short-caption limits.",
                "PASS: mixed 0.1x, 1.6x and 16x MP4 with audio, retimed captions, fixed pause and native speed validation.",
                muted, withAudio, paused, single
            });
        }

        static void VerifyProxyTimestamps(MediaTool tool, string directory, CancellationToken cancel)
        {
            foreach (int fps in new[] { 30, 60 })
            {
                string sourcePath = Path.Combine(directory, "proxy-cfr-" + fps + ".mp4");
                MediaTool.Run(tool.Encoder, new[] { "-y", "-f", "lavfi", "-i", "testsrc2=s=640x960:r=" + fps + ":d=1.5",
                    "-c:v", "libx264", "-preset", "ultrafast", "-threads:v", "2", sourcePath }, cancel, null);
                var source = tool.Read(sourcePath, cancel);
                foreach (string level in ProxyProfile.Levels)
                {
                    var profile = ProxyProfile.Get(level);
                    var output = tool.MakePreview(source, profile, Path.Combine(directory, "proxy-" + fps + "-" + level + ".mp4"), cancel, null);
                    int width = level == "H" ? 480 : level == "M" ? 320 : 240;
                    Qa.Check(output.width == width && output.height == profile.Height && output.fps == fps &&
                        PacketTimes(tool, source.path, "V:0", cancel).SequenceEqual(PacketTimes(tool, output.path, "V:0", cancel)),
                        "CFR proxy keeps every timestamp at " + fps + " fps and scales by portrait height");
                }
            }
            string vfr = Path.Combine(directory, "proxy-vfr.mp4");
            string sync = tool.ProxyFrameSync(cancel);
            MediaTool.Run(tool.Encoder, new[] { "-y", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=60:d=2",
                "-itsoffset", "0.125", "-f", "lavfi", "-i", "sine=sample_rate=48000:d=1.8",
                "-vf", "select='not(mod(n,2))+not(mod(n,5))'", sync, sync == "-fps_mode" ? "passthrough" : "0",
                "-c:v", "libx264", "-preset", "ultrafast", "-bf", "2", "-c:a", "aac", vfr }, cancel, null);
            var variable = tool.Read(vfr, cancel);
            var converted = tool.MakePreview(variable, ProxyProfile.Get("M"), Path.Combine(directory, "proxy-vfr-M.mp4"), cancel, null);
            var times = PacketTimes(tool, vfr, "V:0", cancel);
            Qa.Check(times.Length < 120 && converted.width == 320 && converted.height == 180 &&
                times.SequenceEqual(PacketTimes(tool, converted.path, "V:0", cancel)) &&
                PacketTimes(tool, vfr, "a:0", cancel).SequenceEqual(PacketTimes(tool, converted.path, "a:0", cancel)),
                "VFR presentation times and delayed audio packets match the source without upscaling");
            MediaTool.Run(tool.Encoder, new[] { "-xerror", "-i", converted.path, "-f", "null", "-" }, cancel, null);
        }

        static void VerifyDamagedProxy(MediaTool tool, string directory, CancellationToken cancel)
        {
            string path = Path.Combine(directory, "proxy-damaged.mp4");
            MediaTool.Run(tool.Encoder, new[] { "-y", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=2",
                "-c:v", "libx264", "-threads:v", "2", "-preset", "ultrafast",
                "-bsf:v", @"noise=amount=if(eq(n\,30)\,40\,0)", path }, cancel, null);
            bool rejected = false;
            try { MediaTool.Run(tool.Encoder, new[] { "-xerror", "-threads", "2", "-i", path, "-f", "null", "-" }, cancel, null); }
            catch (IOException) { rejected = true; }
            Qa.Check(rejected, "The damaged H.264 fixture fails strict decoding");
            var input = tool.Read(path, cancel);
            var output = tool.MakePreview(input, ProxyProfile.Get("L"), Path.Combine(directory, "proxy-damaged-L.mp4"), cancel, null);
            Qa.Check(PacketTimes(tool, path, "V:0", cancel).SequenceEqual(PacketTimes(tool, output.path, "V:0", cancel)),
                "Concealing H.264 damage retains every source presentation timestamp");
            MediaTool.Run(tool.Encoder, new[] { "-xerror", "-i", output.path, "-f", "null", "-" }, cancel, null);
        }

        static string[] PacketTimes(MediaTool tool, string path, string stream, CancellationToken cancel)
        {
            return MediaTool.Run(tool.Probe, new[] { "-v", "error", "-select_streams", stream, "-show_packets",
                "-show_entries", "packet=pts_time", "-of", "csv=p=0", path }, cancel, null)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(',')[0]).OrderBy(line => line).ToArray();
        }

        static void VerifyCutSpeeds(MediaTool tool, VideoSource source, string directory, string work, string slide)
        {
            var cancel = CancellationToken.None;
            var settings = new VideoExport {
                folder = directory,
                ranges = new[] {
                    new VideoRange { id = "slow", start = 0, end = 8, speed = .1 },
                    new VideoRange { id = "middle", start = 8, end = 16, speed = 1.6 },
                    new VideoRange { id = "fast", start = 16, end = 24, speed = 16 }
                },
                pauses = new[] { new VideoPause { target = "slow", frame = 4, seconds = .5 } },
                slides = new[] { new VideoSlide { id = slide, target = "slow", start = 4, end = 4, pauseOnly = true } }
            };
            string exported = tool.Export(source, settings, work, cancel, null);
            var video = tool.Read(exported, cancel);
            Qa.Check(video.frames == 92 && video.audio, "Mixed cut speeds and the half-second hold export at the planned frame count with audio: " + video.frames);
            for (int i = 0; i < 3; i++)
            {
                int distance = TransitionPixelDistance(tool, exported, new[] { 3.1, 3.5, 4.1 }[i],
                    Path.Combine(directory, "retimed-hold-" + i + ".png"));
                Qa.Check((distance < 80) == (i == 1), "Pause-only caption is confined to the retimed hold");
            }
            string silent = Path.Combine(directory, "retimed-hold-audio.pcm");
            MediaTool.Run(tool.Encoder, new[] { "-y", "-ss", "3.45", "-i", exported, "-t", "0.2",
                "-map", "0:a:0", "-f", "s16le", silent }, cancel, null);
            var samples = File.ReadAllBytes(silent);
            int peak = 0;
            for (int i = 0; i < samples.Length - 1; i += 2)
                peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(samples, i)));
            Qa.Check(samples.Length > 1000 && peak < 30, "The pause remains silent inside a slow cut");
            settings.output = new VideoOutput { mode = "custom", width = 128, height = 72, fps = 24, videoMbps = 1, audioKbps = 128 };
            var doubled = tool.Read(tool.Export(source, settings, work, cancel, null), cancel);
            Qa.Check(doubled.frames == 184 && doubled.fps == 24 && doubled.audio,
                "Changing export fps preserves the retimed duration and audio: " + doubled.frames);
            settings.ranges[2].speed = 16.1;
            bool rejected = false;
            try { MediaTool.DescribeExport(source, settings, null); }
            catch (IOException) { rejected = true; }
            Qa.Check(rejected, "Out-of-range cut speed is rejected natively");
        }

        static int TransitionPixelDistance(MediaTool tool,string path,double seconds,string frame)
        {
            MediaTool.Run(tool.Encoder,new[] { "-y","-ss",MediaTool.Num(seconds),"-i",path,"-frames:v","1",frame },CancellationToken.None,null);
            using (var image = new Bitmap(frame))
            {
                Color p = image.GetPixel(64,35);
                return 255-p.R+p.G+255-p.B;
            }
        }

        static double FrameDifference(string directory, int first, int second)
        {
            using (var a = new Bitmap(Path.Combine(directory, "freeze-" + first + ".png")))
            using (var b = new Bitmap(Path.Combine(directory, "freeze-" + second + ".png")))
            {
                long difference = 0;
                for (int y = 0; y < a.Height; y++)
                    for (int x = 0; x < a.Width; x++)
                    {
                        Color left = a.GetPixel(x, y), right = b.GetPixel(x, y);
                        difference += Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);
                    }
                return difference / (3.0 * a.Width * a.Height);
            }
        }
    }
}

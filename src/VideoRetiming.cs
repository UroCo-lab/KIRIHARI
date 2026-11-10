using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kirihari
{
    internal sealed partial class MediaTool
    {
        static double CutSpeed(VideoRange cut)
        {
            double speed = cut.speed ?? 1;
            if (!Finite(speed) || speed < .1 || speed > 16)
                throw new IOException("カットの速度は0.1〜16倍で指定してください。");
            return speed;
        }

        static int CutFrame(int offset, double speed)
        {
            return (int)Math.Round(offset / speed, MidpointRounding.AwayFromZero);
        }

        static int CutBodyFrames(VideoRange cut, double speed)
        {
            return Math.Max(1, CutFrame(cut.end - cut.start, speed));
        }

        struct CutPart
        {
            internal int offset, sourceFrames, frames;
            internal bool IsHold { get { return sourceFrames == 0; } }
        }

        static List<CutPart> RetimedParts(VideoRange cut, VideoPause[] holds, double speed, double fps)
        {
            // Round cumulative motion boundaries so adjacent parts always add up to the cut's frame count.
            var parts = new List<CutPart>();
            int start = 0, first = 0;
            foreach (var hold in holds)
            {
                int offset = hold.frame - cut.start;
                int boundary = CutFrame(offset, speed);
                if (boundary > first)
                    parts.Add(new CutPart { offset = start, sourceFrames = offset - start, frames = boundary - first });
                parts.Add(new CutPart { offset = offset, frames = PauseFrames(hold, fps) });
                start = offset;
                first = boundary;
            }
            int remaining = CutBodyFrames(cut, speed) - first;
            if (remaining > 0)
                parts.Add(new CutPart { offset = start, sourceFrames = cut.end - cut.start - start, frames = remaining });
            return parts;
        }

        static string AddRetimedVideo(StringBuilder graph, List<CutPart> parts, string rate)
        {
            if (parts.Count > 1)
            {
                graph.Append(";[v0]split=").Append(parts.Count);
                for (int i = 0; i < parts.Count; i++) graph.Append("[speedInput").Append(i).Append(']');
            }
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                graph.Append(parts.Count == 1 ? ";[v0]" : ";[speedInput" + i + "]")
                    .Append("trim=start_frame=").Append(part.offset).Append(":end_frame=")
                    .Append(part.offset + (part.IsHold ? 1 : part.sourceFrames));
                if (part.IsHold)
                    graph.Append(",tpad=stop_mode=clone:stop=").Append(part.frames - 1);
                else
                    graph.Append(",settb=AVTB,setpts=(PTS-STARTPTS)*").Append(part.frames).Append('/').Append(part.sourceFrames)
                        .Append(",fps=").Append(rate).Append(":round=up:eof_action=pass,tpad=stop_mode=clone:stop=-1,trim=end_frame=").Append(part.frames);
                graph.Append(",setpts=N/(").Append(rate).Append("*TB)[speedPart").Append(i).Append(']');
            }
            if (parts.Count == 1) return "speedPart0";
            graph.Append(';');
            for (int i = 0; i < parts.Count; i++) graph.Append("[speedPart").Append(i).Append(']');
            graph.Append("concat=n=").Append(parts.Count).Append(":v=1:a=0,setpts=N/(").Append(rate).Append("*TB)[retimed]");
            return "retimed";
        }

        static string AddRetimedAudio(StringBuilder graph, List<CutPart> parts, double sourceDuration, double fps)
        {
            int motionParts = parts.Count(part => !part.IsHold);
            graph.Append(";[0:a:0]aresample=48000:async=1:first_pts=0,aformat=channel_layouts=stereo,apad,atrim=duration=")
                .Append(Num(sourceDuration));
            if (motionParts > 1) graph.Append(",asplit=").Append(motionParts);
            for (int i = 0; i < motionParts; i++) graph.Append("[tempoInput").Append(i).Append(']');
            int motion = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.IsHold)
                    graph.Append(";anullsrc=r=48000:cl=stereo");
                else
                {
                    graph.Append(";[tempoInput").Append(motion++).Append("]atrim=start=").Append(Num(part.offset / fps))
                        .Append(":end=").Append(Num((part.offset + part.sourceFrames) / fps)).Append(",asetpts=PTS-STARTPTS");
                    AppendTempo(graph, (double)part.sourceFrames / part.frames);
                    graph.Append(",apad");
                }
                graph.Append(",atrim=duration=").Append(Num(part.frames / fps)).Append("[tempoPart").Append(i).Append(']');
            }
            if (parts.Count == 1) return "tempoPart0";
            graph.Append(';');
            for (int i = 0; i < parts.Count; i++) graph.Append("[tempoPart").Append(i).Append(']');
            graph.Append("concat=n=").Append(parts.Count).Append(":v=0:a=1[retimedAudio]");
            return "retimedAudio";
        }

        static void AppendTempo(StringBuilder graph, double tempo)
        {
            while (tempo > 2) { graph.Append(",atempo=2"); tempo /= 2; }
            while (tempo < .5) { graph.Append(",atempo=0.5"); tempo *= 2; }
            if (tempo != 1) graph.Append(",atempo=").Append(Num(tempo));
        }
    }
}

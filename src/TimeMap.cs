using System;
using System.Collections.Generic;
using System.Linq;

namespace Kirihari
{
    internal sealed class Part
    {
        public string Kind, Target;
        public int Start, End;
        public double From, To, Rate;
    }

    internal sealed class TimeMap
    {
        public readonly List<Part> Parts = new List<Part>();
        readonly Project project;
        readonly Source source;
        public double Duration { get; private set; }

        public TimeMap(Project p)
        {
            project = p;
            source = p.Source;
            var clips = p.Clips.OrderBy(c => c.Start).ToArray();
            var blanks = p.Effects.Where(e => e.Kind == "blank" && !clips.Any(c => c.Start <= e.Start && e.Start < c.End))
                .OrderBy(e => e.Start).ToArray();
            int blank = 0;
            foreach (var clip in clips)
            {
                while (blank < blanks.Length && blanks[blank].Start < clip.Start) Insert(blanks[blank++]);
                var effects = p.Effects.Where(e => e.ClipId == clip.Id).ToArray();
                var speeds = effects.Where(e => e.Kind == "speed" && e.Start < clip.End && e.End > clip.Start).ToArray();
                var holds = effects.Where(e => e.Kind == "freeze" && e.Start >= clip.Start && e.Start < clip.End)
                    .ToDictionary(e => e.Start);
                var edges = new SortedSet<int> { clip.Start, clip.End };
                foreach (var e in speeds)
                {
                    edges.Add(Math.Max(e.Start, clip.Start));
                    edges.Add(Math.Min(e.End, clip.End));
                }
                edges.UnionWith(holds.Keys);
                var frames = edges.ToArray();
                for (int i = 0; i + 1 < frames.Length; i++)
                {
                    int a = frames[i], b = frames[i + 1];
                    Effect freeze;
                    if (holds.TryGetValue(a, out freeze)) Insert(freeze);
                    var speed = speeds.FirstOrDefault(e => e.Start <= a && a < e.End);
                    double rate = speed == null ? 1 : speed.Rate;
                    Add("video", clip.Id, a, b, rate, (source.Seconds(b) - source.Seconds(a)) / rate);
                }
            }
            while (blank < blanks.Length) Insert(blanks[blank++]);
        }

        void Insert(Effect e) { Add(e.Kind, e.Id, e.Start, e.Start, 0, e.Frames / source.Fps); }
        void Add(string kind, string target, int start, int end, double rate, double duration)
        {
            Parts.Add(new Part { Kind = kind, Target = target, Start = start, End = end, Rate = rate,
                From = Duration, To = Duration + duration });
            Duration += duration;
        }
        public Part At(double time)
        {
            if (Parts.Count == 0) return null;
            int lo = 0, hi = Parts.Count - 1;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (time < Parts[mid].To) hi = mid;
                else lo = mid + 1;
            }
            return Parts[lo];
        }
        public double SourceFrame(Part p, double time)
        {
            if (p.Kind != "video") return p.Start;
            return Math.Min(p.End - .000001, source.Frame(source.Seconds(p.Start) +
                Math.Max(0, time - p.From) * p.Rate));
        }
        public double Seek(int frame)
        {
            foreach (var p in Parts)
            {
                if (p.Kind != "video") { if (frame <= p.Start) return p.From; }
                else if (frame < p.End)
                    return p.From + Math.Max(0, source.Seconds(frame) - source.Seconds(p.Start)) / p.Rate;
            }
            return Duration;
        }
        public double SeekTarget(string target, int offset)
        {
            var clip = project.Clips.FirstOrDefault(c => c.Id == target);
            foreach (var p in Parts.Where(p => p.Target == target))
            {
                if (clip == null) return Math.Min(p.From + offset / source.Fps, p.To - .000001);
                int frame = clip.Origin + offset;
                if (frame < p.End)
                    return p.From + Math.Max(0, source.Seconds(frame) - source.Seconds(p.Start)) / p.Rate;
            }
            return Seek(clip == null ? 0 : clip.End);
        }
        public bool Active(Caption caption, Part part, double time)
        {
            if (part == null || part.Target != caption.Target || time >= part.To) return false;
            var clip = part.Kind == "video" ? project.Clips.First(c => c.Id == part.Target) : null;
            double offset = clip == null ? (time - part.From) * source.Fps : SourceFrame(part, time) - clip.Origin;
            return offset + .000001 >= caption.Start && offset < caption.End - .000001;
        }
        public string Warning(Caption c)
        {
            var clip = project.Clips.FirstOrDefault(x => x.Id == c.Target);
            if (clip != null)
                return c.Start < clip.Start - clip.Origin || c.End > clip.End - clip.Origin ? "表示範囲がはみ出しています" : null;
            var insert = Parts.FirstOrDefault(x => x.Target == c.Target);
            if (insert == null) return "貼り付け先が見つかりません";
            return c.Start < 0 || c.End > (insert.To - insert.From) * source.Fps + .0001 ?
                "表示範囲がはみ出しています" : null;
        }
        public bool EffectActive(Effect effect)
        {
            if (effect.Kind != "speed") return Parts.Any(p => p.Target == effect.Id);
            return project.Clips.Any(c => c.Id == effect.ClipId && effect.Start < c.End && effect.End > c.Start);
        }
        public IEnumerable<Tuple<double, double>> CaptionWindows(Caption c)
        {
            var clip = project.Clips.FirstOrDefault(x => x.Id == c.Target);
            foreach (var p in Parts.Where(p => p.Target == c.Target))
            {
                if (clip == null)
                {
                    double a = p.From + Math.Max(0, c.Start) / source.Fps, b = Math.Min(p.To, p.From + c.End / source.Fps);
                    if (b > a) yield return Tuple.Create(a, b);
                }
                else
                {
                    int a = Math.Max(p.Start, c.Start + clip.Origin), b = Math.Min(p.End, c.End + clip.Origin);
                    if (b > a) yield return Tuple.Create(p.From + (source.Seconds(a) - source.Seconds(p.Start)) / p.Rate,
                        p.From + (source.Seconds(b) - source.Seconds(p.Start)) / p.Rate);
                }
            }
        }
    }

    internal sealed class History
    {
        readonly List<string> undo = new List<string>(), redo = new List<string>();
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public void Record(string before, Project after)
        {
            if (before == Files.Json(after)) return;
            Push(undo, before);
            redo.Clear();
        }
        public Project Undo(Project p) { return Transfer(p, undo, redo); }
        public Project Redo(Project p) { return Transfer(p, redo, undo); }
        static void Push(List<string> list, string snapshot)
        {
            if (list.Count == 40) list.RemoveAt(0);
            list.Add(snapshot);
        }
        static Project Transfer(Project p, List<string> from, List<string> to)
        {
            if (from.Count == 0) return p;
            Push(to, Files.Json(p));
            int last = from.Count - 1;
            var restored = Files.Parse(from[last]);
            from.RemoveAt(last);
            return restored;
        }
    }
}

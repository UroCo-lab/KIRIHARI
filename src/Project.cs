using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Kirihari
{
    public sealed class Project
    {
        public int Version { get; set; }
        public Source Source { get; set; }
        public List<Clip> Clips { get; set; }
        public List<Effect> Effects { get; set; }
        public List<Caption> Captions { get; set; }
        public int Tracks { get; set; }
        public bool Sound { get; set; }
        public bool AutoSave { get; set; }
        public string WorkFolder { get; set; }
        public string OutputFolder { get; set; }
        public string Quality { get; set; }

        public static Project Create(Source source)
        {
            return new Project {
                Version = 1, Source = source, Clips = new List<Clip>(), Effects = new List<Effect>(),
                Captions = new List<Caption>(), Tracks = 1, Quality = "標準"
            };
        }
        public static string Id() { return Guid.NewGuid().ToString("N"); }
        public Clip AddClip(int start, int end)
        {
            if (start < 0 || end > Source.Frames || end <= start ||
                Clips.Any(c => start < c.End && end > c.Start))
                throw new ArgumentException("空いている範囲を選んでください。");
            var clip = new Clip { Id = Id(), Start = start, End = end, Origin = start };
            Clips.Add(clip);
            return clip;
        }
        public static Project Demo(bool example)
        {
            var p = Create(new Source { Demo = true, Frames = 900, RateNumerator = 30, RateDenominator = 1,
                Width = 1920, Height = 1080, Audio = false });
            if (example)
            {
                p.AddClip(0, 300);
                var clip = p.AddClip(600, 900);
                p.Effects.Add(new Effect { Id = Id(), Kind = "blank", Start = 450, Frames = 150, Color = "#000000" });
                p.Effects.Add(new Effect { Id = Id(), Kind = "speed", ClipId = clip.Id, Start = 600, End = 720, Rate = 2 });
                p.Effects.Add(new Effect { Id = Id(), Kind = "freeze", ClipId = clip.Id, Start = 780, Frames = 90 });
            }
            return p;
        }

        public void Validate()
        {
            if (Version != 1 || Source == null || Clips == null || Effects == null || Captions == null ||
                Tracks < 1 || Tracks > 3 || !new[] { "元画質優先", "高画質", "標準", "軽量" }.Contains(Quality))
                throw new InvalidDataException("この編集ファイルの形式には対応していません。");
            Source.Validate();
            if (Clips.Any(c => c == null) || Effects.Any(e => e == null) || Captions.Any(c => c == null)) Invalid();
            var ids = new HashSet<string>();
            int end = 0;
            foreach (var clip in Clips.OrderBy(c => c.Start))
            {
                if (!ids.Add(clip.Id ?? "") || string.IsNullOrWhiteSpace(clip.Id) ||
                    clip.Start < end || clip.Start >= clip.End || clip.End > Source.Frames ||
                    clip.Origin < 0 || clip.Origin >= Source.Frames) Invalid();
                end = clip.End;
            }
            foreach (var e in Effects)
            {
                if (!ids.Add(e.Id ?? "") || string.IsNullOrWhiteSpace(e.Id) ||
                    e.Start < 0 || e.Start >= Source.Frames ||
                    !new[] { "blank", "freeze", "speed" }.Contains(e.Kind)) Invalid();
                if (e.Kind == "speed")
                {
                    if (e.End <= e.Start || e.End > Source.Frames || !(e.Rate >= .05 && e.Rate <= 16) ||
                        Effects.Any(x => x != e && x.Kind == "speed" && x.ClipId == e.ClipId &&
                            x.Start < e.End && x.End > e.Start)) Invalid();
                }
                else if (e.Frames < 1 || e.Frames > 1000000 || Effects.Any(x => x != e && x.Kind == e.Kind &&
                    x.ClipId == e.ClipId && x.Start == e.Start)) Invalid();
            }
            foreach (var c in Captions)
            {
                if (!ids.Add(c.Id ?? "") || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Target) ||
                    c.Track < 1 || c.Track > Tracks || !(c.End > c.Start) || c.Start < -Source.Frames ||
                    c.End > Math.Max(Source.Frames, 1000000) || !new[] { "text", "arrow", "line", "rect", "ellipse", "image" }.Contains(c.Kind) ||
                    !(c.Opacity >= 0 && c.Opacity <= 1) || !(c.Width > 0 && c.Width <= 2) ||
                    !(c.Height > 0 && c.Height <= 2) || !(c.FontSize >= 10 && c.FontSize <= 180) ||
                    !(c.X >= 0 && c.X <= 1 && c.Y >= 0 && c.Y <= 1) ||
                    double.IsInfinity(c.Angle) || double.IsNaN(c.Angle)) Invalid();
            }
        }

        internal static void Invalid() { throw new InvalidDataException("編集ファイルに無効な値があります。"); }
    }

    public sealed class Source
    {
        public bool Demo { get; set; }
        public string Path { get; set; }
        public long Bytes { get; set; }
        public bool Audio { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Frames { get; set; }
        public int RateNumerator { get; set; }
        public int RateDenominator { get; set; }
        public bool EstimatedRate { get; set; }
        // Optional source-frame boundaries in 100 ns units, including the end boundary.
        public long[] PtsTicks { get; set; }
        [ScriptIgnore] public double Fps { get { return (double)RateNumerator / RateDenominator; } }
        [ScriptIgnore] public double Duration { get { return Seconds(Frames); } }
        public double Seconds(int frame)
        {
            return PtsTicks != null && frame >= 0 && frame <= Frames
                ? (PtsTicks[frame] - PtsTicks[0]) / 10000000.0 : frame / Fps;
        }
        public double Frame(double seconds)
        {
            if (PtsTicks == null) return seconds * Fps;
            double ticks = PtsTicks[0] + seconds * 10000000.0;
            if (ticks <= PtsTicks[0]) return 0;
            if (ticks >= PtsTicks[Frames]) return Frames;
            int lo = 0, hi = Frames;
            while (hi - lo > 1)
            {
                int mid = lo + (hi - lo) / 2;
                if (PtsTicks[mid] <= ticks) lo = mid;
                else hi = mid;
            }
            return lo + (ticks - PtsTicks[lo]) / (PtsTicks[hi] - PtsTicks[lo]);
        }
        public void Validate()
        {
            if (Frames < 1 || Frames > 100000000 || Width < 1 || Height < 1 || Bytes < 0 || (!Demo && string.IsNullOrWhiteSpace(Path)) ||
                RateNumerator < 1 || RateDenominator < 1 || Fps < 1 || Fps > 1000) Project.Invalid();
            if (PtsTicks == null) return;
            if (PtsTicks.Length != Frames + 1 || PtsTicks[0] < 0 || PtsTicks[Frames] > TimeSpan.FromDays(365).Ticks) Project.Invalid();
            for (int i = 1; i < PtsTicks.Length; i++)
                if (PtsTicks[i] <= PtsTicks[i - 1]) Project.Invalid();
        }
    }
    public sealed class Clip
    {
        public string Id { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public int Origin { get; set; }
    }
    public sealed class Effect
    {
        public string Id { get; set; }
        public string ClipId { get; set; }
        public string Kind { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public int Frames { get; set; }
        public double Rate { get; set; }
        public string Color { get; set; }
    }
    public sealed class Caption
    {
        public string Id { get; set; }
        public string Target { get; set; }
        public string Kind { get; set; }
        public int Track { get; set; }
        public int Layer { get; set; }
        // Frames relative to the clip's immutable origin, or to the start of an insertion.
        public int Start { get; set; }
        public int End { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Angle { get; set; }
        public double Opacity { get; set; }
        public double FontSize { get; set; }
        public string Text { get; set; }
        public string Color { get; set; }
        public string Background { get; set; }
        public string Image { get; set; }
    }

    internal static class Files
    {
        static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 32000000, RecursionLimit = 32 };
        }
        public static string Json(Project p) { return Serializer().Serialize(p); }
        public static Project Parse(string json) { return Serializer().Deserialize<Project>(json); }

        public static string Local(string path, string folder)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(folder, path));
            if (full.StartsWith(@"\\") || full.Length < 3 || full[1] != ':' ||
                full.IndexOf(':', 2) >= 0 || new DriveInfo(Path.GetPathRoot(full)).DriveType == DriveType.Network)
                throw new IOException("PC または接続した USB の保存先を選んでください。");
            return full;
        }

        static string Relative(string path, string folder)
        {
            if (path == null) return null;
            var root = new Uri(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var file = new Uri(path);
            var relative = root.MakeRelativeUri(file);
            return relative.IsAbsoluteUri ? path : Uri.UnescapeDataString(relative.ToString());
        }

        static void ConvertPaths(Project p, Func<string, string> convert)
        {
            p.Source.Path = convert(p.Source.Path);
            p.WorkFolder = convert(p.WorkFolder);
            p.OutputFolder = convert(p.OutputFolder);
            foreach (var c in p.Captions) c.Image = convert(c.Image);
        }
        public static Project Load(string path)
        {
            path = Local(path, AppDomain.CurrentDomain.BaseDirectory);
            if (new FileInfo(path).Length > 32000000) throw new InvalidDataException("編集ファイルが大きすぎます。");
            var p = Parse(File.ReadAllText(path));
            if (p == null) Project.Invalid();
            p.Validate();
            ConvertPaths(p, x => Local(x, Path.GetDirectoryName(path)));
            return p;
        }
        public static void Save(Project p, string path)
        {
            path = Local(path, AppDomain.CurrentDomain.BaseDirectory);
            if (!string.Equals(Path.GetExtension(path), ".kirihari", StringComparison.OrdinalIgnoreCase))
                throw new IOException("編集ファイルの拡張子は .kirihari にしてください。");
            if (string.Equals(p.Source.Path, path, StringComparison.OrdinalIgnoreCase) ||
                p.Captions.Any(c => string.Equals(c.Image, path, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("元動画や画像とは別の名前を指定してください。");
            var copy = Parse(Json(p));
            var folder = Path.GetDirectoryName(path);
            ConvertPaths(copy, x => Relative(x, folder));
            var temp = Path.Combine(folder, "." + Project.Id() + ".tmp");
            try
            {
                File.WriteAllText(temp, Json(copy));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}

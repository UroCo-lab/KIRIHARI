using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Kirihari
{
    internal sealed partial class Editor
    {
        void BuildContext()
        {
            context.Children.Clear();
            var clip = P.Clips.FirstOrDefault(c => c.Id == Selected);
            var effect = P.Effects.FirstOrDefault(e => e.Id == Selected);
            var caption = P.Captions.FirstOrDefault(c => c.Id == Selected);
            if (clip != null)
            {
                Context("残す範囲", Theme.Text(clip.Start + " ～ " + (clip.End - 1) + " F", 11, Theme.Muted),
                    BoundaryButton("開始", true), BoundaryButton("終了", false),
                    Theme.Button("◀ ずらす", () => Step(-1, true)), Theme.Button("ずらす ▶", () => Step(1, true)),
                    Theme.Button("編集", () => EditRange(clip)), Theme.Button("削除", DeleteSelected));
            }
            else if (effect != null)
            {
                string detail = effect.Kind == "speed" ? effect.Start + " ～ " + (effect.End - 1) + " F   ×" + effect.Rate :
                    (effect.Frames / P.Source.Fps).ToString("0.##") + "秒 / " + effect.Frames + " F";
                if (!Map.EffectActive(effect)) detail += "   ⚠ 適用先が見つかりません";
                if (effect.Kind == "speed")
                    Context("再生速度", Theme.Text(detail, 11, Theme.Muted), BoundaryButton("開始", true), BoundaryButton("終了", false),
                        Theme.Button("◀", () => Step(-1, true)), Theme.Button("▶", () => Step(1, true)),
                        Theme.Button("編集", () => EditSpeed(effect, null)), Theme.Button("採用範囲を選ぶ", () =>
                        { Select(effect.ClipId, false); SeekFrame(effect.Start); }), Theme.Button("通常に戻す", DeleteSelected));
                else Context(effect.Kind == "blank" ? "ブランク" : "停止", Theme.Text(detail, 11, Theme.Muted),
                    Theme.Button("長さを変える", () => EditInsert(effect, effect.Kind, effect.Start, effect.ClipId)),
                    Theme.Button("削除", DeleteSelected));
            }
            else if (caption != null)
            {
                var warning = Map.Warning(caption);
                Context("テロップ " + caption.Track + "  " + CaptionLabel(caption.Kind), Theme.Text(warning ??
                    "プレビューで移動・拡縮・回転", 11, warning == null ? Theme.Muted : Theme.Coral),
                    Theme.Button("編集", () => EditCaption(caption)), Theme.Button("前面へ", () => Layer(caption, 1)),
                    Theme.Button("背面へ", () => Layer(caption, -1)), Theme.Button("削除", DeleteSelected));
            }
            else
            {
                var label = Theme.Text(DefaultHint, 11, Theme.Muted);
                label.Margin = new Thickness(9, 0, 0, 0);
                context.Children.Add(label);
            }
        }
        void Context(string label, TextBlock value, params Button[] actions)
        {
            var title = Theme.Text(label, 11, Theme.Teal, true);
            title.Margin = new Thickness(8, 0, 11, 0); context.Children.Add(title);
            value.Margin = new Thickness(0, 0, 9, 0); context.Children.Add(value);
            foreach (var button in actions)
            {
                button.MinHeight = 24; button.Padding = new Thickness(7, 1, 7, 1);
                context.Children.Add(button);
            }
        }
        Button BoundaryButton(string label, bool left)
        {
            var b = Theme.Button(label, () =>
            {
                EdgeLeft = left;
                var clip = P.Clips.FirstOrDefault(c => c.Id == Selected);
                var effect = P.Effects.FirstOrDefault(e => e.Id == Selected);
                if (clip != null) SeekFrame(left ? clip.Start : clip.End - 1);
                if (effect != null) SeekFrame(left ? effect.Start : effect.End - 1);
                Refresh();
            });
            SetActive(b, left == EdgeLeft);
            return b;
        }

        void AddRange() { EditRange(null); }
        void EditRange(Clip clip)
        {
            int from = Math.Min(P.Source.Frames - 1, (int)CursorFrame);
            var existing = P.Clips.FirstOrDefault(c => c.Start <= from && from < c.End);
            if (clip == null && existing != null)
            {
                var free = P.Clips.OrderBy(c => c.Start).Aggregate(0, (f, c) => f >= c.Start ? Math.Max(f, c.End) : f);
                if (free >= P.Source.Frames) { Say("元動画の全体を残しています。緑のバーの端を動かすと空きを作れます。"); return; }
                from = free;
            }
            int limit = P.Clips.Where(c => c.Start > from).Select(c => c.Start).DefaultIfEmpty(P.Source.Frames).Min();
            var sheet = new Sheet(this, clip == null ? "残す範囲を追加" : "残す範囲を編集",
                "最初と最後のフレームを選びます。カット段をドラッグしても指定できます。");
            var a = sheet.Field("開始フレーム", clip == null ? from : clip.Start);
            var b = sheet.Field("終了フレーム（含む）", clip == null ?
                Math.Max(from, Math.Min(limit - 1, from + (int)(P.Source.Fps * 3) - 1)) : clip.End - 1);
            sheet.Open("適用", () =>
            {
                int start = Int(a, 0, P.Source.Frames - 1);
                int end = Int(b, start, P.Source.Frames - 1) + 1;
                if (P.Clips.Any(c => c != clip && c.Start < end && c.End > start))
                    throw new ArgumentException("ほかの「残す範囲」と重なっています。");
                Change(() =>
                {
                    if (clip == null) clip = P.AddClip(start, end);
                    else { clip.Start = start; clip.End = end; }
                    Selected = clip.Id;
                    SeekFrame(clip.Start, false);
                });
                Say("残す範囲を設定しました");
            });
        }

        internal void MoveBoundary(bool left, int frame)
        {
            var clip = P.Clips.FirstOrDefault(c => c.Id == Selected);
            if (clip != null)
            {
                int low = P.Clips.Where(c => c.Id != clip.Id && c.End <= clip.Start).Select(c => c.End).DefaultIfEmpty(0).Max();
                int high = P.Clips.Where(c => c.Id != clip.Id && c.Start >= clip.End).Select(c => c.Start)
                    .DefaultIfEmpty(P.Source.Frames).Min();
                if (left) clip.Start = Math.Max(low, Math.Min(clip.End - 1, frame));
                else clip.End = Math.Max(clip.Start + 1, Math.Min(high, frame));
                Map = new TimeMap(P); SeekFrame(left ? clip.Start : clip.End - 1, false); return;
            }
            var e = P.Effects.FirstOrDefault(x => x.Id == Selected && x.Kind == "speed");
            if (e != null)
            {
                var owner = P.Clips.FirstOrDefault(c => c.Id == e.ClipId);
                if (owner == null) return;
                var others = P.Effects.Where(x => x != e && x.Kind == "speed" && x.ClipId == owner.Id).ToArray();
                int low = Math.Max(owner.Start, others.Where(x => x.End <= e.Start).Select(x => x.End).DefaultIfEmpty(owner.Start).Max());
                int high = Math.Min(owner.End, others.Where(x => x.Start >= e.End).Select(x => x.Start).DefaultIfEmpty(owner.End).Min());
                if (low >= high) return;
                e.Start = Math.Max(low, Math.Min(high - 1, e.Start));
                e.End = Math.Max(e.Start + 1, Math.Min(high, e.End));
                if (left) e.Start = Math.Max(low, Math.Min(e.End - 1, frame));
                else e.End = Math.Max(e.Start + 1, Math.Min(high, frame));
                Map = new TimeMap(P); SeekFrame(left ? e.Start : e.End - 1, false); return;
            }
            var cap = P.Captions.FirstOrDefault(c => c.Id == Selected);
            if (cap == null) return;
            var target = P.Clips.FirstOrDefault(c => c.Id == cap.Target);
            if (target == null) return;
            int a = target.Start - target.Origin, b = target.End - target.Origin;
            cap.Start = Math.Max(a, Math.Min(b - 1, cap.Start));
            cap.End = Math.Max(cap.Start + 1, Math.Min(b, cap.End));
            if (left) cap.Start = Math.Max(a, Math.Min(cap.End - 1, frame - target.Origin));
            else cap.End = Math.Max(cap.Start + 1, Math.Min(b, frame - target.Origin));
            SeekOutput(Map.SeekTarget(cap.Target, left ? cap.Start : cap.End - 1));
        }

        void DeleteSelected()
        {
            string id = Selected;
            Change(() =>
            {
                P.Clips.RemoveAll(x => x.Id == id);
                P.Effects.RemoveAll(x => x.Id == id);
                P.Captions.RemoveAll(x => x.Id == id);
                Selected = null;
            });
            int warnings = P.Captions.Count(c => Map.Warning(c) != null);
            Say(warnings == 0 ? "選んだ項目を削除しました" : "削除しました。貼り付け先を確認するテロップが " + warnings + " 件あります。");
        }

        void AddBlank()
        {
            int frame = Math.Min(P.Source.Frames - 1, (int)CursorFrame);
            if (P.Clips.Any(c => c.Start <= frame && frame < c.End))
            {
                Say("カット段の薄い色の区間をクリックして、ブランクを入れる場所を選んでください。"); return;
            }
            int left = P.Clips.Where(c => c.End <= frame).Select(c => c.End).DefaultIfEmpty(0).Max();
            int right = P.Clips.Where(c => c.Start > frame).Select(c => c.Start).DefaultIfEmpty(P.Source.Frames).Min();
            var existing = P.Effects.FirstOrDefault(e => e.Kind == "blank" && e.Start >= left && e.Start < right);
            EditInsert(existing, "blank", frame, null);
        }
        void AddFreeze()
        {
            int frame = Math.Min(P.Source.Frames - 1, (int)CursorFrame);
            var clip = P.Clips.FirstOrDefault(c => c.Start <= frame && frame < c.End);
            if (clip == null) { Say("緑の「残す」区間で、止めたい位置をクリックしてください。"); return; }
            var existing = P.Effects.FirstOrDefault(e => e.Kind == "freeze" && e.ClipId == clip.Id && e.Start == frame);
            EditInsert(existing, "freeze", frame, clip.Id);
        }
        void EditInsert(Effect existing, string kind, int frame, string clipId)
        {
            bool blank = kind == "blank";
            var sheet = new Sheet(this, blank ? "ブランクの長さ" : "停止の長さ",
                blank ? "選んだ削除区間に、説明を貼れる単色の画面を入れます。" : "選んだフレームを指定の長さだけ表示します。");
            var frames = existing == null ? (int)Math.Round((blank ? 5 : 3) * P.Source.Fps) : existing.Frames;
            var length = sheet.Field("長さ", (frames / P.Source.Fps).ToString("0.######"));
            var unit = sheet.Choice("単位", new[] { "秒", "フレーム" }, "秒");
            var conversion = Theme.Wrap("", 12, Theme.Muted); sheet.Add("", conversion);
            bool converting = false;
            Action update = () =>
            {
                double n;
                if (!double.TryParse(length.Text, out n) || double.IsInfinity(n) || double.IsNaN(n)) { conversion.Text = "数値を入力してください"; return; }
                conversion.Text = (Equals(unit.SelectedItem, "秒") ? Math.Round(n * P.Source.Fps).ToString("0") + " フレーム" :
                    (n / P.Source.Fps).ToString("0.######") + " 秒") + "  （試作の出力 " + P.Source.Fps.ToString("0.##") + " fps）";
            };
            unit.SelectionChanged += delegate
            {
                if (converting) return;
                double n;
                if (double.TryParse(length.Text, out n) && !double.IsInfinity(n))
                {
                    converting = true;
                    length.Text = Equals(unit.SelectedItem, "秒") ? (n / P.Source.Fps).ToString("0.######") :
                        Math.Round(n * P.Source.Fps).ToString("0");
                    converting = false;
                }
                update();
            };
            length.TextChanged += delegate { update(); }; update();
            var background = blank ? sheet.Color("背景色", existing == null ? "#000000" : existing.Color, false) : null;
            sheet.Open("適用", () =>
            {
                int count = Equals(unit.SelectedItem, "秒") ?
                    Math.Max(1, (int)Math.Round(Number(length, .001, 1000000 / P.Source.Fps) * P.Source.Fps)) :
                    Int(length, 1, 1000000);
                Change(() =>
                {
                    var e = existing ?? new Effect { Id = Project.Id(), Kind = kind, ClipId = clipId, Start = frame };
                    e.Frames = count; if (blank) e.Color = background.Value;
                    if (existing == null) P.Effects.Add(e);
                    CutMode = true; Selected = e.Id;
                });
                Say(blank ? "ブランクを設定しました" : "停止を設定しました");
            });
        }

        void Speed()
        {
            var effect = P.Effects.FirstOrDefault(e => e.Id == Selected && e.Kind == "speed");
            var clip = P.Clips.FirstOrDefault(c => c.Id == Selected) ?? P.Clips.FirstOrDefault(c => c.Start <= CursorFrame && CursorFrame < c.End);
            if (effect == null && clip == null) { Say("緑の「残す」区間を選んでから再生速度を指定してください。"); return; }
            EditSpeed(effect, clip);
        }
        void EditSpeed(Effect existing, Clip clip)
        {
            if (existing != null) clip = P.Clips.FirstOrDefault(c => c.Id == existing.ClipId);
            if (clip == null) { Say("元の採用区間が削除されています。速度マーカーを削除できます。"); return; }
            var sheet = new Sheet(this, "再生速度を変える", "完成動画の再生時間に反映します。カット段の幅は元動画の位置を表します。");
            var a = sheet.Field("開始フレーム", existing == null ? clip.Start : existing.Start);
            var b = sheet.Field("終了フレーム（含む）", (existing == null ? clip.End : existing.End) - 1);
            var rate = sheet.Choice("速度", new[] { "0.5倍", "1倍（通常）", "2倍" },
                existing != null && existing.Rate == .5 ? "0.5倍" : "2倍");
            sheet.Open("適用", () =>
            {
                int start = Int(a, clip.Start, clip.End - 1), end = Int(b, start, clip.End - 1) + 1;
                double ratio = rate.SelectedIndex == 0 ? .5 : rate.SelectedIndex == 1 ? 1 : 2;
                if (P.Effects.Any(e => e != existing && e.Kind == "speed" && e.ClipId == clip.Id && e.Start < end && e.End > start))
                    throw new ArgumentException("ほかの速度変更区間と重なっています。");
                Change(() =>
                {
                    if (existing != null) P.Effects.Remove(existing);
                    if (ratio != 1)
                    {
                        var e = existing ?? new Effect { Id = Project.Id(), ClipId = clip.Id, Kind = "speed" };
                        e.Start = start; e.End = end; e.Rate = ratio; P.Effects.Add(e); Selected = e.Id;
                    }
                    else Selected = clip.Id;
                    SeekFrame(start, false);
                });
                Say("再生速度を設定しました");
            });
        }

        internal static string CaptionLabel(string kind)
        {
            switch (kind)
            {
                case "text": return "文字";
                case "arrow": return "矢印";
                case "line": return "直線";
                case "rect": return "四角形";
                case "ellipse": return "円";
                default: return "画像";
            }
        }
        void AddTrack()
        {
            if (P.Tracks >= 3) return;
            HideEmpty = false; Change(() => P.Tracks++); Say("テロップ段を追加しました");
        }
        void CaptionMenu(Button button)
        {
            Menu(button, new[] { "text", "arrow", "line", "rect", "ellipse", "image" }
                .Select(k => Item(CaptionLabel(k) + "を貼る", () => AddCaption(k))).ToArray());
        }
        void AddCaption(string kind, string chosenImage = null)
        {
            var part = CurrentPart;
            var selectedClip = P.Clips.FirstOrDefault(c => c.Id == Selected);
            var clip = part != null ? P.Clips.FirstOrDefault(c => c.Id == part.Target) :
                selectedClip ?? P.Clips.FirstOrDefault(c => c.Start <= CursorFrame && CursorFrame < c.End);
            if (part == null && clip == null)
            {
                Say("緑の区間、ブランク、または停止のマーカーを選んでから貼り付けてください。"); return;
            }
            string image = null;
            if (kind == "image")
            {
                if (chosenImage == null)
                {
                    var dialog = new OpenFileDialog { Title = "貼り付ける画像を選ぶ", Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp" };
                    if (dialog.ShowDialog(this) != true) return;
                    chosenImage = dialog.FileName;
                }
                try { image = Files.Local(chosenImage, AppDomain.CurrentDomain.BaseDirectory); }
                catch (Exception e) { if (!UserError(e)) throw; Say(e.Message); return; }
            }
            int start = clip == null ? (int)Math.Floor((OutputTime - part.From) * P.Source.Fps + .00001) :
                Math.Max(clip.Start, Math.Min(clip.End - 1, (int)CursorFrame)) - clip.Origin;
            int max = clip == null ? P.Effects.First(e => e.Id == part.Target).Frames : clip.End - clip.Origin;
            start = Math.Min(max - 1, start);
            var cap = new Caption { Id = Project.Id(), Target = clip == null ? part.Target : clip.Id,
                Kind = kind, Track = P.Tracks, Start = start, End = Math.Min(max, start + (int)Math.Round(3 * P.Source.Fps)),
                X = kind == "text" ? .10 : .43, Y = kind == "text" ? .73 : .30,
                Width = kind == "text" ? .61 : .26, Height = kind == "text" ? .16 : .25, Opacity = 1,
                FontSize = 35, Text = "ここに説明を入力", Color = kind == "text" ? "#FFFFFF" : "#FFE48D",
                Background = kind == "text" ? "#C8112423" : "#00000000", Image = image };
            EditCaption(cap, true);
        }
        void EditCaption(Caption caption, bool add = false)
        {
            var target = P.Clips.FirstOrDefault(c => c.Id == caption.Target);
            var insert = P.Effects.FirstOrDefault(e => e.Id == caption.Target);
            int origin = target == null ? 0 : target.Start - target.Origin;
            int length = target == null ? (insert == null ? 0 : insert.Frames) : target.End - target.Start;
            var sheet = new Sheet(this, (add ? "貼る · " : "テロップ編集 · ") + CaptionLabel(caption.Kind),
                target != null ? "選んだ「残す範囲」の先頭を 0 とした位置で指定します。" :
                    insert != null ? (insert.Kind == "freeze" ? "停止" : "ブランク") + "の先頭を 0 とした表示時間です。" :
                    "貼り付け先が見つかりません。元に戻すか、このテロップを削除できます。");
            var track = sheet.Choice("表示する段", Enumerable.Range(1, P.Tracks).Select(i => "テロップ " + i), "テロップ " + caption.Track);
            var text = caption.Kind == "text" ? sheet.Field("表示する文字", caption.Text, 248) : null;
            if (text != null) { text.MaxLength = 500; text.TextWrapping = TextWrapping.Wrap; text.AcceptsReturn = true; text.MinHeight = 44; }
            bool seconds = target == null;
            var timing = Theme.Row();
            var start = Theme.Input(seconds ? (object)(caption.Start / P.Source.Fps).ToString("0.######") : caption.Start - origin, 72);
            var end = Theme.Input(seconds ? (object)(caption.End / P.Source.Fps).ToString("0.######") : caption.End - origin, 72);
            timing.Children.Add(start); timing.Children.Add(Theme.Text(" ～ ")); timing.Children.Add(end);
            timing.Children.Add(Theme.Text(seconds ? "秒" : "F（終了は含めない）", 11, Theme.Muted));
            sheet.Add("表示期間", timing);
            var windows = Map.CaptionWindows(caption).ToArray();
            if (windows.Length > 0) sheet.Add("完成時刻", Theme.Wrap(Theme.Clock(windows[0].Item1, true) + " ～ " +
                Theme.Clock(windows[windows.Length - 1].Item2, true) + (windows.Length > 1 ? "（効果で分かれる場合があります）" : ""), 11, Theme.Muted));
            var warning = Map.Warning(caption);
            if (warning != null) sheet.Add("確認", Theme.Wrap(warning, 12, Theme.Coral));
            var opacity = sheet.Field("透明度（0～100%）", Math.Round((1 - caption.Opacity) * 100));
            var angle = sheet.Field("回転（度）", Math.Round(caption.Angle, 1));
            var color = caption.Kind != "image" ? sheet.Color("文字・線の色", caption.Color, false) : null;
            var background = caption.Kind == "text" || caption.Kind == "rect" || caption.Kind == "ellipse" ?
                sheet.Color("背景色", caption.Background, true) : null;
            var size = caption.Kind == "text" ? sheet.Field("文字サイズ", caption.FontSize) : null;
            var fit = sheet.Field("大きさ（%）", 100);
            sheet.Open(add ? "貼る" : "適用", () =>
            {
                if (length == 0) throw new ArgumentException("貼り付け先を確認してください。");
                int a = seconds ? (int)Math.Round(Number(start, 0, length / P.Source.Fps + .000001) * P.Source.Fps) :
                    Int(start, 0, length - 1);
                int b = seconds ? (int)Math.Round(Number(end, 0, length / P.Source.Fps + .000001) * P.Source.Fps) :
                    Int(end, a + 1, length);
                if (a < 0 || a >= b || b > length) throw new ArgumentException("表示期間を選んだ区間の長さに収めてください。");
                double alpha = 1 - Number(opacity, 0, 100) / 100, rotation = Number(angle, -360, 360);
                double multiplier = Number(fit, 5, 300) / 100;
                double font = size == null ? caption.FontSize : Number(size, 10, 180);
                Change(() =>
                {
                    caption.Track = track.SelectedIndex + 1;
                    caption.Start = a + origin; caption.End = b + origin;
                    caption.Opacity = alpha; caption.Angle = rotation; caption.FontSize = font;
                    caption.Width = Math.Max(.035, Math.Min(1 - caption.X, caption.Width * multiplier));
                    caption.Height = Math.Max(.035, Math.Min(1 - caption.Y, caption.Height * multiplier));
                    if (text != null) caption.Text = text.Text;
                    if (color != null) caption.Color = color.Value;
                    if (background != null) caption.Background = background.Value;
                    if (add) { caption.Layer = P.Captions.Count; P.Captions.Add(caption); }
                    Selected = caption.Id; CutMode = true;
                });
                Say(add ? "テロップを貼りました。プレビューで位置を調整できます。" : "テロップを更新しました");
            });
        }
        void Layer(Caption caption, int direction)
        {
            Change(() =>
            {
                var inTrack = P.Captions.Where(c => c.Track == caption.Track && c != caption).Select(c => c.Layer).ToArray();
                if (inTrack.Length > 0) caption.Layer = direction > 0 ? inTrack.Max() + 1 : inTrack.Min() - 1;
            });
            Say("同じ段の重なり順を更新しました");
        }
        static int Int(TextBox field, int min, int max)
        {
            int value;
            if (!int.TryParse(field.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) || value < min || value > max)
                throw new ArgumentException(min + " ～ " + max + " の整数を入力してください。");
            return value;
        }
        static double Number(TextBox field, double min, double max)
        {
            double value;
            if (!double.TryParse(field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) || !(value >= min && value <= max))
                throw new ArgumentException(min.ToString("0.######") + " ～ " + max.ToString("0.######") + " の数を入力してください。");
            return value;
        }
    }
}

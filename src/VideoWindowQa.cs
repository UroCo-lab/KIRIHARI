using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Kirihari
{
    internal sealed partial class VideoWindow
    {
        bool windowQaStarted;
        int qaSaveConflicts;
        string qaFolderToPick;
        uint qaTitleBarBackground, qaTitleBarForeground;

        async Task VerifyTitleBarColors(uint background, uint foreground)
        {
            if (Environment.OSVersion.Version.Build < 22000) return;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (qaTitleBarBackground == background && qaTitleBarForeground == foreground) return;
                await Task.Delay(20);
            }
            throw new IOException(string.Format("The native title bar received {0:x6}/{1:x6}, expected {2:x6}/{3:x6}.",
                qaTitleBarBackground, qaTitleBarForeground, background, foreground));
        }

        const string OpenStartupTheme = "document.querySelector('[data-project-menu]').open = true; document.querySelector('[data-project-menu] [data-theme-open]').click()";

        async Task VerifyTitleBarRestart(string phase)
        {
            if (phase == "save")
            {
                await Script(OpenStartupTheme);
                await Script(@"const select = document.querySelector('[data-theme-select]');
                    select.value = 'light'; select.dispatchEvent(new Event('change'));
                    document.querySelector('[data-theme-save]').click();");
            }
            await WaitFor("document.querySelector('.kh-app').dataset.theme === 'light'");
            await VerifyTitleBarColors(0xf0eeec, 0x302217);
            File.WriteAllText(Path.Combine(qaFolder, "title-bar-" + phase + ".txt"),
                "PASS: the saved light theme was applied to the native title bar.");
        }

        async Task VerifyThemes()
        {
            await VerifyTitleBarColors(0x261d19, 0xf6f2f0);
            await Script(OpenStartupTheme);
            await Script(@"window.qaTheme = value => {
                const select = document.querySelector('[data-theme-select]');
                select.value = value; select.dispatchEvent(new Event('change'));
            };");
            string[] themes = { "standard", "dark", "light", "superlight" };
            string[] backgrounds = { "rgb(16, 18, 23)", "rgb(8, 8, 8)", "rgb(218, 221, 223)", "rgb(255, 255, 255)" };
            uint[] titleBackgrounds = { 0x261d19, 0x111111, 0xf0eeec, 0xffffff };
            uint[] titleForegrounds = { 0xf6f2f0, 0xf2f2f2, 0x302217, 0x332015 };
            for (int i = 0; i < themes.Length; i++)
            {
                await Script("window.qaTheme('" + themes[i] + "')");
                Qa.Check(await Script("getComputedStyle(document.querySelector('.kh-app')).backgroundColor === '" + backgrounds[i] + "'") == "true", "Theme background " + themes[i]);
                await VerifyTitleBarColors(titleBackgrounds[i], titleForegrounds[i]);
            }
            await Script(@"window.qaTheme('manual');
                window.qaThemeColor = (name, value) => {
                    const color = document.querySelector('[data-theme-color=' + name + ']');
                    color.value = value; color.dispatchEvent(new Event('input'));
                };
                window.qaThemeColor('panel', '#17415e');
                window.qaThemeColor('text', '#17415e');");
            await VerifyTitleBarColors(0x5e4117, 0xffffff);
            await Script(@"window.qaThemeColor('text', '#ffebc2');
                const accent = document.querySelector('[data-theme-color=accent]');
                accent.value = '#008088'; accent.dispatchEvent(new Event('input'));
                document.querySelector('[data-theme-save]').click()");
            await VerifyTitleBarColors(0x5e4117, 0xc2ebff);
            SetTitleBarTheme("#191d26", "#f0f2f6");
            browser.CoreWebView2.Reload();
            await WaitFor("document.querySelector('[data-project-dialog]')?.open && document.querySelector('.kh-app').dataset.theme === 'manual'");
            await VerifyTitleBarColors(0x5e4117, 0xc2ebff);
            Qa.Check(await Script(@"document.querySelector('[data-startup-practice]').hidden &&
                !document.querySelector('[data-project-menu]').open &&
                !!document.querySelector('[data-project-menu] [data-open-practice]')") == "true",
                "After reload the startup practice shortcut is hidden and practice is still available from the chooser menu");
            Qa.Check(await Script("document.querySelector('.kh-app').style.getPropertyValue('--accent') === '#008088'") == "true",
                "Manual theme colors persist through a fresh WebView load");
            await Script(OpenStartupTheme);
            await Script("document.querySelector('[data-theme-reset]').click()");
            await VerifyTitleBarColors(0x261d19, 0xf6f2f0);
            await Script("document.querySelector('[data-theme-dialog] [data-close]').click()");
            await WaitFor("document.querySelector('.kh-app').style.getPropertyValue('--accent') === '#008088'");
            await VerifyTitleBarColors(0x5e4117, 0xc2ebff);
            await Script(OpenStartupTheme);
            await Script(@"const select = document.querySelector('[data-theme-select]');
                select.value = 'standard'; select.dispatchEvent(new Event('change'));
                document.querySelector('[data-theme-save]').click()");
            await WaitFor("document.querySelector('.kh-app').dataset.theme === 'standard'");
            await VerifyTitleBarColors(0x261d19, 0xf6f2f0);
        }

        async Task VerifyInitialFolders()
        {
            SetPickerFolder(new Microsoft.Win32.OpenFileDialog(), "media", null);
            Qa.Check(qaPickerDirectory == SampleFolder, "First media picker starts in bundled lightweight samples");
            var samples = Directory.GetFiles(SampleFolder, "*.mp4");
            Qa.Check(samples.Length == 3 && samples.Sum(path => new FileInfo(path).Length) < 300000, "Three lightweight sample videos are bundled");
            foreach (string path in samples)
                Qa.Check((await Task.Run(() => tool.Read(path, lifetime.Token))).duration > 0, "Bundled sample probes successfully");
            qaProjectToOpen = Path.Combine(SampleFolder, "サンプル編集.kirihari");
            await Script("document.querySelector('[data-project-dialog] [data-open-project]').click()");
            await WaitFor("document.querySelector('.kh-picture').hasAttribute('data-media-ready') && document.querySelectorAll('[data-range]').length === 1");
            Qa.Check(qaPickerDirectory == SampleFolder && projectFile == null && ProjectDirectory == SampleFolder,
                "First project picker starts at the usable sample and protects ordinary save from overwriting it");
            await Script("document.querySelector('[data-export]').click()");
            await WaitFor("document.querySelector('[data-export-dialog]').open && !document.querySelector('[data-export-step]').disabled");
            Qa.Check(await Script("!document.querySelector('[data-export-audio]').checked && document.querySelector('[data-export-audio-note]').hasAttribute('data-warning')") == "true",
                "A legacy muted project keeps silent export until the user changes it");
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
            qaProjectToOpen = null;
            await Script("document.querySelector('[data-projects]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open");
        }

        readonly Dictionary<string, string> qaRelinkPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        async Task ConfirmZipReview()
        {
            await WaitFor("document.querySelector('[data-archive-dialog]').open && !document.querySelector('[data-archive-review]').hidden");
            await Script("document.querySelector('[data-archive-create]').click()");
        }

        async Task VerifySaveAndRelink()
        {
            const string practice = "document.querySelector('.kh-settings [data-open-practice]').click()";
            const string prompt = "document.querySelector('[data-close-edit-dialog][data-purpose=switch]')";
            await Script(practice);
            await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
            await DragCut(24, 72, 288);
            await Script("window.qaManualSwitch = true; " + practice);
            await WaitFor(prompt + "?.open");
            Qa.Check(await Script(@"document.querySelector('[data-close-edit-save]').textContent === '保存して切り替える' &&
                document.querySelector('[data-close-edit-discard]').textContent === '保存せず切り替える'") == "true",
                "Switching a dirty project shows both save and discard");
            using (var shot = File.Create(Path.Combine(qaFolder, "切り替え前の保存確認.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('[data-close-edit-cancel]').click()");
            await WaitFor("!window.kirihariQa.debug().busy && window.kirihariQa.snapshot().ranges.length === 1");
            await Script(practice);
            await WaitFor(prompt + "?.open");
            using (File.Open(Path.Combine(qaFolder, "確認.kirihari"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                await Script("document.querySelector('[data-close-edit-save]').click()");
                await WaitFor(prompt + "?.open && document.querySelector('[data-close-edit-error]').textContent.includes('使用中')");
                Qa.Check(await Script("window.kirihariQa.snapshot().ranges.length === 1") == "true", "A failed save keeps the current edit and the switch prompt");
            }
            await Script("document.querySelector('[data-close-edit-save]').click()");
            await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('.kh-picture').hasAttribute('data-media-ready') && window.kirihariQa.snapshot().ranges.length === 0");
            var json = MediaTool.Json();
            var project = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(qaFolder, "確認.kirihari")));
            string id = Text(project, "baseMediaId");
            string missing = Path.Combine(qaFolder, "旧フォルダ", "練習素材.mp4");
            string relocated = Path.Combine(qaFolder, "移動した練習素材.mp4");
            File.Copy(source.path, relocated);
            project["sourcePath"] = missing;
            ((Dictionary<string, object>)project["assets"])[id] = missing;
            qaProjectToOpen = Path.Combine(qaFolder, "移動した編集.kirihari");
            File.WriteAllText(qaProjectToOpen, json.Serialize(project));
            string practicePath = source.path;
            const string open = "document.querySelector('.kh-settings [data-open-project]').click()";
            await Script(open);
            await WaitFor(prompt + "?.open");
            await Script("document.querySelector('[data-close-edit-discard]').click()");
            await WaitFor("!window.kirihariQa.debug().busy");
            Qa.Check(source.path == practicePath && projectFile == null, "Canceling the missing-source picker preserves the current project");
            await Script("window.qaManualSwitch = false");
            qaRelinkPaths[missing] = Path.Combine(SampleFolder, "optical-motion.mp4");
            await Script(open);
            await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('[data-app-status]').textContent.includes('保存時と異なります')");
            Qa.Check(source.path == practicePath && projectFile == null, "An incompatible replacement preserves the current project");
            string different = Path.Combine(qaFolder, "同じ容量の別動画.mp4");
            File.Copy(Path.Combine(SampleFolder, "optical-motion.mp4"), different);
            using (var file = File.OpenWrite(different)) file.SetLength(new FileInfo(relocated).Length);
            qaRelinkPaths[missing] = different;
            await Script(open);
            await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('[data-app-status]').textContent.includes('動画の内容が保存時と異なります')");
            Qa.Check(source.path == practicePath && projectFile == null &&
                new FileInfo(different).Length == new FileInfo(relocated).Length,
                "Relinking rejects a playable different video of the same byte length");
            var legacy = json.Deserialize<Dictionary<string, object>>(json.Serialize(project));
            legacy.Remove("assetFingerprints");
            string modernPath = qaProjectToOpen;
            qaProjectToOpen = Path.Combine(qaFolder, "旧版の編集.kirihari");
            File.WriteAllText(qaProjectToOpen, json.Serialize(legacy));
            qaRelinkPaths[missing] = relocated;
            await Script(open);
            await WaitFor("document.querySelector('[data-relink-dialog]').open && document.querySelector('[data-relink-video]').readyState >= 2");
            Qa.Check(await Script("document.querySelector('[data-relink-info]').textContent.includes('移動した練習素材.mp4') && document.querySelector('[data-relink-info]').textContent.includes('×')") == "true",
                "Legacy relinking displays a decoded preview, duration and resolution");
            await Script("document.querySelector('[data-relink-choice=retry]').click()");
            await WaitFor("document.querySelector('[data-relink-dialog]').open && document.querySelector('[data-relink-video]').readyState >= 2");
            await Script("document.querySelector('[data-relink-choice=cancel]').click()");
            await WaitFor("!window.kirihariQa.debug().busy");
            Qa.Check(source.path == practicePath && projectFile == null, "Retrying and canceling legacy confirmation keeps the current project");
            await Script(open);
            await WaitFor("document.querySelector('[data-relink-dialog]').open");
            await Script("document.querySelector('[data-relink-choice=use]').click()");
            await WaitFor("!window.kirihariQa.debug().busy && window.kirihariQa.snapshot().ranges.length === 1");
            Qa.Check(source.path == relocated && projectFile == qaProjectToOpen, "Confirming the preview opens the legacy edit with the selected video");
            qaProjectToOpen = modernPath;
            qaRelinkPaths[missing] = relocated;
            await Script(open);
            await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('.kh-picture').hasAttribute('data-media-ready') && document.querySelector('[data-save-status]').textContent === '未保存'");
            Qa.Check(source.path == relocated && projectFile == qaProjectToOpen &&
                await Script("window.kirihariQa.snapshot().ranges.map(cut => [cut.start, cut.end]).join()") == "\"24,72\"",
                "Relinking opens the original edits and marks the new source path unsaved");
            await SaveFromToolbar();
            qaRelinkPaths.Clear();
            var saved = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(qaProjectToOpen));
            Qa.Check(Text(saved, "sourcePath") == relocated, "The next save persists the new source path");
            await Script(open);
            await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
            string ordinary = File.ReadAllText(qaProjectToOpen);
            await Script(@"document.querySelector('[data-mute]').click();
                document.querySelector('[data-save-portable]').click();
                window.qaZipProgress = new Set();
                new MutationObserver(() => qaZipProgress.add(document.querySelector('[data-archive-progress]').value))
                    .observe(document.querySelector('[data-archive-progress]'), {attributes:true});");
            await WaitFor("document.querySelector('[data-archive-dialog]').open");
            Qa.Check(await Script(@"document.querySelector('[data-archive-items]').textContent.includes('移動した練習素材.mp4') &&
                document.querySelector('[data-archive-total]').textContent.includes('1本 · 合計')") == "true",
                "ZIP review names the retained source and displays the total size");
            using (var shot = File.Create(Path.Combine(qaFolder, "素材ZIPの保存前確認.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await ConfirmZipReview();
            await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('[data-app-status]').textContent.includes('素材を含むZIPを保存しました')");
            Qa.Check(await Script("document.querySelector('[data-save-status]').textContent === '未保存' && qaZipProgress.has(0) && qaZipProgress.has(100)") == "true" &&
                File.ReadAllText(qaProjectToOpen) == ordinary, "ZIP progress completes without changing the ordinary file or clearing its unsaved state");
            string portable = Path.Combine(qaFolder, "確認.zip");
            using (var archive = ZipFile.OpenRead(portable))
                Qa.Check(archive.GetEntry("source/" + Path.GetFileName(relocated)) != null &&
                    archive.GetEntry("project.json") != null, "The save menu makes a portable ZIP before MP4 export");
            await Script("window.qaManualSwitch = true; " + open);
            await WaitFor(prompt + "?.open");
            await Script("document.querySelector('[data-close-edit-cancel]').click()");
            await WaitFor("!window.kirihariQa.debug().busy");
            await Script("window.qaManualSwitch = false");
            await SaveFromToolbar();
            await VerifyArchiveFailures(portable, relocated, qaProjectToOpen);
            qaProjectToOpen = modernPath;
            await VerifyCrossBandEdit();
            qaProjectToOpen = null;
        }

        async Task VerifyArchiveFailures(string portable, string activeSource, string activeProject)
        {
            const string open = "document.querySelector('.kh-settings [data-open-project]').click()";
            string imports = Path.Combine(qaFolder, "imports");
            int prior = Directory.Exists(imports) ? Directory.GetDirectories(imports).Length : 0;
            string manifest;
            string video;
            long length;
            using (var original = ZipFile.OpenRead(portable))
            {
                using (var reader = new StreamReader(original.GetEntry("project.json").Open())) manifest = reader.ReadToEnd();
                var media = original.Entries.Single(entry => entry.FullName.StartsWith("source/"));
                video = media.FullName;
                length = media.Length;
            }
            foreach (bool brokenVideo in new[] { false, true })
            {
                qaProjectToOpen = Path.Combine(qaFolder, brokenVideo ? "壊れた素材.zip" : "素材なし.zip");
                using (var archive = ZipFile.Open(qaProjectToOpen, ZipArchiveMode.Create))
                {
                    using (var writer = new StreamWriter(archive.CreateEntry("project.json").Open())) writer.Write(manifest);
                    if (brokenVideo)
                        using (var stream = archive.CreateEntry(video).Open()) stream.Write(new byte[length], 0, (int)length);
                }
                await Script(open);
                string error = brokenVideo ? "動画処理に失敗" : "ZIPに必要な動画素材";
                await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('[data-app-status]').textContent.includes('" + error + "')");
                Qa.Check(source.path == activeSource && projectFile == activeProject &&
                    (Directory.Exists(imports) ? Directory.GetDirectories(imports).Length : 0) == prior,
                    "An incomplete or undecodable ZIP leaves no extracted media and preserves the current edit");
            }

            var json = MediaTool.Json();
            var oversized = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(activeProject));
            var cut = json.ConvertToType<object[]>(oversized["ranges"])[0];
            oversized["ranges"] = Enumerable.Repeat(cut, MediaTool.CutLimit + 1).ToArray();
            qaProjectToOpen = Path.Combine(qaFolder, "上限超過.kirihari");
            File.WriteAllText(qaProjectToOpen, json.Serialize(oversized));
            await Script(open);
            await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('[data-app-status]').textContent.includes('2000件まで')");
            Qa.Check(projectFile == activeProject, "An oversized edit is rejected before any cuts are loaded or truncated");

            oversized["ranges"] = Enumerable.Range(0, MediaTool.CutLimit).Select(index => new {
                id = Project.Id(), start = index == 0 ? 24 : 288 + index, end = index == 0 ? 72 : 289 + index,
                mediaId = Text(oversized, "baseMediaId"), mediaOrigin = index == 0 ? 0 : 288 + index, inserted = index != 0
            }).ToArray();
            qaProjectToOpen = Path.Combine(qaFolder, "上限ちょうど.kirihari");
            File.WriteAllText(qaProjectToOpen, json.Serialize(oversized));
            await Script(open);
            await WaitFor("!window.kirihariQa.debug().busy && window.kirihariQa.snapshot().ranges.length === 2000");
            await Script(@"(() => {
                const axis = document.querySelector('.kh-axis'), r = axis.getBoundingClientRect();
                axis.dispatchEvent(new MouseEvent('click', {bubbles:true, clientX:r.left + r.width * 48 / 2288}));
                document.querySelector('[data-split]').click();
            })()");
            await WaitFor("document.querySelector('[data-app-status]').textContent.includes('カットの上限（2000件）')");
            Qa.Check(await Script("window.kirihariQa.snapshot().ranges.length === 2000") == "true",
                "The supported 2,000 cuts are restored, and splitting the 2,001st is blocked");
            qaProjectToOpen = activeProject;
            await Script(open);
            await WaitFor("!window.kirihariQa.debug().busy && window.kirihariQa.snapshot().ranges.length === 1");

            var cancelManifest = json.Deserialize<Dictionary<string, object>>(manifest);
            cancelManifest.Remove("assetBytes");
            cancelManifest.Remove("assetFingerprints");
            qaProjectToOpen = Path.Combine(qaFolder, "展開中止の検証.zip");
            using (var archive = ZipFile.Open(qaProjectToOpen, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("project.json").Open()))
                    writer.Write(json.Serialize(cancelManifest));
                using (var output = archive.CreateEntry(video).Open())
                using (var input = File.OpenRead(activeSource))
                {
                    input.CopyTo(output);
                    var buffer = new byte[1024 * 1024];
                    for (int index = 0; index < 128; index++) output.Write(buffer, 0, buffer.Length);
                }
            }
            await Script(@"window.qaCancelZipObserver = new MutationObserver(() => {
                const dialog = document.querySelector('[data-archive-dialog][open]');
                if (dialog && !dialog.querySelector('[data-archive-running]').hidden) {
                    qaCancelZipObserver.disconnect();
                    dialog.querySelector('[data-archive-cancel]').click();
                }
            });
            qaCancelZipObserver.observe(document.querySelector('[data-archive-dialog]'), {attributes:true, subtree:true});");
            await Script(open);
            await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('[data-app-status]').textContent.includes('中止しました')");
            Qa.Check(projectFile == activeProject && Directory.GetDirectories(imports).Length == prior,
                "The ZIP open progress dialog can cancel extraction without leaving a staged copy");
        }

        async Task VerifyCrossBandEdit()
        {
            await DragCut(42, 120, 288);
            const string summary = "window.kirihariQa.snapshot().ranges.map(cut => [cut.start,cut.end]).join(';')";
            Qa.Check(await Script(summary) == "\"24,72;72,120\"", "Dragging from inside a retained cut extends into the uncovered range");
            await Script("window.qaCaptureSeek(48)");
            await AddPause("0.5");
            await Script(@"document.querySelector('[data-add-text]').click();
                const acrossEditor = document.activeElement;
                acrossEditor.value = '確認する説明';
                acrossEditor.dispatchEvent(new Event('input', {bubbles:true}));
                document.querySelector('[data-slide-done]').click();");
            const string snapshot = "JSON.stringify(window.kirihariQa.snapshot())";
            string before = await Script(snapshot);
            await Script("document.querySelector('[data-exclude-mode]').click()");
            await DragCut(96, 48, 288);
            await WaitFor("document.querySelector('[data-exclude-dialog]').open");
            Qa.Check(await Script(@"document.querySelector('[data-exclude-summary]').textContent.includes('2本') &&
                document.querySelector('[data-exclude-length]').textContent === '完成尺 4.5秒 → 2秒' &&
                document.querySelector('[data-exclude-impact]').textContent.includes('テロップ 1件・静止 1件') &&
                document.querySelector('[data-app-status]').textContent === ''") == "true" &&
                await Script(snapshot) == before, "Cross-band removal previews two cuts and warns about affected edits before changing anything");
            using (var shot = File.Create(Path.Combine(qaFolder, "複数カットの除外確認.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('[data-exclude-apply]').click()");
            Qa.Check(await Script(summary) == "\"24,48;96,120\"" &&
                await Script("window.kirihariQa.snapshot().pauses.length + window.kirihariQa.snapshot().captions.length") == "0",
                "Cross-band removal keeps only the outer parts");
            await Script("document.querySelector('.kh-editor-heading [data-undo]').click()");
            Qa.Check(await Script(snapshot) == before, "One Undo restores both removed spans and their edits");
            await Script("document.querySelector('.kh-editor-heading [data-redo]').click()");
            await SaveFromToolbar();
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            var result = await Task.Run(() => tool.Read(lastExport, lifetime.Token));
            Qa.Check(result.frames == 48 && result.audio, "The edited project exports the expected two seconds with audio");
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
        }

        async Task VerifyBeginnerFlow()
        {
            string previousMediaFolder = FolderSetting("mediaLast");
            await Script(@"document.querySelector('[data-project-menu] summary').click();
                document.querySelector('[data-project-menu] [data-open-practice]').click();");
            await WaitFor("document.querySelector('.kh-picture').hasAttribute('data-media-ready') && !document.querySelector('[data-cut-hint]').hidden");
            Qa.Check(source.path == Path.Combine(SampleFolder, "cat-motion-short.mp4") && source.audio &&
                projectFile == null && FolderSetting("mediaLast") == previousMediaFolder,
                "Practice opens the bundled cat as a fresh project without replacing the previous media folder");
            Qa.Check(await Script("document.querySelector('[data-save-status]').textContent") == "\"保存前\"",
                "An untouched practice session has no saved project yet");
            Qa.Check(await Script(@"document.querySelector('[data-source-mode=original]').textContent === '元動画' &&
                document.querySelector('[data-source-mode=cut]').textContent === '編修後' &&
                document.querySelector('[data-track=original] strong').textContent === '元動画' &&
                document.querySelector('[data-preview-mode]').textContent === '元動画を表示中' &&
                document.querySelector('[data-add-pause]').textContent.trim() === '静止を追加' &&
                document.querySelector('[data-add-text]').hidden && !document.querySelector('[data-cut-hint]').hidden &&
                document.querySelector('[data-clock-mode=original]').hasAttribute('data-source-active') &&
                !document.querySelector('[data-clock-mode=cut]').hasAttribute('data-source-active')") == "true",
                "The first cut hint appears alone and the original clock follows the selected mode");
            Qa.Check(await Script(@"!document.querySelector('[data-path-details]').open &&
                document.querySelector('[data-edit-summary]').offsetHeight > 0 &&
                document.querySelector('[data-original-path]').offsetHeight === 0 &&
                document.querySelector('[data-total]').textContent === '0秒' &&
                document.querySelector('[data-cut-count]').textContent === '残す範囲 0か所'") == "true",
                "The edit summary is visible while full paths start folded");
            await Script("document.querySelector('[data-path-details] summary').click()");
            Qa.Check(await Script(@"document.querySelector('[data-original-path]').offsetHeight > 0 &&
                document.querySelector('[data-output-path]').offsetHeight > 0") == "true", "File locations are visible when opened");
            await Script("document.querySelector('[data-path-details] summary').click()");
            await DragCut(24, 144, 288);
            await WaitFor(@"document.querySelector('[data-cut-hint]').hidden && !document.querySelector('[data-add-text]').hidden &&
                !document.querySelector('[data-add-text]').disabled &&
                document.querySelector('[data-clock-mode=cut]').hasAttribute('data-source-active') &&
                document.querySelector('[data-total]').textContent === '5秒' &&
                document.querySelector('[data-cut-count]').textContent === '残す範囲 1か所'");
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s);
                q('[data-source-mode=original]').click();
                const length = q('[data-total]');
                const prominent = length.textContent === '5秒' && length.offsetWidth > 0 &&
                    parseFloat(getComputedStyle(length).fontSize) > parseFloat(getComputedStyle(q('[data-cut]')).fontSize);
                q('[data-source-mode=cut]').click();
                return prominent;
            })()") == "true", "Edited length is larger than its current position and visible in original mode");
            await Script(@"(() => {
                const zoom = document.querySelector('[data-zoom]');
                zoom.value = 4; zoom.dispatchEvent(new Event('input'));
            })()");
            await WaitFor(@"(() => {
                const view = document.querySelector('.kh-lane-view');
                const button = document.querySelector('.kh-caption-group [data-add-text]');
                const label = document.querySelector('.kh-caption-group strong');
                const bounds = button.getBoundingClientRect(), title = label.getBoundingClientRect();
                return view.scrollLeft > 0 && !button.disabled && bounds.left >= title.right &&
                    bounds.right <= view.getBoundingClientRect().left && bounds.top < title.bottom && bounds.bottom > title.top;
            })()");
            await Script("document.querySelector('[data-add-text]').click()");
            await WaitFor("document.activeElement?.matches('.kh-text-editor') && document.querySelector('[data-text-outline]').checked");
            Qa.Check(await Script(@"document.querySelector('[data-lane=caption][data-layer=""1""]').offsetHeight >= 56 &&
                document.querySelector('[data-lane=caption][data-layer=""2""]').offsetHeight <= 32") == "true",
                "Only the row that receives a caption expands");
            await Script(@"(() => {
                const editor = document.activeElement;
                editor.value = '練習テロップ'; editor.dispatchEvent(new Event('input', {bubbles:true}));
                document.querySelector('[data-slide-done]').click();
                document.querySelector('[data-add-text]').click();
            })()");
            await WaitFor("document.activeElement?.matches('.kh-text-editor') && document.querySelectorAll('[data-caption-wrap]').length === 2");
            await Script(@"(() => {
                const editor = document.activeElement;
                editor.value = '二つ目'; editor.dispatchEvent(new Event('input', {bubbles:true}));
                document.querySelector('[data-text-outline]').click();
                document.querySelector('[data-slide-done]').click();
                document.querySelector('[data-save-edit]').click();
            })()");
            await WaitFor("document.querySelector('[data-save-status]').textContent === '保存済み' && !window.kirihariQa.debug().busy");
            Qa.Check(await Script(@"document.querySelector('[data-save-edit]').nextElementSibling.matches('[data-save-status]') &&
                !document.querySelector('[data-app-status]').textContent.includes('プロジェクトを保存しました')") == "true",
                "Save confirmation is beside the save button without a path toast");
            var saved = File.ReadAllText(Path.Combine(qaFolder, "確認.kirihari"));
            Qa.Check(saved.Contains("練習テロップ") && saved.Contains("二つ目") && saved.Contains("\"exportAudio\":true") &&
                saved.Contains("\"textOutline\":true") && saved.Contains("\"textOutline\":false"),
                "The toolbar saves both captions, their outline preferences and the independent audio setting");
            Qa.Check(await Script("document.querySelector('[data-media]').muted") == "true", "Practice previews muted by default");
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            var output = await Task.Run(() => tool.Read(lastExport, lifetime.Token));
            Qa.Check(output.audio && output.frames == 120, "A new project exports audio even while preview is muted");
            Qa.Check(await Script("document.querySelector('[data-output-mode]').selectedOptions[0].textContent") == "\"標準（フルHDまで）\"",
                "The default export profile uses the standard full HD label");
            string captionId = MediaTool.Json().Deserialize<string>(await Script("window.kirihariQa.snapshot().captions[0].id"));
            using (var png = new Bitmap(MediaTool.ImageFile(work, captionId)))
            {
                var ink = Enumerable.Range(0, png.Height / 2).SelectMany(y =>
                    Enumerable.Range(0, png.Width).Select(x => png.GetPixel(x, y))).Where(pixel => pixel.A > 100).ToArray();
                Qa.Check(ink.Any(pixel => pixel.R < 60) && ink.Any(pixel => pixel.R > 220),
                    "The export raster contains both the black glyph outline and white letters");
            }
            Qa.Check(await Script("!document.querySelector('[data-export-details]').open && document.activeElement.matches('[data-export-open].kh-primary')") == "true",
                "Completed video is the primary focused action and file maintenance starts collapsed");
            await Script("document.querySelector('[data-export-dialog] [data-close]').click(); document.querySelector('[data-fit]').click()");
            await VerifyExcludeRange();
            await Script("document.querySelector('[data-projects]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open");
            await Script("document.querySelector('[data-new-project]').click()");
            await WaitFor("document.querySelector('[data-new-project-dialog]').open");
            await DropFile(Path.Combine(SampleFolder, "optical-motion.mp4"));
            await WaitFor("document.querySelector('[data-new-project-dialog] [data-asset][aria-pressed=true]') && !document.querySelector('[data-start-project]').disabled");
            await Script("document.querySelector('[data-project-back]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open");
        }

        async Task VerifyExcludeRange()
        {
            foreach (int frame in new[] { 48, 72 })
            {
                await Script(@"(() => {
                    const axis = document.querySelector('.kh-axis'), rect = axis.getBoundingClientRect();
                    axis.dispatchEvent(new MouseEvent('click', {bubbles:true, clientX:rect.left + rect.width * " + frame + @" / 288}));
                })()");
                await AddPause("0.5");
            }
            const string snapshot = "JSON.stringify(window.kirihariQa.snapshot())";
            const string mode = "document.querySelector('[data-exclude-mode]').click()";
            const string pending = "document.querySelector('[data-exclude-dialog]').open && document.querySelector('.kh-cut-draft[data-excluded]:not([hidden])')";
            await Script("window.kirihariQa.record()");
            string before = await Script(snapshot);
            await Script(mode);
            await DragCut(48, 72, 288);
            await WaitFor(pending);
            Qa.Check(await Script(snapshot) == before, "The red exclusion draft does not change the edit before confirmation");
            using (var shot = File.Create(Path.Combine(qaFolder, "除く範囲の確認.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('[data-exclude-cancel]').click()");
            await WaitFor("document.querySelector('.kh-cut-draft').hidden");
            Qa.Check(await Script(snapshot) == before, "Cancelling an exclusion preserves cuts, pauses and captions");
            await Script(mode);
            await DragCut(72, 48, 288);
            await WaitFor(pending);
            await Script("document.querySelector('[data-exclude-apply]').click()");
            Qa.Check(await Script(@"(() => {
                const p = window.kirihariQa.snapshot(), [a,b] = p.ranges;
                return p.ranges.length === 2 && a.start === 24 && a.end === 48 && b.start === 72 && b.end === 144 &&
                    p.pauses.length === 1 && p.pauses[0].frame === 72 && p.pauses[0].target === b.id &&
                    p.captions.length === 4 && p.captions.every(c => c.target === a.id ? c.end === 48 : c.target === b.id && c.start === 72);
            })()") == "true", "Exclusion retains captions on both sides and keeps the pause on the right boundary only");
            string after = await Script(snapshot);
            await Script("document.querySelector('.kh-editor-heading [data-undo]').click()");
            Qa.Check(await Script(snapshot) == before, "One undo restores the complete excluded portion");
            await Script("document.querySelector('.kh-editor-heading [data-redo]').click()");
            Qa.Check(await Script(snapshot) == after, "One redo reapplies the complete exclusion");
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            var video = await Task.Run(() => tool.Read(lastExport, lifetime.Token));
            Qa.Check(video.frames == 108 && video.audio, "The exported MP4 omits the exact frames and retains the remaining pause and audio");
            await Script("document.querySelector('[data-export-dialog] [data-close]').click(); document.querySelector('[data-save-edit]').click()");
            await WaitFor("document.querySelector('[data-save-status]').textContent === '保存済み'");
        }

        async Task VerifyFolderChoices()
        {
            string configured = Path.Combine(qaFolder, "指定フォルダ");
            Directory.CreateDirectory(configured);
            string lastMedia = Path.GetDirectoryName(source.path);
            string projectLocation = ProjectDirectory;
            folderSettings = null;
            Qa.Check(InitialFolder("media", null) == lastMedia, "Previous media folder is persisted and used by default");
            Qa.Check(ExistingFolder(FolderSetting("projectLast")) != null && ExistingFolder(FolderSetting("outputLast")) != null,
                "Previously used project and output folders resolve after reloading preferences");
            qaFolderToPick = configured;
            await Script("document.querySelector('.kh-settings-menu [data-folder-settings]').click()");
            await WaitFor("document.querySelector('[data-folder-config]').open");
            await Script("document.querySelector('[data-folder-kind=media] [data-folder-action=pick]').click()");
            await WaitFor("document.querySelector('[data-folder-kind=media] small').textContent.includes('指定フォルダ')");
            await Script("document.querySelector('[data-folder-kind=save] [data-folder-action=pick]').click()");
            await WaitFor("document.querySelector('[data-folder-kind=save] small').textContent.includes('指定フォルダ') && !window.kirihariQa.debug().busy");
            await Script("document.querySelector('[data-folder-config] [data-close]').click()");
            folderSettings = null;
            Qa.Check(InitialFolder("media", null) == configured && InitialFolder("media", "last") == lastMedia, "Configured default and previous media folder are independent");
            foreach (string mode in new[] { "last", "project", "default" })
            {
                string expected = InitialFolder("media", mode);
                qaPickerDirectory = null;
                await Script("document.querySelector('.kh-settings-menu [data-media-location]').click()");
                await WaitFor("document.querySelector('[data-folder-dialog]').open");
                await Script("document.querySelector('[data-location-mode=" + mode + "]').click()");
                await WaitFor("!window.kirihariQa.debug().busy");
                Qa.Check(qaPickerDirectory == expected, "Media menu reaches the Windows picker with the chosen " + mode + " folder");
            }
            await Script("document.querySelector('[data-save-as]').click()");
            await WaitFor("document.querySelector('[data-folder-dialog]').open");
            await Script("document.querySelector('[data-location-mode=default]').click()");
            await WaitFor("!window.kirihariQa.debug().busy");
            Qa.Check(qaPickerDirectory == configured && Path.GetDirectoryName(projectFile) == configured && File.Exists(projectFile),
                "Save As uses the configured starting folder and saves the project there");
            await Script("document.querySelector('[data-folder]').click()");
            await WaitFor("document.querySelector('[data-folder-dialog]').open");
            await Script("document.querySelector('[data-location-mode=default]').click()");
            await WaitFor("document.querySelector('[data-output-path]').textContent.includes('指定フォルダ') && !window.kirihariQa.debug().busy");
            Qa.Check(projectLocation != null && FolderSetting("saveDefault") == configured, "MP4 output uses the configured save default");
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            Qa.Check(Path.GetDirectoryName(lastExport) == configured && File.Exists(lastExport), "A real MP4 is written to the chosen default folder");
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
            qaFolderToPick = null;
        }

        async Task VerifyThemeDoesNotChangeSlide()
        {
            await Script(@"void (async () => {
                const before = JSON.stringify(window.kirihariQa.snapshot());
                const original = await window.kirihariQa.overlay();
                const button = document.querySelector('.kh-settings-menu [data-theme-open]');
                button.click();
                const select = document.querySelector('[data-theme-select]');
                select.value = 'superlight'; select.dispatchEvent(new Event('change'));
                const themed = await window.kirihariQa.overlay();
                document.querySelector('[data-theme-dialog] [data-close]').click();
                window.qaSameOverlay = original.length > 100 && themed === original && JSON.stringify(window.kirihariQa.snapshot()) === before;
            })()");
            await WaitFor("typeof window.qaSameOverlay === 'boolean'");
            Qa.Check(await Script("window.qaSameOverlay") == "true", "Changing theme preserves the project and rendered slide bytes");
        }

        Task<string> Script(string code) { return browser.CoreWebView2.ExecuteScriptAsync(code); }
        Task<string> Mouse(string type, double x, double y, int buttons, string button, int clicks)
        {
            return browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", MediaTool.Json().Serialize(
                new { type, x, y, buttons, button, clickCount = clicks }));
        }
        async Task DragCut(int start, int end, int sourceFrames = 4320)
        {
            await Task.Delay(100); // Allow layout/scroll observers to settle before native coordinates are sampled.
            var points = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const r = document.querySelector('[data-lane=cut]').getBoundingClientRect();
                const x = frame => r.left + Math.max(0.05, r.width * frame / " + sourceFrames + @");
                const y = r.top + r.height / 2;
                window.qaCutHit = document.elementFromPoint(x(" + start + @"), y)?.outerHTML.slice(0, 500);
                window.qaPointerLog=[];
                if (!window.qaPointerListening) {
                    window.qaPointerListening=true;
                    for (const type of ['pointerdown','pointermove','pointerup','lostpointercapture'])
                        document.addEventListener(type,e=>window.qaPointerLog.push([type,e.target.className,e.clientX,e.clientY]),true);
                }
                return [x(" + start + "), x(" + end + @"), y];
            })()"));
            await Mouse("mouseMoved", points[0], points[2], 0, "none", 0);
            await Mouse("mousePressed", points[0], points[2], 1, "left", 1);
            await Mouse("mouseMoved", points[1], points[2], 1, "none", 0);
            await WaitFor("!document.querySelector('.kh-cut-draft').hidden");
            await Mouse("mouseReleased", points[1], points[2], 0, "left", 1);
        }
        async Task WaitFor(string condition, int attempts = 120)
        {
            for (int i = 0; i < attempts; i++)
            {
                if (await Script("Boolean(" + condition + ")") == "true") return;
                await Task.Delay(100);
            }
            throw new IOException("Window QA timed out waiting for " + condition + "\n" +
                await Script(@"(() => {
                    const q = s => document.querySelector(s), v = q('video');
                    return { status: q('[data-app-status]').textContent, saveStatus: q('[data-save-status]').textContent,
                        notices: window.qaNotices, exportStatus: q('[data-export-result]').textContent,
                        projectError: q('[data-project-error]').textContent,
                        time: v.currentTime, paused: v.paused, seeking: v.seeking, ready: v.readyState,
                        network: v.networkState, buffered: Array.from({length:v.buffered.length}, (_,i)=>[v.buffered.start(i),v.buffered.end(i)]),
                        play: q('[data-action=play]').getAttribute('aria-label'),
                        cursor: q('.kh-lanes').style.getPropertyValue('--position'), seekingWithHandle: q('[data-seek-handle]').hasAttribute('data-dragging'),
                        compactLayout: [innerWidth, innerHeight, q('.kh-preview').offsetHeight, q('[data-cut-hint]').getBoundingClientRect().toJSON(),
                            q('.kh-track-scroll').getBoundingClientRect().toJSON(), [...document.querySelectorAll('[data-lane=caption]')].map(row => row.offsetHeight)],
                        cuts: [...document.querySelectorAll('[data-range]')].map(c => [c.style.left,c.style.width]),
                        errors: window.qaErrors ?? [], debug: window.kirihariQa?.debug(), pointer: window.qaPointerLog, hit: window.qaCutHit, dialogs: [...document.querySelectorAll('dialog[open]')].map(x=>x.outerHTML.slice(0,120)), bounds: q('[data-lane=cut]').getBoundingClientRect().toJSON() };
                })()"));
        }
        async Task<double[]> MeasurePreview()
        {
            await Script(@"(() => {
                window.qaPreviewStats = null;
                window.qaPreviewError = null;
                const video = document.querySelector('[data-media]');
                const axis = document.querySelector('.kh-axis');
                const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
                const seek = async seconds => {
                    const r = axis.getBoundingClientRect();
                    const start = performance.now();
                    axis.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: r.left + r.width * seconds / 18 }));
                    while (video.seeking || Math.abs(video.currentTime - seconds) > .08) {
                        if (performance.now() - start > 6000)
                            throw new Error('Seek timed out at ' + seconds + ' (media ' + video.currentTime + ', seeking ' + video.seeking + ')');
                        await delay(2);
                    }
                    return performance.now() - start;
                };
                void (async () => {
                    const times = [];
                    for (const seconds of [14.2, 3.1, 12.4, 5.2, 15.1, 1.7, 10.3, 7.2]) times.push(await seek(seconds));
                    await seek(.3);
                    const button = document.querySelector('[data-action=play]');
                    const first = video.getVideoPlaybackQuality();
                    button.click();
                    const started = performance.now();
                    await delay(2500);
                    const elapsed = performance.now() - started;
                    const played = video.currentTime - .3;
                    const last = video.getVideoPlaybackQuality();
                    button.click();
                    times.sort((a, b) => a - b);
                    window.qaPreviewStats = [(times[3] + times[4]) / 2, times[7],
                        last.totalVideoFrames - first.totalVideoFrames, last.droppedVideoFrames - first.droppedVideoFrames, elapsed, played];
                })().catch(error => { window.qaPreviewError = error.message; });
            })()");
            await WaitFor("window.qaPreviewStats || window.qaPreviewError", 420);
            var error = await Script("window.qaPreviewError");
            Qa.Check(error == "null", "Preview seek performance: " + error);
            return MediaTool.Json().Deserialize<double[]>(await Script("window.qaPreviewStats"));
        }

        async Task<string[]> VerifyHeavyProxy()
        {
            string stress = Path.Combine(Path.GetDirectoryName(home.TrimEnd(Path.DirectorySeparatorChar)), "proxy-test", "KIRIHARI-4K60-stress.mp4");
            if (!File.Exists(stress)) return new string[0];
            await Script("document.querySelector('[data-projects]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open");
            await Script("document.querySelector('[data-new-project]').click()");
            await WaitFor("document.querySelector('[data-new-project-dialog]').open");
            await DropFile(stress);
            await WaitFor("document.querySelector('[data-new-project-dialog] [data-asset]')");
            bool cached = assets.Values.Single().proxies["M"].state == "ready";
            if (!cached)
            {
                await VerifyImportProxyChoices();
                Qa.Check(await Script("document.querySelector('[data-proxy-candidates]').textContent.includes('3840×2160・60 fps')") == "true",
                    "Heavy import proposes a proxy with its video specs");
                await Script("document.querySelector('[data-proxy-import=ORIGINAL]').click()");
            }
            
            await Script(@"document.querySelector('[data-project-name]').value = 'QA 4K60';
                document.querySelector('[data-new-project-dialog] [data-asset]').click();
                document.querySelector('[data-start-project]').click();");
            const string originalReady = "document.querySelector('.kh-picture').hasAttribute('data-media-ready') && document.querySelector('[data-media]').videoWidth === 3840";
            await WaitFor(originalReady);
            await SaveFromToolbar();
            string managed = Path.GetFileName(projectFile);
            var original = await MeasurePreview();
            await WaitFor("!document.querySelector('[data-media]').seeking");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Qa.Check(await Script("!document.querySelector('[data-preview-proxy]').hidden") == "true",
                "A recommended proxy remains available from the preview after dismissing the import offer");
            await SelectProxyQuality("M", !cached);
            const string proxyReady = "!document.querySelector('[data-proxy-dialog]').open && " +
                "document.querySelector('.kh-picture').hasAttribute('data-media-ready') && document.querySelector('[data-media]').videoHeight === 480";
            await WaitFor(proxyReady, 330);
            double proxySeconds = clock.Elapsed.TotalSeconds;
            var proxy = await MeasurePreview();
            var proxyFile = await Task.Run(() => tool.Read(source.proxies["M"].file, lifetime.Token));
            Qa.Check(proxyFile.width == 852 && proxyFile.height == 480 && proxyFile.fps == 60 && Math.Abs(proxyFile.duration - 18) < .05,
                "Proxy reduces decoding size and keeps the source duration");
            await Script(@"(() => {
                const axis = document.querySelector('.kh-axis');
                const r = axis.getBoundingClientRect();
                axis.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: r.left + r.width / 3 }));
            })()");
            await WaitFor("!document.querySelector('[data-media]').seeking && Math.abs(document.querySelector('[data-media]').currentTime - 6) < .07");
            await SelectProxyQuality("ORIGINAL");
            await WaitFor(originalReady);
            await WaitFor("!document.querySelector('[data-media]').seeking && Math.abs(document.querySelector('[data-media]').currentTime - 6) < .07");
            Qa.Check(await Script("document.querySelector('[data-preview-proxy]').dataset.proxyTone") == "\"ORIGINAL\"", "The pool icon can switch back to the original");
            await SelectProxyQuality("M");
            await WaitFor(proxyReady);
            await DragCut(60, 240, 1080);
            await WaitFor("document.querySelectorAll('[data-range]').length === 1 && !document.querySelector('[data-media]').seeking");
            var expected = MediaTool.Json().Deserialize<int>(await Script("Math.round(parseFloat(document.querySelector('[data-range]').style.width)*10.8)"));
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden", 250);
            string output = MediaTool.Json().Deserialize<string>(await Script("document.querySelector('[data-export-result]').textContent"));
            var rendered = await Task.Run(() => tool.Read(output, lifetime.Token));
            Qa.Check(rendered.width == 1920 && rendered.height == 1080 && rendered.fps == 60 && rendered.frames == expected,
                "Editing with a 852x480/60 proxy exports the selected original frames at 1080p60");
            await Script("document.querySelector('[data-export-dialog]').close()");
            await SelectProxyQuality("ORIGINAL");
            await SaveFromToolbar();
            await WaitFor("document.querySelector('[data-save-status]').textContent === '保存済み' && !window.kirihariQa.debug().busy");
            await Script("document.querySelector('[data-projects]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open");
            await Script("[...document.querySelectorAll('[data-project-id]')].find(button => button.dataset.projectId === " +
                MediaTool.Json().Serialize(managed) + ").click()");
            await WaitFor("!document.querySelector('[data-project-dialog]').open && " + originalReady);
            Qa.Check(await Script("document.querySelector('[data-preview-proxy]').dataset.proxyTone") == "\"ORIGINAL\"", "Saved original preference and proxy cache restore together");
            return new[] {
                "PASS: heavy import, manual proxy, position-preserving ON/OFF, saved preference and cache, seek/play and original-source export.",
                string.Format("Original 3840x2160 60 fps ({0} bytes): median seek {1:F1} ms, max {2:F1} ms, dropped {4:0}/{3:0} frames, {5:F2}s video in {6:F2}s.", source.bytes,
                    original[0], original[1], original[2], original[3], original[5], original[4] / 1000),
                string.Format("Proxy 852x480 60 fps ({0} bytes): median seek {1:F1} ms, max {2:F1} ms, dropped {4:0}/{3:0} frames, {5:F2}s video in {6:F2}s. {7} {8:F1}s.",
                    proxyFile.bytes, proxy[0], proxy[1], proxy[2], proxy[3], proxy[5], proxy[4] / 1000, cached ? "Cache opened in" : "Created and opened in", proxySeconds),
                output
            };
        }
        async Task VerifyZoomBounds()
        {
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s), zoom = q('[data-zoom]'), view = q('.kh-lane-view');
                const overview = q('[data-overview]'), before = JSON.stringify(window.kirihariQa.snapshot());
                const factor = value => {
                    zoom.value = value;
                    zoom.dispatchEvent(new Event('input'));
                    return q('.kh-lanes').getBoundingClientRect().width / view.clientWidth;
                };
                const a = factor(1.25), b = factor(1.75);
                const aligned = !overview.hidden && Math.abs(overview.getBoundingClientRect().bottom - q('.kh-axis').getBoundingClientRect().top) < 1;
                q('[data-fit]').click();
                return a > 2.3 && a < 2.5 && b > 3.2 && b < 3.5 && aligned && overview.hidden &&
                    JSON.stringify(window.kirihariQa.snapshot()) === before;
            })()") == "true", "The slider uses fractional scales, with an overview above the ruler and a clean full-view reset");
            await Script("(() => { const z = document.querySelector('[data-zoom]'); z.value = 2; z.dispatchEvent(new Event('input')); })()");
            Qa.Check(await Script(@"(() => {
                const view = document.querySelector('.kh-lane-view');
                const lanes = document.querySelector('.kh-lanes').getBoundingClientRect();
                return Math.abs(lanes.left - view.getBoundingClientRect().left) < 1 &&
                    Math.abs(view.scrollWidth - lanes.width) < 1;
            })()") == "true", "Zoom fills the view from the first frame without padding");
            await Script("(() => { const view = document.querySelector('.kh-lane-view'); view.scrollLeft = (view.scrollWidth - view.clientWidth) / 2; })()");
            await WaitFor(@"(() => {
                const view = document.querySelector('.kh-lane-view').getBoundingClientRect();
                const cursor = document.querySelector('.kh-playhead').getBoundingClientRect();
                const overview = document.querySelector('[data-overview]');
                return Math.abs(cursor.left + cursor.width / 2 - view.left - view.width / 2) < 1 &&
                    Math.abs(parseFloat(overview.style.getPropertyValue('--visible-start')) - 37.5) < .2 &&
                    Math.abs(parseFloat(overview.style.getPropertyValue('--visible-span')) - 25) < .2;
            })()");
            await Script("document.querySelector('.kh-lane-view').scrollLeft = 1e9");
            await WaitFor(@"(() => {
                const view = document.querySelector('.kh-lane-view').getBoundingClientRect();
                const cursor = document.querySelector('.kh-playhead').getBoundingClientRect();
                return Math.abs(cursor.right - view.right) < 1 &&
                    Math.abs(parseFloat(document.querySelector('[data-overview]').style.getPropertyValue('--visible-start')) - 75) < .2;
            })()");
            await Script("document.querySelector('.kh-lane-view').scrollLeft = 0");
            await WaitFor("document.querySelector('[data-source]').textContent === '00:00'");
            var points = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const view = document.querySelector('.kh-lane-view').getBoundingClientRect();
                const lane = document.querySelector('[data-lane=original]').getBoundingClientRect();
                return [view.left + view.width * .6, view.left + view.width * .45, lane.top + lane.height / 2];
            })()"));
            await Mouse("mouseMoved", points[0], points[2], 0, "none", 0);
            await Mouse("mousePressed", points[0], points[2], 1, "left", 1);
            await Mouse("mouseMoved", points[1], points[2], 1, "none", 0);
            await Mouse("mouseReleased", points[1], points[2], 0, "left", 1);
            Qa.Check(await Script(@"(() => {
                const view = document.querySelector('.kh-lane-view');
                const cursor = document.querySelector('.kh-playhead').getBoundingClientRect();
                const offset = cursor.left - view.getBoundingClientRect().left;
                return view.scrollLeft === 0 && Math.abs(offset - view.clientWidth * .15) < 3;
            })()") == "true", "Dragging from the beginning moves the cursor smoothly through the first visible frames");
            await Script(@"document.querySelector('[data-jump=""-10""]').click()");
            await WaitFor("document.querySelector('[data-source]').textContent === '00:00'");
        }
        async Task VerifyTrim(int edge, int deltaFrames)
        {
            var points = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const handle = document.querySelector('[data-range] [data-edge=""" + edge + @"""]');
                const grip = handle.getBoundingClientRect();
                const lane = document.querySelector('.kh-lanes').getBoundingClientRect();
                const x = grip.left + grip.width / 2, y = grip.top + grip.height / 2;
                const hit = document.elementFromPoint(x, y);
                const start = handle.parentElement.getBoundingClientRect()." + (edge == 0 ? "left" : "right") + @";
                return [x, x + lane.width * " + deltaFrames + @" / 4320, y, hit === handle ? 1 : 0, start - lane.left];
            })()"));
            Qa.Check(points[3] == 1, "Trim QA can hit edge " + edge + " at " + points[0] + "," + points[2]);
            await Mouse("mouseMoved", points[0], points[2], 0, "none", 0);
            await Mouse("mousePressed", points[0], points[2], 1, "left", 1);
            await Mouse("mouseMoved", points[1], points[2], 1, "none", 0);
            await WaitFor(@"(() => {
                const band = document.querySelector('[data-range]').getBoundingClientRect();
                const lane = document.querySelector('.kh-lanes').getBoundingClientRect();
                return Math.abs(band." + (edge == 0 ? "left" : "right") + @" - lane.left - " +
                points[4].ToString(System.Globalization.CultureInfo.InvariantCulture) + " - lane.width * " + deltaFrames + @" / 4320) < 2 &&
                    !document.querySelector('video').seeking;
            })()");
            var tracked = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const band = document.querySelector('[data-range]').getBoundingClientRect();
                const cursor = document.querySelector('.kh-playhead').getBoundingClientRect();
                const lane = document.querySelector('.kh-lanes').getBoundingClientRect();
                const edge = band." + (edge == 0 ? "left" : "right") + @";
                const time = (edge - lane.left) / lane.width * 180;
                return [cursor.left + cursor.width / 2 - edge, document.querySelector('video').currentTime - time,
                    edge - lane.left, lane.width];
            })()"));
            Qa.Check(Math.Abs(tracked[0]) < 2 && Math.Abs(tracked[1]) < .05, "Playhead and video follow the cut edge before release");
            Qa.Check(Math.Abs(tracked[2] - points[4] - deltaFrames * tracked[3] / 4320) < 2,
                "Dragging moves the chosen edge by the expected number of frames (edge " + edge +
                ", old " + points[4] + ", new " + tracked[2] + ", expected delta " + deltaFrames * tracked[3] / 4320 + ")");
            await Mouse("mouseReleased", points[1], points[2], 0, "left", 1);
            Qa.Check(await Script("document.querySelectorAll('[data-range]').length") == "2",
                "Trimming an existing cut keeps the number of cuts at two");
        }
        async Task VerifyRestart(bool cut)
        {
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s);
                const position = " + (cut ? "parseFloat(q('[data-range]').style.left)" : "0") + @";
                q('[data-action=restart]').click();
                return q('[data-action=play]').getAttribute('aria-label') === '一時停止' &&
                    Math.abs(parseFloat(q('.kh-lanes').style.getPropertyValue('--position')) - position) < .00001;
            })()") == "true", cut ? "Restart starts at the first retained cut" : "Restart starts the original at zero");
        }
        async Task SaveFromToolbar()
        {
            await WaitFor("!window.kirihariQa.debug().busy");
            await Script("document.querySelector('[data-save-edit]').click()");
            await WaitFor("!window.kirihariQa.debug().busy && document.querySelector('[data-save-status]').textContent === '保存済み'");
        }
        async Task VerifyPausePosition()
        {
            Qa.Check(await Script(@"(() => {
                const q = selector => document.querySelector(selector);
                const handle = q('[data-seek-handle]'), button = q('[data-action=play]'), video = q('[data-media]');
                const frame = handle.getAttribute('aria-valuenow'), scroll = q('.kh-lane-view').scrollLeft;
                if (button.getAttribute('aria-label') !== '一時停止') return false;
                button.click();
                return video.paused && button.getAttribute('aria-label') === '再生' &&
                    handle.getAttribute('aria-valuenow') === frame && q('.kh-lane-view').scrollLeft === scroll;
            })()") == "true", "Pausing keeps the timeline frame and horizontal scroll position");
        }
        async Task SetCutSpeed(string selector, string speed)
        {
            await RightClick(selector);
            await Script("document.querySelector('[data-menu-speed]').click()");
            await WaitFor("document.querySelector('[data-cut-speed-dialog]').open");
            await Script("document.querySelector('[data-cut-speed]').value = '" + speed + "'; " +
                "document.querySelector('[data-cut-speed]').dispatchEvent(new Event('input')); " +
                "document.querySelector('[data-cut-speed-apply]').click()");
            await WaitFor("!document.querySelector('[data-cut-speed-dialog]').open");
        }

        async Task VerifyCutSpeeds()
        {
            await Script(@"document.querySelector('.kh-settings summary').click();
                document.querySelector('.kh-settings [data-open-practice]').click();");
            await WaitFor(@"!window.kirihariQa.debug().busy && document.querySelector('.kh-picture').hasAttribute('data-media-ready') &&
                document.querySelector('[data-original-path]').textContent.endsWith('cat-motion-short.mp4')");
            await DragCut(24, 96, 288);
            await DragCut(120, 192, 288);
            string[] cuts = MediaTool.Json().Deserialize<string[]>(await Script("[...document.querySelectorAll('[data-range]')].map(b => b.dataset.range)"));
            Qa.Check(cuts.Length == 2, "Two speed test cuts were created");
            await SetCutSpeed("[data-range='" + cuts[0] + "'] [data-clip]", "0.75");
            await SetCutSpeed("[data-range='" + cuts[1] + "'] [data-clip]", "1.5");
            await Script(@"window.qaSpeedCutFrame = frame => {
                const axis = document.querySelector('.kh-axis'), r = axis.getBoundingClientRect();
                axis.dispatchEvent(new MouseEvent('click', {bubbles:true,clientX:r.left+r.width*frame/288}));
            }; window.qaSpeedCutFrame(48);");
            await AddPause("1");
            await WaitFor("document.querySelector('[data-total]').textContent === '7秒'");
            await Script("document.querySelector('[data-action=restart]').click()");
            await WaitFor("!document.querySelector('[data-media]').paused && document.querySelector('[data-media]').playbackRate === .75");
            await WaitFor("!document.querySelector('[data-media]').paused && document.querySelector('[data-media]').currentTime > 5 && document.querySelector('[data-media]').playbackRate === 1.5");
            await VerifyPausePosition();
            await SaveFromToolbar();
            qaProjectToOpen = Path.Combine(qaFolder, "確認.kirihari");
            string saved = File.ReadAllText(qaProjectToOpen);
            Qa.Check(saved.Contains("\"format\":\"kirihari-video-3\"") && saved.Contains("\"speed\":0.75") && saved.Contains("\"speed\":1.5"),
                "Cut speeds are persisted in the versioned project format");
            await Script("document.querySelector('[data-open-project]').click()");
            await WaitFor("document.querySelector('.kh-picture').hasAttribute('data-media-ready') && !window.kirihariQa.debug().busy");
            Qa.Check(await Script("window.kirihariQa.snapshot().ranges.map(c => c.speed).join(',')") == "\"0.75,1.5\"" &&
                await Script("document.querySelector('[data-total]').textContent") == "\"7秒\"", "Reopening restores the two cut speeds and the edited length");
            await Script("window.qaSpeedCutFrame(156); document.querySelector('[data-split]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().ranges.map(c => c.speed).join(',')") == "\"0.75,1.5,1.5\"",
                "A split inherits its cut speed on both sides");
            await SaveFromToolbar();
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            var result = await Task.Run(() => tool.Read(lastExport, lifetime.Token));
            Qa.Check(result.frames == 168 && result.audio, "The seven-second edited preview agrees with the exported MP4");
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
            qaProjectToOpen = null;
        }
        async Task VerifyCaptureAndBookmarks()
        {
            await Script("document.querySelector('[data-projects]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open");
            await Script(@"document.querySelector('[data-project-menu] summary').click();
                document.querySelector('[data-project-menu] [data-open-practice]').click();");
            await WaitFor("!document.querySelector('[data-project-dialog]').open && document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
            await Script("document.querySelector('[data-action=play]').click()");
            await WaitFor("document.querySelector('[data-media]').currentTime > .3 && !document.querySelector('[data-media]').paused");
            Qa.Check(await Script("!document.querySelector('[data-cut-hint]').hidden && document.querySelector('[data-cut-hint]').textContent === 'Kで範囲を記録'") == "true",
                "Original playback explains the capture key in the cut lane");
            await Script(@"window.qaFrame = () => Number(document.querySelector('[data-seek-handle]').getAttribute('aria-valuenow'));
                window.qaCaptureStart = window.qaFrame(); window.qaHotkey('k');
                window.qaCaptureWidth = parseFloat(document.querySelector('[data-key-cut-draft]').style.width);
                document.dispatchEvent(new KeyboardEvent('keydown', {key:'k', bubbles:true, repeat:true}));");
            await WaitFor(@"!document.querySelector('[data-key-cut-draft]').hidden &&
                parseFloat(document.querySelector('[data-key-cut-draft]').style.width) > window.qaCaptureWidth + 2");
            Qa.Check(await Script("window.kirihariQa.snapshot().ranges.length === 0") == "true", "The growing K draft does not create a cut until confirmed");
            Qa.Check(await Script(@"(() => {
                window.qaCaptureEnd = window.qaFrame();
                window.qaHotkey('K');
                const { ranges } = window.kirihariQa.snapshot();
                return ranges.length === 1 && ranges[0].start === window.qaCaptureStart && ranges[0].end === window.qaCaptureEnd &&
                    document.querySelector('[data-key-cut-draft]').hidden &&
                    document.querySelector('[data-source-mode=original]').getAttribute('aria-pressed') === 'true' &&
                    !document.querySelector('[data-media]').paused;
            })()") == "true", "K saves the two original playhead positions without interrupting original playback");
            await WaitFor("window.qaFrame() > window.qaCaptureEnd + 6");
            await Script("window.qaHotkey('k')");
            await WaitFor("!document.querySelector('[data-key-cut-draft]').hidden");
            await Script(@"document.querySelector('[data-guide-open]').click();
                document.querySelector('[data-guide-close]').click();
                document.querySelector('[data-bookmarks-open]').click();
                document.querySelector('[data-bookmarks-dialog] [data-close]').click();");
            Qa.Check(await Script("!document.querySelector('[data-key-cut-draft]').hidden") == "true", "Help and bookmarks preserve the pending K capture");
            await Script("document.querySelector('[data-action=play]').click()");
            await WaitFor("!document.querySelector('[data-media]').paused");
            await Script(@"const handle = document.querySelector('[data-seek-handle]');
                handle.focus({preventScroll:true}); window.qaHotkey('Escape', handle);");
            Qa.Check(await Script(@"document.querySelector('[data-key-cut-draft]').hidden &&
                !document.querySelector('[data-media]').paused && window.kirihariQa.snapshot().ranges.length === 1") == "true",
                "Escape cancels the draft without stopping playback even when the seek handle is focused");
            await Script(@"window.qaCaptureSeek = frame => {
                const axis = document.querySelector('.kh-axis'), r = axis.getBoundingClientRect();
                axis.dispatchEvent(new MouseEvent('click', {bubbles:true, clientX:r.left + r.width * frame / 288}));
            };
            window.qaCaptureSeek(36); document.querySelector('[data-action=play]').click();");
            await WaitFor("window.qaFrame() > 36 && !document.querySelector('[data-media]').paused");
            Qa.Check(await Script(@"(() => {
                const q = selector => document.querySelector(selector);
                const draft = q('[data-key-cut-draft]');
                window.qaSecondCutStart = window.qaFrame();
                window.qaHotkey('k');
                window.qaHotkey('ArrowLeft');
                if (draft.hidden || window.qaFrame() >= window.qaSecondCutStart ||
                    Math.abs(parseFloat(draft.style.left)) > .001 ||
                    Math.abs(parseFloat(draft.style.width) * 2.88 - window.qaSecondCutStart) > .001) return false;
                window.qaHotkey('ArrowRight');
                if (draft.hidden || parseFloat(draft.style.width) <= 0 || q('[data-media]').paused) return false;
                window.qaHotkey('ArrowLeft');
                return !draft.hidden;
            })()") == "true", "Left and right jumps update the draft across the full selected span, including the preceding cut");
            await DragControl("[data-seek-handle]", "document.querySelector('.kh-lanes').getBoundingClientRect().width / 3", 0);
            await WaitFor(@"window.qaFrame() > window.qaSecondCutStart &&
                document.querySelector('[data-media]').paused && !document.querySelector('[data-media]').seeking");
            Qa.Check(await Script(@"(() => {
                const q = selector => document.querySelector(selector);
                const draft = q('[data-key-cut-draft]'), handle = q('[data-seek-handle]');
                if (draft.hidden || document.activeElement !== handle) return false;
                const before = window.qaFrame();
                window.qaHotkey('ArrowRight', handle);
                if (draft.hidden || window.qaFrame() !== before + 1) return false;
                handle.blur();
                window.qaHotkey('ArrowRight');
                if (draft.hidden || window.qaFrame() !== 288) return false;
                window.qaHotkey('ArrowLeft');
                if (draft.hidden || window.qaFrame() !== 48) return false;
                q('[data-action=play]').click();
                if (draft.hidden || q('[data-action=play]').getAttribute('aria-label') !== '一時停止') return false;
                q('[data-action=play]').click();
                handle.focus({preventScroll:true});
                window.qaHotkey('ArrowRight', handle);
                const end = window.qaFrame();
                window.qaHotkey('k', handle);
                const { ranges } = window.kirihariQa.snapshot();
                return draft.hidden && ranges.length === 2 && ranges[1].start === window.qaSecondCutStart &&
                    ranges[1].end === end && q('[data-media]').paused;
            })()") == "true", "Scrubbing, stepping, paused jumps and playback toggles retain the K anchor, and K confirms with the seek handle focused");
            Qa.Check(await Script(@"(() => {
                const q = selector => document.querySelector(selector);
                const draft = q('[data-key-cut-draft]'), handle = q('[data-seek-handle]');
                window.qaCaptureSeek(96);
                q('[data-action=play]').click();
                window.qaHotkey('k');
                window.qaHotkey('k');
                if (draft.hidden || window.kirihariQa.snapshot().ranges.length !== 2) return false;
                window.qaCaptureSeek(72);
                if (draft.hidden || Math.abs(parseFloat(draft.style.left) * 2.88 - 72) > .001 ||
                    Math.abs(parseFloat(draft.style.width) * 2.88 - 24) > .001) return false;
                handle.focus({preventScroll:true});
                window.qaHotkey('k', handle);
                const { ranges } = window.kirihariQa.snapshot();
                return draft.hidden && ranges.length === 3 && ranges[2].start === 72 &&
                    ranges[2].end === 96 && q('[data-media]').paused;
            })()") == "true", "K confirms an exact reverse range from the earlier playhead to the first K press, including while paused");
            string annotatedCut = MediaTool.Json().Deserialize<string>(await Script("window.kirihariQa.snapshot().ranges[2].id"));
            await SetCutSpeed("[data-range='" + annotatedCut + "'] [data-clip]", "2");
            await Script("window.qaCaptureSeek(80)");
            await AddPause("0.5");
            await Script(@"document.querySelector('[data-add-text]').click();
                const captionEditor = document.activeElement;
                captionEditor.value = '維持するテロップ';
                captionEditor.dispatchEvent(new Event('input', {bubbles:true}));
                document.querySelector('[data-slide-done]').click();
                document.querySelector('[data-source-mode=original]').click();");
            Qa.Check(await Script(@"(() => {
                const q = selector => document.querySelector(selector);
                window.qaCutsBeforeFill = window.kirihariQa.snapshot();
                window.qaCaptureSeek(120);
                q('[data-action=play]').click();
                window.qaHotkey('k');
                window.qaHotkey('ArrowLeft');
                window.qaHotkey('k');
                const before = window.qaCutsBeforeFill;
                const { ranges, pauses, captions } = window.kirihariQa.snapshot();
                return ranges[0].start === 0 && ranges.at(-1).end === 120 &&
                    ranges.every((cut, index) => index === 0 || ranges[index - 1].end === cut.start) &&
                    before.ranges.every(cut => JSON.stringify(ranges.find(next => next.id === cut.id)) === JSON.stringify(cut)) &&
                    before.ranges[2].speed === 2 && pauses.length === 1 && captions.length === 1 &&
                    JSON.stringify([pauses, captions]) === JSON.stringify([before.pauses, before.captions]) &&
                    !q('[data-range][data-conflict]') && q('[data-action=play]').getAttribute('aria-label') === '一時停止';
            })()") == "true", "Reverse K fills every gap across existing cuts, preserves speed, pause and caption, and keeps playback active");
            Qa.Check(await Script(@"(() => {
                const q = selector => document.querySelector(selector);
                const snapshot = () => JSON.stringify(window.kirihariQa.snapshot());
                window.qaFilledCuts = snapshot();
                q('.kh-editor-heading [data-undo]').click();
                if (snapshot() !== JSON.stringify(window.qaCutsBeforeFill)) return false;
                q('.kh-editor-heading [data-redo]').click();
                if (snapshot() !== window.qaFilledCuts) return false;
                window.qaCaptureSeek(80);
                q('[data-action=play]').click();
                window.qaHotkey('k');
                if (q('[data-key-cut-draft]').hidden) return false;
                window.qaCaptureSeek(84);
                window.qaHotkey('k');
                return q('[data-key-cut-draft]').hidden && snapshot() === window.qaFilledCuts &&
                    q('[data-app-status]').textContent.includes('すでに');
            })()") == "true", "One Undo and Redo handle all new cuts, and K can start and finish inside a retained interval without creating overlap");
            await Script(@"window.qaBookmarkFrame = window.qaFrame();
                document.querySelector('[data-bookmarks-open]').click();
                document.querySelector('[data-bookmark-note]').value = '手順の目印';
                document.querySelector('[data-bookmark-form]').requestSubmit();
                const note = document.querySelector('[data-bookmark-row] input');
                note.value += '（追記）'; note.dispatchEvent(new Event('change'));
                document.querySelector('[data-bookmarks-dialog] [data-close]').click();");
            Qa.Check(await Script(@"window.kirihariQa.snapshot().bookmarks[0].frame === window.qaBookmarkFrame &&
                document.querySelector('[data-bookmark-marker]').title.includes('手順の目印（追記）')") == "true",
                "A bookmark uses the current original frame and the ruler shows its edited note");
            await SaveFromToolbar();
            qaProjectToOpen = Path.Combine(qaFolder, "確認.kirihari");
            string saved = File.ReadAllText(qaProjectToOpen);
            Qa.Check(saved.Contains("\"format\":\"kirihari-video-4\"") && saved.Contains("\"bookmarks\""), "Manual save uses the bookmark project format");
            await Script("document.querySelector('[data-open-project]').click()");
            await WaitFor("document.querySelector('.kh-picture').hasAttribute('data-media-ready') && !window.kirihariQa.debug().busy");
            Qa.Check(await Script(@"(() => {
                const saved = JSON.parse(window.qaFilledCuts), opened = window.kirihariQa.snapshot();
                const content = project => [
                    project.ranges.map(cut => [cut.id, cut.start, cut.end, cut.mediaId, cut.mediaOrigin, cut.speed]),
                    project.pauses.map(pause => [pause.target, pause.frame, pause.seconds]),
                    project.captions.map(caption => [caption.id, caption.target, caption.start, caption.end,
                        caption.objects.map(object => object.text)])
                ];
                return JSON.stringify(content(opened)) === JSON.stringify(content(saved));
            })()") == "true", "Reopening retains the added cuts and the original per-cut edits");
            await Script("document.querySelector('[data-bookmarks-open]').click()");
            Qa.Check(await Script("document.querySelector('[data-bookmark-row] input').value") == "\"手順の目印（追記）\"", "Reopening restores the bookmark note");
            await Script("document.querySelector('[data-bookmark-jump]').click()");
            Qa.Check(await Script(@"window.qaFrame() === window.qaBookmarkFrame &&
                document.querySelector('[data-source-mode=original]').getAttribute('aria-pressed') === 'true' &&
                document.querySelector('[data-media]').paused") == "true", "Clicking a saved bookmark seeks to the original frame and pauses there");
            await Script(@"document.querySelector('[data-bookmarks-open]').click();
                document.querySelector('[data-bookmark-remove]').click();
                document.querySelector('[data-bookmarks-dialog] [data-close]').click();");
            Qa.Check(await Script("!window.kirihariQa.snapshot().bookmarks") == "true", "Deleting a bookmark removes it from the project");
            await Script("document.querySelector('.kh-editor-heading [data-undo]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().bookmarks[0].frame === window.qaBookmarkFrame") == "true", "Undo restores a deleted bookmark");
            await Script("document.querySelector('[data-source-mode=cut]').click(); document.querySelector('[data-action=restart]').click()");
            await WaitFor("window.qaFrame() > 108 && document.querySelector('[data-action=play]').getAttribute('aria-label') === '一時停止'");
            await VerifyPausePosition();
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            var filled = await Task.Run(() => tool.Read(lastExport, lifetime.Token));
            Qa.Check(filled.frames == 120 && filled.audio, "Playback and MP4 cover the filled five-second span with the retained speed and pause");
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
            qaProjectToOpen = null;
        }
        async Task VerifySeekHandle()
        {
            await Script(@"(() => {
                const axis = document.querySelector('.kh-axis'), r = axis.getBoundingClientRect();
                axis.dispatchEvent(new MouseEvent('click', {bubbles:true, clientX:r.left + r.width * 16 / 180}));
            })()");
            await WaitFor("document.querySelector('[data-media]').paused && !document.querySelector('[data-media]').seeking && Math.abs(document.querySelector('[data-media]').currentTime - 16) < .05");
            await Script("document.querySelector('[data-action=play]').click()");
            await WaitFor("document.querySelector('[data-media]').currentTime > 16.1");
            await VerifyPausePosition();
            var points = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const r = document.querySelector('[data-seek-handle]').getBoundingClientRect();
                return [r.left + r.width / 2, r.top + r.height / 2];
            })()"));
            await Mouse("mouseMoved", points[0], points[1], 0, "none", 0);
            await Mouse("mousePressed", points[0], points[1], 1, "left", 1);
            await WaitFor("document.querySelector('[data-seek-handle]').hasAttribute('data-dragging')");
            await Mouse("mouseMoved", points[0] + 90, points[1], 1, "none", 0);
            await WaitFor(@"(() => {
                const v = document.querySelector('[data-media]'), handle = document.querySelector('[data-seek-handle]');
                return handle.hasAttribute('data-dragging') && v.paused && !v.seeking && v.currentTime > 17 &&
                    Math.abs(v.currentTime * 24 - Number(handle.getAttribute('aria-valuenow'))) < 1;
            })()");
            await Mouse("mouseMoved", points[0] - 45, points[1], 1, "none", 0);
            await WaitFor(@"(() => {
                const v = document.querySelector('[data-media]'), handle = document.querySelector('[data-seek-handle]');
                return v.paused && !v.seeking && v.currentTime < 15.8 &&
                    Math.abs(v.currentTime * 24 - Number(handle.getAttribute('aria-valuenow'))) < 1;
            })()");
            await Mouse("mouseReleased", points[0] - 45, points[1], 0, "left", 1);
            Qa.Check(await Script(@"(() => {
                const handle = document.querySelector('[data-seek-handle]');
                const previous = Number(handle.getAttribute('aria-valuenow'));
                handle.dispatchEvent(new KeyboardEvent('keydown', {key:'ArrowRight', bubbles:true}));
                return !handle.hasAttribute('data-dragging') && Number(handle.getAttribute('aria-valuenow')) === previous + 1 &&
                    document.querySelectorAll('[data-range]').length === 0;
            })()") == "true", "Release leaves the video paused and the focused seek handle moves by one frame");
            await Script("document.querySelector('[data-seek-handle]').dispatchEvent(new KeyboardEvent('keydown', {key:'Home', bubbles:true}))");
            await WaitFor("!document.querySelector('[data-media]').seeking && document.querySelector('[data-media]').currentTime < .05");
        }
        async Task VerifyPlaybackHotkeys()
        {
            await Script(@"window.qaHotkey = (key, target = document) =>
                target.dispatchEvent(new KeyboardEvent('keydown', {key, bubbles:true, cancelable:true}));
                window.qaSpeed = value => {
                    const input = document.querySelector('[data-speed]');
                    input.value = value;
                    input.dispatchEvent(new Event('input'));
                };");
            Qa.Check(await Script(@"(() => {
                const input = document.querySelector('[data-speed]'), label = document.querySelector('[data-speed-value]');
                const choices = [.1,.5,1,1.5,2,4,8,16,32];
                const values = choices.map((_, index) => { window.qaSpeed(index); return label.textContent; });
                window.qaSpeed(2);
                return Number(input.max) === choices.length - 1 && values.join(',') === choices.map(speed => '×' + speed).join(',');
            })()") == "true", "The preview slider exposes exactly the nine requested speed steps");
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s), v = q('[data-media]');
                window.qaHotkey('ArrowUp');
                if (v.playbackRate !== 1) return false;
                q('[data-action=play]').click();
                window.qaHotkey('ArrowUp');
                if (v.playbackRate !== 1.5) return false;
                window.qaHotkey('ArrowDown');
                const handle = q('[data-seek-handle]');
                const frame = Number(handle.getAttribute('aria-valuenow'));
                window.qaHotkey('ArrowRight');
                if (Number(handle.getAttribute('aria-valuenow')) !== frame + 240) return false;
                window.qaHotkey('ArrowLeft');
                return v.playbackRate === 1 && Number(handle.getAttribute('aria-valuenow')) === frame;
            })()") == "true", "Default arrows work during playback and preserve the 10-second step on short projects");
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s), v = q('[data-media]');
                window.qaSpeed(6);
                window.qaHotkey('ArrowUp');
                if (v.playbackRate !== 16 || q('[data-speed-value]').textContent !== '×16') return false;
                window.qaHotkey('ArrowUp');
                return v.paused && q('[data-speed-value]').textContent === '×32';
            })()") == "true", "The two fastest speed settings use native 16x and silent 32x scanning");
            await WaitFor("document.querySelector('[data-media]').currentTime > 5 && !document.querySelector('[data-media]').seeking");
            await Script("window.qaHotkey('ArrowDown')");
            await WaitFor("!document.querySelector('[data-media]').paused && document.querySelector('[data-media]').playbackRate === 16");
            await VerifyPausePosition();
            await Script(@"window.qaSpeed(2);
                window.qaSlowStart = document.querySelector('[data-media]').currentTime;
                document.querySelector('[data-action=play]').click();
                window.qaHotkey('ArrowDown'); window.qaHotkey('ArrowDown');");
            await WaitFor(@"(() => {
                const media = document.querySelector('[data-media]');
                return !media.paused && media.playbackRate === .1 && media.currentTime > window.qaSlowStart + .02;
            })()");
            await VerifyPausePosition();
            await Script("window.qaSpeed(2); document.querySelector('[data-hotkeys-open]').click()");
            await WaitFor("document.querySelector('[data-hotkeys-dialog]').open");
            Qa.Check(await Script(@"!document.querySelector('.kh-settings [data-undo]') && !document.querySelector('.kh-settings [data-redo]') &&
                !!document.querySelector('.kh-editor-heading [data-undo]') && !!document.querySelector('.kh-editor-heading [data-redo]')") == "true",
                "Undo and redo remain on the timeline and are absent from Settings");
            await Script(@"(() => {
                const q = s => document.querySelector(s);
                q('[data-hotkey=keep]').value = 'S';
                q('[data-hotkey=split]').value = 'S';
                q('[data-hotkeys-save]').click();
            })()");
            await WaitFor("document.querySelector('[data-hotkeys-dialog]').open && document.querySelector('[data-hotkeys-error]').textContent.includes('同じキー')");
            await Script(@"(() => {
                const q = s => document.querySelector(s);
                q('[data-hotkey=keep]').value = 'K';
                q('[data-hotkey=capture]').value = 'C';
                q('[data-hotkey=caption]').value = 'T';
                q('[data-hotkey=pause]').value = 'P';
                q('[data-hotkeys-save]').click();
                window.qaRulerTime = seconds => {
                    const axis = q('.kh-axis'), r = axis.getBoundingClientRect();
                    axis.dispatchEvent(new MouseEvent('click', {bubbles:true, clientX:r.left + r.width * seconds / 180}));
                };
                window.qaRulerTime(10);
                window.qaHotkey('k');
                window.qaRulerTime(20);
                window.qaHotkey('s');
                window.qaRulerTime(25);
                window.qaHotkey('p');
            })()");
            await WaitFor("document.querySelector('[data-pause-dialog]').open");
            await Script("document.querySelector('[data-pause-seconds]').value='1'; document.querySelector('[data-pause-apply]').click(); window.qaHotkey('t')");
            await WaitFor("document.activeElement?.matches('.kh-text-editor') && document.querySelectorAll('[data-caption-wrap]').length === 1");
            Qa.Check(await Script(@"(() => {
                window.qaHotkey('t', document.activeElement);
                const project = window.kirihariQa.snapshot();
                return project.ranges.length === 2 && project.pauses.length === 1 && project.captions.length === 1 &&
                    JSON.parse(localStorage.getItem('kirihari-ui')).hotkeys.caption === 'T';
            })()") == "true", "Assigned editing keys work while paused, persist, and leave text entry alone");
            await Script(@"document.querySelector('[data-slide-done]').click();
                window.kirihariQa.undoAll();
                document.querySelector('[data-hotkeys-open]').click();
                document.querySelector('[data-hotkeys-reset]').click();
                document.querySelector('[data-hotkeys-save]').click();
                document.querySelector('[data-seek-handle]').dispatchEvent(new KeyboardEvent('keydown', {key:'Home', bubbles:true}));");
            await WaitFor("document.querySelectorAll('[data-range]').length === 0 && !document.querySelector('[data-media]').seeking");
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s);
                q('[data-jump-settings-open]').click();
                q('[data-jump-width=small] [data-jump-percent]').value = '0';
                q('[data-jump-width=small] [data-jump-minimum]').value = '7';
                q('[data-jump-width=large] [data-jump-percent]').value = '0';
                q('[data-jump-width=large] [data-jump-minimum]').value = '25';
                q('[data-jump-settings-save]').click();
                const large = q('[data-jump=""10""]'), small = q('[data-jump=""5""]');
                const handle = q('[data-seek-handle]');
                large.click();
                if (Number(handle.getAttribute('aria-valuenow')) !== 600 || large.getAttribute('aria-label') !== '25秒送る' ||
                    small.getAttribute('aria-label') !== '7秒送る') return false;
                q('[data-action=play]').click();
                window.qaHotkey('ArrowLeft');
                q('[data-action=play]').click();
                return Number(handle.getAttribute('aria-valuenow')) === 0 &&
                    JSON.parse(localStorage.getItem('kirihari-ui')).jumpWidths.large.minimum === 25;
            })()") == "true", "Settings save custom jump widths for the buttons and keyboard");
            await Script(@"document.querySelector('[data-jump-settings-open]').click();
                document.querySelector('[data-jump-settings-reset]').click();
                document.querySelector('[data-jump-settings-save]').click();");
        }
        async Task DropFile(string path)
        {
            var point = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const r = document.querySelector('[data-new-project-dialog] [data-drop-pool]').getBoundingClientRect();
                return [r.left + r.width / 2, r.top + r.height / 2];
            })()"));
            foreach (string type in new[] { "dragEnter", "dragOver", "drop" })
            {
                await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchDragEvent", MediaTool.Json().Serialize(new
                {
                    type, x = point[0], y = point[1],
                    data = new { items = new object[0], files = new[] { path }, dragOperationsMask = 1 }
                }));
            }
        }
        async Task RightClick(string selector, string menu = "[data-cut-menu]", string horizontalAnchor = null)
        {
            var point = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const node = document.querySelector(" + MediaTool.Json().Serialize(selector) + @");
                const anchor = " + (horizontalAnchor == null ? "node" : "document.querySelector(" + MediaTool.Json().Serialize(horizontalAnchor) + ")") + @";
                const y = () => node.getBoundingClientRect().top + node.getBoundingClientRect().height / 2;
                if (y() < 5 || y() > innerHeight - 5) window.scrollBy(0, y() - innerHeight / 2);
                const r = anchor.getBoundingClientRect();
                return [r.left + r.width / 2, y()];
            })()"));
            await Mouse("mouseMoved", point[0], point[1], 0, "none", 0);
            await Mouse("mousePressed", point[0], point[1], 2, "right", 1);
            await Mouse("mouseReleased", point[0], point[1], 0, "right", 1);
            await WaitFor("document.querySelector(" + MediaTool.Json().Serialize(menu) + ").open");
        }

        async Task DragControl(string selector, string horizontalDelta, int verticalDelta)
        {
            await Task.Delay(100); // Let modal close, focus restoration and layout settle before native hit testing.
            var points = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const rect = document.querySelector(" + MediaTool.Json().Serialize(selector) + @").getBoundingClientRect();
                const x = rect.left + rect.width / 2, y = rect.top + rect.height / 2;
                return [x, y, x + " + horizontalDelta + ", y + " + verticalDelta + @"];
            })()"));
            await Mouse("mouseMoved", points[0], points[1], 0, "none", 0);
            await Mouse("mousePressed", points[0], points[1], 1, "left", 1);
            await Mouse("mouseMoved", points[2], points[3], 1, "none", 0);
            await Mouse("mouseReleased", points[2], points[3], 0, "left", 1);
        }

        async Task VerifyManualLink(string parent, string kind)
        {
            qaOpenedManual = null;
            string expected = Manuals.Resolve(home, kind);
            await Script("document.querySelector('" + parent + " [data-manual=" + kind + "]').click()");
            for (int attempt = 0; attempt < 100 && qaOpenedManual != expected; attempt++) await Task.Delay(20);
            Qa.Check(qaOpenedManual == expected, parent + " opens the bundled " + kind + " PDF");
        }

        async Task PressTimeEditorTab(bool reverse = false)
        {
            foreach (string type in new[] { "keyDown", "keyUp" })
                await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                    MediaTool.Json().Serialize(new { type = type, key = "Tab", code = "Tab", windowsVirtualKeyCode = 9,
                        modifiers = reverse ? 8 : 0 }));
        }

        async Task VerifyHistoryAndTimes()
        {
            string baseline = File.ReadAllText(projectFile);
            await Script(@"window.qaSetTime = (side,h,m,s,ms) => {
                const g=document.querySelector('[data-time-editor=' + side + ']');
                for (const [key,value] of Object.entries({hours:h,minutes:m,seconds:s,milliseconds:ms})) {
                    const input=g.querySelector('[data-time-part=' + key + ']'); input.value=value; input.dispatchEvent(new Event('input',{bubbles:true}));
                }
            };");
            await WaitFor("window.kirihariQa && document.querySelector('[data-save-status]').textContent === '保存済み'");
            await DragCut(24, 120);
            await Script("window.kirihariQa.record()");
            await WaitFor("document.querySelector('[data-save-status]').textContent === '未保存' && !document.querySelector('.kh-editor-heading [data-undo]').disabled");
            await Script("document.querySelector('.kh-editor-heading [data-undo]').click()");
            await WaitFor("document.querySelectorAll('[data-range]').length === 0 && document.querySelector('[data-save-status]').textContent === '保存済み'");
            await Script("document.querySelector('.kh-editor-heading [data-redo]').click()");
            await WaitFor("document.querySelectorAll('[data-range]').length === 1");
            await DoubleClickControl("[data-range] [data-edge='0']");
            await WaitFor("document.querySelector('[data-range-times-dialog]').open");
            Qa.Check(await Script("document.activeElement.closest('[data-time-editor]').dataset.timeEditor === 'start'") == "true", "Double-clicking the start handle opens the original-clock start editor");
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText", MediaTool.Json().Serialize(new { text = "1" }));
            Qa.Check(await Script("document.activeElement.value === '1'") == "true", "Direct typing replaces the selected clock component");
            Qa.Check(await Script("document.querySelector('#kh-range-times-keys').offsetHeight > 0 && " +
                "document.querySelector('#kh-range-times-keys').textContent.includes('Shift＋Tab')") == "true",
                "The time editor shows both Tab directions");
            const string focusedTimePart = "document.activeElement.closest('[data-time-editor]')?.dataset.timeEditor + ':' + document.activeElement.dataset.timePart";
            foreach (string expected in new[] { "start:minutes", "start:seconds", "start:milliseconds",
                "end:hours", "end:minutes", "end:seconds", "end:milliseconds" })
            {
                await PressTimeEditorTab();
                Qa.Check(await Script(focusedTimePart) == "\"" + expected + "\"", "Tab reaches " + expected);
            }
            await PressTimeEditorTab(true);
            Qa.Check(await Script(focusedTimePart) == "\"end:seconds\"", "Shift+Tab returns to the previous clock field");
            await Script("document.querySelector('[data-range-times-cancel]').click()");
            await DoubleClickControl("[data-range] [data-edge='1']");
            await WaitFor("document.querySelector('[data-range-times-dialog]').open");
            Qa.Check(await Script("document.activeElement.closest('[data-time-editor]').dataset.timeEditor === 'end'") == "true", "Double-clicking the end handle focuses the end editor");
            await Script("document.querySelector('[data-range-times-cancel]').click()");
            await RightClick("[data-range] [data-clip]");
            await Script("document.querySelector('[data-menu-times]').click()");
            await WaitFor("document.querySelector('[data-range-times-dialog]').open");
            await Script("window.qaSetTime('start',0,0,3,1); window.qaSetTime('end',0,0,3,0); document.querySelector('[data-range-times-apply]').click()");
            Qa.Check(await Script("document.querySelector('[data-range-times-dialog]').open && document.querySelector('[data-range-times-error]').textContent.includes('1フレーム以上')") == "true", "Reversed times within the same frame are rejected");
            await Script("window.qaSetTime('start',0,0,3,0); window.qaSetTime('end',0,0,2,0)");
            Qa.Check(await Script("document.querySelector('[data-range-times-preview]').textContent.startsWith('00:00:02.000 ～ 00:00:03.000') && document.querySelector('[data-range-times-preview]').textContent.includes('入れ替えて適用')") == "true",
                "The time editor previews reversed input in chronological order");
            await Script("document.querySelector('[data-range-times-apply]').click(); window.kirihariQa.record()");
            Qa.Check(await Script("!document.querySelector('[data-range-times-dialog]').open && window.kirihariQa.snapshot().ranges[0].start === 48 && window.kirihariQa.snapshot().ranges[0].end === 72") == "true", "Reversed times apply in chronological order on the 24-fps timeline");
            await Script("document.querySelector('.kh-editor-heading [data-undo]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().ranges[0].start === 24 && window.kirihariQa.snapshot().ranges[0].end === 120") == "true", "Undo restores both numeric boundaries together");
            await Script("document.querySelector('.kh-editor-heading [data-redo]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().ranges[0].start === 48") == "true", "Redo reapplies numeric boundaries");
            await Script(@"document.querySelector('[data-range] [data-clip]').click();
                document.querySelector('[data-layer=""1""] [data-add-caption]').click(); window.kirihariQa.record();
                document.querySelector('[data-caption-wrap] [data-clip]').click();
                document.querySelector('[data-object-add=text]').click();
                const editor = document.querySelector('.kh-picture textarea');
                editor.value='履歴の確認'; editor.dispatchEvent(new Event('input',{bubbles:true})); editor.blur();
                document.querySelector('[data-slide-done]').click(); window.kirihariQa.record();");
            Qa.Check(await Script("window.kirihariQa.snapshot().captions[0].objects[0].text === '履歴の確認'") == "true", "Caption text enters history");
            await Script("document.querySelector('.kh-editor-heading [data-undo]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().captions[0].objects.length === 0") == "true", "Undo removes the text object without removing its slide");
            await Script("document.querySelector('.kh-editor-heading [data-redo]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().captions[0].objects[0].text === '履歴の確認'") == "true", "Redo restores caption contents");
            await VerifyTransitionTimes();
            await VerifyOutputOptions();
            await VerifyPauseCaption();
            await VerifyPauseMoves();
            await VerifyShapeTransforms();
            await VerifyObjectFormatting();
            await ReopenCurrentProject();
            Qa.Check(await Script("!!window.kirihariQa.snapshot().captions[0].pauseOnly && !!document.querySelector('[data-pause-only]')") == "true", "Pause-only caption reloads from the saved project");
            Qa.Check(await Script("window.kirihariQa.snapshot().captions[0].transition.enterSeconds === .05 && window.kirihariQa.snapshot().captions[0].transition.exitSeconds === .65") == "true", "Independent transition durations survive save and reload");
            SaveText(projectFile, baseline);
            await ReopenCurrentProject();
            await Script("window.kirihariQa.undoAll(); document.querySelector('[data-save-project]').click()");
            await WaitFor("document.querySelectorAll('[data-range]').length === 0 && document.querySelector('[data-save-status]').textContent === '保存済み'");
            await WaitFor("!document.querySelector('video').seeking");
        }

        async Task DoubleClickControl(string selector)
        {
            var point = MediaTool.Json().Deserialize<double[]>(await Script("(() => {const r=document.querySelector(" + MediaTool.Json().Serialize(selector) + ").getBoundingClientRect();return [r.left+r.width/2,r.top+r.height/2];})()"));
            for (int count = 1; count <= 2; count++)
            {
                await Mouse("mousePressed",point[0],point[1],1,"left",count);
                await Mouse("mouseReleased",point[0],point[1],0,"left",count);
            }
        }

        async Task VerifyPauseMoves()
        {
            await Script("window.kirihariQa.record(); window.qaPauseBefore=window.kirihariQa.snapshot().pauses[0].frame");
            await DoubleClickControl("[data-pause]");
            await WaitFor("document.querySelector('[data-range-times-dialog]').open");
            Qa.Check(await Script("document.querySelector('[data-range-times-dialog] strong').textContent.includes('静止') && document.querySelector('[data-range-end]').closest('fieldset').hidden") == "true", "Pause double click opens a single original-clock position editor");
            await Script("window.qaSetTime('start',0,0,4,0); document.querySelector('[data-range-times-apply]').click()");
            Qa.Check(await Script("document.querySelector('[data-range-times-dialog]').open && !!document.querySelector('[data-range-times-error]').textContent") == "true", "Pause position outside its cut is rejected");
            await Script("window.qaSetTime('start',0,0,2,750); document.querySelector('[data-range-times-apply]').click(); window.kirihariQa.record()");
            Qa.Check(await Script("(()=>{const s=window.kirihariQa.snapshot();return s.pauses[0].frame===66 && s.captions[0].start===66 && s.captions[0].end===66 && s.captions[0].pauseOnly;})()") == "true", "Pause clock edit moves the linked pause-only caption");
            await Script("document.querySelector('.kh-editor-heading [data-undo]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().pauses[0].frame===window.qaPauseBefore && window.kirihariQa.snapshot().captions[0].start===window.qaPauseBefore") == "true", "Undo restores pause position and linked caption together");
            await Script("document.querySelector('.kh-editor-heading [data-redo]').click()");
            await RightClick("[data-pause]");
            Qa.Check(await Script("document.querySelector('[data-menu-times]').textContent.includes('静止する位置を変更')") == "true", "Pause context menu offers a position edit separately from hold duration");
            await Script("document.querySelector('[data-menu-times]').click(); window.qaSetTime('start',0,0,2,500); document.querySelector('[data-range-times-apply]').click()");
            await DragControl("[data-pause]", "document.querySelector('.kh-lanes').getBoundingClientRect().width*6/4320", 0);
            await Script("window.kirihariQa.record()");
            Qa.Check(await Script("window.kirihariQa.snapshot().pauses[0].frame===66 && window.kirihariQa.snapshot().captions[0].start===66") == "true", "Native pointer drag moves the pause six source frames and preserves its caption: " + await Script("({s:window.kirihariQa.snapshot(),pointer:window.qaPointerLog.slice(-12),debug:window.kirihariQa.debug(),drag:window.kirihariQa.pauseDrag})"));
            await DragControl("[data-pause]", "-document.querySelector('.kh-lanes').getBoundingClientRect().width*30/4320", 0);
            Qa.Check(await Script("window.kirihariQa.snapshot().pauses[0].frame===48") == "true", "Pause dragging clamps to the start of its cut");
            await DragControl("[data-pause]", "document.querySelector('.kh-lanes').getBoundingClientRect().width*30/4320", 0);
            Qa.Check(await Script("window.kirihariQa.snapshot().pauses[0].frame===71") == "true", "Pause dragging clamps to the final included cut frame");
            await DoubleClickControl("[data-pause]");
            await Script("window.qaSetTime('start',0,0,2,500); document.querySelector('[data-range-times-apply]').click(); (()=>{const r=document.querySelector('.kh-lanes').getBoundingClientRect();document.querySelector('.kh-axis').dispatchEvent(new MouseEvent('click',{bubbles:true,clientX:r.left+r.width*49/4320}));})()");
            await AddPause("0.5");
            Qa.Check(await Script("window.kirihariQa.snapshot().pauses.length===2") == "true", "A separate pause is available for the overlap check");
            await DoubleClickControl("[data-pause]:last-child");
            await Script("window.qaSetTime('start',0,0,2,500); document.querySelector('[data-range-times-apply]').click()");
            Qa.Check(await Script("document.querySelector('[data-range-times-dialog]').open && !!document.querySelector('[data-range-times-error]').textContent") == "true", "Moving a second pause onto the existing pause is rejected");
            await Script("document.querySelector('[data-range-times-cancel]').click()");
            await RightClick("[data-pause]:last-child");
            await Script("document.querySelector('[data-menu-delete]').click(); document.querySelector('[data-save-project]').click()");
            await WaitFor("document.querySelector('[data-save-status]').textContent==='保存済み'");
            await ReopenCurrentProject();
            Qa.Check(await Script("window.kirihariQa.snapshot().pauses[0].frame===60 && window.kirihariQa.snapshot().captions[0].start===60") == "true", "Moved pause and pause-only caption survive native save/reload");
        }

        async Task VerifyShapeTransforms()
        {
            Qa.Check(Icon != null && Icon.Width == 256, "Editor uses the embedded supplied app icon");
            using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location))
            { Qa.Check(icon != null, "Windows executable contains an application icon resource"); using(var stream=File.Create(Path.Combine(qaFolder,"app-icon.ico"))) icon.Save(stream); }
            await Script(@"document.querySelector('[data-caption-wrap] [data-clip]').click();
                document.querySelector('.kh-object-hit').click(); document.querySelector('[data-object-delete]').click();
                window.qaShape = kind => {const s=document.querySelector('[data-shape]');s.value=kind;s.dispatchEvent(new Event('change'));};
                window.qaTransform = kind => {const s=document.querySelector('[data-object-transform]');s.value=kind;s.dispatchEvent(new Event('change'));};
                window.qaAngle = value => {const s=document.querySelector('[data-object-rotation]');s.value=value;s.dispatchEvent(new Event('change'));};
                window.qaShape('triangle');
                const fill=document.querySelector('[data-object-fill]');fill.checked=true;fill.dispatchEvent(new Event('change'));
                for(const selector of ['[data-object-color]','[data-object-fill-color]']) {const c=document.querySelector(selector);c.value='#00ffff';c.dispatchEvent(new Event('input'));}");
            await Task.Delay(150);
            var points = MediaTool.Json().Deserialize<double[]>(await Script(@"(()=>{const n=document.querySelector('.kh-slide-object'),p=document.querySelector('.kh-picture').getBoundingClientRect(),o=window.kirihariQa.snapshot().captions[0].objects[0],r=n.querySelector('.kh-object-rotate').getBoundingClientRect();
                const cx=p.left+(o.x+o.w/2)*p.width/100,cy=p.top+(o.y+o.h/2)*p.height/100,rad=cy-(r.top+r.height/2);return[r.left+r.width/2,r.top+r.height/2,cx+rad,cy];})()"));
            await Mouse("mouseMoved",points[0],points[1],0,"none",0);
            await Mouse("mousePressed",points[0],points[1],1,"left",1);
            await Mouse("mouseMoved",points[2],points[3],1,"none",0);
            await Mouse("mouseReleased",points[2],points[3],0,"left",1);
            Qa.Check(await Script("Math.abs(window.kirihariQa.snapshot().captions[0].objects[0].rotation-90)<.1") == "true", "Native rotation handle drag rotates around the shape center: "+await Script("({object:window.kirihariQa.snapshot().captions[0].objects[0],pointer:window.qaPointerLog.slice(-8)})"));
            await Script("window.qaAngle(0)");
            points=MediaTool.Json().Deserialize<double[]>(await Script(@"(()=>{const p=document.querySelector('.kh-picture').getBoundingClientRect(),o=window.kirihariQa.snapshot().captions[0].objects[0],r=document.querySelector('.kh-object-rotate').getBoundingClientRect(),cx=p.left+(o.x+o.w/2)*p.width/100,cy=p.top+(o.y+o.h/2)*p.height/100,d=cy-(r.top+r.height/2),a=17*Math.PI/180;return[r.left+r.width/2,r.top+r.height/2,cx+d*Math.sin(a),cy-d*Math.cos(a)];})()"));
            await Mouse("mouseMoved",points[0],points[1],0,"none",0);
            await Mouse("mousePressed",points[0],points[1],1,"left",1);
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",MediaTool.Json().Serialize(new{type="mouseMoved",x=points[2],y=points[3],buttons=1,button="none",modifiers=8}));
            await Mouse("mouseReleased",points[2],points[3],0,"left",1);
            Qa.Check(await Script("Math.abs(window.kirihariQa.snapshot().captions[0].objects[0].rotation-15)<.1") == "true", "Shift plus native rotation drag snaps seventeen degrees to fifteen");
            await Script("window.qaTransform('horizontal'); window.qaTransform('vertical'); window.qaAngle(30); window.kirihariQa.record(); window.qaTransform('right'); window.kirihariQa.record()");
            Qa.Check(await Script("window.kirihariQa.snapshot().captions[0].objects[0].rotation===120") == "true", "Rotation menu adds ninety degrees");
            await Script("document.querySelector('[data-slide-done]').click(); document.querySelector('.kh-editor-heading [data-undo]').click()");
            Qa.Check(await Script("(()=>{const o=window.kirihariQa.snapshot().captions[0].objects[0];return o.rotation===30 && o.flipH && o.flipV;})()") == "true", "Undo restores angle without losing both flip states");
            await Script("document.querySelector('.kh-editor-heading [data-redo]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().captions[0].objects[0].rotation===120") == "true", "Redo restores rotated geometry");
            await Script("document.querySelector('[data-caption-wrap] [data-clip]').click(); document.querySelector('.kh-object-hit').click(); window.qaAngle(30); window.qaShape('bubble'); window.qaAngle(35); window.qaTransform('horizontal')");
            await Script(@"window.qaShapeAnchor=()=>{const o=window.kirihariQa.snapshot().captions[0].objects[1],r=document.querySelector('.kh-picture').getBoundingClientRect(),a=o.rotation*Math.PI/180,w=o.w*r.width/100,h=o.h*r.height/100;return[(o.x+o.w/2)*r.width/100-w/2*Math.cos(a)+h/2*Math.sin(a),(o.y+o.h/2)*r.height/100-w/2*Math.sin(a)-h/2*Math.cos(a)];};window.qaAnchorBefore=window.qaShapeAnchor();window.qaWidthBefore=window.kirihariQa.snapshot().captions[0].objects[1].w;");
            await DragControl("[data-kind=bubble] .kh-object-resize","20",15);
            Qa.Check(await Script("(()=>{const p=window.qaShapeAnchor();return Math.hypot(p[0]-window.qaAnchorBefore[0],p[1]-window.qaAnchorBefore[1])<1 && window.kirihariQa.snapshot().captions[0].objects[1].w>window.qaWidthBefore;})()") == "true", "Resizing a rotated shape preserves the opposite corner");
            await Script("window.qaTailBefore=JSON.stringify(window.kirihariQa.snapshot().captions[0].objects[1].tail)");
            await DragControl("[data-kind=bubble] .kh-callout-tail", "30", 25);
            Qa.Check(await Script("JSON.stringify(window.kirihariQa.snapshot().captions[0].objects[1].tail)!==window.qaTailBefore") == "true", "Native tail drag changes the callout geometry");
            Qa.Check(await Script(@"(()=>{const o=window.kirihariQa.snapshot().captions[0].objects[1],node=document.querySelector('[data-kind=bubble]'),r=node.querySelector('.kh-callout-tail').getBoundingClientRect(),svg=node.querySelector('svg'),p=new DOMPoint(o.tail[0]*100/o.w,o.tail[1]*100/o.h).matrixTransform(svg.getScreenCTM());return Math.hypot(r.left+r.width/2-p.x,r.top+r.height/2-p.y)<1;})()") == "true", "Callout handle is centered on the SVG tail endpoint itself");
            Qa.Check(await Script(@"(()=>{const o=window.kirihariQa.snapshot().captions[0].objects[1],p=document.querySelector('.kh-picture').getBoundingClientRect(),r=document.querySelector('[data-kind=bubble] .kh-callout-tail').getBoundingClientRect(),a=o.rotation*Math.PI/180;
                const x=(o.tail[0]-o.w/2)*p.width/100*(o.flipH?-1:1),y=(o.tail[1]-o.h/2)*p.height/100*(o.flipV?-1:1);
                return Math.abs(r.left+r.width/2-(p.left+(o.x+o.w/2)*p.width/100+x*Math.cos(a)-y*Math.sin(a)))<1 && Math.abs(r.top+r.height/2-(p.top+(o.y+o.h/2)*p.height/100+x*Math.sin(a)+y*Math.cos(a)))<1;
            })()") == "true", "Callout handle center matches its actual rotated/flipped tip after native drag");
            using (var shot = File.Create(Path.Combine(qaFolder,"回転と反転.png"))) await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,shot);
            // Keep the export probe unobstructed by moving the callout away from the triangle.
            await DragControl("[data-kind=bubble] .kh-object-hit", "document.querySelector('.kh-picture').getBoundingClientRect().width*.4", 0);
            await Script("document.querySelector('[data-slide-done]').click(); document.querySelector('[data-save-project]').click()");
            await WaitFor("document.querySelector('[data-save-status]').textContent==='保存済み'");
            await ReopenCurrentProject();
            Qa.Check(await Script("(()=>{const o=window.kirihariQa.snapshot().captions[0].objects;return o[0].rotation===30 && o[0].flipH && o[0].flipV && o[1].rotation===35 && o[1].flipH;})()") == "true", "Rotation and flip geometry survives native save/reload");
            await VerifyThemeDoesNotChangeSlide();
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            string output = MediaTool.Json().Deserialize<string>(await Script("document.querySelector('[data-export-result]').textContent"));
            string frame=Path.Combine(qaFolder,"回転した図形の出力.png");
            await Task.Run(()=>MediaTool.Run(tool.Encoder,new[]{"-y","-ss","1.1","-i",output,"-frames:v","1",frame},lifetime.Token,null));
            using(var image=new Bitmap(frame))
            {
                double sx=0,sy=0; int count=0;
                for(int y=0;y<image.Height;y++) for(int x=0;x<image.Width;x++) {var c=image.GetPixel(x,y);if(c.R<70 && c.G>180 && c.B>180){sx+=x;sy+=y;count++;}}
                // Triangle centroid: (50,65.667) in its local viewBox; flip both axes then rotate 30 degrees.
                double dy=-(65.667-50)/100*.25*image.Height;
                double expectedX=.275*image.Width-dy*.5,expectedY=.385*image.Height+dy*Math.Cos(Math.PI/6);
                Qa.Check(count>200 && Math.Abs(sx/count-expectedX)<5 && Math.Abs(sy/count-expectedY)<5,"MP4 contains the rotated and flipped triangle at the expected pixel centroid");
            }
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
        }

        async Task<byte[]> RenderTestCaption()
        {
            await Script("window.qaRenderedCaption=null; void window.kirihariQa.overlay().then(png => {window.qaRenderedCaption=png;})");
            await WaitFor("typeof window.qaRenderedCaption === 'string'");
            return Convert.FromBase64String(MediaTool.Json().Deserialize<string>(await Script("window.qaRenderedCaption")));
        }

        static int[] OpaqueRuns(System.Collections.Generic.IEnumerable<bool> pixels)
        {
            var runs = new System.Collections.Generic.List<int>();
            int run = 0;
            foreach (bool pixel in pixels.Concat(new[] { false }))
            {
                if (pixel) run++;
                else if (run > 0) { runs.Add(run); run = 0; }
            }
            return runs.ToArray();
        }

        async Task VerifyObjectFormatting()
        {
            await Script(@"document.querySelector('[data-caption-wrap] [data-clip]').click();
                for (const object of [...document.querySelectorAll('.kh-object-hit')]) {
                    object.click(); document.querySelector('[data-object-delete]').click();
                }
                window.qaShape('bubble');
                window.qaSetFormat = (selector, value, event = 'change') => {
                    const input = document.querySelector('[data-object-' + selector + ']');
                    input.value = value; input.dispatchEvent(new Event(event, { bubbles: true }));
                };
                window.qaSetFormat('color', '#ffffff', 'input');
                window.qaSetFormat('stroke', 4);");
            int thin;
            using (var stream = new MemoryStream(await RenderTestCaption()))
            using (var png = new Bitmap(stream))
            {
                int x = (int)(png.Width * .27), y = (int)(png.Height * .26);
                thin = Enumerable.Range(y - 9, 18).Count(row => png.GetPixel(x, row).A > 180);
            }
            await Script("window.qaSetFormat('stroke',12)");
            int dotted = 0;
            foreach (string type in new[] { "solid", "dotted", "dashed", "double" })
            {
                await Script("window.qaSetFormat('stroke-type','" + type + "')");
                using (var stream = new MemoryStream(await RenderTestCaption()))
                using (var png = new Bitmap(stream))
                {
                    int x = (int)(png.Width * .27), y = (int)(png.Height * .26);
                    int[] across = OpaqueRuns(Enumerable.Range((int)(png.Width * .16), (int)(png.Width * .23))
                        .Select(col => png.GetPixel(col, y).A > 180));
                    int[] vertical = OpaqueRuns(Enumerable.Range(y - 9, 18).Select(row => png.GetPixel(x, row).A > 180));
                    if (type == "solid")
                        Qa.Check(across.Length == 1 && vertical.Sum() > thin + 5, "Solid stroke is continuous and the width control thickens it");
                    else if (type == "dotted")
                    {
                        Qa.Check(across.Length >= 3, "Dotted stroke has separate dots");
                        dotted = across.Max();
                    }
                    else if (type == "dashed")
                        Qa.Check(across.Length >= 2 && across.Max() > dotted * 2, "Long dashes are visibly longer than dots");
                    else
                        Qa.Check(vertical.Length == 2 && png.GetPixel(x, y).A < 20,
                            "Vector double stroke has two rails and a transparent gap: " + string.Join(",", vertical));
                }
            }
            await Script("window.qaSetFormat('transparency',50,'input'); window.kirihariQa.record()");
            using (var stream = new MemoryStream(await RenderTestCaption()))
            using (var png = new Bitmap(stream))
                Qa.Check(Enumerable.Range(0, png.Height).Select(y => png.GetPixel((int)(png.Width * .27), y).A).Max() == 128,
                    "Shape transparency composites the double stroke as a single object");
            string badge = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(SampleFolder, "images", "transparent-badge.png")));
            await Script(@"(() => {
                const picker = document.querySelector('[data-image-file]');
                const files = new DataTransfer();
                files.items.add(new File([Uint8Array.from(atob('" + badge + @"'), ch => ch.charCodeAt(0))], 'transparent-badge.png', {type:'image/png'}));
                picker.files = files.files; picker.dispatchEvent(new Event('change'));
            })()");
            await WaitFor("document.querySelector('[data-kind=image]')?.hasAttribute('data-selected')");
            Qa.Check(await Script("document.querySelector('[data-shape-format]').hidden && !document.querySelector('[data-transparency-format]').hidden") == "true",
                "Images show transparency without line controls");
            await Script("window.qaSetFormat('transparency',100,'input'); window.kirihariQa.record()");
            Qa.Check(await Script("document.querySelector('[data-kind=image] .kh-object-hit').style.opacity === '0' && document.querySelector('[data-kind=image] .kh-object-resize').offsetWidth > 0") == "true",
                "Fully transparent images remain editable");
            await Script("window.qaSetFormat('transparency',60,'input'); window.kirihariQa.record(); document.querySelector('[data-slide-done]').click(); document.querySelector('.kh-editor-heading [data-undo]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().captions[0].objects[1].transparency === 100") == "true", "Undo restores image transparency");
            await Script("document.querySelector('.kh-editor-heading [data-redo]').click(); document.querySelector('[data-save-project]').click()");
            await WaitFor("document.querySelector('[data-save-status]').textContent === '保存済み'");
            await ReopenCurrentProject();
            Qa.Check(await Script("(()=>{const [shape,image]=window.kirihariQa.snapshot().captions[0].objects; return shape.stroke===12 && shape.strokeType==='double' && shape.transparency===50 && image.transparency===60;})()") == "true",
                "Stroke style, stroke width and both transparencies survive save and reload");
            using (var stream = new MemoryStream(await RenderTestCaption()))
            using (var png = new Bitmap(stream))
                Qa.Check(Math.Abs(png.GetPixel((int)(png.Width * .325), (int)(png.Height * .48)).A - 80) <= 2,
                    "Image transparency multiplies the bundled PNG's original alpha");

            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            string output = MediaTool.Json().Deserialize<string>(await Script("document.querySelector('[data-export-result]').textContent"));
            string baseline = Path.Combine(qaFolder, "透過の元フレーム.png");
            string exported = Path.Combine(qaFolder, "線と透過の書き出し.png");
            await Task.Run(() => MediaTool.Run(tool.Encoder, new[] { "-y", "-ss", "2.5", "-i", source.path, "-frames:v", "1", baseline }, lifetime.Token, null));
            await Task.Run(() => MediaTool.Run(tool.Encoder, new[] { "-y", "-ss", "1.1", "-i", output, "-frames:v", "1", exported }, lifetime.Token, null));
            using (var original = new Bitmap(baseline))
            using (var video = new Bitmap(exported))
            {
                var before = original.GetPixel((int)(original.Width * .325), (int)(original.Height * .48));
                var after = video.GetPixel((int)(video.Width * .325), (int)(video.Height * .48));
                double opacity = 80.0 / 255;
                Qa.Check(Math.Abs(after.R - (before.R * (1 - opacity) + 115 * opacity)) < 20
                    && Math.Abs(after.G - (before.G * (1 - opacity) + 47 * opacity)) < 20
                    && Math.Abs(after.B - (before.B * (1 - opacity) + 154 * opacity)) < 20
                    && Math.Abs(after.G - before.G) > 24, "MP4 blends the partially transparent PNG with the underlying source");
            }
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
        }

        async Task VerifyTransitionTimes()
        {
            await Script(@"window.qaTransition = (a,b,kind='fade') => {
                document.querySelector('[data-caption-wrap] [data-clip]').click();
                if(document.querySelector('#kh-transition-settings').hidden) document.querySelector('[data-transition-toggle]').click();
                document.querySelector('[data-transition-kind]').value=kind;
                document.querySelector('[data-transition-seconds]').value=a;
                document.querySelector('[data-transition-exit-seconds]').value=b;
                document.querySelector('[data-transition-seconds]').dispatchEvent(new Event('change',{bubbles:true}));
            };
            window.qaTransition(.05,.65);");
            Qa.Check(await Script("document.querySelector('[data-transition-seconds]').offsetWidth>0 && document.querySelector('[data-transition-exit-seconds]').offsetWidth>0") == "true", "Both transition duration inputs are visible for fade");
            await Script("document.querySelector('[data-transition-step=\"in:1\"]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().captions[0].transition.enterSeconds === .15") == "true", "Duration plus button adjusts the entry animation");
            await Script("document.querySelector('[data-transition-step=\"in:-1\"]').click(); document.querySelector('[data-slide-done]').click(); (()=>{const z=document.querySelector('[data-zoom]');z.value=3;z.dispatchEvent(new Event('input'));})()");
            await Script(@"window.qaCaptionPreview = () => {
                const r=document.querySelector('.kh-lanes').getBoundingClientRect();
                document.querySelector('.kh-axis').dispatchEvent(new MouseEvent('click',{bubbles:true,clientX:r.left+r.width*52/4320}));
                return Number(document.querySelector('.kh-overlay').style.opacity);
            }; window.qaFastOpacity=window.qaCaptionPreview();");
            await Script("window.qaTransition(.65,.05); document.querySelector('[data-slide-done]').click(); window.qaSlowOpacity=window.qaCaptionPreview()");
            Qa.Check(await Script("window.qaFastOpacity>.9 && window.qaSlowOpacity<.5") == "true", "Fast and slow entry durations produce different preview opacities at the same source time");
            await Script("window.qaTransition(60,60)");
            Qa.Check(await Script("!document.querySelector('[data-transition-limit]').hidden && document.querySelector('[data-transition-limit]').textContent.includes('短縮')") == "true", "A short caption shows the proportionally shortened durations");
            using (var shot = File.Create(Path.Combine(qaFolder, "遷移時間.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("window.qaTransition(.05,.65,'none'); document.querySelector('[data-slide-done]').click(); document.querySelector('[data-fit]').click()");
        }

        async Task BeginExport()
        {
            await Script("document.querySelector('[data-export]').click()");
            await WaitFor("document.querySelector('[data-export-dialog]').open && !document.querySelector('[data-export-step]').disabled");
            await Script("document.querySelector('[data-export-step]').click()");
        }

        void TraceQaSave(string command, int id, string result)
        {
            if (qaFolder == null || (command != "save" && command != "draft")) return;
            File.AppendAllText(Path.Combine(qaFolder, "save-trace.txt"),
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + command + " #" + id + " " + result + Environment.NewLine);
        }

        async Task VerifyLongCaptionWidth()
        {
            const string span = "(() => { const c = window.kirihariQa.snapshot().captions[0]; return c.end - c.start; })()";
            await Script(@"window.kirihariQa.record();
                document.querySelector('[data-add-text]').click();
                document.querySelector('[data-slide-done]').click();
                window.kirihariQa.record();");
            int unzoomed = MediaTool.Json().Deserialize<int>(await Script(span));
            Qa.Check(await Script("document.querySelector('[data-caption-wrap]').getBoundingClientRect().width >= 31") == "true",
                "A new text caption on a six-hour timeline is wide enough to grab at 1x");
            await Script(@"(() => {
                const z = document.querySelector('[data-zoom]');
                z.value = z.max;
                z.dispatchEvent(new Event('input'));
            })()");
            Qa.Check(await Script(@"(() => {
                const overview = document.querySelector('[data-overview]');
                return document.querySelector('[data-magnification]').textContent === '×100' && !overview.hidden &&
                    Math.abs(parseFloat(overview.style.getPropertyValue('--visible-span')) - 1) < .02 &&
                    document.querySelectorAll('.kh-axis span').length < 100;
            })()") == "true", "A six-hour timeline reaches 100x with a 1% viewport and bounded ruler marks");
            using (var shot = File.Create(Path.Combine(qaFolder, "6時間のタイムライン100倍.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            Qa.Check(MediaTool.Json().Deserialize<int>(await Script(span)) == unzoomed,
                "Changing zoom leaves the first caption duration intact");
            await Script(@"document.querySelector('[data-layer=""2""] [data-add-caption]').click(); window.kirihariQa.record();");
            var zoomed = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const [first, second] = window.kirihariQa.snapshot().captions;
                const band = document.querySelector('[data-lane=caption][data-layer=""2""] [data-caption-wrap]');
                return [band.getBoundingClientRect().width, second.end - second.start, first.end - first.start];
            })()"));
            Qa.Check(zoomed[0] >= 31 && zoomed[1] < zoomed[2],
                "Inserting a slide at higher zoom creates a shorter caption with a usable width: " + string.Join(", ", zoomed));
            const string edge = "[data-lane=caption][data-layer='2'] [data-edge='1']";
            const string secondEnd = "window.kirihariQa.snapshot().captions[1].end";
            int priorEnd = MediaTool.Json().Deserialize<int>(await Script(secondEnd));
            await DragControl(edge, "12", 0);
            Qa.Check(MediaTool.Json().Deserialize<int>(await Script(secondEnd)) > priorEnd,
                "The right handle can be dragged to extend the new caption");
            await Script(@"(() => {
                window.kirihariQa.record();
                const undo = document.querySelector('.kh-editor-heading [data-undo]');
                undo.click(); undo.click(); undo.click();
                document.querySelector('[data-fit]').click();
            })()");
            await WaitFor("document.querySelectorAll('[data-range]').length === 1 && !document.querySelector('[data-caption-wrap]') && document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
        }

        async Task VerifyLongTimeEditor()
        {
            string path = Path.Combine(qaFolder,"6時間の時刻指定.mp4");
            await Task.Run(() => MediaTool.Run(tool.Encoder,new[] { "-y","-f","lavfi","-i","color=c=navy:s=64x36:r=10","-t","21600",
                "-an","-c:v","libx264","-preset","ultrafast","-crf","35",path },lifetime.Token,null));
            await Script("document.querySelector('[data-projects]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open");
            await Script("document.querySelector('[data-new-project]').click()");
            await WaitFor("document.querySelector('[data-new-project-dialog]').open");
            await DropFile(path);
            await WaitFor("document.querySelector('[data-new-project-dialog] [data-asset]')");
            await VerifyImportProxyChoices();
            Qa.Check(await Script("document.querySelector('[data-proxy-candidates]').textContent.includes('6時間0分')") == "true",
                "A long duration alone recommends a proxy for a small, low-bitrate video");
            Qa.Check(await Script(@"document.querySelector('[data-proxy-title]').textContent === '編集時のプレビュー画質を選ぶ' &&
                document.querySelector('[data-proxy-offer] strong').textContent === 'プロキシとは' &&
                ['倍速', '場面移動', '作成に時間', '完成動画には元の映像を使います'].every(text =>
                    document.querySelector('[data-proxy-offer]').textContent.includes(text))") == "true",
                "The offer explains what the lightweight copy does and what will be exported");
            await Script("document.querySelector('[data-proxy-import=ORIGINAL]').click()");
            await Script("document.querySelector('[data-new-project-dialog] [data-asset]').click(); document.querySelector('[data-start-project]').click()");
            await WaitFor("!document.querySelector('[data-new-project-dialog]').open && document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
            await SaveFromToolbar();
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s), video = q('[data-media]');
                const forward = q('[data-jump=""10""]'), short = q('[data-jump=""5""]');
                if (forward.getAttribute('aria-label') !== '3分36秒送る' || short.getAttribute('aria-label') !== '1分48秒送る') return false;
                q('[data-action=play]').click();
                window.qaHotkey('ArrowRight');
                const moved = Math.abs(video.currentTime - 216) < .15;
                q('[data-action=play]').click();
                q('[data-seek-handle]').dispatchEvent(new KeyboardEvent('keydown', {key:'Home', bubbles:true}));
                return moved;
            })()") == "true", "A long original uses proportional buttons and a 1 percent keyboard jump");
            await Script(@"(() => {const lane=document.querySelector('[data-lane=cut]'),r=lane.getBoundingClientRect();
                lane.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,clientX:r.left+2,clientY:r.top+r.height/2}));
                document.querySelector('[data-menu-keep]').click();})()");
            await WaitFor("document.querySelector('[data-range]')");
            await VerifyLongCaptionWidth();
            await RightClick("[data-range] [data-clip]");
            await Script("document.querySelector('[data-menu-times]').click()");
            await WaitFor("document.querySelector('[data-range-times-dialog]').open");
            await Script("window.qaSetTime('start',0,59,59,900); document.querySelector('[data-range-start] [data-time-step=\"seconds:1\"]').click()");
            Qa.Check(await Script("Number(document.querySelector('[data-range-start] [data-time-part=hours]').value)===1 && Number(document.querySelector('[data-range-start] [data-time-part=minutes]').value)===0 && Number(document.querySelector('[data-range-start] [data-time-part=seconds]').value)===0 && Number(document.querySelector('[data-range-start] [data-time-part=milliseconds]').value)===900") == "true", "Time step buttons carry seconds/minutes into hours without losing fractions");
            await Script("window.qaSetTime('start',0,0,0,0); document.querySelector('[data-range-start] [data-time-step=\"milliseconds:-1\"]').click()");
            Qa.Check(await Script("Number(document.querySelector('[data-range-start] [data-time-part=milliseconds]').value)===0") == "true", "Decrementing zero does not make negative timeline times");
            await Script("window.qaSetTime('start',1,60,15,400); window.qaSetTime('end',1,3,17,500); document.querySelector('[data-range-times-apply]').click()");
            Qa.Check(await Script("document.querySelector('[data-range-times-dialog]').open && document.querySelector('[data-range-times-error]').textContent.includes('0〜59')") == "true", "Invalid clock components do not change the cut");
            await Script("window.qaSetTime('start',1,3,15,400)");
            Qa.Check(await Script("document.querySelector('[data-range-times-preview]').textContent.includes('01:03:15.400')") == "true", "The original timeline time is shown as hours/minutes/seconds/milliseconds");
            using (var shot = File.Create(Path.Combine(qaFolder,"時刻指定.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,shot);
            await Script("document.querySelector('[data-range-times-apply]').click()");
            await WaitFor("!document.querySelector('[data-range-times-dialog]').open");
            Qa.Check(await Script("window.kirihariQa.snapshot().ranges[0].start===37954 && window.kirihariQa.snapshot().ranges[0].end===37975") == "true", "1h3m15.4s to 1h3m17.5s becomes the exact original 10-fps frame range");
            await RightClick("[data-range] [data-clip]");
            await Script("document.querySelector('[data-menu-zoom]').click()");
            await WaitFor(@"(() => {
                const view = document.querySelector('.kh-lane-view').getBoundingClientRect();
                const cut = document.querySelector('[data-range]').getBoundingClientRect();
                return cut.width / view.width > .84 && cut.width / view.width < .95 &&
                    cut.left >= view.left && cut.right <= view.right && document.querySelectorAll('.kh-axis span').length < 100;
            })()");
            using (var shot = File.Create(Path.Combine(qaFolder, "カット拡大.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('[data-fit]').click()");
            Qa.Check(await Script("document.querySelector('[data-magnification]').textContent === '×1' && document.querySelector('.kh-lane-view').scrollLeft === 0") == "true",
                "Full view restores the original overview after fitting a two-second cut on a six-hour source");
            await Script(@"document.querySelector('[data-cut-list-open]').click();
                const cutSearch = document.querySelector('[data-cut-list-filter]');
                cutSearch.value = '存在しないカット'; cutSearch.dispatchEvent(new Event('input'));");
            Qa.Check(await Script("!document.querySelector('.kh-cut-list-row') && document.querySelector('[data-cut-list-items]').textContent.includes('ありません')") == "true",
                "The cut list shows a clear empty result when filtering");
            await Script(@"cutSearch.value = '01:03:15.400'; cutSearch.dispatchEvent(new Event('input'));");
            Qa.Check(await Script(@"document.querySelectorAll('.kh-cut-list-row').length === 1 &&
                document.querySelector('.kh-cut-list-row').textContent.includes('2.1秒') &&
                document.querySelector('.kh-cut-list-row').textContent.includes('1倍')") == "true",
                "The tiny cut can be found by time and its length and speed are shown");
            foreach (string query in new[] { "１：０３ １倍", "1時間3分15秒", "63:15" })
            {
                await Script("cutSearch.value = " + MediaTool.Json().Serialize(query) + "; cutSearch.dispatchEvent(new Event('input'));");
                Qa.Check(await Script("document.querySelectorAll('.kh-cut-list-row').length === 1 && document.querySelector('[data-cut-list-count]').textContent.includes('1件')") == "true",
                    "Cut search normalizes time and speed: " + query);
            }
            using (var shot = File.Create(Path.Combine(qaFolder, "カット一覧.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('.kh-cut-list-row button').click()");
            await WaitFor("Number(document.querySelector('[data-seek-handle]').getAttribute('aria-valuenow')) === 37954");
            await Script(@"document.querySelector('[data-cut-list-open]').click();
                document.querySelector('.kh-cut-list-row button:last-child').click()");
            await WaitFor("document.querySelector('[data-range]').getBoundingClientRect().width / document.querySelector('.kh-lane-view').clientWidth > .84");
            await Script("document.querySelector('[data-fit]').click()");
            string previous = File.ReadAllText(projectFile);
            int conflicts = qaSaveConflicts;
            using (File.Open(projectFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await Script("document.querySelector('[data-save-edit]').click()");
                for (int attempt = 0; attempt < 80 && qaSaveConflicts == conflicts; attempt++)
                    await Task.Delay(10);
                Qa.Check(qaSaveConflicts > conflicts, "The manual save attempted to write while the file was locked");
                Qa.Check(File.ReadAllText(projectFile) == previous, "The existing long project stays intact while its file is in use");
            }
            await WaitFor("document.querySelector('[data-save-status]').textContent==='保存済み'");
            await Script("document.querySelector('.kh-editor-heading [data-undo]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().ranges[0].start!==37954") == "true", "Clock edit is one undo action");
            await Script("document.querySelector('.kh-editor-heading [data-redo]').click()");
            Qa.Check(await Script("window.kirihariQa.snapshot().ranges[0].start===37954") == "true", "Redo restores the entered original clock range");
            await Script(@"(() => {
                const video = document.querySelector('[data-media]');
                video.addEventListener('loadstart', () => {
                    window.qaPlayBeforeReady = video.readyState === 0;
                    document.querySelector('[data-action=restart]').click();
                }, {once:true});
            })()");
            await ReopenCurrentProject();
            Qa.Check(await Script("window.qaPlayBeforeReady") == "true", "Edited playback was requested while the media was still loading");
            await WaitFor("document.querySelector('[data-media]').currentTime > 3795.6 && !document.querySelector('[data-media]').paused");
            await VerifyPausePosition();
            await Script("document.querySelector('[data-range] [data-clip]').click()");
            await WaitFor("document.querySelector('[data-source]').textContent==='01:03:15'");
            Qa.Check(await Script("window.kirihariQa.snapshot().ranges[0].start===37954 && window.kirihariQa.snapshot().ranges[0].end===37975") == "true", "Hour-based editing survives save/reload and updates the original clock when the cut is selected");
            await Script("document.querySelector('[data-export]').click()");
            await WaitFor("document.querySelector('[data-export-audio]').disabled && document.querySelector('[data-export-audio-note]').textContent.includes('素材に音声がありません')");
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            string output = MediaTool.Json().Deserialize<string>(await Script("document.querySelector('[data-export-result]').textContent"));
            var video = await Task.Run(() => tool.Read(output,lifetime.Token));
            Qa.Check(video.frames==21 && Math.Abs(video.duration-2.1)<.02,"Hour-based original-clock range exports exactly the selected 2.1 seconds");
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
            await AddPause("1");
            await DoubleClickControl("[data-pause]");
            await Script("window.qaSetTime('start',1,3,16,100); document.querySelector('[data-range-times-apply]').click()");
            previous = File.ReadAllText(projectFile);
            conflicts = qaSaveConflicts;
            using (File.Open(projectFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await Script("document.querySelector('[data-save-project]').click()");
                await WaitFor(@"window.qaNotices.some(text => text.includes('保存先のファイルが使用中')) &&
                    document.querySelector('[data-save-status]').textContent === '未保存'");
                Qa.Check(qaSaveConflicts == conflicts + 4 && File.ReadAllText(projectFile) == previous,
                    "A persistent lock stops the retries and keeps the previous project");
                await Script("window.kirihariQa.draft().then(() => { window.qaLockedDraft = true; })");
                await WaitFor("window.qaLockedDraft === true");
                string draft = Path.Combine(work, "自動保存.kirihari");
                Qa.Check(File.Exists(draft) && File.ReadAllText(draft).Contains("\"frame\":37961"),
                    "The draft can preserve the edit even while manual saving is blocked");
            }
            await Script("document.querySelector('[data-save-project]').click()");
            await WaitFor("document.querySelector('[data-save-status]').textContent==='保存済み'");
            await ReopenCurrentProject();
            Qa.Check(await Script("window.kirihariQa.snapshot().pauses[0].frame===37961") == "true", "An hour-based pause position survives save/reload at the exact original timeline frame");
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            output=MediaTool.Json().Deserialize<string>(await Script("document.querySelector('[data-export-result]').textContent"));
            video=await Task.Run(()=>tool.Read(output,lifetime.Token));
            Qa.Check(video.frames==31 && Math.Abs(video.duration-3.1)<.02,"Hour-based pause position adds exactly one second to the exported cut");
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
        }

        async Task ReopenCurrentProject()
        {
            string name = Path.GetFileName(projectFile);
            await Script("document.querySelector('[data-projects]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open");
            await Script("[...document.querySelectorAll('[data-project-id]')].find(x=>x.dataset.projectId === " + MediaTool.Json().Serialize(name) + ").click()");
            await WaitFor("!document.querySelector('[data-project-dialog]').open && document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
        }

        async Task VerifyOutputOptions()
        {
            await Script("document.querySelector('[data-export]').click()");
            await WaitFor("!document.querySelector('[data-export-step]').disabled");
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s);
                return q('[data-export-title]').textContent === '完成動画を作る（MP4）' &&
                    q('[data-export-duration]').textContent.endsWith('秒') &&
                    q('[data-export-audio-state]').textContent === (q('[data-export-audio]').checked ? 'あり' : 'なし') &&
                    q('[data-export-output]').textContent.trim() !== '' &&
                    !q('[data-output-details]').open && q('[data-output-summary]').offsetHeight === 0;
            })()") == "true", "The MP4 screen shows duration, audio and destination while technical specs start collapsed: " +
                await Script(@"[...document.querySelectorAll('[data-export-title],[data-export-duration],[data-export-audio-state],[data-export-output],[data-output-summary]')]
                    .map(node => [node.textContent, node.offsetHeight])"));
            await Script(@"window.qaOutputMode = value => { const m=document.querySelector('[data-output-mode]'); m.value=value; m.dispatchEvent(new Event('change')); };
                window.qaOutputMode('original');");
            await WaitFor("!document.querySelector('[data-export-step]').disabled && document.querySelector('[data-output-estimate]').textContent.includes('大きく')");
            await Script("window.qaOutputMode('conference')");
            await WaitFor("!document.querySelector('[data-export-step]').disabled && document.querySelector('[data-output-summary]').textContent.includes('3 Mbps')");
            await Script(@"window.qaOutputMode('custom');
                document.querySelector('[data-output-width]').value=320;
                document.querySelector('[data-output-height]').value=180;
                document.querySelector('[data-output-fps]').value=15;
                document.querySelector('[data-output-mbps]').value=1;
                document.querySelector('[data-output-audio]').value=128;
                document.querySelector('[data-output-width]').dispatchEvent(new Event('input'));");
            await WaitFor("!document.querySelector('[data-export-step]').disabled && document.querySelector('[data-output-summary]').textContent.includes('320 × 180')");
            Qa.Check(await Script("document.querySelector('[data-output-feedback]').textContent.includes('予測容量')") == "true", "Estimated size is visible with the technical details collapsed");
            await Script("document.querySelector('[data-output-name]').value='QA 低容量'; document.querySelector('[data-output-save]').click()");
            await Script("document.querySelector('[data-output-width]').value=319; document.querySelector('[data-output-width]').dispatchEvent(new Event('input'))");
            await WaitFor("document.querySelector('[data-export-step]').disabled && document.querySelector('[data-output-feedback]').textContent.includes('偶数')");
            await Script("document.querySelector('[data-output-width]').value=640; document.querySelector('[data-output-mbps]').value=2; document.querySelector('[data-output-width]').dispatchEvent(new Event('input'))");
            await WaitFor("!document.querySelector('[data-export-step]').disabled");
            await Script(@"document.querySelector('[data-output-name]').value='QA 別設定'; document.querySelector('[data-output-save]').click();
                const s=document.querySelector('[data-output-saved]'); s.value='QA 低容量'; s.dispatchEvent(new Event('change'));");
            await WaitFor("!document.querySelector('[data-export-step]').disabled && document.querySelector('[data-output-width]').value === '320'");
            Qa.Check(await Script("JSON.parse(localStorage.getItem('kirihari-export-v1')).presets.length === 2 && document.querySelector('[data-output-mbps]').value === '1'") == "true", "Two named output presets persist and recall independent values");
            await Script("window.qaOutputMode('youtube')");
            await WaitFor("!document.querySelector('[data-export-step]').disabled");
            using (var shot = File.Create(Path.Combine(qaFolder, "書き出し設定.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('[data-export-dialog] [data-close]').click()");
        }

        async Task VerifyPauseCaption()
        {
            await Script(@"const axis = document.querySelector('.kh-axis'), r = document.querySelector('.kh-lanes').getBoundingClientRect();
                axis.dispatchEvent(new MouseEvent('click',{bubbles:true,clientX:r.left+r.width*60/4320}));");
            await AddPause("1.5");
            Qa.Check(await Script("document.querySelector('[data-total]').textContent") == "\"2.5秒\"",
                "The edited length includes the inserted pause");
            const string pauseColorsMatch = @"getComputedStyle(document.querySelector('[data-caption-wrap] [data-clip]')).backgroundColor ===
                getComputedStyle(document.querySelector('[data-pause]')).backgroundColor";
            Qa.Check(await Script(pauseColorsMatch) == "false", "A regular caption retains its usual color before docking");
            await Script("const z=document.querySelector('[data-zoom]'); z.value=4; z.dispatchEvent(new Event('input')); document.querySelector('[data-caption-wrap] [data-clip]').click()");
            await DragControl("[data-caption-wrap] [data-edge='0']", "document.querySelector('.kh-lanes').getBoundingClientRect().width * 15 / 4320", 0);
            await DragControl("[data-caption-wrap] [data-edge='1']", "-document.querySelector('.kh-lanes').getBoundingClientRect().width * 15 / 4320", 0);
            await Script("window.kirihariQa.record()");
            using (var shot = File.Create(Path.Combine(qaFolder, "ポーズ端調整.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            Qa.Check(await Script("(() => { const s=window.kirihariQa.snapshot(), c=s.captions[0], p=s.pauses[0]; return c.start===p.frame && c.end===p.frame && c.pauseOnly; })()") == "true", "Both caption edges stop at the same pause and create a pause-only interval: " + await Script("({captions:window.kirihariQa.snapshot().captions,pauses:window.kirihariQa.snapshot().pauses,errors:window.qaErrors})"));
            Qa.Check(await Script(pauseColorsMatch) == "true", "A caption fixed to a pause uses the same fill color as that pause");
            await Script("document.querySelector('[data-caption-wrap] [data-clip]').click()");
            Qa.Check(await Script("!document.querySelector('.kh-overlay').hidden") == "true", "Pause-only caption can be previewed and edited");
            Qa.Check(await Script(@"(() => {
                document.querySelector('[data-slide-done]').click();
                document.querySelector('[data-action=restart]').click();
                return document.querySelector('.kh-overlay').hidden;
            })()") == "true", "Pause-only preview is hidden before the hold");
            await WaitFor("document.querySelector('[data-media]').paused && !document.querySelector('.kh-overlay').hidden");
            await WaitFor("!document.querySelector('[data-media]').paused && document.querySelector('.kh-overlay').hidden");
            await Script("document.querySelector('[data-action=play]').click(); document.querySelector('[data-save-project]').click()");
            await WaitFor("document.querySelector('[data-save-status]').textContent === '保存済み'");
            Qa.Check(File.ReadAllText(projectFile).Contains("\"pauseOnly\":true"), "Pause-only timing is written to the project file");
            using (var shot = File.Create(Path.Combine(qaFolder, "ポーズ中テロップ.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
        }

        async Task VerifyRecoveryFlow()
        {
            await Script("document.querySelector('[data-mute]').click(); void window.kirihariQa.draft()");
            string draft = Path.Combine(work, "自動保存.kirihari");
            for (int attempt = 0; attempt < 100 && !File.Exists(draft); attempt++) await Task.Delay(30);
            Qa.Check(File.Exists(draft) && !Recoverable(work), "An active session is not offered as crash recovery");
            string id = "crash-qa-" + Project.Id();
            string crashed = Path.Combine(WorkRoot, id);
            Directory.CreateDirectory(crashed);
            File.Copy(draft, Path.Combine(crashed, "自動保存.kirihari"));
            File.WriteAllText(Path.Combine(crashed, RecoveryInfo), MediaTool.Json().Serialize(new { pid = int.MaxValue, started = 0L, clean = false }));
            Qa.Check(Recoverable(crashed), "An interrupted session with an exited owner is recoverable");
            await Script("void window.kirihariQa.recoveries()");
            await WaitFor("document.querySelector('[data-recovery-dialog]').open && document.querySelector('[data-recovery-id=" + id + "]')");
            await Script("document.querySelector('[data-recovery-id=" + id + "]').click(); document.querySelector('[data-recovery-open]').click()");
            await WaitFor("!document.querySelector('[data-recovery-dialog]').open && document.querySelector('[data-current-project]').textContent.startsWith('復元_')");
            await WaitFor("document.querySelector('[data-save-status]').textContent === '未保存'");
            Qa.Check(!Recoverable(crashed) && File.Exists(Path.Combine(work, "自動保存.kirihari")), "Restoring preserves a new crash-safe draft and suppresses the old prompt");
            Qa.Check(projectFile.StartsWith(ProjectsRoot + Path.DirectorySeparatorChar) && !File.Exists(projectFile), "Recovery uses a new project destination without overwriting the original");
            await Script("document.querySelector('[data-save-project]').click()");
            await WaitFor("document.querySelector('[data-save-status]').textContent === '保存済み'");
            Qa.Check(File.Exists(projectFile), "Recovered editing can be saved as a managed project");
        }

        async Task VerifyCompactLayout()
        {
            double normalWidth = Width, normalHeight = Height;
            var viewport = MediaTool.Json().Deserialize<int[]>(await Script("[innerWidth, innerHeight]"));
            await Script(@"(() => {
                const grip = document.querySelector('.kh-grip');
                const percent = () => parseInt(grip.getAttribute('aria-label').match(/\d+/)[0]);
                window.qaPreviewPercent = percent();
                window.qaSetPreviewPercent = target => {
                    for (let step = 0; step < 50 && Math.abs(target - percent()) >= 2; step++)
                        grip.dispatchEvent(new KeyboardEvent('keydown', { key: percent() > target ? 'ArrowUp' : 'ArrowDown' }));
                };
                window.qaSetPreviewPercent(48);
                window.qaEditorHeight = document.querySelector('.kh-editor').offsetHeight;
            })()");
            await DragControl(".kh-grip", "0", -80);
            await WaitFor("document.querySelector('.kh-editor').offsetHeight > window.qaEditorHeight + 35");
            await DragControl(".kh-grip", "0", 240);
            Qa.Check(await Script(@"(() => {
                const track = document.querySelector('.kh-track-scroll');
                return document.documentElement.scrollHeight <= innerHeight &&
                    track.scrollHeight <= track.clientHeight + 1 &&
                    document.querySelector('[data-lane=caption][data-layer=""3""]').getBoundingClientRect().bottom <= innerHeight &&
                    !document.querySelector('.kh-edit-selection, footer[data-app-status]');
            })()") == "true", "Dragging down stops before the last caption row goes outside the viewport");
            await Script("window.qaSetPreviewPercent(window.qaPreviewPercent)");
            bool touch = await Script("matchMedia('(pointer: coarse)').matches") == "true";
            Width = 600;
            Height = touch ? 730 : 600;
            UpdateLayout();
            await WaitFor("innerWidth <= 610 && innerHeight <= " + (touch ? 740 : 610) + " && !document.querySelector('[data-info-details]').open");
            await Script("document.querySelector('[data-guide-open]').click()");
            await WaitFor("document.querySelector('[data-guide]').open && document.querySelectorAll('[data-guide] li').length === 5");
            await VerifyManualLink("[data-guide]", "quickstart");
            await VerifyManualLink("[data-guide]", "detail");
            await Script("document.querySelector('[data-guide-close]').click()");
            await WaitFor("!document.querySelector('[data-guide]').open");
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s);
                const split = q('[data-split]').getBoundingClientRect();
                const pause = q('[data-add-pause]').getBoundingClientRect();
                const exclude = q('.kh-cut-actions [data-exclude-mode]').getBoundingClientRect();
                return split.right < pause.left && Math.abs(split.top - pause.top) < 1 && q('[data-add-pause] svg') &&
                    pause.right < exclude.left && Math.abs(pause.top - exclude.top) < 1 &&
                    !q('[data-layer-up],[data-layer-down]') && document.querySelectorAll('.kh-caption-track').length === 3 &&
                    q('[data-source]').offsetWidth > 0 && q('[data-cut]').offsetWidth > 0 &&
                    document.documentElement.scrollWidth <= innerWidth;
            })()") == "true", "The compact layout shows both clocks, three caption rows and side-by-side cut actions without horizontal overflow");
            await Script(@"(() => {
                const q = s => document.querySelector(s);
                q('[data-info-details] summary').click();
                window.qaPreviousRow = q('[data-row]').value;
                window.qaPreviousFont = q('[data-font]').value;
                window.qaSetOption = (selector, value) => {
                    q(selector).value = value;
                    q(selector).dispatchEvent(new Event('change'));
                };
                window.qaSetOption('[data-font]', '20');
            })()");
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s);
                return q('[data-info-details]').open && q('.kh-info [data-pick-media]').offsetWidth > 0 &&
                    q('[data-adjustment-toggle=zoom]').offsetWidth >= 40 && q('[data-adjustment-toggle=speed]').offsetWidth >= 40 &&
                    !q('[data-zoom]').offsetWidth && !q('[data-speed]').offsetWidth &&
                    document.documentElement.scrollWidth <= innerWidth;
            })()") == "true", "Files and media open at compact width, with two adjustment buttons at the largest font");
            await Script("document.querySelector('[data-info-details]').open = false");
            await VerifyCompactAdjustments();
            await Script(@"(() => {
                const q = s => document.querySelector(s);
                window.qaSetOption('[data-row]', '72');
                q('.kh-track-scroll').scrollTop = 0;
            })()");
            // Resizing the WebView and closing the media details can settle on the next frame.
            await WaitFor(@"(() => {
                const q = s => document.querySelector(s);
                const hint = q('[data-cut-hint]').getBoundingClientRect(), track = q('.kh-track-scroll').getBoundingClientRect();
                return q('.kh-preview').offsetHeight >= 110 &&
                    hint.height > 0 && hint.top >= track.top && hint.bottom <= track.bottom &&
                    [...document.querySelectorAll('[data-lane=caption]')].every(row =>
                        row.offsetHeight <= (matchMedia('(pointer: coarse)').matches ? 46 : 32));
            })()");
            using (var shot = File.Create(Path.Combine(qaFolder, "コンパクト表示.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('[data-lane=cut]').scrollIntoView({block:'center'})");
            await DragCut(32, 400);
            await Script(@"document.querySelector('[data-layer=""1""] [data-add-caption]').click(); window.kirihariQa.record()");
            await WaitFor("document.querySelector('[data-lane=caption]').offsetHeight >= 72");
            Qa.Check(await Script(@"(() => {
                const track = document.querySelector('.kh-track-scroll');
                const caption = document.querySelector('[data-lane=caption]').getBoundingClientRect(), view = track.getBoundingClientRect();
                return caption.top >= view.top - 1 && caption.bottom <= view.bottom + 1;
            })()") == "true", "An added row expands fully into view");
            using (var shot = File.Create(Path.Combine(qaFolder, "テロップ行の展開.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            Qa.Check(await Script(@"(() => {
                const track = document.querySelector('.kh-track-scroll');
                track.scrollTop = track.scrollHeight;
                return document.documentElement.scrollHeight <= innerHeight &&
                    document.querySelector('[data-lane=caption][data-layer=""3""]').getBoundingClientRect().bottom <= innerHeight;
            })()") == "true", "Other caption rows remain reachable by timeline scrolling");
            await Script("window.kirihariQa.undoAll()");
            await WaitFor("document.querySelector('[data-lane=caption]').offsetHeight <= " + (touch ? 46 : 32) +
                " && !document.querySelectorAll('[data-range]').length");
            await Script("window.qaSetOption('[data-row]', window.qaPreviousRow); window.qaSetOption('[data-font]', window.qaPreviousFont);");
            Width = normalWidth;
            Height = normalHeight;
            UpdateLayout();
            await WaitFor("Math.abs(innerWidth - " + viewport[0] + ") <= 3 && Math.abs(innerHeight - " + viewport[1] + ") <= 3");
            if (viewport[0] > 800)
                await WaitFor("document.querySelector('[data-speed]').offsetWidth > 0 && document.querySelector('[data-zoom]').offsetWidth > 0 && !document.querySelector('[data-adjustment-toggle=zoom]').offsetWidth");
            await Script("window.scrollTo(0, 0)");
        }

        async Task VerifyCompactAdjustments()
        {
            const string speed = "#kh-speed-panel:popover-open";
            await WaitFor(@"(() => {
                const r = document.querySelector('[data-adjustment-toggle=speed]').getBoundingClientRect();
                return document.elementFromPoint(r.left+r.width/2,r.top+r.height/2)?.closest('[data-adjustment-toggle=speed]');
            })()");
            var trigger = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const r = document.querySelector('[data-adjustment-toggle=speed]').getBoundingClientRect();
                return [r.left+r.width/2,r.top+r.height/2];
            })()"));
            await Mouse("mouseMoved", trigger[0], trigger[1], 0, "none", 0);
            await WaitFor("document.querySelector('" + speed + "')");
            var slider = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const r = document.querySelector('[data-speed]').getBoundingClientRect();
                return [r.left+r.width/2,r.top+r.height/2];
            })()"));
            await Mouse("mouseMoved", slider[0], slider[1], 0, "none", 0);
            await Task.Delay(240);
            Qa.Check(await Script(@"(() => {
                const panel = document.querySelector('#kh-speed-panel:popover-open');
                if (!panel) return false;
                const bounds = panel.getBoundingClientRect(), slider = panel.querySelector('[data-speed]');
                const previous = slider.value;
                slider.value = slider.max;
                slider.dispatchEvent(new Event('input'));
                const label = document.querySelector('[data-adjustment-toggle=speed] span').textContent;
                slider.value = previous;
                slider.dispatchEvent(new Event('input'));
                return label === '×32' && slider.offsetWidth >= 32 &&
                    bounds.left >= 0 && bounds.right <= innerWidth && bounds.top >= 0 && bounds.bottom <= innerHeight;
            })()") == "true", "The hover popup stays open while the pointer moves to its usable slider and the button mirrors the speed");
            using (var shot = File.Create(Path.Combine(qaFolder, "速度のポップアップ.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Mouse("mouseMoved", 4, 4, 0, "none", 0);
            await WaitFor("!document.querySelector('" + speed + "')");
            Qa.Check(await Script(@"(() => {
                const q = selector => document.querySelector(selector);
                const toggle = q('[data-adjustment-toggle=zoom]');
                toggle.focus(); toggle.click();
                const panel = q('#kh-zoom-panel:popover-open');
                if (!panel || document.activeElement !== q('[data-zoom]')) return false;
                q('[data-zoom]').value = q('[data-zoom]').max;
                q('[data-zoom]').dispatchEvent(new Event('input'));
                const label = toggle.firstElementChild.textContent;
                q('[data-fit]').click();
                document.activeElement.dispatchEvent(new KeyboardEvent('keydown', {key:'Escape', bubbles:true}));
                return label === '×100' && toggle.firstElementChild.textContent === '×1' &&
                    !q('#kh-zoom-panel:popover-open') && document.activeElement === toggle;
            })()") == "true", "Keyboard activation focuses zoom, the full-view control resets the label, and Escape restores focus");
            await Script("document.querySelector('.kh-settings summary').click()");
            await WaitFor(@"(() => {
                const menu = document.querySelector('.kh-settings-menu');
                const bounds = menu.getBoundingClientRect();
                return bounds.width > 0 && bounds.left >= 0 && bounds.right <= innerWidth && bounds.bottom <= innerHeight &&
                    [...menu.querySelectorAll('h2')].map(h => h.textContent).join('|') === 'プロジェクト|表示・操作|素材・フォルダ|セットアップ|練習・ガイド';
            })()");
            using (var shot = File.Create(Path.Combine(qaFolder, "設定の分類.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('.kh-settings summary').click()");
        }

        async Task VerifyCaptionMenu(string first)
        {
            const string menu = "[data-caption-menu]";
            const string pastedLane = "[data-lane=caption][data-layer='3']";
            const string pasted = pastedLane + " [data-caption-wrap] [data-clip]";
            await RightClick(first, menu);
            await Script("document.querySelector('[data-caption-action=visibility]').click()");
            await WaitFor("document.querySelector(" + MediaTool.Json().Serialize(first) + ").closest('[data-caption-hidden]')");
            await RightClick(first, menu);
            Qa.Check(await Script("document.querySelector('[data-caption-action=visibility]').textContent") == "\"表示\"",
                "Caption context menu reflects its visibility");
            await Script("document.querySelector('[data-caption-action=visibility]').click()");
            await RightClick(first, menu);
            await Script("document.querySelector('[data-caption-action=copy]').click()");
            await RightClick(pastedLane, menu, first);
            Qa.Check(await Script(@"[...document.querySelectorAll('[data-caption-action]')].filter(button => !button.hidden)
                .map(button => button.dataset.captionAction).join(',')") == "\"paste\"", "An empty caption lane offers Paste");
            await Script("document.querySelector('[data-caption-action=paste]').click()");
            await WaitFor("document.querySelector(" + MediaTool.Json().Serialize(pasted) + ")");
            Qa.Check(await Script(@"(() => {
                const button = document.querySelector(" + MediaTool.Json().Serialize(pasted) + @");
                return !!document.querySelector('[data-overlay=' + CSS.escape(button.dataset.clip) + '] .kh-slide-object');
            })()") == "true", "Pasting into a different lane retains the copied slide objects");
            await RightClick(pasted, menu);
            await Script("document.querySelector('[data-caption-action=cut]').click()");
            await WaitFor("!document.querySelector(" + MediaTool.Json().Serialize(pasted) + ")");
            await RightClick(pastedLane, menu, first);
            await Script("document.querySelector('[data-caption-action=paste]').click()");
            await RightClick(pasted, menu);
            await Script("document.querySelector('[data-caption-action=delete]').click()");
            await WaitFor("document.querySelector('[data-caption-delete-dialog]').open");
            await Script("document.querySelector('[data-caption-delete-cancel]').click()");
            Qa.Check(await Script("!!document.querySelector(" + MediaTool.Json().Serialize(pasted) + ")") == "true",
                "Canceling the delete confirmation keeps the caption");
            await RightClick(pasted, menu);
            await Script("document.querySelector('[data-caption-action=delete]').click(); document.querySelector('[data-caption-delete-apply]').click()");
            await WaitFor("!document.querySelector(" + MediaTool.Json().Serialize(pasted) + ")");
        }

        async Task AddPause(string seconds)
        {
            await Script("document.querySelector('[data-add-pause]').click()");
            await WaitFor("document.querySelector('[data-pause-dialog]').open");
            await Script("document.querySelector('[data-pause-seconds]').value = '" + seconds +
                "'; document.querySelector('[data-pause-apply]').click()");
            await WaitFor("!document.querySelector('[data-pause-dialog]').open");
        }

        async Task<string> VerifyMediaPool()
        {
            await Script("document.querySelector('[data-projects]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open");
            await Script("document.querySelector('[data-new-project]').click()");
            await WaitFor("document.querySelector('[data-new-project-dialog]').open");
            await Script("document.querySelector('[data-new-project-dialog] [data-pick-media]').click()");
            await WaitFor("document.querySelectorAll('[data-new-project-dialog] [data-asset]').length === 2");
            await DropFile(Path.Combine(Program.QaSamples, "insert-motion.mp4"));
            await WaitFor("document.querySelectorAll('[data-new-project-dialog] [data-asset]').length === 3");
            Qa.Check(await Script("document.querySelector('[data-start-project]').disabled") == "true", "Choose the main video before starting");
            using (var shot = File.Create(Path.Combine(qaFolder, "素材選択.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script(@"(() => {
                const dialog = document.querySelector('[data-new-project-dialog]');
                dialog.querySelector('[data-project-name]').value = 'QA 複数素材';
                [...dialog.querySelectorAll('[data-asset]')].find(b => b.textContent.includes('cat-motion')).click();
                dialog.querySelector('[data-start-project]').click();
            })()");
            await WaitFor("!document.querySelector('[data-new-project-dialog]').open && document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
            await WaitFor("document.querySelector('[data-source]').nextElementSibling.textContent === '/ 03:00'");
            await Script("document.querySelector('[data-save-portable]').click()");
            await WaitFor("document.querySelector('[data-archive-dialog]').open");
            Qa.Check(await Script(@"document.querySelector('[data-archive-items]').children.length === 3 &&
                [...document.querySelectorAll('[data-archive-items] small')].filter(item => item.textContent.includes('メディアプールのみ')).length === 2") == "true",
                "ZIP review includes and identifies videos that are registered but not placed on the timeline");
            await Script("document.querySelector('[data-archive-cancel]').click()");
            await SaveFromToolbar();
            string managed = projectFile;
            Qa.Check(File.Exists(managed) && Path.GetDirectoryName(managed) == ProjectsRoot, "New project is saved in the project catalog");
            Qa.Check(await Script(@"(() => {
                const q = s => document.querySelector(s);
                const zoom = q('[data-zoom]'), view = q('.kh-lane-view'), lane = q('[data-lane=original]');
                const cue = q('[data-original-drop]'), asset = q('.kh-info [data-asset]'), dataTransfer = new DataTransfer();
                const setZoom = value => { zoom.value = value; zoom.dispatchEvent(new Event('input')); };
                const drag = (target, type, x = 0, relatedTarget = null) =>
                    target.dispatchEvent(new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer, clientX: x, relatedTarget }));
                setZoom(2);
                view.scrollLeft = view.clientWidth;
                const rect = view.getBoundingClientRect();
                const middle = rect.left + rect.width / 2;
                drag(asset, 'dragstart');
                drag(lane, 'dragover', middle - 5);
                const left = cue.querySelector('[data-side=front]'), right = cue.querySelector('[data-side=back]');
                const background = element => getComputedStyle(element).backgroundColor;
                const first = !cue.hidden && cue.dataset.originalDrop === 'front' && background(left) !== background(right);
                const highlight = background(left);
                const cueRect = cue.getBoundingClientRect();
                const aligned = view.scrollLeft > 0 && Math.abs(cueRect.left - rect.left) < 1 &&
                    Math.abs(cueRect.right - rect.right) < 1 && Math.abs(cueRect.top - lane.getBoundingClientRect().top) < 1;
                drag(lane, 'dragover', middle + 5);
                const last = cue.dataset.originalDrop === 'back' && background(right) === highlight;
                drag(lane, 'dragleave', middle + 5, q('[data-lane=audio]'));
                const leftLane = cue.hidden;
                drag(lane, 'dragover', middle + 5);
                drag(asset, 'dragend');
                const ended = cue.hidden;
                setZoom(0);
                return first && last && aligned && leftLane && ended;
            })()") == "true", "The original drop cue switches at the visible midpoint, aligns when zoomed and clears after leaving or canceling");
            await Script(@"window.qaDropMedia = (name, track, x) => {
                const item = [...document.querySelectorAll('.kh-info [data-asset]')].find(b => b.textContent.includes(name));
                const lane = document.querySelector('[data-lane=' + track + ']');
                const dataTransfer = new DataTransfer();
                item.dispatchEvent(new DragEvent('dragstart', { bubbles:true, dataTransfer }));
                lane.dispatchEvent(new DragEvent('dragover', { bubbles:true, cancelable:true, dataTransfer, clientX:x }));
                const preview = document.querySelector('[data-insert-preview]');
                window.qaDropPreview = { visible: !preview.hidden, width: preview.style.width, conflict: preview.hasAttribute('data-conflict') };
                lane.dispatchEvent(new DragEvent('drop', { bubbles:true, cancelable:true, dataTransfer, clientX:x }));
            }");
            await Script(@"(() => {
                const view = document.querySelector('.kh-lane-view').getBoundingClientRect();
                window.qaDropMedia('optical-motion', 'original', view.left + 10);
                window.qaDropMedia('insert-motion', 'original', view.right - 10);
            })()");
            await WaitFor("document.querySelector('[data-source]').nextElementSibling.textContent === '/ 03:09' && document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
            Qa.Check(await Script("[...document.querySelectorAll('.kh-original-clip')].map(b=>b.textContent).join(',')") ==
                "\"optical-motion.mp4,cat-motion.mp4,insert-motion.mp4\"", "Pool drops add the original before and after the main clip");
            Qa.Check(await Script("document.querySelector('[data-original-drop]').hidden") == "true", "The original drop cue disappears after placement");
            await VerifyRestart(false);
            await WaitFor("document.querySelector('[data-original-path]').textContent.includes('cat-motion') && document.querySelector('video').currentTime > .12");
            await Script("document.querySelector('[data-action=play]').click()");
            await Script("(() => { const z=document.querySelector('[data-zoom]'); z.value=2; z.dispatchEvent(new Event('input')); })()");
            await DragCut(52, 90, 4536);
            await DragCut(300, 345, 4536);
            await WaitFor("document.querySelectorAll('[data-range]').length === 2");
            await Script(@"window.qaDropAtFrame = (name, frame) => {
                const r = document.querySelector('.kh-lanes').getBoundingClientRect();
                window.qaDropMedia(name, 'cut', r.left + r.width * frame / 4536);
            }");
            await Script("window.qaDropAtFrame('insert-motion', 95)");
            await WaitFor("document.querySelectorAll('[data-range]').length === 3 && document.querySelector('[data-original-path]').textContent.includes('insert-motion')");
            await Script("window.qaDropAtFrame('insert-motion', 275)");
            Qa.Check(await Script(@"(() => {
                const items = document.querySelectorAll('[data-inserted]');
                return items.length === 2 && items[1].hasAttribute('data-conflict') &&
                    window.qaDropPreview.visible && window.qaDropPreview.conflict && window.qaDropPreview.width === items[1].style.width &&
                    Math.abs(parseFloat(items[1].style.width) * 45.36 - 168) < .01 &&
                    !items[1].querySelector('[data-edge]') && document.querySelector('[data-split]').disabled;
            })()") == "true", "An overlapping insert keeps its full width, turns red and has no timeline trim handles");
            await Script("document.querySelector('[data-export]').click()");
            Qa.Check(await Script("!document.querySelector('[data-export-dialog]').open && document.querySelector('[data-action=play]').disabled") == "true",
                "Overlapping cuts block cut playback and export");
            var insert = MediaTool.Json().Deserialize<double[]>(await Script(@"(() => {
                const band = document.querySelectorAll('[data-inserted]')[1].getBoundingClientRect();
                const lane = document.querySelector('.kh-lanes').getBoundingClientRect();
                return [band.left + band.width / 2, band.top + band.height / 2, lane.width * 105 / 4536];
            })()"));
            await Mouse("mouseMoved", insert[0], insert[1], 0, "none", 0);
            await Mouse("mousePressed", insert[0], insert[1], 1, "left", 1);
            await Mouse("mouseMoved", insert[0] + insert[2], insert[1], 1, "none", 0);
            await Mouse("mouseReleased", insert[0] + insert[2], insert[1], 0, "left", 1);
            Qa.Check(await Script(@"(() => {
                const item = document.querySelectorAll('[data-inserted]')[1];
                return !document.querySelector('[data-range][data-conflict]') &&
                    Math.abs(parseFloat(item.style.left) * 45.36 - 380) <= 2 &&
                    Math.abs(parseFloat(item.style.width) * 45.36 - 168) < .01;
            })()") == "true", "Moving a whole insert preserves duration and clears its overlap");
            await RightClick("[data-inserted]:last-of-type [data-clip]");
            await Script("document.querySelector('[data-menu-delete]').click()");
            await WaitFor("document.querySelectorAll('[data-inserted]').length === 1");
            await Script("window.qaInsertStart = document.querySelector('[data-inserted]').style.left");
            await RightClick("[data-inserted] [data-clip]");
            await Script("document.querySelector('[data-menu-edit]').click()");
            await WaitFor("document.querySelector('[data-insert-trim]').open && document.querySelector('[data-trim-preview]').readyState >= 2");
            await Script("window.qaTrimLeft = document.querySelector('[data-insert-trim]').getBoundingClientRect().left");
            await DragControl("[data-trim-title]", "110", 35);
            await WaitFor("document.querySelector('[data-insert-trim]').getBoundingClientRect().left > window.qaTrimLeft + 80");
            await DragControl("[data-trim-handle=front]", "document.querySelector('[data-trim-strip]').getBoundingClientRect().width / 7", 0);
            await WaitFor(@"document.querySelector('[data-trim-front]').valueAsNumber === 1 &&
                !document.querySelector('[data-trim-preview]').seeking && Math.abs(document.querySelector('[data-trim-preview]').currentTime - 1) < .05");
            await DragControl("[data-trim-handle=back]", "-document.querySelector('[data-trim-strip]').getBoundingClientRect().width / 7", 0);
            await WaitFor(@"document.querySelector('[data-trim-back]').valueAsNumber === 1 &&
                !document.querySelector('[data-trim-preview]').seeking && Math.abs(document.querySelector('[data-trim-preview]').currentTime - 143/24) < .05");
            Qa.Check(await Script("document.querySelector('[data-trim-length]').textContent") == "\"カット後 5秒\"",
                "Trimming both ends shows the resulting duration before applying");
            Qa.Check(await Script(@"(() => {
                const draft = document.querySelector('[data-insert-preview]');
                const band = document.querySelector('[data-inserted]');
                return !draft.hidden && !band.querySelector('[data-edge],[data-trim-handle]') &&
                    Math.abs(parseFloat(band.style.width)*45.36 - 168) < .01 &&
                    Math.abs(parseFloat(draft.style.width)*45.36 - 120) < .01;
            })()") == "true", "The timeline shows the draft length while its actual insert has no edge handles");
            using (var shot = File.Create(Path.Combine(qaFolder, "差し込みカット画面.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('[data-trim-apply]').click()");
            await WaitFor(@"!document.querySelector('[data-insert-trim]').open &&
                document.querySelector('.kh-picture').hasAttribute('data-media-ready') && !document.querySelector('[data-media]').seeking &&
                Math.abs(document.querySelector('[data-media]').currentTime - 1) < .05");
            Qa.Check(await Script(@"(() => {
                const band = document.querySelector('[data-inserted]');
                return Math.abs(parseFloat(band.style.width) * 45.36 - 120) < .01 &&
                    band.style.left === window.qaInsertStart &&
                    Math.abs(document.querySelector('[data-media]').currentTime - 1) < .05;
            })()") == "true", "Trim keeps the placement, shortens the band and seeks to the new source in-point");
            await AddPause("0.5");
            await RightClick("[data-pause]");
            await Script("document.querySelector('[data-menu-delete]').click()");
            await WaitFor("!document.querySelector('[data-pause]')");
            await AddPause("1.2");
            Qa.Check(await Script("document.querySelector('[data-pause]').textContent") == "\"1.2秒\"", "The pause circle shows the requested seconds");
            using (var shot = File.Create(Path.Combine(qaFolder, "差し込みとポーズ.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('[data-action=play]').click()");
            await WaitFor("document.querySelector('[data-action=play]').getAttribute('aria-label') === '一時停止' && document.querySelector('[data-media]').paused");
            await Task.Delay(300);
            Qa.Check(await Script("document.querySelector('[data-media]').paused && Math.abs(document.querySelector('[data-media]').currentTime - 1) < .05") == "true",
                "Playback holds the trimmed source frame");
            await WaitFor("!document.querySelector('[data-media]').paused && document.querySelector('[data-media]').currentTime > 1.1");
            await Script("document.querySelector('[data-action=play]').click()");
            Qa.Check(await Script(@"(() => {
                const q = selector => document.querySelector(selector);
                const snapshot = () => window.kirihariQa.snapshot();
                const before = snapshot();
                const seek = frame => {
                    const axis = q('.kh-axis'), r = axis.getBoundingClientRect();
                    axis.dispatchEvent(new MouseEvent('click', {bubbles:true, clientX:Math.round(r.left + r.width * frame / 4536)}));
                };
                q('[data-source-mode=original]').click();
                seek(230);
                q('[data-action=play]').click();
                window.qaHotkey('k');
                seek(44);
                window.qaHotkey('k');
                const after = snapshot();
                const fresh = after.ranges.filter(cut => !before.ranges.some(old => old.id === cut.id));
                window.qaMediaCapture = { before: before.ranges.map(cut => [cut.start, cut.end, cut.mediaOrigin]),
                    added: fresh.map(cut => [cut.start, cut.end, cut.mediaOrigin]) };
                if (fresh.map(cut => [cut.start, cut.end, cut.mediaOrigin]).join(';') !== '44,48,0;48,52,48;90,95,48;215,230,48' ||
                    fresh[0].mediaId !== before.originals[0] || fresh.slice(1).some(cut => cut.mediaId !== before.originals[1]) ||
                    JSON.stringify(after.ranges.find(cut => cut.inserted)) !== JSON.stringify(before.ranges.find(cut => cut.inserted)) ||
                    JSON.stringify(after.pauses) !== JSON.stringify(before.pauses) || q('[data-range][data-conflict]')) return false;
                q('.kh-editor-heading [data-undo]').click();
                return JSON.stringify(snapshot()) === JSON.stringify(before);
            })()") == "true", "One K capture crosses original media and a trimmed insert, adds only uncovered source spans, and undoes together: " +
                await Script("window.qaMediaCapture"));
            var expected = MediaTool.Json().Deserialize<int>(await Script(
                "[...document.querySelectorAll('[data-range]')].reduce((n,c)=>n+Math.round(parseFloat(c.style.width)*45.36),29)"));
            await Script("document.querySelector('[data-mute]').click(); document.querySelector('[data-save-project]').click();");
            await WaitFor("document.querySelector('[data-save-status]').textContent === '保存済み' && !window.kirihariQa.debug().busy");
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            string output = MediaTool.Json().Deserialize<string>(await Script("document.querySelector('[data-export-result]').textContent"));
            var video = await Task.Run(() => tool.Read(output, lifetime.Token));
            Qa.Check(video.frames == expected && video.audio && video.width == 640 && video.fps == 24,
                "Mixed resolutions, frame rates and a silent clip export together with audio");
            string frame = Path.Combine(qaFolder, "差し込み確認.png");
            await Task.Run(() => MediaTool.Run(tool.Encoder, new[] { "-y", "-ss", "3", "-i", output, "-frames:v", "1", frame }, lifetime.Token, null));
            using (var bitmap = new Bitmap(frame))
            {
                var pixel = bitmap.GetPixel(12, 110);
                Qa.Check(pixel.R < 55 && pixel.G < 55 && pixel.B > pixel.R, "The inserted rocket is present in the exported MP4");
            }
            await Script("document.querySelector('[data-work-zip]').click()");
            await ConfirmZipReview();
            await WaitFor("document.querySelector('[data-export-result]').textContent.includes('確認.zip')");
            using (var zip = ZipFile.OpenRead(Path.Combine(qaFolder, "確認.zip")))
                Qa.Check(new[] { "cat-motion.mp4", "optical-motion.mp4", "insert-motion.mp4" }
                    .All(file => zip.GetEntry("source/" + file) != null), "ZIP includes all three project videos");
            await Script("document.querySelector('[data-export-dialog]').close(); document.querySelector('[data-projects]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open");
            string selected = await Script(@"(() => {
                const id = " + MediaTool.Json().Serialize(Path.GetFileName(managed)) + @";
                const button = [...document.querySelectorAll('[data-project-id]')].find(item => item.dataset.projectId === id);
                if (!button) return false;
                button.click();
                return true;
            })()");
            Qa.Check(selected == "true", "The new project appears in the project chooser");
            await WaitFor("!document.querySelector('[data-project-dialog]').open && document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
            Qa.Check(await Script(@"document.querySelectorAll('.kh-original-clip').length === 3 &&
                document.querySelectorAll('[data-range]').length === 3 && document.querySelector('[data-pause]').textContent === '1.2秒' &&
                document.querySelector('[data-inserted] [data-clip]').textContent.startsWith('5秒')") == "true",
                "Selecting the saved project restores the pool, trimmed insert and pause");
            await Script("document.querySelector('[data-open-project]').click()");
            await WaitFor("document.querySelector('[data-original-path]').textContent.includes('imports') && document.querySelectorAll('.kh-info [data-asset]').length === 3");
            return output;
        }
        async Task VerifyWindow(bool loaded)
        {
            try
            {
                Qa.Check(loaded, "WebView2 navigated to the local editor");
                await WaitFor("document.querySelector('[data-project-dialog]').open");
                string titleBarPhase = Environment.GetEnvironmentVariable("KIRIHARI_QA_TITLE_BAR");
                if (titleBarPhase == "save" || titleBarPhase == "reopen")
                {
                    await VerifyTitleBarRestart(titleBarPhase);
                    Close();
                    return;
                }
                if (Environment.GetEnvironmentVariable("KIRIHARI_QA_PROXY_RESTART") == "1")
                {
                    await VerifyProxyRestart();
                    Close();
                    return;
                }
                if (Environment.GetEnvironmentVariable("KIRIHARI_QA_PROXY_ONLY") == "1")
                {
                    await VerifyProxyManager();
                    Close();
                    return;
                }
                Qa.Check(await Script(@"!document.querySelector('[data-startup-practice]').hidden &&
                    !document.querySelector('[data-project-menu]').open &&
                    document.querySelector('[data-new-project]').textContent === '動画を選んで編集' &&
                    document.querySelector('[data-project-dialog] [data-open-project]').textContent === '前の編集を開く'") == "true",
                    "On the first launch the chooser shows the practice shortcut beside the two main entry points");
                using (var shot = File.Create(Path.Combine(qaFolder, "初回起動画面.png")))
                    await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
                Qa.Check(await Script("document.querySelector('[data-edit-summary]').offsetHeight === 0") == "true",
                    "The edit summary does not appear until a source is open");
                await Script("document.querySelector('[data-project-menu] summary').click()");
                await VerifyManualLink("[data-project-dialog]", "quickstart");
                await VerifyManualLink("[data-project-dialog]", "detail");
                await VerifyThemes();
                await Script(@"(() => {
                    const status = document.querySelector('[data-app-status]');
                    window.qaNotices = [];
                    new MutationObserver(() => {
                        if (!status.textContent) return;
                        window.qaNotices.push(status.textContent);
                        if (window.qaNotices.length > 10) window.qaNotices.shift();
                    }).observe(status, {childList:true});
                })()");
                await VerifyInitialFolders();
                await VerifyBeginnerFlow();
                await Script("document.querySelector('[data-new-project]').click()");
                await WaitFor("document.querySelector('[data-new-project-dialog]').open");
                Qa.Check(await Script(@"(() => {
                    const name = document.querySelector('[data-project-name]');
                    const pick = document.querySelector('[data-new-project-dialog] [data-pick-media]');
                    return name.value === '' && name.placeholder === '新規プロジェクト' &&
                        name.closest('label').textContent.includes('任意') &&
                        pick.getBoundingClientRect().bottom < name.getBoundingClientRect().top;
                })()") == "true", "The chooser puts selecting a video before the optional project name");
                string folderDrop = Path.Combine(qaFolder, "フォルダ素材");
                Directory.CreateDirectory(Path.Combine(folderDrop, "子フォルダ"));
                File.Copy(Path.Combine(Program.QaSamples, "cat-motion.mp4"), Path.Combine(folderDrop, "cat-motion.MP4"));
                File.Copy(Path.Combine(Program.QaSamples, "optical-motion.mp4"), Path.Combine(folderDrop, "子フォルダ", "optical-motion.mp4"));
                File.WriteAllText(Path.Combine(folderDrop, "対象外.txt"), "not a video");
                File.Copy(Path.Combine(Program.QaSamples, "optical-motion.mp4"), Path.Combine(folderDrop, "対象外.mov"));
                await DropFile(folderDrop);
                await WaitFor("document.querySelectorAll('[data-new-project-dialog] [data-asset]').length === 2");
                Qa.Check(await Script("document.querySelector('[data-start-project]').disabled") == "true",
                    "Importing several videos together still requires choosing a main video");
                Qa.Check(await Script("[...document.querySelectorAll('[data-new-project-dialog] [data-asset]')].every(x=>/\\.mp4/i.test(x.textContent))") == "true", "A native directory drop imports MP4 including nested/uppercase files, excluding other file types");
                await DropFile(folderDrop);
                await Task.Delay(250);
                Qa.Check(assets.Count == 2, "Dropping the same directory again does not duplicate media");
                await Script("document.querySelector('[data-new-project-dialog] [data-asset]').click(); document.querySelector('[data-start-project]').click()");
                await WaitFor("document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
                Qa.Check(await Script("document.querySelector('[data-source]').nextElementSibling.textContent") == "\"/ 03:00\"",
                    "The cat sample opens at three minutes");
                Qa.Check(await Script("document.querySelectorAll('[data-range]').length") == "0", "New source starts with no cuts");
                Qa.Check(projectFile == null && await Script("document.querySelector('[data-save-status]').textContent") == "\"保存前\"",
                    "Starting a new project keeps it unsaved even after loading the video");
                Close();
                await WaitFor(@"document.querySelector('[data-close-edit-dialog]').open &&
                    document.querySelector('[data-close-edit-message]').textContent.includes('まだ保存されていません')");
                await Script("document.querySelector('[data-close-edit-cancel]').click()");
                await WaitFor("!document.querySelector('[data-close-edit-dialog]').open");
                Qa.Check(projectFile == null && !closeAllowed, "Canceling close leaves the new project open and unsaved");
                await SaveFromToolbar();
                await VerifyHistoryAndTimes();
                await VerifyCompactLayout();
                await VerifyManualLink(".kh-settings-menu", "quickstart");
                await VerifyManualLink(".kh-settings-menu", "detail");
                await VerifyZoomBounds();
                await VerifySeekHandle();
                await VerifyPlaybackHotkeys();
                await Script(@"document.querySelector('[data-jump=""10""]').click()");
                await VerifyRestart(false);
                await WaitFor("document.querySelector('video').currentTime > 1.0");
                await Script("document.querySelector('[data-action=play]').click()");
                await Script("const z = document.querySelector('[data-zoom]'); z.value = 3; z.dispatchEvent(new Event('input'));");
                await DragCut(2, 112);
                await WaitFor("document.querySelectorAll('[data-range]').length === 1 && document.querySelector('[data-range]').hasAttribute('data-selected')");
                await DragCut(216, 154);
                await WaitFor("document.querySelectorAll('[data-range]').length === 2 && document.querySelector('.kh-cut-draft').hidden");
                await DragCut(135, 80);
                await WaitFor("document.querySelectorAll('[data-range]').length === 3");
                Qa.Check(await Script(@"(() => {
                    const cuts = [...document.querySelectorAll('[data-range]')].sort((a,b) => parseFloat(a.style.left)-parseFloat(b.style.left));
                    const [left, gap] = cuts;
                    return Math.abs(parseFloat(left.style.left) + parseFloat(left.style.width) - parseFloat(gap.style.left)) < .00001;
                })()") == "true", "Dragging into a retained range adds only the gap");
                await RightClick("[data-range][data-selected] [data-clip]");
                Qa.Check(await Script("document.querySelector('[data-menu-edit]').hidden") == "true",
                    "The context menu for a regular cut offers deletion");
                await Script("document.querySelector('[data-menu-delete]').click()");
                await WaitFor("document.querySelectorAll('[data-range]').length === 2");
                await VerifyTrim(0, 12);
                await VerifyTrim(1, -8);
                var expectedFrames = MediaTool.Json().Deserialize<int>(await Script(
                    "[...document.querySelectorAll('[data-range]')].reduce((sum, r) => sum + Math.round(parseFloat(r.style.width) * 43.2), 0)"));
                const string firstCaption = "[data-lane=caption][data-layer='1'] [data-caption-wrap] [data-clip]";
                await Script(@"(() => {
                    const q = s => document.querySelector(s);
                    const click = s => q(s).click();
                    const seek = (button, frame) => {
                        const r = q('.kh-lanes').getBoundingClientRect();
                        button.dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 1, clientX: r.left+r.width*frame/4320 }));
                    };
                    const cuts = document.querySelectorAll('[data-range] [data-clip]');
                    seek(cuts[0], 12); click('[data-layer=""1""] [data-add-caption]');
                    seek(cuts[1], 204); click('[data-layer=""2""] [data-add-caption]');
                })()");
                await RightClick(firstCaption, "[data-caption-menu]");
                await Script("document.querySelector('[data-caption-action=edit]').click()");
                await WaitFor("!document.querySelector('[data-slide-toolbar]').hidden");
                await Script(@"(() => {
                    const q = s => document.querySelector(s);
                    const click = s => q(s).click();
                    q('[data-shape]').value = 'rect'; q('[data-shape]').dispatchEvent(new Event('change', { bubbles: true }));
                    q('[data-object-fill-color]').value = '#ff00ff';
                    q('[data-object-fill-color]').dispatchEvent(new Event('input', { bubbles: true }));
                    click('[data-transition-toggle]');
                    q('[data-transition-kind]').value = 'fade';
                    q('[data-transition-seconds]').value = '0.1';
                    q('[data-transition-kind]').dispatchEvent(new Event('change', { bubbles: true }));
                })()");
                await WaitFor("document.querySelector('[data-transition-kind]').value === 'fade'");
                Qa.Check(await Script(@"(() => {
                    const first = document.querySelector('[data-lane=caption][data-layer=""1""] [data-caption-wrap]');
                    const short = document.querySelector('[data-lane=caption][data-layer=""2""] [data-caption-wrap]');
                    const rightCut = document.querySelectorAll('[data-range]')[1];
                    return Math.round(parseFloat(first.style.width) * 43.2) === 72 &&
                        Math.abs(parseFloat(short.style.left) + parseFloat(short.style.width) -
                            parseFloat(rightCut.style.left) - parseFloat(rightCut.style.width)) < .00001;
                })()") == "true", "Captions start at three seconds and stop at the cut boundary");
                using (var shot = File.Create(Path.Combine(qaFolder, "編集画面.png")))
                    await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
                await VerifyCaptionMenu(firstCaption);
                await Script("document.querySelector('[data-slide-done]').click(); document.querySelector('[data-action=play]').click();");
                await WaitFor("document.querySelector('video').currentTime > 0.25");
                await WaitFor("document.querySelector('video').currentTime > 6.5");
                await VerifyRestart(true);
                await WaitFor(@"(() => {
                    const seek = document.querySelector('[data-seek-handle]');
                    const start = parseFloat(document.querySelector('[data-range]').style.left) * Number(seek.getAttribute('aria-valuemax')) / 100;
                    return Number(seek.getAttribute('aria-valuenow')) > start + 3;
                })()");
                await VerifyPausePosition();
                Close();
                await WaitFor("document.querySelector('[data-close-edit-dialog]').open");
                Qa.Check(await Script("document.activeElement.hasAttribute('data-close-edit-cancel')") == "true",
                    "The unsaved-changes dialog focuses Cancel");
                await Script("document.querySelector('[data-close-edit-cancel]').click()");
                await WaitFor("!document.querySelector('[data-close-edit-dialog]').open");
                await Script(@"(() => {
                    const stop = document.querySelector('[data-action=play]');
                    if (stop.getAttribute('aria-label') === '一時停止') stop.click();
                })()");
                await Script(@"document.querySelector('[data-export]').click();
                    document.querySelector('[data-export-audio]').click()");
                await WaitFor("document.querySelector('[data-export-audio-note]').textContent === '無音で書き出します。' && !document.querySelector('[data-export-step]').disabled");
                await Script("document.querySelector('[data-export-step]').click()");
                await WaitFor("!document.querySelector('[data-export-after]').hidden");
                string output = MediaTool.Json().Deserialize<string>(await Script("document.querySelector('[data-export-result]').textContent"));
                var rendered = await Task.Run(() => tool.Read(output, lifetime.Token));
                Qa.Check(rendered.frames == expectedFrames && !rendered.audio, "Window export matches the two retained cuts and unchecked output audio");
                await Script("document.querySelector('[data-export-open]').click()");
                for (int attempt = 0; attempt < 100 && qaOpenedExport != output; attempt++) await Task.Delay(20);
                Qa.Check(qaOpenedExport == output, "Open video targets the completed MP4");
                await Script("document.querySelector('[data-export-folder]').click()");
                for (int attempt = 0; attempt < 100 && qaOpenedExport != Path.GetDirectoryName(output); attempt++) await Task.Delay(20);
                Qa.Check(qaOpenedExport == Path.GetDirectoryName(output), "Open folder targets the completed output folder");
                string frame = Path.Combine(qaFolder, "完成フレーム.png");
                await Task.Run(() => MediaTool.Run(tool.Encoder, new[] { "-y", "-ss", "1", "-i", output, "-frames:v", "1", frame }, lifetime.Token, null));
                using (var image = new Bitmap(frame))
                {
                    var pixel = image.GetPixel(160, 135);
                    Qa.Check(pixel.R > 180 && pixel.B > 180 && pixel.G < 80, "WebView slide pixels baked into output");
                }
                await Script("document.querySelector('[data-export-details] summary').click(); document.querySelector('[data-work-zip]').click()");
                await ConfirmZipReview();
                await WaitFor("document.querySelector('[data-export-result]').textContent.includes('確認.zip')");
                using (var archive = ZipFile.OpenRead(Path.Combine(qaFolder, "確認.zip")))
                {
                    Qa.Check(archive.GetEntry("source/" + Path.GetFileName(source.path)) != null, "ZIP includes original video");
                    using (var data = archive.GetEntry("source/" + Path.GetFileName(source.path)).Open())
                    using (var original = File.OpenRead(Path.Combine(Program.QaSamples, "cat-motion.mp4")))
                    {
                        var first = new byte[original.Length];
                        var second = new byte[original.Length];
                        using (var a = new BinaryReader(data))
                        using (var b = new BinaryReader(original))
                        {
                            first = a.ReadBytes(first.Length);
                            second = b.ReadBytes(second.Length);
                        }
                        Qa.Check(first.SequenceEqual(second), "ZIP keeps byte-identical source");
                    }
                }
                await Script("document.querySelector('[data-work-delete]').click()");
                await WaitFor("document.querySelector('[data-export-result]').textContent.includes('削除しました')");
                await Script("document.querySelector('[data-export-dialog]').close(); document.querySelector('[data-open-project]').click();");
                await WaitFor("document.querySelector('[data-original-path]').textContent.includes('imports') && document.querySelector('.kh-picture').hasAttribute('data-media-ready')");
                await WaitFor("document.querySelectorAll('[data-range]').length === 2 && document.querySelectorAll('[data-caption-wrap]').length === 2");
                await Script("document.querySelector('[data-save-project]').click()");
                await WaitFor("document.querySelector('[data-save-status]').textContent === '保存済み' && !window.kirihariQa.debug().busy");
                Qa.Check(File.ReadAllText(Path.Combine(qaFolder, "確認.kirihari")).Contains("imports"), "The reopened archive saves an edit with its imported source path");
                string mixed = await VerifyMediaPool();
                var heavy = await VerifyHeavyProxy();
                await VerifyRecoveryFlow();
                await VerifyLongTimeEditor();
                await VerifyFolderChoices();
                await VerifyCutSpeeds();
                await VerifyCaptureAndBookmarks();
                await VerifySaveAndRelink();
                await VerifyProxyManager();
                File.WriteAllLines(Path.Combine(qaFolder, "window-results.txt"), new[] {
                    "PASS: four import choices, Original without encoding, H generation across editor startup; three proxy qualities; background seek; exact position, cuts and captions; cache switch; regeneration, locked replacement and cancellation; delete; reuse, invalidation and original-source export.",
                    "PASS: ZIP file list, unused assets, combined size, byte progress, independent ordinary save, switch prompt after ZIP save, successful reopen, failed extraction cleanup and in-progress cancellation.",
                    "PASS: same-length wrong replacement rejected; legacy preview, retry, cancel and confirmation; 2,000 cuts restored and the 2,001st split blocked; oversized file rejected; exclusion length and normalized cut search.",
                    "PASS: compact preview and initial cut hint visible together at the largest font, empty and occupied caption row heights with automatic expansion/scroll and undo, folded file paths, save status beside the button, edit summary, and active playback clock.",
                    "PASS: native exclusion drag in both directions, red draft, cancel, one-step undo/redo, linked captions and boundary pauses, exact MP4 length and audio; repeat direct text entry, outline glyph raster, new playback labels and standard export label.",
                    "PASS: practice shortcut, single-video auto-selection, first cut hint and direct caption typing, toolbar save, independent output audio including legacy mute and real AAC, silent export warning and result, collapsed maintenance and focused video action.",
                    "PASS: practice shortcut only on first startup, later access from chooser and editor menus, optional project name, preview labels, plain-language lightweight video offer, and simplified MP4 summary with collapsible technical specs.",
                    "PASS: four rendered stroke patterns, measured width, real double-stroke gap, editable fully transparent image, retained PNG alpha, undo/redo, project reload and MP4 alpha blend.",
                    "PASS: all five themes, native Windows 11 title-bar colors, manual palette across WebView reload, cancel rollback and byte-identical caption rasterization; three bundled sample videos and first project open; configured, project and last folder routes, Save As and MP4 destination.",
                    "PASS: pause double click and context-menu original-clock position edit; native drag within its cut; cut boundary and duplicate-position rejection; linked pause-only captions; Undo/Redo and native save/reload. Shape rotation handle, angle input, both flips, ninety-degree rotation, Undo/Redo, transformed callout tip pointer handle, save/reload and rotated/flipped MP4 pixels; embedded app icon and executable icon resource.",
                    "PASS: both cut-edge double clicks, Tab and Shift+Tab in the clock editor, carry/decrement buttons, reversed inputs reordered with same-frame rejection, undo/redo/save/reload, exact 1h3m15.4s to 1h3m17.5s MP4; independent fast/slow transition controls, preview and persistence.",
                    "PASS: long-project manual save retries a transient sharing violation, reports a sustained lock, preserves the previous save and the draft, and saves and exports after release.",
                    "PASS: long duration recommends a proxy; an edited playback request made during load starts at the retained range once ready.",
                    "PASS: blank project-name placeholder, native folder drop with nested/uppercase MP4 and deduplication, pause-edge drag and pause-only preview/save/reload, four export modes, invalid-size rejection, two persisted custom presets and estimated size.",
                    "PASS: bundled PDF menu links, cut/caption Undo/Redo, save-state indicator, numeric cut bounds, output open actions, and recovery UI with live-owner protection and a separate recovered project.",
                    "PASS: timeline seek handle scrubs in both directions while paused, keyboard stepping, ruler click, pause without moving the cursor in both modes, and close confirmation before the first save.",
                    "PASS: 16x and 32x previews, default arrows, proportional seeking on a long original, and saved custom shortcuts for retained cuts, splits, captions, and pauses.",
                    "PASS: nine preview speed choices, native 0.1x playback, and matching up/down steps. Per-cut numeric speeds are covered separately.",
                    "PASS: zoom-aware new caption widths on a six-hour source, a draggable end handle, and a shared color for a pause and its fixed caption.",
                    "PASS: edited length is emphasized in both preview modes and includes pauses.",
                    "PASS: right-click per-cut speed, native playback across differently timed cuts, save/reopen, split inheritance, and MP4 agreement.",
                    "PASS: growing K draft, uninterrupted original playback on confirm/cancel, bookmark notes and markers, manual save/reopen/jump and Undo, and viewport fit for a 2.1-second cut on a six-hour original.",
                    "PASS: K drafts retain the start across backwards and forwards jumps, native seek-handle drag, frame stepping, pause and resume. K and Escape work while the handle is focused.",
                    "PASS: reverse K range preview and exact confirmation while paused, full coverage across existing cuts and original media, retained inserts and annotations, grouped Undo and Redo, and save/reopen.",
                    "PASS: project switch with save, discard or cancel; missing media selection, cancellation and rejection; persistent relink and portable ZIP before MP4 export.",
                    "PASS: full-span drag, cross-band exclusion with edit impact and grouped undo, retained K draft in help/bookmarks, and six-hour cut lookup, jump and fit.",
                    "PASS: bounded divider, full timeline at maximum preview, compact in-panel scrolling with tall rows, quick guide, video information disclosure and large text.",
                    "PASS: native cat sample, zoom at start/middle/end, smooth edge drag, empty initial cuts, forward/reverse drag, gap boundary, trim playback tracking, three-second captions, original/cut restart, unsaved-changes confirmation and cancel, playback, muted MP4 and overlay pixels, portable ZIP round trip, project save, work-folder cleanup.",
                    "PASS: project chooser, native file drop, original front/back cue and placement at normal and zoomed scales, insert preview and overlap, insert trim, caption context actions, timed pause, mixed MP4 pixels/audio, ZIP and project restore.",
                    output, mixed
                }.Concat(heavy));
                Close();
                for (int attempt = 0; attempt < 100 && !closeAllowed; attempt++) await Task.Delay(100);
                Qa.Check(closeAllowed, "The saved project allows closing without a prompt");
            }
            catch (Exception error) { FailWindowTest(error); }
        }
        void FailWindowTest(Exception error)
        {
            File.WriteAllText(Path.Combine(qaFolder, "window-failed.txt"), error.ToString());
            closeAllowed = true;
            Application.Current.Shutdown(1);
        }
    }
}

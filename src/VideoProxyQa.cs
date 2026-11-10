using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Kirihari
{
    internal sealed partial class VideoWindow
    {
        async Task SelectProxyQuality(string level, bool create = false)
        {
            await Script("document.querySelector('[data-proxy-quick]').open = true");
            string option = "document.querySelector('[data-proxy-option=" + level + "]')";
            await WaitFor(option);
            await Script(option + ".click()");
            if (create)
            {
                await WaitFor("document.querySelector('[data-proxy-confirm]').open");
                await Script("document.querySelector('[data-proxy-confirm-apply]').click()");
            }
        }

        async Task VerifyProxyManager()
        {
            string input = Path.Combine(qaFolder, "proxy-60fps.mp4");
            await Task.Run(() => MediaTool.Run(tool.Encoder, new[] { "-y",
                "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=60:d=6",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:d=6",
                "-c:v", "libx264", "-threads:v", "2", "-preset", "ultrafast", "-c:a", "aac", input }, lifetime.Token, null));
            await Script("if (!document.querySelector('[data-project-dialog]').open) document.querySelector('[data-projects]').click()");
            await WaitFor("document.querySelector('[data-project-dialog]').open && !window.kirihariQa.debug().busy");
            await Script("document.querySelector('[data-new-project]').click()");
            await WaitFor("document.querySelector('[data-new-project-dialog]').open");
            await DropFile(input);
            await WaitFor("document.querySelector('[data-new-project-dialog] [data-asset]')");
            const string importPicker = "document.querySelector('[data-new-project-dialog] [data-proxy-for]').click()";
            await Script(importPicker);
            await VerifyImportProxyChoices();
            using (var shot = File.Create(Path.Combine(qaFolder, "読み込み時のプロキシ選択.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            double normalWidth = Width;
            string normalViewport = await Script("innerWidth");
            Width = MinWidth;
            await WaitFor("innerWidth < 560");
            await VerifyImportProxyChoices();
            using (var shot = File.Create(Path.Combine(qaFolder, "小さい画面でのプロキシ選択.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            Width = normalWidth;
            await WaitFor("innerWidth === " + normalViewport);
            await Script("document.querySelector('[data-proxy-import=ORIGINAL]').click()");
            Qa.Check(await Script("!window.kirihariQa.debug().converting && " +
                "document.querySelector('[data-new-project-dialog] [data-proxy-for]').dataset.proxyState === 'ORIGINAL'") == "true",
                "Original skips proxy encoding");
            await Script(importPicker);
            await WaitFor("document.querySelector('[data-proxy-dialog]').open");
            const string progressNote = "document.querySelector('[data-proxy-job-note]').textContent";
            Qa.Check(await Script(@"(() => {
                document.querySelector('[data-proxy-import=H] span').click();
                const panel = document.querySelector('[data-new-project-dialog] [data-proxy-job]');
                return !!panel && !panel.hidden && panel.querySelector('[data-proxy-job-title]').textContent.includes('プロキシ変換中') &&
                    " + progressNote + @".includes('動作が重くなる') && " + progressNote + @".includes('自動でプロキシに切り替わります');
            })()") == "true", "Import shows conversion progress, the original-source load and the automatic handoff");
            await Script("document.querySelector('[data-start-project]').click()");
            await WaitProxyQuality("H", 720, 0);
            await SelectProxyQuality("ORIGINAL");
            await WaitProxyQuality("ORIGINAL", 720, 0);
            await DragCut(60, 240, 360);
            await Script(@"document.querySelector('[data-add-text]').click();
                const editor = document.activeElement;
                editor.value = 'プロキシを切り替えても保持';
                editor.dispatchEvent(new Event('input', {bubbles:true}));
                document.querySelector('[data-slide-done]').click();
                document.querySelector('[data-proxy-manager]').open = true;");
            await SaveFromToolbar();
            string savedPath = projectFile;
            const string edit = "JSON.stringify(window.kirihariQa.snapshot())";
            string initial = await Script(edit);
            await SeekProxyQa(120);
            await WaitProxyQuality("ORIGINAL", 720, 2);

            await SelectProxyQuality("L", true);
            Qa.Check(await Script("window.kirihariQa.debug().converting && !window.kirihariQa.debug().busy") == "true",
                "Proxy generation leaves the editor available");
            Qa.Check(await Script(@"(() => {
                const panel = document.querySelector('[data-proxy-job-slot] [data-proxy-job]');
                return !!panel && !panel.hidden && panel.getBoundingClientRect().bottom <= document.querySelector('.kh-screen').getBoundingClientRect().top &&
                    panel.querySelector('progress').max === 100 && " + progressNote + @".includes('動作が重くなる') &&
                    " + progressNote + @".includes('自動でプロキシに切り替わります');
            })()") == "true", "The editor shows the progress bar and handoff above the preview during conversion");
            using (var shot = File.Create(Path.Combine(qaFolder, "編集中のプロキシ変換.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await SeekProxyQa(132);
            await WaitProxyQuality("L", 360, 2.2);
            Qa.Check(await Script("document.querySelector('[data-proxy-job]').hidden && " +
                "document.querySelector('[data-app-status]').textContent.includes('プレビューに適用しました')") == "true",
                "The panel clears and the editor confirms the proxy was applied");
            await SelectProxyQuality("M", true);
            await WaitProxyQuality("M", 480, 2.2);
            await SelectProxyQuality("H");
            await WaitProxyQuality("H", 720, 2.2);
            await Script("document.querySelector('[data-proxy-quick]').open = true");
            await WaitFor("document.querySelector('[data-proxy-option=H]').dataset.proxyActive === 'true'");
            using (var shot = File.Create(Path.Combine(qaFolder, "プロキシ画質と管理.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('[data-proxy-quick]').open = false");
            string old = source.proxies["M"].file;
            string l = source.proxies["L"].file;
            var lWrite = File.GetLastWriteTimeUtc(l);
            await SelectProxyQuality("L");
            await WaitProxyQuality("L", 360, 2.2);
            Qa.Check(File.GetLastWriteTimeUtc(l) == lWrite && source.proxies["L"].file == l && await Script(edit) == initial,
                "Switching to a cached quality preserves the frame and all edit data");
            await SelectProxyQuality("M");
            await WaitProxyQuality("M", 480, 2.2);
            await ChooseProxyAction("M", "build");
            await WaitFor("!window.kirihariQa.debug().converting && document.querySelector('[data-app-status]').textContent.includes('作成しました')");
            string refreshed = source.proxies["M"].file;
            Qa.Check(refreshed != old && File.Exists(refreshed), "Regeneration publishes a new validated generation");
            await WaitProxyQuality("M", 480, 2.2);

            string manifest = Path.Combine(Path.GetDirectoryName(refreshed), "current.json");
            using (File.Open(manifest, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await ChooseProxyAction("M", "build");
                await WaitFor("!window.kirihariQa.debug().converting && document.querySelector('[data-proxy-level=M]').textContent.includes('再生成失敗')");
            }
            Qa.Check(File.Exists(refreshed) && source.proxies["M"].file == refreshed, "Replacement failure preserves the working M copy");
            await ChooseProxyAction("M", "build");
            string cache = Path.GetDirectoryName(refreshed);
            bool started = false;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (Directory.GetFiles(cache, "*.tmp.mp4").Length != 0) { started = true; break; }
                await Task.Delay(15);
            }
            Qa.Check(started, "The replacement has started writing its own copy before cancellation");
            await Script("document.querySelector('[data-proxy-level=M] [data-proxy-cancel]').click()");
            await WaitFor("!window.kirihariQa.debug().converting && document.querySelector('[data-app-status]').textContent.includes('中止しました')");
            Qa.Check(File.Exists(refreshed) && source.proxies["M"].file == refreshed &&
                Directory.GetFiles(Path.GetDirectoryName(refreshed), "*.tmp.*").Length == 0, "Cancellation preserves M and removes staged files");
            await ChooseProxyAction("M", "delete");
            await WaitProxyQuality("ORIGINAL", 720, 2.2);
            Qa.Check(!File.Exists(refreshed) && File.Exists(l) && await Script(edit) == initial,
                "Deleting the active quality returns to the original and preserves the other levels and edits");
            await SelectProxyQuality("L");
            await WaitProxyQuality("L", 360, 2.2);
            await BeginExport();
            await WaitFor("!document.querySelector('[data-export-after]').hidden");
            var output = await Task.Run(() => tool.Read(lastExport, lifetime.Token));
            Qa.Check(output.height == 720 && output.width == 1280 && output.frames == 180 && output.fps == 60 && output.audio,
                "MP4 export uses the original 720p60 and audio with the 360p copy active");
            await Script("document.querySelector('[data-export-dialog]').close()");
            qaProjectToOpen = savedPath;
            const string open = "document.querySelector('.kh-settings [data-open-project]').click()";
            await Script(open);
            await WaitProxyQuality("L", 360);
            Qa.Check(source.proxies["L"].file == l && File.GetLastWriteTimeUtc(l) == lWrite,
                "Reopening the project reuses the cached generation and remembers its quality");
            using (var change = File.Open(input, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                change.Write(new byte[16], 0, 16);
            await Script(open);
            await WaitProxyQuality("ORIGINAL", 720);
            Qa.Check(source.proxies["L"].state == "stale" && source.proxies["H"].state == "stale",
                "Changing the original invalidates its old quality levels");
            await SelectProxyQuality("M", true);
            await WaitProxyQuality("M", 480);
            await Script("document.querySelector('[data-proxy-manager]').open = true; document.querySelector('[data-proxy-quick]').open = true");
            await WaitFor("document.querySelector('[data-proxy-total]').textContent.startsWith('キャッシュ全体 ')");
            using (var shot = File.Create(Path.Combine(qaFolder, "プロキシの無効化.png")))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
            await Script("document.querySelector('[data-proxy-manager]').open = false; document.querySelector('[data-proxy-quick]').open = false");
            File.WriteAllLines(Path.Combine(qaFolder, "proxy-restart-input.txt"), new[] { savedPath, source.proxies["M"].file });
            qaProjectToOpen = null;
        }

        async Task VerifyProxyRestart()
        {
            var expected = File.ReadAllLines(Path.Combine(qaFolder, "proxy-restart-input.txt"));
            qaProjectToOpen = expected[0];
            var modified = File.GetLastWriteTimeUtc(expected[1]);
            await Script("document.querySelector('[data-project-dialog] [data-open-project]').click()");
            await WaitProxyQuality("M", 480);
            Qa.Check(source.proxies["M"].file == expected[1] && File.GetLastWriteTimeUtc(expected[1]) == modified,
                "After an application restart the original project uses the same validated M file without encoding");
            File.WriteAllText(Path.Combine(qaFolder, "proxy-restart-results.txt"),
                "PASS: new KIRIHARI process reopens the saved edit and reuses the same M generation.");
        }

        async Task VerifyImportProxyChoices()
        {
            await WaitFor("document.querySelector('[data-proxy-dialog]').open");
            Qa.Check(await Script(@"Array.from(document.querySelectorAll('[data-proxy-import]'), x => x.dataset.proxyImport)
                .join(',') === 'ORIGINAL,H,L,M'") == "true", "Import offers original, H, L and M in order");
            Qa.Check(await Script(@"(() => {
                const dialog = document.querySelector('[data-proxy-dialog]');
                return dialog.scrollWidth === dialog.clientWidth &&
                    [['ORIGINAL', '元動画', '変換待ちなし'], ['H', '720p', '負荷は高め'], ['L', '360p', '軽さを優先'], ['M', '480p', '迷ったら']]
                        .every(([level, title, note]) => {
                            const button = dialog.querySelector('[data-proxy-import=' + level + ']');
                            return button.getBoundingClientRect().width > 0 && button.scrollWidth === button.clientWidth &&
                                button.querySelector('strong').textContent.includes(title) && button.querySelector('span').textContent.includes(note);
                        });
            })()") == "true", "The quality and editing-load guidance appears inside each choice without horizontal clipping");
        }

        async Task SeekProxyQa(int frame)
        {
            await Script(@"(() => {
                const axis = document.querySelector('.kh-axis'), r = axis.getBoundingClientRect();
                axis.dispatchEvent(new MouseEvent('click', {bubbles:true, clientX:r.left+r.width*" + frame + @"/360}));
            })()");
        }

        async Task WaitProxyQuality(string level, int height, double? time = null)
        {
            await WaitFor("!window.kirihariQa.debug().converting && !window.kirihariQa.debug().busy && " +
                "document.querySelector('[data-preview-proxy]').dataset.proxyTone === '" + level + "' && " +
                "document.querySelector('[data-media]').videoHeight === " + height +
                " && !document.querySelector('[data-media]').seeking" +
                (time.HasValue ? " && Math.abs(document.querySelector('[data-media]').currentTime-" + MediaTool.Num(time.Value) + ") < .025" : ""), 180);
        }

        async Task ChooseProxyAction(string level, string action)
        {
            await Script("document.querySelector('[data-proxy-manager]').open = true");
            string control = "document.querySelector('[data-proxy-level=" + level + "] [data-proxy-" + action + "]')";
            await WaitFor(control);
            await Script(control + ".click()");
            await WaitFor("document.querySelector('[data-proxy-confirm]').open");
            await Script("document.querySelector('[data-proxy-confirm-apply]').click()");
        }
    }
}

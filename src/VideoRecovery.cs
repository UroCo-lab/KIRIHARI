using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Kirihari
{
    internal sealed partial class VideoWindow
    {
        const string RecoveryInfo = ".kirihari-recovery.json";
        static readonly long ProcessStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        sealed class OpenedProject
        {
            public object session;
            public Dictionary<string, object> project;
            public bool recovered;
            public bool relinked;
        }
        void BeginRecoverySession(string folder)
        {
            SaveText(Path.Combine(folder, RecoveryInfo), MediaTool.Json().Serialize(new {
                pid = Process.GetCurrentProcess().Id, started = ProcessStarted, clean = false
            }));
        }
        void MarkSessionClean(string folder = null)
        {
            folder = folder ?? work;
            if (folder == null || !Directory.Exists(folder)) return;
            try
            {
                SaveText(Path.Combine(folder, RecoveryInfo), MediaTool.Json().Serialize(new {
                    pid = Process.GetCurrentProcess().Id, started = ProcessStarted, clean = true
                }));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        bool Recoverable(string folder)
        {
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return false;
                string metadata = Path.Combine(folder, RecoveryInfo), draft = Path.Combine(folder, "自動保存.kirihari");
                if (!File.Exists(metadata) || !File.Exists(draft) || new FileInfo(metadata).Length > 65536 || new FileInfo(draft).Length > 64000000) return false;
                var info = MediaTool.Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(metadata));
                if (info == null) return false;
                if (Convert.ToBoolean(info["clean"])) return false;
                int pid = Convert.ToInt32(info["pid"]);
                long started = Convert.ToInt64(info["started"]);
                try
                {
                    using (var process = Process.GetProcessById(pid))
                        if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == started) return false;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { return false; }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (KeyNotFoundException) { return false; }
            catch (FormatException) { return false; }
            catch (InvalidCastException) { return false; }
            catch (OverflowException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        object[] RecentRecoveries()
        {
            if (!Directory.Exists(WorkRoot)) return new object[0];
            return Directory.EnumerateDirectories(WorkRoot).Where(Recoverable)
                .OrderByDescending(folder => File.GetLastWriteTimeUtc(Path.Combine(folder, "自動保存.kirihari")))
                .Take(20).Select(folder => (object)new {
                    id = Path.GetFileName(folder), name = Path.GetFileName(folder),
                    updated = File.GetLastWriteTime(Path.Combine(folder, "自動保存.kirihari")).ToString("yyyy/MM/dd HH:mm:ss")
                }).ToArray();
        }
        string RecoveryFolder(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id != Path.GetFileName(id) || id == "." || id == ".." ||
                id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new IOException("自動保存を選んでください。");
            string folder = Path.Combine(WorkRoot, id);
            if (!Recoverable(folder)) throw new IOException("この自動保存は復元できません。別のアプリで編集中でないか確認してください。");
            return folder;
        }
        async Task<object> RecoverProject(string id)
        {
            string folder = RecoveryFolder(id);
            var result = (OpenedProject)await OpenProject(Path.Combine(folder, "自動保存.kirihari"), false);
            if (result == null) return null;
            string name = "復元_" + (Text(result.project, "projectName") ?? Path.GetFileName(folder));
            result.project["projectName"] = name;
            Directory.CreateDirectory(ProjectsRoot);
            projectFile = MediaTool.Available(ProjectsRoot, SafeProjectName(name), ".kirihari");
            // Preserve recovery across a second crash, even before the first edit or save.
            SaveText(Path.Combine(work, "自動保存.kirihari"), MediaTool.Json().Serialize(result.project));
            result.recovered = true;
            MarkSessionClean(folder);
            return result;
        }
    }
}

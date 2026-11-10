using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Kirihari
{
    internal sealed partial class VideoWindow
    {
        Dictionary<string, string> folderSettings;
        string archiveFolder;
        string qaPickerDirectory;
        string qaProjectToOpen;

        string SampleFolder { get { return Path.Combine(home, "samples", "lightweight"); } }
        string FolderSettingsFile { get { return Path.Combine(qaFolder ?? Path.Combine(home, "data"), "folders.json"); } }
        string OutputDefault { get { return qaFolder ?? Path.Combine(home, "output"); } }
        string ProjectDirectory { get { return projectFile == null ? archiveFolder : Path.GetDirectoryName(projectFile); } }

        Dictionary<string, string> ReadFolderSettings()
        {
            try
            {
                var file = new FileInfo(FolderSettingsFile);
                if (file.Exists && file.Length <= 32768)
                    return MediaTool.Json().Deserialize<Dictionary<string, string>>(File.ReadAllText(file.FullName))
                        ?? new Dictionary<string, string>();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return new Dictionary<string, string>();
        }

        string ExistingFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return null;
            try
            {
                string local = Files.Local(path, home);
                return Directory.Exists(local) ? local : null;
            }
            catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        string FolderSetting(string key)
        {
            if (folderSettings == null) folderSettings = ReadFolderSettings();
            string value;
            return folderSettings.TryGetValue(key, out value) ? value : null;
        }

        void StoreFolder(string key, string path)
        {
            var next = ReadFolderSettings();
            if (path == null) next.Remove(key);
            else next[key] = path;
            Directory.CreateDirectory(Path.GetDirectoryName(FolderSettingsFile));
            SaveText(FolderSettingsFile, MediaTool.Json().Serialize(next));
            folderSettings = next;
        }

        void RememberFolder(string kind, string file)
        {
            try { StoreFolder(kind + "Last", Path.GetDirectoryName(file)); }
            catch (IOException) { } // A successful open/save remains successful if preferences are unwritable.
            catch (UnauthorizedAccessException) { }
        }

        string DefaultFolder(string kind)
        {
            switch (kind)
            {
                case "media": return ExistingFolder(FolderSetting("mediaDefault")) ?? ExistingFolder(SampleFolder) ?? home;
                case "project": return ExistingFolder(FolderSetting("projectDefault")) ?? ProjectsRoot;
                case "save": return ExistingFolder(FolderSetting("saveDefault")) ?? ProjectsRoot;
                case "output": return ExistingFolder(FolderSetting("saveDefault")) ?? OutputDefault;
                default: throw new IOException("フォルダの種類を確認できません。");
            }
        }

        string InitialFolder(string kind, string mode)
        {
            string fallback = DefaultFolder(kind);
            switch (mode ?? "auto")
            {
                case "auto":
                    return ExistingFolder(FolderSetting(kind + "Default"))
                        ?? ExistingFolder(FolderSetting(kind + "Last"))
                        ?? ((kind == "media" || kind == "project") ? ExistingFolder(SampleFolder) : null)
                        ?? fallback;
                case "default": return fallback;
                case "project": return ExistingFolder(ProjectDirectory) ?? fallback;
                case "last": return ExistingFolder(FolderSetting(kind + "Last")) ?? fallback;
                default: throw new IOException("フォルダの選び方を確認できません。");
            }
        }

        object FolderChoices(string kind)
        {
            return new {
                preferred = DefaultFolder(kind),
                project = ExistingFolder(ProjectDirectory),
                last = ExistingFolder(FolderSetting(kind + "Last")),
                configured = FolderSetting((kind == "output" ? "save" : kind) + "Default")
            };
        }

        void SetPickerFolder(FileDialog dialog, string kind, string mode)
        {
            string folder = InitialFolder(kind, mode);
            if (folder == ProjectsRoot && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
            dialog.InitialDirectory = folder;
            if (qaFolder != null) qaPickerDirectory = dialog.InitialDirectory;
        }

        string PickFolder(string title, string start)
        {
            if (qaFolder != null) return qaFolderToPick;
            using (var folder = new Forms.FolderBrowserDialog { Description = title, ShowNewFolderButton = true })
            {
                string existing = ExistingFolder(start);
                if (existing != null) folder.SelectedPath = existing;
                if (folder.ShowDialog() != Forms.DialogResult.OK) return null;
                return Files.Local(folder.SelectedPath, home);
            }
        }

        object ConfigureFolder(string kind, bool reset)
        {
            if (kind != "media" && kind != "project" && kind != "save") throw new IOException("フォルダの種類を確認できません。");
            if (reset) StoreFolder(kind + "Default", null);
            else
            {
                string chosen = PickFolder("デフォルトのフォルダを指定", DefaultFolder(kind));
                if (chosen == null) return null;
                StoreFolder(kind + "Default", chosen);
            }
            return FolderChoices(kind);
        }
    }
}

using System;
using System.IO;
using System.Windows;
using System.Runtime.CompilerServices;

namespace Kirihari
{
    internal static class Program
    {
        internal static string QaSamples = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples");

        [STAThread]
        public static int Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            bool qa = args.Length >= 1 && (args[0] == "--qa" || args[0] == "--video-qa" || args[0] == "--window-qa" || args[0] == "--setup-download-qa");
            if (!qa && !(args.Length == 1 && args[0] == "--classic"))
            {
                string home = AppDomain.CurrentDomain.BaseDirectory;
                try
                {
                    if ((args.Length == 1 && args[0] == "--setup") || Setup.ShouldShow(File.Exists(Setup.Marker(home)), Setup.Ready(home)))
                    {
                        var setup = new SetupWindow(home);
                        setup.ShowDialog();
                        if (!setup.Accepted) return 0;
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show("最初の準備を開始できません。ZIPをすべて展開し、書き込み可能な場所に置いてください。\n\n" + e.Message, "KIRIHARI");
                    return 1;
                }
            }
            app.ShutdownMode = ShutdownMode.OnLastWindowClose;
            return RunEditor(app, args);
        }

        // Keep the editor/SDK types out of the entry-point JIT before .NET preparation.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int RunEditor(Application app, string[] args)
        {
            if (args.Length == 3 && (args[0] == "--qa" || args[0] == "--video-qa" || args[0] == "--window-qa" || args[0] == "--setup-download-qa"))
            {
                QaSamples = Path.GetFullPath(args[2]);
                args = new[] { args[0], args[1] };
            }
            if (args.Length == 2 && args[0] == "--qa")
            {
                try { return Qa.Run(Path.GetFullPath(args[1])); }
                catch (Exception e) { File.WriteAllText(Path.Combine(args[1], "failed.txt"), e.ToString()); return 1; }
            }
            if (args.Length == 2 && args[0] == "--video-qa")
            {
                try { MediaQa.Run(Path.GetFullPath(args[1])); return 0; }
                catch (Exception e) { File.WriteAllText(Path.Combine(args[1], "video-failed.txt"), e.ToString()); return 1; }
            }
            if (args.Length == 2 && args[0] == "--setup-download-qa")
            {
                try { SetupQa.RunDownload(Path.GetFullPath(args[1])); return 0; }
                catch (Exception e) { File.WriteAllText(Path.Combine(args[1], "setup-download-failed.txt"), e.ToString()); return 1; }
            }
            if (args.Length == 2 && args[0] == "--window-qa")
                return app.Run(new VideoWindow(Path.GetFullPath(args[1])));
            return app.Run(args.Length == 1 && args[0] == "--classic" ? (Window)new Editor() : new VideoWindow());
        }
    }
}

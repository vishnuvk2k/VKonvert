using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using VKonvert.Engine;
using VKonvert.UI;

namespace VKonvert
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();
            Theme.InitScale();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportCrash(e.Exception);

            // VKonvert-Setup.exe unpacks the files, then runs:  VKonvert.exe --register <menu> <start> <desktop> <default>
            if (args.Length >= 4 && args[0] == "--register")
            {
                try
                {
                    ShellIntegration.Register(new ShellIntegration.InstallChoices
                    {
                        Folder = AppDomain.CurrentDomain.BaseDirectory,
                        RightClickMenu = args[1] == "1",
                        StartMenuShortcut = args[2] == "1",
                        DesktopShortcut = args[3] == "1",
                        DefaultForImages = args.Length > 4 && args[4] == "1",
                    });
                    return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("VKonvert couldn’t finish setting up.\n\n" + ex.Message, "VKonvert Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }
            }

            // Settings › Apps › VKonvert › Uninstall runs:  VKonvert.exe --uninstall
            if (args.Length >= 1 && args[0] == "--uninstall")
            {
                var quiet = args.Contains("--quiet");
                if (!quiet && !ConfirmUninstall(null)) return 1;
                CloseOtherWindows();
                ShellIntegration.Uninstall();
                if (!quiet) MessageBox.Show("VKonvert has been removed from this PC.", "Uninstall VKonvert", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShellIntegration.RemoveFilesAfterExit();
                return 0;
            }

            if (!ImageEngineWorks()) return 1;
            Images.HeicWriter = HeicWindows.Write;
            Images.HeicReader = HeicWindows.ReadAsPng;

            // Right-click › VKonvert › Convert to JPG  runs:  VKonvert.exe --to jpg "file"
            if (args.Length >= 2 && args[0] == "--to")
                return QuickConvert(args[1], args.Skip(2).ToArray());

            // Right-click › VKonvert › Compress  runs:  VKonvert.exe --tool compress "file"
            string toolId = null;
            if (args.Length >= 2 && args[0] == "--tool") { toolId = args[1]; args = args.Skip(2).ToArray(); }

            Application.AddMessageFilter(new WheelRouter());
            var files = args.Where(a => File.Exists(a) || Directory.Exists(a)).ToArray();
            using (var single = new SingleInstance())
            {
                var message = toolId != null ? new[] { "@tool:" + toolId }.Concat(files).ToArray() : files;
                if (!single.IsFirst && single.SendToFirst(message)) return 0;

                var form = new MainForm(files, toolId);
                if (single.IsFirst)
                    form.Shown += (s, e) =>
                        single.Listen(more => form.BeginInvoke(new Action(() => form.AddFilesFromOtherLaunch(more))));
                Application.Run(form);
                ShellIntegration.RemoveFilesAfterExit(); // only does something after "Uninstall VKonvert"
            }
            return 0;
        }

        public static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VKonvert", "error.log");

        /// <summary>Keeps the technical details of a failure for troubleshooting (the person sees a plain message).</summary>
        public static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024) File.Delete(LogPath);
                File.AppendAllText(LogPath, $"{DateTime.Now:u} VKonvert {ShellIntegration.Version}\r\n{ex}\r\n\r\n");
            }
            catch { }
        }

        /// <summary>Writes the details to %LOCALAPPDATA%\VKonvert\error.log and tells the person in plain words.</summary>
        static void ReportCrash(Exception ex)
        {
            Log(ex);
            var log = LogPath;
            MessageBox.Show("Something went wrong in VKonvert, but your files are safe.\n\n" + ex.Message +
                "\n\nDetails were saved to:\n" + log, "VKonvert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        static bool ConfirmUninstall(IWin32Window owner) =>
            MessageBox.Show(owner,
                "Uninstall VKonvert?\n\nThis removes the right-click menu, the shortcuts and the installed VKonvert files. " +
                "Your pictures, PDFs and converted files are not touched.",
                "Uninstall VKonvert", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        /// <summary>The "•••" menu's Uninstall: removes everything, then closes the app.</summary>
        public static void Uninstall(Form owner)
        {
            if (!ConfirmUninstall(owner)) return;
            CloseOtherWindows();
            ShellIntegration.Uninstall();
            MessageBox.Show(owner, "VKonvert has been removed from this PC. It will close now.", "Uninstall VKonvert", MessageBoxButtons.OK, MessageBoxIcon.Information);
            owner.Close();
        }

        // An open window keeps its files locked, so they couldn't be removed.
        static void CloseOtherWindows()
        {
            var me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("VKonvert").Where(p => p.Id != me))
                try { p.CloseMainWindow(); if (!p.WaitForExit(3000)) p.Kill(); } catch { }
        }

        // The ImageMagick engine needs the Microsoft Visual C++ runtime, which most PCs already have.
        static bool ImageEngineWorks()
        {
            try
            {
                new ImageMagick.MagickImage(ImageMagick.MagickColors.White, 1, 1).Dispose();
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is TypeInitializationException || ex is BadImageFormatException)
            {
                const string url = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
                var answer = MessageBox.Show(
                    "VKonvert needs the free Microsoft Visual C++ Runtime (x64), which isn’t installed on this PC.\n\n" +
                    "Download it now? After installing it, open VKonvert again.",
                    "VKonvert", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer == DialogResult.Yes) Process.Start(url);
                return false;
            }
        }

        static int QuickConvert(string formatName, string[] files)
        {
            var target = Formats.TryGet(formatName);
            if (target == null)
            {
                MessageBox.Show($"Unknown format: {formatName}", "VKonvert", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            // Explorer may start dozens of these at once; only run a few conversions at a time.
            var slots = Math.Max(2, Environment.ProcessorCount);
            using (var gate = new Semaphore(slots, slots, @"Local\VKonvert.QuickConvert"))
            {
                gate.WaitOne();
                try
                {
                    var o = new ConvertOptions { Target = target };
                    foreach (var f in files)
                    {
                        try { Images.Convert(f, o); }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Couldn’t convert {Path.GetFileName(f)} to {target.Name}.\n\n{Tool.Friendly(ex)}",
                                "VKonvert", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return 1;
                        }
                    }
                }
                finally { gate.Release(); }
            }
            return 0;
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using VKonvert.Engine;

namespace VKonvert
{
    /// <summary>
    /// Adds VKonvert to Windows for the current user: "Open with", a "VKonvert" right-click submenu,
    /// shortcuts, and the Settings › Apps entry. Everything is under HKEY_CURRENT_USER, so no admin rights.
    /// </summary>
    public static class ShellIntegration
    {
        public const string Version = "2.0";
        const string ExeName = "VKonvert.exe";
        const string ProgId = "VKonvert.File";
        const string VerbKey = "VKonvert";
        const string ClassesRoot = @"Software\Classes";
        const string SettingsKey = @"Software\VKonvert";
        const string AppsKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VKonvert";

        static string[] AllExtensions => Formats.ImageExtensions.Concat(new[] { ".pdf" }).ToArray();

        public static string DefaultInstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VKonvert");

        public static string InstalledDir
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey)) return k?.GetValue("InstallDir") as string; }
            private set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (value == null) k.DeleteValue("InstallDir", false);
                    else k.SetValue("InstallDir", value);
                }
            }
        }

        static string[] InstalledFiles
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey)) return k?.GetValue("InstalledFiles") as string[]; }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (value == null) k.DeleteValue("InstalledFiles", false);
                    else k.SetValue("InstalledFiles", value, RegistryValueKind.MultiString);
                }
            }
        }

        public static bool IsInstalled => InstalledDir != null;

        static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "VKonvert.lnk");
        static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VKonvert.lnk");

        public sealed class InstallChoices
        {
            public string Folder = DefaultInstallDir;
            public bool RightClickMenu = true;
            public bool StartMenuShortcut = true;
            public bool DesktopShortcut = true;
            public bool DefaultForImages;
        }

        /// <summary>Called by the installer after it has unpacked the files into <see cref="InstallChoices.Folder"/>.</summary>
        public static void Register(InstallChoices c)
        {
            var target = Path.GetFullPath(c.Folder).TrimEnd('\\');
            var previous = InstalledDir;
            if (previous != null && !SamePath(previous, target)) UnregisterMenus();

            var exe = Path.Combine(target, ExeName);
            InstalledDir = target;
            InstalledFiles = Directory.GetFiles(target).Select(Path.GetFileName).ToArray();
            RemoveOldVersions();
            if (c.RightClickMenu) RegisterMenus(exe, c.DefaultForImages); else UnregisterMenus();
            SetShortcut(StartMenuShortcut, exe, c.StartMenuShortcut);
            SetShortcut(DesktopShortcut, exe, c.DesktopShortcut);
            WriteAppsEntry(exe, target);
        }

        static void WriteAppsEntry(string exe, string dir)
        {
            long kb = 0;
            try { kb = Directory.GetFiles(dir).Sum(f => new FileInfo(f).Length) / 1024; } catch { }
            using (var k = Registry.CurrentUser.CreateSubKey(AppsKey))
            {
                k.SetValue("DisplayName", "VKonvert");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Vishnu VK");
                k.SetValue("DisplayIcon", $"\"{exe}\",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", $"\"{exe}\" --uninstall");
                k.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
                k.SetValue("URLInfoAbout", "https://vkonvert.com");
                k.SetValue("Comments", "Convert, compress and combine images and PDFs");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
            }
        }

        // ───────────── Right-click menu ─────────────

        static readonly (string tool, string label)[] ImageTools =
        {
            ("compress", "Compress"), ("resize", "Resize"), ("imagestopdf", "Make one PDF"), ("removeinfo", "Remove photo info"),
        };

        static readonly (string tool, string label)[] PdfTools =
        {
            ("compresspdf", "Compress PDF"), ("merge", "Merge PDFs"), ("split", "Split PDF"),
            ("pdftoimages", "Save pages as images"), ("rotate", "Rotate && reorder pages"), ("protect", "Lock with a password"),
        };

        static void RegisterMenus(string exe, bool defaultForImages)
        {
            var open = $"\"{exe}\" \"%1\"";
            var icon = $"\"{exe}\",0";

            using (var classes = Registry.CurrentUser.CreateSubKey(ClassesRoot))
            {
                using (var app = classes.CreateSubKey($@"Applications\{ExeName}"))
                {
                    app.SetValue("FriendlyAppName", "VKonvert");
                    using (var k = app.CreateSubKey("DefaultIcon")) k.SetValue("", icon);
                    using (var k = app.CreateSubKey(@"shell\open\command")) k.SetValue("", open);
                    using (var types = app.CreateSubKey("SupportedTypes"))
                        foreach (var ext in AllExtensions) types.SetValue(ext, "");
                }
                using (var prog = classes.CreateSubKey(ProgId))
                {
                    prog.SetValue("", "VKonvert file");
                    using (var k = prog.CreateSubKey("DefaultIcon")) k.SetValue("", icon);
                    using (var k = prog.CreateSubKey(@"shell\open\command")) k.SetValue("", open);
                }

                foreach (var ext in AllExtensions)
                {
                    var isPdf = ext == ".pdf";
                    using (var k = classes.CreateSubKey($@"{ext}\OpenWithProgids"))
                        k.SetValue(ProgId, new byte[0], RegistryValueKind.None);
                    classes.CreateSubKey($@"{ext}\OpenWithList\{ExeName}")?.Dispose();

                    var menuPath = $@"SystemFileAssociations\{ext}\shell\{VerbKey}";
                    classes.DeleteSubKeyTree(menuPath, false);
                    using (var menu = classes.CreateSubKey(menuPath))
                    {
                        menu.SetValue("MUIVerb", "VKonvert");
                        menu.SetValue("Icon", icon);
                        menu.SetValue("SubCommands", "");
                        int n = 0;
                        void Item(string label, string args, bool separatorAbove = false)
                        {
                            using (var item = menu.CreateSubKey($@"shell\{n++:00}item"))
                            {
                                item.SetValue("MUIVerb", label);
                                item.SetValue("MultiSelectModel", "Player");
                                if (separatorAbove) item.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord);
                                using (var cmd = item.CreateSubKey("command")) cmd.SetValue("", $"\"{exe}\" {args}\"%1\"");
                            }
                        }
                        Item("Open in VKonvert", "");
                        foreach (var (tool, label) in isPdf ? PdfTools : ImageTools) Item(label, $"--tool {tool} ");
                        if (!isPdf)
                        {
                            var current = Formats.NameOf("x" + ext);
                            var first = true;
                            foreach (var t in Formats.Targets.Where(t => t.Name != current))
                            {
                                Item("Convert to " + t.Name, $"--to {t.Name.ToLowerInvariant()} ", first);
                                first = false;
                            }
                        }
                    }
                    if (defaultForImages && !isPdf)
                        using (var k = classes.CreateSubKey(ext)) k.SetValue("", ProgId);
                }
            }
            NotifyShell();
        }

        static void UnregisterMenus()
        {
            using (var classes = Registry.CurrentUser.OpenSubKey(ClassesRoot, true))
            {
                if (classes == null) return;
                classes.DeleteSubKeyTree($@"Applications\{ExeName}", false);
                classes.DeleteSubKeyTree(ProgId, false);
                foreach (var ext in AllExtensions)
                {
                    using (var k = classes.OpenSubKey($@"{ext}\OpenWithProgids", true)) k?.DeleteValue(ProgId, false);
                    using (var k = classes.OpenSubKey(ext, true))
                        if (k?.GetValue("") as string == ProgId) k.DeleteValue("", false);
                    classes.DeleteSubKeyTree($@"{ext}\OpenWithList\{ExeName}", false);
                    classes.DeleteSubKeyTree($@"SystemFileAssociations\{ext}\shell\{VerbKey}", false);
                }
            }
            NotifyShell();
        }

        // ───────────── Older releases (VPixShift 1.0, Vpix 1.1) ─────────────

        /// <summary>Removes what the earlier releases added (menus, shortcuts, Apps entry and their files).</summary>
        public static void RemoveOldVersions()
        {
            foreach (var (exe, prog, verb, name) in new[] { ("VPixShift.exe", "VPixShift.Image", "VPixShift", "VPixShift"), ("Vpix.exe", "Vpix.Image", "Vpix", "Vpix") })
            {
                try
                {
                    using (var classes = Registry.CurrentUser.OpenSubKey(ClassesRoot, true))
                    {
                        if (classes != null)
                        {
                            classes.DeleteSubKeyTree($@"Applications\{exe}", false);
                            classes.DeleteSubKeyTree(prog, false);
                            foreach (var ext in AllExtensions)
                            {
                                using (var k = classes.OpenSubKey($@"{ext}\OpenWithProgids", true)) k?.DeleteValue(prog, false);
                                classes.DeleteSubKeyTree($@"{ext}\OpenWithList\{exe}", false);
                                classes.DeleteSubKeyTree($@"SystemFileAssociations\{ext}\shell\{verb}", false);
                            }
                        }
                    }

                    string dir = null; string[] files = null;
                    using (var k = Registry.CurrentUser.OpenSubKey($@"Software\{name}"))
                    {
                        dir = k?.GetValue("InstallDir") as string;
                        files = k?.GetValue("InstalledFiles") as string[];
                    }
                    if (dir == null)
                    {
                        var guess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", name);
                        if (File.Exists(Path.Combine(guess, exe))) { dir = guess; files = Directory.GetFiles(guess).Select(Path.GetFileName).ToArray(); }
                    }
                    Registry.CurrentUser.DeleteSubKeyTree($@"Software\{name}", false);
                    Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{name}", false);
                    foreach (var lnk in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory })
                    {
                        var p = Path.Combine(Environment.GetFolderPath(lnk), name + ".lnk");
                        if (File.Exists(p)) File.Delete(p);
                    }
                    foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                        try { proc.CloseMainWindow(); if (!proc.WaitForExit(3000)) proc.Kill(); } catch { }
                    if (dir != null && files != null && File.Exists(Path.Combine(dir, exe)))
                    {
                        foreach (var f in files) try { File.Delete(Path.Combine(dir, f)); } catch { }
                        try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
                    }
                }
                catch
                {
                    // Anything left behind is harmless; the new menus replace the old ones.
                }
            }
            NotifyShell();
        }

        // ───────────── Uninstall ─────────────

        public static void Uninstall()
        {
            UnregisterMenus();
            Registry.CurrentUser.DeleteSubKeyTree(AppsKey, false);
            SetShortcut(StartMenuShortcut, null, false);
            SetShortcut(DesktopShortcut, null, false);

            var dir = InstalledDir;
            var installed = InstalledFiles;
            Registry.CurrentUser.DeleteSubKeyTree(SettingsKey, false); // also forgets the theme choice
            if (dir != null && installed != null && installed.Length > 0 && File.Exists(Path.Combine(dir, ExeName)))
                pendingRemoval = Tuple.Create(dir, installed);
        }

        static Tuple<string, string[]> pendingRemoval;

        /// <summary>
        /// The last thing VKonvert does after an uninstall: deletes only the files it installed (the folder
        /// may hold other things), then the folder if that leaves it empty. Our own exe is locked until we
        /// exit, so a hidden cmd waits a moment and tries twice.
        /// </summary>
        public static void RemoveFilesAfterExit()
        {
            if (pendingRemoval == null) return;
            var dir = pendingRemoval.Item1;
            var del = string.Join(" ", pendingRemoval.Item2.Select(f => $"\"{Path.Combine(dir, f)}\""));
            var once = $"ping 127.0.0.1 -n 4 >nul & del /f /q {del} 2>nul";
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c {once} & {once} & rmdir \"{dir}\" 2>nul")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(), // not inside the folder being removed
            });
            pendingRemoval = null;
        }

        static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        static void SetShortcut(string path, string exe, bool wanted)
        {
            try
            {
                if (!wanted)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var link = (IShellLinkW)new ShellLink();
                link.SetPath(exe);
                link.SetWorkingDirectory(Path.GetDirectoryName(exe));
                link.SetIconLocation(exe, 0);
                link.SetDescription("Convert, compress and combine images and PDFs");
                ((IPersistFile)link).Save(path, true);
                Marshal.ReleaseComObject(link);
            }
            catch
            {
                // A missing shortcut is not worth failing the install over.
            }
        }

        // The Windows shell's own shortcut object (works even where Windows Script Host is turned off).
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int max, IntPtr findData, uint flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int cmd);
            void SetShowCmd(int cmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int max, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
        interface IPersistFile
        {
            void GetClassID(out Guid clsid);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace USBGuard
{
    public class PcResult
    {
        public int Threats;
        public int Killed;
        public readonly List<string> Details = new List<string>();
        public bool SalitySuspected;
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// تنظيف الحاسبة نفسها: فايروس الشورتكت/الفلاشة يزرع نفسه بالحاسبة (سكربت VBS بـ AppData + تشغيل تلقائي)
    /// وكل فلاشة تخشها ترجع تتلوث. هنا نقتل العملية ونشيل التشغيل التلقائي ونعزل الملفات.
    /// </summary>
    public static class PcCleaner
    {
        static readonly Regex ScriptHost = new Regex(@"(^|[\\/\s""])(wscript|cscript|mshta)(\.exe)?([\s""]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex FileRef = new Regex(
            @"(?:""([A-Za-z]:\\[^""]+?\.(?:vbs|vbe|js|jse|wsf|wsh|hta|exe|scr|pif|bat|cmd|com))"")|([A-Za-z]:\\[^""\s,]+?\.(?:vbs|vbe|js|jse|wsf|wsh|hta|exe|scr|pif|bat|cmd|com))(?=[\s"",]|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly string[] ScriptExt = { ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta" };

        // برامج يحاول الفايروس يمنع تشغيلها عن طريق IFEO Debugger
        static readonly string[] ProtectedExe =
        {
            "taskmgr.exe", "regedit.exe", "msconfig.exe", "cmd.exe", "mmc.exe", "rstrui.exe", "explorer.exe",
            "mpcmdrun.exe", "msmpeng.exe", "msascui.exe", "securityhealthsystray.exe", "usbguard.exe",
            "kvrt.exe", "salitykiller.exe", "mbam.exe", "avp.exe", "procexp.exe", "autoruns.exe", "powershell.exe",
        };

        static string[] _suspiciousDirs;
        static string[] SuspiciousDirs => _suspiciousDirs ??= new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.GetTempPath(),
            Environment.ExpandEnvironmentVariables(@"%PUBLIC%"),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Temp"),
        }.Where(d => !string.IsNullOrEmpty(d) && !d.Contains("%")).Select(d => d.TrimEnd('\\') + "\\").ToArray();

        public static PcResult Scan(IProgress<(int pct, string msg)> progress)
        {
            var r = new PcResult();
            void P(int pct, string key) => progress?.Report((pct, L.T(key)));
            Settings.Log("===== بدء فحص الحاسبة =====");

            P(5, "pc_step_proc");    try { CleanProcesses(r); } catch (Exception ex) { Settings.Log("proc: " + ex.Message); }
            P(25, "pc_step_run");    try { CleanRunKeys(r); } catch (Exception ex) { Settings.Log("run: " + ex.Message); }
            P(40, "pc_step_startup");try { CleanStartupFolders(r); } catch (Exception ex) { Settings.Log("startup: " + ex.Message); }
            P(55, "pc_step_tasks");  try { CleanScheduledTasks(r); } catch (Exception ex) { Settings.Log("tasks: " + ex.Message); }
            P(70, "pc_step_system"); try { CleanWinlogon(r); CleanIfeo(r); } catch (Exception ex) { Settings.Log("sys: " + ex.Message); }
            P(80, "pc_step_drives"); try { CleanFixedDriveRoots(r); } catch (Exception ex) { Settings.Log("drives: " + ex.Message); }
            P(90, "pc_step_sality"); try { CheckSality(r); } catch { }
            try { var fixes = SystemTools.RepairSystem(); if (fixes > 0) r.Details.Add(L.F("pc_repaired", fixes)); } catch { }

            P(100, "step_done");
            Settings.Log($"===== انتهى فحص الحاسبة: تهديدات={r.Threats} عمليات={r.Killed} =====");
            return r;
        }

        // ------------------------------------------------------------------
        // 1) العمليات الشغالة
        // ------------------------------------------------------------------
        static void CleanProcesses(PcResult r)
        {
            int me = Process.GetCurrentProcess().Id;
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    int pid = Convert.ToInt32(mo["ProcessId"]);
                    if (pid == me || pid <= 4) continue;
                    var name = (mo["Name"] as string ?? "").ToLowerInvariant();
                    var exe = mo["ExecutablePath"] as string;
                    var cmd = mo["CommandLine"] as string ?? "";

                    var files = new List<string>();
                    bool bad = false;

                    if (name == "wscript.exe" || name == "cscript.exe" || name == "mshta.exe")
                    {
                        if (IsSuspiciousCommand(cmd, out var f)) { bad = true; files.AddRange(f); }
                    }
                    else if (!string.IsNullOrEmpty(exe))
                    {
                        bool onUsb = IsOnRemovable(exe);
                        bool hiddenSys = IsHiddenSystem(exe);
                        if ((onUsb && hiddenSys) || (InSuspiciousDir(exe) && hiddenSys))
                        {
                            bad = true; files.Add(exe);
                        }
                    }

                    if (!bad) continue;
                    try
                    {
                        Process.GetProcessById(pid).Kill();
                        r.Killed++;
                        r.Threats++;
                        r.Details.Add(L.F("pc_killed", name));
                        Settings.Log($"  [قتل عملية] {name} ({pid}) {cmd}");
                    }
                    catch { }
                    System.Threading.Thread.Sleep(150);
                    foreach (var f in files.Distinct()) QuarantineFile(r, f, "running malware");
                }
            }
        }

        // ------------------------------------------------------------------
        // 2) مفاتيح التشغيل التلقائي Run / RunOnce
        // ------------------------------------------------------------------
        static void CleanRunKeys(PcResult r)
        {
            var paths = new[]
            {
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
            };
            var roots = new List<RegistryKey>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try { roots.Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)); } catch { }
                try { roots.Add(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view)); } catch { }
            }
            foreach (var root in roots)
            {
                using (root)
                    foreach (var p in paths)
                    {
                        try
                        {
                            using var k = root.OpenSubKey(p, true);
                            if (k == null) continue;
                            foreach (var name in k.GetValueNames())
                            {
                                var cmd = k.GetValue(name) as string;
                                if (string.IsNullOrWhiteSpace(cmd)) continue;
                                if (!IsSuspiciousCommand(cmd, out var files)) continue;
                                k.DeleteValue(name, false);
                                r.Threats++;
                                r.Details.Add(L.F("pc_startup_removed", name));
                                Settings.Log($"  [حذف تشغيل تلقائي] {root.Name}\\{p}\\{name} = {cmd}");
                                foreach (var f in files) QuarantineFile(r, f, "autostart: " + name);
                            }
                        }
                        catch { }
                    }
            }
        }

        // ------------------------------------------------------------------
        // 3) مجلدات بدء التشغيل (Startup)
        // ------------------------------------------------------------------
        static void CleanStartupFolders(PcResult r)
        {
            foreach (var dir in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    bool bad = false;
                    var extra = new List<string>();

                    if (ScriptExt.Contains(ext)) bad = true;                                   // سكربت بمجلد بدء التشغيل
                    else if (ext == ".lnk" && Scanner.IsMaliciousLnk(f)) bad = true;          // اختصار يشغل wscript/cmd...
                    else if ((ext == ".exe" || ext == ".scr" || ext == ".com" || ext == ".pif") && IsHiddenSystem(f)) bad = true;

                    if (!bad) continue;
                    if (ext == ".lnk")
                    {
                        try
                        {
                            var bytes = File.ReadAllBytes(f);
                            var text = System.Text.Encoding.Unicode.GetString(bytes) + " " + System.Text.Encoding.ASCII.GetString(bytes);
                            foreach (Match m in FileRef.Matches(text))
                            {
                                var p = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                                if (File.Exists(p) && (InSuspiciousDir(p) || IsOnRemovable(p)) && ScriptOrHidden(p)) extra.Add(p);
                            }
                        }
                        catch { }
                    }
                    QuarantineFile(r, f, "startup folder");
                    r.Details.Add(L.F("pc_startup_removed", Path.GetFileName(f)));
                    foreach (var e in extra.Distinct()) QuarantineFile(r, e, "startup target");
                }
            }
        }

        // ------------------------------------------------------------------
        // 4) المهام المجدولة (Task Scheduler COM)
        // ------------------------------------------------------------------
        static void CleanScheduledTasks(PcResult r)
        {
            var t = Type.GetTypeFromProgID("Schedule.Service");
            if (t == null) return;
            var svc = Activator.CreateInstance(t);
            Call(svc, "Connect");
            var root = Call(svc, "GetFolder", "\\");
            WalkTaskFolder(root, r, 0);
        }

        static void WalkTaskFolder(object folder, PcResult r, int depth)
        {
            if (folder == null || depth > 8) return;
            var path = Get(folder, "Path") as string ?? "";
            if (path.StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase)) return;

            var tasks = Call(folder, "GetTasks", 1); // TASK_ENUM_HIDDEN
            int count = Convert.ToInt32(Get(tasks, "Count"));
            var toDelete = new List<(string name, List<string> files)>();
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    var task = GetIndexed(tasks, i);
                    var name = Get(task, "Name") as string;
                    if (string.Equals(name, SystemTools.TaskName, StringComparison.OrdinalIgnoreCase)) continue;
                    var def = Get(task, "Definition");
                    var actions = Get(def, "Actions");
                    int ac = Convert.ToInt32(Get(actions, "Count"));
                    for (int j = 1; j <= ac; j++)
                    {
                        var a = GetIndexed(actions, j);
                        if (Convert.ToInt32(Get(a, "Type")) != 0) continue; // TASK_ACTION_EXEC
                        var cmd = $"\"{Get(a, "Path")}\" {Get(a, "Arguments")}";
                        if (IsSuspiciousCommand(cmd, out var files)) { toDelete.Add((name, files)); break; }
                    }
                }
                catch { }
            }
            foreach (var (name, files) in toDelete)
            {
                try
                {
                    Call(folder, "DeleteTask", name, 0);
                    r.Threats++;
                    r.Details.Add(L.F("pc_task_removed", name));
                    Settings.Log($"  [حذف مهمة مجدولة] {path}\\{name}");
                    foreach (var f in files) QuarantineFile(r, f, "scheduled task: " + name);
                }
                catch { }
            }

            var subs = Call(folder, "GetFolders", 0);
            int sc = Convert.ToInt32(Get(subs, "Count"));
            for (int i = 1; i <= sc; i++)
            {
                try { WalkTaskFolder(GetIndexed(subs, i), r, depth + 1); } catch { }
            }
        }

        // ------------------------------------------------------------------
        // 5) Winlogon و IFEO (حيل تشغيل وحجب برامج الحماية)
        // ------------------------------------------------------------------
        static void CleanWinlogon(PcResult r)
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var k = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true);
            if (k == null) return;

            var shell = (k.GetValue("Shell") as string ?? "").Trim();
            if (!string.IsNullOrEmpty(shell) && !shell.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in FileRef.Matches(shell))
                {
                    var f = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (!Path.GetFileName(f).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) QuarantineFile(r, f, "winlogon shell");
                }
                k.SetValue("Shell", "explorer.exe");
                r.Threats++;
                r.Details.Add(L.T("pc_winlogon"));
                Settings.Log($"  [تصليح Winlogon Shell] كان: {shell}");
            }

            var userinit = (k.GetValue("Userinit") as string ?? "").Trim();
            var good = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\system32\userinit.exe,");
            var parts = userinit.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
            var extras = parts.Where(x => !Path.GetFileName(x).Equals("userinit.exe", StringComparison.OrdinalIgnoreCase)).ToList();
            if (extras.Count > 0 && extras.Any(x => IsSuspiciousCommand(x, out _) || InSuspiciousDir(x.Trim('"'))))
            {
                foreach (var x in extras) if (File.Exists(x.Trim('"'))) QuarantineFile(r, x.Trim('"'), "winlogon userinit");
                k.SetValue("Userinit", good);
                r.Threats++;
                r.Details.Add(L.T("pc_winlogon"));
                Settings.Log($"  [تصليح Winlogon Userinit] كان: {userinit}");
            }
        }

        static void CleanIfeo(PcResult r)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var ifeo = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", true);
                if (ifeo == null) continue;
                foreach (var sub in ifeo.GetSubKeyNames())
                {
                    try
                    {
                        using var k = ifeo.OpenSubKey(sub, true);
                        var dbg = k?.GetValue("Debugger") as string;
                        if (string.IsNullOrWhiteSpace(dbg)) continue;
                        var low = dbg.ToLowerInvariant();
                        if (low.Contains("procexp") || low.Contains("vsjitdebugger") || low.Contains("windbg") || low.Contains("devenv")) continue;
                        bool isProtected = ProtectedExe.Contains(sub.ToLowerInvariant());
                        if (isProtected || IsSuspiciousCommand(dbg, out _))
                        {
                            k.DeleteValue("Debugger", false);
                            r.Threats++;
                            r.Details.Add(L.F("pc_ifeo", sub));
                            Settings.Log($"  [IFEO] رفع الحجب عن {sub} (Debugger={dbg})");
                        }
                    }
                    catch { }
                }
            }
        }

        // ------------------------------------------------------------------
        // 6) autorun.inf بجذر اقراص الحاسبة (Sality ينشر نفسه هيچ)
        // ------------------------------------------------------------------
        static void CleanFixedDriveRoots(PcResult r)
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    var inf = Path.Combine(d.RootDirectory.FullName, "autorun.inf");
                    if (!File.Exists(inf)) continue;
                    foreach (var target in Scanner.AutorunTargets(inf, d.RootDirectory.FullName))
                    {
                        // بالقرص الثابت نعزل الهدف فقط اذا مخفي او بجذر القرص مباشرة
                        if (Path.GetDirectoryName(target).TrimEnd('\\') == d.RootDirectory.FullName.TrimEnd('\\') || IsHiddenSystem(target))
                            QuarantineFile(r, target, "drive autorun target");
                    }
                    QuarantineFile(r, inf, "drive autorun.inf");
                    r.Details.Add(L.F("pc_drive_autorun", d.Name));
                }
                catch { }
            }
        }

        // ------------------------------------------------------------------
        // 7) علامات Sality (ما نگدر نصلّح الملفات الملوّثة، بس ننبّه)
        // ------------------------------------------------------------------
        static void CheckSality(PcResult r)
        {
            int signs = 0;
            try
            {
                using var sb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SafeBoot");
                if (sb == null || sb.OpenSubKey("Minimal") == null || sb.OpenSubKey("Network") == null)
                {
                    signs += 2;
                    r.Warnings.Add(L.T("sality_safeboot"));
                }
            }
            catch { }
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                if (k?.GetValue("EnableLUA") is int lua && lua == 0) { signs++; r.Warnings.Add(L.T("sality_uac")); }
            }
            catch { }
            try
            {
                // Sality يسجل مفاتيح بهيئة HKCU\Software\<random>\-993627007 ...
                using var k = Registry.CurrentUser.OpenSubKey(@"Software");
                foreach (var n in k.GetSubKeyNames())
                {
                    using var s = k.OpenSubKey(n);
                    if (s == null) continue;
                    if (s.GetValueNames().Count(v => Regex.IsMatch(v, @"^-?\d{9,10}$")) >= 3) { signs += 2; r.Warnings.Add(L.T("sality_reg")); break; }
                }
            }
            catch { }
            r.SalitySuspected = signs >= 2;
            if (r.SalitySuspected) Settings.Log("  [تحذير] علامات اصابة Sality");
        }

        // ==================================================================
        //  مساعدات
        // ==================================================================
        static void QuarantineFile(PcResult r, string path, string reason)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                if (IsSystemProtected(path)) return;
                if (Scanner.Remove(path, reason, "PC"))
                {
                    if (!r.Details.Contains(Path.GetFileName(path))) r.Details.Add(Path.GetFileName(path));
                }
            }
            catch { }
        }

        static bool IsSystemProtected(string path)
        {
            var full = Path.GetFullPath(path).ToLowerInvariant();
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant().TrimEnd('\\') + "\\";
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLowerInvariant().TrimEnd('\\') + "\\";
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToLowerInvariant().TrimEnd('\\') + "\\";
            // ما نمس ملفات ويندوز او البرامج المثبتة
            return full.StartsWith(win) || (pf.Length > 1 && full.StartsWith(pf)) || (pf86.Length > 1 && full.StartsWith(pf86));
        }

        /// <summary>
        /// الأمر مشبوه اذا: يشغّل سكربت (vbs/js/hta) من مكان مشبوه او من فلاشة،
        /// او يشغّل ملف من فلاشة، او ملف مخفي+نظام من مكان مشبوه.
        /// </summary>
        public static bool IsSuspiciousCommand(string cmd, out List<string> files)
        {
            files = new List<string>();
            if (string.IsNullOrWhiteSpace(cmd)) return false;
            cmd = Environment.ExpandEnvironmentVariables(cmd);
            if (cmd.IndexOf("usbguard", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            bool usesHost = ScriptHost.IsMatch(cmd) || Regex.IsMatch(cmd, @"\bcmd(\.exe)?\b.*\b(start|/c)\b.*\.(vbs|vbe|js|jse|wsf|hta)\b", RegexOptions.IgnoreCase);
            bool bad = false;

            foreach (Match m in FileRef.Matches(cmd))
            {
                var p = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
                var ext = Path.GetExtension(p).ToLowerInvariant();
                bool isScript = ScriptExt.Contains(ext);
                bool onUsb = IsOnRemovable(p);
                bool inSus = InSuspiciousDir(p);
                bool hidden = File.Exists(p) && IsHiddenSystem(p);

                if (onUsb) { bad = true; files.Add(p); }
                else if (isScript && (inSus || hidden)) { bad = true; files.Add(p); }
                else if (!isScript && inSus && hidden) { bad = true; files.Add(p); }
                else if (isScript && usesHost && hidden) { bad = true; files.Add(p); }
            }
            files = files.Where(File.Exists).Distinct().ToList();
            return bad;
        }

        static bool ScriptOrHidden(string p) => ScriptExt.Contains(Path.GetExtension(p).ToLowerInvariant()) || IsHiddenSystem(p);

        static bool InSuspiciousDir(string p)
        {
            try
            {
                var full = Path.GetFullPath(p.Trim('"'));
                return SuspiciousDirs.Any(d => full.StartsWith(d, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        static bool IsOnRemovable(string p)
        {
            try
            {
                if (p.Length < 3 || p[1] != ':') return false;
                return new DriveInfo(p.Substring(0, 3)).DriveType == DriveType.Removable;
            }
            catch { return false; }
        }

        static bool IsHiddenSystem(string p)
        {
            try
            {
                var a = File.GetAttributes(p);
                return a.HasFlag(FileAttributes.Hidden) && a.HasFlag(FileAttributes.System);
            }
            catch { return false; }
        }

        // COM late-binding helpers (Task Scheduler)
        static object Call(object o, string method, params object[] args) =>
            o.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, o, args);
        static object Get(object o, string prop) =>
            o.GetType().InvokeMember(prop, BindingFlags.GetProperty, null, o, null);
        static object GetIndexed(object o, int index) =>
            o.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, o, new object[] { index });
    }
}

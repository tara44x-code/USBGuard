using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace USBGuard
{
    // =====================================================================
    //  مدير برامج بدء التشغيل (نفس طريقة مدير المهام بويندوز: StartupApproved)
    // =====================================================================
    public class StartupEntry
    {
        public string Name, Command, Location;
        public bool Enabled;
        internal RegistryHive Hive;
        internal string ApprovedKey;
    }

    public static class StartupManager
    {
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static List<StartupEntry> List()
        {
            var list = new List<StartupEntry>();

            void FromKey(RegistryHive hive, RegistryView view, string approvedSub, string label)
            {
                try
                {
                    using var bk = RegistryKey.OpenBaseKey(hive, view);
                    using var k = bk.OpenSubKey(RunKey);
                    if (k == null) return;
                    foreach (var n in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(n)) continue;
                        var cmd = k.GetValue(n) as string;
                        if (cmd == null || list.Any(e => e.Name == n && e.Location == label)) continue;
                        list.Add(new StartupEntry
                        {
                            Name = n, Command = cmd, Location = label, Hive = hive,
                            ApprovedKey = Approved + approvedSub,
                            Enabled = IsEnabled(hive, Approved + approvedSub, n),
                        });
                    }
                }
                catch { }
            }

            void FromFolder(string dir, RegistryHive hive, string label)
            {
                try
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        var name = Path.GetFileName(f);
                        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(new StartupEntry
                        {
                            Name = name, Command = f, Location = label, Hive = hive,
                            ApprovedKey = Approved + "StartupFolder",
                            Enabled = IsEnabled(hive, Approved + "StartupFolder", name),
                        });
                    }
                }
                catch { }
            }

            FromKey(RegistryHive.CurrentUser, RegistryView.Registry64, "Run", L.Ar ? "المستخدم" : "User");
            FromKey(RegistryHive.LocalMachine, RegistryView.Registry64, "Run", L.Ar ? "كل المستخدمين" : "All users");
            FromKey(RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", L.Ar ? "كل المستخدمين (32bit)" : "All users (32-bit)");
            FromFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, L.Ar ? "مجلد بدء التشغيل" : "Startup folder");
            FromFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, L.Ar ? "مجلد بدء التشغيل العام" : "Common Startup folder");
            return list.OrderByDescending(e => e.Enabled).ThenBy(e => e.Name).ToList();
        }

        static bool IsEnabled(RegistryHive hive, string key, string name)
        {
            try
            {
                using var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var k = bk.OpenSubKey(key);
                if (!(k?.GetValue(name) is byte[] v) || v.Length == 0) return true;
                return (v[0] & 1) == 0; // 02/06 = شغال ، 03/07 = معطّل
            }
            catch { return true; }
        }

        public static bool SetEnabled(StartupEntry e, bool enable)
        {
            try
            {
                using var bk = RegistryKey.OpenBaseKey(e.Hive, RegistryView.Registry64);
                using var k = bk.CreateSubKey(e.ApprovedKey);
                var data = new byte[12];
                data[0] = (byte)(enable ? 2 : 3);
                if (!enable) Array.Copy(BitConverter.GetBytes(DateTime.Now.ToFileTime()), 0, data, 4, 8);
                k.SetValue(e.Name, data, RegistryValueKind.Binary);
                e.Enabled = enable;
                Settings.Log($"بدء التشغيل: {(enable ? "تفعيل" : "تعطيل")} {e.Name}");
                return true;
            }
            catch { return false; }
        }
    }

    // =====================================================================
    //  منظّف الملفات المؤقتة
    // =====================================================================
    public static class JunkCleaner
    {
        static IEnumerable<string> Dirs()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return Path.GetTempPath();
            yield return Path.Combine(win, "Temp");
            yield return Path.Combine(local, @"Microsoft\Windows\INetCache");
            yield return Path.Combine(local, "CrashDumps");
            yield return Path.Combine(local, @"Microsoft\Windows\WER\ReportArchive");
            foreach (var browser in new[] { @"Google\Chrome", @"Microsoft\Edge", @"BraveSoftware\Brave-Browser" })
            {
                var ud = Path.Combine(local, browser, "User Data");
                if (!Directory.Exists(ud)) continue;
                string[] profiles;
                try { profiles = Directory.GetDirectories(ud); } catch { continue; }
                foreach (var prof in profiles)
                {
                    yield return Path.Combine(prof, @"Cache\Cache_Data");
                    yield return Path.Combine(prof, "Code Cache");
                    yield return Path.Combine(prof, "GPUCache");
                }
            }
        }

        static IEnumerable<FileInfo> Files(DateTime olderThan)
        {
            foreach (var d in Dirs().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(d)) continue;
                var stack = new Stack<DirectoryInfo>();
                stack.Push(new DirectoryInfo(d));
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    FileSystemInfo[] items;
                    try { items = cur.GetFileSystemInfos(); } catch { continue; }
                    foreach (var i in items)
                    {
                        if (i is DirectoryInfo sd) { if ((sd.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(sd); }
                        else if (i is FileInfo fi && fi.LastWriteTime < olderThan) yield return fi;
                    }
                }
            }
        }

        public static (long bytes, int files) Measure()
        {
            long b = 0; int n = 0;
            foreach (var f in Files(DateTime.Now.AddHours(-2))) { try { b += f.Length; n++; } catch { } }
            return (b, n);
        }

        public static (long bytes, int files) Clean()
        {
            long b = 0; int n = 0;
            foreach (var f in Files(DateTime.Now.AddHours(-2)).ToList())
            {
                try
                {
                    long len = f.Length;
                    if (f.IsReadOnly) f.IsReadOnly = false;
                    f.Delete();
                    b += len; n++;
                }
                catch { } // ملف مستخدم حالياً
            }
            // المجلدات الفارغة
            foreach (var d in Dirs().Where(Directory.Exists))
            {
                try
                {
                    foreach (var sub in Directory.GetDirectories(d, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                        try { if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub); } catch { }
                }
                catch { }
            }
            Settings.Log($"تنظيف الملفات المؤقتة: {n} ملف ({b / 1048576.0:0.#} MB)");
            return (b, n);
        }
    }

    // =====================================================================
    //  فحص ملف hosts (تحويل مواقع لمواقع مزيفة او حجب برامج الحماية)
    // =====================================================================
    public static class HostsCheck
    {
        public static string HostsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

        static readonly string[] Popular =
        {
            "google.", "gmail.", "youtube.", "facebook.", "fb.com", "instagram.", "whatsapp.", "telegram.", "t.me",
            "microsoft.", "live.com", "outlook.", "office.com", "bing.", "windowsupdate.", "apple.", "icloud.",
            "paypal.", "yahoo.", "twitter.", "x.com", "tiktok.", "snapchat.", "amazon.", "github.", "binance.",
        };
        static readonly string[] SecurityDomains =
        {
            "kaspersky", "avast", "avg.", "eset", "mcafee", "symantec", "norton", "malwarebytes", "bitdefender",
            "virustotal", "avira", "sophos", "trendmicro", "drweb", "f-secure", "panda", "windowsupdate.",
            "wdcp.microsoft", "wd.microsoft", "definitionupdates", "update.microsoft",
        };

        static bool IsLoopback(string ip) => ip == "127.0.0.1" || ip == "0.0.0.0" || ip == "::1" || ip == "::" || ip.StartsWith("127.");

        /// <summary>يرجع الاسطر المشبوهة (رقم السطر، السبب)</summary>
        public static List<(int line, string text)> Find()
        {
            var bad = new List<(int, string)>();
            try
            {
                var lines = File.ReadAllLines(HostsPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    var t = lines[i].Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    var hash = t.IndexOf('#');
                    if (hash > 0) t = t.Substring(0, hash).Trim();
                    var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    var ip = parts[0];
                    var hosts = parts.Skip(1).Select(h => h.ToLowerInvariant()).ToList();
                    bool redirectPopular = !IsLoopback(ip) && hosts.Any(h => Popular.Any(p => h.Contains(p)));
                    bool blocksSecurity = hosts.Any(h => SecurityDomains.Any(p => h.Contains(p)));
                    if (redirectPopular || blocksSecurity) bad.Add((i, lines[i].Trim()));
                }
            }
            catch { }
            return bad;
        }

        /// <summary>يعطّل الاسطر المشبوهة (يحطلها #) بعد ما ياخذ نسخة احتياطية</summary>
        public static int Fix()
        {
            var bad = Find();
            if (bad.Count == 0) return 0;
            try
            {
                var path = HostsPath;
                File.Copy(path, Path.Combine(Settings.DataDir, $"hosts.backup_{DateTime.Now:yyyyMMdd_HHmmss}"), true);
                var lines = File.ReadAllLines(path);
                foreach (var (i, _) in bad) lines[i] = "# [USB Guard disabled] " + lines[i];
                File.SetAttributes(path, FileAttributes.Normal);
                File.WriteAllLines(path, lines, Encoding.ASCII);
                SystemTools.RunHidden("ipconfig.exe", "/flushdns");
                Settings.Log($"ملف hosts: تعطيل {bad.Count} سطر مشبوه");
                return bad.Count;
            }
            catch (Exception ex)
            {
                Settings.Log("فشل تصليح hosts: " + ex.Message);
                return 0;
            }
        }
    }

    // =====================================================================
    //  الحماية من الفدية (Controlled Folder Access بـ Windows Defender)
    // =====================================================================
    public static class Ransomware
    {
        static string Ps(string cmd)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + cmd.Replace("\"", "\\\"") + "\"")
                {
                    CreateNoWindow = true, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                });
                var o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(20000);
                return p.ExitCode == 0 ? o.Trim() : null;
            }
            catch { return null; }
        }

        /// <summary>true شغال، false مطفّي، null اذا Defender مو متوفر</summary>
        public static bool? IsEnabled()
        {
            var o = Ps("(Get-MpPreference).EnableControlledFolderAccess");
            if (string.IsNullOrEmpty(o)) return null;
            return o.Trim() == "1";
        }

        public static bool Set(bool on)
        {
            var exe = SystemTools.IsRunningInstalled ? SystemTools.InstalledExe : SystemTools.CurrentExe;
            var cmd = on
                ? $"Add-MpPreference -ControlledFolderAccessAllowedApplications '{exe.Replace("'", "''")}'; Set-MpPreference -EnableControlledFolderAccess Enabled"
                : "Set-MpPreference -EnableControlledFolderAccess Disabled";
            var ok = Ps(cmd + "; 'ok'") != null;
            Settings.Log($"الحماية من الفدية: {(on ? "تشغيل" : "اطفاء")} {(ok ? "✓" : "✗")}");
            return ok && IsEnabled() == on;
        }
    }

    // =====================================================================
    //  النسخة المحمولة على الفلاشة
    // =====================================================================
    public static class Portable
    {
        public static string Copy(string usbRoot)
        {
            var dir = Path.Combine(usbRoot, "USB Guard");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, "USBGuard.exe");
            File.Copy(SystemTools.CurrentExe, dest, true);
            File.WriteAllText(Path.Combine(dir, "README.txt"),
                "USB Guard - Portable\r\n" +
                "شغّل USBGuard.exe على اي حاسبة حتى تفحصها وتفحص الفلاشة بدون تثبيت.\r\n" +
                "Run USBGuard.exe on any PC to scan it and this drive without installing.\r\n\r\n" +
                "Developer: Muhannad Sat Link - " + SystemTools.DeveloperUrl + "\r\n", Encoding.UTF8);
            Settings.Log("نسخة محمولة: " + dest);
            return dest;
        }
    }
}

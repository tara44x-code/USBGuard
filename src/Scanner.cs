using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace USBGuard
{
    public class ScanResult
    {
        public string Drive;
        public int Threats;          // عناصر خبيثة محذوفة
        public int Restored;         // ملفات/مجلدات رجعت ظاهرة
        public bool DefenderRan;
        public bool DefenderFound;
        public string Error;
        public readonly List<string> Details = new List<string>();
        public bool HadThreats => Threats > 0 || DefenderFound;
    }

    /// <summary>محرك فحص وتنظيف الفلاشة</summary>
    public static class Scanner
    {
        // اوامر يستخدمها فايروس الاختصار داخل ملف .lnk
        static readonly Regex BadLnk = new Regex(
            @"cmd\.exe|wscript|cscript|powershell|rundll32|forfiles|mshta|regsvr32|\.vbs|\.vbe|\.jse?\b|\.wsf|\.bat\b|\.cmd\b|\.scr\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly string[] ExecExt = { ".exe", ".vbs", ".vbe", ".bat", ".cmd", ".scr", ".pif", ".com", ".js", ".jse", ".wsf", ".dll" };

        // اسماء فايروسات فلاشة معروفة (gamez وغيرها)
        static readonly string[] KnownBad =
        {
            "gamez.exe", "gamez.lnk", "gamez.vbs", "gamez", "games.exe",
            "autorun.exe", "autorun.bat", "autorun.vbs", "autorun.cmd",
            "thumbs.exe", "desktop.exe", "system.exe", "svchost.exe", "winlogon.exe",
            "csrss.exe", "lsass.exe", "services.exe", "explorer.exe", "smss.exe",
            "driveguard.exe", "usbdriver.exe", "windowsservices", "windowsservices.exe",
            "indexervolumeguid.vbs", "copy of shortcut to.lnk", "new folder.exe",
            "recycler", "recycled", "autorun", "ravmon.exe", "ntdelect.com", "kavo.exe",
        };

        static readonly string[] Protected = { "system volume information", "$recycle.bin", "autorun.inf" };

        public static bool IsScannable(string root, out string reason)
        {
            reason = null;
            try
            {
                var di = new DriveInfo(root);
                if (di.DriveType != DriveType.Removable) { reason = L.T("not_removable"); return false; }
                if (!di.IsReady) { reason = L.T("not_ready"); return false; }
                return true;
            }
            catch { reason = L.T("not_ready"); return false; }
        }

        public static string NormalizeRoot(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return null;
            p = p.Trim().Trim('"');
            if (p.Length >= 2 && p[1] == ':') return char.ToUpperInvariant(p[0]) + @":\";
            return null;
        }

        public static ScanResult Scan(string root, bool deep, IProgress<(int pct, string msg)> progress)
        {
            var r = new ScanResult { Drive = root };
            void P(int pct, string key) => progress?.Report((pct, L.T(key)));

            try
            {
                Settings.Log($"===== بدء فحص {root} =====");
                P(3, "step_prepare");

                // 1) autorun.inf
                P(10, "step_autorun");
                var auto = Path.Combine(root, "autorun.inf");
                if (File.Exists(auto))
                {
                    foreach (var target in AutorunTargets(auto, root))
                        if (Remove(target, "autorun target")) Hit(r, Path.GetFileName(target));
                    if (Remove(auto, "autorun.inf")) Hit(r, "autorun.inf");
                }

                // 2) ملفات تنفيذية مخفية+نظام بجذر الفلاشة (قبل ما نظهر الملفات!)
                P(20, "step_exe");
                foreach (var f in SafeFiles(root))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (!ExecExt.Contains(ext)) continue;
                    var a = SafeAttr(f);
                    if (a.HasFlag(FileAttributes.Hidden) && a.HasFlag(FileAttributes.System))
                        if (Remove(f, "hidden executable")) Hit(r, Path.GetFileName(f));
                }

                // 3) اختصارات فايروس الشورتكت (بالجذر والمستوى الاول)
                P(32, "step_lnk");
                var lnkDirs = new List<string> { root };
                lnkDirs.AddRange(SafeDirs(root));
                foreach (var d in lnkDirs)
                    foreach (var f in SafeFiles(d, "*.lnk"))
                        if (IsMaliciousLnk(f) && Remove(f, "shortcut virus")) Hit(r, f.Substring(root.Length));

                // 4) المجلد بلا اسم (الفلاشة تطلع فارغة)
                P(45, "step_unhide");
                foreach (var d in SafeDirs(root))
                {
                    var name = Path.GetFileName(d);
                    if (IsBlankName(name))
                    {
                        SetAttr(d, FileAttributes.Directory);
                        foreach (var child in SafeEntries(d))
                        {
                            var dest = Path.Combine(root, Path.GetFileName(child));
                            if (File.Exists(dest) || Directory.Exists(dest)) continue;
                            try
                            {
                                if (Directory.Exists(child)) Directory.Move(child, dest); else File.Move(child, dest);
                                r.Restored++;
                            }
                            catch { }
                        }
                        try { if (!SafeEntries(d).Any()) { Directory.Delete(d, false); Hit(r, "[blank folder]"); } } catch { }
                    }
                }

                // 5) اعادة اظهار كل الملفات المخفية (مع الحفاظ على مجلدات النظام)
                r.Restored += UnhideAll(root);

                // 6) فايروسات معروفة + exe منتحل اسم مجلد
                P(60, "step_known");
                foreach (var e in SafeEntries(root))
                {
                    var n = Path.GetFileName(e).ToLowerInvariant();
                    if (KnownBad.Contains(n))
                    {
                        if (Remove(e, "known USB malware")) Hit(r, Path.GetFileName(e));
                        continue;
                    }
                    var ext = Path.GetExtension(n);
                    if ((ext == ".exe" || ext == ".scr" || ext == ".com") && File.Exists(e))
                    {
                        var twin = Path.Combine(root, Path.GetFileNameWithoutExtension(e));
                        if (Directory.Exists(twin) && Remove(e, "fake folder exe")) Hit(r, Path.GetFileName(e));
                    }
                }

                // 7) تلقيح الفلاشة: مجلد autorun.inf محمي يمنع الفايروس يسوي ملف بنفس الاسم
                P(72, "step_vaccine");
                Vaccinate(root);

                // 8) فحص عميق بـ Windows Defender
                if (deep)
                {
                    P(78, "step_defender");
                    var code = RunDefender(root);
                    if (code >= 0)
                    {
                        r.DefenderRan = true;
                        r.DefenderFound = code == 2;
                        if (r.DefenderFound) { r.Details.Add("Windows Defender"); Settings.Log("  Defender لگى تهديدات وعالجها"); }
                    }
                }

                P(100, "step_done");
                Settings.Log($"===== انتهى فحص {root}: تهديدات={r.Threats} مسترجع={r.Restored} =====");
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                Settings.Log("خطأ: " + ex.Message);
            }
            return r;
        }

        // ------------------------------------------------------------------
        static void Hit(ScanResult r, string what)
        {
            r.Threats++;
            r.Details.Add(what);
            Settings.Log("  [حذف] " + what);
        }

        static bool IsBlankName(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            foreach (var ch in name)
            {
                if (char.IsWhiteSpace(ch) || ch == '_' || ch == ' ' || ch == '​' || ch == '‌' ||
                    ch == '‍' || ch == ' ' || ch == ' ' || ch == '﻿' || ch == 'ㅤ' ||
                    char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.Format)
                    continue;
                return false;
            }
            return true;
        }

        public static bool IsMaliciousLnk(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > 64 * 1024) return false;
                var bytes = File.ReadAllBytes(path);
                var text = Encoding.Unicode.GetString(bytes) + "\n" + Encoding.ASCII.GetString(bytes);
                return BadLnk.IsMatch(text);
            }
            catch { return false; }
        }

        static int UnhideAll(string root)
        {
            int count = 0;
            var stack = new Stack<string>();
            stack.Push(root);
            int guard = 0;
            while (stack.Count > 0 && guard++ < 200000)
            {
                var dir = stack.Pop();
                foreach (var e in SafeEntries(dir))
                {
                    var n = Path.GetFileName(e).ToLowerInvariant();
                    if (dir == root && Protected.Contains(n)) continue;
                    var a = SafeAttr(e);
                    if (a.HasFlag(FileAttributes.Hidden) || a.HasFlag(FileAttributes.System))
                    {
                        SetAttr(e, a & ~(FileAttributes.Hidden | FileAttributes.System));
                        count++;
                    }
                    if (a.HasFlag(FileAttributes.Directory) && !a.HasFlag(FileAttributes.ReparsePoint))
                        stack.Push(e);
                }
            }
            if (count > 0) Settings.Log($"  [استرجاع] {count} ملف/مجلد مخفي");
            return count;
        }

        static void Vaccinate(string root)
        {
            try
            {
                var v = Path.Combine(root, "autorun.inf");
                if (File.Exists(v)) Delete(v);
                if (!Directory.Exists(v)) Directory.CreateDirectory(v);
                var inner = Path.Combine(v, "USBGuard-Protected");
                if (!Directory.Exists(inner)) Directory.CreateDirectory(inner);
                File.SetAttributes(v, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
            }
            catch { }
        }

        static int RunDefender(string root)
        {
            try
            {
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var mp = Path.Combine(pf, @"Windows Defender\MpCmdRun.exe");
                if (!File.Exists(mp)) return -1;
                var psi = new ProcessStartInfo(mp, $"-Scan -ScanType 3 -File {root}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using var p = Process.Start(psi);
                if (!p.WaitForExit(30 * 60 * 1000)) { try { p.Kill(); } catch { } return -1; }
                return p.ExitCode; // 0 = نظيف, 2 = لگى تهديدات
            }
            catch { return -1; }
        }

        /// <summary>يعزل بالحجر الصحي، واذا ما گدر يحذف</summary>
        public static bool Remove(string path, string reason, string source = "USB")
        {
            if (Quarantine.Add(path, reason, source)) return true;
            return Delete(path);
        }

        /// <summary>يقرأ autorun.inf ويرجع الملفات الي يشغلها (open= / shellexecute= / command=)</summary>
        public static List<string> AutorunTargets(string infPath, string root)
        {
            var list = new List<string>();
            try
            {
                var fi = new FileInfo(infPath);
                if (fi.Length > 64 * 1024) return list;
                foreach (var raw in File.ReadAllLines(infPath))
                {
                    var line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    if (!(key == "open" || key == "shellexecute" || key.EndsWith("\\command"))) continue;
                    var val = line.Substring(eq + 1).Trim().Trim('"');
                    // اول كلمة = اسم الملف
                    var m = Regex.Match(val, @"^\s*""?([^""]+?\.(exe|com|scr|pif|bat|cmd|vbs|vbe|js|jse|wsf))", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    var file = m.Groups[1].Value.Trim();
                    var full = Path.IsPathRooted(file) ? file : Path.Combine(root, file);
                    if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full) && !list.Contains(full))
                        list.Add(full);
                }
            }
            catch { }
            return list;
        }

        // ---------------- filesystem helpers ----------------
        static bool Delete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (var e in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
                        SetAttr(e, Directory.Exists(e) ? FileAttributes.Directory : FileAttributes.Normal);
                    SetAttr(path, FileAttributes.Directory);
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path))
                {
                    SetAttr(path, FileAttributes.Normal);
                    File.Delete(path);
                }
                return true;
            }
            catch (Exception ex)
            {
                Settings.Log($"  [فشل الحذف] {path}: {ex.Message}");
                return false;
            }
        }

        static FileAttributes SafeAttr(string p) { try { return File.GetAttributes(p); } catch { return FileAttributes.Normal; } }
        static void SetAttr(string p, FileAttributes a) { try { File.SetAttributes(p, a); } catch { } }

        static IEnumerable<string> SafeEntries(string d)
        {
            try { return Directory.GetFileSystemEntries(d); } catch { return new string[0]; }
        }
        static IEnumerable<string> SafeFiles(string d, string pattern = "*")
        {
            try { return Directory.GetFiles(d, pattern); } catch { return new string[0]; }
        }
        static IEnumerable<string> SafeDirs(string d)
        {
            try { return Directory.GetDirectories(d); } catch { return new string[0]; }
        }
    }
}

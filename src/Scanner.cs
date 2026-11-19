using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

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
        public bool Cancelled;
        public long FilesChecked;
        public readonly List<string> DefenderThreats = new List<string>();
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

        static readonly string[] ScriptExt = { ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta" };
        static readonly string[] BinExt = { ".exe", ".scr", ".pif", ".com" };

        // نسخة صارمة لكشف فايروس الاختصار على الهارد (حتى ما نمس اختصارات ويندوز الطبيعية مثل PowerShell)
        static readonly Regex StrictLnk = new Regex(
            @"wscript|cscript|mshta|\.vbs\b|\.vbe\b|\.jse?\b|\.wsf\b|\.hta\b|cmd(\.exe)?\W{1,6}/c\W{1,6}start\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>يوحّد المسار: "e:" → "E:\" ، ويشيل \ من آخر المجلدات</summary>
        public static string NormalizePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return null;
            p = p.Trim().Trim('"').Trim();
            if (p.Length == 2 && p[1] == ':') p += "\\";
            try { p = Path.GetFullPath(p); } catch { return null; }
            if (p.Length > 3) p = p.TrimEnd('\\');
            if (p.Length == 3 && p[1] == ':') p = char.ToUpperInvariant(p[0]) + @":\";
            return p;
        }

        public static bool IsDriveRoot(string p) => p != null && p.Length == 3 && p[1] == ':';

        public static bool IsRemovableRoot(string p)
        {
            try { return IsDriveRoot(p) && new DriveInfo(p).DriveType == DriveType.Removable; } catch { return false; }
        }

        public static bool CanScan(string p, out string reason)
        {
            reason = null;
            try
            {
                if (IsDriveRoot(p))
                {
                    var di = new DriveInfo(p);
                    if (!di.IsReady) { reason = L.T("not_ready"); return false; }
                    return true;
                }
                if (File.Exists(p) || Directory.Exists(p)) return true;
                reason = L.T("path_missing");
                return false;
            }
            catch { reason = L.T("not_ready"); return false; }
        }

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

        public static ScanResult Scan(string root, bool deep, IProgress<(int pct, string msg)> progress, CancellationToken ct = default)
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
                    ApplyDefender(r, DefenderChunked(root, ct, progress, 78, 99, r.DefenderThreats));
                }

                P(100, "step_done");
                Settings.Log($"===== انتهى فحص {root}: تهديدات={r.Threats} مسترجع={r.Restored} =====");
            }
            catch (OperationCanceledException) { r.Cancelled = true; Settings.Log("  تم ايقاف الفحص"); }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                Settings.Log("خطأ: " + ex.Message);
            }
            return r;
        }

        // ==================================================================
        //  فحص اي هارد / مجلد / ملف (بدون تعديلات الفلاشة الخطرة مثل اظهار كل الملفات)
        // ==================================================================
        public static ScanResult ScanPath(string path, bool deep, IProgress<(int pct, string msg)> progress, CancellationToken ct)
        {
            var r = new ScanResult { Drive = path };
            try
            {
                Settings.Log($"===== بدء فحص {path} =====");
                progress?.Report((-1, L.T("step_prepare")));

                // autorun.inf بجذر القرص
                if (IsDriveRoot(path))
                {
                    var inf = Path.Combine(path, "autorun.inf");
                    if (File.Exists(inf))
                    {
                        foreach (var t in AutorunTargets(inf, path))
                            if ((Path.GetDirectoryName(t).TrimEnd('\\') + "\\").Equals(path, StringComparison.OrdinalIgnoreCase) || IsHiddenSys(SafeAttr(t)))
                                if (Remove(t, "autorun target", "HDD")) Hit(r, Path.GetFileName(t));
                        if (Remove(inf, "autorun.inf", "HDD")) Hit(r, "autorun.inf");
                    }
                }

                if (Directory.Exists(path))
                {
                    var stack = new Stack<DirectoryInfo>();
                    stack.Push(new DirectoryInfo(path));
                    long count = 0;
                    var lastReport = Environment.TickCount;
                    bool first = true; // المجلد الي اختاره المستخدم نفحصه حتى لو من المستثنيات
                    while (stack.Count > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        var d = stack.Pop();
                        if (!first && IsExcludedDir(d.FullName)) continue;
                        first = false;
                        FileSystemInfo[] entries;
                        try { entries = d.GetFileSystemInfos(); } catch { continue; }
                        foreach (var e in entries)
                        {
                            if (e is DirectoryInfo sub)
                            {
                                if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(sub);
                                continue;
                            }
                            count++;
                            if (e is FileInfo fi) CheckFile(fi, r);
                            if (Environment.TickCount - lastReport > 400)
                            {
                                lastReport = Environment.TickCount;
                                progress?.Report((-1, L.F("step_files", count.ToString("N0"))));
                            }
                        }
                    }
                    r.FilesChecked = count;
                }
                else if (File.Exists(path))
                {
                    r.FilesChecked = 1;
                    CheckFile(new FileInfo(path), r);
                }

                if (deep)
                {
                    ApplyDefender(r, DefenderChunked(path, ct, progress, 6, 99, r.DefenderThreats));
                }
                progress?.Report((100, L.T("step_done")));
                Settings.Log($"===== انتهى فحص {path}: ملفات={r.FilesChecked} تهديدات={r.Threats} Defender={r.DefenderThreats.Count} =====");
            }
            catch (OperationCanceledException) { r.Cancelled = true; Settings.Log("  تم ايقاف الفحص"); }
            catch (Exception ex) { r.Error = ex.Message; Settings.Log("خطأ: " + ex.Message); }
            return r;
        }

        static void CheckFile(FileInfo f, ScanResult r)
        {
            try
            {
                var ext = f.Extension.ToLowerInvariant();
                var a = f.Attributes;
                bool hs = IsHiddenSys(a);
                if (IsUnderSystemDir(f.FullName)) return; // ملفات ويندوز والبرامج يفحصها Defender بس

                if (hs && ScriptExt.Contains(ext))
                {
                    if (Remove(f.FullName, "hidden script", "HDD")) Hit(r, f.FullName);
                }
                else if (hs && BinExt.Contains(ext))
                {
                    if (Remove(f.FullName, "hidden executable", "HDD")) Hit(r, f.FullName);
                }
                else if (ext == ".lnk" && f.Length < 64 * 1024 && IsShortcutVirusLnk(f.FullName))
                {
                    if (Remove(f.FullName, "shortcut virus", "HDD")) Hit(r, f.FullName);
                }
            }
            catch { }
        }

        public static bool IsHiddenSys(FileAttributes a) => (a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0;

        public static bool IsShortcutVirusLnk(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var text = Encoding.Unicode.GetString(bytes) + "\n" + Encoding.ASCII.GetString(bytes);
                return StrictLnk.IsMatch(text);
            }
            catch { return false; }
        }

        static string[] _excluded;
        static bool IsExcludedDir(string dir)
        {
            _excluded ??= new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft"),
                Settings.DataDir,
            }.Where(x => !string.IsNullOrEmpty(x)).Select(x => x.TrimEnd('\\')).ToArray();

            var name = Path.GetFileName(dir.TrimEnd('\\')).ToLowerInvariant();
            if (name == "$recycle.bin" || name == "system volume information" || name == "windowsapps" || name == "winsxs") return true;
            return _excluded.Any(x => dir.TrimEnd('\\').Equals(x, StringComparison.OrdinalIgnoreCase));
        }

        static bool IsUnderSystemDir(string file)
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
            return file.StartsWith(win, StringComparison.OrdinalIgnoreCase)
                || file.StartsWith(Settings.DataDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        static void ApplyDefender(ScanResult r, int code)
        {
            if (code < 0) return;
            r.DefenderRan = true;
            r.DefenderFound = r.DefenderThreats.Count > 0;
            foreach (var t in r.DefenderThreats) r.Details.Add("Defender: " + t);
            if (r.DefenderFound) Settings.Log("  Defender لگى: " + string.Join(", ", r.DefenderThreats));
        }

        // ==================================================================
        //  Defender مع نسبة تقدّم حقيقية:
        //  نحسب الملفات، نقسّم المسار لأجزاء، ونفحص جزء جزء حتى نعرف شكد وصلنا
        // ==================================================================
        class DirNode
        {
            public string Path;
            public DirNode Parent;
            public readonly List<DirNode> Children = new List<DirNode>();
            public readonly List<string> Files = new List<string>(); // نحفظ لحد 60 اسم بس
            public long DirectFiles;
            public long Total;
        }

        const int MaxDirectFilesToSplit = 60;

        static string MpCmdRunPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");

        public static int DefenderChunked(string path, CancellationToken ct, IProgress<(int pct, string msg)> progress,
                                          int from, int to, List<string> threats)
        {
            if (!File.Exists(MpCmdRunPath)) return -1;
            if (File.Exists(path))
            {
                progress?.Report((from, L.F("step_def_progress", "0", "1", Path.GetFileName(path))));
                return RunDefender(path, 3, ct, threats);
            }

            // 1) عد الملفات وبناء شجرة المجلدات
            var nodes = new List<DirNode>();
            var rootNode = new DirNode { Path = path.Length == 3 ? path : path.TrimEnd('\\') };
            nodes.Add(rootNode);
            var stack = new Stack<DirNode>();
            stack.Push(rootNode);
            long counted = 0;
            int lastReport = Environment.TickCount;
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var n = stack.Pop();
                FileSystemInfo[] entries;
                try { entries = new DirectoryInfo(n.Path).GetFileSystemInfos(); } catch { continue; }
                foreach (var e in entries)
                {
                    if (e is DirectoryInfo d)
                    {
                        if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        var c = new DirNode { Path = d.FullName, Parent = n };
                        n.Children.Add(c);
                        nodes.Add(c);
                        stack.Push(c);
                    }
                    else
                    {
                        n.DirectFiles++;
                        if (n.Files.Count <= MaxDirectFilesToSplit) n.Files.Add(e.FullName);
                        counted++;
                    }
                }
                if (Environment.TickCount - lastReport > 400)
                {
                    lastReport = Environment.TickCount;
                    progress?.Report((-1, L.F("step_counting", counted.ToString("N0"))));
                }
            }
            // مجموع كل مجلد (الابناء انضافوا بعد الاباء، فنمشي بالعكس)
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                var n = nodes[i];
                n.Total += n.DirectFiles;
                if (n.Parent != null) n.Parent.Total += n.Total;
            }
            long total = Math.Max(1, rootNode.Total);

            // 2) تقسيم لأجزاء (كل جزء تقريباً 4% من الملفات)
            var chunks = new List<(string path, long count)>();
            long limit = Math.Max(1500, total / 25);
            Split(rootNode, limit, chunks);
            if (chunks.Count == 0) chunks.Add((rootNode.Path, total));

            // 3) فحص جزء جزء
            long done = 0;
            bool any = false;
            foreach (var (cp, count) in chunks)
            {
                ct.ThrowIfCancellationRequested();
                int pct = from + (int)((to - from) * done / total);
                progress?.Report((pct, L.F("step_def_progress", done.ToString("N0"), total.ToString("N0"), ShortName(cp))));
                var code = RunDefender(cp, 3, ct, threats);
                if (code < 0 && !any) return -1; // Defender مو شغال
                any = true;
                done += count;
            }
            progress?.Report((to, L.F("step_def_progress", total.ToString("N0"), total.ToString("N0"), "")));
            return 0;
        }

        static void Split(DirNode n, long limit, List<(string, long)> outList)
        {
            if (n.Total == 0) return;
            if (n.Total <= limit || n.Children.Count == 0 || n.DirectFiles > MaxDirectFilesToSplit)
            {
                outList.Add((n.Path, n.Total));
                return;
            }
            foreach (var c in n.Children) Split(c, limit, outList);
            foreach (var f in n.Files) outList.Add((f, 1));
        }

        static string ShortName(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            if (p.Length <= 3) return p;
            var name = Path.GetFileName(p.TrimEnd('\\'));
            var parent = Path.GetFileName(Path.GetDirectoryName(p.TrimEnd('\\'))?.TrimEnd('\\') ?? "");
            var s2 = string.IsNullOrEmpty(parent) ? name : parent + "\\" + name;
            return s2.Length > 34 ? "..." + s2.Substring(s2.Length - 31) : s2;
        }

        /// <summary>
        /// يشغل Windows Defender. scanType: 1 = سريع (الذاكرة والبرامج الشغالة والتشغيل التلقائي) ، 3 = مسار معين.
        /// يرجع كود الخروج او -1 اذا Defender مو متوفر. اسماء التهديدات تنضاف لـ threats.
        /// </summary>
        public static int RunDefender(string target, int scanType, CancellationToken ct, List<string> threats)
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var mp = Path.Combine(pf, @"Windows Defender\MpCmdRun.exe");
            if (!File.Exists(mp)) return -1;

            string args = scanType == 3
                ? "-Scan -ScanType 3 -File " + (IsDriveRoot(target) ? target : "\"" + target.TrimEnd('\\') + "\"")
                : "-Scan -ScanType " + scanType;

            var psi = new ProcessStartInfo(mp, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            var output = new StringBuilder();
            p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
            {
                while (!p.WaitForExit(500))
                    if (ct.IsCancellationRequested) break;
            }
            ct.ThrowIfCancellationRequested();
            p.WaitForExit();

            string text; lock (output) text = output.ToString();
            foreach (Match m in Regex.Matches(text, @"^\s*Threat\s*:\s*(.+?)\s*$", RegexOptions.Multiline))
            {
                var name = m.Groups[1].Value.Trim();
                if (name.Length > 0 && !threats.Contains(name)) threats.Add(name);
            }
            // Defender مطفّي / برنامج حماية ثاني
            if (Regex.IsMatch(text, @"0x800106ba|0x80508023|service.*(not|disabled)|Failed with hr", RegexOptions.IgnoreCase) && threats.Count == 0)
                return -1;
            return p.ExitCode;
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

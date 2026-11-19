using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace USBGuard
{
    // =====================================================================
    //  الحماية اللحظية: اي ملف خطر جديد بالتنزيلات / سطح المكتب / الفلاشة ينفحص أول ما ينكتب
    // =====================================================================
    public sealed class RealtimeGuard : IDisposable
    {
        static readonly string[] RiskyExt =
        {
            ".exe", ".scr", ".pif", ".com", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
            ".hta", ".lnk", ".msi", ".ps1", ".jar", ".dll", ".inf",
        };

        readonly Dictionary<string, FileSystemWatcher> _watchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        readonly BlockingCollection<string> _queue = new BlockingCollection<string>(new ConcurrentQueue<string>(), 500);
        readonly ConcurrentDictionary<string, DateTime> _recent = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly Thread _worker;
        volatile bool _running;

        /// <summary>path, threat description</summary>
        public event Action<string, string> Detected;

        public RealtimeGuard()
        {
            _worker = new Thread(Work) { IsBackground = true, Name = "USBGuardRealtime", Priority = ThreadPriority.BelowNormal };
        }

        public bool Running => _running;

        public void Start()
        {
            if (_running) return;
            _running = true;
            if (!_worker.IsAlive) _worker.Start();

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddRoot(Path.Combine(profile, "Downloads"));
            AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.DriveType == DriveType.Removable && d.IsReady) AddRoot(d.Name); } catch { }
            }
            Settings.Log("الحماية اللحظية: شغالة");
        }

        public void Stop()
        {
            _running = false;
            lock (_watchers)
            {
                foreach (var w in _watchers.Values) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
                _watchers.Clear();
            }
            Settings.Log("الحماية اللحظية: متوقفة");
        }

        public void AddRoot(string path)
        {
            if (!_running || string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            lock (_watchers)
            {
                if (_watchers.ContainsKey(path)) return;
                try
                {
                    var w = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Attributes,
                        InternalBufferSize = 64 * 1024,
                    };
                    w.Created += (s, e) => OnEvent(e.FullPath);
                    w.Changed += (s, e) => OnEvent(e.FullPath);
                    w.Renamed += (s, e) => OnEvent(e.FullPath);
                    w.Error += (s, e) => { };
                    w.EnableRaisingEvents = true;
                    _watchers[path] = w;
                }
                catch { }
            }
        }

        public void RemoveRoot(string path)
        {
            lock (_watchers)
            {
                if (_watchers.TryGetValue(path, out var w))
                {
                    try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
                    _watchers.Remove(path);
                }
            }
        }

        void OnEvent(string path)
        {
            if (!_running || string.IsNullOrEmpty(path)) return;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!RiskyExt.Contains(ext)) return;
            if (path.StartsWith(Settings.DataDir, StringComparison.OrdinalIgnoreCase)) return;
            var now = DateTime.Now;
            if (_recent.TryGetValue(path, out var last) && (now - last).TotalSeconds < 10) return;
            _recent[path] = now;
            _queue.TryAdd(path);
            if (_recent.Count > 2000)
                foreach (var k in _recent.Where(x => (now - x.Value).TotalMinutes > 5).Select(x => x.Key).ToList()) _recent.TryRemove(k, out _);
        }

        void Work()
        {
            foreach (var path in _queue.GetConsumingEnumerable())
            {
                if (!_running) continue;
                try { Check(path); } catch { }
            }
        }

        void Check(string path)
        {
            // ننتظر الملف يخلص كتابة
            for (int i = 0; i < 20; i++)
            {
                if (!File.Exists(path)) return;
                try { using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) break; }
                catch { Thread.Sleep(500); }
            }
            if (!File.Exists(path)) return;

            var fi = new FileInfo(path);
            var ext = fi.Extension.ToLowerInvariant();
            var root = Path.GetPathRoot(path);
            bool onUsb = false;
            try { onUsb = new DriveInfo(root).DriveType == DriveType.Removable; } catch { }

            string reason = null;
            if (onUsb && fi.Name.Equals("autorun.inf", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fi.DirectoryName.TrimEnd('\\') + "\\", root, StringComparison.OrdinalIgnoreCase))
                reason = "autorun.inf";
            else if (Scanner.IsHiddenSys(fi.Attributes) && ext != ".inf" && ext != ".dll")
                reason = "hidden executable";
            else if (ext == ".lnk" && fi.Length < 64 * 1024 &&
                     (onUsb ? Scanner.IsMaliciousLnk(path) : Scanner.IsShortcutVirusLnk(path)))
                reason = "shortcut virus";

            if (reason != null)
            {
                if (Scanner.Remove(path, "realtime: " + reason, onUsb ? "USB" : "PC"))
                {
                    Settings.Log($"[حماية لحظية] {path} ({reason})");
                    Detected?.Invoke(path, reason);
                }
                return;
            }

            if (ext == ".inf" || ext == ".lnk") return;

            // فحص الملف بمحرك Defender
            var threats = new List<string>();
            var code = Scanner.RunDefender(path, 3, CancellationToken.None, threats);
            if (threats.Count > 0)
            {
                Settings.Log($"[حماية لحظية] Defender: {path} = {string.Join(", ", threats)}");
                Detected?.Invoke(path, string.Join(", ", threats));
            }
        }

        public void Dispose()
        {
            Stop();
            _queue.CompleteAdding();
        }
    }

    // =====================================================================
    //  وضع اللعب: اذا اكو برنامج بملء الشاشة (لعبة / فلم / عرض تقديمي)
    // =====================================================================
    public static class GameMode
    {
        [DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out int state);

        public static bool IsBusy()
        {
            if (!Settings.GameMode) return false;
            try
            {
                if (SHQueryUserNotificationState(out int s) == 0)
                    return s == 2 /* BUSY */ || s == 3 /* D3D FULL SCREEN */ || s == 4 /* PRESENTATION */;
            }
            catch { }
            return false;
        }
    }

    // =====================================================================
    //  الفحص المجدول
    // =====================================================================
    public static class Schedule
    {
        /// <summary>آخر موعد فحص (قبل او يساوي الآن). null اذا مطفّي</summary>
        public static DateTime? LastDue(DateTime now)
        {
            int mode = Settings.ScheduleMode;
            int hour = Math.Max(0, Math.Min(23, Settings.ScheduleHour));
            if (mode <= 0) return null;
            var today = now.Date.AddHours(hour);
            if (mode == 1) return now >= today ? today : today.AddDays(-1);
            var day = (DayOfWeek)(mode - 2);
            int back = ((int)now.DayOfWeek - (int)day + 7) % 7;
            var due = now.Date.AddDays(-back).AddHours(hour);
            if (due > now) due = due.AddDays(-7);
            return due;
        }

        public static bool IsDue(DateTime now)
        {
            var due = LastDue(now);
            if (due == null) return false;
            DateTime.TryParse(Settings.LastScheduledRun, out var last);
            return last < due.Value && (now - due.Value).TotalDays < 7;
        }

        public static void MarkRun() => Settings.LastScheduledRun = DateTime.Now.ToString("s");

        public static string ModeText(int mode)
        {
            if (mode <= 0) return L.T("sch_off");
            if (mode == 1) return L.T("sch_daily");
            var day = (DayOfWeek)(mode - 2);
            string[] ar = { "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت" };
            return L.Ar ? "كل " + ar[(int)day] : "Every " + day;
        }

        public static string HourText(int h)
        {
            var t = new DateTime(2000, 1, 1, h, 0, 0);
            return t.ToString("hh:mm") + " " + (h < 12 ? (L.Ar ? "ص" : "AM") : (L.Ar ? "م" : "PM"));
        }
    }

    // =====================================================================
    //  كلمة السر (SHA256 + salt)
    // =====================================================================
    public static class Security
    {
        static bool _unlocked;

        public static bool IsSet => !string.IsNullOrEmpty(Settings.PasswordHash);

        public static void Lock() => _unlocked = false;

        public static void SetPassword(string pw)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            Settings.PasswordSalt = Convert.ToBase64String(salt);
            Settings.PasswordHash = Hash(pw, salt);
            _unlocked = true;
        }

        public static void Clear()
        {
            Settings.PasswordHash = "";
            Settings.PasswordSalt = "";
        }

        public static bool Verify(string pw)
        {
            if (!IsSet) return true;
            try { return Hash(pw ?? "", Convert.FromBase64String(Settings.PasswordSalt)) == Settings.PasswordHash; }
            catch { return false; }
        }

        static string Hash(string pw, byte[] salt)
        {
            using var sha = SHA256.Create();
            var data = salt.Concat(Encoding.UTF8.GetBytes(pw)).ToArray();
            for (int i = 0; i < 10000; i++) data = sha.ComputeHash(data);
            return Convert.ToBase64String(data);
        }

        /// <summary>يطلب كلمة السر اذا مفعّلة. يرجع true اذا مسموح</summary>
        public static bool Require(System.Windows.Forms.IWin32Window owner)
        {
            if (!IsSet || _unlocked) return true;
            for (int i = 0; i < 3; i++)
            {
                var pw = PasswordDialog.Ask(owner, L.T("pw_enter"), i > 0 ? L.T("pw_wrong") : null);
                if (pw == null) return false;
                if (Verify(pw)) { _unlocked = true; return true; }
            }
            return false;
        }
    }
}

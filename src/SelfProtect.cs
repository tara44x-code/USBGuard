using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Threading;
using Microsoft.Win32;

namespace USBGuard
{
    /// <summary>
    /// الحماية الذاتية: الفايروس ما يگدر يطفّي البرنامج.
    ///  1) صلاحيات العملية: منع "انهاء العملية" عن الكل (حتى من مدير المهام)
    ///  2) عملية حارسة (Guard): اذا البرنامج انطفى بالقوة، ترجعه يشتغل فوراً — وهو يرجع الحارس اذا انطفى
    ///  3) المهمة المجدولة تتأكد كل 5 دقايق إنه شغال
    ///  4) تصليح ذاتي: يرجّع التشغيل التلقائي والقائمة اليمنى ويرفع اي حجب عن البرنامج
    /// </summary>
    public static class SelfProtect
    {
        const string QuitEventName = @"Global\USBGuard_SatLink_Quit";
        const string GuardMutexName = @"Global\USBGuard_SatLink_Guard";

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool SetKernelObjectSecurity(IntPtr handle, int securityInformation, byte[] sd);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

        static EventWaitHandle _quit;

        static EventWaitHandle QuitEvent
        {
            get
            {
                if (_quit == null)
                {
                    try { _quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName); }
                    catch { _quit = new EventWaitHandle(false, EventResetMode.ManualReset); }
                }
                return _quit;
            }
        }

        // ------------------------------------------------------------------
        // 1) منع انهاء العملية
        // ------------------------------------------------------------------
        public static void ProtectCurrentProcess()
        {
            try
            {
                // D = Deny PROCESS_TERMINATE(0x1) للكل
                // A = السماح لـ SYSTEM والمدراء بكل شي غير الانهاء، وللكل بالاستعلام والمزامنة فقط
                var sd = new RawSecurityDescriptor("D:P(D;;0x1;;;WD)(A;;0x1FFFFE;;;SY)(A;;0x1FFFFE;;;BA)(A;;0x101410;;;WD)");
                var bytes = new byte[sd.BinaryLength];
                sd.GetBinaryForm(bytes, 0);
                SetKernelObjectSecurity(GetCurrentProcess(), 4 /* DACL_SECURITY_INFORMATION */, bytes);
            }
            catch { }
        }

        public static void UnprotectCurrentProcess()
        {
            try
            {
                var sd = new RawSecurityDescriptor("D:(A;;0x1FFFFF;;;SY)(A;;0x1FFFFF;;;BA)(A;;0x101410;;;WD)");
                var bytes = new byte[sd.BinaryLength];
                sd.GetBinaryForm(bytes, 0);
                SetKernelObjectSecurity(GetCurrentProcess(), 4, bytes);
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // 2) الحارس
        // ------------------------------------------------------------------
        public static void BeginSession() { try { QuitEvent.Reset(); } catch { } }

        /// <summary>يُستدعى قبل الخروج المقصود (خروج / تحديث / الغاء تثبيت) حتى الحارس ما يرجّعه</summary>
        public static void SignalQuit() { try { QuitEvent.Set(); } catch { } }

        public static bool GuardRunning()
        {
            try
            {
                if (Mutex.TryOpenExisting(GuardMutexName, out var m)) { m.Dispose(); return true; }
            }
            catch (UnauthorizedAccessException) { return true; }
            catch { }
            return false;
        }

        public static void StartGuard()
        {
            if (!Settings.SelfProtect || GuardRunning()) return;
            if (!File.Exists(SystemTools.InstalledExe) || !SystemTools.IsRunningInstalled) return;
            try
            {
                Process.Start(new ProcessStartInfo(SystemTools.InstalledExe, "--guard " + Process.GetCurrentProcess().Id)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            catch { }
        }

        /// <summary>حلقة العملية الحارسة (تشتغل بعملية منفصلة بدون واجهة)</summary>
        public static void RunGuard(int mainPid)
        {
            using var mutex = new Mutex(true, GuardMutexName, out bool first);
            if (!first) return;
            ProtectCurrentProcess();
            var quit = QuitEvent;
            int restarts = 0;
            var window = Stopwatch.StartNew();

            while (true)
            {
                if (quit.WaitOne(1500)) return;
                if (!Settings.SelfProtect) return;

                bool alive;
                try { using var p = Process.GetProcessById(mainPid); alive = !p.HasExited; }
                catch { alive = false; }
                if (alive) continue;

                // انطفى: نتأكد إنه مو خروج مقصود
                if (quit.WaitOne(1200)) return;

                // حماية من اعادة تشغيل لا نهائية اذا البرنامج نفسه يطيح
                if (window.Elapsed.TotalMinutes > 10) { window.Restart(); restarts = 0; }
                if (++restarts > 5) { Thread.Sleep(60000); continue; }

                try
                {
                    // نحاول نلگى نسخة شغالة اولاً (ممكن المهمة المجدولة رجعته)
                    int found = FindMainProcess();
                    if (found > 0) { mainPid = found; continue; }

                    var p = Process.Start(new ProcessStartInfo(SystemTools.InstalledExe, "--tray --restarted") { UseShellExecute = false });
                    mainPid = p.Id;
                    Settings.Log("الحماية الذاتية: البرنامج انطفى بالقوة ورجع اشتغل ✓");
                }
                catch { }
                Thread.Sleep(3000);
            }
        }

        static int FindMainProcess()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("USBGuard"))
            {
                using (p)
                {
                    if (p.Id == me) continue;
                    try
                    {
                        // الحارس نفسه ما عنده نافذة رئيسية، فنتحقق من المتغير البيئي غير متاح؛ نعتمد على الـ mutex
                        if (Mutex.TryOpenExisting(@"Global\USBGuard_SatLink_Mutex", out var m)) { m.Dispose(); return p.Id; }
                    }
                    catch (UnauthorizedAccessException) { return p.Id; }
                    catch { }
                }
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // 3) تصليح ذاتي (كل دقيقة)
        // ------------------------------------------------------------------
        public static void SelfRepair()
        {
            if (!SystemTools.IsRunningInstalled) return;
            try
            {
                if (Settings.Startup && !SystemTools.IsStartupEnabled())
                {
                    SystemTools.SetStartup(true);
                    Settings.Log("الحماية الذاتية: التشغيل التلقائي انحذف ورجّعناه");
                }

                using (var k = Registry.ClassesRoot.OpenSubKey(@"Drive\shell\USBGuard\command"))
                {
                    if (k == null) SystemTools.RegisterContextMenu();
                }

                // اذا الفايروس حجب البرنامج عن طريق IFEO او DisallowRun
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var ifeo = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", true);
                    if (ifeo?.OpenSubKey("USBGuard.exe") != null)
                    {
                        ifeo.DeleteSubKeyTree("USBGuard.exe", false);
                        Settings.Log("الحماية الذاتية: رفع حجب IFEO عن البرنامج");
                    }
                }
                foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    using var dr = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", true);
                    if (dr == null) continue;
                    foreach (var n in dr.GetValueNames())
                        if ((dr.GetValue(n) as string ?? "").IndexOf("usbguard", StringComparison.OrdinalIgnoreCase) >= 0)
                            dr.DeleteValue(n, false);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // 4) التحقق إن الرسالة (مثل EXIT) جاية من البرنامج نفسه مو من فايروس
        // ------------------------------------------------------------------
        public static bool IsTrustedClient(NamedPipeServerStream server)
        {
            try
            {
                if (!GetNamedPipeClientProcessId(server.SafePipeHandle.DangerousGetHandle(), out uint pid)) return false;
                using var p = Process.GetProcessById((int)pid);
                var path = p.MainModule.FileName;
                return string.Equals(Path.GetFullPath(path), Path.GetFullPath(SystemTools.InstalledExe), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFullPath(path), Path.GetFullPath(SystemTools.CurrentExe), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace USBGuard
{
    /// <summary>اعدادات النظام: الاوتورن، تصليح الريجستري، التثبيت، القائمة اليمنى، التشغيل مع ويندوز</summary>
    public static class SystemTools
    {
        public const string AppName = "USB Guard";
        public const string TaskName = "USBGuard_SatLink";
        public const string GitHubUser = "tara44x-code";
        public const string GitHubRepo = "USBGuard";
        public const string DeveloperUrl = "https://github.com/" + GitHubUser;
        public const string RepoUrl = "https://github.com/" + GitHubUser + "/" + GitHubRepo;

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
        public static string InstalledExe => Path.Combine(InstallDir, "USBGuard.exe");
        public static string CurrentExe => Assembly.GetExecutingAssembly().Location;

        public static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static bool IsRunningInstalled =>
            string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------
        // 1) تعطيل الاوتورن نهائياً
        // ------------------------------------------------------------------
        public static void DisableAutorun()
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var k = hive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
                    k.SetValue("NoDriveTypeAutoRun", 0xFF, RegistryValueKind.DWord);
                    k.SetValue("NoAutorun", 1, RegistryValueKind.DWord);
                }
                catch { }
            }
            try
            {
                using var k = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\IniFileMapping\Autorun.inf");
                k.SetValue("", "@SYS:DoesNotExist", RegistryValueKind.String);
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // 2) تصليح الي تخربه الفايروسات (Sality / gamez ...)
        // ------------------------------------------------------------------
        public static int RepairSystem()
        {
            int fixes = 0;

            // منع الكتابة على الفلاشة
            fixes += SetIfDifferent(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies", "WriteProtect", 0, onlyIfExists: true);

            // قفل الكتابة على الاقراص القابلة للازالة / CD
            try
            {
                using var p = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows", true);
                if (p?.OpenSubKey("RemovableStorageDevices") != null)
                {
                    p.DeleteSubKeyTree("RemovableStorageDevices", false);
                    fixes++;
                }
            }
            catch { }

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                fixes += DeleteValues(hive, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer",
                    "NoCDBurning", "NoFolderOptions", "NoDrives", "NoViewOnDrive", "NoControlPanel");
                fixes += DeleteValues(hive, @"Software\Microsoft\Windows\CurrentVersion\Policies\System",
                    "DisableTaskMgr", "DisableRegistryTools", "DisableCMD");
                fixes += DeleteValues(hive, @"Software\Policies\Microsoft\Windows\System", "DisableCMD");
            }

            // اظهار الملفات المخفية
            fixes += SetIfDifferent(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1);
            fixes += SetIfDifferent(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Folder\Hidden\SHOWALL", "CheckedValue", 1, onlyIfExists: true);

            if (fixes > 0) Settings.Log($"تصليح النظام: {fixes} اعداد مخرّب رجع طبيعي");
            return fixes;
        }

        static int SetIfDifferent(RegistryKey hive, string path, string name, int value, bool onlyIfExists = false)
        {
            try
            {
                using var k = onlyIfExists ? hive.OpenSubKey(path, true) : hive.CreateSubKey(path);
                if (k == null) return 0;
                var cur = k.GetValue(name);
                if (cur != null && Convert.ToInt32(cur) == value) return 0;
                if (cur == null && onlyIfExists) { }
                k.SetValue(name, value, RegistryValueKind.DWord);
                return cur == null ? 0 : 1;
            }
            catch { return 0; }
        }

        static int DeleteValues(RegistryKey hive, string path, params string[] names)
        {
            int n = 0;
            try
            {
                using var k = hive.OpenSubKey(path, true);
                if (k == null) return 0;
                foreach (var name in names)
                {
                    if (k.GetValue(name) != null) { k.DeleteValue(name, false); n++; }
                }
            }
            catch { }
            return n;
        }

        // ------------------------------------------------------------------
        // 3) التثبيت
        // ------------------------------------------------------------------
        public static bool Install()
        {
            try
            {
                Directory.CreateDirectory(InstallDir);
                if (!IsRunningInstalled)
                    File.Copy(CurrentExe, InstalledExe, true);

                RegisterContextMenu();
                RegisterUninstall();
                CreateStartMenuShortcut();
                SetStartup(true);
                DisableAutorun();
                Settings.Log("تم تثبيت USB Guard " + CurrentVersion);
                return true;
            }
            catch (Exception ex)
            {
                Settings.Log("فشل التثبيت: " + ex.Message);
                return false;
            }
        }

        public static void Uninstall()
        {
            SetStartup(false);
            try { Registry.ClassesRoot.DeleteSubKeyTree(@"Drive\shell\USBGuard", false); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\USBGuard", false); } catch { }
            try { File.Delete(StartMenuLink); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\SatLink\USBGuard", false); } catch { }

            // حذف مجلد البرنامج بعد ما يسكر
            try
            {
                var bat = Path.Combine(Path.GetTempPath(), "usbguard_uninstall.cmd");
                File.WriteAllText(bat,
                    "@echo off\r\n" +
                    "ping 127.0.0.1 -n 3 >nul\r\n" +
                    $"rmdir /s /q \"{InstallDir}\"\r\n" +
                    "del \"%~f0\"\r\n");
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // 4) القائمة اليمنى على الفلاشة (Drive context menu)
        // ------------------------------------------------------------------
        public static void RegisterContextMenu()
        {
            try
            {
                using var k = Registry.ClassesRoot.CreateSubKey(@"Drive\shell\USBGuard");
                k.SetValue("", L.T("menu_scan"));
                k.SetValue("MUIVerb", L.T("menu_scan"));
                k.SetValue("Icon", $"\"{InstalledExe}\",0");
                k.SetValue("Position", "Top");
                using var c = k.CreateSubKey("command");
                c.SetValue("", $"\"{InstalledExe}\" --scan %1");
            }
            catch (Exception ex) { Settings.Log("فشل تسجيل القائمة اليمنى: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        // 5) التشغيل مع ويندوز (مهمة مجدولة بصلاحية كاملة بدون رسالة UAC)
        // ------------------------------------------------------------------
        public static bool IsStartupEnabled()
        {
            return RunHidden("schtasks.exe", $"/Query /TN \"{TaskName}\"") == 0;
        }

        public static void SetStartup(bool enable)
        {
            if (enable)
            {
                // XML حتى: يشتغل على البطارية، وما ينطفي بعد 3 أيام، وبصلاحية كاملة
                var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                var xml =
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Author>Muhannad Sat Link</Author><Description>USB Guard - USB protection</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{System.Security.SecurityElement.Escape(user)}</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{System.Security.SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{System.Security.SecurityElement.Escape(InstalledExe)}</Command><Arguments>--tray</Arguments><WorkingDirectory>{System.Security.SecurityElement.Escape(InstallDir)}</WorkingDirectory></Exec></Actions>
</Task>";
                var tmp = Path.Combine(Path.GetTempPath(), "usbguard_task.xml");
                File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
                var code = RunHidden("schtasks.exe", $"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F");
                try { File.Delete(tmp); } catch { }
                if (code != 0)
                {
                    var tr = $"\\\"{InstalledExe}\\\" --tray";
                    RunHidden("schtasks.exe", $"/Create /TN \"{TaskName}\" /TR \"{tr}\" /SC ONLOGON /RL HIGHEST /F");
                }
            }
            else
            {
                RunHidden("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F");
            }
        }

        // ------------------------------------------------------------------
        // 6) برامج ويندوز (Apps & features) + قائمة ابدأ
        // ------------------------------------------------------------------
        static void RegisterUninstall()
        {
            try
            {
                using var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\USBGuard");
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", CurrentVersion.ToString(3));
                k.SetValue("Publisher", "Muhannad Sat Link - مهند سات لنك");
                k.SetValue("DisplayIcon", $"\"{InstalledExe}\",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
                k.SetValue("URLInfoAbout", RepoUrl);
                k.SetValue("HelpLink", DeveloperUrl);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try { k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord); } catch { }
            }
            catch { }
        }

        public static void UpdateUninstallVersion()
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\USBGuard", true);
                k?.SetValue("DisplayVersion", CurrentVersion.ToString(3));
            }
            catch { }
        }

        static string StartMenuLink =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "USB Guard.lnk");

        static void CreateStartMenuShortcut()
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                var sh = Activator.CreateInstance(t);
                var lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { StartMenuLink });
                var lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { InstallDir });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "USB Guard - Muhannad Sat Link" });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe + ",0" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
        }

        // ------------------------------------------------------------------
        public static int RunHidden(string file, string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(file, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                return p.ExitCode;
            }
            catch { return -1; }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}

using System;
using System.IO;
using Microsoft.Win32;

namespace USBGuard
{
    /// <summary>اعدادات البرنامج (محفوظة بالريجستري)</summary>
    public static class Settings
    {
        const string KeyPath = @"Software\SatLink\USBGuard";

        public static bool Arabic      { get => GetBool("Arabic", true);      set => SetBool("Arabic", value); }
        public static bool AutoScan    { get => GetBool("AutoScan", true);    set => SetBool("AutoScan", value); }
        public static bool DeepScan    { get => GetBool("DeepScan", true);    set => SetBool("DeepScan", value); }
        public static bool AutoUpdate  { get => GetBool("AutoUpdate", true);  set => SetBool("AutoUpdate", value); }
        public static int  TotalThreats{ get => GetInt("TotalThreats", 0);    set => SetInt("TotalThreats", value); }
        public static int  TotalRestored{get => GetInt("TotalRestored", 0);   set => SetInt("TotalRestored", value); }
        public static string LastScan  { get => GetStr("LastScan", "");       set => SetStr("LastScan", value); }
        public static string SkippedVersion { get => GetStr("SkippedVersion", ""); set => SetStr("SkippedVersion", value); }

        public static string DataDir
        {
            get
            {
                var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "USBGuard");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string LogFile => Path.Combine(DataDir, "USBGuard.log");

        public static void Log(string msg)
        {
            try
            {
                var fi = new FileInfo(LogFile);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete(); // لا يكبر اكثر من 2MB
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}", System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // ---------------- helpers ----------------
        static object Get(string name)
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(KeyPath); return k?.GetValue(name); }
            catch { return null; }
        }
        static void Set(string name, object value, RegistryValueKind kind)
        {
            try { using var k = Registry.CurrentUser.CreateSubKey(KeyPath); k.SetValue(name, value, kind); }
            catch { }
        }
        static bool GetBool(string n, bool def) { var v = Get(n); return v == null ? def : Convert.ToInt32(v) != 0; }
        static void SetBool(string n, bool v) => Set(n, v ? 1 : 0, RegistryValueKind.DWord);
        static int GetInt(string n, int def) { var v = Get(n); return v == null ? def : Convert.ToInt32(v); }
        static void SetInt(string n, int v) => Set(n, v, RegistryValueKind.DWord);
        static string GetStr(string n, string def) => Get(n) as string ?? def;
        static void SetStr(string n, string v) => Set(n, v ?? "", RegistryValueKind.String);
    }
}

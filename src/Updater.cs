using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace USBGuard
{
    public class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string DownloadUrl;
        public string Notes;
    }

    /// <summary>التحديث الذاتي من GitHub Releases (باختيار المستخدم)</summary>
    public static class Updater
    {
        static string ApiUrl => $"https://api.github.com/repos/{SystemTools.GitHubUser}/{SystemTools.GitHubRepo}/releases/latest";

        static WebClient NewClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "USBGuard-Updater";
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            wc.Encoding = System.Text.Encoding.UTF8;
            return wc;
        }

        /// <returns>معلومات التحديث اذا اكو اصدار احدث، null اذا ماكو، ويرمي Exception اذا فشل الاتصال</returns>
        public static async Task<UpdateInfo> CheckAsync()
        {
            using var wc = NewClient();
            var json = await wc.DownloadStringTaskAsync(ApiUrl);
            var js = new JavaScriptSerializer();
            var root = js.Deserialize<Dictionary<string, object>>(json);

            var tag = root.TryGetValue("tag_name", out var t) ? t as string : null;
            if (string.IsNullOrEmpty(tag)) return null;
            if (!Version.TryParse(tag.TrimStart('v', 'V').Trim(), out var remote)) return null;
            remote = new Version(remote.Major, remote.Minor, Math.Max(0, remote.Build));

            if (remote <= SystemTools.CurrentVersion) return null;

            string url = null;
            if (root.TryGetValue("assets", out var a) && a is IEnumerable assets)
            {
                foreach (var item in assets)
                {
                    if (item is Dictionary<string, object> asset &&
                        asset.TryGetValue("name", out var n) &&
                        string.Equals(n as string, "USBGuard.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        url = asset["browser_download_url"] as string;
                        break;
                    }
                }
            }
            if (url == null) return null;

            var notes = root.TryGetValue("body", out var b) ? (b as string ?? "") : "";
            if (notes.Length > 400) notes = notes.Substring(0, 400) + "...";

            return new UpdateInfo { Version = remote, Tag = tag, DownloadUrl = url, Notes = notes.Trim() };
        }

        /// <summary>ينزل التحديث، يبدّل الملف، ويعيد تشغيل البرنامج</summary>
        public static async Task DownloadAndApplyAsync(UpdateInfo info)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "USBGuard_update.exe");
            try { File.Delete(tmp); } catch { }

            using (var wc = NewClient())
                await wc.DownloadFileTaskAsync(info.DownloadUrl, tmp);

            // تأكد إنه ملف exe سليم
            var fi = new FileInfo(tmp);
            if (!fi.Exists || fi.Length < 20 * 1024) throw new Exception("Invalid update file");
            using (var fs = File.OpenRead(tmp))
            {
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') throw new Exception("Invalid update file");
            }

            var target = SystemTools.IsRunningInstalled ? SystemTools.InstalledExe : SystemTools.CurrentExe;
            var bat = Path.Combine(Path.GetTempPath(), "usbguard_update.cmd");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "set n=0\r\n" +
                ":retry\r\n" +
                "ping 127.0.0.1 -n 2 >nul\r\n" +
                $"copy /y \"{tmp}\" \"{target}\" >nul 2>&1 && goto ok\r\n" +
                "set /a n+=1\r\n" +
                "if %n% lss 15 goto retry\r\n" +
                ":ok\r\n" +
                $"start \"\" \"{target}\" --updated\r\n" +
                $"del \"{tmp}\" >nul 2>&1\r\n" +
                "del \"%~f0\"\r\n",
                System.Text.Encoding.Default);

            Settings.Log($"تحديث الى {info.Tag}");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
    }
}

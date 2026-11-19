using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace USBGuard
{
    static class Program
    {
        const string MutexName = @"Global\USBGuard_SatLink_Mutex";
        public const string PipeName = "USBGuard_SatLink_Pipe";

        [STAThread]
        static void Main(string[] args)
        {
            string scanTarget = null;
            bool tray = false, updated = false, uninstall = false, installedNow = false;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i].Trim().ToLowerInvariant();
                if (a == "--tray") tray = true;
                else if (a == "--updated") updated = true;
                else if (a == "--installed") installedNow = true;
                else if (a == "--uninstall") uninstall = true;
                else if (a == "--scan" && i + 1 < args.Length) scanTarget = args[++i];
                else if (a == "--guard" && i + 1 < args.Length)
                {
                    // العملية الحارسة: بدون واجهة
                    if (int.TryParse(args[i + 1], out int pid)) SelfProtect.RunGuard(pid);
                    return;
                }
            }

            L.Ar = Settings.Arabic;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (uninstall)
            {
                var opt = L.Ar ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
                if (MessageBox.Show(L.T("uninstall_ask"), "USB Guard", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2, opt) == DialogResult.Yes)
                {
                    SendToRunning("EXIT");
                    Thread.Sleep(800);
                    SystemTools.Uninstall();
                }
                return;
            }

            using (var mutex = new Mutex(true, MutexName, out bool first))
            {
                if (!first)
                {
                    // تشغيل من المهمة المجدولة (كل 5 دقايق) والبرنامج اصلاً شغال: نطلع بهدوء
                    if (tray && scanTarget == null) return;
                    // البرنامج شغال: نرسله طلب الفحص او نفتح نافذته
                    SendToRunning(scanTarget != null ? "SCAN|" + scanTarget : "SHOW");
                    return;
                }

                // اول تشغيل من ملف التحميل: نثبّت البرنامج بمكانه الصحيح ونشغله من هناك
                if (!SystemTools.IsRunningInstalled)
                {
                    if (SystemTools.Install())
                    {
                        mutex.ReleaseMutex();
                        mutex.Dispose();
                        var extra = scanTarget != null ? $" --scan \"{scanTarget.Trim('"').TrimEnd('\\')}\"" : "";
                        Process.Start(new ProcessStartInfo(SystemTools.InstalledExe, "--installed" + extra) { UseShellExecute = true });
                        return;
                    }
                    // اذا فشل التثبيت نشتغل بدون تثبيت
                }
                else
                {
                    if (updated || installedNow)
                    {
                        SystemTools.RegisterContextMenu();
                        SystemTools.UpdateUninstallVersion();
                    }
                }

                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    Theme.Scale = Math.Max(1f, g.DpiX / 96f);

                Application.Run(new MainForm(tray, scanTarget, updated, installedNow));
            }
        }

        static void SendToRunning(string msg)
        {
            try
            {
                using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                c.Connect(3000);
                var b = Encoding.UTF8.GetBytes(msg);
                c.Write(b, 0, b.Length);
                c.Flush();
            }
            catch { }
        }
    }
}

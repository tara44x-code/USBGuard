using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace USBGuard
{
    /// <summary>تقرير HTML احترافي بعد كل فحص (يفتح بالمتصفح، ويگدر ينطبع او ينحفظ PDF)</summary>
    public static class Report
    {
        static string _logoB64;

        static string LogoBase64()
        {
            if (_logoB64 != null) return _logoB64;
            try
            {
                using var s = typeof(Report).Assembly.GetManifestResourceStream("USBGuard.logo.png");
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                _logoB64 = Convert.ToBase64String(ms.ToArray());
            }
            catch { _logoB64 = ""; }
            return _logoB64;
        }

        static string E(string s) => WebUtility.HtmlEncode(s ?? "");

        public static string Create(string title, string target, DateTime started, TimeSpan duration, long files,
                                    int threats, int restored, IEnumerable<string> items, IEnumerable<string> warnings)
        {
            try
            {
                var dir = Path.Combine(Settings.DataDir, "Reports");
                Directory.CreateDirectory(dir);
                // نخلي آخر 60 تقرير بس
                foreach (var old in new DirectoryInfo(dir).GetFiles("*.html").OrderByDescending(f => f.CreationTime).Skip(60))
                    try { old.Delete(); } catch { }

                var file = Path.Combine(dir, $"report_{started:yyyyMMdd_HHmmss}.html");
                bool ar = L.Ar;
                var list = (items ?? Enumerable.Empty<string>()).ToList();
                var warn = (warnings ?? Enumerable.Empty<string>()).ToList();
                bool clean = threats == 0;
                string status = clean ? (ar ? "نظيف ✓" : "Clean ✓") : (ar ? $"تم التعامل مع {threats} تهديد" : $"{threats} threat(s) handled");
                string dur = duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}" : $"{duration.Minutes}:{duration.Seconds:00}";

                var sb = new StringBuilder();
                sb.Append($@"<!DOCTYPE html><html lang=""{(ar ? "ar" : "en")}"" dir=""{(ar ? "rtl" : "ltr")}""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>USB Guard — {E(title)}</title>
<style>
*{{box-sizing:border-box}}body{{margin:0;font-family:'Segoe UI',Tahoma,Arial,sans-serif;background:#0a0f1c;color:#e6edf7}}
.wrap{{max-width:760px;margin:32px auto;padding:0 18px}}
.head{{display:flex;align-items:center;gap:16px;padding:22px;border-radius:18px;background:linear-gradient(135deg,#16244a,#0f1830);border:1px solid #203052}}
.head img{{width:64px;height:64px;border-radius:14px}}
.head h1{{margin:0;font-size:24px}} .head p{{margin:4px 0 0;color:#8a97b0}}
.badge{{display:inline-block;margin-top:18px;padding:10px 18px;border-radius:999px;font-weight:700;
background:{(clean ? "rgba(34,197,94,.15);color:#22c55e;border:1px solid #22c55e66" : "rgba(245,158,11,.15);color:#f59e0b;border:1px solid #f59e0b66")}}}
.grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-top:18px}}
.card{{background:#121a2c;border:1px solid #203052;border-radius:14px;padding:14px;text-align:center}}
.card b{{display:block;font-size:22px}} .card span{{color:#8a97b0;font-size:12px}}
h2{{font-size:16px;margin:26px 0 10px;color:#00c2ff}}
.item{{background:#121a2c;border:1px solid #203052;border-{(ar ? "right" : "left")}:4px solid #f59e0b;border-radius:10px;padding:10px 14px;margin:6px 0;word-break:break-all;font-size:13px}}
.warn{{border-{(ar ? "right" : "left")}-color:#ef4444}}
.meta{{color:#8a97b0;font-size:13px;line-height:1.9}}
.foot{{margin:30px 0 10px;text-align:center;color:#8a97b0;font-size:13px}} .foot a{{color:#2f80ff;text-decoration:none;font-weight:700}}
@media print{{body{{background:#fff;color:#000}}.head,.card,.item{{background:#fff;border-color:#ccc}}.card span,.meta,.foot{{color:#444}}}}
@media(max-width:560px){{.grid{{grid-template-columns:repeat(2,1fr)}}}}
</style></head><body><div class=""wrap"">
<div class=""head""><img src=""data:image/png;base64,{LogoBase64()}"" alt=""""><div>
<h1>USB Guard — {(ar ? "تقرير الفحص" : "Scan report")}</h1><p>{E(title)}</p></div></div>
<div class=""badge"">{E(status)}</div>
<div class=""grid"">
<div class=""card""><b>{threats}</b><span>{(ar ? "تهديدات" : "Threats")}</span></div>
<div class=""card""><b>{restored}</b><span>{(ar ? "ملفات مُسترجعة" : "Restored")}</span></div>
<div class=""card""><b>{(files > 0 ? files.ToString("N0") : "—")}</b><span>{(ar ? "ملفات مفحوصة" : "Files checked")}</span></div>
<div class=""card""><b>{dur}</b><span>{(ar ? "المدة" : "Duration")}</span></div>
</div>
<h2>{(ar ? "التفاصيل" : "Details")}</h2>
<div class=""meta"">{(ar ? "المسار" : "Target")}: <b>{E(target)}</b><br>
{(ar ? "التاريخ" : "Date")}: {started:yyyy-MM-dd HH:mm:ss}<br>
{(ar ? "الجهاز" : "Computer")}: {E(Environment.MachineName)} • Windows {E(Environment.OSVersion.Version.ToString())}<br>
{(ar ? "الإصدار" : "Version")}: USB Guard v{SystemTools.CurrentVersion.ToString(3)}</div>");

                if (list.Count > 0)
                {
                    sb.Append($"<h2>{(ar ? "العناصر الي انعزلت أو انصلحت" : "Isolated / repaired items")}</h2>");
                    foreach (var i in list.Take(300)) sb.Append($"<div class=\"item\">{E(i)}</div>");
                }
                if (warn.Count > 0)
                {
                    sb.Append($"<h2>{(ar ? "تنبيهات" : "Warnings")}</h2>");
                    foreach (var w in warn) sb.Append($"<div class=\"item warn\">{E(w)}</div>");
                }
                sb.Append($@"<div class=""foot"">{(ar ? "المطور" : "Developer")}: <a href=""{SystemTools.DeveloperUrl}"">{(ar ? "مهند سات لنك" : "Muhannad Sat Link")}</a>
 • <a href=""{SystemTools.RepoUrl}"">USB Guard</a></div></div></body></html>");

                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
                return file;
            }
            catch (Exception ex)
            {
                Settings.Log("فشل انشاء التقرير: " + ex.Message);
                return null;
            }
        }
    }
}

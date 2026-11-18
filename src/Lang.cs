using System.Collections.Generic;

namespace USBGuard
{
    /// <summary>نصوص البرنامج بالعربي والانكليزي</summary>
    public static class L
    {
        public static bool Ar = true;

        static readonly Dictionary<string, string[]> D = new Dictionary<string, string[]>
        {
            // key                      عربي                                         English
            ["protected"]       = new[] { "جهازك محمي",                          "You're protected" },
            ["protected_sub"]   = new[] { "الأوتورن معطّل • المراقبة شغالة",      "Autorun blocked • Monitoring on" },
            ["scanning"]        = new[] { "جاري الفحص...",                       "Scanning..." },
            ["clean"]           = new[] { "الفلاشة نظيفة",                       "Drive is clean" },
            ["cleaned"]         = new[] { "تم تنظيف الفلاشة",                    "Drive cleaned" },
            ["drives"]          = new[] { "الفلاشات المتصلة",                    "Connected drives" },
            ["no_drives"]       = new[] { "وصّل فلاشة حتى تفحصها",               "Plug in a USB drive to scan" },
            ["scan_btn"]        = new[] { "فحص الفلاشة",                         "Scan drive" },
            ["stop_btn"]        = new[] { "جاري الفحص...",                       "Scanning..." },
            ["st_threats"]      = new[] { "تهديدات محذوفة",                      "Threats removed" },
            ["st_restored"]     = new[] { "ملفات مُسترجعة",                      "Files restored" },
            ["st_last"]         = new[] { "آخر فحص",                             "Last scan" },
            ["never"]           = new[] { "—",                                   "—" },
            ["opt_startup"]     = new[] { "التشغيل مع ويندوز",                   "Start with Windows" },
            ["opt_autoscan"]    = new[] { "فحص تلقائي عند توصيل الفلاشة",        "Auto-scan on plug-in" },
            ["opt_deep"]        = new[] { "فحص عميق بـ Windows Defender",        "Deep scan with Defender" },
            ["opt_update"]      = new[] { "التحقق من التحديثات تلقائياً",        "Check for updates automatically" },
            ["dev"]             = new[] { "المطور: مهند سات لنك",                "Developer: Muhannad Sat Link" },
            ["check_update"]    = new[] { "تحقق من التحديث",                     "Check for updates" },
            ["view_log"]        = new[] { "السجل",                               "Log" },
            ["uninstall"]       = new[] { "إلغاء التثبيت",                       "Uninstall" },
            ["menu_scan"]       = new[] { "فحص بـ USB Guard",                    "Scan with USB Guard" },
            ["tray_open"]       = new[] { "فتح USB Guard",                       "Open USB Guard" },
            ["tray_exit"]       = new[] { "خروج",                                "Exit" },
            ["step_prepare"]    = new[] { "تجهيز الفحص...",                      "Preparing..." },
            ["step_autorun"]    = new[] { "فحص ملفات الأوتورن...",               "Checking autorun..." },
            ["step_exe"]        = new[] { "البحث عن ملفات مخفية خبيثة...",       "Looking for hidden malware..." },
            ["step_lnk"]        = new[] { "فحص الاختصارات (Shortcut)...",        "Checking shortcuts..." },
            ["step_unhide"]     = new[] { "استرجاع الملفات المخفية...",          "Restoring hidden files..." },
            ["step_known"]      = new[] { "حذف الفايروسات المعروفة...",          "Removing known threats..." },
            ["step_vaccine"]    = new[] { "تلقيح الفلاشة ضد الأوتورن...",        "Vaccinating drive..." },
            ["step_defender"]   = new[] { "فحص عميق بـ Defender (قد يأخذ وقت)...", "Deep scan with Defender (may take a while)..." },
            ["step_done"]       = new[] { "انتهى الفحص",                         "Scan finished" },
            ["res_clean"]       = new[] { "ما لگينا أي فايروس. الفلاشة نظيفة ✓",  "No threats found. Drive is clean ✓" },
            ["res_found"]       = new[] { "تم حذف {0} تهديد واسترجاع {1} ملف",    "Removed {0} threat(s), restored {1} file(s)" },
            ["res_defender"]    = new[] { "Defender لگى تهديدات وتعامل وياها",    "Defender found and handled threats" },
            ["not_removable"]   = new[] { "هذا القرص مو فلاشة. البرنامج يفحص الفلاشات والأقراص الخارجية بس.", "This is not a removable drive. USB Guard scans USB drives only." },
            ["not_ready"]       = new[] { "القرص مو جاهز",                       "Drive is not ready" },
            ["new_drive"]       = new[] { "تم توصيل فلاشة: {0}",                  "USB drive connected: {0}" },
            ["upd_title"]       = new[] { "تحديث جديد",                          "Update available" },
            ["upd_ask"]         = new[] { "يتوفر إصدار جديد من USB Guard\n\nالإصدار الحالي: {0}\nالإصدار الجديد: {1}\n\n{2}\n\nتريد تحدّث الحين؟", "A new version of USB Guard is available.\n\nCurrent: {0}\nNew: {1}\n\n{2}\n\nUpdate now?" },
            ["upd_none"]        = new[] { "عندك آخر إصدار ✓",                    "You have the latest version ✓" },
            ["upd_fail"]        = new[] { "ما گدرنا نتحقق من التحديث. تأكد من الإنترنت.", "Couldn't check for updates. Check your internet." },
            ["upd_downloading"] = new[] { "جاري تنزيل التحديث...",               "Downloading update..." },
            ["upd_balloon"]     = new[] { "اضغط هنا حتى تحدّث USB Guard إلى {0}", "Click here to update USB Guard to {0}" },
            ["uninstall_ask"]   = new[] { "متأكد تريد تحذف USB Guard؟\n(إعدادات تعطيل الأوتورن تبقى لحماية جهازك)", "Remove USB Guard?\n(Autorun protection settings will stay to keep you safe)" },
            ["running_tray"]    = new[] { "USB Guard شغال بالخلفية ويراقب الفلاشات", "USB Guard is running in the background" },
            ["installed"]       = new[] { "تم تثبيت USB Guard وصار يحمي جهازك ✓", "USB Guard is installed and protecting you ✓" },
        };

        public static string T(string key)
        {
            if (D.TryGetValue(key, out var v)) return Ar ? v[0] : v[1];
            return key;
        }

        public static string F(string key, params object[] args) => string.Format(T(key), args);
    }
}

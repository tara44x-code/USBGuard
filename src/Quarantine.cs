using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace USBGuard
{
    public class QuarantineItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string OriginalPath { get; set; }
        public string Reason { get; set; }
        public string Source { get; set; }      // USB / PC
        public string Date { get; set; }        // yyyy-MM-dd HH:mm
        public long Size { get; set; }
        public bool IsDirectory { get; set; }
    }

    /// <summary>
    /// الحجر الصحي: بدل الحذف النهائي، الملف يتعزل بشكل مُعطّل (مشفّر XOR) وما يگدر يشتغل.
    /// المستخدم يگدر يرجّعه او يحذفه نهائياً.
    /// </summary>
    public static class Quarantine
    {
        static readonly object Lock = new object();
        const long MaxSize = 60L * 1024 * 1024;
        const byte Key = 0x5A;

        public static event Action Changed;

        static string Dir
        {
            get
            {
                var d = Path.Combine(Settings.DataDir, "Quarantine");
                Directory.CreateDirectory(d);
                return d;
            }
        }
        static string IndexFile => Path.Combine(Dir, "index.json");

        public static List<QuarantineItem> Items()
        {
            lock (Lock) return Load();
        }

        public static int Count
        {
            get { lock (Lock) return Load().Count; }
        }

        /// <summary>يعزل ملف او مجلد. يرجع false اذا ما گدر (والمستدعي يحذفه عادي)</summary>
        public static bool Add(string path, string reason, string source)
        {
            lock (Lock)
            {
                string dest = null;
                try
                {
                    var id = Guid.NewGuid().ToString("N");
                    dest = Path.Combine(Dir, id + ".qrt");
                    bool isDir = Directory.Exists(path);
                    long size;

                    if (isDir)
                    {
                        ClearAttributesRecursive(path);
                        var tmpZip = dest + ".zip";
                        ZipFile.CreateFromDirectory(path, tmpZip, CompressionLevel.Fastest, false);
                        size = new FileInfo(tmpZip).Length;
                        if (size > MaxSize) { File.Delete(tmpZip); return false; }
                        XorCopy(tmpZip, dest);
                        File.Delete(tmpZip);
                        Directory.Delete(path, true);
                    }
                    else if (File.Exists(path))
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        size = new FileInfo(path).Length;
                        if (size > MaxSize) return false;
                        XorCopy(path, dest);
                        File.Delete(path);
                    }
                    else return false;

                    var list = Load();
                    list.Insert(0, new QuarantineItem
                    {
                        Id = id,
                        Name = Path.GetFileName(path.TrimEnd('\\')),
                        OriginalPath = path,
                        Reason = reason ?? "",
                        Source = source ?? "",
                        Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                        Size = size,
                        IsDirectory = isDir,
                    });
                    Save(list);
                    Settings.Log($"  [حجر صحي] {path}  ({reason})");
                    Changed?.Invoke();
                    return true;
                }
                catch (Exception ex)
                {
                    // اذا انكتبت نسخة بالحجر بس الاصل ما انحذف: نمسح النسخة حتى ما تتكرر
                    if (dest != null) try { File.Delete(dest); } catch { }
                    try { File.Delete(dest + ".zip"); } catch { }
                    Settings.Log($"  [فشل الحجر] {path}: {ex.Message}");
                    return false;
                }
            }
        }

        public static bool Restore(string id, out string restoredTo)
        {
            restoredTo = null;
            lock (Lock)
            {
                var list = Load();
                var item = list.FirstOrDefault(i => i.Id == id);
                if (item == null) return false;
                var src = Path.Combine(Dir, id + ".qrt");
                if (!File.Exists(src)) { list.Remove(item); Save(list); return false; }

                try
                {
                    var target = item.OriginalPath;
                    var parent = Path.GetDirectoryName(target.TrimEnd('\\'));
                    if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                    {
                        // الفلاشة مو متصلة: نرجعه لسطح المكتب
                        parent = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        target = Path.Combine(parent, item.Name);
                    }
                    target = UniquePath(target);

                    if (item.IsDirectory)
                    {
                        var tmpZip = Path.Combine(Path.GetTempPath(), id + ".zip");
                        XorCopy(src, tmpZip);
                        ZipFile.ExtractToDirectory(tmpZip, target);
                        File.Delete(tmpZip);
                    }
                    else
                    {
                        XorCopy(src, target);
                    }
                    File.Delete(src);
                    list.Remove(item);
                    Save(list);
                    restoredTo = target;
                    Settings.Log($"[استرجاع من الحجر] {target}");
                    Changed?.Invoke();
                    return true;
                }
                catch (Exception ex)
                {
                    Settings.Log($"[فشل الاسترجاع] {item.OriginalPath}: {ex.Message}");
                    return false;
                }
            }
        }

        public static void Delete(string id)
        {
            lock (Lock)
            {
                var list = Load();
                var item = list.FirstOrDefault(i => i.Id == id);
                try { File.Delete(Path.Combine(Dir, id + ".qrt")); } catch { }
                if (item != null) { list.Remove(item); Save(list); }
                Changed?.Invoke();
            }
        }

        public static void DeleteAll()
        {
            lock (Lock)
            {
                foreach (var f in Directory.GetFiles(Dir, "*.qrt")) try { File.Delete(f); } catch { }
                Save(new List<QuarantineItem>());
                Changed?.Invoke();
            }
        }

        // ------------------------------------------------------------------
        static List<QuarantineItem> Load()
        {
            try
            {
                if (!File.Exists(IndexFile)) return new List<QuarantineItem>();
                return new JavaScriptSerializer().Deserialize<List<QuarantineItem>>(File.ReadAllText(IndexFile, Encoding.UTF8))
                       ?? new List<QuarantineItem>();
            }
            catch { return new List<QuarantineItem>(); }
        }

        static void Save(List<QuarantineItem> list)
        {
            try { File.WriteAllText(IndexFile, new JavaScriptSerializer().Serialize(list), Encoding.UTF8); } catch { }
        }

        static void XorCopy(string src, string dst)
        {
            var b = File.ReadAllBytes(src);
            for (int i = 0; i < b.Length; i++) b[i] ^= Key;
            File.WriteAllBytes(dst, b);
        }

        static string UniquePath(string p)
        {
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
            var dir = Path.GetDirectoryName(p);
            var name = Path.GetFileNameWithoutExtension(p);
            var ext = Path.GetExtension(p);
            for (int i = 1; i < 1000; i++)
            {
                var c = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(c) && !Directory.Exists(c)) return c;
            }
            return p;
        }

        static void ClearAttributesRecursive(string dir)
        {
            try
            {
                foreach (var e in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
                    try { File.SetAttributes(e, Directory.Exists(e) ? FileAttributes.Directory : FileAttributes.Normal); } catch { }
                File.SetAttributes(dir, FileAttributes.Directory);
            }
            catch { }
        }
    }

    // =====================================================================
    //  سجل الفحوصات
    // =====================================================================
    public class ScanRecord
    {
        public string Date { get; set; }
        public string Target { get; set; }     // E:\  او  PC
        public int Threats { get; set; }
        public int Restored { get; set; }
        public List<string> Items { get; set; } = new List<string>();
        public string Report { get; set; }
        public string Duration { get; set; }
        public long Files { get; set; }
    }

    public static class History
    {
        static readonly object Lock = new object();
        static string FilePath => Path.Combine(Settings.DataDir, "history.json");
        public static event Action Changed;

        public static List<ScanRecord> Items()
        {
            lock (Lock)
            {
                try
                {
                    if (!File.Exists(FilePath)) return new List<ScanRecord>();
                    return new JavaScriptSerializer().Deserialize<List<ScanRecord>>(File.ReadAllText(FilePath, Encoding.UTF8))
                           ?? new List<ScanRecord>();
                }
                catch { return new List<ScanRecord>(); }
            }
        }

        public static void Add(ScanRecord r)
        {
            lock (Lock)
            {
                var list = Items();
                list.Insert(0, r);
                if (list.Count > 100) list = list.Take(100).ToList();
                try { File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(list), Encoding.UTF8); } catch { }
            }
            Changed?.Invoke();
        }

        public static void Clear()
        {
            lock (Lock) { try { File.Delete(FilePath); } catch { } }
            Changed?.Invoke();
        }
    }
}

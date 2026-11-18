using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USBGuard
{
    public class MainForm : Form
    {
        // ---------------- Win32 ----------------
        const int WM_DEVICECHANGE = 0x0219, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_VOLUME = 2;
        const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2;
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        // ---------------- UI ----------------
        readonly StatusRing _ring = new StatusRing();
        readonly TextBlock _title = new TextBlock(), _sub = new TextBlock(), _msg = new TextBlock();
        readonly Panel _drivesArea = new DoubleBufferedPanel();
        readonly TextBlock _noDrives = new TextBlock();
        readonly GradientButton _scanBtn = new GradientButton();
        readonly StatCard _stThreats = new StatCard(), _stRestored = new StatCard(), _stLast = new StatCard();
        readonly ToggleRow _tStartup = new ToggleRow(), _tAuto = new ToggleRow(), _tDeep = new ToggleRow(), _tUpdate = new ToggleRow();
        readonly LinkText _dev = new LinkText(), _ver = new LinkText(), _lnkUpdate = new LinkText(), _lnkLog = new LinkText(), _lnkUninstall = new LinkText();
        readonly TitleButton _btnClose = new TitleButton { Type = TitleButton.Kind.Close };
        readonly TitleButton _btnMin = new TitleButton { Type = TitleButton.Kind.Minimize };
        readonly TitleButton _btnLang = new TitleButton { Type = TitleButton.Kind.Lang };
        readonly NotifyIcon _tray = new NotifyIcon();
        readonly ContextMenuStrip _trayMenu = new ContextMenuStrip();
        readonly List<DriveRow> _rows = new List<DriveRow>();
        readonly Image _logo;

        // ---------------- state ----------------
        bool _allowShow;
        bool _exiting;
        bool _scanning;
        string _selectedRoot;
        readonly Queue<string> _pending = new Queue<string>();
        UpdateInfo _pendingUpdate;
        bool _trayHintShown;
        readonly string _startScan;
        readonly bool _justUpdated, _justInstalled;

        public MainForm(bool startHidden, string scanTarget, bool justUpdated, bool justInstalled)
        {
            _allowShow = !startHidden;
            _startScan = scanTarget;
            _justUpdated = justUpdated;
            _justInstalled = justInstalled;

            using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("USBGuard.logo.png"))
            {
                if (s != null) using (var img = Image.FromStream(s)) _logo = new Bitmap(img);
            }
            using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("USBGuard.icon.ico"))
                Icon = s != null ? new Icon(s) : SystemIcons.Shield;

            Text = "USB Guard";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            ClientSize = new Size(Theme.S(360), Theme.S(704));
            DoubleBuffered = true;
            MaximizeBox = false;
            ShowInTaskbar = true;

            BuildUi();
            BuildTray();
            ApplyLanguage();
            RefreshStats();
            RefreshDrives();

            var _ = Handle; // انشاء النافذة حتى تستلم رسائل توصيل الفلاشة حتى لو مخفية
            StartPipeServer();
        }

        // ==================================================================
        //  بناء الواجهة
        // ==================================================================
        void BuildUi()
        {
            int W = 360;
            Rectangle R(int x, int y, int w, int h) => new Rectangle(Theme.S(x), Theme.S(y), Theme.S(w), Theme.S(h));

            // شريط العنوان
            _btnClose.Bounds = R(W - 42, 6, 34, 32);
            _btnMin.Bounds = R(W - 78, 6, 34, 32);
            _btnLang.Bounds = R(W - 114, 6, 34, 32);
            _btnClose.Click += (s, e) => HideToTray();
            _btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            _btnLang.Click += (s, e) => ToggleLanguage();

            // دائرة الحالة
            _ring.Logo = _logo;
            _ring.Bounds = R(100, 48, 160, 160);

            _title.Bounds = R(16, 210, W - 32, 28);
            _title.Font = Theme.Font(15f, FontStyle.Bold);
            _sub.Bounds = R(16, 238, W - 32, 18);
            _sub.Font = Theme.Font(9f);
            _sub.Color = Theme.Muted;

            // الفلاشات
            _drivesArea.Bounds = R(20, 266, W - 40, 108);
            _drivesArea.BackColor = Color.Transparent;
            _noDrives.Dock = DockStyle.Fill;
            _noDrives.Font = Theme.Font(9.5f);
            _noDrives.Color = Theme.Muted;
            _noDrives.Dashed = true;
            _drivesArea.Controls.Add(_noDrives);

            // زر الفحص
            _scanBtn.Bounds = R(20, 384, W - 40, 48);
            _scanBtn.Click += (s, e) => { if (_selectedRoot != null) StartScan(_selectedRoot); };

            _msg.Bounds = R(16, 434, W - 32, 20);
            _msg.Font = Theme.Font(8.5f);
            _msg.Color = Theme.Muted;

            // الاحصائيات
            int cw = (W - 40 - 16) / 3;
            _stThreats.Bounds = R(20, 458, cw, 52);
            _stRestored.Bounds = R(20 + cw + 8, 458, cw, 52);
            _stLast.Bounds = R(20 + (cw + 8) * 2, 458, cw, 52);
            _stThreats.ValueColor = Theme.Amber;
            _stRestored.ValueColor = Theme.Green;

            // الاعدادات
            int ty = 522;
            foreach (var t in new[] { _tStartup, _tAuto, _tDeep, _tUpdate })
            {
                t.Bounds = R(24, ty, W - 48, 30);
                ty += 30;
            }
            _tStartup.SetSilently(SystemTools.IsStartupEnabled());
            _tAuto.SetSilently(Settings.AutoScan);
            _tDeep.SetSilently(Settings.DeepScan);
            _tUpdate.SetSilently(Settings.AutoUpdate);
            _tStartup.CheckedChanged += (s, e) => { var v = _tStartup.Checked; Task.Run(() => SystemTools.SetStartup(v)); };
            _tAuto.CheckedChanged += (s, e) => Settings.AutoScan = _tAuto.Checked;
            _tDeep.CheckedChanged += (s, e) => Settings.DeepScan = _tDeep.Checked;
            _tUpdate.CheckedChanged += (s, e) => Settings.AutoUpdate = _tUpdate.Checked;

            // الفوتر
            _dev.Bounds = R(16, 654, W - 32, 20);
            _dev.Font = Theme.Font(9f, FontStyle.Bold);
            _dev.Normal = Theme.Accent;
            _dev.Click += (s, e) => SystemTools.OpenUrl(SystemTools.DeveloperUrl);

            _ver.Text = "v" + SystemTools.CurrentVersion.ToString(3);
            _ver.Click += (s, e) => SystemTools.OpenUrl(SystemTools.RepoUrl);
            _lnkUpdate.Click += async (s, e) => await CheckUpdates(manual: true);
            _lnkLog.Click += (s, e) => { try { Process.Start("notepad.exe", $"\"{Settings.LogFile}\""); } catch { } };
            _lnkUninstall.Click += (s, e) => DoUninstall();
            _lnkUninstall.HoverColor = Theme.Red;

            Controls.AddRange(new Control[] {
                _btnClose, _btnMin, _btnLang, _ring, _title, _sub, _drivesArea, _scanBtn, _msg,
                _stThreats, _stRestored, _stLast, _tStartup, _tAuto, _tDeep, _tUpdate,
                _dev, _ver, _lnkUpdate, _lnkLog, _lnkUninstall });
        }

        void LayoutFooter()
        {
            var links = new[] { _ver, _lnkUpdate, _lnkLog, _lnkUninstall };
            int gap = Theme.S(14);
            var widths = links.Select(l => TextRenderer.MeasureText(l.Text, l.Font).Width + Theme.S(4)).ToArray();
            int total = widths.Sum() + gap * (links.Length - 1);
            int x = (ClientSize.Width - total) / 2;
            var order = L.Ar ? Enumerable.Range(0, links.Length).Reverse() : Enumerable.Range(0, links.Length);
            foreach (var i in order)
            {
                links[i].Bounds = new Rectangle(x, Theme.S(676), widths[i], Theme.S(18));
                x += widths[i] + gap;
            }
        }

        void BuildTray()
        {
            _tray.Icon = new Icon(Icon, SystemInformation.SmallIconSize);
            _tray.Text = "USB Guard";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _trayMenu;
            _tray.DoubleClick += (s, e) => ShowMain();
            _tray.BalloonTipClicked += async (s, e) =>
            {
                if (_pendingUpdate != null) await PromptUpdate(_pendingUpdate);
                else ShowMain();
            };
            _trayMenu.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            _trayMenu.ShowImageMargin = false;
        }

        void RebuildTrayMenu()
        {
            _trayMenu.Items.Clear();
            _trayMenu.RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No;
            _trayMenu.Items.Add(L.T("tray_open"), null, (s, e) => ShowMain());
            _trayMenu.Items.Add(L.T("check_update"), null, async (s, e) => await CheckUpdates(manual: true));
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(L.T("tray_exit"), null, (s, e) => ExitApp());
            foreach (ToolStripItem i in _trayMenu.Items) i.ForeColor = Theme.Text;
        }

        // ==================================================================
        //  اللغة
        // ==================================================================
        void ToggleLanguage()
        {
            Settings.Arabic = !Settings.Arabic;
            L.Ar = Settings.Arabic;
            ApplyLanguage();
            Task.Run(() => SystemTools.RegisterContextMenu()); // تحديث نص القائمة اليمنى
        }

        void ApplyLanguage()
        {
            L.Ar = Settings.Arabic;
            _btnLang.Text = L.Ar ? "EN" : "ع";
            _scanBtn.Text = _scanning ? L.T("stop_btn") : L.T("scan_btn");
            _noDrives.Text = L.T("no_drives");
            _stThreats.Caption = L.T("st_threats");
            _stRestored.Caption = L.T("st_restored");
            _stLast.Caption = L.T("st_last");
            _tStartup.Text = L.T("opt_startup");
            _tAuto.Text = L.T("opt_autoscan");
            _tDeep.Text = L.T("opt_deep");
            _tUpdate.Text = L.T("opt_update");
            _dev.Text = L.T("dev");
            _lnkUpdate.Text = L.T("check_update");
            _lnkLog.Text = L.T("view_log");
            _lnkUninstall.Text = L.T("uninstall");
            if (!_scanning) SetState(_ring.State == GuardState.Scanning ? GuardState.Protected : _ring.State, _lastSub);
            RebuildTrayMenu();
            LayoutFooter();
            RefreshDrives();
            foreach (Control c in Controls) c.Invalidate();
            Invalidate();
        }

        string _lastSub;
        void SetState(GuardState st, string sub = null)
        {
            _ring.State = st;
            switch (st)
            {
                case GuardState.Protected:
                    _title.Text = L.T("protected"); _title.Color = Theme.Text;
                    _sub.Text = L.T("protected_sub");
                    break;
                case GuardState.Scanning:
                    _title.Text = L.T("scanning"); _title.Color = Theme.Accent2;
                    _sub.Text = sub ?? "";
                    break;
                case GuardState.Clean:
                    _title.Text = L.T("clean"); _title.Color = Theme.Green;
                    _sub.Text = sub ?? L.T("res_clean");
                    break;
                case GuardState.Threat:
                    _title.Text = L.T("cleaned"); _title.Color = Theme.Amber;
                    _sub.Text = sub ?? "";
                    break;
            }
            _lastSub = st == GuardState.Protected ? null : _sub.Text;
            _title.Invalidate(); _sub.Invalidate();
        }

        // ==================================================================
        //  الفلاشات
        // ==================================================================
        void RefreshDrives()
        {
            List<DriveInfo> drives;
            try { drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && SafeReady(d)).ToList(); }
            catch { drives = new List<DriveInfo>(); }

            foreach (var r in _rows) { _drivesArea.Controls.Remove(r); r.Dispose(); }
            _rows.Clear();

            if (_selectedRoot == null || !drives.Any(d => d.Name == _selectedRoot))
                _selectedRoot = drives.FirstOrDefault()?.Name;

            int n = Math.Min(drives.Count, 4);
            int gap = Theme.S(8);
            int areaH = _drivesArea.Height;
            int rowH = n == 0 ? 0 : Math.Min(Theme.S(50), (areaH - gap * (n - 1)) / n);

            for (int i = 0; i < n; i++)
            {
                var d = drives[i];
                var row = new DriveRow
                {
                    Root = d.Name,
                    Label = string.IsNullOrWhiteSpace(SafeLabel(d)) ? (L.Ar ? "فلاشة USB" : "USB Drive") : SafeLabel(d),
                    SizeText = SizeText(d),
                    Selected = d.Name == _selectedRoot,
                    Bounds = new Rectangle(0, i * (rowH + gap), _drivesArea.Width, rowH),
                };
                row.Click += (s, e) => { _selectedRoot = row.Root; foreach (var x in _rows) x.Selected = x == row; };
                row.DoubleClick += (s, e) => StartScan(row.Root);
                _rows.Add(row);
                _drivesArea.Controls.Add(row);
            }
            _noDrives.Visible = n == 0;
            _scanBtn.Enabled = n > 0 && !_scanning;
        }

        static bool SafeReady(DriveInfo d) { try { return d.IsReady; } catch { return false; } }
        static string SafeLabel(DriveInfo d) { try { return d.VolumeLabel; } catch { return ""; } }
        static string SizeText(DriveInfo d)
        {
            try
            {
                double tot = d.TotalSize / 1073741824.0, free = d.AvailableFreeSpace / 1073741824.0;
                return L.Ar ? $"{free:0.#} GB متاح من {tot:0.#} GB • {d.DriveFormat}"
                            : $"{free:0.#} GB free of {tot:0.#} GB • {d.DriveFormat}";
            }
            catch { return ""; }
        }

        void RefreshStats()
        {
            _stThreats.Value = Settings.TotalThreats.ToString();
            _stRestored.Value = Settings.TotalRestored.ToString();
            var last = Settings.LastScan;
            _stLast.Value = string.IsNullOrEmpty(last) ? L.T("never") : last;
            _stThreats.Invalidate(); _stRestored.Invalidate(); _stLast.Invalidate();
        }

        // ==================================================================
        //  الفحص
        // ==================================================================
        async void StartScan(string root)
        {
            root = Scanner.NormalizeRoot(root);
            if (root == null) return;

            if (_scanning)
            {
                if (!_pending.Contains(root)) _pending.Enqueue(root);
                return;
            }

            if (!Scanner.IsScannable(root, out var reason))
            {
                Notify(reason, ToolTipIcon.Warning, forceDialog: Visible);
                return;
            }

            _scanning = true;
            _selectedRoot = root;
            foreach (var x in _rows) x.Selected = x.Root == root;
            _scanBtn.Enabled = false;
            _scanBtn.Text = L.T("stop_btn");
            _ring.Progress = 0;
            SetState(GuardState.Scanning, root);

            var progress = new Progress<(int pct, string msg)>(p =>
            {
                _ring.Progress = p.pct;
                _msg.Text = p.msg; _msg.Invalidate();
            });

            var deep = Settings.DeepScan;
            var result = await Task.Run(() =>
            {
                SystemTools.RepairSystem();
                return Scanner.Scan(root, deep, progress);
            });

            _scanning = false;
            Settings.TotalThreats += result.Threats;
            Settings.TotalRestored += result.Restored;
            Settings.LastScan = DateTime.Now.ToString("HH:mm");
            RefreshStats();

            string summary;
            if (result.HadThreats || result.Restored > 0)
            {
                summary = L.F("res_found", result.Threats, result.Restored);
                if (result.DefenderFound) summary += " • " + L.T("res_defender");
                SetState(result.HadThreats ? GuardState.Threat : GuardState.Clean, summary);
                Notify($"{root}  {summary}", result.HadThreats ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
            else
            {
                summary = L.T("res_clean");
                SetState(GuardState.Clean, summary);
                if (!Visible) Notify($"{root}  {summary}", ToolTipIcon.Info);
            }
            _msg.Text = L.T("step_done") + " • " + DateTime.Now.ToString("HH:mm:ss"); _msg.Invalidate();

            _scanBtn.Text = L.T("scan_btn");
            RefreshDrives();

            if (_pending.Count > 0) StartScan(_pending.Dequeue());
        }

        // ==================================================================
        //  مراقبة توصيل الفلاشة
        // ==================================================================
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE && m.LParam != IntPtr.Zero)
            {
                int ev = m.WParam.ToInt32();
                if (ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE)
                {
                    int type = Marshal.ReadInt32(m.LParam, 4);
                    if (type == DBT_DEVTYP_VOLUME)
                    {
                        int mask = Marshal.ReadInt32(m.LParam, 12);
                        var letters = new List<string>();
                        for (int i = 0; i < 26; i++) if ((mask & (1 << i)) != 0) letters.Add((char)('A' + i) + @":\");
                        if (ev == DBT_DEVICEARRIVAL) OnDrivesArrived(letters);
                        else RefreshDrives();
                    }
                }
            }
            base.WndProc(ref m);
        }

        async void OnDrivesArrived(List<string> roots)
        {
            await Task.Delay(1200);
            RefreshDrives();
            foreach (var root in roots)
            {
                // انتظار جاهزية الفلاشة
                for (int i = 0; i < 10; i++)
                {
                    bool ok = false;
                    try { var d = new DriveInfo(root); ok = d.DriveType == DriveType.Removable && d.IsReady; } catch { }
                    if (ok) break;
                    await Task.Delay(500);
                }
                Settings.Log($"تم توصيل فلاشة: {root}");
                if (Settings.AutoScan)
                {
                    if (!Visible) Notify(L.F("new_drive", root), ToolTipIcon.Info);
                    StartScan(root);
                }
            }
        }

        // ==================================================================
        //  رسائل من نسخة ثانية (القائمة اليمنى)
        // ==================================================================
        void StartPipeServer()
        {
            var t = new Thread(() =>
            {
                while (!_exiting)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(Program.PipeName, PipeDirection.In, 1);
                        server.WaitForConnection();
                        using var rd = new StreamReader(server, Encoding.UTF8);
                        var msg = rd.ReadToEnd();
                        if (IsHandleCreated) BeginInvoke(new Action(() => HandleExternal(msg)));
                    }
                    catch { Thread.Sleep(500); }
                }
            }) { IsBackground = true, Name = "USBGuardPipe" };
            t.Start();
        }

        void HandleExternal(string msg)
        {
            msg = (msg ?? "").Trim();
            if (msg == "EXIT") { ExitApp(); return; }
            ShowMain();
            if (msg.StartsWith("SCAN|")) StartScan(msg.Substring(5));
        }

        // ==================================================================
        //  التحديث
        // ==================================================================
        async Task CheckUpdates(bool manual)
        {
            try
            {
                var info = await Updater.CheckAsync();
                if (info == null)
                {
                    if (manual) Msg(L.T("upd_none"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!manual && Settings.SkippedVersion == info.Tag) return;

                if (Visible || manual) await PromptUpdate(info);
                else
                {
                    _pendingUpdate = info;
                    _tray.ShowBalloonTip(15000, L.T("upd_title"), L.F("upd_balloon", info.Tag), ToolTipIcon.Info);
                }
            }
            catch
            {
                if (manual) Msg(L.T("upd_fail"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        async Task PromptUpdate(UpdateInfo info)
        {
            ShowMain();
            var ask = L.F("upd_ask", "v" + SystemTools.CurrentVersion.ToString(3), info.Tag, info.Notes);
            if (Msg(ask, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            {
                Settings.SkippedVersion = info.Tag; // ما نزعجه بنفس الاصدار مرة ثانية (يگدر يحدث يدوياً)
                _pendingUpdate = null;
                return;
            }
            try
            {
                _msg.Text = L.T("upd_downloading"); _msg.Invalidate();
                _scanBtn.Enabled = false;
                await Updater.DownloadAndApplyAsync(info);
                ExitApp();
            }
            catch
            {
                _scanBtn.Enabled = _rows.Count > 0;
                _msg.Text = ""; _msg.Invalidate();
                Msg(L.T("upd_fail"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ==================================================================
        //  نافذة / ترَي
        // ==================================================================
        protected override void SetVisibleCore(bool value)
        {
            if (!_allowShow)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override async void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int pref = 2; // DWMWCP_ROUND - زوايا دائرية بويندوز 11
                DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
                int dark = 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            }
            catch { }

            if (_startedOnce) return;
            _startedOnce = true;

            await Task.Run(() => { SystemTools.DisableAutorun(); SystemTools.RepairSystem(); });

            if (_justInstalled) Notify(L.T("installed"), ToolTipIcon.Info);
            else if (_justUpdated) Notify("USB Guard v" + SystemTools.CurrentVersion.ToString(3) + " ✓", ToolTipIcon.Info);

            if (_startScan != null) { ShowMain(); StartScan(_startScan); }
            else if (Settings.AutoScan)
            {
                // فحص الفلاشات المتصلة وقت التشغيل
                foreach (var r in _rows.Select(x => x.Root).ToList()) StartScan(r);
            }

            if (Settings.AutoUpdate)
            {
                await Task.Delay(8000);
                await CheckUpdates(manual: false);
            }
        }
        bool _startedOnce;

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            _ring.Animate = Visible; // ما نستهلك المعالج والبرنامج بالترَي
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        void ShowMain()
        {
            _allowShow = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        void HideToTray()
        {
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(3000, "USB Guard", L.T("running_tray"), ToolTipIcon.Info);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        void ExitApp()
        {
            _exiting = true;
            _tray.Visible = false;
            _tray.Dispose();
            Application.Exit();
        }

        void DoUninstall()
        {
            if (Msg(L.T("uninstall_ask"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SystemTools.Uninstall();
            ExitApp();
        }

        // سحب النافذة من شريط العنوان
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < Theme.S(44))
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; Theme.HQ(g);

            // خلفية متدرجة خفيفة
            using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(14, 22, 42), Theme.Bg, 90f))
                g.FillRectangle(bg, ClientRectangle);

            // شعار + اسم البرنامج
            if (_logo != null) g.DrawImage(_logo, Theme.S(14), Theme.S(10), Theme.S(24), Theme.S(24));
            using (var f = Theme.Font(11f, FontStyle.Bold))
            using (var b = new SolidBrush(Theme.Text))
                g.DrawString("USB Guard", f, b, Theme.S(44), Theme.S(11));

            // خط فاصل فوق الفوتر
            using (var p = new Pen(Theme.Border))
                g.DrawLine(p, Theme.S(24), Theme.S(646), ClientSize.Width - Theme.S(24), Theme.S(646));

            // اطار النافذة
            using (var p = new Pen(Theme.Border))
                g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // ==================================================================
        //  رسائل
        // ==================================================================
        void Notify(string text, ToolTipIcon icon, bool forceDialog = false)
        {
            if (forceDialog) { Msg(text, MessageBoxButtons.OK, icon == ToolTipIcon.Warning ? MessageBoxIcon.Warning : MessageBoxIcon.Information); return; }
            try { _tray.ShowBalloonTip(5000, "USB Guard", text, icon); } catch { }
        }

        DialogResult Msg(string text, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            var opt = L.Ar ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
            return MessageBox.Show(Visible ? this : null, text, "USB Guard", buttons, icon, MessageBoxDefaultButton.Button1, opt);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { _tray.Dispose(); } catch { } _logo?.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // ======================================================================
    //  كنترولات مساعدة
    // ======================================================================
    public class TextBlock : PaintedControl
    {
        public Color Color = Theme.Text;
        public bool Dashed;
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            if (Dashed)
            {
                var r = new RectangleF(1, 1, Width - 2, Height - 2);
                using var p = Theme.Round(r, Theme.S(12));
                using var pen = new Pen(Theme.Border, 1.4f) { DashStyle = DashStyle.Dash };
                g.DrawPath(pen, p);
            }
            Theme.DrawText(g, Text, Font, Color, ClientRectangle);
        }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() { DoubleBuffered = true; }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Card;
        public override Color MenuItemSelected => Theme.CardHover;
        public override Color MenuItemBorder => Theme.Accent;
        public override Color MenuBorder => Theme.Border;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color ImageMarginGradientBegin => Theme.Card;
        public override Color ImageMarginGradientMiddle => Theme.Card;
        public override Color ImageMarginGradientEnd => Theme.Card;
    }
}

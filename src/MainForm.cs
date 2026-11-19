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
using Microsoft.Win32;

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

        const int W = 360, TitleH = 44, PageH = 556, TabH = 60;

        // ---------------- shared ----------------
        readonly Image _logo;
        readonly NotifyIcon _tray = new NotifyIcon();
        readonly ContextMenuStrip _trayMenu = new ContextMenuStrip();
        readonly TitleButton _btnClose = new TitleButton { Type = TitleButton.Kind.Close };
        readonly TitleButton _btnMin = new TitleButton { Type = TitleButton.Kind.Minimize };
        readonly TitleButton _btnLang = new TitleButton { Type = TitleButton.Kind.Lang };
        readonly TabBar _tabs = new TabBar();
        readonly Panel[] _pages = new Panel[5];
        readonly System.Windows.Forms.Timer _repairTimer = new System.Windows.Forms.Timer { Interval = 60000 };

        // ---------------- page 0: USB ----------------
        readonly StatusRing _ring = new StatusRing();
        readonly TextBlock _title = new TextBlock(), _sub = new TextBlock(), _msg = new TextBlock();
        readonly DriveList _driveList = new DriveList();
        readonly GradientButton _scanBtn = new GradientButton();
        readonly OutlineButton _pathBtn = new OutlineButton();
        readonly ContextMenuStrip _pathMenu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer _elapsedTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        CancellationTokenSource _cts;
        DateTime _scanStart;

        // ---------------- page 1: PC ----------------
        readonly StatusRing _pcRing = new StatusRing();
        readonly TextBlock _pcTitle = new TextBlock(), _pcSub = new TextBlock(), _pcMsg = new TextBlock();
        readonly GradientButton _pcBtn = new GradientButton();
        readonly Banner _salityBanner = new Banner();
        readonly CardList _pcList = new CardList();

        // ---------------- page 2: quarantine ----------------
        readonly TextBlock _qTitle = new TextBlock(), _qSub = new TextBlock();
        readonly LinkText _qDeleteAll = new LinkText();
        readonly CardList _qList = new CardList();

        // ---------------- page 3: history ----------------
        readonly TextBlock _hTitle = new TextBlock(), _hSub = new TextBlock();
        readonly LinkText _hClear = new LinkText(), _hLog = new LinkText();
        readonly CardList _hList = new CardList();

        // ---------------- page 4: settings ----------------
        readonly TextBlock _sTitle = new TextBlock();
        readonly ToggleRow _tStartup = new ToggleRow(), _tAuto = new ToggleRow(), _tDeep = new ToggleRow(),
                           _tPc = new ToggleRow(), _tSelf = new ToggleRow(), _tUpdate = new ToggleRow();
        readonly AboutCard _about = new AboutCard();
        readonly LinkText _dev = new LinkText(), _lnkUninstall = new LinkText();
        readonly OutlineButton _btnUpdate = new OutlineButton(), _btnRepo = new OutlineButton();

        // ---------------- state ----------------
        bool _allowShow, _exiting, _scanning, _pcScanning, _trayHintShown, _startedOnce;
        string _selectedRoot, _lastSub;
        readonly Queue<string> _pending = new Queue<string>();
        UpdateInfo _pendingUpdate;
        readonly string _startScan;
        readonly bool _justUpdated, _justInstalled, _restarted;
        List<string> _lastPcDetails = new List<string>();

        public MainForm(bool startHidden, string scanTarget, bool justUpdated, bool justInstalled)
        {
            _allowShow = !startHidden;
            _startScan = scanTarget;
            _justUpdated = justUpdated;
            _justInstalled = justInstalled;
            _restarted = Environment.GetCommandLineArgs().Any(a => a.Equals("--restarted", StringComparison.OrdinalIgnoreCase));

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
            ClientSize = new Size(Theme.S(W), Theme.S(TitleH + PageH + TabH));
            DoubleBuffered = true;
            MaximizeBox = false;
            ShowInTaskbar = true;

            BuildChrome();
            BuildUsbPage();
            BuildPcPage();
            BuildQuarantinePage();
            BuildHistoryPage();
            BuildSettingsPage();
            BuildTray();
            ApplyLanguage();
            RefreshDrives();
            ShowPage(0);

            Quarantine.Changed += () => UI(RefreshQuarantine);
            History.Changed += () => UI(RefreshHistory);
            SystemEvents.SessionEnding += (s, e) => SelfProtect.SignalQuit();
            _repairTimer.Tick += (s, e) => Task.Run(() =>
            {
                SelfProtect.SelfRepair();
                if (Settings.SelfProtect) SelfProtect.StartGuard();
            });

            var _ = Handle; // انشاء النافذة حتى تستلم رسائل توصيل الفلاشة حتى لو مخفية
            StartPipeServer();
        }

        static Rectangle R(int x, int y, int w, int h) => new Rectangle(Theme.S(x), Theme.S(y), Theme.S(w), Theme.S(h));

        void UI(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        // ==================================================================
        //  الهيكل: شريط العنوان + الصفحات + التبويبات
        // ==================================================================
        void BuildChrome()
        {
            _btnClose.Bounds = R(W - 42, 6, 34, 32);
            _btnMin.Bounds = R(W - 78, 6, 34, 32);
            _btnLang.Bounds = R(W - 114, 6, 34, 32);
            _btnClose.Click += (s, e) => HideToTray();
            _btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            _btnLang.Click += (s, e) => ToggleLanguage();

            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i] = new DoubleBufferedPanel { Bounds = R(0, TitleH, W, PageH), BackColor = Color.Transparent, Visible = false };
            }

            _tabs.Bounds = R(0, TitleH + PageH, W, TabH);
            _tabs.SelectedChanged += ShowPage;

            Controls.AddRange(new Control[] { _btnClose, _btnMin, _btnLang, _tabs });
            Controls.AddRange(_pages);
        }

        void ShowPage(int i)
        {
            for (int k = 0; k < _pages.Length; k++) _pages[k].Visible = k == i;
            if (_tabs.Selected != i) _tabs.Selected = i;
            _ring.Animate = Visible && i == 0;
            _pcRing.Animate = Visible && i == 1;
            if (i == 2) RefreshQuarantine();
            if (i == 3) RefreshHistory();
        }

        // ==================================================================
        //  صفحة 0: فحص الفلاشة
        // ==================================================================
        void BuildUsbPage()
        {
            var p = _pages[0];
            _ring.Logo = _logo;
            _ring.Bounds = R(115, 8, 130, 130);

            _title.Bounds = R(16, 142, W - 32, 28);
            _title.Font = Theme.Font(15f, FontStyle.Bold);
            _sub.Bounds = R(16, 170, W - 32, 18);
            _sub.Font = Theme.Font(9f);
            _sub.Color = Theme.Muted;

            // قائمة الاقراص: الفلاشات + الهاردات
            _driveList.Bounds = R(20, 198, W - 36, 214);
            _driveList.SelectionChanged += UpdateScanButtonText;
            _driveList.ItemActivated += root => { if (!_scanning) StartScan(root); };

            _scanBtn.Bounds = R(20, 420, W - 40, 46);
            _scanBtn.Click += (s, e) =>
            {
                if (_scanning) { _cts?.Cancel(); _scanBtn.Enabled = false; return; }
                if (_driveList.SelectedRoot != null) StartScan(_driveList.SelectedRoot);
            };

            _pathBtn.Bounds = R(20, 474, W - 40, 38);
            _pathMenu.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            _pathMenu.ShowImageMargin = false;
            _pathBtn.Click += (s, e) => _pathMenu.Show(_pathBtn, new Point(0, _pathBtn.Height));

            _msg.Bounds = R(16, 518, W - 32, 34);
            _msg.Font = Theme.Font(8.5f);
            _msg.Color = Theme.Muted;

            _elapsedTimer.Tick += (s, e) =>
            {
                if (_ring.Indeterminate) _ring.CenterText = (DateTime.Now - _scanStart).ToString(@"m\:ss");
            };

            p.Controls.AddRange(new Control[] { _ring, _title, _sub, _driveList, _scanBtn, _pathBtn, _msg });
        }

        void RebuildPathMenu()
        {
            _pathMenu.Items.Clear();
            _pathMenu.RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No;
            _pathMenu.Items.Add(L.T("pick_file"), null, (s, e) =>
            {
                using var d = new OpenFileDialog { Title = "USB Guard", CheckFileExists = true, Multiselect = false };
                if (d.ShowDialog(this) == DialogResult.OK) StartScan(d.FileName);
            });
            _pathMenu.Items.Add(L.T("pick_folder"), null, (s, e) =>
            {
                using var d = new FolderBrowserDialog { Description = L.T("pick_folder"), ShowNewFolderButton = false };
                if (d.ShowDialog(this) == DialogResult.OK) StartScan(d.SelectedPath);
            });
            foreach (ToolStripItem i in _pathMenu.Items) i.ForeColor = Theme.Text;
        }

        void UpdateScanButtonText()
        {
            if (_scanning) { _scanBtn.Text = L.T("cancel_btn"); return; }
            var root = _driveList.SelectedRoot;
            _scanBtn.Text = root == null ? L.T("scan_btn") : L.F("scan_target", root.TrimEnd('\\'));
            _scanBtn.Enabled = root != null;
            _scanBtn.Invalidate();
        }

        // ==================================================================
        //  صفحة 1: فحص الحاسبة
        // ==================================================================
        void BuildPcPage()
        {
            var p = _pages[1];
            _pcRing.Logo = _logo;
            _pcRing.Bounds = R(120, 12, 120, 120);

            _pcTitle.Bounds = R(16, 136, W - 32, 28);
            _pcTitle.Font = Theme.Font(14f, FontStyle.Bold);
            _pcSub.Bounds = R(16, 164, W - 32, 18);
            _pcSub.Font = Theme.Font(8.5f);
            _pcSub.Color = Theme.Muted;

            _pcBtn.Bounds = R(20, 192, W - 40, 46);
            _pcBtn.Click += (s, e) => StartPcScan(silent: false);

            _pcMsg.Bounds = R(16, 242, W - 32, 18);
            _pcMsg.Font = Theme.Font(8.5f);
            _pcMsg.Color = Theme.Muted;

            _salityBanner.Bounds = R(20, 266, W - 40, 58);
            _salityBanner.Visible = false;
            _salityBanner.Click += (s, e) => SystemTools.OpenUrl("https://www.kaspersky.com/downloads/free-virus-removal-tool");

            _pcList.Bounds = R(20, 268, W - 32, PageH - 268 - 8);

            p.Controls.AddRange(new Control[] { _pcRing, _pcTitle, _pcSub, _pcBtn, _pcMsg, _salityBanner, _pcList });
        }

        void LayoutPcList()
        {
            int top = _salityBanner.Visible ? 332 : 268;
            _pcList.Bounds = R(20, top, W - 32, PageH - top - 8);
        }

        // ==================================================================
        //  صفحة 2: الحجر الصحي
        // ==================================================================
        void BuildQuarantinePage()
        {
            var p = _pages[2];
            _qTitle.Bounds = R(20, 14, W - 140, 28);
            _qTitle.Font = Theme.Font(14f, FontStyle.Bold);
            _qTitle.Align = StringAlignment.Near;
            _qDeleteAll.Font = Theme.Font(8.5f, FontStyle.Bold);
            _qDeleteAll.HoverColor = Theme.Red;
            _qDeleteAll.Click += (s, e) =>
            {
                if (Quarantine.Count == 0) return;
                if (Msg(L.T("q_delall_ask"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) Quarantine.DeleteAll();
            };
            _qSub.Bounds = R(20, 42, W - 40, 18);
            _qSub.Font = Theme.Font(8.5f);
            _qSub.Color = Theme.Muted;
            _qSub.Align = StringAlignment.Near;
            _qList.Bounds = R(20, 70, W - 32, PageH - 78);
            p.Controls.AddRange(new Control[] { _qTitle, _qDeleteAll, _qSub, _qList });
        }

        void RefreshQuarantine()
        {
            var items = Quarantine.Items();
            _tabs.Badge = items.Count; _tabs.Invalidate();
            _qSub.Text = items.Count == 0 ? L.T("q_sub") : L.F("q_count", items.Count) + " • " + L.T("q_sub");
            _qDeleteAll.Visible = items.Count > 0;
            _qList.EmptyText = L.T("q_empty");
            _qList.SetItems(items.Select(q => new CardItem
            {
                Title = q.Name,
                Subtitle = q.OriginalPath,
                Meta = $"{q.Date} • {q.Source} • {FormatSize(q.Size)}",
                Accent = q.Source == "PC" ? Theme.Red : Theme.Amber,
                Actions = new List<CardAction>
                {
                    new CardAction { Text = L.T("q_delete"), Color = Theme.Red, Click = () => Quarantine.Delete(q.Id) },
                    new CardAction { Text = L.T("q_restore"), Color = Theme.Accent2, Click = () => RestoreItem(q) },
                },
            }));
        }

        void RestoreItem(QuarantineItem q)
        {
            if (Msg(L.F("q_restore_ask", q.OriginalPath), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (Quarantine.Restore(q.Id, out var to)) Msg(L.F("q_restored", to), MessageBoxButtons.OK, MessageBoxIcon.Information);
            else Msg(L.T("q_restore_fail"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static string FormatSize(long b) =>
            b >= 1048576 ? $"{b / 1048576.0:0.#} MB" : b >= 1024 ? $"{b / 1024.0:0} KB" : $"{b} B";

        // ==================================================================
        //  صفحة 3: السجل
        // ==================================================================
        void BuildHistoryPage()
        {
            var p = _pages[3];
            _hTitle.Bounds = R(20, 14, W - 160, 28);
            _hTitle.Font = Theme.Font(14f, FontStyle.Bold);
            _hTitle.Align = StringAlignment.Near;
            _hClear.Font = Theme.Font(8.5f, FontStyle.Bold);
            _hClear.HoverColor = Theme.Red;
            _hClear.Click += (s, e) => History.Clear();
            _hLog.Font = Theme.Font(8.5f, FontStyle.Bold);
            _hLog.Click += (s, e) => { try { Process.Start("notepad.exe", $"\"{Settings.LogFile}\""); } catch { } };
            _hSub.Bounds = R(20, 42, W - 40, 18);
            _hSub.Font = Theme.Font(8.5f);
            _hSub.Color = Theme.Muted;
            _hSub.Align = StringAlignment.Near;
            _hList.Bounds = R(20, 70, W - 32, PageH - 78);
            p.Controls.AddRange(new Control[] { _hTitle, _hClear, _hLog, _hSub, _hList });
        }

        void RefreshHistory()
        {
            var items = History.Items();
            _hSub.Text = L.F("q_count", items.Count) + " • " + L.F("totals", Settings.TotalThreats, Settings.TotalRestored);
            _hList.EmptyText = L.T("h_empty");
            _hList.SetItems(items.Select(h => new CardItem
            {
                Title = h.Target == "PC" ? L.T("h_pc") : L.F("h_usb", h.Target),
                Subtitle = (h.Threats == 0 && h.Restored == 0) ? L.T("h_clean")
                           : L.F("h_summary", h.Threats, h.Restored) + (h.Items != null && h.Items.Count > 0 ? "  —  " + string.Join("، ", h.Items.Take(4)) : ""),
                Meta = h.Date,
                Accent = h.Threats > 0 ? Theme.Amber : Theme.Green,
            }));
        }

        // ==================================================================
        //  صفحة 4: الاعدادات وحول
        // ==================================================================
        void BuildSettingsPage()
        {
            var p = _pages[4];
            _sTitle.Bounds = R(20, 10, W - 40, 28);
            _sTitle.Font = Theme.Font(14f, FontStyle.Bold);
            _sTitle.Align = StringAlignment.Near;

            int ty = 44;
            foreach (var t in new[] { _tStartup, _tAuto, _tDeep, _tPc, _tSelf, _tUpdate })
            {
                t.Bounds = R(24, ty, W - 48, 32);
                ty += 33;
            }
            _tStartup.SetSilently(Settings.Startup);
            _tAuto.SetSilently(Settings.AutoScan);
            _tDeep.SetSilently(Settings.DeepScan);
            _tPc.SetSilently(Settings.AutoPcClean);
            _tSelf.SetSilently(Settings.SelfProtect);
            _tUpdate.SetSilently(Settings.AutoUpdate);

            _tStartup.CheckedChanged += (s, e) => { var v = _tStartup.Checked; Settings.Startup = v; Task.Run(() => SystemTools.SetStartup(v)); };
            _tAuto.CheckedChanged += (s, e) => Settings.AutoScan = _tAuto.Checked;
            _tDeep.CheckedChanged += (s, e) => Settings.DeepScan = _tDeep.Checked;
            _tPc.CheckedChanged += (s, e) => Settings.AutoPcClean = _tPc.Checked;
            _tUpdate.CheckedChanged += (s, e) => Settings.AutoUpdate = _tUpdate.Checked;
            _tSelf.CheckedChanged += (s, e) =>
            {
                Settings.SelfProtect = _tSelf.Checked;
                if (_tSelf.Checked) { SelfProtect.BeginSession(); SelfProtect.ProtectCurrentProcess(); SelfProtect.StartGuard(); }
                else { SelfProtect.SignalQuit(); SelfProtect.UnprotectCurrentProcess(); }
            };

            _about.Logo = _logo;
            _about.Bounds = R(20, 252, W - 40, 168);
            _dev.Bounds = R(10, 114, W - 60, 24); // داخل بطاقة "حول"
            _dev.Font = Theme.Font(9.5f, FontStyle.Bold);
            _dev.Normal = Theme.Accent2;
            _dev.HoverColor = Color.White;
            _dev.Click += (s, e) => SystemTools.OpenUrl(SystemTools.DeveloperUrl);
            _dev.BackColor = Color.Transparent;

            int bw = (W - 40 - 10) / 2;
            _btnUpdate.Bounds = R(20, 432, bw, 42);
            _btnRepo.Bounds = R(20 + bw + 10, 432, bw, 42);
            _btnRepo.Color = Theme.Accent;
            _btnUpdate.Click += async (s, e) => await CheckUpdates(manual: true);
            _btnRepo.Click += (s, e) => SystemTools.OpenUrl(SystemTools.RepoUrl);

            _lnkUninstall.Bounds = R(20, 492, W - 40, 22);
            _lnkUninstall.HoverColor = Theme.Red;
            _lnkUninstall.Click += (s, e) => DoUninstall();

            _about.Controls.Add(_dev);
            p.Controls.AddRange(new Control[] { _sTitle, _tStartup, _tAuto, _tDeep, _tPc, _tSelf, _tUpdate, _about, _btnUpdate, _btnRepo, _lnkUninstall });
        }

        // ==================================================================
        //  الترَي
        // ==================================================================
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
            _trayMenu.Items.Add(L.T("pc_btn"), null, (s, e) => { ShowMain(); ShowPage(1); StartPcScan(false); });
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
            Task.Run(() => SystemTools.RegisterContextMenu());
        }

        void ApplyLanguage()
        {
            L.Ar = Settings.Arabic;
            _btnLang.Text = L.Ar ? "EN" : "ع";
            _tabs.Labels = new[] { L.T("tab_home"), L.T("tab_pc"), L.T("tab_quar"), L.T("tab_hist"), L.T("tab_settings") };

            _driveList.EmptyText = L.T("drives_empty");
            _pathBtn.Text = L.T("path_btn");
            RebuildPathMenu();
            if (!_scanning) SetState(_ring.State == GuardState.Scanning ? GuardState.Protected : _ring.State, _lastSub);

            _pcBtn.Text = _pcScanning ? L.T("scanning") : L.T("pc_btn");
            if (!_pcScanning)
            {
                _pcTitle.Text = L.T("pc_title");
                _pcSub.Text = string.IsNullOrEmpty(Settings.LastPcScan) ? L.T("pc_sub") : L.F("pc_last", Settings.LastPcScan);
            }
            _pcList.EmptyText = L.T("pc_empty");
            _salityBanner.Title = L.T("sality_title");
            _salityBanner.Message = L.T("sality_msg");

            _qTitle.Text = L.T("q_title");
            _qDeleteAll.Text = L.T("q_delete_all");
            _hTitle.Text = L.T("h_title");
            _hClear.Text = L.T("h_clear");
            _hLog.Text = L.T("h_log");
            _sTitle.Text = L.T("s_title");
            LayoutHeaderLinks();

            _tStartup.Text = L.T("opt_startup");
            _tAuto.Text = L.T("opt_autoscan");
            _tDeep.Text = L.T("opt_deep");
            _tPc.Text = L.T("opt_pcclean");
            _tSelf.Text = L.T("opt_selfprotect");
            _tUpdate.Text = L.T("opt_update");
            _about.Line1 = "USB Guard  v" + SystemTools.CurrentVersion.ToString(3);
            _about.Line2 = L.T("about_line");
            _dev.Text = L.T("dev");
            _btnUpdate.Text = L.T("check_update");
            _btnRepo.Text = L.T("about_page");
            _lnkUninstall.Text = L.T("uninstall");

            RebuildTrayMenu();
            RefreshDrives();
            UpdateScanButtonText();
            RefreshQuarantine();
            RefreshHistory();
            RefreshPcList();
            InvalidateAll(this);
        }

        void LayoutHeaderLinks()
        {
            int end(int w) => L.Ar ? 20 : W - 20 - w;
            int w1 = Measure(_qDeleteAll), w2 = Measure(_hClear), w3 = Measure(_hLog);
            _qDeleteAll.Bounds = R(end(w1), 18, w1, 22);
            _hClear.Bounds = R(end(w2), 18, w2, 22);
            _hLog.Bounds = R(L.Ar ? 20 + w2 + 12 : W - 20 - w2 - 12 - w3, 18, w3, 22);
        }

        static int Measure(Control c) => (int)(TextRenderer.MeasureText(c.Text ?? "", c.Font).Width / Theme.Scale) + 6;

        static void InvalidateAll(Control c)
        {
            c.Invalidate();
            foreach (Control x in c.Controls) InvalidateAll(x);
        }

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
            var items = new List<DriveItem>();
            var sysRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed) continue;
                    if (!SafeReady(d)) continue;
                    int kind = d.DriveType == DriveType.Removable ? 0
                             : string.Equals(d.Name, sysRoot, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                    var label = SafeLabel(d);
                    var typeName = L.T(kind == 0 ? "drive_usb" : kind == 2 ? "drive_sys" : "drive_hdd");
                    items.Add(new DriveItem
                    {
                        Root = d.Name,
                        Kind = kind,
                        Title = string.IsNullOrWhiteSpace(label) ? typeName : $"{label} • {typeName}",
                        Sub = SizeText(d),
                    });
                }
            }
            catch { }
            // الفلاشات اولاً
            items = items.OrderBy(i => i.Kind == 0 ? 0 : 1).ThenBy(i => i.Root).ToList();
            _driveList.SetItems(items);
            UpdateScanButtonText();
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

        // ==================================================================
        //  فحص الفلاشة
        // ==================================================================
        async void StartScan(string target)
        {
            target = Scanner.NormalizePath(target);
            if (target == null) return;

            if (_scanning)
            {
                if (!_pending.Contains(target)) _pending.Enqueue(target);
                return;
            }

            if (!Scanner.CanScan(target, out var reason))
            {
                Notify(reason, ToolTipIcon.Warning, forceDialog: Visible);
                return;
            }

            bool usbMode = Scanner.IsRemovableRoot(target);
            _scanning = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            if (Scanner.IsDriveRoot(target)) _driveList.Select(target);
            _scanBtn.Enabled = true;
            _scanBtn.Text = L.T("cancel_btn");
            _pathBtn.Enabled = false;
            _ring.Progress = 0;
            _ring.Indeterminate = !usbMode;
            _ring.CenterText = null;
            _scanStart = DateTime.Now;
            _elapsedTimer.Start();
            SetState(GuardState.Scanning, target);
            if (!usbMode && Scanner.IsDriveRoot(target)) { _msg.Text = L.T("long_scan"); _msg.Invalidate(); }

            var progress = new Progress<(int pct, string msg)>(p =>
            {
                if (p.pct < 0) _ring.Indeterminate = true;
                else if (usbMode) { _ring.Indeterminate = false; _ring.Progress = p.pct; }
                _msg.Text = p.msg; _msg.Invalidate();
            });

            var deep = Settings.DeepScan;
            var result = await Task.Run(() =>
            {
                if (usbMode)
                {
                    SystemTools.RepairSystem();
                    return Scanner.Scan(target, deep, progress, ct);
                }
                return Scanner.ScanPath(target, deep, progress, ct);
            });

            _scanning = false;
            _elapsedTimer.Stop();
            _ring.Indeterminate = false;
            _ring.CenterText = null;
            _pathBtn.Enabled = true;

            if (result.Cancelled)
            {
                SetState(GuardState.Protected);
                _msg.Text = L.T("res_cancelled"); _msg.Invalidate();
                UpdateScanButtonText();
                _pending.Clear();
                return;
            }

            Settings.TotalThreats += result.Threats + result.DefenderThreats.Count;
            Settings.TotalRestored += result.Restored;
            Settings.LastScan = DateTime.Now.ToString("HH:mm");
            History.Add(new ScanRecord
            {
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Target = target,
                Threats = result.Threats + result.DefenderThreats.Count,
                Restored = result.Restored,
                Items = result.Details.Take(20).ToList(),
            });

            string summary;
            if (result.HadThreats || result.Restored > 0)
            {
                summary = L.F("res_found", result.Threats + result.DefenderThreats.Count, result.Restored);
                if (result.DefenderThreats.Count > 0) summary += " • " + L.F("res_defender_n", string.Join("، ", result.DefenderThreats.Take(3)));
                SetState(result.HadThreats ? GuardState.Threat : GuardState.Clean, summary);
                Notify($"{target}  {summary}", result.HadThreats ? ToolTipIcon.Warning : ToolTipIcon.Info);
                // كان اكو فايروس = ممكن الحاسبة اتلوثت: فحص سريع للحاسبة
                if (result.HadThreats && Settings.AutoPcClean) StartPcScan(silent: true);
            }
            else
            {
                summary = L.T("res_clean");
                SetState(GuardState.Clean, summary);
                if (!Visible) Notify($"{target}  {summary}", ToolTipIcon.Info);
            }
            var extra = result.FilesChecked > 0 ? L.F("res_files", result.FilesChecked.ToString("N0")) + " • " : "";
            if (deep && !result.DefenderRan) extra += L.T("def_unavailable") + " • ";
            _msg.Text = extra + L.T("step_done") + " " + DateTime.Now.ToString("HH:mm:ss"); _msg.Invalidate();

            RefreshDrives();
            if (_pending.Count > 0) StartScan(_pending.Dequeue());
        }

        // ==================================================================
        //  فحص الحاسبة
        // ==================================================================
        async void StartPcScan(bool silent)
        {
            if (_pcScanning) return;
            _pcScanning = true;
            _pcBtn.Enabled = false;
            _pcBtn.Text = L.T("scanning");
            _pcRing.State = GuardState.Scanning;
            _pcRing.Progress = 0;
            _pcTitle.Text = L.T("scanning"); _pcTitle.Color = Theme.Accent2;

            var progress = new Progress<(int pct, string msg)>(p =>
            {
                _pcRing.Progress = p.pct;
                _pcMsg.Text = p.msg; _pcMsg.Invalidate();
            });

            var deepPc = Settings.DeepScan;
            var r = await Task.Run(() => PcCleaner.Scan(deepPc, progress));

            _pcScanning = false;
            Settings.LastPcScan = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Settings.TotalThreats += r.Threats;
            _lastPcDetails = r.Details.ToList();
            History.Add(new ScanRecord
            {
                Date = Settings.LastPcScan,
                Target = "PC",
                Threats = r.Threats,
                Restored = 0,
                Items = r.Details.Take(20).ToList(),
            });

            _pcBtn.Enabled = true;
            _pcBtn.Text = L.T("pc_btn");
            _salityBanner.Visible = r.SalitySuspected;
            if (r.SalitySuspected) _salityBanner.Message = L.T("sality_msg") + "\n" + string.Join(" • ", r.Warnings);
            LayoutPcList();

            if (r.Threats > 0)
            {
                _pcRing.State = GuardState.Threat;
                _pcTitle.Text = L.T("pc_cleaned"); _pcTitle.Color = Theme.Amber;
                _pcSub.Text = L.F("pc_res", r.Killed, r.Threats);
                Notify(L.F("pc_res", r.Killed, r.Threats), ToolTipIcon.Warning);
            }
            else
            {
                _pcRing.State = GuardState.Clean;
                _pcTitle.Text = L.T("pc_clean"); _pcTitle.Color = Theme.Green;
                _pcSub.Text = L.T("pc_res_clean");
                if (!silent && !Visible) Notify(L.T("pc_res_clean"), ToolTipIcon.Info);
            }
            if (r.SalitySuspected) Notify(L.T("sality_title") + " — " + L.T("sality_msg"), ToolTipIcon.Warning);

            _pcMsg.Text = L.T("step_done") + " • " + DateTime.Now.ToString("HH:mm:ss");
            RefreshPcList();
            InvalidateAll(_pages[1]);
        }

        void RefreshPcList()
        {
            _pcList.EmptyText = L.T("pc_empty");
            _pcList.SetItems(_lastPcDetails.Select(d => new CardItem { Title = d, Subtitle = "", Accent = Theme.Amber }));
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
        //  رسائل من نسخة ثانية (القائمة اليمنى / الغاء التثبيت)
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
                        bool trusted = SelfProtect.IsTrustedClient(server);
                        using var rd = new StreamReader(server, Encoding.UTF8);
                        var msg = rd.ReadToEnd();
                        UI(() => HandleExternal(msg, trusted));
                    }
                    catch { Thread.Sleep(500); }
                }
            }) { IsBackground = true, Name = "USBGuardPipe" };
            t.Start();
        }

        void HandleExternal(string msg, bool trusted)
        {
            msg = (msg ?? "").Trim();
            if (msg == "EXIT")
            {
                // بس البرنامج نفسه يگدر يطفّيه — مو اي فايروس
                if (trusted) ExitApp();
                else Settings.Log("الحماية الذاتية: رفض طلب ايقاف من برنامج غريب");
                return;
            }
            ShowMain();
            if (msg.StartsWith("SCAN|")) { ShowPage(0); StartScan(msg.Substring(5)); }
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
                Settings.SkippedVersion = info.Tag;
                _pendingUpdate = null;
                return;
            }
            try
            {
                _msg.Text = L.T("upd_downloading"); _msg.Invalidate();
                _btnUpdate.Enabled = false;
                await Updater.DownloadAndApplyAsync(info);
                ExitApp();
            }
            catch
            {
                _btnUpdate.Enabled = true;
                _msg.Text = ""; _msg.Invalidate();
                Msg(L.T("upd_fail"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ==================================================================
        //  نافذة / ترَي / تشغيل
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

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            _ring.Animate = Visible && _tabs.Selected == 0;
            _pcRing.Animate = Visible && _tabs.Selected == 1;
        }

        protected override async void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int pref = 2; // زوايا دائرية بويندوز 11
                DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
                int dark = 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            }
            catch { }

            if (_startedOnce) return;
            _startedOnce = true;

            // الحماية الذاتية
            SelfProtect.BeginSession();
            if (Settings.SelfProtect)
            {
                SelfProtect.ProtectCurrentProcess();
                SelfProtect.StartGuard();
            }
            _repairTimer.Start();

            await Task.Run(() => { SystemTools.DisableAutorun(); SystemTools.RepairSystem(); SelfProtect.SelfRepair(); });

            if (_justInstalled) Notify(L.T("installed"), ToolTipIcon.Info);
            else if (_justUpdated) Notify("USB Guard v" + SystemTools.CurrentVersion.ToString(3) + " ✓", ToolTipIcon.Info);
            else if (_restarted) Notify(L.T("self_restarted"), ToolTipIcon.Warning);

            if (Settings.AutoPcClean && !_restarted) StartPcScan(silent: true);

            if (_startScan != null) { ShowMain(); ShowPage(0); StartScan(_startScan); }
            else if (Settings.AutoScan)
                foreach (var r in _driveList.Items.Where(x => x.Kind == 0).Select(x => x.Root).ToList()) StartScan(r);

            if (Settings.AutoUpdate)
            {
                await Task.Delay(8000);
                await CheckUpdates(manual: false);
            }
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
            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                SelfProtect.SignalQuit();
                _exiting = true;
            }
            if (!_exiting)
            {
                // اغلاق من المستخدم او "End task" من مدير المهام: نخفيه بس، الحماية تبقى شغالة
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        void ExitApp()
        {
            SelfProtect.SignalQuit();
            _exiting = true;
            _repairTimer.Stop();
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < Theme.S(TitleH))
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; Theme.HQ(g);
            using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(14, 22, 42), Theme.Bg, 90f))
                g.FillRectangle(bg, ClientRectangle);

            if (_logo != null) g.DrawImage(_logo, Theme.S(14), Theme.S(10), Theme.S(24), Theme.S(24));
            using (var f = Theme.Font(11f, FontStyle.Bold))
            using (var b = new SolidBrush(Theme.Text))
                g.DrawString("USB Guard", f, b, Theme.S(44), Theme.S(11));

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
            if (disposing) { try { _tray.Dispose(); } catch { } _logo?.Dispose(); _repairTimer.Dispose(); }
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
        public StringAlignment Align = StringAlignment.Center;
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
            Theme.DrawText(g, Text, Font, Color, ClientRectangle, Align);
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

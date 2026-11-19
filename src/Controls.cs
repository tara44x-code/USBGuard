using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace USBGuard
{
    // =====================================================================
    //  الثيم: داكن + ازرق
    // =====================================================================
    public static class Theme
    {
        public static Color Bg        = Color.FromArgb(10, 15, 28);
        public static Color Bg2       = Color.FromArgb(14, 22, 42);
        public static Color Card      = Color.FromArgb(18, 26, 44);
        public static Color CardHover = Color.FromArgb(24, 35, 60);
        public static Color Border    = Color.FromArgb(32, 46, 76);
        public static Color Accent    = Color.FromArgb(47, 128, 255);
        public static Color Accent2   = Color.FromArgb(0, 194, 255);
        public static Color Text      = Color.FromArgb(230, 237, 247);
        public static Color Strong    = Color.White;              // نص الهوفر
        public static Color Muted     = Color.FromArgb(138, 151, 176);
        public static Color TabBg     = Color.FromArgb(14, 21, 38);
        public static Color RingIn1   = Color.FromArgb(22, 34, 60);
        public static Color RingIn2   = Color.FromArgb(12, 18, 34);
        public static Color Track     = Color.FromArgb(30, 44, 74);
        public static Color ToggleOff = Color.FromArgb(45, 58, 88);
        public static Color AboutTop  = Color.FromArgb(24, 38, 70);
        public static bool Light;
        public static readonly Color Green     = Color.FromArgb(34, 197, 94);
        public static readonly Color Red       = Color.FromArgb(239, 68, 68);
        public static readonly Color Amber     = Color.FromArgb(245, 158, 11);

        // الوان التمييز: ازرق، اخضر، احمر، بنفسجي، برتقالي
        static readonly Color[][] Accents =
        {
            new[] { Color.FromArgb(47, 128, 255), Color.FromArgb(0, 194, 255) },
            new[] { Color.FromArgb(16, 185, 129), Color.FromArgb(52, 211, 153) },
            new[] { Color.FromArgb(225, 29, 72),  Color.FromArgb(251, 113, 133) },
            new[] { Color.FromArgb(124, 58, 237), Color.FromArgb(192, 132, 252) },
            new[] { Color.FromArgb(234, 88, 12),  Color.FromArgb(251, 191, 36) },
        };
        public static int AccentCount => Accents.Length;

        public static void Apply(int accent, bool light)
        {
            var a = Accents[Math.Max(0, Math.Min(Accents.Length - 1, accent))];
            Accent = a[0]; Accent2 = a[1];
            Light = light;
            if (!light) return;
            Bg = Color.FromArgb(244, 247, 252); Bg2 = Color.FromArgb(232, 238, 248);
            Card = Color.White; CardHover = Color.FromArgb(236, 242, 252);
            Border = Color.FromArgb(212, 220, 235);
            Text = Color.FromArgb(22, 30, 46); Strong = Color.Black;
            Muted = Color.FromArgb(98, 112, 136);
            TabBg = Color.White;
            RingIn1 = Color.FromArgb(236, 242, 252); RingIn2 = Color.FromArgb(250, 252, 255);
            Track = Color.FromArgb(218, 226, 240); ToggleOff = Color.FromArgb(196, 206, 224);
            AboutTop = Color.FromArgb(226, 236, 254);
            // في الوضع الفاتح نخلي اللون الثاني اغمق شوية حتى يبين النص
            Accent2 = ControlPaint.Dark(a[1], 0.15f);
        }

        public static float Scale = 1f;
        public static int S(int v) => (int)Math.Round(v * Scale);
        public static float S(float v) => v * Scale;

        public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
            new Font("Segoe UI", size, style, GraphicsUnit.Point);

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void HQ(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        /// <summary>رسم نص يدعم العربي (من اليمين لليسار)</summary>
        public static void DrawText(Graphics g, string text, Font f, Color c, RectangleF r,
            StringAlignment h = StringAlignment.Center, StringAlignment v = StringAlignment.Center)
        {
            using var sf = new StringFormat { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter };
            if (L.Ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            using var b = new SolidBrush(c);
            g.DrawString(text, f, b, r, sf);
        }

        /// <summary>محاذاة "البداية" حسب اللغة (يمين للعربي، يسار للانكليزي)</summary>
        public static StringAlignment Start => StringAlignment.Near; // مع DirectionRightToLeft تصير يمين تلقائياً
    }

    // =====================================================================
    //  اساس لكل الكنترولات المرسومة
    // =====================================================================
    public class PaintedControl : Control
    {
        protected bool Hover;
        public PaintedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
        }
        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // =====================================================================
    //  زر رئيسي متدرج
    // =====================================================================
    public class GradientButton : PaintedControl
    {
        bool _down;
        public GradientButton() { Cursor = Cursors.Hand; Font = Theme.Font(12f, FontStyle.Bold); }

        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (_down) r.Inflate(-1, -1);
            using var path = Theme.Round(r, Theme.S(14));
            Color c1 = Enabled ? Theme.Accent : Theme.Track;
            Color c2 = Enabled ? Theme.Accent2 : Theme.Track;
            if (Hover && Enabled) { c1 = ControlPaint.Light(c1, 0.15f); c2 = ControlPaint.Light(c2, 0.15f); }
            using (var br = new LinearGradientBrush(r, c1, c2, LinearGradientMode.Horizontal))
                g.FillPath(br, path);
            // لمعة خفيفة
            using (var gl = new LinearGradientBrush(r, Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                g.FillPath(gl, path);
            Theme.DrawText(g, Text, Font, Enabled ? Color.White : Theme.Muted, r);
        }
    }

    // =====================================================================
    //  سطر اعداد مع مفتاح تشغيل/اطفاء
    // =====================================================================
    public class ToggleRow : PaintedControl
    {
        bool _checked;
        float _anim; // 0..1
        readonly Timer _t = new Timer { Interval = 15 };
        public event EventHandler CheckedChanged;

        [DefaultValue(false)]
        public bool Checked
        {
            get => _checked;
            set { if (_checked == value) return; _checked = value; _t.Start(); }
        }
        public void SetSilently(bool v) { _checked = v; _anim = v ? 1 : 0; Invalidate(); }

        public ToggleRow()
        {
            Cursor = Cursors.Hand;
            Font = Theme.Font(9.5f);
            _t.Tick += (s, e) =>
            {
                float target = _checked ? 1 : 0;
                _anim += (target - _anim) * 0.35f;
                if (Math.Abs(target - _anim) < 0.02f) { _anim = target; _t.Stop(); }
                Invalidate();
            };
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            CheckedChanged?.Invoke(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            int sw = Theme.S(40), sh = Theme.S(22);
            // المفتاح على اليسار بالعربي، وعلى اليمين بالانكليزي
            float sx = L.Ar ? 2 : Width - sw - 2;
            var sr = new RectangleF(sx, (Height - sh) / 2f, sw, sh);
            var off = Theme.ToggleOff;
            Color track = Blend(off, Theme.Accent, _anim);
            using (var p = Theme.Round(sr, sh / 2f))
            using (var b = new SolidBrush(track)) g.FillPath(b, p);
            float knob = sh - Theme.S(6);
            float kx = sr.X + Theme.S(3) + (sw - knob - Theme.S(6)) * _anim;
            using (var kb = new SolidBrush(Color.White))
                g.FillEllipse(kb, kx, sr.Y + Theme.S(3), knob, knob);

            var tr = L.Ar ? new RectangleF(sw + Theme.S(10), 0, Width - sw - Theme.S(10), Height)
                          : new RectangleF(0, 0, Width - sw - Theme.S(10), Height);
            Theme.DrawText(g, Text, Font, Hover ? Theme.Strong : Theme.Text, tr, StringAlignment.Near);
        }

        static Color Blend(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        protected override void Dispose(bool disposing) { if (disposing) _t.Dispose(); base.Dispose(disposing); }
    }

    // =====================================================================
    //  دائرة الحالة (الدرع) مع انيميشن اثناء الفحص
    // =====================================================================
    public enum GuardState { Protected, Scanning, Clean, Threat }

    public class StatusRing : PaintedControl
    {
        public Image Logo;
        GuardState _state = GuardState.Protected;
        int _progress;
        float _spin, _pulse;
        readonly Timer _t = new Timer { Interval = 16 };

        public GuardState State { get => _state; set { _state = value; Invalidate(); } }
        public bool Animate { get => _t.Enabled; set { _t.Enabled = value; } }
        bool _indeterminate;
        public bool Indeterminate { get => _indeterminate; set { _indeterminate = value; Invalidate(); } }
        string _center;
        public string CenterText { get => _center; set { _center = value; Invalidate(); } }
        public int Progress { get => _progress; set { _progress = Math.Max(0, Math.Min(100, value)); Invalidate(); } }

        public StatusRing()
        {
            _t.Tick += (s, e) => { _spin = (_spin + 4) % 360; _pulse += 0.05f; Invalidate(); };
        }

        Color StateColor => _state switch
        {
            GuardState.Scanning => Theme.Accent2,
            GuardState.Clean => Theme.Green,
            GuardState.Threat => Theme.Amber,
            _ => Theme.Accent,
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            float size = Math.Min(Width, Height) - Theme.S(8);
            var r = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
            var c = StateColor;

            // توهج
            float glow = 0.5f + 0.5f * (float)Math.Sin(_pulse);
            for (int i = 4; i >= 1; i--)
            {
                var gr = RectangleF.Inflate(r, -Theme.S(10) + i * Theme.S(3), -Theme.S(10) + i * Theme.S(3));
                using var gp = new Pen(Color.FromArgb((int)(10 + 10 * glow) / i, c), Theme.S(6));
                g.DrawEllipse(gp, gr);
            }

            // دائرة داخلية
            var inner = RectangleF.Inflate(r, -Theme.S(14), -Theme.S(14));
            using (var ib = new LinearGradientBrush(inner, Theme.RingIn1, Theme.RingIn2, 90f))
                g.FillEllipse(ib, inner);

            // المسار
            var ring = RectangleF.Inflate(r, -Theme.S(6), -Theme.S(6));
            using (var track = new Pen(Theme.Track, Theme.S(5)))
                g.DrawEllipse(track, ring);

            using (var pen = new Pen(c, Theme.S(5)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (_state == GuardState.Scanning && _indeterminate)
                {
                    g.DrawArc(pen, ring, _spin * 1.5f, 110);
                    using var spinPen2 = new Pen(Color.FromArgb(160, Theme.Accent2), Theme.S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawArc(spinPen2, RectangleF.Inflate(ring, -Theme.S(9), -Theme.S(9)), -_spin, 70);
                }
                else if (_state == GuardState.Scanning)
                {
                    float sweep = Math.Max(8, 360f * _progress / 100f);
                    g.DrawArc(pen, ring, -90, sweep);
                    using var spinPen = new Pen(Color.FromArgb(160, Theme.Accent2), Theme.S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawArc(spinPen, RectangleF.Inflate(ring, -Theme.S(9), -Theme.S(9)), _spin, 70);
                }
                else
                {
                    g.DrawEllipse(pen, ring);
                }
            }

            // الشعار
            if (Logo != null)
            {
                float ls = inner.Width * 1.18f; // اكبر من الدائرة حتى الخلفية الداكنة للشعار تملي الدائرة بدون مربع
                var lr = new RectangleF(inner.X + (inner.Width - ls) / 2f, inner.Y + (inner.Height - ls) / 2f, ls, ls);
                if (_state == GuardState.Scanning)
                {
                    var txt = _center ?? (_indeterminate ? "" : _progress + "%");
                    using var f = Theme.Font(txt.Length > 4 ? 15f : 20f, FontStyle.Bold);
                    Theme.DrawText(g, txt, f, Theme.Text, inner);
                }
                else
                {
                    using var clip = new GraphicsPath(); clip.AddEllipse(inner);
                    var old = g.Clip; g.SetClip(clip);
                    g.DrawImage(Logo, lr);
                    g.Clip = old;
                }
            }

            // علامة الحالة الصغيرة
            if (_state == GuardState.Clean || _state == GuardState.Protected || _state == GuardState.Threat)
            {
                float bs = Theme.S(28);
                var br = new RectangleF(ring.Right - bs * 0.95f, ring.Bottom - bs * 0.95f, bs, bs);
                using (var bb = new SolidBrush(c)) g.FillEllipse(bb, br);
                using (var bp = new Pen(Theme.Bg, Theme.S(3))) g.DrawEllipse(bp, br);
                using var wp = new Pen(Color.White, Theme.S(2.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                if (_state == GuardState.Threat)
                {
                    g.DrawLine(wp, br.X + bs / 2, br.Y + bs * 0.27f, br.X + bs / 2, br.Y + bs * 0.58f);
                    g.DrawLine(wp, br.X + bs / 2, br.Y + bs * 0.74f, br.X + bs / 2, br.Y + bs * 0.75f);
                }
                else
                {
                    g.DrawLines(wp, new[] {
                        new PointF(br.X + bs * 0.28f, br.Y + bs * 0.52f),
                        new PointF(br.X + bs * 0.44f, br.Y + bs * 0.67f),
                        new PointF(br.X + bs * 0.72f, br.Y + bs * 0.36f) });
                }
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) _t.Dispose(); base.Dispose(disposing); }
    }

    // =====================================================================
    //  سطر فلاشة (قابل للاختيار)
    // =====================================================================
    public class DriveRow : PaintedControl
    {
        public string Root;
        public string Label;
        public string SizeText;
        bool _selected;
        public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

        public DriveRow() { Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, Theme.S(12)))
            {
                using (var b = new SolidBrush(Hover || _selected ? Theme.CardHover : Theme.Card)) g.FillPath(b, p);
                using var pen = new Pen(_selected ? Theme.Accent : Theme.Border, _selected ? Theme.S(1.6f) : 1f);
                g.DrawPath(pen, p);
            }

            // ايقونة USB مرسومة
            float isz = Height - Theme.S(18);
            float ix = L.Ar ? Width - isz - Theme.S(12) : Theme.S(12);
            var ir = new RectangleF(ix, (Height - isz) / 2f, isz, isz);
            using (var ip = Theme.Round(ir, Theme.S(8)))
            using (var ib = new LinearGradientBrush(ir, Theme.Accent, Theme.Accent2, 45f))
                g.FillPath(ib, ip);
            using (var f = Theme.Font(9.5f, FontStyle.Bold))
                Theme.DrawText(g, Root.TrimEnd('\\'), f, Color.White, ir);

            // النصوص
            float tx = L.Ar ? Theme.S(12) : ix + isz + Theme.S(10);
            float tw = Width - isz - Theme.S(34);
            using (var f1 = Theme.Font(10f, FontStyle.Bold))
                Theme.DrawText(g, Label, f1, Theme.Text, new RectangleF(tx, Theme.S(6), tw, Height / 2f - Theme.S(4)), StringAlignment.Near, StringAlignment.Far);
            using (var f2 = Theme.Font(8.5f))
                Theme.DrawText(g, SizeText, f2, Theme.Muted, new RectangleF(tx, Height / 2f + Theme.S(1), tw, Height / 2f - Theme.S(6)), StringAlignment.Near, StringAlignment.Near);
        }
    }

    // =====================================================================
    //  بطاقة احصائية صغيرة
    // =====================================================================
    public class StatCard : PaintedControl
    {
        public string Value = "0";
        public string Caption = "";
        public Color ValueColor = Theme.Text;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, Theme.S(12)))
            {
                using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using var pen = new Pen(Theme.Border); g.DrawPath(pen, p);
            }
            using (var f = Theme.Font(13f, FontStyle.Bold))
                Theme.DrawText(g, Value, f, ValueColor, new RectangleF(0, Theme.S(4), Width, Height * 0.58f));
            using (var f2 = Theme.Font(7.5f))
                Theme.DrawText(g, Caption, f2, Theme.Muted, new RectangleF(Theme.S(2), Height * 0.55f, Width - Theme.S(4), Height * 0.4f), StringAlignment.Center, StringAlignment.Near);
        }
    }

    // =====================================================================
    //  نص قابل للضغط (رابط)
    // =====================================================================
    public class LinkText : PaintedControl
    {
        public Color Normal = Theme.Muted;
        public Color HoverColor = Theme.Accent2;
        public StringAlignment Align = StringAlignment.Center;
        public LinkText() { Cursor = Cursors.Hand; Font = Theme.Font(8.5f); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            Theme.DrawText(g, Text, Font, Hover ? HoverColor : Normal, ClientRectangle, Align);
        }
    }

    // =====================================================================
    //  زر صغير بشريط العنوان
    // =====================================================================
    public class TitleButton : PaintedControl
    {
        public enum Kind { Close, Minimize, Lang }
        public Kind Type;
        public TitleButton() { Cursor = Cursors.Hand; Font = Theme.Font(8.5f, FontStyle.Bold); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            var r = new RectangleF(2, 2, Width - 4, Height - 4);
            if (Hover)
            {
                using var p = Theme.Round(r, Theme.S(8));
                using var b = new SolidBrush(Type == Kind.Close ? Theme.Red : Theme.CardHover);
                g.FillPath(b, p);
            }
            using var pen = new Pen(Hover ? (Type == Kind.Close ? Color.White : Theme.Strong) : Theme.Muted, Theme.S(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float cx = Width / 2f, cy = Height / 2f, s = Theme.S(5);
            switch (Type)
            {
                case Kind.Close:
                    g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                    g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
                    break;
                case Kind.Minimize:
                    g.DrawLine(pen, cx - s, cy, cx + s, cy);
                    break;
                case Kind.Lang:
                    using (var b = new SolidBrush(Hover ? Theme.Strong : Theme.Muted))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(Text, Font, b, r, sf);
                    break;
            }
        }
    }
}

namespace USBGuard
{
    using System.Collections.Generic;
    using System.Linq;

    // =====================================================================
    //  شريط التبويبات السفلي (مثل تطبيقات الموبايل)
    // =====================================================================
    public class TabBar : PaintedControl
    {
        public string[] Labels = new string[5];
        int _selected, _hover = -1;
        public event Action<int> SelectedChanged;
        public int Badge; // عدد على تبويب الحجر
        public int BadgeIndex = 2;
        public int[] IconMap; // اي ايقونة لكل تبويب

        public int Selected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; Invalidate(); SelectedChanged?.Invoke(value); }
        }

        public TabBar() { Cursor = Cursors.Hand; Font = Theme.Font(7.5f); }

        int Count => Labels.Length;
        int VisualToIndex(int v) => L.Ar ? Count - 1 - v : v;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int v = Math.Max(0, Math.Min(Count - 1, e.X * Count / Math.Max(1, Width)));
            int i = VisualToIndex(v);
            if (i != _hover) { _hover = i; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int v = Math.Max(0, Math.Min(Count - 1, e.X * Count / Math.Max(1, Width)));
            Selected = VisualToIndex(v);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            using (var b = new SolidBrush(Theme.TabBg)) g.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, 0, Width, 0);

            float w = Width / (float)Count;
            for (int v = 0; v < Count; v++)
            {
                int i = VisualToIndex(v);
                var cell = new RectangleF(v * w, 0, w, Height);
                bool sel = i == _selected;
                var c = sel ? Theme.Accent2 : (i == _hover ? Theme.Strong : Theme.Muted);

                if (sel)
                {
                    var pill = new RectangleF(cell.X + w / 2 - Theme.S(18), Theme.S(6), Theme.S(36), Theme.S(26));
                    using var pp = Theme.Round(pill, Theme.S(13));
                    using var pb = new SolidBrush(Color.FromArgb(40, Theme.Accent));
                    g.FillPath(pb, pp);
                }
                DrawIcon(g, IconMap != null && i < IconMap.Length ? IconMap[i] : i, new PointF(cell.X + w / 2, Theme.S(19)), Theme.S(8.5f), c);
                using (var f = Theme.Font(7.5f, sel ? FontStyle.Bold : FontStyle.Regular))
                    Theme.DrawText(g, Labels[i] ?? "", f, c, new RectangleF(cell.X, Theme.S(34), w, Height - Theme.S(36)), StringAlignment.Center, StringAlignment.Near);

                if (i == BadgeIndex && Badge > 0)
                {
                    var br = new RectangleF(cell.X + w / 2 + Theme.S(6), Theme.S(4), Theme.S(16), Theme.S(16));
                    using (var bb = new SolidBrush(Theme.Red)) g.FillEllipse(bb, br);
                    using var bf = Theme.Font(6.5f, FontStyle.Bold);
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(Badge > 99 ? "99" : Badge.ToString(), bf, Brushes.White, br, sf);
                }
            }
        }

        static void DrawIcon(Graphics g, int idx, PointF c, float s, Color color)
        {
            using var pen = new Pen(color, Theme.S(1.7f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            float x = c.X, y = c.Y;
            switch (idx)
            {
                case 0: // درع
                    g.DrawPolygon(pen, new[] {
                        new PointF(x, y - s), new PointF(x + s * 0.85f, y - s * 0.6f), new PointF(x + s * 0.8f, y + s * 0.1f),
                        new PointF(x, y + s), new PointF(x - s * 0.8f, y + s * 0.1f), new PointF(x - s * 0.85f, y - s * 0.6f) });
                    g.DrawLines(pen, new[] { new PointF(x - s * 0.35f, y), new PointF(x - s * 0.05f, y + s * 0.3f), new PointF(x + s * 0.4f, y - s * 0.3f) });
                    break;
                case 1: // شاشة
                    using (var p = Theme.Round(new RectangleF(x - s, y - s * 0.8f, s * 2, s * 1.35f), s * 0.2f)) g.DrawPath(pen, p);
                    g.DrawLine(pen, x, y + s * 0.55f, x, y + s * 0.9f);
                    g.DrawLine(pen, x - s * 0.5f, y + s * 0.95f, x + s * 0.5f, y + s * 0.95f);
                    break;
                case 2: // صندوق الحجر
                    g.DrawRectangle(pen, x - s * 0.85f, y - s * 0.35f, s * 1.7f, s * 1.25f);
                    g.DrawRectangle(pen, x - s, y - s * 0.85f, s * 2f, s * 0.5f);
                    g.DrawLine(pen, x - s * 0.3f, y + s * 0.1f, x + s * 0.3f, y + s * 0.1f);
                    break;
                case 3: // ساعة
                    g.DrawEllipse(pen, x - s, y - s, s * 2, s * 2);
                    g.DrawLine(pen, x, y, x, y - s * 0.55f);
                    g.DrawLine(pen, x, y, x + s * 0.45f, y + s * 0.25f);
                    break;
                case 5: // مفتاح ربط (الادوات)
                    g.DrawLine(pen, x - s * 0.7f, y + s * 0.7f, x + s * 0.15f, y - s * 0.15f);
                    g.DrawArc(pen, x - s * 0.05f, y - s * 0.95f, s * 0.95f, s * 0.95f, 100, 250);
                    break;
                case 4: // ترس
                    g.DrawEllipse(pen, x - s * 0.62f, y - s * 0.62f, s * 1.24f, s * 1.24f);
                    g.DrawEllipse(pen, x - s * 0.24f, y - s * 0.24f, s * 0.48f, s * 0.48f);
                    for (int k = 0; k < 8; k++)
                    {
                        double a = k * Math.PI / 4;
                        g.DrawLine(pen, x + (float)Math.Cos(a) * s * 0.72f, y + (float)Math.Sin(a) * s * 0.72f,
                                        x + (float)Math.Cos(a) * s, y + (float)Math.Sin(a) * s);
                    }
                    break;
            }
        }
    }

    // =====================================================================
    //  قائمة بطاقات قابلة للتمرير (للحجر والسجل ونتائج فحص الحاسبة)
    // =====================================================================
    public class CardAction
    {
        public string Text;
        public Color Color = Theme.Accent;
        public Action Click;
        internal RectangleF Bounds;
    }

    public class CardItem
    {
        public string Title = "", Subtitle = "", Meta = "";
        public Color Accent = Theme.Accent;
        public List<CardAction> Actions = new List<CardAction>();
    }

    public class CardList : PaintedControl, IWheelScrollable
    {
        public void WheelScroll(int delta)
        {
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(delta) * Theme.S(60)));
            Invalidate();
        }
        List<CardItem> _items = new List<CardItem>();
        int _scroll;
        CardAction _hoverAction;
        public string EmptyText = "";

        public CardList()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        public void SetItems(IEnumerable<CardItem> items)
        {
            _items = items.ToList();
            _scroll = 0;
            Invalidate();
        }

        int CardH => Theme.S(_items.Any(i => i.Actions.Count > 0) ? 82 : 66);
        int Gap => Theme.S(8);
        int ContentH => _items.Count * (CardH + Gap);
        int MaxScroll => Math.Max(0, ContentH - Height);

        protected override void OnMouseEnter(EventArgs e) { Focus(); base.OnMouseEnter(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(e.Delta) * Theme.S(60)));
            Invalidate();
            base.OnMouseWheel(e);
        }

        // سحب بالماوس للتمرير (مثل الموبايل)
        int _dragY = -1, _dragScroll; bool _dragged;
        protected override void OnMouseDown(MouseEventArgs e) { _dragY = e.Y; _dragScroll = _scroll; _dragged = false; base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _dragY = -1; base.OnMouseUp(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragY >= 0 && e.Button == MouseButtons.Left)
            {
                int d = e.Y - _dragY;
                if (Math.Abs(d) > Theme.S(4)) _dragged = true;
                if (_dragged) { _scroll = Math.Max(0, Math.Min(MaxScroll, _dragScroll - d)); Invalidate(); return; }
            }
            var a = HitAction(e.Location);
            if (a != _hoverAction) { _hoverAction = a; Cursor = a != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (!_dragged) HitAction(e.Location)?.Click?.Invoke();
            base.OnMouseClick(e);
        }

        CardAction HitAction(Point p)
        {
            foreach (var it in _items)
                foreach (var a in it.Actions)
                    if (a.Bounds.Contains(p)) return a;
            return null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            if (_items.Count == 0)
            {
                using var f = Theme.Font(9.5f);
                var r = new RectangleF(1, 1, Width - 2, Math.Min(Height - 2, Theme.S(90)));
                using (var p = Theme.Round(r, Theme.S(12)))
                using (var pen = new Pen(Theme.Border, 1.4f) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, p);
                Theme.DrawText(g, EmptyText, f, Theme.Muted, r);
                return;
            }

            _scroll = Math.Min(_scroll, MaxScroll);
            int ch = CardH, gap = Gap;
            float barW = Theme.S(4);
            using var fTitle = Theme.Font(9.5f, FontStyle.Bold);
            using var fSub = Theme.Font(8f);
            using var fMeta = Theme.Font(7.5f);
            using var fBtn = Theme.Font(8f, FontStyle.Bold);

            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                float y = i * (ch + gap) - _scroll;
                foreach (var a in it.Actions) a.Bounds = RectangleF.Empty;
                if (y + ch < 0 || y > Height) continue;

                var r = new RectangleF(0.5f, y + 0.5f, Width - Theme.S(8), ch - 1);
                using (var p = Theme.Round(r, Theme.S(12)))
                {
                    using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                    using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
                    var old = g.Clip; g.SetClip(p, CombineMode.Intersect);
                    using (var ab = new SolidBrush(it.Accent))
                        g.FillRectangle(ab, L.Ar ? r.Right - barW : r.X, r.Y, barW, r.Height);
                    g.Clip = old;
                }

                float pad = Theme.S(14);
                var inner = new RectangleF(r.X + pad, r.Y + Theme.S(8), r.Width - pad * 2, r.Height - Theme.S(16));
                Theme.DrawText(g, it.Title, fTitle, Theme.Text, new RectangleF(inner.X, inner.Y, inner.Width, Theme.S(18)), StringAlignment.Near);
                Theme.DrawText(g, it.Subtitle, fSub, Theme.Muted, new RectangleF(inner.X, inner.Y + Theme.S(19), inner.Width, Theme.S(16)), StringAlignment.Near);

                float rowY = inner.Y + Theme.S(38);
                // الازرار بنهاية السطر
                float bx = L.Ar ? inner.X : inner.Right;
                foreach (var a in it.Actions)
                {
                    float bw = TextRenderer.MeasureText(a.Text, fBtn).Width + Theme.S(14);
                    var br = L.Ar ? new RectangleF(bx, rowY, bw, Theme.S(24)) : new RectangleF(bx - bw, rowY, bw, Theme.S(24));
                    bx = L.Ar ? bx + bw + Theme.S(6) : bx - bw - Theme.S(6);
                    a.Bounds = br;
                    bool hv = a == _hoverAction;
                    using (var bp = Theme.Round(br, Theme.S(8)))
                    {
                        using (var bb = new SolidBrush(Color.FromArgb(hv ? 70 : 35, a.Color))) g.FillPath(bb, bp);
                        using (var pen = new Pen(Color.FromArgb(hv ? 220 : 120, a.Color))) g.DrawPath(pen, bp);
                    }
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    if (L.Ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
                    using var tb = new SolidBrush(hv ? Theme.Strong : a.Color);
                    g.DrawString(a.Text, fBtn, tb, br, sf);
                }

                // الوقت/المعلومات بالجهة الثانية
                if (!string.IsNullOrEmpty(it.Meta))
                {
                    float used = it.Actions.Sum(a => a.Bounds.Width + Theme.S(6));
                    var mr = L.Ar ? new RectangleF(inner.X + used, rowY, inner.Width - used, Theme.S(24))
                                  : new RectangleF(inner.X, rowY, inner.Width - used, Theme.S(24));
                    if (it.Actions.Count == 0) mr = new RectangleF(inner.X, inner.Y + Theme.S(34), inner.Width, Theme.S(16));
                    Theme.DrawText(g, it.Meta, fMeta, Theme.Muted, mr, L.Ar ? StringAlignment.Far : StringAlignment.Near);
                }
            }

            // مؤشر التمرير
            if (MaxScroll > 0)
            {
                float trackH = Height - Theme.S(4);
                float thumbH = Math.Max(Theme.S(30), trackH * Height / (float)ContentH);
                float thumbY = Theme.S(2) + (trackH - thumbH) * _scroll / MaxScroll;
                float tx = L.Ar ? 0 : Width - Theme.S(4);
                using var tp = Theme.Round(new RectangleF(tx, thumbY, Theme.S(3), thumbH), Theme.S(1.5f));
                using var tb = new SolidBrush(Color.FromArgb(120, Theme.Muted));
                g.FillPath(tb, tp);
            }
        }
    }

    // =====================================================================
    //  زر ثانوي (اطار)
    // =====================================================================
    public class OutlineButton : PaintedControl
    {
        public Color Color = Theme.Accent2;
        public OutlineButton() { Cursor = Cursors.Hand; Font = Theme.Font(9f, FontStyle.Bold); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            var r = new RectangleF(1, 1, Width - 2, Height - 2);
            using var p = Theme.Round(r, Theme.S(12));
            using (var b = new SolidBrush(Color.FromArgb(Hover ? 45 : 18, Color))) g.FillPath(b, p);
            using (var pen = new Pen(Color.FromArgb(Hover ? 230 : 140, Color), 1.3f)) g.DrawPath(pen, p);
            Theme.DrawText(g, Text, Font, Hover ? Theme.Strong : Color, r);
        }
    }

    // =====================================================================
    //  شريط تحذير (Sality)
    // =====================================================================
    public class Banner : PaintedControl
    {
        public string Title = "", Message = "";
        public Color Color = Theme.Amber;
        public Banner() { Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using var p = Theme.Round(r, Theme.S(12));
            using (var b = new SolidBrush(Color.FromArgb(Hover ? 50 : 30, Color))) g.FillPath(b, p);
            using (var pen = new Pen(Color.FromArgb(160, Color))) g.DrawPath(pen, p);
            using var f1 = Theme.Font(9.5f, FontStyle.Bold);
            using var f2 = Theme.Font(8f);
            var pad = Theme.S(12);
            Theme.DrawText(g, Title, f1, Color, new RectangleF(pad, Theme.S(6), Width - pad * 2, Theme.S(20)), StringAlignment.Near);
            Theme.DrawText(g, Message, f2, Theme.Text, new RectangleF(pad, Theme.S(26), Width - pad * 2, Height - Theme.S(30)), StringAlignment.Near, StringAlignment.Near);
        }
    }

    // =====================================================================
    //  بطاقة "حول"
    // =====================================================================
    public class AboutCard : PaintedControl
    {
        public Image Logo;
        public string Line1 = "", Line2 = "";
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, Theme.S(16)))
            {
                using (var b = new LinearGradientBrush(r, Theme.AboutTop, Theme.Card, 90f)) g.FillPath(b, p);
                using var pen = new Pen(Theme.Border); g.DrawPath(pen, p);
            }
            float ls = Theme.S(52);
            if (Logo != null)
            {
                var lr = new RectangleF((Width - ls) / 2, Theme.S(12), ls, ls);
                using var clip = Theme.Round(lr, Theme.S(12));
                var old = g.Clip; g.SetClip(clip, CombineMode.Intersect);
                g.DrawImage(Logo, lr);
                g.Clip = old;
            }
            using (var f = Theme.Font(12f, FontStyle.Bold))
            using (var b = new SolidBrush(Theme.Text))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(Line1, f, b, new RectangleF(0, Theme.S(68), Width, Theme.S(22)), sf);
            using (var f2 = Theme.Font(8f))
                Theme.DrawText(g, Line2, f2, Theme.Muted, new RectangleF(Theme.S(10), Theme.S(90), Width - Theme.S(20), Theme.S(18)));
        }
    }
}


namespace USBGuard
{
    using System.Collections.Generic;
    using System.Linq;

    public class DriveItem
    {
        public string Root;       // E:\
        public string Title;      // اسم القرص
        public string Sub;        // المساحة
        public int Kind;          // 0 فلاشة، 1 قرص محلي، 2 قرص النظام
    }

    /// <summary>قائمة الاقراص (فلاشات + هاردات) قابلة للتمرير والاختيار</summary>
    public class DriveList : PaintedControl, IWheelScrollable
    {
        public void WheelScroll(int delta)
        {
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(delta) * Theme.S(54)));
            Invalidate();
        }
        List<DriveItem> _items = new List<DriveItem>();
        int _scroll, _hover = -1;
        public string SelectedRoot;
        public string EmptyText = "";
        public event Action SelectionChanged;
        public event Action<string> ItemActivated;

        public DriveList() { SetStyle(ControlStyles.Selectable, true); TabStop = true; Cursor = Cursors.Hand; }

        public IReadOnlyList<DriveItem> Items => _items;

        public void SetItems(List<DriveItem> items)
        {
            _items = items;
            if (SelectedRoot == null || !_items.Any(i => i.Root == SelectedRoot))
                SelectedRoot = (_items.FirstOrDefault(i => i.Kind == 0) ?? _items.FirstOrDefault())?.Root;
            _scroll = Math.Min(_scroll, MaxScroll);
            Invalidate();
        }

        public void Select(string root) { SelectedRoot = root; Invalidate(); SelectionChanged?.Invoke(); }

        int RowH => Theme.S(48);
        int Gap => Theme.S(6);
        int MaxScroll => Math.Max(0, _items.Count * (RowH + Gap) - Gap - Height);

        int IndexAt(Point p)
        {
            int i = (p.Y + _scroll) / (RowH + Gap);
            int within = (p.Y + _scroll) % (RowH + Gap);
            return i >= 0 && i < _items.Count && within <= RowH ? i : -1;
        }

        protected override void OnMouseEnter(EventArgs e) { Focus(); base.OnMouseEnter(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(e.Delta) * Theme.S(54)));
            Invalidate(); base.OnMouseWheel(e);
        }
        int _dragY = -1, _dragScroll; bool _dragged;
        protected override void OnMouseDown(MouseEventArgs e) { _dragY = e.Y; _dragScroll = _scroll; _dragged = false; base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _dragY = -1; base.OnMouseUp(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragY >= 0 && e.Button == MouseButtons.Left)
            {
                int d = e.Y - _dragY;
                if (Math.Abs(d) > Theme.S(4)) _dragged = true;
                if (_dragged) { _scroll = Math.Max(0, Math.Min(MaxScroll, _dragScroll - d)); Invalidate(); return; }
            }
            int h = IndexAt(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (!_dragged) { int i = IndexAt(e.Location); if (i >= 0) Select(_items[i].Root); }
            base.OnMouseClick(e);
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (i >= 0) ItemActivated?.Invoke(_items[i].Root);
            base.OnMouseDoubleClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            if (_items.Count == 0)
            {
                var er = new RectangleF(1, 1, Width - 2, Math.Min(Height - 2, Theme.S(90)));
                using (var p = Theme.Round(er, Theme.S(12)))
                using (var pen = new Pen(Theme.Border, 1.4f) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, p);
                using var f = Theme.Font(9.5f);
                Theme.DrawText(g, EmptyText, f, Theme.Muted, er);
                return;
            }
            using var fT = Theme.Font(10f, FontStyle.Bold);
            using var fS = Theme.Font(8f);
            using var fL = Theme.Font(9.5f, FontStyle.Bold);
            float barSpace = MaxScroll > 0 ? Theme.S(8) : 0;

            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                float y = i * (RowH + Gap) - _scroll;
                if (y + RowH < 0 || y > Height) continue;
                bool sel = it.Root == SelectedRoot;
                var r = new RectangleF(L.Ar ? barSpace + 0.5f : 0.5f, y + 0.5f, Width - barSpace - 1.5f, RowH - 1);
                using (var p = Theme.Round(r, Theme.S(12)))
                {
                    using (var b = new SolidBrush(sel || i == _hover ? Theme.CardHover : Theme.Card)) g.FillPath(b, p);
                    using var pen = new Pen(sel ? Theme.Accent : Theme.Border, sel ? Theme.S(1.6f) : 1f);
                    g.DrawPath(pen, p);
                }

                float isz = RowH - Theme.S(16);
                float ix = L.Ar ? r.Right - isz - Theme.S(10) : r.X + Theme.S(10);
                var ir = new RectangleF(ix, y + (RowH - isz) / 2f, isz, isz);
                Color c1 = it.Kind == 0 ? Theme.Accent : it.Kind == 2 ? Color.FromArgb(124, 58, 237) : Color.FromArgb(71, 85, 105);
                Color c2 = it.Kind == 0 ? Theme.Accent2 : it.Kind == 2 ? Color.FromArgb(168, 85, 247) : Color.FromArgb(100, 116, 139);
                using (var ip = Theme.Round(ir, Theme.S(8)))
                using (var ib = new LinearGradientBrush(ir, c1, c2, 45f)) g.FillPath(ib, ip);
                Theme.DrawText(g, it.Root.TrimEnd('\\'), fL, Color.White, ir);

                float tx = L.Ar ? r.X + Theme.S(12) : ix + isz + Theme.S(10);
                float tw = r.Width - isz - Theme.S(34);
                Theme.DrawText(g, it.Title, fT, Theme.Text, new RectangleF(tx, y + Theme.S(5), tw, RowH / 2f - Theme.S(3)), StringAlignment.Near, StringAlignment.Far);
                Theme.DrawText(g, it.Sub, fS, Theme.Muted, new RectangleF(tx, y + RowH / 2f + Theme.S(1), tw, RowH / 2f - Theme.S(5)), StringAlignment.Near, StringAlignment.Near);
            }

            if (MaxScroll > 0)
            {
                float trackH = Height - Theme.S(4);
                float contentH = _items.Count * (RowH + Gap) - Gap;
                float thumbH = Math.Max(Theme.S(24), trackH * Height / contentH);
                float thumbY = Theme.S(2) + (trackH - thumbH) * _scroll / MaxScroll;
                float tx = L.Ar ? 0 : Width - Theme.S(4);
                using var tp = Theme.Round(new RectangleF(tx, thumbY, Theme.S(3), thumbH), Theme.S(1.5f));
                using var tb = new SolidBrush(Color.FromArgb(120, Theme.Muted));
                g.FillPath(tb, tp);
            }
        }
    }
}


namespace USBGuard
{
    /// <summary>كنترول يتمرر بعجلة الماوس حتى بدون ما يكون عليه التركيز</summary>
    public interface IWheelScrollable { void WheelScroll(int delta); }

    // =====================================================================
    //  لوحة قابلة للتمرير (لصفحة الاعدادات)
    // =====================================================================
    public class ScrollPanel : DoubleBufferedPanel, IWheelScrollable
    {
        public readonly Panel Content = new DoubleBufferedPanel { BackColor = Color.Transparent, Location = Point.Empty };
        readonly ScrollTrack _track;
        int _dragY = -1, _dragTop;

        public ScrollPanel()
        {
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            _track = new ScrollTrack(this);
            Controls.Add(Content);
            Controls.Add(_track);
            _track.BringToFront();
        }

        public int MaxScroll => Math.Max(0, Content.Height - Height);
        public int Offset => -Content.Top;

        public void SetContentHeight(int h)
        {
            Content.Size = new Size(Width, Math.Max(h, Height));
            Content.Top = 0;
            LayoutTrack();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutTrack(); }

        void LayoutTrack()
        {
            if (_track == null) return;
            int tw = Theme.S(8);
            _track.Bounds = new Rectangle(L.Ar ? Theme.S(2) : Width - tw - Theme.S(2), Theme.S(2), tw, Height - Theme.S(4));
            _track.Visible = MaxScroll > 0;
            _track.BringToFront();
        }

        public void ScrollTo(int offset)
        {
            offset = Math.Max(0, Math.Min(MaxScroll, offset));
            if (-Content.Top == offset) return;
            Content.Top = -offset;
            _track.Invalidate();
            Invalidate(true);
        }

        public void WheelScroll(int delta) => ScrollTo(Offset - Math.Sign(delta) * Theme.S(60));

        protected override void OnMouseWheel(MouseEventArgs e) { WheelScroll(e.Delta); base.OnMouseWheel(e); }

        /// <summary>يربط كل العناصر: المرور يعطي تركيز للعجلة، والسحب على المساحات الفارغة يمرر</summary>
        public void HookChildren() => Hook(Content);

        void Hook(Control c)
        {
            c.MouseEnter += (s, e) => { if (!Focused && FindForm()?.ActiveControl is TextBox == false) Focus(); };
            c.MouseWheel += (s, e) => { if (!Focused) WheelScroll(e.Delta); };
            if (c is Panel || c is TextBlock)
            {
                c.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _dragY = Cursor.Position.Y; _dragTop = Offset; } };
                c.MouseMove += (s, e) => { if (_dragY >= 0 && e.Button == MouseButtons.Left) ScrollTo(_dragTop - (Cursor.Position.Y - _dragY)); };
                c.MouseUp += (s, e) => _dragY = -1;
            }
            foreach (Control ch in c.Controls) Hook(ch);
        }

        // شريط التمرير الظاهر
        class ScrollTrack : PaintedControl
        {
            readonly ScrollPanel _p;
            int _grab = -1, _grabOffset;
            public ScrollTrack(ScrollPanel p) { _p = p; Cursor = Cursors.Hand; }

            RectangleF Thumb()
            {
                float h = Height;
                float ch = Math.Max(1, _p.Content.Height);
                float th = Math.Max(Theme.S(30), h * _p.Height / ch);
                float ty = _p.MaxScroll == 0 ? 0 : (h - th) * _p.Offset / _p.MaxScroll;
                return new RectangleF(1, ty, Width - 2, th);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                var t = Thumb();
                if (t.Contains(e.Location)) { _grab = e.Y; _grabOffset = _p.Offset; }
                else _p.ScrollTo(_p.Offset + (e.Y < t.Y ? -_p.Height : _p.Height) * 3 / 4);
                Invalidate();
                base.OnMouseDown(e);
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (_grab >= 0 && e.Button == MouseButtons.Left)
                {
                    var t = Thumb();
                    float range = Math.Max(1, Height - t.Height);
                    _p.ScrollTo(_grabOffset + (int)((e.Y - _grab) * _p.MaxScroll / range));
                }
                base.OnMouseMove(e);
            }
            protected override void OnMouseUp(MouseEventArgs e) { _grab = -1; base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; Theme.HQ(g);
                using (var tp = Theme.Round(new RectangleF(Width / 2f - Theme.S(1.5f), 0, Theme.S(3), Height), Theme.S(1.5f)))
                using (var tb = new SolidBrush(Color.FromArgb(60, Theme.Muted))) g.FillPath(tb, tp);
                var t = Thumb();
                using var p = Theme.Round(t, Width / 2f - 1);
                using var b = new SolidBrush(Hover || _grab >= 0 ? Theme.Accent2 : Color.FromArgb(170, Theme.Accent));
                g.FillPath(b, p);
            }
        }
    }

    // =====================================================================
    //  سطر اختيار (يتبدل بالضغط) — للفحص المجدول
    // =====================================================================
    public class ChoiceRow : PaintedControl
    {
        public string Value = "";
        public event EventHandler Next;
        public ChoiceRow() { Cursor = Cursors.Hand; Font = Theme.Font(9.5f); }

        protected override void OnClick(EventArgs e) { Next?.Invoke(this, EventArgs.Empty); base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            using var fb = Theme.Font(8.5f, FontStyle.Bold);
            float pw = TextRenderer.MeasureText(Value ?? "", fb).Width + Theme.S(26);
            float ph = Theme.S(24);
            var pr = L.Ar ? new RectangleF(2, (Height - ph) / 2f, pw, ph) : new RectangleF(Width - pw - 2, (Height - ph) / 2f, pw, ph);
            using (var p = Theme.Round(pr, ph / 2f))
            {
                using (var b = new SolidBrush(Color.FromArgb(Hover ? 70 : 40, Theme.Accent))) g.FillPath(b, p);
                using var pen = new Pen(Color.FromArgb(150, Theme.Accent)); g.DrawPath(pen, p);
            }
            Theme.DrawText(g, (Value ?? "") + "  ▾", fb, Theme.Accent2, pr);
            var tr = L.Ar ? new RectangleF(pw + Theme.S(10), 0, Width - pw - Theme.S(10), Height)
                          : new RectangleF(0, 0, Width - pw - Theme.S(10), Height);
            Theme.DrawText(g, Text, Font, Hover ? Theme.Strong : Theme.Text, tr, StringAlignment.Near);
        }
    }

    // =====================================================================
    //  نافذة كلمة السر (داكنة بنفس ثيم البرنامج)
    // =====================================================================
    public class PasswordDialog : Form
    {
        readonly TextBox _box = new TextBox();
        readonly string _prompt, _error;

        PasswordDialog(string prompt, string error)
        {
            _prompt = prompt; _error = error;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Card;
            ClientSize = new Size(Theme.S(300), Theme.S(178));
            KeyPreview = true;

            _box.UseSystemPasswordChar = true;
            _box.BorderStyle = BorderStyle.FixedSingle;
            _box.BackColor = Theme.Bg;
            _box.ForeColor = Theme.Text;
            _box.Font = Theme.Font(12f);
            _box.Bounds = new Rectangle(Theme.S(20), Theme.S(70), Theme.S(260), Theme.S(30));
            _box.TextAlign = HorizontalAlignment.Center;

            var ok = new GradientButton { Text = L.T("pw_ok"), Font = Theme.Font(10f, FontStyle.Bold), Bounds = new Rectangle(Theme.S(20), Theme.S(124), Theme.S(125), Theme.S(36)) };
            var cancel = new OutlineButton { Text = L.T("pw_cancel"), Color = Theme.Muted, Bounds = new Rectangle(Theme.S(155), Theme.S(124), Theme.S(125), Theme.S(36)) };
            if (L.Ar) { var t = ok.Left; ok.Left = cancel.Left; cancel.Left = t; }
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };
            Controls.AddRange(new Control[] { _box, ok, cancel });
            Shown += (s, e) => _box.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; Theme.HQ(g);
            using (var p = new Pen(Theme.Accent, 1.5f)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (var f = Theme.Font(11f, FontStyle.Bold))
                Theme.DrawText(g, "🔒  " + _prompt, f, Theme.Text, new RectangleF(Theme.S(16), Theme.S(14), ClientSize.Width - Theme.S(32), Theme.S(26)));
            if (!string.IsNullOrEmpty(_error))
                using (var f2 = Theme.Font(8.5f))
                    Theme.DrawText(g, _error, f2, Theme.Red, new RectangleF(Theme.S(16), Theme.S(42), ClientSize.Width - Theme.S(32), Theme.S(22)));
            using (var f3 = Theme.Font(7.5f))
                Theme.DrawText(g, "USB Guard", f3, Theme.Muted, new RectangleF(0, Theme.S(104), ClientSize.Width, Theme.S(18)));
        }

        /// <summary>يرجع النص او null اذا الغى</summary>
        public static string Ask(IWin32Window owner, string prompt, string error = null)
        {
            using var d = new PasswordDialog(prompt, error);
            return d.ShowDialog(owner) == DialogResult.OK ? d._box.Text : null;
        }
    }
}

namespace USBGuard
{
    using System.Collections.Generic;
    using System.Linq;

    // =====================================================================
    //  اشعارات حديثة بشكل البرنامج (بدل فقاعات ويندوز القديمة)
    // =====================================================================
    public class Toast : Form
    {
        static readonly List<Toast> Open = new List<Toast>();
        readonly string _title, _text, _button;
        readonly Action _onClick;
        readonly Color _accent;
        readonly Image _logo;
        readonly Timer _life = new Timer { Interval = 6500 };
        readonly Timer _fade = new Timer { Interval = 15 };
        bool _closing, _hover;
        RectangleF _btnRect, _closeRect;

        Toast(string title, string text, ToolTipIcon icon, Image logo, string button, Action onClick)
        {
            _title = title; _text = text; _button = button; _onClick = onClick; _logo = logo;
            _accent = icon == ToolTipIcon.Warning ? Theme.Amber : icon == ToolTipIcon.Error ? Theme.Red : Theme.Accent;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Card;
            DoubleBuffered = true;
            Opacity = 0;
            int h = string.IsNullOrEmpty(button) ? 92 : 120;
            ClientSize = new Size(Theme.S(340), Theme.S(h));
            Cursor = Cursors.Hand;
            _life.Tick += (s, e) => { if (!_hover) BeginClose(); };
            _fade.Tick += (s, e) =>
            {
                if (_closing) { Opacity -= 0.08; if (Opacity <= 0.02) { _fade.Stop(); Close(); } }
                else { Opacity = Math.Min(1, Opacity + 0.12); Top = Math.Max(_targetTop, Top - Theme.S(4)); if (Opacity >= 1 && Top == _targetTop) _fade.Stop(); }
            };
        }

        int _targetTop;
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/;
                cp.ClassStyle |= 0x20000; // ظل
                return cp;
            }
        }

        public static void Show(string text, ToolTipIcon icon, Image logo, string button = null, Action onClick = null, string title = "USB Guard")
        {
            try
            {
                var t = new Toast(title, text, icon, logo, button, onClick);
                var wa = Screen.PrimaryScreen.WorkingArea;
                int gap = Theme.S(10);
                int bottom = wa.Bottom - gap - Open.Where(o => !o.IsDisposed).Sum(o => o.Height + gap);
                t._targetTop = bottom - t.Height;
                t.Location = new Point(wa.Right - t.Width - gap, t._targetTop + Theme.S(24));
                Open.Add(t);
                if (Open.Count > 4) Open[0].BeginClose();
                t.FormClosed += (s, e) => { Open.Remove(t); Reflow(); };
                t.Show();
                t._fade.Start();
                t._life.Start();
            }
            catch { }
        }

        static void Reflow()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int gap = Theme.S(10), bottom = wa.Bottom - gap;
            foreach (var o in Open.Where(x => !x.IsDisposed))
            {
                o._targetTop = bottom - o.Height;
                o.Top = o._targetTop;
                bottom -= o.Height + gap;
            }
        }

        void BeginClose() { if (_closing) return; _closing = true; _life.Stop(); _fade.Start(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_closeRect.Contains(e.Location)) { BeginClose(); return; }
            BeginClose();
            try { _onClick?.Invoke(); } catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var b = new LinearGradientBrush(r, Theme.Card, Theme.Bg2, 90f)) g.FillRectangle(b, r);
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var ab = new SolidBrush(_accent)) g.FillRectangle(ab, L.Ar ? Width - Theme.S(4) : 0, 0, Theme.S(4), Height);

            float pad = Theme.S(14), ls = Theme.S(40);
            float lx = L.Ar ? Width - pad - ls - Theme.S(4) : pad + Theme.S(4);
            if (_logo != null)
            {
                var lr = new RectangleF(lx, pad, ls, ls);
                using var clip = Theme.Round(lr, Theme.S(10));
                var old = g.Clip; g.SetClip(clip, CombineMode.Intersect); g.DrawImage(_logo, lr); g.Clip = old;
            }
            float tx = L.Ar ? pad + Theme.S(20) : lx + ls + Theme.S(12);
            float tw = Width - ls - pad * 2 - Theme.S(36);
            using (var f1 = Theme.Font(10f, FontStyle.Bold))
                Theme.DrawText(g, _title, f1, Theme.Text, new RectangleF(tx, pad - Theme.S(2), tw, Theme.S(20)), StringAlignment.Near);
            using (var f2 = Theme.Font(8.5f))
                Theme.DrawText(g, _text, f2, Theme.Muted, new RectangleF(tx, pad + Theme.S(19), tw, Theme.S(44)), StringAlignment.Near, StringAlignment.Near);

            // زر الاغلاق
            float cs = Theme.S(18);
            _closeRect = new RectangleF(L.Ar ? Theme.S(8) : Width - cs - Theme.S(8), Theme.S(8), cs, cs);
            using (var cp = new Pen(Theme.Muted, Theme.S(1.4f)))
            {
                float c = Theme.S(4.5f);
                var cc = new PointF(_closeRect.X + cs / 2, _closeRect.Y + cs / 2);
                g.DrawLine(cp, cc.X - c, cc.Y - c, cc.X + c, cc.Y + c);
                g.DrawLine(cp, cc.X + c, cc.Y - c, cc.X - c, cc.Y + c);
            }

            if (!string.IsNullOrEmpty(_button))
            {
                using var fb = Theme.Font(8.5f, FontStyle.Bold);
                float bw = TextRenderer.MeasureText(_button, fb).Width + Theme.S(24);
                _btnRect = L.Ar ? new RectangleF(pad, Height - Theme.S(38), bw, Theme.S(26)) : new RectangleF(Width - bw - pad, Height - Theme.S(38), bw, Theme.S(26));
                using (var bp = Theme.Round(_btnRect, Theme.S(8)))
                using (var bb = new LinearGradientBrush(_btnRect, Theme.Accent, Theme.Accent2, 0f)) g.FillPath(bb, bp);
                Theme.DrawText(g, _button, fb, Color.White, _btnRect);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _life.Dispose(); _fade.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // =====================================================================
    //  رسم بياني: التهديدات بآخر 8 أسابيع
    // =====================================================================
    public class WeeklyChart : PaintedControl
    {
        public int[] Values = new int[8];
        public string[] Labels = new string[8];
        public string Title = "";

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.HQ(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, Theme.S(12)))
            {
                using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using var pen = new Pen(Theme.Border); g.DrawPath(pen, p);
            }
            using (var ft = Theme.Font(8f, FontStyle.Bold))
                Theme.DrawText(g, Title, ft, Theme.Muted, new RectangleF(Theme.S(12), Theme.S(4), Width - Theme.S(24), Theme.S(18)), StringAlignment.Near);

            int n = Values.Length;
            int max = Math.Max(1, Values.Max());
            float top = Theme.S(26), bottom = Height - Theme.S(18);
            float area = Width - Theme.S(24);
            float bw = area / n;
            using var fl = Theme.Font(6.5f);
            using var fv = Theme.Font(7f, FontStyle.Bold);
            for (int i = 0; i < n; i++)
            {
                int vi = L.Ar ? n - 1 - i : i; // الاحدث على اليسار بالعربي
                float x = Theme.S(12) + i * bw;
                float h = (bottom - top - Theme.S(10)) * Values[vi] / max;
                var bar = new RectangleF(x + bw * 0.22f, bottom - Math.Max(h, Theme.S(2)), bw * 0.56f, Math.Max(h, Theme.S(2)));
                using (var bp = Theme.Round(bar, Theme.S(3)))
                using (var bb = new LinearGradientBrush(new RectangleF(bar.X, top, bar.Width, bottom - top + 1),
                       Values[vi] > 0 ? Theme.Amber : Theme.Track, Values[vi] > 0 ? Theme.Accent : Theme.Track, 90f))
                    g.FillPath(bb, bp);
                if (Values[vi] > 0)
                    Theme.DrawText(g, Values[vi].ToString(), fv, Theme.Text, new RectangleF(x, bar.Y - Theme.S(13), bw, Theme.S(12)));
                Theme.DrawText(g, Labels[vi] ?? "", fl, Theme.Muted, new RectangleF(x, bottom + Theme.S(2), bw, Theme.S(14)));
            }
        }
    }

    // =====================================================================
    //  الشخصية المتحركة (الروبوت والزومبي) بخلفية شفافة
    // =====================================================================
    public class MascotView : PaintedControl
    {
        readonly List<Image> _frames = new List<Image>();
        int _i;
        readonly Timer _t = new Timer { Interval = 100 };

        public MascotView()
        {
            try
            {
                using var s = typeof(MascotView).Assembly.GetManifestResourceStream("USBGuard.mascot.png");
                if (s != null)
                {
                    using var strip = new Bitmap(s);
                    int fw = 300, count = strip.Width / fw;
                    for (int k = 0; k < count; k++)
                        _frames.Add(strip.Clone(new Rectangle(k * fw, 0, fw, strip.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb));
                }
            }
            catch { }
            _t.Tick += (s, e) => { if (_frames.Count > 0) { _i = (_i + 1) % _frames.Count; Invalidate(); } };
        }

        public bool Animate { get => _t.Enabled; set => _t.Enabled = value && _frames.Count > 0; }
        public float AspectRatio => _frames.Count > 0 ? _frames[0].Height / (float)_frames[0].Width : 0.6f;

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_frames.Count == 0) return;
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var f = _frames[_i];
            float scale = Math.Min(Width / (float)f.Width, Height / (float)f.Height);
            float w = f.Width * scale, h = f.Height * scale;
            g.DrawImage(f, (Width - w) / 2, (Height - h) / 2, w, h);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _t.Dispose(); foreach (var f in _frames) f.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

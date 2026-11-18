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
        public static readonly Color Bg        = Color.FromArgb(10, 15, 28);
        public static readonly Color Card      = Color.FromArgb(18, 26, 44);
        public static readonly Color CardHover = Color.FromArgb(24, 35, 60);
        public static readonly Color Border    = Color.FromArgb(32, 46, 76);
        public static readonly Color Accent    = Color.FromArgb(47, 128, 255);
        public static readonly Color Accent2   = Color.FromArgb(0, 194, 255);
        public static readonly Color Text      = Color.FromArgb(230, 237, 247);
        public static readonly Color Muted     = Color.FromArgb(138, 151, 176);
        public static readonly Color Green     = Color.FromArgb(34, 197, 94);
        public static readonly Color Red       = Color.FromArgb(239, 68, 68);
        public static readonly Color Amber     = Color.FromArgb(245, 158, 11);

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
            Color c1 = Enabled ? Theme.Accent : Color.FromArgb(40, 56, 90);
            Color c2 = Enabled ? Theme.Accent2 : Color.FromArgb(40, 56, 90);
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
            var off = Color.FromArgb(45, 58, 88);
            Color track = Blend(off, Theme.Accent, _anim);
            using (var p = Theme.Round(sr, sh / 2f))
            using (var b = new SolidBrush(track)) g.FillPath(b, p);
            float knob = sh - Theme.S(6);
            float kx = sr.X + Theme.S(3) + (sw - knob - Theme.S(6)) * _anim;
            using (var kb = new SolidBrush(Color.White))
                g.FillEllipse(kb, kx, sr.Y + Theme.S(3), knob, knob);

            var tr = L.Ar ? new RectangleF(sw + Theme.S(10), 0, Width - sw - Theme.S(10), Height)
                          : new RectangleF(0, 0, Width - sw - Theme.S(10), Height);
            Theme.DrawText(g, Text, Font, Hover ? Color.White : Theme.Text, tr, StringAlignment.Near);
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
            using (var ib = new LinearGradientBrush(inner, Color.FromArgb(22, 34, 60), Color.FromArgb(12, 18, 34), 90f))
                g.FillEllipse(ib, inner);

            // المسار
            var ring = RectangleF.Inflate(r, -Theme.S(6), -Theme.S(6));
            using (var track = new Pen(Color.FromArgb(30, 44, 74), Theme.S(5)))
                g.DrawEllipse(track, ring);

            using (var pen = new Pen(c, Theme.S(5)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (_state == GuardState.Scanning)
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
                float ls = inner.Width * 0.72f;
                var lr = new RectangleF(inner.X + (inner.Width - ls) / 2f, inner.Y + (inner.Height - ls) / 2f, ls, ls);
                if (_state == GuardState.Scanning)
                {
                    using var f = Theme.Font(20f, FontStyle.Bold);
                    Theme.DrawText(g, _progress + "%", f, Color.White, inner);
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
            using var pen = new Pen(Hover ? Color.White : Theme.Muted, Theme.S(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
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
                    using (var b = new SolidBrush(Hover ? Color.White : Theme.Muted))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(Text, Font, b, r, sf);
                    break;
            }
        }
    }
}

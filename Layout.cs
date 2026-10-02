// ERESM-RDP-Manager — Raster: Zellen (Pane) mit je einer Sitzung, Ablage-Anzeige beim Ziehen.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RdpManager
{
    public enum DockZone { None, Center, Left, Right, Top, Bottom, Window, TabStrip }

    // Eine Rasterzelle: Kopfzeile (nur sichtbar, wenn mehrere Zellen) + Fläche für eine Sitzung.
    public class Pane : Panel
    {
        public SessionPage Page { get; private set; }
        public event Action<Pane> HeaderClicked, CloseClicked;
        public readonly Panel Body = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        readonly Panel header = new Panel { Dock = DockStyle.Top };
        readonly Label title = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(8, 0, 0, 0) };
        readonly Label close = new Label { Dock = DockStyle.Right, Text = "✕", TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
        readonly Panel accent = new Panel { Dock = DockStyle.Top, Height = 2 };
        readonly ToolTip tip = new ToolTip();
        bool active;

        public Pane(float scale)
        {
            Dock = DockStyle.Fill;
            header.Height = (int)(24 * scale);
            close.Width = (int)(28 * scale);
            header.Controls.Add(title);
            header.Controls.Add(close);
            header.Controls.Add(accent);
            Controls.Add(Body);
            Controls.Add(header);
            title.Click += delegate { if (HeaderClicked != null) HeaderClicked(this); };
            header.Click += delegate { if (HeaderClicked != null) HeaderClicked(this); };
            close.Click += delegate { if (CloseClicked != null) CloseClicked(this); };
            ApplyLanguage();
        }

        public bool HeaderVisible { set { header.Visible = value; } }

        public bool Active
        {
            get { return active; }
            set { active = value; ApplyTheme(); }
        }

        // Sitzung anzeigen; die bisherige gibt der Aufrufer vorher ab.
        public void SetPage(SessionPage p)
        {
            Page = p;
            if (p != null)
            {
                p.Parent = Body;
                p.Dock = DockStyle.Fill;
                Body.PerformLayout();      // Größe erst setzen, dann zeigen — der Server bekommt höchstens eine Änderung
                p.Visible = true;
            }
            UpdateTitle();
        }

        public SessionPage TakePage()
        {
            var p = Page;
            Page = null;
            UpdateTitle();
            return p;
        }

        public void UpdateTitle()
        {
            title.Text = Page == null ? L.T("Leere Zelle – Verbindung hierher ziehen") : Page.Text;
            title.Font = new Font(Font, active ? FontStyle.Bold : FontStyle.Regular);
        }

        public void ApplyLanguage()
        {
            tip.SetToolTip(close, L.T("Aus dem Raster nehmen (Sitzung bleibt als Tab offen)"));
            UpdateTitle();
        }

        public void ApplyTheme()
        {
            var t = Theme.Current;
            header.BackColor = t.TabStrip;
            title.ForeColor = active ? (t.DarkChrome || t.Dark ? Color.White : t.Text) : t.TabText;
            title.BackColor = Color.Transparent;
            close.ForeColor = t.TabText;
            close.BackColor = Color.Transparent;
            accent.BackColor = active ? t.Accent : t.TabStrip;
            Body.BackColor = Color.Black;
            UpdateTitle();
        }

        // Ablagezone für einen Bildschirmpunkt: Ränder (je ein Viertel) teilen, die Mitte ersetzt.
        public DockZone ZoneAt(Point screen, out Rectangle zoneRect)
        {
            Rectangle r = RectangleToScreen(ClientRectangle);
            zoneRect = r;
            if (!r.Contains(screen)) return DockZone.None;
            double x = (screen.X - r.Left) / (double)r.Width, y = (screen.Y - r.Top) / (double)r.Height;
            double edge = 0.25;
            double dl = x, dr = 1 - x, dt = y, db = 1 - y;
            double min = Math.Min(Math.Min(dl, dr), Math.Min(dt, db));
            if (min > edge) return DockZone.Center;
            if (min == dl) { zoneRect = new Rectangle(r.Left, r.Top, r.Width / 2, r.Height); return DockZone.Left; }
            if (min == dr) { zoneRect = new Rectangle(r.Left + r.Width / 2, r.Top, r.Width - r.Width / 2, r.Height); return DockZone.Right; }
            if (min == dt) { zoneRect = new Rectangle(r.Left, r.Top, r.Width, r.Height / 2); return DockZone.Top; }
            zoneRect = new Rectangle(r.Left, r.Top + r.Height / 2, r.Width, r.Height - r.Height / 2);
            return DockZone.Bottom;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }

    // Halbtransparente Fläche über der Ablagezone — eigenes Fenster, damit sie auch über den RDP-Sitzungen liegt.
    public class DropOverlay : Form
    {
        public DropOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Opacity = 0.35;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000;   // LAYERED | TRANSPARENT | TOOLWINDOW | NOACTIVATE
                return cp;
            }
        }

        public void ShowAt(Rectangle r)
        {
            BackColor = Theme.Current.Accent;
            if (Bounds != r) Bounds = r;
            if (!Visible) Show();
        }
    }

    static class Input
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern IntPtr GetFocus();
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr child);
        public static bool EscapePressed { get { return (GetAsyncKeyState(0x1B) & 0x8000) != 0; } }
    }
}

// RDP-Manager — eigene Tableiste: Statuspunkt, Schließen-Kreuz, Ziehen zum Umsortieren, hell/dunkel.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RdpManager
{
    public class TabStrip : Control
    {
        public readonly List<SessionPage> Pages = new List<SessionPage>();
        public event EventHandler SelectedChanged;
        public event Action<SessionPage> CloseRequested;
        public event Action<SessionPage> DragOut;      // Tab nach unten herausgezogen: Raster/Fenster übernimmt
        public ContextMenuStrip TabMenu;
        public SessionPage MenuTarget { get; private set; }   // Tab unter dem Rechtsklick

        SessionPage selected;
        int hoverIndex = -1;
        bool hoverClose;
        int dragIndex = -1;
        Point dragStart;
        bool dragging;
        readonly ToolTip tip = new ToolTip();
        string tipText;
        readonly float scale;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            Height = (int)(34 * scale);
            Dock = DockStyle.Top;
        }

        public SessionPage Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        public void Add(SessionPage p) { Pages.Add(p); Invalidate(); }

        public void Remove(SessionPage p)
        {
            int i = Pages.IndexOf(p);
            if (i < 0) return;
            Pages.RemoveAt(i);
            if (selected == p) Selected = Pages.Count == 0 ? null : Pages[Math.Min(i, Pages.Count - 1)];
            Invalidate();
        }

        // ---------- Geometrie ----------

        List<Rectangle> TabRects()
        {
            var rects = new List<Rectangle>();
            int pad = (int)(6 * scale), top = (int)(5 * scale), h = Height - top;
            int minW = (int)(90 * scale), maxW = (int)(230 * scale);
            var widths = new List<int>();
            int total = 0;
            foreach (var p in Pages)
            {
                int w = TextRenderer.MeasureText(p.Text, Font).Width + (int)(58 * scale);
                w = Math.Max(minW, Math.Min(maxW, w));
                widths.Add(w);
                total += w;
            }
            int avail = Width - 2 * pad;
            if (total > avail && Pages.Count > 0)
            {
                int each = Math.Max((int)(60 * scale), avail / Pages.Count);
                for (int i = 0; i < widths.Count; i++) widths[i] = Math.Min(widths[i], each);
            }
            int x = pad;
            foreach (int w in widths) { rects.Add(new Rectangle(x, top, w, h)); x += w + (int)(2 * scale); }
            return rects;
        }

        Rectangle CloseRect(Rectangle tab)
        {
            int s = (int)(16 * scale);
            return new Rectangle(tab.Right - s - (int)(8 * scale), tab.Top + (tab.Height - s) / 2, s, s);
        }

        int IndexAt(Point pt)
        {
            var rects = TabRects();
            for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(pt)) return i;
            return -1;
        }

        // ---------- Zeichnen ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.Clear(t.TabStrip);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rects = TabRects();
            for (int i = 0; i < Pages.Count; i++)
            {
                var p = Pages[i];
                var r = rects[i];
                bool sel = p == selected;
                Color bg = sel ? t.TabActive : i == hoverIndex ? t.TabHover : t.TabStrip;
                using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
                if (sel) using (var b = new SolidBrush(t.Accent)) g.FillRectangle(b, r.Left, r.Top, r.Width, Math.Max(2, (int)(2 * scale)));
                else if (i + 1 < Pages.Count && Pages[i + 1] != selected && i != hoverIndex && i + 1 != hoverIndex)
                    using (var pen = new Pen(t.TabSeparator)) g.DrawLine(pen, r.Right + 1, r.Top + r.Height / 4, r.Right + 1, r.Bottom - r.Height / 4);

                int d = (int)(8 * scale);
                Color dot = p.IsLoggedIn ? Color.SeaGreen : p.State == SessionState.Disconnected ? Color.Firebrick : Color.DarkOrange;
                using (var b = new SolidBrush(dot)) g.FillEllipse(b, r.Left + (int)(10 * scale), r.Top + (r.Height - d) / 2, d, d);

                var cr = CloseRect(r);
                var textRect = new Rectangle(r.Left + (int)(24 * scale), r.Top, cr.Left - r.Left - (int)(28 * scale), r.Height);
                TextRenderer.DrawText(g, p.Text, Font, textRect, sel ? t.Text : t.TabText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                Color xColor = sel ? t.Muted : t.TabText;
                if (i == hoverIndex && hoverClose) using (var b = new SolidBrush(Color.FromArgb(50, xColor))) g.FillEllipse(b, cr);
                if (sel || i == hoverIndex)
                    using (var pen = new Pen(xColor, 1.5f * scale))
                    {
                        int m = (int)(5 * scale);
                        g.DrawLine(pen, cr.Left + m, cr.Top + m, cr.Right - m, cr.Bottom - m);
                        g.DrawLine(pen, cr.Right - m, cr.Top + m, cr.Left + m, cr.Bottom - m);
                    }
            }
            using (var pen = new Pen(t.DarkChrome && !t.Dark ? t.TabStrip : t.Border)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        // ---------- Maus ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = IndexAt(e.Location);
            if (i < 0) return;
            var p = Pages[i];
            if (e.Button == MouseButtons.Middle) { if (CloseRequested != null) CloseRequested(p); return; }
            if (e.Button == MouseButtons.Left && CloseRect(TabRects()[i]).Contains(e.Location)) { if (CloseRequested != null) CloseRequested(p); return; }
            if (e.Button == MouseButtons.Right)
            {
                // Rechtsklick wählt nicht um — sonst würde die Sitzung in der aktiven Rasterzelle ersetzt
                MenuTarget = p;
                if (TabMenu != null) { TabMenu.Closed -= ClearMenuTarget; TabMenu.Closed += ClearMenuTarget; TabMenu.Show(this, e.Location); }
                return;
            }
            Selected = p;
            if (e.Button == MouseButtons.Left) { dragIndex = i; dragStart = e.Location; dragging = false; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragIndex >= 0 && e.Button == MouseButtons.Left)
            {
                if (!dragging && (Math.Abs(e.X - dragStart.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - dragStart.Y) > SystemInformation.DragSize.Height))
                { dragging = true; Cursor = Cursors.SizeWE; }
                if (dragging && e.Y > Height + (int)(8 * scale) && DragOut != null)
                {
                    // Tab nach unten aus der Leiste gezogen: Ablage im Raster oder außerhalb als eigenes Fenster
                    var p = Pages[dragIndex];
                    dragIndex = -1; dragging = false; Cursor = Cursors.Default; Capture = false;
                    DragOut(p);
                    return;
                }
                if (dragging)
                {
                    // Tab wandert, sobald die Maus die Mitte des Nachbarn überquert
                    var rects = TabRects();
                    int target = dragIndex;
                    for (int i = 0; i < rects.Count; i++)
                    {
                        int mid = rects[i].Left + rects[i].Width / 2;
                        if (i < dragIndex && e.X < mid) { target = i; break; }
                        if (i > dragIndex && e.X > mid) target = i;
                    }
                    if (target != dragIndex)
                    {
                        var p = Pages[dragIndex];
                        Pages.RemoveAt(dragIndex);
                        Pages.Insert(target, p);
                        dragIndex = target;
                        hoverIndex = target;
                        Invalidate();
                    }
                    return;
                }
            }

            int h = IndexAt(e.Location);
            bool hc = h >= 0 && CloseRect(TabRects()[h]).Contains(e.Location);
            if (h != hoverIndex || hc != hoverClose) { hoverIndex = h; hoverClose = hc; Invalidate(); }
            string text = h >= 0 ? (hc ? L.T("Tab schließen (oder mittlere Maustaste)") : Pages[h].Tooltip) : null;
            if (text != tipText) { tipText = text; tip.SetToolTip(this, text); }
        }

        void ClearMenuTarget(object sender, ToolStripDropDownClosedEventArgs e)
        {
            // erst nach dem Klick auf einen Menüeintrag zurücksetzen
            BeginInvoke((MethodInvoker)delegate { MenuTarget = null; });
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragIndex = -1;
            if (dragging) { dragging = false; Cursor = Cursors.Default; }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverIndex != -1) { hoverIndex = -1; hoverClose = false; Invalidate(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }
}

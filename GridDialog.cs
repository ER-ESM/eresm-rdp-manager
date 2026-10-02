// ERESM-RDP-Manager — Raster-Dialog: Aufteilung wählen, offene Verbindungen per Drag & Drop in die Felder setzen.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace RdpManager
{
    class GridDialog : Form
    {
        // Aufteilungen zeilenweise: Anzahl Felder je Zeile
        public static readonly int[][] Presets = {
            new[] { 1 }, new[] { 2 }, new[] { 1, 1 }, new[] { 3 }, new[] { 1, 2 }, new[] { 2, 1 }, new[] { 2, 2 }, new[] { 3, 3 }, new[] { 3, 3, 3 } };

        static readonly string[] PresetNames = {
            "Eine Sitzung", "2 nebeneinander", "2 übereinander", "3 nebeneinander", "1 oben, 2 unten",
            "2 oben, 1 unten", "2 × 2", "3 × 2", "3 × 3" };

        class DragItem { public SessionPage Page; public int FromCell = -1; }

        readonly List<SessionPage> pages;
        readonly FlowLayoutPanel presetBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        readonly ListBox list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
        readonly Panel preview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0) };
        readonly ToolTip tip = new ToolTip();
        readonly float scale;
        int[] rows;
        SessionPage[] cells;
        int presetIndex;
        int listDragIndex = -1;
        Point listDragStart;

        public int[] Rows { get { return rows; } }
        public SessionPage[] Cells { get { return cells; } }

        public GridDialog(List<SessionPage> openPages)
        {
            pages = openPages;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            Text = L.T("Raster anordnen");
            Font = new Font("Segoe UI", 9f);
            Icon = AppIcon.ToIcon(32, null);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Size = new Size((int)(900 * scale), (int)(600 * scale));
            MinimumSize = new Size((int)(640 * scale), (int)(440 * scale));
            Padding = new Padding((int)(14 * scale));
            KeyPreview = true;

            // Aufteilungen als Vorschaubilder
            for (int i = 0; i < Presets.Length; i++)
            {
                int idx = i;
                var b = new Button { Size = new Size((int)(64 * scale), (int)(46 * scale)), FlatStyle = FlatStyle.Flat, Tag = idx, Margin = new Padding(0, 0, (int)(6 * scale), (int)(6 * scale)) };
                b.Paint += PaintPresetButton;
                b.Click += delegate { SelectPreset(idx); };
                tip.SetToolTip(b, L.T(PresetNames[idx]));
                presetBar.Controls.Add(b);
            }

            // Links: offene Verbindungen (Drag-Quelle)
            var left = new Panel { Dock = DockStyle.Left, Width = (int)(220 * scale) };
            left.Controls.Add(list);
            left.Controls.Add(new Label { Text = L.T("Offene Verbindungen"), Dock = DockStyle.Top, Height = (int)(24 * scale), Font = new Font(Font, FontStyle.Bold) });
            list.ItemHeight = (int)(26 * scale);
            foreach (var p in pages) list.Items.Add(p);
            list.DrawItem += DrawListItem;
            list.MouseDown += (s, e) => { listDragIndex = list.IndexFromPoint(e.Location); listDragStart = e.Location; };
            list.MouseMove += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || listDragIndex < 0) return;
                if (Math.Abs(e.X - listDragStart.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - listDragStart.Y) < SystemInformation.DragSize.Height) return;
                var page = (SessionPage)list.Items[listDragIndex];
                listDragIndex = -1;
                list.DoDragDrop(new DragItem { Page = page }, DragDropEffects.Move);
            };

            var hint = new Label
            {
                Dock = DockStyle.Bottom, AutoSize = false, Height = (int)(40 * scale), TextAlign = ContentAlignment.MiddleLeft, Tag = "muted",
                Text = L.T("Verbindungen aus der Liste in die Felder ziehen – oder links anklicken und dann ein Feld anklicken. Felder untereinander tauschen per Ziehen, ✕ leert ein Feld. Nicht platzierte Verbindungen bleiben als Tab offen.")
            };

            var buttons = Ui.Buttons(this, "Übernehmen");
            var btnClear = new Button { Text = L.T("Alle Felder leeren"), AutoSize = true, MinimumSize = new Size(90, 0) };
            btnClear.Click += delegate { for (int i = 0; i < cells.Length; i++) cells[i] = null; RefreshCells(); };
            buttons.Controls.Add(btnClear);

            var middle = new Panel { Dock = DockStyle.Fill };
            middle.Controls.Add(preview);
            middle.Controls.Add(left);
            Controls.Add(middle);
            Controls.Add(hint);
            Controls.Add(presetBar);
            Controls.Add(new Label { Text = L.T("Aufteilung"), Dock = DockStyle.Top, Height = (int)(24 * scale), Font = new Font(Font, FontStyle.Bold) });
            Controls.Add(buttons);

            // Vorbelegung: passende Aufteilung für die Anzahl offener Verbindungen, Felder in Tab-Reihenfolge
            int n = pages.Count;
            int start = n <= 1 ? 0 : n == 2 ? 1 : n == 3 ? 3 : n == 4 ? 6 : n <= 6 ? 7 : 8;
            cells = new SessionPage[0];
            SelectPreset(start);
            for (int i = 0; i < cells.Length && i < n; i++) cells[i] = pages[i];
            RefreshCells();

            Load += delegate { Theme.Current.Apply(this); ThemeOwn(); };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }

        void ThemeOwn()
        {
            var t = Theme.Current;
            list.BackColor = t.Window;
            list.ForeColor = t.Text;
            preview.BackColor = Color.Transparent;
            foreach (Control b in presetBar.Controls) { b.BackColor = t.Window; ((Button)b).FlatAppearance.BorderColor = t.Border; }
            RefreshCells();
        }

        // ---------- Aufteilung ----------

        void SelectPreset(int idx)
        {
            presetIndex = idx;
            rows = Presets[idx];
            // bisherige Belegung in Reihenfolge übernehmen
            var keep = cells.Where(c => c != null).ToList();
            cells = new SessionPage[rows.Sum()];
            for (int i = 0; i < cells.Length && i < keep.Count; i++) cells[i] = keep[i];
            BuildCells();
            presetBar.Invalidate(true);
            foreach (Control b in presetBar.Controls) b.Invalidate();
        }

        void PaintPresetButton(object sender, PaintEventArgs e)
        {
            var b = (Button)sender;
            int idx = (int)b.Tag;
            var t = Theme.Current;
            bool sel = idx == presetIndex;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(sel ? t.Selection : t.Window);
            var area = Rectangle.Inflate(b.ClientRectangle, -(int)(8 * scale), -(int)(7 * scale));
            var r = Presets[idx];
            float gap = 2 * scale, rh = (area.Height - gap * (r.Length - 1)) / r.Length;
            using (var fill = new SolidBrush(sel ? t.Accent : t.Muted))
                for (int row = 0; row < r.Length; row++)
                {
                    float cw = (area.Width - gap * (r[row] - 1)) / r[row];
                    for (int col = 0; col < r[row]; col++)
                        g.FillRectangle(fill, area.Left + col * (cw + gap), area.Top + row * (rh + gap), cw, rh);
                }
            using (var p = new Pen(sel ? t.Accent : t.Border, sel ? 2 : 1)) g.DrawRectangle(p, 0, 0, b.Width - 1, b.Height - 1);
        }

        // ---------- Felder ----------

        void BuildCells()
        {
            preview.SuspendLayout();
            foreach (Control c in preview.Controls.Cast<Control>().ToList()) { preview.Controls.Remove(c); c.Dispose(); }
            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = rows.Length, ColumnCount = 1 };
            int cellIndex = 0;
            for (int row = 0; row < rows.Length; row++)
            {
                outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows.Length));
                var inner = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = rows[row], Margin = Padding.Empty };
                inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                for (int col = 0; col < rows[row]; col++)
                {
                    inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / rows[row]));
                    inner.Controls.Add(MakeCell(cellIndex++), col, 0);
                }
                outer.Controls.Add(inner, 0, row);
            }
            preview.Controls.Add(outer);
            preview.ResumeLayout();
        }

        Panel MakeCell(int index)
        {
            var cell = new Panel { Dock = DockStyle.Fill, Margin = new Padding((int)(4 * scale)), AllowDrop = true, Tag = index, Cursor = Cursors.Hand };
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(cell, true, null);
            bool hover = false;
            Point downAt = Point.Empty;
            cell.Paint += (s, e) => PaintCell(cell, index, hover, e.Graphics);
            cell.Resize += delegate { cell.Invalidate(); };
            cell.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(typeof(DragItem)) ? DragDropEffects.Move : DragDropEffects.None; hover = true; cell.Invalidate(); };
            cell.DragLeave += delegate { hover = false; cell.Invalidate(); };
            cell.DragDrop += (s, e) =>
            {
                hover = false;
                var item = (DragItem)e.Data.GetData(typeof(DragItem));
                if (item != null) Place(item.Page, index, item.FromCell);
            };
            cell.MouseDown += (s, e) => { downAt = e.Location; };
            cell.MouseMove += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || cells[index] == null) return;
                if (Math.Abs(e.X - downAt.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - downAt.Y) < SystemInformation.DragSize.Height) return;
                cell.DoDragDrop(new DragItem { Page = cells[index], FromCell = index }, DragDropEffects.Move);
            };
            cell.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (cells[index] != null && CloseRect(cell).Contains(e.Location)) { cells[index] = null; RefreshCells(); return; }
                // Klick-Alternative: links gewählte Verbindung in dieses Feld
                var sel = list.SelectedItem as SessionPage;
                if (sel != null) { Place(sel, index, -1); list.ClearSelected(); }
            };
            return cell;
        }

        Rectangle CloseRect(Panel cell)
        {
            int s = (int)(18 * scale);
            return new Rectangle(cell.Width - s - (int)(6 * scale), (int)(6 * scale), s, s);
        }

        // Verbindung in ein Feld setzen: aus anderem Feld entfernen bzw. beim Ziehen zwischen Feldern tauschen
        void Place(SessionPage page, int target, int fromCell)
        {
            if (fromCell >= 0 && fromCell != target)
            {
                var other = cells[target];
                cells[target] = page;
                cells[fromCell] = other;
            }
            else
            {
                for (int i = 0; i < cells.Length; i++) if (cells[i] == page) cells[i] = null;
                cells[target] = page;
            }
            RefreshCells();
        }

        void RefreshCells()
        {
            preview.Invalidate(true);
            list.Invalidate();
        }

        void PaintCell(Panel cell, int index, bool hover, Graphics g)
        {
            var t = Theme.Current;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var page = index < cells.Length ? cells[index] : null;
            var r = new Rectangle(0, 0, cell.Width - 1, cell.Height - 1);
            using (var bg = new SolidBrush(page != null ? t.Selection : t.Window)) g.FillRectangle(bg, r);
            using (var pen = new Pen(hover ? t.Accent : page != null ? t.Accent : t.Border, hover ? 3 : 1.5f))
            {
                if (page == null && !hover) pen.DashStyle = DashStyle.Dash;
                g.DrawRectangle(pen, r);
            }
            var textRect = Rectangle.Inflate(r, -(int)(10 * scale), -(int)(8 * scale));
            if (page == null)
            {
                TextRenderer.DrawText(g, L.T("Hierher ziehen"), Font, textRect, t.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            using (var bold = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Bold))
            {
                var nameSize = TextRenderer.MeasureText(page.Text, bold);
                int cy = r.Top + r.Height / 2;
                TextRenderer.DrawText(g, page.Text, bold, new Rectangle(textRect.Left, cy - nameSize.Height, textRect.Width, nameSize.Height), t.SelectionText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, page.Conn.HostDisplay, Font, new Rectangle(textRect.Left, cy + 2, textRect.Width, nameSize.Height), t.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }
            var x = CloseRect(cell);
            using (var pen = new Pen(t.Muted, 1.6f * scale))
            {
                int m = (int)(5 * scale);
                g.DrawLine(pen, x.Left + m, x.Top + m, x.Right - m, x.Bottom - m);
                g.DrawLine(pen, x.Right - m, x.Top + m, x.Left + m, x.Bottom - m);
            }
        }

        void DrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var t = Theme.Current;
            var page = (SessionPage)list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            bool placed = cells.Contains(page);
            using (var bg = new SolidBrush(sel ? t.Selection : t.Window)) e.Graphics.FillRectangle(bg, e.Bounds);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int d = (int)(8 * scale);
            Color dot = page.IsLoggedIn ? Color.SeaGreen : page.State == SessionState.Disconnected ? Color.Firebrick : Color.DarkOrange;
            using (var b = new SolidBrush(dot)) e.Graphics.FillEllipse(b, e.Bounds.Left + (int)(8 * scale), e.Bounds.Top + (e.Bounds.Height - d) / 2, d, d);
            var tr = new Rectangle(e.Bounds.Left + (int)(24 * scale), e.Bounds.Top, e.Bounds.Width - (int)(50 * scale), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, page.Text, list.Font, tr, sel ? t.SelectionText : placed ? t.Muted : t.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (placed)
                TextRenderer.DrawText(e.Graphics, "✓", list.Font, new Rectangle(e.Bounds.Right - (int)(24 * scale), e.Bounds.Top, (int)(20 * scale), e.Bounds.Height), t.Accent,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }
}

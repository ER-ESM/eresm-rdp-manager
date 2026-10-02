// RDP-Manager — Darstellung hell/dunkel (Farben, Werkzeugleiste, Titelleiste, Bildlaufleisten).
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RdpManager
{
    public class Theme
    {
        public bool Dark;                  // dunkle Flächen (Bildlaufleisten, Eingaben)
        public bool DarkChrome;            // dunkler Kopfbereich/Titelleiste
        public Color Window, Surface, Text, Muted, Border, TabStrip, TabActive, TabHover, Accent, Input, Selection, SelectionText;
        public Color ToolbarBack, ToolbarText, ToolbarHover, TabText, TabSeparator;
        public Color SidebarBack, SidebarText, SidebarInput, SidebarSelection, SidebarSelectionText, StatusBack, StatusText;
        public bool? SidebarDark;              // dunkle Seitenleiste (Bildlaufleiste/Aufklapp-Pfeile)
        public Color? Caption, CaptionText;   // Windows 11: eigene Titelleistenfarbe
        public bool AccentPrimaryButton;      // Hauptknopf in Dialogen in Akzentfarbe

        // Fehlende Kopf-/Tab-Farben aus den Grundfarben ableiten
        Theme Done()
        {
            if (ToolbarBack.IsEmpty) ToolbarBack = Surface;
            if (ToolbarText.IsEmpty) ToolbarText = Text;
            if (ToolbarHover.IsEmpty) ToolbarHover = Dark ? TabActive : Selection;
            if (TabText.IsEmpty) TabText = Muted;
            if (TabSeparator.IsEmpty) TabSeparator = Border;
            if (SidebarBack.IsEmpty) SidebarBack = Window;
            if (SidebarText.IsEmpty) SidebarText = Text;
            if (SidebarInput.IsEmpty) SidebarInput = Input;
            if (SidebarSelection.IsEmpty) SidebarSelection = Selection;
            if (SidebarSelectionText.IsEmpty) SidebarSelectionText = SelectionText;
            if (StatusBack.IsEmpty) StatusBack = Surface;
            if (StatusText.IsEmpty) StatusText = Text;
            if (!SidebarDark.HasValue) SidebarDark = Dark;
            return this;
        }

        public static readonly Theme Light = new Theme
        {
            Dark = false,
            Window = Color.White, Surface = Color.FromArgb(243, 243, 243), Text = Color.FromArgb(27, 27, 27),
            Muted = Color.FromArgb(110, 110, 110), Border = Color.FromArgb(214, 214, 214),
            TabStrip = Color.FromArgb(232, 232, 232), TabActive = Color.White, TabHover = Color.FromArgb(244, 244, 244),
            Accent = Color.FromArgb(24, 104, 184), Input = Color.White,
            Selection = Color.FromArgb(204, 228, 247), SelectionText = Color.Black
        }.Done();

        public static readonly Theme DarkTheme = new Theme
        {
            Dark = true,
            Window = Color.FromArgb(32, 32, 32), Surface = Color.FromArgb(43, 43, 43), Text = Color.FromArgb(230, 230, 230),
            Muted = Color.FromArgb(160, 160, 160), Border = Color.FromArgb(62, 62, 62),
            TabStrip = Color.FromArgb(24, 24, 24), TabActive = Color.FromArgb(45, 45, 45), TabHover = Color.FromArgb(36, 36, 36),
            Accent = Color.FromArgb(76, 160, 240), Input = Color.FromArgb(45, 45, 45),
            Selection = Color.FromArgb(9, 71, 113), SelectionText = Color.White, DarkChrome = true
        }.Done();

        // ERESM-Design (Werte aus lib/eresm-ui/css/tokens.css): Petrol-Kopf, warme Arbeitsfläche, Akzent #0c7569
        public static readonly Theme Eresm = new Theme
        {
            Dark = false, DarkChrome = true, AccentPrimaryButton = true,
            Window = Color.White, Surface = Color.FromArgb(246, 245, 240), Text = Color.FromArgb(36, 50, 48),
            Muted = Color.FromArgb(102, 115, 109), Border = Color.FromArgb(220, 223, 216),
            TabStrip = Color.FromArgb(21, 40, 43), TabActive = Color.White, TabHover = Color.FromArgb(35, 58, 62),
            TabText = Color.FromArgb(190, 204, 200), TabSeparator = Color.FromArgb(52, 74, 78),
            Accent = Color.FromArgb(12, 117, 105), Input = Color.White,
            Selection = Color.FromArgb(231, 241, 238), SelectionText = Color.FromArgb(9, 92, 82),
            ToolbarBack = Color.FromArgb(21, 40, 43), ToolbarText = Color.FromArgb(242, 245, 243), ToolbarHover = Color.FromArgb(40, 66, 70),
            Caption = Color.FromArgb(21, 40, 43), CaptionText = Color.FromArgb(242, 245, 243),
            // Rahmen rundum in Petrol: Liste links, Statusleiste unten
            SidebarBack = Color.FromArgb(27, 49, 52), SidebarText = Color.FromArgb(230, 236, 234), SidebarInput = Color.FromArgb(36, 61, 64),
            SidebarSelection = Color.FromArgb(12, 117, 105), SidebarSelectionText = Color.White, SidebarDark = true,
            StatusBack = Color.FromArgb(21, 40, 43), StatusText = Color.FromArgb(200, 212, 208)
        }.Done();

        public static Theme Current = Light;

        // Modus: "System" (wie Windows-Einstellung „App-Modus“), "Hell", "Dunkel"
        public static Theme Resolve(string mode)
        {
            if (mode == "Dunkel") return DarkTheme;
            if (mode == "Hell") return Light;
            if (mode == "ERESM") return Eresm;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    if (v is int && (int)v == 0) return DarkTheme;
                }
            }
            catch { }
            return Light;
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        public void TitleBar(Form f)
        {
            if (!f.IsHandleCreated) return;
            int on = DarkChrome ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                // 35 = Titelleistenfarbe, 36 = Titeltextfarbe (Windows 11); 0xFFFFFFFF = Systemstandard
                int cap = Caption.HasValue ? ColorRef(Caption.Value) : unchecked((int)0xFFFFFFFF);
                int txt = CaptionText.HasValue ? ColorRef(CaptionText.Value) : unchecked((int)0xFFFFFFFF);
                DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);
                DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);
            }
            catch { }
        }

        // Farben rekursiv anwenden; eigene Steuerelemente (Tableiste, Sitzungen) zeichnen selbst.
        public void Apply(Control root)
        {
            var f = root as Form;
            if (f != null)
            {
                f.BackColor = Surface;
                f.ForeColor = Text;
                TitleBar(f);
                f.HandleCreated -= TitleBarOnCreate;
                f.HandleCreated += TitleBarOnCreate;
            }
            ApplyOne(root);
            foreach (Control c in root.Controls) Apply(c);
        }

        static void TitleBarOnCreate(object sender, EventArgs e) { Current.TitleBar((Form)sender); }

        void ApplyOne(Control c)
        {
            if (c is Form) return;
            if (c is ToolStrip)
            {
                var ts = (ToolStrip)c;
                ts.Renderer = new ThemedRenderer(this);
                bool bar = !(ts is StatusStrip);
                ts.BackColor = bar ? ToolbarBack : Surface;
                ts.ForeColor = bar ? ToolbarText : Text;
                foreach (ToolStripItem item in ts.Items) ApplyItem(item, bar);
                return;
            }
            if (c is TreeView)
            {
                var tv = (TreeView)c;
                tv.BackColor = SidebarBack;
                tv.ForeColor = SidebarText;
                tv.LineColor = Border;
                Explorer(tv, SidebarDark.Value);
                return;
            }
            if (c is TextBox || c is ComboBox || c is NumericUpDown)
            {
                c.BackColor = Input;
                c.ForeColor = Text;
                if (c is TextBox) { ((TextBox)c).BorderStyle = BorderStyle.FixedSingle; if (((TextBox)c).Multiline) Explorer(c); }
                if (c is ComboBox) ((ComboBox)c).FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.Standard;
                if (c is NumericUpDown) ((NumericUpDown)c).BorderStyle = BorderStyle.FixedSingle;
                return;
            }
            if (c is Button)
            {
                var b = (Button)c;
                var form = b.FindForm();
                if (AccentPrimaryButton && form != null && form.AcceptButton == b)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = Accent;
                    b.ForeColor = Color.White;
                    b.UseVisualStyleBackColor = false;
                    b.FlatAppearance.BorderColor = Accent;
                    return;
                }
                b.FlatStyle = Dark || AccentPrimaryButton ? FlatStyle.Flat : FlatStyle.Standard;
                b.BackColor = Dark ? TabActive : AccentPrimaryButton ? Window : SystemColors.Control;
                b.ForeColor = Text;
                b.UseVisualStyleBackColor = !Dark && !AccentPrimaryButton;
                b.FlatAppearance.BorderColor = AccentPrimaryButton ? Color.FromArgb(132, 142, 136) : Border;
                return;
            }
            if (c is Label)
            {
                // Hinweis-Labels behalten ihren „leisen“ Ton, Fehler-Labels ihr Rot
                var l = (Label)c;
                if (l.Tag as string == "muted") l.ForeColor = Muted;
                else if (l.Tag as string != "error") l.ForeColor = Text;
                l.BackColor = Color.Transparent;
                return;
            }
            if (c is CheckBox) { c.ForeColor = Text; c.BackColor = Color.Transparent; return; }
            if (c is SplitContainer) { c.BackColor = Border; ((SplitContainer)c).Panel1.BackColor = Surface; ((SplitContainer)c).Panel2.BackColor = Window; return; }
            if (c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel || c is SplitterPanel) { if (!(c is SessionPage)) { c.BackColor = Color.Transparent == c.BackColor ? c.BackColor : Surface; c.ForeColor = Text; } return; }
        }

        void ApplyItem(ToolStripItem item, bool onBar)
        {
            item.ForeColor = onBar ? ToolbarText : item.Tag as string == "muted" ? Muted : Text;
            var dd = item as ToolStripDropDownItem;
            if (dd != null)
            {
                dd.DropDown.Renderer = new ThemedRenderer(this);
                dd.DropDown.BackColor = Surface;
                foreach (ToolStripItem sub in dd.DropDownItems) ApplyItem(sub, false);
            }
        }

        public void ApplyMenu(ContextMenuStrip m)
        {
            m.Renderer = new ThemedRenderer(this);
            m.BackColor = Surface;
            m.ForeColor = Text;
            foreach (ToolStripItem i in m.Items) ApplyItem(i, false);
        }

        void Explorer(Control c) { Explorer(c, Dark); }

        void Explorer(Control c, bool dark)
        {
            string name = dark ? "DarkMode_Explorer" : "Explorer";
            if (c.IsHandleCreated) SetWindowTheme(c.Handle, name, null);
            else c.HandleCreated += (s, e) => SetWindowTheme(c.Handle, name, null);
        }
    }

    class ThemedRenderer : ToolStripProfessionalRenderer
    {
        readonly Theme t;
        public ThemedRenderer(Theme t) : base(new ThemedColors(t)) { this.t = t; RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool bar = OnBar(e.ToolStrip);
            Color normal = bar ? t.ToolbarText : e.ToolStrip is StatusStrip ? t.StatusText : t.Text;
            e.TextColor = !e.Item.Enabled ? Color.FromArgb(bar && t.DarkChrome ? 120 : 160, normal)
                        : e.Item.Tag as string == "muted" ? (bar && t.DarkChrome ? Color.FromArgb(160, normal) : t.Muted) : normal;
            base.OnRenderItemText(e);
        }

        static bool OnBar(ToolStrip s) { return !(s is ToolStripDropDown) && !(s is StatusStrip); }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Color back = OnBar(e.ToolStrip) ? t.ToolbarBack : e.ToolStrip is StatusStrip ? t.StatusBack : t.Surface;
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (!OnBar(e.ToolStrip)) { base.OnRenderButtonBackground(e); return; }
            if (e.Item.Selected || e.Item.Pressed)
                using (var b = new SolidBrush(t.ToolbarHover)) e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) { OnRenderButtonBackground(e); }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (!OnBar(e.ToolStrip)) { base.OnRenderSeparator(e); return; }
            int x = e.Item.Width / 2;
            using (var p = new Pen(t.DarkChrome && !t.Dark ? Color.FromArgb(70, t.ToolbarText) : t.Border)) e.Graphics.DrawLine(p, x, 4, x, e.Item.Height - 4);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is StatusStrip) return;
            if (e.ToolStrip is ToolStripDropDown) { using (var p = new Pen(t.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); return; }
            if (OnBar(e.ToolStrip) && t.DarkChrome && !t.Dark) return;   // dunkler Kopf ohne Trennlinie
            using (var p = new Pen(t.Border)) e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = OnBar(e.Item != null ? e.Item.Owner : null) ? t.ToolbarText : t.Text; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using (var b = new SolidBrush(t.Accent)) e.Graphics.FillEllipse(b, r.Left + r.Width / 2 - 3, r.Top + r.Height / 2 - 3, 7, 7);
        }
    }

    class ThemedColors : ProfessionalColorTable
    {
        readonly Theme t;
        public ThemedColors(Theme t) { this.t = t; UseSystemColors = false; }
        public override Color ToolStripGradientBegin { get { return t.Surface; } }
        public override Color ToolStripGradientMiddle { get { return t.Surface; } }
        public override Color ToolStripGradientEnd { get { return t.Surface; } }
        public override Color ToolStripBorder { get { return t.Border; } }
        public override Color ToolStripDropDownBackground { get { return t.Surface; } }
        public override Color ImageMarginGradientBegin { get { return t.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return t.Surface; } }
        public override Color ImageMarginGradientEnd { get { return t.Surface; } }
        public override Color MenuBorder { get { return t.Border; } }
        public override Color MenuItemBorder { get { return t.Selection; } }
        public override Color MenuItemSelected { get { return t.Dark ? t.TabActive : t.Selection; } }
        public override Color MenuItemSelectedGradientBegin { get { return MenuItemSelected; } }
        public override Color MenuItemSelectedGradientEnd { get { return MenuItemSelected; } }
        public override Color MenuItemPressedGradientBegin { get { return MenuItemSelected; } }
        public override Color MenuItemPressedGradientEnd { get { return MenuItemSelected; } }
        public override Color ButtonSelectedHighlight { get { return MenuItemSelected; } }
        public override Color ButtonSelectedGradientBegin { get { return MenuItemSelected; } }
        public override Color ButtonSelectedGradientMiddle { get { return MenuItemSelected; } }
        public override Color ButtonSelectedGradientEnd { get { return MenuItemSelected; } }
        public override Color ButtonSelectedBorder { get { return t.Border; } }
        public override Color ButtonPressedGradientBegin { get { return t.Selection; } }
        public override Color ButtonPressedGradientMiddle { get { return t.Selection; } }
        public override Color ButtonPressedGradientEnd { get { return t.Selection; } }
        public override Color ButtonCheckedGradientBegin { get { return t.Selection; } }
        public override Color ButtonCheckedGradientEnd { get { return t.Selection; } }
        public override Color SeparatorDark { get { return t.Border; } }
        public override Color SeparatorLight { get { return t.Surface; } }
        public override Color StatusStripGradientBegin { get { return t.Surface; } }
        public override Color StatusStripGradientEnd { get { return t.Surface; } }
        public override Color CheckBackground { get { return t.Surface; } }
        public override Color CheckSelectedBackground { get { return t.Surface; } }
        public override Color CheckPressedBackground { get { return t.Surface; } }
    }
}

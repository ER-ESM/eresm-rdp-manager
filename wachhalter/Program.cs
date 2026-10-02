// RDP-Wachhalter 2.2 — hält mehrere Remotedesktop-Sitzungen wach; Standard: kurzer Fokus nur in lokalen Eingabepausen.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("RDP-Wachhalter")]
[assembly: System.Reflection.AssemblyVersion("2.2.0.0")]

namespace RdpWachhalter
{
    static class Native
    {
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }

        const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_MOUSEMOVE = 0x0200;
        const int VK_SHIFT = 0x10, SC_LSHIFT = 0x2A;
        const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;

        public class RdpWindow
        {
            public IntPtr Top, Input;
            public string Title;
            public override string ToString() { return Title; }
        }

        static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
        public static string TextOf(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }

        static readonly string[] RdpProcesses = { "mstsc", "msrdc", "msrdcw" };

        static string ProcessNameOf(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            try { return Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { return ""; }
        }

        // Remotedesktop-Fenster = Hauptfenster mit RDP-Eingabefenster (IHWindowClass) oder eines bekannten RDP-Clients.
        public static List<RdpWindow> FindRdpWindows()
        {
            var list = new List<RdpWindow>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                string title = TextOf(h);
                if (title.Length == 0) return true;
                IntPtr input = FindInput(h);
                if (input != IntPtr.Zero || Array.IndexOf(RdpProcesses, ProcessNameOf(h)) >= 0)
                    list.Add(new RdpWindow { Top = h, Input = input, Title = title });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static bool IsRdpWindow(IntPtr h)
        {
            return FindInput(h) != IntPtr.Zero || Array.IndexOf(RdpProcesses, ProcessNameOf(h)) >= 0;
        }

        static IntPtr FindInput(IntPtr top)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(top, delegate (IntPtr c, IntPtr l)
            {
                if (ClassOf(c) == "IHWindowClass") { found = c; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static uint LocalIdleMs()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            GetLastInputInfo(ref info);
            return unchecked((uint)Environment.TickCount - info.dwTime);
        }

        [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);
        const uint INPUT_MOUSE = 0, MOUSEEVENTF_MOVE = 0x1;

        // Windows erlaubt den Fokuswechsel nur dem Prozess mit der letzten Eingabe:
        // eine Mausbewegung um 0 Pixel macht uns dazu, ohne sichtbare Wirkung.
        static void ClaimLastInput()
        {
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_MOVE };
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        static bool ForceForeground(IntPtr h)
        {
            if (GetForegroundWindow() == h) return true;

            ClaimLastInput();
            SetForegroundWindow(h);
            if (GetForegroundWindow() == h) return true;

            IntPtr fg = GetForegroundWindow();
            uint pid;
            uint fgThread = GetWindowThreadProcessId(fg, out pid);
            uint me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            SetForegroundWindow(h);
            if (attached) AttachThreadInput(me, fgThread, false);
            if (GetForegroundWindow() == h) return true;

            SwitchToThisWindow(h, true);
            for (int i = 0; i < 5 && GetForegroundWindow() != h; i++) Thread.Sleep(20);
            return GetForegroundWindow() == h;
        }

        static void SendShiftInput()
        {
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki = new KEYBDINPUT { wScan = SC_LSHIFT, dwFlags = KEYEVENTF_SCANCODE };
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki = new KEYBDINPUT { wScan = SC_LSHIFT, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP };
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        // Fokus kurz auf das RDP-Fenster, Umschalt-Taste, Fokus zurück. Taste nur, wenn der Fokus sicher dort liegt.
        public static string PulseWithFocus(RdpWindow w)
        {
            if (IsIconic(w.Top)) return PulseMinimized(w);
            return PulseVisible(w);
        }

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);
        const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
        const uint LWA_ALPHA = 0x2;
        const int SW_SHOWNOACTIVATE = 4, SW_SHOWMINNOACTIVE = 7, DWMWA_TRANSITIONS_FORCEDISABLED = 3;

        // Minimiertes Fenster: unsichtbar (Transparenz 0, keine Animation) wiederherstellen, anstoßen,
        // wieder minimieren — der Originalzustand wird in jedem Fall zurückgesetzt.
        static string PulseMinimized(RdpWindow w)
        {
            IntPtr h = w.Top;
            int exStyle = GetWindowLong(h, GWL_EXSTYLE);
            int on = 1, off = 0;
            try
            {
                DwmSetWindowAttribute(h, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, sizeof(int));
                SetWindowLong(h, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);
                SetLayeredWindowAttributes(h, 0, 0, LWA_ALPHA);
                ShowWindow(h, SW_SHOWNOACTIVATE);
                Thread.Sleep(120);
                return PulseVisible(w);
            }
            finally
            {
                ShowWindow(h, SW_SHOWMINNOACTIVE);
                Thread.Sleep(50);
                SetWindowLong(h, GWL_EXSTYLE, exStyle);
                DwmSetWindowAttribute(h, DWMWA_TRANSITIONS_FORCEDISABLED, ref off, sizeof(int));
            }
        }

        static string PulseVisible(RdpWindow w)
        {
            IntPtr prev = GetForegroundWindow();
            if (prev == w.Top) { SendShiftInput(); return null; }
            if (!ForceForeground(w.Top)) return "Fokus wurde von Windows verweigert";
            Thread.Sleep(80);
            if (GetForegroundWindow() == w.Top) SendShiftInput();
            Thread.Sleep(80);
            if (prev != IntPtr.Zero && IsWindow(prev) && !ForceForeground(prev))
                return "Impuls gesendet, aber der Fokus ließ sich nicht an dein vorheriges Fenster zurückgeben";
            return null;
        }

        public static void PostShift(IntPtr input)
        {
            int down = 1 | (SC_LSHIFT << 16);
            int up = unchecked((int)(1u | ((uint)SC_LSHIFT << 16) | 0xC0000000u));
            PostMessage(input, WM_KEYDOWN, (IntPtr)VK_SHIFT, (IntPtr)down);
            PostMessage(input, WM_KEYUP, (IntPtr)VK_SHIFT, (IntPtr)up);
        }

        static bool toggle;
        public static void PostMouse(IntPtr input)
        {
            RECT r;
            GetClientRect(input, out r);
            toggle = !toggle;
            int x = Math.Max(1, (r.Right - r.Left) / 2) + (toggle ? 1 : 0);
            int y = Math.Max(1, (r.Bottom - r.Top) / 2);
            PostMessage(input, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)((y << 16) | (x & 0xFFFF)));
        }
    }

    class Target
    {
        public string Name = "";
        public int Minutes = 4;
        public bool Enabled = true;
        public DateTime NextDue = DateTime.MaxValue;
        public string LastResult = "";
        public bool LastOk = true;
        public DateTime LastPulse = DateTime.MinValue, LastActivity = DateTime.MinValue;
    }

    class Settings
    {
        public List<Target> Targets = new List<Target>();
        public int Minutes = 4;        // Vorschlag für neue Verbindungen
        public int Method = 0;         // 0 = kurz aktivieren in Eingabepause, 1 = Hintergrund Taste, 2 = Hintergrund Maus
        public int PauseSeconds = 60;
        public bool AutoStart = false;
        public int Width = 0, Height = 0;

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpWachhalter", "settings.ini"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            string legacyRemote = "";
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    int n;
                    if (k == "Target")
                    {
                        // Format: aktiv|Minuten|Name
                        var parts = v.Split(new[] { '|' }, 3);
                        if (parts.Length == 3 && parts[2].Trim().Length > 0 && int.TryParse(parts[1], out n))
                            s.Targets.Add(new Target { Enabled = parts[0] == "1", Minutes = Math.Max(1, Math.Min(240, n)), Name = parts[2].Trim() });
                    }
                    else if (k == "Remote") legacyRemote = v.Trim();
                    else if (k == "Minutes" && int.TryParse(v, out n)) s.Minutes = n;
                    else if (k == "Method" && int.TryParse(v, out n)) s.Method = n;
                    else if (k == "Pause" && int.TryParse(v, out n)) s.PauseSeconds = n;
                    else if (k == "AutoStart") s.AutoStart = v == "1";
                    else if (k == "Width" && int.TryParse(v, out n)) s.Width = n;
                    else if (k == "Height" && int.TryParse(v, out n)) s.Height = n;
                }
                // Übernahme aus Version 1.x (eine Verbindung)
                if (s.Targets.Count == 0 && legacyRemote.Length > 0)
                    s.Targets.Add(new Target { Name = ShortName(legacyRemote), Minutes = Math.Max(1, Math.Min(240, s.Minutes)) });
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var lines = new List<string> {
                    "Minutes=" + Minutes, "Method=" + Method, "Pause=" + PauseSeconds,
                    "AutoStart=" + (AutoStart ? "1" : "0"), "Width=" + Width, "Height=" + Height };
                foreach (var t in Targets) lines.Add("Target=" + (t.Enabled ? "1" : "0") + "|" + t.Minutes + "|" + t.Name);
                File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        // "EDU3 - 85.215.100.191 - Remotedesktopverbindung" -> "EDU3"
        public static string ShortName(string title)
        {
            int i = title.IndexOf(" - ", StringComparison.Ordinal);
            return (i > 0 ? title.Substring(0, i) : title).Trim();
        }
    }

    class MainForm : Form
    {
        const int ExtraWaitSeconds = 120;   // höchstens Pause + 2 Minuten warten, dann trotzdem senden

        readonly Settings settings = Settings.Load();
        readonly ListView lvTargets = new ListView {
            View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, MultiSelect = false,
            Dock = DockStyle.Fill, Margin = new Padding(3, 3, 3, 8) };
        readonly ComboBox cbRemote = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        readonly NumericUpDown numMinutes = new NumericUpDown { Minimum = 1, Maximum = 240, Width = 70 };
        readonly NumericUpDown numPause = new NumericUpDown { Minimum = 1, Maximum = 300, Width = 70 };
        readonly ComboBox cbMethod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly CheckBox chkAuto = new CheckBox { Text = "Beim Programmstart sofort aktiv (startet unsichtbar im Infobereich)", AutoSize = true };
        readonly Button btnStart = new Button { Text = "Starten", AutoSize = true };
        readonly Button btnTest = new Button { Text = "Jetzt einmal senden", AutoSize = true };
        readonly Button btnRefresh = new Button { Text = "Offene Fenster laden", AutoSize = true };
        readonly Button btnAdd = new Button { Text = "Hinzufügen", AutoSize = true, Margin = new Padding(12, 3, 3, 3) };
        readonly Button btnRemove = new Button { Text = "Entfernen", AutoSize = true };
        readonly Label lblStatus = new Label { AutoSize = true, Text = "Gestoppt.", Margin = new Padding(3, 8, 3, 3) };
        readonly Label lblMethodInfo = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 8) };
        readonly Label lblHint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 8),
            Text = "Servername oder Teil des Fenstertitels, z. B. EDU3. Jede Verbindung hat ihr eigenes Intervall; " +
                   "das Häkchen in der Liste schaltet sie ein oder aus. Eine Zeile anklicken, um sie zu ändern." };
        TableLayoutPanel grid;

        static readonly string[] MethodInfo = {
            "Wartet, bis du lokal die eingestellte Pause lang weder tippst noch die Maus bewegst. Holt dann jedes fällige Remote-Fenster " +
            "für etwa 0,2 Sekunden nach vorn, drückt einmal die Umschalt-Taste und gibt den Fokus sofort an dein vorheriges " +
            "Fenster zurück. Minimierte Fenster werden dafür unsichtbar wiederhergestellt und gleich wieder minimiert. Zuverlässig; in der Pause kann es kurz flackern. Kommt keine Pause zustande, wird spätestens 2 Minuten nach der Wartezeit trotzdem gesendet. Arbeitest du selbst im Remote-Fenster, entfällt der Impuls.",
            "Schickt die Umschalt-Taste direkt an das Remote-Fenster, ohne es nach vorn zu holen. Stört nie – kommt beim " +
            "Windows-Remotedesktop aber meist nicht an (im Test ohne Wirkung).",
            "Wie die Hintergrund-Taste, aber als Mausbewegung um 1 Pixel in der Fenstermitte. Meist ebenfalls ohne Wirkung; " +
            "falls doch, springt der Mauszeiger in der Remote-Sitzung kurz in die Mitte." };
        readonly System.Windows.Forms.Timer scheduler = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem miToggle = new ToolStripMenuItem("Starten");
        bool allowShow, exiting, running, manualPending, loadingList, listReady;
        DateTime? waitDeadline;
        IntPtr currentIcon = IntPtr.Zero;

        public MainForm()
        {
            Text = "RDP-Wachhalter 2.2";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f);
            Padding = new Padding(12);

            float scale;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            MinimumSize = new Size((int)(620 * scale), (int)(600 * scale));
            Size = new Size(Math.Max(MinimumSize.Width, settings.Width > 0 ? settings.Width : (int)(840 * scale)),
                            Math.Max(MinimumSize.Height, settings.Height > 0 ? settings.Height : (int)(660 * scale)));

            cbMethod.Items.AddRange(new object[] {
                "Kurz aktivieren in Eingabepausen (empfohlen)",
                "Nur im Hintergrund: Umschalt-Taste",
                "Nur im Hintergrund: Mausbewegung" });

            lvTargets.Columns.Add("Verbindung", 200);
            lvTargets.Columns.Add("Alle", (int)(70 * scale));
            lvTargets.Columns.Add("Letzter Impuls", (int)(200 * scale));
            lvTargets.Columns.Add("Nächster", (int)(80 * scale));

            grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 10, Dock = DockStyle.Fill };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int r = 1; r < 10; r++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            grid.Controls.Add(Lbl("Verbindungen:"), 0, 0);
            grid.Controls.Add(lvTargets, 1, 0);
            grid.SetColumnSpan(lvTargets, 2);

            grid.Controls.Add(Lbl("Server:"), 0, 1);
            grid.Controls.Add(cbRemote, 1, 1);
            grid.Controls.Add(btnRefresh, 2, 1);

            grid.Controls.Add(Lbl("Alle:"), 0, 2);
            var editRow = Row(numMinutes, "Minuten");
            editRow.Controls.Add(btnAdd);
            editRow.Controls.Add(btnRemove);
            grid.Controls.Add(editRow, 1, 2);
            grid.SetColumnSpan(editRow, 2);
            grid.Controls.Add(lblHint, 1, 3);
            grid.SetColumnSpan(lblHint, 2);

            grid.Controls.Add(Lbl("Methode:"), 0, 4);
            grid.Controls.Add(cbMethod, 1, 4);
            grid.SetColumnSpan(cbMethod, 2);
            grid.Controls.Add(lblMethodInfo, 1, 5);
            grid.SetColumnSpan(lblMethodInfo, 2);

            grid.Controls.Add(Lbl("Pause:"), 0, 6);
            var pauseRow = Row(numPause, "Sekunden ohne lokale Eingabe abwarten (höchstens 2 Minuten länger, dann trotzdem senden)");
            grid.Controls.Add(pauseRow, 1, 6);
            grid.SetColumnSpan(pauseRow, 2);

            grid.Controls.Add(chkAuto, 0, 7);
            grid.SetColumnSpan(chkAuto, 3);

            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(btnStart);
            buttons.Controls.Add(btnTest);
            grid.Controls.Add(buttons, 0, 8);
            grid.SetColumnSpan(buttons, 3);

            grid.Controls.Add(lblStatus, 0, 9);
            grid.SetColumnSpan(lblStatus, 3);
            Controls.Add(grid);
            grid.Resize += delegate { WrapLabels(); };
            lvTargets.Resize += delegate { FitColumns(); };
            ResizeEnd += delegate { SaveSettings(); };

            numMinutes.Value = Math.Max(1, Math.Min(240, settings.Minutes));
            numPause.Value = Math.Max(1, Math.Min(300, settings.PauseSeconds));
            cbMethod.SelectedIndex = settings.Method >= 0 && settings.Method <= 2 ? settings.Method : 0;
            chkAuto.Checked = settings.AutoStart;
            FillRemoteList();
            RebuildList();

            btnRefresh.Click += delegate { FillRemoteList(); cbRemote.DroppedDown = cbRemote.Items.Count > 0; };
            btnAdd.Click += delegate { AddOrUpdate(); };
            btnRemove.Click += delegate { RemoveSelected(); };
            btnStart.Click += delegate { Toggle(); };
            btnTest.Click += delegate { SendNow(); };
            cbRemote.SelectedIndexChanged += delegate
            {
                if (cbRemote.SelectedIndex < 0) return;
                string shortName = Settings.ShortName((string)cbRemote.SelectedItem);
                BeginInvoke((MethodInvoker)delegate { cbRemote.Text = shortName; });
            };
            cbRemote.TextChanged += delegate { UpdateAddButton(); };
            cbRemote.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddOrUpdate(); e.SuppressKeyPress = true; } };
            lvTargets.SelectedIndexChanged += delegate { SelectionChanged(); };
            lvTargets.ItemChecked += (s, e) => ItemChecked(e.Item);
            // Beim Erzeugen des Listenfensters meldet Windows Häkchen-Zwischenstände; erst danach auswerten
            // und die Häkchen aus den Einstellungen neu setzen.
            lvTargets.HandleCreated += delegate
            {
                listReady = false;
                BeginInvoke((MethodInvoker)delegate
                {
                    loadingList = true;
                    foreach (ListViewItem item in lvTargets.Items)
                    {
                        var t = item.Tag as Target;
                        if (t != null) item.Checked = t.Enabled;
                    }
                    loadingList = false;
                    listReady = true;
                    RefreshList();
                });
            };
            numPause.ValueChanged += delegate { SaveSettings(); };
            cbMethod.SelectedIndexChanged += delegate { MethodChanged(); SaveSettings(); };
            chkAuto.CheckedChanged += delegate { SaveSettings(); };
            scheduler.Tick += delegate { Tick(); };
            MethodChanged();
            UpdateAddButton();
            WrapLabels();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Öffnen", null, delegate { ShowWindow(); });
            menu.Items.Add(miToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Beenden", null, delegate { exiting = true; Close(); });
            miToggle.Click += delegate { Toggle(); };
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowWindow(); };
            tray.Visible = true;
            UpdateTray();

            scheduler.Start();
            allowShow = !(settings.AutoStart && settings.Targets.Count > 0);
            if (settings.AutoStart) StartRunning();
        }

        static Label Lbl(string text) { return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 6, 6, 3) }; }
        static FlowLayoutPanel Row(Control c, string suffix)
        {
            var p = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
            p.Controls.Add(c);
            p.Controls.Add(new Label { Text = suffix, AutoSize = true, Margin = new Padding(4, 6, 0, 0) });
            return p;
        }

        void MethodChanged()
        {
            int i = cbMethod.SelectedIndex;
            lblMethodInfo.Text = i >= 0 ? MethodInfo[i] : "";
            numPause.Enabled = i == 0;
        }

        // Mehrzeilige Texte an die aktuelle Fensterbreite anpassen.
        void WrapLabels()
        {
            if (grid == null) return;
            int full = Math.Max(200, grid.ClientSize.Width - 12);
            int labelCol = grid.GetColumnWidths().Length > 0 ? grid.GetColumnWidths()[0] : 0;
            lblStatus.MaximumSize = new Size(full, 0);
            lblHint.MaximumSize = new Size(Math.Max(150, full - labelCol), 0);
            lblMethodInfo.MaximumSize = new Size(Math.Max(150, full - labelCol), 0);
        }

        void FitColumns()
        {
            int others = lvTargets.Columns[1].Width + lvTargets.Columns[2].Width + lvTargets.Columns[3].Width;
            lvTargets.Columns[0].Width = Math.Max(120, lvTargets.ClientSize.Width - others - 4);
        }

        // Fenster nie von selbst zeigen oder aktivieren — nur auf Wunsch (Doppelklick/Öffnen).
        protected override void SetVisibleCore(bool value)
        {
            if (!allowShow) { value = false; if (!IsHandleCreated) CreateHandle(); }
            base.SetVisibleCore(value);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && running && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;   // läuft im Infobereich weiter
                Hide();
                return;
            }
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        void ShowWindow()
        {
            allowShow = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void FillRemoteList()
        {
            string current = cbRemote.Text;
            cbRemote.Items.Clear();
            foreach (var w in Native.FindRdpWindows()) cbRemote.Items.Add(w.Title);
            cbRemote.Text = current;
        }

        void SaveSettings()
        {
            settings.Minutes = (int)numMinutes.Value;
            settings.PauseSeconds = (int)numPause.Value;
            settings.Method = cbMethod.SelectedIndex;
            settings.AutoStart = chkAuto.Checked;
            if (WindowState == FormWindowState.Normal && Visible) { settings.Width = Width; settings.Height = Height; }
            settings.Save();
        }

        // ---------- Liste der Verbindungen ----------

        Target FindTarget(string name)
        {
            foreach (var t in settings.Targets)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        Target SelectedTarget
        {
            get { return lvTargets.SelectedItems.Count > 0 ? (Target)lvTargets.SelectedItems[0].Tag : null; }
        }

        void UpdateAddButton()
        {
            string name = cbRemote.Text.Trim();
            btnAdd.Text = FindTarget(name) != null ? "Ändern" : "Hinzufügen";
            btnAdd.Enabled = name.Length > 0;
            btnRemove.Enabled = SelectedTarget != null;
        }

        void SelectionChanged()
        {
            var t = SelectedTarget;
            if (t != null)
            {
                cbRemote.Text = t.Name;
                numMinutes.Value = t.Minutes;
            }
            UpdateAddButton();
        }

        void AddOrUpdate()
        {
            string name = cbRemote.Text.Trim();
            if (name.Length == 0) return;
            var t = FindTarget(name);
            if (t == null)
            {
                t = new Target { Name = name };
                settings.Targets.Add(t);
            }
            t.Minutes = (int)numMinutes.Value;
            if (running && t.Enabled) t.NextDue = DateTime.Now;
            SaveSettings();
            RebuildList();
            foreach (ListViewItem item in lvTargets.Items)
                if (item.Tag == t) { item.Selected = true; item.EnsureVisible(); }
            UpdateAddButton();
        }

        void RemoveSelected()
        {
            var t = SelectedTarget;
            if (t == null) return;
            settings.Targets.Remove(t);
            SaveSettings();
            RebuildList();
            cbRemote.Text = "";
            UpdateAddButton();
            UpdateTray();
        }

        void ItemChecked(ListViewItem item)
        {
            if (!listReady || loadingList || item.Tag == null) return;
            var t = (Target)item.Tag;
            if (t.Enabled == item.Checked) return;
            t.Enabled = item.Checked;
            t.NextDue = running && t.Enabled ? DateTime.Now : DateTime.MaxValue;
            SaveSettings();
            RefreshList();
            UpdateTray();
        }

        void RebuildList()
        {
            loadingList = true;
            lvTargets.BeginUpdate();
            lvTargets.Items.Clear();
            foreach (var t in settings.Targets)
            {
                var item = new ListViewItem(new[] { t.Name, "", "", "" }) { Tag = t, Checked = t.Enabled };
                lvTargets.Items.Add(item);
            }
            lvTargets.EndUpdate();
            loadingList = false;
            RefreshList();
            FitColumns();
        }

        void RefreshList()
        {
            foreach (ListViewItem item in lvTargets.Items)
            {
                var t = item.Tag as Target;
                if (t == null || item.SubItems.Count < 4) continue;
                item.SubItems[1].Text = t.Minutes + " Min.";
                item.SubItems[2].Text = t.LastActivity > t.LastPulse ? t.LastActivity.ToString("HH:mm:ss") + " – du warst aktiv" : t.LastResult;
                item.SubItems[3].Text = running && t.Enabled && t.NextDue != DateTime.MaxValue ? t.NextDue.ToString("HH:mm") : "";
                item.ForeColor = !t.Enabled ? SystemColors.GrayText : (t.LastOk ? SystemColors.WindowText : Color.DarkOrange);
            }
        }

        // ---------- Zeitplan ----------

        void Toggle() { if (running) StopRunning(); else StartRunning(); }

        void StartRunning()
        {
            bool any = false;
            foreach (var t in settings.Targets) if (t.Enabled) any = true;
            if (!any)
            {
                lblStatus.Text = "Bitte zuerst eine Verbindung hinzufügen und mit Häkchen einschalten.";
                return;
            }
            running = true;
            foreach (var t in settings.Targets) t.NextDue = t.Enabled ? DateTime.Now : DateTime.MaxValue;
            btnStart.Text = "Stoppen";
            miToggle.Text = "Stoppen";
            UpdateTray();
            Tick();
        }

        void StopRunning()
        {
            running = false;
            waitDeadline = null;
            foreach (var t in settings.Targets) t.NextDue = DateTime.MaxValue;
            btnStart.Text = "Starten";
            miToggle.Text = "Starten";
            lblStatus.Text = "Gestoppt.";
            RefreshList();
            UpdateTray();
        }

        void SendNow()
        {
            manualPending = true;
            Tick();
        }

        // Wer gerade selbst im Remote-Fenster arbeitet, hält die Sitzung schon wach: Zeitplan neu beginnen.
        void TrackActivity()
        {
            if (!running || Native.LocalIdleMs() > 1500) return;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            string title = Native.TextOf(fg);
            if (!Native.IsRdpWindow(fg)) return;
            DateTime now = DateTime.Now;
            bool changed = false;
            foreach (var t in settings.Targets)
            {
                if (!t.Enabled || title.IndexOf(t.Name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool newSecond = (now - t.LastActivity).TotalSeconds >= 1;
                t.LastActivity = now;
                t.NextDue = now.AddMinutes(t.Minutes);
                t.LastOk = true;
                changed |= newSecond;
            }
            if (changed) { RefreshList(); UpdateTray(); }
        }

        List<Target> DueTargets()
        {
            var due = new List<Target>();
            DateTime now = DateTime.Now;
            foreach (var t in settings.Targets)
                if (t.Enabled && (manualPending || (running && t.NextDue <= now))) due.Add(t);
            return due;
        }

        void Tick()
        {
            TrackActivity();
            var due = DueTargets();
            if (due.Count == 0)
            {
                manualPending = false;
                waitDeadline = null;
                return;
            }
            if (cbMethod.SelectedIndex == 0)
            {
                if (waitDeadline == null) waitDeadline = DateTime.Now.AddSeconds((int)numPause.Value + ExtraWaitSeconds);
                if (Native.LocalIdleMs() < (uint)numPause.Value * 1000 && DateTime.Now < waitDeadline.Value)
                {
                    lblStatus.Text = DateTime.Now.ToString("HH:mm:ss") + " – " + due.Count + (due.Count == 1 ? " Verbindung" : " Verbindungen") +
                                     " fällig. Warte auf eine Eingabepause von " + numPause.Value + " s (spätestens " +
                                     waitDeadline.Value.ToString("HH:mm:ss") + ").";
                    return;
                }
            }
            waitDeadline = null;
            manualPending = false;
            Fire(due);
        }

        void Fire(List<Target> due)
        {
            DateTime now = DateTime.Now;
            string time = now.ToString("HH:mm:ss");
            var windows = Native.FindRdpWindows();
            var done = new HashSet<IntPtr>();
            int ok = 0;
            var notFound = new List<string>();
            var problems = new List<string>();

            foreach (var t in due)
            {
                string problem = null;
                int hits = 0;
                foreach (var w in windows)
                {
                    if (w.Title.IndexOf(t.Name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    hits++;
                    if (done.Contains(w.Top)) continue;   // gleiches Fenster passt zu mehreren Einträgen
                    done.Add(w.Top);
                    string p = Send(w);
                    if (p != null) problem = p;
                }
                if (hits == 0)
                {
                    t.LastOk = false;
                    t.LastResult = time + " – Fenster nicht offen";
                    notFound.Add(t.Name);
                }
                else if (problem != null)
                {
                    t.LastOk = false;
                    t.LastResult = time + " – " + problem;
                    problems.Add(t.Name + ": " + problem);
                }
                else
                {
                    t.LastOk = true;
                    t.LastResult = time + " – gesendet";
                    t.LastPulse = now;
                    ok++;
                }
                if (running) t.NextDue = now.AddMinutes(t.Minutes);
            }

            string text = time + " – " + ok + " von " + due.Count + (due.Count == 1 ? " Verbindung" : " Verbindungen") + " angestoßen.";
            if (notFound.Count > 0) text += " Nicht offen: " + string.Join(", ", notFound.ToArray()) + ".";
            if (problems.Count > 0) text += " Problem – " + string.Join("; ", problems.ToArray()) + ".";
            lblStatus.Text = text;
            RefreshList();
            UpdateTray();
        }

        string Send(Native.RdpWindow w)
        {
            if (cbMethod.SelectedIndex == 0) return Native.PulseWithFocus(w);
            if (w.Input == IntPtr.Zero) return "kein RDP-Eingabefenster für den Hintergrund-Modus";
            if (cbMethod.SelectedIndex == 2) Native.PostMouse(w.Input); else Native.PostShift(w.Input);
            return null;
        }

        void UpdateTray()
        {
            if (!running) { SetIcon(Color.Gray, "RDP-Wachhalter – gestoppt"); return; }
            int active = 0, bad = 0;
            foreach (var t in settings.Targets)
                if (t.Enabled) { active++; if (!t.LastOk) bad++; }
            if (bad > 0) SetIcon(Color.DarkOrange, "RDP-Wachhalter – " + bad + " von " + active + " mit Problem");
            else SetIcon(Color.SeaGreen, "RDP-Wachhalter – aktiv, " + active + (active == 1 ? " Verbindung" : " Verbindungen"));
        }

        IntPtr currentFormIcon = IntPtr.Zero;

        void SetIcon(Color color, string tip)
        {
            using (var bmp = AppIcon.Render(SystemInformation.SmallIconSize.Width, color))
            {
                IntPtr h = bmp.GetHicon();
                tray.Icon = Icon.FromHandle(h);
                if (currentIcon != IntPtr.Zero) Native.DestroyIcon(currentIcon);
                currentIcon = h;
            }
            using (var bmp = AppIcon.Render(SystemInformation.IconSize.Width, color))
            {
                IntPtr h = bmp.GetHicon();
                Icon = Icon.FromHandle(h);
                if (currentFormIcon != IntPtr.Zero) Native.DestroyIcon(currentFormIcon);
                currentFormIcon = h;
            }
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }
    }

    // Programmsymbol: Bildschirm mit Herzschlag-Linie, optional Statuspunkt unten rechts.
    static class AppIcon
    {
        public static Bitmap Render(int size, Color? status)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float s = size / 32f;
                g.ScaleTransform(s, s);

                // Fuß
                using (var stand = new SolidBrush(Color.FromArgb(120, 134, 148)))
                {
                    g.FillRectangle(stand, 13.5f, 23f, 5f, 4f);
                    using (var p = Rounded(8.5f, 26.5f, 15f, 3f, 1.5f)) g.FillPath(stand, p);
                }
                // Gehäuse + Bildschirm
                using (var p = Rounded(1f, 3f, 30f, 21f, 3f))
                using (var b = new SolidBrush(Color.FromArgb(31, 52, 68))) g.FillPath(b, p);
                using (var p = Rounded(3f, 5f, 26f, 16.5f, 1.5f))
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new PointF(0, 5f), new PointF(0, 21.5f), Color.FromArgb(64, 164, 236), Color.FromArgb(24, 104, 184)))
                    g.FillPath(b, p);
                // Herzschlag
                using (var pen = new Pen(Color.White, Math.Max(2.2f, 1.4f / s)))
                {
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLines(pen, new[] {
                        new PointF(5.5f, 13.5f), new PointF(10.5f, 13.5f), new PointF(12.5f, 8.5f),
                        new PointF(15.5f, 18.5f), new PointF(18f, 11f), new PointF(19.5f, 13.5f), new PointF(26.5f, 13.5f) });
                }
                // Statuspunkt
                if (status.HasValue)
                {
                    using (var ring = new SolidBrush(Color.White)) g.FillEllipse(ring, 19f, 17f, 13f, 13f);
                    using (var dot = new SolidBrush(status.Value)) g.FillEllipse(dot, 20.75f, 18.75f, 9.5f, 9.5f);
                }
            }
            return bmp;
        }

        static System.Drawing.Drawing2D.GraphicsPath Rounded(float x, float y, float w, float h, float r)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // .ico mit mehreren Größen (32-bit-DIB-Einträge) für das Programmsymbol der .exe.
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
                using (var bmp = Render(size, null)) images.Add(ToDib(bmp));

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
        }

        static byte[] ToDib(Bitmap bmp)
        {
            int n = bmp.Width;
            int maskStride = ((n + 31) / 32) * 4;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(40); w.Write(n); w.Write(n * 2); w.Write((short)1); w.Write((short)32);
                w.Write(0); w.Write(n * n * 4 + maskStride * n); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                for (int y = n - 1; y >= 0; y--)
                    for (int x = 0; x < n; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                    }
                w.Write(new byte[maskStride * n]);
                return ms.ToArray();
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--export-icon") { AppIcon.WriteIco(args[1]); return; }
            if (args.Length == 2 && args[0] == "--export-png")
            {
                using (var bmp = AppIcon.Render(256, Color.SeaGreen)) bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "RdpWachhalter_SingleInstance", out created))
            {
                if (!created) { MessageBox.Show("Der RDP-Wachhalter läuft bereits (Symbol im Infobereich neben der Uhr).", "RDP-Wachhalter"); return; }
                try { Native.SetProcessDPIAware(); } catch { }
                Application.ThreadException += (s, e) =>
                {
                    // Fehler protokollieren statt Absturzdialog; die App läuft weiter.
                    try
                    {
                        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpWachhalter");
                        Directory.CreateDirectory(dir);
                        File.AppendAllText(Path.Combine(dir, "fehler.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + e.Exception + Environment.NewLine);
                    }
                    catch { }
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}

// ERESM-RDP-Manager — Hauptfenster: Verbindungsliste links, Sitzungen als Tabs bzw. im Raster rechts.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RdpManager
{
    class MainForm : Form
    {
        public const string AppName = "ERESM-RDP-Manager";
        public const string Version = "1.9";

        readonly Store store = Store.Load();
        readonly SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        readonly TextBox tbSearch = new TextBox { Dock = DockStyle.Top };
        readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, FullRowSelect = false, ItemHeight = 22, BorderStyle = BorderStyle.None };
        readonly TabStrip tabs = new TabStrip();
        readonly List<SessionWindow> windows = new List<SessionWindow>();
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        readonly Panel layoutRoot = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };   // Raster: Zellen und Teilungen
        readonly Panel stash = new Panel { Visible = false };                                         // offene, gerade nicht angezeigte Sitzungen
        readonly Label emptyText = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Tag = "muted" };
        readonly Panel emptyState = new Panel { Dock = DockStyle.Fill };
        readonly ToolStrip toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        readonly StatusStrip statusBar = new StatusStrip();
        readonly ToolStripStatusLabel status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly ImageList images = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        readonly Timer keepAliveTimer = new Timer { Interval = 5000 };
        readonly Timer focusTimer = new Timer { Interval = 300 };
        readonly ContextMenuStrip treeMenu = new ContextMenuStrip();
        readonly ContextMenuStrip tabMenu = new ContextMenuStrip();
        readonly float scale;
        readonly Panel sidebar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 0, 6) };
        readonly Panel sidebarGap = new Panel { Dock = DockStyle.Top, Height = 6 };
        ToolStripButton btnConnect, btnDisconnect, btnFullScreen, btnEdit, btnDelete;
        Pane activePane;
        bool syncingSelection;

        // Ziehen in das Raster (Tab oder Verbindung aus der Liste)
        Timer dragTimer;
        DropOverlay overlay;
        SessionPage dragPage;
        Connection dragConn;

        const int ImgGroup = 0, ImgIdle = 1, ImgConnected = 2, ImgBusy = 3;

        readonly string[] openOnStart;

        public MainForm(string[] openOnStart)
        {
            this.openOnStart = openOnStart;
            L.Lang = L.Resolve(store.Language);
            Text = AppName + " " + Version;
            Font = new Font("Segoe UI", 9f);
            Icon = AppIcon.ToIcon(32, null);
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            MinimumSize = new Size((int)(700 * scale), (int)(450 * scale));
            Size = new Size(store.WindowWidth > 0 ? store.WindowWidth : (int)(1280 * scale), store.WindowHeight > 0 ? store.WindowHeight : (int)(800 * scale));
            if (store.WindowX >= 0 && Screen.AllScreens.Any(s => s.WorkingArea.Contains(store.WindowX + 50, store.WindowY + 20)))
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(store.WindowX, store.WindowY);
            }
            else StartPosition = FormStartPosition.CenterScreen;
            if (store.Maximized) WindowState = FormWindowState.Maximized;

            int icon = (int)(16 * scale);
            images.ImageSize = new Size(icon, icon);
            images.Images.Add(AppIcon.Folder(icon));
            images.Images.Add(AppIcon.Render(icon, null));
            images.Images.Add(AppIcon.Render(icon, Color.SeaGreen));
            images.Images.Add(AppIcon.Render(icon, Color.DarkOrange));
            tree.ImageList = images;
            tree.ItemHeight = Math.Max(22, icon + 6);

            BuildToolbar();
            BuildTreeMenu();
            BuildTabMenu();

            tbSearch.Margin = new Padding(0);
            tbSearch.HandleCreated += delegate { SetCue(); };
            sidebar.Controls.Add(tree);
            sidebar.Controls.Add(sidebarGap);
            sidebar.Controls.Add(tbSearch);
            split.Panel1.Controls.Add(sidebar);

            content.Controls.Add(layoutRoot);
            content.Controls.Add(stash);
            // Ablage immer so groß wie der Sitzungsbereich: abgelegte Sitzungen behalten ihre Größe
            layoutRoot.Resize += delegate { stash.Bounds = layoutRoot.Bounds; };
            activePane = NewPane();
            layoutRoot.Controls.Add(activePane);

            emptyState.Controls.Add(emptyText);
            split.Panel2.Controls.Add(content);    // Füllfläche zuerst, damit die Tableiste oben andockt
            split.Panel2.Controls.Add(tabs);
            split.Panel2.Controls.Add(emptyState);

            Controls.Add(split);
            Controls.Add(toolbar);
            statusBar.Items.Add(status);
            Controls.Add(statusBar);

            tree.NodeMouseDoubleClick += (s, e) => { if (e.Node.Tag is Connection) Open((Connection)e.Node.Tag); };
            tree.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && SelectedConnection != null) { Open(SelectedConnection); e.Handled = true; } else if (e.KeyCode == Keys.Delete) DeleteSelected(); };
            tree.NodeMouseClick += (s, e) => { if (e.Button == MouseButtons.Right) tree.SelectedNode = e.Node; };
            tree.AfterSelect += delegate { UpdateCommands(); };
            tree.ItemDrag += (s, e) => { var n = e.Item as TreeNode; if (n != null && n.Tag is Connection) StartDockDrag(null, (Connection)n.Tag); };
            // Gruppen über ihren Schlüssel (Node.Name, "" = ohne Gruppe) merken — unabhängig von der Sprache
            tree.AfterCollapse += (s, e) => { if (e.Node.Tag == null && !store.CollapsedGroups.Contains(e.Node.Name)) store.CollapsedGroups.Add(e.Node.Name); };
            tree.AfterExpand += (s, e) => { if (e.Node.Tag == null) store.CollapsedGroups.Remove(e.Node.Name); };
            tree.ContextMenuStrip = treeMenu;
            // Markierung in der Farbe der Darstellung statt Windows-Blau
            tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            tree.DrawNode += (s, e) =>
            {
                if ((e.State & TreeNodeStates.Selected) == 0 || e.Node.Bounds.IsEmpty) { e.DrawDefault = true; return; }
                var t = Theme.Current;
                var r = new Rectangle(e.Bounds.X, e.Bounds.Y, tree.ClientSize.Width - e.Bounds.X, e.Bounds.Height);
                using (var b = new SolidBrush(t.SidebarSelection)) e.Graphics.FillRectangle(b, r);
                TextRenderer.DrawText(e.Graphics, e.Node.Text, e.Node.NodeFont ?? tree.Font, e.Bounds, t.SidebarSelectionText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            };
            tbSearch.TextChanged += delegate { RebuildTree(); };

            tabs.SelectedChanged += delegate { OnTabSelected(); UpdateCommands(); UpdateStatus(); };
            tabs.CloseRequested += p => p.RequestClose();
            tabs.DragOut += p => StartDockDrag(p, null);
            tabs.TabMenu = tabMenu;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) => { if (store.Theme == "System" && IsHandleCreated) BeginInvoke((MethodInvoker)ApplyTheme); };

            keepAliveTimer.Tick += delegate { KeepAliveTick(); };
            keepAliveTimer.Start();
            focusTimer.Tick += delegate { FocusTick(); };
            focusTimer.Start();

            Shown += delegate
            {
                try { split.SplitterDistance = Math.Max(160, store.SidebarWidth > 0 ? store.SidebarWidth : (int)(260 * scale)); } catch { }
                // Start mit Verbindungsnamen (z. B. Desktop-Verknüpfung "ERESM-RDP-Manager.exe EDU3")
                foreach (var name in openOnStart)
                {
                    var c = store.Connections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (c != null) Open(c); else status.Text = L.F("Verbindung „{0}“ nicht gefunden.", name);
                }
            };

            ApplyTexts();
            ApplyTheme();
            RebuildTree();
            UpdatePaneHeaders();
            UpdateEmptyState();
            UpdateCommands();
            UpdateStatus();
        }

        // ---------- Aufbau ----------

        void BuildToolbar()
        {
            toolbar.Items.Clear();
            var btnNew = new ToolStripButton(L.T("Neue Verbindung"), AppIcon.Plus(16));
            btnEdit = new ToolStripButton(L.T("Bearbeiten"));
            btnDelete = new ToolStripButton(L.T("Löschen"));
            btnConnect = new ToolStripButton(L.T("Verbinden"), AppIcon.Play(16));
            btnDisconnect = new ToolStripButton(L.T("Trennen"));
            btnFullScreen = new ToolStripButton(L.T("Vollbild"));
            var btnGrid = new ToolStripButton(L.T("Raster"), AppIcon.Grid(16)) { ToolTipText = L.T("Offene Verbindungen im Raster anordnen") };
            btnGrid.Click += delegate { ShowGridDialog(); };
            var btnImport = new ToolStripButton(L.T("RDP-Datei importieren"));
            var lblVersion = new ToolStripLabel("v" + Version) { Alignment = ToolStripItemAlignment.Right, Tag = "muted" };

            var ddTheme = new ToolStripDropDownButton(L.T("Darstellung")) { Alignment = ToolStripItemAlignment.Right };
            foreach (var mode in new[] { "System", "Hell", "Dunkel", "ERESM" })
            {
                string m = mode;
                var item = new ToolStripMenuItem(L.T(m == "System" ? "Wie Windows" : m == "ERESM" ? "ERESM Design" : m)) { Tag = m };
                item.Click += delegate { store.Theme = m; store.Save(); ApplyTheme(); };
                ddTheme.DropDownItems.Add(item);
            }
            ddTheme.DropDownOpening += delegate { foreach (ToolStripMenuItem i in ddTheme.DropDownItems) i.Checked = (string)i.Tag == store.Theme; };

            // Sprache: Flagge + Landesname auf Englisch
            var current = L.Languages.First(x => x.Code == L.Lang);
            var ddLang = new ToolStripDropDownButton(current.CountryName, L.Flag(current.Code, 16)) { Alignment = ToolStripItemAlignment.Right };
            foreach (var lang in L.Languages)
            {
                var lg = lang;
                var item = new ToolStripMenuItem(lg.CountryName, L.Flag(lg.Code, 16)) { Checked = lg.Code == L.Lang };
                item.Click += delegate { if (lg.Code != L.Lang) SetLanguage(lg.Code); };
                ddLang.DropDownItems.Add(item);
            }

            var btnLog = new ToolStripButton(L.T("Protokoll")) { Alignment = ToolStripItemAlignment.Right };

            btnNew.Click += delegate { NewConnection(SelectedGroup); };
            btnEdit.Click += delegate { EditSelected(); };
            btnDelete.Click += delegate { DeleteSelected(); };
            btnConnect.Click += delegate { if (SelectedConnection != null) Open(SelectedConnection); };
            btnDisconnect.Click += delegate { var p = CurrentPage; if (p != null) p.Disconnect(); };
            btnFullScreen.Click += delegate { var p = CurrentPage; if (p != null) p.ToggleFullScreen(); };
            btnImport.Click += delegate { ImportRdpFiles(); };
            btnLog.Click += delegate { OpenLog(); };

            // Rechtsbündige Einträge stehen in umgekehrter Reihenfolge: Version ganz rechts
            toolbar.Items.AddRange(new ToolStripItem[] {
                btnNew, btnEdit, btnDelete, new ToolStripSeparator(),
                btnConnect, btnDisconnect, btnFullScreen, btnGrid, new ToolStripSeparator(),
                btnImport, lblVersion, ddLang, ddTheme, btnLog });
        }

        void BuildTreeMenu()
        {
            treeMenu.Opening += (s, e) =>
            {
                treeMenu.Items.Clear();
                var c = SelectedConnection;
                if (c != null)
                {
                    treeMenu.Items.Add(L.T("Verbinden"), null, delegate { Open(c); }).Font = new Font(Font, FontStyle.Bold);
                    if (tabs.Pages.Count > 0)
                    {
                        treeMenu.Items.Add(L.T("Rechts neben der aktiven Sitzung anzeigen"), null, delegate { OpenInGrid(c, DockZone.Right); });
                        treeMenu.Items.Add(L.T("Unter der aktiven Sitzung anzeigen"), null, delegate { OpenInGrid(c, DockZone.Bottom); });
                    }
                    treeMenu.Items.Add(L.T("Bearbeiten …"), null, delegate { EditSelected(); });
                    treeMenu.Items.Add(L.T("Duplizieren"), null, delegate { Duplicate(c); });
                    treeMenu.Items.Add(new ToolStripSeparator());
                    treeMenu.Items.Add(L.T("Löschen"), null, delegate { DeleteSelected(); });
                }
                else treeMenu.Items.Add(L.T("Neue Verbindung …"), null, delegate { NewConnection(SelectedGroup); });
                Theme.Current.ApplyMenu(treeMenu);
            };
        }

        // Tab-Menü wirkt auf den angeklickten Tab (nicht zwingend den gewählten)
        SessionPage MenuPage { get { return tabs.MenuTarget ?? CurrentPage; } }

        void BuildTabMenu()
        {
            tabMenu.Items.Clear();
            tabMenu.Items.Add(L.T("Erneut verbinden"), null, delegate { var p = MenuPage; if (p != null) p.Connect(); });
            tabMenu.Items.Add(L.T("Trennen"), null, delegate { var p = MenuPage; if (p != null) p.Disconnect(); });
            tabMenu.Items.Add(L.T("Vollbild"), null, delegate { var p = MenuPage; if (p != null) p.ToggleFullScreen(); });
            tabMenu.Items.Add(new ToolStripSeparator());
            tabMenu.Items.Add(L.T("Rechts neben der aktiven Sitzung anzeigen"), null, delegate { var p = MenuPage; if (p != null) DockPage(p, activePane, DockZone.Right); });
            tabMenu.Items.Add(L.T("Unter der aktiven Sitzung anzeigen"), null, delegate { var p = MenuPage; if (p != null) DockPage(p, activePane, DockZone.Bottom); });
            tabMenu.Items.Add(L.T("Raster anordnen …"), null, delegate { ShowGridDialog(); });
            tabMenu.Items.Add(L.T("Raster auflösen"), null, delegate { ClearGrid(); });
            tabMenu.Items.Add(L.T("In eigenem Fenster öffnen"), null, delegate { var p = MenuPage; if (p != null) Detach(p, null); });
            tabMenu.Items.Add(new ToolStripSeparator());
            tabMenu.Items.Add(L.T("Wachhalten jetzt auslösen"), null, delegate { var p = MenuPage; if (p != null && p.IsLoggedIn) { p.SendKeepAlive(); UpdateStatus(); } });
            tabMenu.Items.Add(new ToolStripSeparator());
            tabMenu.Items.Add(L.T("Tab schließen"), null, delegate { var p = MenuPage; if (p != null) p.RequestClose(); });
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        void SetCue() { if (tbSearch.IsHandleCreated) SendMessage(tbSearch.Handle, 0x1501, (IntPtr)1, L.T("Suchen …")); }

        // ---------- Sprache ----------

        void SetLanguage(string code)
        {
            store.Language = code;
            store.Save();
            L.Lang = code;
            BuildToolbar();
            BuildTabMenu();
            ApplyTexts();
            foreach (var p in AllPages) p.ApplyLanguage();
            foreach (var w in windows) w.ApplyLanguage();
            foreach (var pane in AllPanes()) pane.ApplyLanguage();
            ApplyTheme();
            RebuildTree();
            UpdateCommands();
            UpdateStatus();
        }

        void ApplyTexts()
        {
            SetCue();
            emptyText.Text = L.T("Keine Sitzung geöffnet.\n\nDoppelklick auf eine Verbindung links öffnet sie in einem neuen Tab.\n" +
                                 "Über „Neue Verbindung“ legst du eine Verbindung an; sie bleibt gespeichert.\n\n" +
                                 "Tabs oder Verbindungen in den Sitzungsbereich ziehen, um ihn aufzuteilen.");
        }

        // ---------- Verbindungsliste ----------

        Connection SelectedConnection { get { return tree.SelectedNode != null ? tree.SelectedNode.Tag as Connection : null; } }

        string SelectedGroup
        {
            get
            {
                var n = tree.SelectedNode;
                if (n == null) return "";
                if (n.Tag is Connection) return ((Connection)n.Tag).Group;
                return n.Name;
            }
        }

        IEnumerable<string> Groups
        {
            get { return store.Connections.Select(c => c.Group).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g); }
        }

        void RebuildTree(string selectId = null)
        {
            if (selectId == null && SelectedConnection != null) selectId = SelectedConnection.Id;
            string q = tbSearch.Text.Trim();
            tree.BeginUpdate();
            tree.Nodes.Clear();
            var filtered = store.Connections.Where(c => q.Length == 0 ||
                c.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.Host.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.Group.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            TreeNode toSelect = null;
            foreach (var grp in filtered.GroupBy(c => c.Group, StringComparer.OrdinalIgnoreCase)
                                        .OrderBy(g => g.Key.Length == 0 ? "￿" : g.Key))
            {
                var gn = new TreeNode(grp.Key.Length > 0 ? grp.Key : L.T("Ohne Gruppe"))
                {
                    Name = grp.Key, ImageIndex = ImgGroup, SelectedImageIndex = ImgGroup, NodeFont = new Font(Font, FontStyle.Bold)
                };
                foreach (var c in grp.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var n = new TreeNode(c.Name) { Tag = c, ToolTipText = c.HostDisplay + (c.User.Length > 0 ? " · " + c.User : "") };
                    SetNodeImage(n, c);
                    gn.Nodes.Add(n);
                    if (c.Id == selectId) toSelect = n;
                }
                tree.Nodes.Add(gn);
                if (q.Length > 0 || !store.CollapsedGroups.Contains(grp.Key)) gn.Expand();
            }
            if (store.Connections.Count == 0)
                tree.Nodes.Add(new TreeNode(L.T("Noch keine Verbindungen")) { ForeColor = SystemColors.GrayText, ImageIndex = ImgIdle, SelectedImageIndex = ImgIdle });
            tree.EndUpdate();
            tree.ShowNodeToolTips = true;
            if (toSelect != null) tree.SelectedNode = toSelect;
            UpdateCommands();
        }

        void SetNodeImage(TreeNode n, Connection c)
        {
            var p = PageFor(c);
            int img = p == null ? ImgIdle : p.IsLoggedIn ? ImgConnected : ImgBusy;
            n.ImageIndex = n.SelectedImageIndex = img;
        }

        void RefreshNodeImages()
        {
            foreach (TreeNode g in tree.Nodes)
                foreach (TreeNode n in g.Nodes)
                    if (n.Tag is Connection) SetNodeImage(n, (Connection)n.Tag);
        }

        void NewConnection(string group)
        {
            var c = new Connection { Group = group ?? "" };
            using (var d = new ConnectionDialog(c, Groups, true))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                store.Connections.Add(c);
                store.Save();
                RebuildTree(c.Id);
            }
        }

        void EditSelected()
        {
            var c = SelectedConnection;
            if (c == null) return;
            var copy = c.Clone();
            using (var d = new ConnectionDialog(copy, Groups, false))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                store.Connections[store.Connections.IndexOf(c)] = copy;
                var p = PageFor(c);
                if (p != null) { p.Text = copy.Name; tabs.Invalidate(); foreach (var pane in AllPanes()) pane.UpdateTitle(); }   // Einstellungen greifen beim nächsten Verbinden
                store.Save();
                RebuildTree(copy.Id);
            }
        }

        void Duplicate(Connection c)
        {
            var copy = c.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = L.F("{0} (Kopie)", c.Name);
            store.Connections.Add(copy);
            store.Save();
            RebuildTree(copy.Id);
        }

        void DeleteSelected()
        {
            var c = SelectedConnection;
            if (c == null) return;
            if (MessageBox.Show(this, L.F("Verbindung „{0}“ löschen?\n\nOffene Sitzungen bleiben bis zum Schließen bestehen.", c.Name),
                    L.T("Verbindung löschen"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            store.Connections.Remove(c);
            store.Save();
            RebuildTree();
        }

        // .rdp-Dateien (z. B. vom Kunden) übernehmen: Adresse, Benutzer, Gateway, Umleitungen.
        void ImportRdpFiles()
        {
            using (var d = new OpenFileDialog { Filter = L.T("Remotedesktop-Dateien (*.rdp)|*.rdp"), Multiselect = true, Title = L.T("RDP-Dateien importieren") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string lastId = null;
                int count = 0;
                foreach (var file in d.FileNames)
                {
                    try
                    {
                        var c = new Connection { Name = Path.GetFileNameWithoutExtension(file), Group = SelectedGroup };
                        foreach (var raw in File.ReadAllLines(file))
                        {
                            var parts = raw.Split(new[] { ':' }, 3);
                            if (parts.Length < 3) continue;
                            string key = parts[0].Trim().ToLowerInvariant(), val = parts[2].Trim();
                            if (key == "full address") { string h; int p; Connection.SplitHost(val, out h, out p); c.Host = h; c.Port = p; }
                            else if (key == "server port") { int p; if (int.TryParse(val, out p)) c.Port = p; }
                            else if (key == "username")
                            {
                                int bs = val.IndexOf('\\');
                                if (bs > 0) { c.Domain = val.Substring(0, bs); c.User = val.Substring(bs + 1); } else c.User = val;
                            }
                            else if (key == "domain") c.Domain = val;
                            else if (key == "gateway hostname") c.Gateway = val;
                            else if (key == "redirectclipboard") c.Clipboard = val == "1";
                            else if (key == "redirectprinters") c.Printers = val == "1";
                            else if (key == "drivestoredirect") c.Drives = val.Length > 0;
                            else if (key == "administrative session") c.Admin = val == "1";
                        }
                        if (c.Host.Length == 0) continue;
                        store.Connections.Add(c);
                        lastId = c.Id;
                        count++;
                    }
                    catch (Exception ex) { Log.Write("Import fehlgeschlagen (" + file + "): " + ex.Message); }
                }
                store.Save();
                RebuildTree(lastId);
                status.Text = L.F("{0} Verbindung(en) importiert. Passwörter werden beim ersten Verbinden abgefragt.", count);
            }
        }

        void OpenLog()
        {
            try
            {
                if (!File.Exists(Log.FilePath)) Log.Write("Protokoll angelegt");
                System.Diagnostics.Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
            }
            catch { }
        }

        // ---------- Raster ----------

        Pane NewPane()
        {
            var p = new Pane(scale);
            p.HeaderClicked += ActivatePane;
            p.CloseClicked += pane => { if (AllPanes().Count > 1) RemovePane(pane); };
            p.ApplyTheme();
            return p;
        }

        List<Pane> AllPanes()
        {
            var list = new List<Pane>();
            CollectPanes(layoutRoot, list);
            return list;
        }

        static void CollectPanes(Control c, List<Pane> list)
        {
            foreach (Control x in c.Controls)
            {
                if (x is Pane) list.Add((Pane)x);
                else if (x is SplitContainer) { CollectPanes(((SplitContainer)x).Panel1, list); CollectPanes(((SplitContainer)x).Panel2, list); }
            }
        }

        Pane PaneOf(SessionPage page) { return page == null ? null : AllPanes().FirstOrDefault(p => p.Page == page); }

        void ActivatePane(Pane pane)
        {
            if (pane == null) return;
            activePane = pane;
            UpdatePaneHeaders();
            if (pane.Page != null && tabs.Selected != pane.Page)
            {
                syncingSelection = true;
                tabs.Selected = pane.Page;
                syncingSelection = false;
            }
            UpdateCommands();
            UpdateStatus();
        }

        void UpdatePaneHeaders()
        {
            var panes = AllPanes();
            if (!panes.Contains(activePane)) activePane = panes.FirstOrDefault();
            bool multi = panes.Count > 1;
            foreach (var p in panes)
            {
                p.HeaderVisible = multi;
                p.Active = multi && p == activePane;
            }
        }

        // Tab gewählt: liegt die Sitzung schon in einer Zelle, dorthin springen — sonst in der aktiven Zelle zeigen.
        void OnTabSelected()
        {
            if (syncingSelection) return;
            var page = tabs.Selected;
            if (page == null) return;
            var pane = PaneOf(page);
            if (pane != null) ActivatePane(pane); else ShowInPane(activePane, page);
        }

        void Stash(SessionPage page)
        {
            page.Visible = false;          // erst verstecken, dann umhängen: keine Größenmeldung unterwegs
            page.Parent = stash;
            page.Dock = DockStyle.Fill;
        }

        // Sitzung aus ihrer Zelle lösen; leere Zellen verschwinden, solange mehr als eine da ist.
        void ReleaseFromPane(SessionPage page, bool keepEmptySingle)
        {
            var pane = PaneOf(page);
            if (pane == null) return;
            pane.TakePage();
            Stash(page);
            if (AllPanes().Count > 1) RemovePane(pane);
            else if (!keepEmptySingle) pane.UpdateTitle();
        }

        void ShowInPane(Pane pane, SessionPage page)
        {
            if (pane == null) pane = activePane ?? AllPanes().First();
            if (pane.Page == page) { ActivatePane(pane); return; }
            if (PaneOf(page) != null) ReleaseFromPane(page, true);
            if (!AllPanes().Contains(pane)) pane = activePane;
            var prev = pane.TakePage();
            if (prev != null) Stash(prev);
            pane.SetPage(page);
            ActivatePane(pane);
        }

        Pane SplitPane(Pane target, DockZone zone, double firstShare = 0.5)
        {
            var parent = target.Parent;
            var sc = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = zone == DockZone.Left || zone == DockZone.Right ? Orientation.Vertical : Orientation.Horizontal,
                SplitterWidth = Math.Max(4, (int)(5 * scale)),
                BackColor = Theme.Current.TabStrip
            };
            sc.Panel1.BackColor = sc.Panel2.BackColor = Color.Black;
            parent.Controls.Add(sc);
            var np = NewPane();
            bool newFirst = zone == DockZone.Left || zone == DockZone.Top;
            // Zelle als Ganzes umhängen — die Sitzung darin bleibt verbunden
            target.Parent = newFirst ? sc.Panel2 : sc.Panel1;
            np.Parent = newFirst ? sc.Panel1 : sc.Panel2;
            try { sc.SplitterDistance = (int)((sc.Orientation == Orientation.Vertical ? sc.Width : sc.Height) * firstShare); } catch { }
            return np;
        }

        // Zelle in count gleich große Teile zerlegen (Right = nebeneinander, Bottom = untereinander)
        List<Pane> SplitEven(Pane target, int count, DockZone zone)
        {
            var result = new List<Pane> { target };
            var rest = target;
            for (int i = count; i > 1; i--)
            {
                rest = SplitPane(rest, zone, 1.0 / i);
                result.Add(rest);
            }
            return result;
        }

        void ShowGridDialog()
        {
            if (tabs.Pages.Count == 0) return;
            using (var d = new GridDialog(tabs.Pages.ToList()))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ApplyGrid(d.Rows, d.Cells);
            }
        }

        // Raster neu aufbauen: Zeilen untereinander, je Zeile Felder nebeneinander; Sitzungen zeilenweise einsetzen.
        void ApplyGrid(int[] rows, SessionPage[] cells)
        {
            ClearGrid();
            var root = AllPanes().First();
            var old = root.TakePage();
            if (old != null) Stash(old);
            var panes = new List<Pane>();
            foreach (var rowPane in SplitEven(root, rows.Length, DockZone.Bottom).Select((p, i) => new { p, i }).ToList())
                panes.AddRange(SplitEven(rowPane.p, rows[rowPane.i], DockZone.Right));
            for (int i = 0; i < panes.Count && i < cells.Length; i++)
                if (cells[i] != null) panes[i].SetPage(cells[i]);
            ActivatePane(panes.FirstOrDefault(p => p.Page != null) ?? panes[0]);
            Log.Write("Raster angeordnet: " + string.Join("/", rows.Select(r => r.ToString()).ToArray()) + ", " + cells.Count(x => x != null) + " Sitzung(en)");
        }

        void RemovePane(Pane pane)
        {
            var panel = pane.Parent as SplitterPanel;
            if (panel == null) return;   // einzige Zelle bleibt
            var sc = (SplitContainer)panel.Parent;
            var otherPanel = panel == sc.Panel1 ? sc.Panel2 : sc.Panel1;
            var other = otherPanel.Controls.Count > 0 ? otherPanel.Controls[0] : null;
            var grand = sc.Parent;
            var page = pane.TakePage();
            if (page != null) Stash(page);
            if (other != null) { other.Parent = grand; other.Dock = DockStyle.Fill; }
            grand.Controls.Remove(sc);
            sc.Dispose();
            if (activePane == pane || !AllPanes().Contains(activePane))
            {
                var list = new List<Pane>();
                if (other is Pane) list.Add((Pane)other); else if (other != null) CollectPanes(other, list);
                activePane = list.FirstOrDefault() ?? AllPanes().FirstOrDefault();
            }
            ActivatePane(activePane);
        }

        // Sitzung an einer Seite der Zielzelle andocken (teilt sie) oder in der Mitte ablegen (ersetzt).
        void DockPage(SessionPage page, Pane target, DockZone zone)
        {
            if (page == null || target == null || !AllPanes().Contains(target)) return;
            if (zone == DockZone.Center || target.Page == null) { ShowInPane(target, page); return; }
            if (target.Page == page) return;          // eigene Zelle nicht mit sich selbst teilen
            if (PaneOf(page) != null) ReleaseFromPane(page, true);
            if (!AllPanes().Contains(target)) target = activePane;
            var np = SplitPane(target, zone);
            np.SetPage(page);
            ActivatePane(np);
            Log.Write(page.Conn.Name + ": im Raster angeordnet (" + zone + ")");
        }

        void ClearGrid()
        {
            var keep = activePane;
            foreach (var p in AllPanes().Where(x => x != keep).ToList()) RemovePane(p);
            ActivatePane(keep);
        }

        // ---------- Ziehen ins Raster ----------

        void StartDockDrag(SessionPage page, Connection conn)
        {
            if (dragTimer != null) return;
            dragPage = page;
            dragConn = conn;
            overlay = new DropOverlay();
            dragTimer = new Timer { Interval = 30 };
            dragTimer.Tick += delegate { DragTick(); };
            dragTimer.Start();
        }

        DockZone HitTest(Point pt, out Pane pane, out Rectangle zoneRect)
        {
            pane = null;
            zoneRect = Rectangle.Empty;
            if (tabs.Visible && tabs.RectangleToScreen(tabs.ClientRectangle).Contains(pt)) return DockZone.TabStrip;
            if (emptyState.Visible && emptyState.RectangleToScreen(emptyState.ClientRectangle).Contains(pt))
            {
                pane = activePane;
                zoneRect = emptyState.RectangleToScreen(emptyState.ClientRectangle);
                return DockZone.Center;
            }
            foreach (var p in AllPanes())
            {
                if (!p.Visible) continue;
                var z = p.ZoneAt(pt, out zoneRect);
                if (z != DockZone.None) { pane = p; return z; }
            }
            if (!Bounds.Contains(pt)) return DockZone.Window;
            return DockZone.None;
        }

        void DragTick()
        {
            if (Input.EscapePressed) { EndDrag(); return; }
            Point pt = Control.MousePosition;
            Pane pane;
            Rectangle zr;
            var zone = HitTest(pt, out pane, out zr);
            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                var page = dragPage;
                var conn = dragConn;
                EndDrag();
                Drop(page, conn, zone, pane, pt);
                return;
            }
            bool showable = pane != null && zone != DockZone.None && zone != DockZone.TabStrip && zone != DockZone.Window &&
                            !(dragPage != null && pane.Page == dragPage && zone != DockZone.Center);
            if (showable) overlay.ShowAt(zr); else if (overlay.Visible) overlay.Hide();
        }

        void EndDrag()
        {
            if (dragTimer != null) { dragTimer.Stop(); dragTimer.Dispose(); dragTimer = null; }
            if (overlay != null) { overlay.Close(); overlay.Dispose(); overlay = null; }
            dragPage = null;
            dragConn = null;
        }

        void Drop(SessionPage page, Connection conn, DockZone zone, Pane pane, Point pt)
        {
            if (conn != null)
            {
                if (zone == DockZone.TabStrip) { Open(conn); return; }
                if (pane == null) return;
                Open(conn, p => DockPage(p, pane, zone));
                return;
            }
            if (page == null) return;
            if (zone == DockZone.Window) { Detach(page, pt); return; }
            if (pane != null) DockPage(page, pane, zone);
        }

        void OpenInGrid(Connection c, DockZone zone)
        {
            var target = activePane;
            Open(c, p => DockPage(p, target, zone));
        }

        // ---------- Sitzungen ----------

        SessionPage CurrentPage { get { return tabs.Selected; } }

        // Sitzung, in der gerade die Eingabe liegt (Hauptfenster-Zelle oder eigenes Fenster)
        SessionPage FocusedPage()
        {
            IntPtr h = Input.GetFocus();
            if (h == IntPtr.Zero) return null;
            foreach (var p in AllPages)
                if (p.IsHandleCreated && (p.Handle == h || Input.IsChild(p.Handle, h))) return p;
            return null;
        }

        // Klick in eine Sitzung im Raster macht deren Zelle aktiv (und wählt ihren Tab)
        void FocusTick()
        {
            if (Form.ActiveForm != this || dragTimer != null) return;
            var fp = FocusedPage();
            if (fp == null) return;
            var pane = PaneOf(fp);
            if (pane != null && pane != activePane) ActivatePane(pane);
        }

        void ApplyTheme()
        {
            var t = Theme.Current = Theme.Resolve(store.Theme);
            t.Apply(this);
            t.ApplyMenu(treeMenu);
            t.ApplyMenu(tabMenu);
            statusBar.Renderer = new ThemedRenderer(t);
            statusBar.BackColor = t.StatusBack;
            // Seitenleiste: Liste, Suchfeld und Ränder in der Farbe der Seitenleiste
            sidebar.BackColor = sidebarGap.BackColor = split.Panel1.BackColor = t.SidebarBack;
            split.BackColor = t.SidebarBack == t.Window ? t.Border : t.SidebarBack;
            tbSearch.BackColor = t.SidebarInput;
            tbSearch.ForeColor = t.SidebarText;
            foreach (TreeNode n in tree.Nodes) if (n.Tag == null && n.ForeColor != SystemColors.GrayText) n.ForeColor = t.SidebarText;
            emptyState.BackColor = t.Window;
            emptyText.ForeColor = t.Muted;
            content.BackColor = layoutRoot.BackColor = Color.Black;
            ThemeLayout(layoutRoot, t);
            foreach (var p in AllPages) p.ApplyTheme();
            foreach (var w in windows) w.ApplyTheme();
            tabs.Invalidate();
            Invalidate(true);
        }

        static void ThemeLayout(Control c, Theme t)
        {
            foreach (Control x in c.Controls)
            {
                var sc = x as SplitContainer;
                if (sc != null)
                {
                    sc.BackColor = t.TabStrip;
                    sc.Panel1.BackColor = sc.Panel2.BackColor = Color.Black;
                    ThemeLayout(sc.Panel1, t);
                    ThemeLayout(sc.Panel2, t);
                }
                else if (x is Pane) ((Pane)x).ApplyTheme();
            }
        }

        SessionPage PageFor(Connection c)
        {
            return AllPages.FirstOrDefault(p => p.Conn.Id == c.Id);
        }

        IEnumerable<SessionPage> AllPages { get { return tabs.Pages.Concat(windows.Select(w => w.Page)).ToList(); } }

        SessionWindow WindowFor(SessionPage p) { return windows.FirstOrDefault(w => w.Page == p); }

        // Vor dem Entfernen eines Tabs: Auswahl auf eine sichtbare Sitzung legen, damit keine andere Zelle ersetzt wird.
        void RemoveTab(SessionPage page)
        {
            ReleaseFromPane(page, false);
            if (tabs.Selected == page)
            {
                var next = activePane != null && activePane.Page != null && activePane.Page != page ? activePane.Page
                         : tabs.Pages.FirstOrDefault(p => p != page && PaneOf(p) == null) ?? tabs.Pages.FirstOrDefault(p => p != page);
                if (next != null) tabs.Selected = next;
            }
            tabs.Remove(page);
        }

        // Tab in ein eigenes Fenster lösen — gleiche Größe wie der Sitzungsbereich, leicht versetzt bzw. an der Ablagestelle.
        void Detach(SessionPage page, Point? at)
        {
            if (WindowFor(page) != null) return;
            Point origin = at.HasValue ? new Point(at.Value.X - 120, at.Value.Y - 15) : content.PointToScreen(new Point(40, 40));
            var bounds = new Rectangle(origin.X, origin.Y, Math.Max(640, content.Width), Math.Max(420, content.Height + 90));
            RemoveTab(page);
            var w = new SessionWindow(page, bounds);
            windows.Add(w);
            w.AttachRequested += delegate { Attach(w); };
            w.Show();
            w.Adopt();
            UpdateEmptyState();
            Log.Write(page.Conn.Name + ": in eigenes Fenster gelöst");
        }

        void Attach(SessionWindow w)
        {
            var page = w.Page;
            windows.Remove(w);
            tabs.Add(page);
            UpdateEmptyState();
            ShowInPane(activePane, page);
            w.CloseSilently();
            Activate();
        }

        SessionPage Open(Connection c) { return Open(c, null); }

        // place = wohin die Sitzung kommt (Standard: aktive Zelle). Verbunden wird erst danach, mit der richtigen Größe.
        SessionPage Open(Connection c, Action<SessionPage> place)
        {
            var existing = PageFor(c);
            if (existing != null)
            {
                var win = WindowFor(existing);
                if (win != null) { if (win.WindowState == FormWindowState.Minimized) win.WindowState = FormWindowState.Normal; win.Activate(); }
                else if (place != null) place(existing);
                else tabs.Selected = existing;
                if (existing.State == SessionState.Disconnected) existing.Connect();
                return existing;
            }

            string pw = Secret.Unprotect(c.PasswordEnc);
            if (pw.Length == 0)
            {
                using (var d = new PasswordDialog(c))
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return null;
                    pw = d.Password;
                    if (d.Save && pw.Length > 0) { c.PasswordEnc = Secret.Protect(pw); store.Save(); }
                }
            }

            var page = new SessionPage(c, pw);
            page.StateChanged += delegate
            {
                tabs.Invalidate();
                var w = WindowFor(page);
                if (w != null) w.UpdateTitle();
                RefreshNodeImages(); UpdateCommands(); UpdateStatus();
            };
            page.ApplyTheme();
            page.Closed += delegate
            {
                RemoveTab(page);
                var w = WindowFor(page);
                if (w != null) { windows.Remove(w); w.CloseSilently(); }
                UpdateEmptyState();
            };
            Stash(page);
            tabs.Add(page);
            UpdateEmptyState();
            if (place != null) place(page); else ShowInPane(activePane, page);
            page.Connect();
            RefreshNodeImages();
            return page;
        }

        void UpdateEmptyState()
        {
            bool any = tabs.Pages.Count > 0;
            tabs.Visible = any;
            content.Visible = any;
            emptyState.Visible = !any;
            UpdateCommands();
            UpdateStatus();
            RefreshNodeImages();
        }

        void UpdateCommands()
        {
            var c = SelectedConnection;
            var p = CurrentPage;
            if (btnEdit == null) return;
            btnEdit.Enabled = btnDelete.Enabled = btnConnect.Enabled = c != null;
            btnDisconnect.Enabled = p != null && p.State != SessionState.Disconnected;
            btnFullScreen.Enabled = p != null && p.IsLoggedIn;
        }

        void UpdateStatus()
        {
            var p = CurrentPage;
            status.Text = p == null ? L.F("{0} gespeicherte Verbindung(en)", store.Connections.Count) : p.StatusText(true);
        }

        // Wachhalten: fällige Sitzungen anstoßen; in der Sitzung mit der Eingabe arbeitet man gerade selbst.
        void KeepAliveTick()
        {
            DateTime now = DateTime.Now;
            bool userActive = Native.LocalIdleMs() < 5000 && Form.ActiveForm != null;
            var focused = userActive ? FocusedPage() : null;
            foreach (var p in AllPages)
            {
                if (!p.IsLoggedIn || p.Conn.KeepAliveMinutes <= 0) continue;
                if (p == focused) { p.ScheduleKeepAlive(); continue; }
                if (now >= p.NextKeepAlive) p.SendKeepAlive();
            }
            UpdateStatus();
            foreach (var w in windows) w.UpdateTitle();
        }

        // ---------- Beenden ----------

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            int open = AllPages.Count(p => p.State != SessionState.Disconnected);
            if (open > 0 && e.CloseReason == CloseReason.UserClosing &&
                MessageBox.Show(this, L.F("{0} Sitzung(en) noch verbunden. Trennen und beenden?", open),
                    L.T("ERESM-RDP-Manager beenden"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                e.Cancel = true;
                return;
            }
            EndDrag();
            store.Maximized = WindowState == FormWindowState.Maximized;
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            store.WindowX = b.X; store.WindowY = b.Y; store.WindowWidth = b.Width; store.WindowHeight = b.Height;
            store.SidebarWidth = split.SplitterDistance;
            store.Save();
            foreach (var p in AllPages) p.Disconnect();
            foreach (var w in windows.ToList()) w.CloseSilently();
            base.OnFormClosing(e);
        }
    }
}

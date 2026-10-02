// ERESM-RDP-Manager — eine Sitzung in einem eigenen Fenster (vom Tab gelöst).
using System;
using System.Drawing;
using System.Windows.Forms;

namespace RdpManager
{
    class SessionWindow : Form
    {
        public readonly SessionPage Page;
        public event EventHandler AttachRequested;
        readonly Panel host = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        readonly ToolStrip bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        readonly StatusStrip statusBar = new StatusStrip();
        readonly ToolStripStatusLabel status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        bool silent;
        readonly ToolStripButton btnBack = new ToolStripButton(), btnFull = new ToolStripButton(),
                                 btnReconnect = new ToolStripButton(), btnDisconnect = new ToolStripButton();

        public SessionWindow(SessionPage page, Rectangle bounds)
        {
            Page = page;
            Font = new Font("Segoe UI", 9f);
            Icon = AppIcon.ToIcon(32, null);
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            MinimumSize = new Size(500, 350);


            btnBack.Click += delegate { if (AttachRequested != null) AttachRequested(this, EventArgs.Empty); };
            btnFull.Click += delegate { Page.ToggleFullScreen(); };
            btnReconnect.Click += delegate { Page.Connect(); };
            btnDisconnect.Click += delegate { Page.Disconnect(); };
            bar.Items.AddRange(new ToolStripItem[] { btnBack, new ToolStripSeparator(), btnFull, btnReconnect, btnDisconnect });

            Controls.Add(host);
            Controls.Add(bar);
            statusBar.Items.Add(status);
            Controls.Add(statusBar);
            ApplyLanguage();
            ApplyTheme();
        }

        // Die Sitzung als Ganzes umhängen — das RDP-Steuerelement bleibt dabei verbunden.
        public void Adopt()
        {
            Page.Parent = host;
            Page.Dock = DockStyle.Fill;
            Page.Visible = true;
        }

        public void ApplyTheme()
        {
            var t = Theme.Current;
            t.Apply(this);
            statusBar.Renderer = new ThemedRenderer(t);
            statusBar.BackColor = t.StatusBack;
            host.BackColor = Color.Black;
            Page.ApplyTheme();
        }

        public void ApplyLanguage()
        {
            btnBack.Text = L.T("Zurück ins Hauptfenster");
            btnFull.Text = L.T("Vollbild");
            btnReconnect.Text = L.T("Erneut verbinden");
            btnDisconnect.Text = L.T("Trennen");
            Page.ApplyLanguage();
            UpdateTitle();
        }

        public void UpdateTitle()
        {
            Text = Page.Text + " – " + MainForm.AppName;
            status.Text = Page.StatusText(true);
        }

        public void CloseSilently()
        {
            silent = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (silent || e.CloseReason != CloseReason.UserClosing) { base.OnFormClosing(e); return; }
            e.Cancel = true;
            if (Page.State == SessionState.Disconnected) { Page.RequestClose(); return; }
            var answer = MessageBox.Show(this,
                L.F("Die Verbindung zu „{0}“ ist noch offen.\n\nJa: Verbindung trennen und Fenster schließen\nNein: zurück als Tab ins Hauptfenster", Page.Text),
                L.T("Fenster schließen"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) Page.RequestClose();
            else if (answer == DialogResult.No && AttachRequested != null) AttachRequested(this, EventArgs.Empty);
        }
    }
}

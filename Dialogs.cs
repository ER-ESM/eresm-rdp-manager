// ERESM-RDP-Manager — Dialoge: Verbindung anlegen/bearbeiten, Passwort abfragen.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RdpManager
{
    static class Ui
    {
        public static Label Label(string text) { return new Label { Text = L.T(text), AutoSize = true, Margin = new Padding(3, 7, 10, 3) }; }
        public static Label Hint(string text)
        {
            return new Label { Text = L.T(text), AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 8), Tag = "muted" };
        }

        public static FlowLayoutPanel Buttons(Form f, string okText)
        {
            var ok = new Button { Text = L.T(okText), DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(90, 0) };
            var cancel = new Button { Text = L.T("Abbrechen"), DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 0) };
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
            p.Controls.Add(cancel);
            p.Controls.Add(ok);
            return p;
        }

        public static void Setup(Form f, string title)
        {
            f.Text = L.T(title);
            f.Font = new Font("Segoe UI", 9f);
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.Padding = new Padding(14);
            f.Load += delegate { Theme.Current.Apply(f); };
        }
    }

    class ConnectionDialog : Form
    {
        readonly Connection conn;
        readonly TextBox tbName = new TextBox { Width = 320 };
        readonly ComboBox cbGroup = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDown };
        readonly TextBox tbHost = new TextBox { Width = 320 };
        readonly TextBox tbUser = new TextBox { Width = 320 };
        readonly TextBox tbDomain = new TextBox { Width = 320 };
        readonly TextBox tbPassword = new TextBox { Width = 320, UseSystemPasswordChar = true };
        readonly CheckBox chkSavePw = new CheckBox { Text = L.T("Passwort speichern (verschlüsselt, nur für dein Windows-Konto lesbar)"), AutoSize = true };
        readonly TextBox tbGateway = new TextBox { Width = 320 };
        readonly CheckBox chkClipboard = new CheckBox { Text = L.T("Zwischenablage teilen"), AutoSize = true };
        readonly CheckBox chkDrives = new CheckBox { Text = L.T("Lokale Laufwerke freigeben"), AutoSize = true };
        readonly CheckBox chkPrinters = new CheckBox { Text = L.T("Drucker freigeben"), AutoSize = true };
        readonly CheckBox chkAdmin = new CheckBox { Text = L.T("Administrative Sitzung (wie mstsc /admin)"), AutoSize = true };
        readonly NumericUpDown numKeepAlive = new NumericUpDown { Minimum = 0, Maximum = 240, Width = 70 };
        readonly TextBox tbNotes = new TextBox { Width = 320, Height = 50, Multiline = true, ScrollBars = ScrollBars.Vertical };
        readonly Label lblError = new Label { AutoSize = true, ForeColor = Color.Firebrick, Tag = "error" };
        bool passwordTouched;

        public ConnectionDialog(Connection c, IEnumerable<string> groups, bool isNew)
        {
            conn = c;
            Ui.Setup(this, isNew ? "Neue Verbindung" : "Verbindung bearbeiten");

            var g = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            int r = 0;
            Action<string, Control> row = (label, ctl) => { g.Controls.Add(Ui.Label(label), 0, r); g.Controls.Add(ctl, 1, r); r++; };
            Action<Control> full = ctl => { g.Controls.Add(ctl, 1, r); r++; };

            row("Server:", tbHost);
            full(Ui.Hint("Rechnername oder IP-Adresse, optional mit Port: server01:3390"));
            row("Name:", tbName);
            row("Gruppe:", cbGroup);
            full(Ui.Hint("Zum Ordnen in der Liste, z. B. Kundenname. Leer = ohne Gruppe."));
            row("Benutzer:", tbUser);
            row("Domäne:", tbDomain);
            row("Passwort:", tbPassword);
            full(chkSavePw);
            row("RD-Gateway:", tbGateway);
            full(Ui.Hint("Nur nötig, wenn der Kunde ein Remotedesktop-Gateway nutzt. Sonst leer lassen."));
            full(chkClipboard);
            full(chkDrives);
            full(chkPrinters);
            full(chkAdmin);
            var ka = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
            ka.Controls.Add(numKeepAlive);
            ka.Controls.Add(new Label { Text = L.T("Minuten (0 = aus)"), AutoSize = true, Margin = new Padding(4, 6, 0, 0) });
            row("Wachhalten alle:", ka);
            full(Ui.Hint("Drückt in diesem Abstand unsichtbar die Umschalt-Taste in der Sitzung, damit der Server nicht abmeldet."));
            row("Notizen:", tbNotes);
            full(lblError);
            Controls.Add(g);
            Controls.Add(Ui.Buttons(this, "Speichern"));

            foreach (var name in groups) cbGroup.Items.Add(name);
            tbName.Text = c.Name;
            cbGroup.Text = c.Group;
            tbHost.Text = c.Host.Length > 0 ? c.HostDisplay : "";
            tbUser.Text = c.User;
            tbDomain.Text = c.Domain;
            if (c.HasPassword) tbPassword.Text = "••••••••";   // Platzhalter; echtes Passwort bleibt verschlüsselt
            chkSavePw.Checked = c.HasPassword || isNew;
            tbGateway.Text = c.Gateway;
            chkClipboard.Checked = c.Clipboard;
            chkDrives.Checked = c.Drives;
            chkPrinters.Checked = c.Printers;
            chkAdmin.Checked = c.Admin;
            numKeepAlive.Value = Math.Max(0, Math.Min(240, c.KeepAliveMinutes));
            tbNotes.Text = c.Notes;

            tbPassword.Enter += delegate { if (!passwordTouched && conn.HasPassword) tbPassword.SelectAll(); };
            tbPassword.TextChanged += delegate { passwordTouched = true; };
            passwordTouched = false;
            // Name aus dem Server vorschlagen, solange er nicht selbst gesetzt wurde
            tbHost.TextChanged += delegate
            {
                if (isNew && (tbName.Text.Length == 0 || tbName.Tag != null))
                {
                    string h; int p;
                    Connection.SplitHost(tbHost.Text, out h, out p);
                    tbName.Text = h;
                    tbName.Tag = "auto";
                }
            };
            tbName.KeyPress += delegate { tbName.Tag = null; };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                string host; int port;
                Connection.SplitHost(tbHost.Text, out host, out port);
                if (tbName.Text.Trim().Length == 0 || host.Length == 0)
                {
                    lblError.Text = L.T("Bitte Name und Server ausfüllen.");
                    e.Cancel = true;
                    return;
                }
                conn.Name = tbName.Text.Trim();
                conn.Group = cbGroup.Text.Trim();
                conn.Host = host;
                conn.Port = port;
                conn.User = tbUser.Text.Trim();
                conn.Domain = tbDomain.Text.Trim();
                if (!chkSavePw.Checked) conn.PasswordEnc = "";
                else if (passwordTouched) conn.PasswordEnc = Secret.Protect(tbPassword.Text);
                conn.Gateway = tbGateway.Text.Trim();
                conn.Clipboard = chkClipboard.Checked;
                conn.Drives = chkDrives.Checked;
                conn.Printers = chkPrinters.Checked;
                conn.Admin = chkAdmin.Checked;
                conn.KeepAliveMinutes = (int)numKeepAlive.Value;
                conn.Notes = tbNotes.Text;
            }
            base.OnFormClosing(e);
        }

        // Ein frisch eingegebenes, aber nicht gespeichertes Passwort für genau diese Verbindung.
        public string TransientPassword
        {
            get { return !chkSavePw.Checked && passwordTouched ? tbPassword.Text : null; }
        }
    }

    class PasswordDialog : Form
    {
        readonly TextBox tbPassword = new TextBox { Width = 300, UseSystemPasswordChar = true };
        readonly CheckBox chkSave = new CheckBox { Text = L.T("Passwort speichern (verschlüsselt)"), AutoSize = true };

        public PasswordDialog(Connection c)
        {
            Ui.Setup(this, L.F("Anmelden – {0}", c.Name));
            var g = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            string who = (c.Domain.Length > 0 ? c.Domain + "\\" : "") + (c.User.Length > 0 ? c.User : L.T("(Benutzer wird beim Server abgefragt)"));
            g.Controls.Add(Ui.Label("Server:"), 0, 0); g.Controls.Add(Ui.Label(c.HostDisplay), 1, 0);
            g.Controls.Add(Ui.Label("Benutzer:"), 0, 1); g.Controls.Add(Ui.Label(who), 1, 1);
            g.Controls.Add(Ui.Label("Passwort:"), 0, 2); g.Controls.Add(tbPassword, 1, 2);
            g.Controls.Add(chkSave, 1, 3);
            Controls.Add(g);
            Controls.Add(Ui.Buttons(this, "Verbinden"));
            ActiveControl = tbPassword;
        }

        public string Password { get { return tbPassword.Text; } }
        public bool Save { get { return chkSave.Checked; } }
    }
}

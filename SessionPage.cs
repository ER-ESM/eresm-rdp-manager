// ERESM-RDP-Manager — eine Remotedesktop-Sitzung (Inhalt eines Tabs).
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace RdpManager
{
    public enum SessionState { Connecting, Connected, Disconnected }

    public class SessionPage : Panel
    {
        public readonly Connection Conn;
        public SessionState State { get; private set; }
        public DateTime NextKeepAlive { get; private set; }
        public DateTime LastKeepAlive { get; private set; }
        public string LastKeepAliveError;
        public bool CloseWhenDisconnected;
        public event EventHandler StateChanged;
        public event EventHandler Closed;

        RdpHost rdp;
        readonly TableLayoutPanel overlay = new TableLayoutPanel { Dock = DockStyle.Fill, Visible = false, ColumnCount = 1, RowCount = 4 };
        readonly Label overlayText = new Label { AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.None, Margin = new Padding(20) };
        readonly Button btnReconnect = new Button { AutoSize = true };
        readonly Button btnClose = new Button { AutoSize = true };
        string overlayKey;            // deutscher Schlüssel der Meldung, für den Sprachwechsel
        object[] overlayArgs;
        readonly Timer resizeTimer = new Timer { Interval = 600 };
        readonly Timer restoreTimer = new Timer { Interval = 700 };
        readonly string password;
        bool loggedIn, fullScreen;
        Size lastSessionSize;

        public SessionPage(Connection conn, string password)
        {
            Conn = conn;
            this.password = password;
            NextKeepAlive = DateTime.MaxValue;
            LastKeepAlive = DateTime.MinValue;
            Text = conn.Name;
            Dock = DockStyle.Fill;
            BackColor = Color.Black;

            // Meldung mittig: leere Zeile / Text / Knöpfe / leere Zeile
            overlay.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            overlay.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            overlay.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            overlay.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, WrapContents = false };
            buttons.Controls.Add(btnReconnect);
            buttons.Controls.Add(btnClose);
            overlay.Controls.Add(overlayText, 0, 1);
            overlay.Controls.Add(buttons, 0, 2);
            overlay.Resize += delegate { overlayText.MaximumSize = new Size(Math.Max(200, overlay.Width - 80), 0); };
            btnReconnect.Click += delegate { Connect(); };
            btnClose.Click += delegate { RequestClose(); };
            Controls.Add(overlay);
            ApplyLanguage();
            ApplyTheme();

            resizeTimer.Tick += delegate { resizeTimer.Stop(); ApplySessionSize(); };
            // Wieder sichtbar: nur nachziehen, falls sich die Fläche wirklich geändert hat (z. B. Rasterzelle)
            VisibleChanged += delegate { if (Visible && IsLoggedIn && !fullScreen) { resizeTimer.Stop(); resizeTimer.Start(); } };
            restoreTimer.Tick += delegate { RestoreTick(); };
        }

        public string Tooltip
        {
            get { return Conn.HostDisplay + (Conn.User.Length > 0 ? " · " + (Conn.Domain.Length > 0 ? Conn.Domain + "\\" : "") + Conn.User : ""); }
        }

        public bool IsLoggedIn { get { return State == SessionState.Connected && loggedIn; } }

        public void ApplyLanguage()
        {
            btnReconnect.Text = L.T("Erneut verbinden");
            btnClose.Text = L.T("Tab schließen");
            if (overlayKey != null) overlayText.Text = L.F(overlayKey, overlayArgs);
        }

        // Statuszeile für Hauptfenster und eigenes Fenster
        public string StatusText(bool withKeepAlive)
        {
            string s = State == SessionState.Connecting ? L.F("Verbinde mit {0} …", Conn.HostDisplay)
                     : State == SessionState.Disconnected ? L.F("Getrennt: {0}", Conn.HostDisplay)
                     : IsLoggedIn ? L.F("Verbunden mit {0}", Conn.HostDisplay) : L.F("Anmeldung an {0} …", Conn.HostDisplay);
            if (withKeepAlive && Conn.KeepAliveMinutes > 0 && IsLoggedIn)
            {
                s += "   ·   " + L.F("Wachhalten alle {0} Min.", Conn.KeepAliveMinutes);
                if (LastKeepAliveError != null) s += " – " + L.F("Fehler: {0}", LastKeepAliveError);
                else if (LastKeepAlive > DateTime.MinValue) s += ", " + L.F("zuletzt {0}", LastKeepAlive.ToString("HH:mm:ss"));
                if (NextKeepAlive != DateTime.MaxValue) s += ", " + L.F("nächstes {0}", NextKeepAlive.ToString("HH:mm"));
            }
            return s;
        }

        public void ApplyTheme()
        {
            var t = Theme.Current;
            overlay.BackColor = t.Window;
            overlayText.ForeColor = t.Text;
            t.Apply(btnReconnect);
            t.Apply(btnClose);
        }

        void SetState(SessionState s)
        {
            State = s;
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
        }

        // Neues Steuerelement je Verbindungsversuch — ein getrenntes ActiveX lässt sich nicht zuverlässig wiederverwenden.
        public void Connect()
        {
            DisposeRdp();
            overlay.Visible = false;
            loggedIn = false;
            fullScreen = false;

            rdp = new RdpHost { Dock = DockStyle.Fill };
            ((ISupportInitialize)rdp).BeginInit();
            Controls.Add(rdp);
            ((ISupportInitialize)rdp).EndInit();
            rdp.BringToFront();
            rdp.CreateControl();

            rdp.Connecting = delegate { SetState(SessionState.Connecting); };
            rdp.Connected = delegate { Log.Write(Conn.Name + ": verbunden"); SetState(SessionState.Connected); };
            rdp.LoginComplete = delegate
            {
                loggedIn = true;
                Log.Write(Conn.Name + ": angemeldet");
                ScheduleKeepAlive();
                SetState(SessionState.Connected);
            };
            rdp.Disconnected = OnDisconnected;
            rdp.FatalError = code => Log.Write(Conn.Name + ": schwerer Fehler " + code);
            rdp.LogonError = code => Log.Write(Conn.Name + ": Anmeldefehler " + code);
            rdp.EnterFullScreen = delegate { fullScreen = true; FitToMonitor(); };
            rdp.LeaveFullScreen = delegate
            {
                fullScreen = false;
                lastSessionSize = Size.Empty;
                Log.Write(Conn.Name + ": Vollbild verlassen");
                BeginInvoke((MethodInvoker)AfterLeaveFullScreen);
            };
            // Nur sichtbare Sitzungen melden eine neue Größe — sonst schickt jeder Tabwechsel zwei Auflösungswechsel an den Server
            rdp.Resize += delegate { if (loggedIn && !fullScreen && Visible) { resizeTimer.Stop(); resizeTimer.Start(); } };

            try
            {
                dynamic o = rdp.Ocx;
                o.Server = Conn.Host;
                if (Conn.User.Length > 0) o.UserName = Conn.User;
                if (Conn.Domain.Length > 0) o.Domain = Conn.Domain;
                o.ConnectingText = L.F("Verbinde mit {0} …", Conn.HostDisplay);
                o.FullScreenTitle = Conn.Name + " – " + MainForm.AppName;

                Size sz = SessionSizeFor(ClientSize);
                o.DesktopWidth = sz.Width;
                o.DesktopHeight = sz.Height;
                lastSessionSize = sz;
                o.ColorDepth = 32;

                dynamic a = o.AdvancedSettings9;
                a.RDPPort = Conn.Port;
                a.EnableCredSspSupport = true;
                a.AuthenticationLevel = 2;            // bei Zertifikatsproblemen warnen statt abbrechen
                a.EnableAutoReconnect = true;
                a.RedirectClipboard = Conn.Clipboard;
                a.RedirectDrives = Conn.Drives;
                a.RedirectPrinters = Conn.Printers;
                a.ConnectToAdministerServer = Conn.Admin;
                a.SmartSizing = false;
                a.allowBackgroundInput = 1;           // Eingaben auch ohne Fokus annehmen (Wachhalten)
                a.GrabFocusOnConnect = false;
                a.BitmapPersistence = 1;
                if (password.Length > 0) a.ClearTextPassword = password;

                if (Conn.Gateway.Trim().Length > 0)
                {
                    dynamic t = o.TransportSettings2;
                    t.GatewayHostname = Conn.Gateway.Trim();
                    t.GatewayUsageMethod = 1;         // immer über das Gateway
                    t.GatewayProfileUsageMethod = 1;  // ausdrücklich angegeben
                    t.GatewayCredsSource = 4;         // beliebige Anmeldemethode
                    t.GatewayCredSharing = 1;         // gleiche Anmeldedaten wie für den Server
                }

                SetState(SessionState.Connecting);
                Log.Write(Conn.Name + ": verbinde mit " + Conn.HostDisplay + (Conn.Gateway.Length > 0 ? " über " + Conn.Gateway : ""));
                o.Connect();
            }
            catch (Exception ex)
            {
                Log.Write(Conn.Name + ": Verbinden fehlgeschlagen: " + ex);
                ShowOverlay("Verbinden nicht möglich:\n{0}", ex.Message);
                SetState(SessionState.Disconnected);
            }
        }

        void OnDisconnected(int reason)
        {
            loggedIn = false;
            fullScreen = false;
            NextKeepAlive = DateTime.MaxValue;
            string osText;
            string key = DescribeDisconnect(reason, out osText);
            Log.Write(Conn.Name + ": getrennt (Grund " + reason + "): " + (osText ?? key));
            SetState(SessionState.Disconnected);
            if (CloseWhenDisconnected) { BeginInvoke((MethodInvoker)delegate { CloseTab(); }); return; }
            if (osText != null) ShowOverlay("{0}", osText); else ShowOverlay(key, reason);
        }

        // Liefert den deutschen Text-Schlüssel; osText = Beschreibung von Windows (in der Systemsprache)
        string DescribeDisconnect(int reason, out string osText)
        {
            osText = null;
            // 1 = vom Benutzer getrennt, 2 = Abmeldung/Trennung auf dem Server
            if (reason == 1) return "Verbindung getrennt.";
            if (reason == 2) return "Sitzung wurde auf dem Server abgemeldet oder getrennt.";
            try
            {
                dynamic o = rdp.Ocx;
                string d = o.GetErrorDescription((uint)reason, (uint)o.ExtendedDisconnectReason);
                if (!string.IsNullOrEmpty(d)) osText = d;
            }
            catch { }
            return "Verbindung beendet (Code {0}).";
        }

        void ShowOverlay(string key, params object[] args)
        {
            overlayKey = key;
            overlayArgs = args;
            overlayText.Text = L.F(key, args);
            overlay.Visible = true;
            overlay.BringToFront();
        }

        public void Disconnect()
        {
            try { if (rdp != null && State != SessionState.Disconnected) rdp.Ocx.Disconnect(); } catch { }
        }

        // Vollbild: Auflösung vorher auf den Monitor setzen, auf dem das Fenster liegt.
        public void ToggleFullScreen()
        {
            if (!IsLoggedIn) return;
            try
            {
                if ((bool)rdp.Ocx.FullScreen) { rdp.Ocx.FullScreen = false; return; }
                fullScreen = true;
                FitToMonitor();
                rdp.Ocx.FullScreen = true;
            }
            catch (Exception ex) { Log.Write(Conn.Name + ": Vollbild nicht möglich: " + ex.Message); }
        }

        void FitToMonitor()
        {
            if (rdp == null) return;
            Rectangle b = Screen.FromControl(this).Bounds;   // physische Pixel (Prozess ist DPI-bewusst)
            SetSessionSize(SessionSizeFor(b.Size));
        }

        // Tab schließen: verbundene Sitzung zuerst trennen, Aufräumen nach dem Trennen-Ereignis.
        public void RequestClose()
        {
            if (rdp != null && State != SessionState.Disconnected)
            {
                CloseWhenDisconnected = true;
                Disconnect();
                return;
            }
            CloseTab();
        }

        void CloseTab()
        {
            DisposeRdp();
            if (Closed != null) Closed(this, EventArgs.Empty);
            Dispose();
        }

        void DisposeRdp()
        {
            if (rdp == null) return;
            var r = rdp;
            rdp = null;
            r.Disconnected = null;
            try { Controls.Remove(r); r.Dispose(); } catch { }
        }

        // ---------- Größe ----------

        static Size SessionSizeFor(Size client)
        {
            int w = Math.Max(640, Math.Min(8192, client.Width)) & ~1;
            int h = Math.Max(480, Math.Min(8192, client.Height)) & ~1;
            return new Size(w, h);
        }

        void ApplySessionSize()
        {
            if (!IsLoggedIn || rdp == null || fullScreen || !Visible || rdp.ClientSize.Width < 100) return;
            SetSessionSize(SessionSizeFor(rdp.ClientSize), true);
        }

        // false = Steuerelement hat abgelehnt (z. B. mitten in einem Umschaltvorgang)
        bool SetSessionSize(Size sz, bool fallbackToScaling = false)
        {
            if (!IsLoggedIn || sz == lastSessionSize) return true;
            try
            {
                rdp.Ocx.UpdateSessionDisplaySettings((uint)sz.Width, (uint)sz.Height, (uint)sz.Width, (uint)sz.Height, 0u, 100u, 100u);
                lastSessionSize = sz;
                return true;
            }
            catch (Exception ex)
            {
                if (!fallbackToScaling) return false;
                Log.Write(Conn.Name + ": Größe anpassen nicht möglich (" + ex.Message + ") – skaliere stattdessen");
                try { rdp.Ocx.AdvancedSettings9.SmartSizing = true; } catch { }
                return false;
            }
        }

        // Nach dem Vollbild: Steuerelement erst neu einpassen und zeichnen lassen, dann Auflösung zurückstellen.
        // Direkt im Ereignis lehnt das Steuerelement die Umstellung ab, die Fläche bleibt sonst weiß.
        int restoreTries;

        void AfterLeaveFullScreen()
        {
            if (rdp == null) return;
            rdp.Dock = DockStyle.None;
            rdp.Bounds = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - 1), ClientSize.Height);
            rdp.Dock = DockStyle.Fill;
            rdp.Refresh();
            restoreTries = 0;
            restoreTimer.Start();
        }

        void RestoreTick()
        {
            if (rdp == null || fullScreen || !IsLoggedIn) { restoreTimer.Stop(); return; }
            Size sz = SessionSizeFor(rdp.ClientSize);
            restoreTries++;
            if (SetSessionSize(sz, restoreTries >= 4))
            {
                restoreTimer.Stop();
                rdp.Refresh();
                if (restoreTries > 1) Log.Write(Conn.Name + ": Größe nach Vollbild im " + restoreTries + ". Versuch zurückgestellt");
            }
            else if (restoreTries >= 4) restoreTimer.Stop();
        }

        // ---------- Wachhalten ----------

        public void ScheduleKeepAlive()
        {
            NextKeepAlive = Conn.KeepAliveMinutes > 0 && IsLoggedIn ? DateTime.Now.AddMinutes(Conn.KeepAliveMinutes) : DateTime.MaxValue;
        }

        public void SendKeepAlive()
        {
            try
            {
                rdp.SendShift();
                LastKeepAlive = DateTime.Now;
                LastKeepAliveError = null;
                Log.Write(Conn.Name + ": Wachhalte-Impuls gesendet");
            }
            catch (Exception ex)
            {
                LastKeepAliveError = ex.Message;
                Log.Write(Conn.Name + ": Wachhalten fehlgeschlagen: " + ex.Message);
            }
            ScheduleKeepAlive();
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { resizeTimer.Dispose(); restoreTimer.Dispose(); DisposeRdp(); }
            base.Dispose(disposing);
        }
    }
}

// ERESM-RDP-Manager — Sprache (Deutsch/Englisch): deutscher Text ist der Schlüssel, Englisch aus der Liste.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace RdpManager
{
    public static class L
    {
        public static string Lang = "de";

        public class Language
        {
            public string Code, CountryName;
            public Language(string code, string country) { Code = code; CountryName = country; }
        }

        // Anzeige: Flagge + Landesname auf Englisch
        public static readonly Language[] Languages = { new Language("de", "Germany"), new Language("en", "United Kingdom") };

        // Leer = beim ersten Start nach der Windows-Sprache richten
        public static string Resolve(string stored)
        {
            if (stored == "de" || stored == "en") return stored;
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
        }

        public static string T(string de)
        {
            string en;
            if (Lang == "en" && En.TryGetValue(de, out en)) return en;
            return de;
        }

        public static string F(string deFormat, params object[] args) { return string.Format(T(deFormat), args); }

        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            // Hauptfenster
            { "Suchen …", "Search …" },
            { "Keine Sitzung geöffnet.\n\nDoppelklick auf eine Verbindung links öffnet sie in einem neuen Tab.\nÜber „Neue Verbindung“ legst du eine Verbindung an; sie bleibt gespeichert.\n\nTabs lassen sich mit der Maus verschieben.",
              "No session open.\n\nDouble-click a connection on the left to open it in a new tab.\nUse “New connection” to add a connection; it will be saved.\n\nTabs can be rearranged by dragging them." },
            { "Keine Sitzung geöffnet.\n\nDoppelklick auf eine Verbindung links öffnet sie in einem neuen Tab.\nÜber „Neue Verbindung“ legst du eine Verbindung an; sie bleibt gespeichert.\n\nTabs oder Verbindungen in den Sitzungsbereich ziehen, um ihn aufzuteilen.",
              "No session open.\n\nDouble-click a connection on the left to open it in a new tab.\nUse “New connection” to add a connection; it will be saved.\n\nDrag tabs or connections into the session area to split it." },
            // Raster
            { "Rechts neben der aktiven Sitzung anzeigen", "Show to the right of the active session" },
            { "Unter der aktiven Sitzung anzeigen", "Show below the active session" },
            { "Raster auflösen", "Ungroup grid" },
            { "Raster", "Grid" },
            { "Raster anordnen", "Arrange grid" },
            { "Raster anordnen …", "Arrange grid …" },
            { "Offene Verbindungen im Raster anordnen", "Arrange open connections in a grid" },
            { "Offene Verbindungen", "Open connections" },
            { "Aufteilung", "Layout" },
            { "Übernehmen", "Apply" },
            { "Alle Felder leeren", "Clear all cells" },
            { "Hierher ziehen", "Drag here" },
            { "Verbindungen aus der Liste in die Felder ziehen – oder links anklicken und dann ein Feld anklicken. Felder untereinander tauschen per Ziehen, ✕ leert ein Feld. Nicht platzierte Verbindungen bleiben als Tab offen.",
              "Drag connections from the list into the cells – or click one on the left and then click a cell. Drag between cells to swap, ✕ clears a cell. Connections not placed stay open as tabs." },
            { "Eine Sitzung", "Single session" },
            { "2 nebeneinander", "2 side by side" },
            { "2 übereinander", "2 stacked" },
            { "3 nebeneinander", "3 side by side" },
            { "1 oben, 2 unten", "1 top, 2 bottom" },
            { "2 oben, 1 unten", "2 top, 1 bottom" },
            { "2 × 2", "2 × 2" },
            { "3 × 2", "3 × 2" },
            { "3 × 3", "3 × 3" },
            { "Leere Zelle – Verbindung hierher ziehen", "Empty cell – drag a connection here" },
            { "Aus dem Raster nehmen (Sitzung bleibt als Tab offen)", "Remove from grid (session stays open as a tab)" },
            { "Verbindung „{0}“ nicht gefunden.", "Connection “{0}” not found." },
            { "Neue Verbindung", "New connection" },
            { "Bearbeiten", "Edit" },
            { "Löschen", "Delete" },
            { "Verbinden", "Connect" },
            { "Trennen", "Disconnect" },
            { "Vollbild", "Full screen" },
            { "RDP-Datei importieren", "Import RDP file" },
            { "Darstellung", "Appearance" },
            { "Wie Windows", "Same as Windows" },
            { "Hell", "Light" },
            { "Dunkel", "Dark" },
            { "ERESM Design", "ERESM design" },
            { "Protokoll", "Log" },
            { "Bearbeiten …", "Edit …" },
            { "Duplizieren", "Duplicate" },
            { "Neue Verbindung …", "New connection …" },
            { "Erneut verbinden", "Reconnect" },
            { "In eigenem Fenster öffnen", "Open in separate window" },
            { "Wachhalten jetzt auslösen", "Send keep-alive now" },
            { "Tab schließen", "Close tab" },
            { "Ohne Gruppe", "No group" },
            { "Noch keine Verbindungen", "No connections yet" },
            { "{0} (Kopie)", "{0} (copy)" },
            { "Verbindung „{0}“ löschen?\n\nOffene Sitzungen bleiben bis zum Schließen bestehen.", "Delete connection “{0}”?\n\nOpen sessions stay open until you close them." },
            { "Verbindung löschen", "Delete connection" },
            { "Remotedesktop-Dateien (*.rdp)|*.rdp", "Remote Desktop files (*.rdp)|*.rdp" },
            { "RDP-Dateien importieren", "Import RDP files" },
            { "{0} Verbindung(en) importiert. Passwörter werden beim ersten Verbinden abgefragt.", "{0} connection(s) imported. Passwords will be requested on first connect." },
            { "{0} gespeicherte Verbindung(en)", "{0} saved connection(s)" },
            { "{0} Sitzung(en) noch verbunden. Trennen und beenden?", "{0} session(s) still connected. Disconnect and exit?" },
            { "ERESM-RDP-Manager beenden", "Exit ERESM-RDP-Manager" },
            { "Der ERESM-RDP-Manager ist bereits geöffnet.", "ERESM-RDP-Manager is already running." },
            // Sitzungsstatus
            { "Verbinde mit {0} …", "Connecting to {0} …" },
            { "Getrennt: {0}", "Disconnected: {0}" },
            { "Verbunden mit {0}", "Connected to {0}" },
            { "Anmeldung an {0} …", "Signing in to {0} …" },
            { "Wachhalten alle {0} Min.", "Keep-alive every {0} min" },
            { "Fehler: {0}", "Error: {0}" },
            { "zuletzt {0}", "last {0}" },
            { "nächstes {0}", "next {0}" },
            { "Verbinden nicht möglich:\n{0}", "Unable to connect:\n{0}" },
            { "Verbindung getrennt.", "Disconnected." },
            { "Sitzung wurde auf dem Server abgemeldet oder getrennt.", "The session was signed out or disconnected on the server." },
            { "Verbindung beendet (Code {0}).", "Connection ended (code {0})." },
            // Eigenes Fenster
            { "Zurück ins Hauptfenster", "Back to main window" },
            { "Die Verbindung zu „{0}“ ist noch offen.\n\nJa: Verbindung trennen und Fenster schließen\nNein: zurück als Tab ins Hauptfenster",
              "The connection to “{0}” is still open.\n\nYes: disconnect and close the window\nNo: move it back as a tab into the main window" },
            { "Fenster schließen", "Close window" },
            { "Tab schließen (oder mittlere Maustaste)", "Close tab (or middle mouse button)" },
            // Dialoge
            { "Verbindung bearbeiten", "Edit connection" },
            { "Server:", "Server:" },
            { "Rechnername oder IP-Adresse, optional mit Port: server01:3390", "Computer name or IP address, optionally with port: server01:3390" },
            { "Name:", "Name:" },
            { "Gruppe:", "Group:" },
            { "Zum Ordnen in der Liste, z. B. Kundenname. Leer = ohne Gruppe.", "For sorting the list, e.g. customer name. Empty = no group." },
            { "Benutzer:", "User:" },
            { "Domäne:", "Domain:" },
            { "Passwort:", "Password:" },
            { "Passwort speichern (verschlüsselt, nur für dein Windows-Konto lesbar)", "Save password (encrypted, readable only by your Windows account)" },
            { "RD-Gateway:", "RD Gateway:" },
            { "Nur nötig, wenn der Kunde ein Remotedesktop-Gateway nutzt. Sonst leer lassen.", "Only needed if the customer uses a Remote Desktop Gateway. Otherwise leave empty." },
            { "Zwischenablage teilen", "Share clipboard" },
            { "Lokale Laufwerke freigeben", "Share local drives" },
            { "Drucker freigeben", "Share printers" },
            { "Administrative Sitzung (wie mstsc /admin)", "Administrative session (like mstsc /admin)" },
            { "Wachhalten alle:", "Keep alive every:" },
            { "Minuten (0 = aus)", "minutes (0 = off)" },
            { "Drückt in diesem Abstand unsichtbar die Umschalt-Taste in der Sitzung, damit der Server nicht abmeldet.", "Invisibly presses Shift in the session at this interval so the server does not sign you out." },
            { "Notizen:", "Notes:" },
            { "Speichern", "Save" },
            { "Abbrechen", "Cancel" },
            { "Bitte Name und Server ausfüllen.", "Please enter a name and a server." },
            { "Anmelden – {0}", "Sign in – {0}" },
            { "(Benutzer wird beim Server abgefragt)", "(user will be requested by the server)" },
            { "Passwort speichern (verschlüsselt)", "Save password (encrypted)" },
        };

        // ---------- Flaggen ----------

        public static Bitmap Flag(string code, int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int h = (int)Math.Round(size * 0.68), top = (size - h) / 2;
                var r = new Rectangle(0, top, size, h);
                if (code == "de") Germany(g, r); else UnitedKingdom(g, r);
                using (var p = new Pen(Color.FromArgb(90, 0, 0, 0))) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            return bmp;
        }

        static void Germany(Graphics g, Rectangle r)
        {
            float s = r.Height / 3f;
            using (var b = new SolidBrush(Color.Black)) g.FillRectangle(b, r.X, r.Y, r.Width, s + 0.5f);
            using (var b = new SolidBrush(Color.FromArgb(221, 0, 0))) g.FillRectangle(b, r.X, r.Y + s, r.Width, s + 0.5f);
            using (var b = new SolidBrush(Color.FromArgb(255, 206, 0))) g.FillRectangle(b, r.X, r.Y + 2 * s, r.Width, r.Height - 2 * s);
        }

        static void UnitedKingdom(Graphics g, Rectangle r)
        {
            var red = Color.FromArgb(200, 16, 46);
            using (var b = new SolidBrush(Color.FromArgb(1, 33, 105))) g.FillRectangle(b, r);
            var state = g.Save();
            g.SetClip(r);
            float w = r.Width, h = r.Height, u = h / 10f;
            using (var white = new Pen(Color.White, 2f * u))
            using (var redPen = new Pen(red, 0.9f * u))
            {
                g.DrawLine(white, r.Left, r.Top, r.Right, r.Bottom);
                g.DrawLine(white, r.Right, r.Top, r.Left, r.Bottom);
                g.DrawLine(redPen, r.Left, r.Top, r.Right, r.Bottom);
                g.DrawLine(redPen, r.Right, r.Top, r.Left, r.Bottom);
            }
            using (var b = new SolidBrush(Color.White))
            {
                g.FillRectangle(b, r.Left + w / 2 - 1.7f * u, r.Top, 3.4f * u, h);
                g.FillRectangle(b, r.Left, r.Top + h / 2 - 1.7f * u, w, 3.4f * u);
            }
            using (var b = new SolidBrush(red))
            {
                g.FillRectangle(b, r.Left + w / 2 - u, r.Top, 2 * u, h);
                g.FillRectangle(b, r.Left, r.Top + h / 2 - u, w, 2 * u);
            }
            g.Restore(state);
        }
    }
}

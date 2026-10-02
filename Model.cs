// RDP-Manager — gespeicherte Verbindungen, Passwort-Verschlüsselung (DPAPI), Protokoll.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace RdpManager
{
    public class Connection
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public string Group = "";
        public string Host = "";
        public int Port = 3389;
        public string User = "";
        public string Domain = "";
        public string PasswordEnc = "";      // DPAPI, an das Windows-Konto gebunden; leer = beim Verbinden fragen
        public string Gateway = "";
        public bool Admin;
        public bool Clipboard = true;
        public bool Drives;
        public bool Printers;
        public int KeepAliveMinutes = 8;     // 0 = aus
        public string Notes = "";

        [XmlIgnore] public bool HasPassword { get { return !string.IsNullOrEmpty(PasswordEnc); } }

        public Connection Clone()
        {
            return (Connection)MemberwiseClone();
        }

        // "server:3390" -> Host + Port; IPv6 nur in eckigen Klammern mit Port.
        public static void SplitHost(string input, out string host, out int port)
        {
            host = (input ?? "").Trim();
            port = 3389;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && host.IndexOf(':') == colon || (host.StartsWith("[") && colon > host.IndexOf(']')))
            {
                int p;
                if (int.TryParse(host.Substring(colon + 1), out p) && p > 0 && p < 65536)
                {
                    port = p;
                    host = host.Substring(0, colon);
                }
            }
            host = host.Trim('[', ']');
        }

        public string HostDisplay { get { return Port == 3389 ? Host : Host + ":" + Port; } }
    }

    public class Store
    {
        public List<Connection> Connections = new List<Connection>();
        public int WindowX = -1, WindowY = -1, WindowWidth = 0, WindowHeight = 0;
        public bool Maximized;
        public int SidebarWidth = 260;
        public string Theme = "System";        // System | Hell | Dunkel | ERESM
        public string Language = "";           // de | en; leer = nach Windows-Sprache
        public List<string> CollapsedGroups = new List<string>();

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpManager"); }
        }

        static string FilePath { get { return Path.Combine(Folder, "verbindungen.xml"); } }

        public static Store Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var fs = File.OpenRead(FilePath))
                        return (Store)new XmlSerializer(typeof(Store)).Deserialize(fs);
            }
            catch (Exception ex)
            {
                Log.Write("Verbindungsliste nicht lesbar: " + ex.Message);
                try { File.Copy(FilePath, FilePath + ".defekt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); } catch { }
            }
            return new Store();
        }

        // Erst in eine Temp-Datei schreiben, dann ersetzen — kein halb geschriebenes XML bei Absturz.
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                using (var fs = File.Create(tmp))
                    new XmlSerializer(typeof(Store)).Serialize(fs, this);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak");
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("Speichern fehlgeschlagen: " + ex.Message); }
        }
    }

    public static class Secret
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RdpManager/1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));
        }

        public static string Unprotect(string enc)
        {
            if (string.IsNullOrEmpty(enc)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), Entropy, DataProtectionScope.CurrentUser)); }
            catch { return ""; }   // anderes Windows-Konto/Rechner: Passwort wird erneut abgefragt
        }
    }

    // Protokoll für den Support-Fall (keine Passwörter); rollt bei 1 MB.
    public static class Log
    {
        static readonly object Gate = new object();

        public static string FilePath { get { return Path.Combine(Store.Folder, "protokoll.log"); } }

        public static void Write(string text)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Store.Folder);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = FilePath + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}

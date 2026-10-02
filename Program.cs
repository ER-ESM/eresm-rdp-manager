// ERESM-RDP-Manager 1.9 — Remotedesktop-Verbindungen verwalten, Sitzungen in Tabs, Wachhalten ohne Fokus.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("ERESM-RDP-Manager")]
[assembly: System.Reflection.AssemblyProduct("ERESM-RDP-Manager")]
[assembly: System.Reflection.AssemblyFileVersion("1.9.0.0")]
[assembly: System.Reflection.AssemblyVersion("1.9.0.0")]

namespace RdpManager
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

        public static uint LocalIdleMs()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            GetLastInputInfo(ref info);
            return unchecked((uint)Environment.TickCount - info.dwTime);
        }
    }

    // Programmsymbol: Bildschirm mit Tab-Leiste und Herzschlag; kleine Hilfsbilder für Liste und Werkzeugleiste.
    static class AppIcon
    {
        public static Bitmap Render(int size, Color? status)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size / 32f;
                g.ScaleTransform(s, s);
                using (var stand = new SolidBrush(Color.FromArgb(120, 134, 148)))
                {
                    g.FillRectangle(stand, 13.5f, 23f, 5f, 4f);
                    using (var p = Rounded(8.5f, 26.5f, 15f, 3f, 1.5f)) g.FillPath(stand, p);
                }
                using (var p = Rounded(1f, 3f, 30f, 21f, 3f))
                using (var b = new SolidBrush(Color.FromArgb(31, 52, 68))) g.FillPath(b, p);
                using (var p = Rounded(3f, 5f, 26f, 16.5f, 1.5f))
                using (var b = new LinearGradientBrush(new PointF(0, 5f), new PointF(0, 21.5f), Color.FromArgb(64, 164, 236), Color.FromArgb(24, 104, 184)))
                    g.FillPath(b, p);
                // Tab-Leiste
                using (var tab = new SolidBrush(Color.White))
                using (var tab2 = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                {
                    using (var p = Rounded(5f, 7f, 8f, 3.5f, 1f)) g.FillPath(tab, p);
                    using (var p = Rounded(14f, 7f, 8f, 3.5f, 1f)) g.FillPath(tab2, p);
                }
                using (var pen = new Pen(Color.White, Math.Max(2f, 1.4f / s)))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new[] {
                        new PointF(5.5f, 15.5f), new PointF(10.5f, 15.5f), new PointF(12.5f, 12f),
                        new PointF(15.5f, 19f), new PointF(18f, 13.5f), new PointF(19.5f, 15.5f), new PointF(26.5f, 15.5f) });
                }
                if (status.HasValue)
                {
                    using (var ring = new SolidBrush(Color.White)) g.FillEllipse(ring, 19f, 17f, 13f, 13f);
                    using (var dot = new SolidBrush(status.Value)) g.FillEllipse(dot, 20.75f, 18.75f, 9.5f, 9.5f);
                }
            }
            return bmp;
        }

        public static Icon ToIcon(int size, Color? status)
        {
            using (var bmp = Render(size, status)) return Icon.FromHandle(bmp.GetHicon());
        }

        public static Bitmap Folder(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = size / 16f;
                g.ScaleTransform(s, s);
                using (var back = new SolidBrush(Color.FromArgb(226, 170, 40)))
                using (var front = new SolidBrush(Color.FromArgb(248, 200, 80)))
                {
                    using (var p = Rounded(1f, 3f, 6f, 3f, 1f)) g.FillPath(back, p);
                    using (var p = Rounded(1f, 4.5f, 14f, 9.5f, 1.2f)) g.FillPath(front, p);
                }
            }
            return bmp;
        }

        public static Bitmap Plus(int size) { return Glyph(size, Color.SeaGreen, true); }

        // Raster-Symbol: 2 × 2 Felder
        public static Bitmap Grid(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            using (var b = new SolidBrush(Color.FromArgb(24, 140, 120)))
            {
                float s = size / 16f;
                g.ScaleTransform(s, s);
                g.FillRectangle(b, 2f, 2f, 5.5f, 5.5f); g.FillRectangle(b, 8.5f, 2f, 5.5f, 5.5f);
                g.FillRectangle(b, 2f, 8.5f, 5.5f, 5.5f); g.FillRectangle(b, 8.5f, 8.5f, 5.5f, 5.5f);
            }
            return bmp;
        }
        public static Bitmap Play(int size) { return Glyph(size, Color.FromArgb(24, 104, 184), false); }

        static Bitmap Glyph(int size, Color c, bool plus)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = size / 16f;
                g.ScaleTransform(s, s);
                using (var b = new SolidBrush(c))
                {
                    if (plus) { g.FillRectangle(b, 7f, 2f, 2.2f, 12f); g.FillRectangle(b, 2f, 6.9f, 12f, 2.2f); }
                    else g.FillPolygon(b, new[] { new PointF(4f, 2f), new PointF(14f, 8f), new PointF(4f, 14f) });
                }
            }
            return bmp;
        }

        static GraphicsPath Rounded(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
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

            bool created;
            using (var mutex = new Mutex(true, "RdpManager_SingleInstance", out created))
            {
                if (!created)
                {
                    L.Lang = L.Resolve(Store.Load().Language);
                    MessageBox.Show(L.T("Der ERESM-RDP-Manager ist bereits geöffnet."), MainForm.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { Native.SetProcessDPIAware(); } catch { }
                Application.ThreadException += (s, e) => Log.Write("Fehler: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Schwerer Fehler: " + e.ExceptionObject);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Log.Write("Start " + MainForm.AppName + " " + MainForm.Version + ", RDP-Client " + RdpHost.ClsId);
                Application.Run(new MainForm(Array.FindAll(args, a => !a.StartsWith("--"))));
            }
        }
    }
}

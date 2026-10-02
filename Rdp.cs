// RDP-Manager — Anbindung an das Windows-Remotedesktop-ActiveX (mstscax.dll), Schnittstellen aus der Typbibliothek.
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RdpManager
{
    // Ereignisse des RDP-Steuerelements (dispinterface IMsTscAxEvents); nur benötigte DISPIDs.
    [ComImport, Guid("336d5562-efa8-482e-8cb3-c5c0fc7a7db6"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IMsTscAxEvents
    {
        [DispId(1)] void OnConnecting();
        [DispId(2)] void OnConnected();
        [DispId(3)] void OnLoginComplete();
        [DispId(4)] void OnDisconnected(int discReason);
        [DispId(5)] void OnEnterFullScreenMode();
        [DispId(6)] void OnLeaveFullScreenMode();
        [DispId(10)] void OnFatalError(int errorCode);
        [DispId(11)] void OnWarning(int warningCode);
        [DispId(22)] void OnLogonError(int lError);
    }

    // IMsRdpClientNonScriptable (erbt IMsTscNonScriptable) — vollständige vtable-Reihenfolge, gebraucht für SendKeys.
    [ComImport, Guid("2f079c4c-87b2-4afd-97ab-20cdb43038ae"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMsRdpClientNonScriptable
    {
        void put_ClearTextPassword([MarshalAs(UnmanagedType.BStr)] string value);
        void put_PortablePassword([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string get_PortablePassword();
        void put_PortableSalt([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string get_PortableSalt();
        void put_BinaryPassword([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string get_BinaryPassword();
        void put_BinarySalt([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string get_BinarySalt();
        void ResetPassword();
        void NotifyRedirectDeviceChange(IntPtr wParam, IntPtr lParam);
        void SendKeys(int numKeys,
                      [MarshalAs(UnmanagedType.LPArray)] short[] keyUp,     // VARIANT_BOOL: -1 = losgelassen
                      [MarshalAs(UnmanagedType.LPArray)] int[] keyData);    // Scancodes
    }

    [ClassInterface(ClassInterfaceType.None)]
    public class RdpEventSink : IMsTscAxEvents
    {
        readonly RdpHost host;
        public RdpEventSink(RdpHost host) { this.host = host; }
        public void OnConnecting() { host.Raise(host.Connecting); }
        public void OnConnected() { host.Raise(host.Connected); }
        public void OnLoginComplete() { host.Raise(host.LoginComplete); }
        public void OnDisconnected(int discReason) { if (host.Disconnected != null) host.Disconnected(discReason); }
        public void OnEnterFullScreenMode() { host.Raise(host.EnterFullScreen); }
        public void OnLeaveFullScreenMode() { host.Raise(host.LeaveFullScreen); }
        public void OnFatalError(int errorCode) { if (host.FatalError != null) host.FatalError(errorCode); }
        public void OnWarning(int warningCode) { if (host.Warning != null) host.Warning(warningCode); }
        public void OnLogonError(int lError) { if (host.LogonError != null) host.LogonError(lError); }
    }

    // Das RDP-Steuerelement als WinForms-Control. Neueste vorhandene Client-Version wird gewählt.
    public class RdpHost : AxHost
    {
        static readonly string[] ClsIds = {
            "a0c63c30-f08d-4ab4-907c-34905d770c7d",   // MsRdpClient10NotSafeForScripting
            "8b918b82-7985-4c24-89df-c33ad2bbfbcd",   // MsRdpClient9NotSafeForScripting
            "a3bc03a0-041d-42e3-ad22-882b7865c9c5",   // MsRdpClient8NotSafeForScripting
            "54d38bf7-b1ef-4479-9674-1bd6ea465258" }; // MsRdpClient7NotSafeForScripting

        public static readonly string ClsId = PickClsId();

        static string PickClsId()
        {
            foreach (var id in ClsIds)
                using (var k = Registry.ClassesRoot.OpenSubKey(@"CLSID\{" + id + "}"))
                    if (k != null) return id;
            return ClsIds[ClsIds.Length - 1];
        }

        // Rückrufe statt C#-Events, damit die Ereignis-Senke sie auslösen kann.
        public EventHandler Connecting, Connected, LoginComplete, EnterFullScreen, LeaveFullScreen;
        public Action<int> Disconnected, FatalError, Warning, LogonError;

        ConnectionPointCookie cookie;
        RdpEventSink sink;

        public RdpHost() : base(ClsId) { }

        internal void Raise(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }

        protected override void CreateSink()
        {
            try
            {
                sink = new RdpEventSink(this);
                cookie = new ConnectionPointCookie(GetOcx(), sink, typeof(IMsTscAxEvents));
            }
            catch (Exception ex) { Log.Write("Ereignis-Anbindung fehlgeschlagen: " + ex.Message); }
        }

        protected override void DetachSink()
        {
            try { if (cookie != null) cookie.Disconnect(); } catch { }
            cookie = null;
        }

        public dynamic Ocx { get { return GetOcx(); } }

        public IMsRdpClientNonScriptable NonScriptable
        {
            get { return GetOcx() as IMsRdpClientNonScriptable; }
        }

        // Umschalt-Taste in die Sitzung schicken — direkt über das Steuerelement, ohne Fokus/Vordergrund.
        public void SendShift()
        {
            var ns = NonScriptable;
            if (ns == null) throw new InvalidOperationException("SendKeys-Schnittstelle nicht verfügbar");
            ns.SendKeys(2, new short[] { 0, -1 }, new[] { 0x2A, 0x2A });
        }
    }
}

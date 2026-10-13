using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Serilog;

namespace PzlEv.Shared.Utils.Ui.Notifications;

/// <summary>
/// Powiadomienie systemowe Windows (w Windows 10/11 – powiadomienie w centrum akcji) przez ikonę obszaru powiadomień
/// (Shell_NotifyIcon, NIF_INFO) – bez dodatkowych bibliotek. Ikona jest usuwana po kilku sekundach.
/// </summary>
public static class WindowsNotification
{
    private const int NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const int NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10;
    private const int NiifInfo = 0x1, NiifWarning = 0x2, NiifError = 0x3;
    private const int IdiInformation = 32516;
    private static readonly ILogger Logger = Log.ForContext("Module", "shell");

    public static void Show(string title, string text, bool warning = false, bool error = false)
    {
        try
        {
            var window = Application.Current?.MainWindow;
            if (window is null)
                return;
            var data = new NotifyIconData
            {
                Size = Marshal.SizeOf<NotifyIconData>(),
                Window = new WindowInteropHelper(window).Handle,
                Id = 7001,
                Flags = NifIcon | NifTip | NifInfo,
                Icon = LoadIcon(IntPtr.Zero, IdiInformation),
                Tip = "PZL-EV",
                Info = text.Length > 255 ? text[..252] + "…" : text,
                InfoTitle = title.Length > 63 ? title[..60] + "…" : title,
                InfoFlags = error ? NiifError : warning ? NiifWarning : NiifInfo,
            };
            Shell_NotifyIcon(NimAdd, ref data);
            Shell_NotifyIcon(NimModify, ref data);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Shell_NotifyIcon(NimDelete, ref data);
            };
            timer.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "Powiadomienie Windows nieudane");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public int Id;
        public int Flags;
        public int CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public int State;
        public int StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public int TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public int InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, int name);
}

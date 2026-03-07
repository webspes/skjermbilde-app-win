using System;
using System.Threading;
using System.Windows.Forms;

namespace Skjermbilde;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        // Single instance check
        _mutex = new Mutex(true, "SkjermbildeNoSingleInstance", out bool isNew);
        if (!isNew)
        {
            // Signal existing instance to show settings
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SHOWSETTINGS, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new TrayApp());
        GC.KeepAlive(_mutex);
    }
}

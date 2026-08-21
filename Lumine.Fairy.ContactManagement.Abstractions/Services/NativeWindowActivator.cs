using System;
using System.Runtime.InteropServices;

namespace Lumine.Fairy.ContactManagement.Abstractions.Services;

public static class NativeWindowActivator
{
    public static bool TrySetForegroundWindow(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        return SetForegroundWindow(windowHandle);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
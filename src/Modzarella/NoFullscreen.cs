using System.Runtime.InteropServices;
using Photino.NET;

namespace Modzarella;

static class NoFullscreen
{
    public static void Apply(PhotinoWindow window)
    {
        window.RegisterMaximizedHandler((_, _) => window.SetMaximized(false));
        if (OperatingSystem.IsWindows()) Windows(window.WindowHandle);
        else if (OperatingSystem.IsMacOS()) Mac();
    }

    const int GwlStyle = -16;
    const long WsMaximizeBox = 0x00010000;

    static void Windows(IntPtr hwnd) => SetWindowLongPtr(hwnd, GwlStyle, (IntPtr)((long)GetWindowLongPtr(hwnd, GwlStyle) & ~WsMaximizeBox));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    const nint FullScreenNone = 1 << 9, ZoomButton = 2;

    static void Mac()
    {
        var windows = Send(Send(objc_getClass("NSApplication"), Sel("sharedApplication")), Sel("windows"));
        for (nint i = 0, n = Send(windows, Sel("count")); i < n; i++)
        {
            var w = Send(windows, Sel("objectAtIndex:"), i);
            Send(w, Sel("setCollectionBehavior:"), FullScreenNone);
            SendBool(Send(w, Sel("standardWindowButton:"), ZoomButton), Sel("setEnabled:"), false);
        }
    }

    const string ObjC = "/usr/lib/libobjc.A.dylib";
    static IntPtr Sel(string name) => sel_registerName(name);
    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(IntPtr target, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(IntPtr target, IntPtr sel, nint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendBool(IntPtr target, IntPtr sel, bool arg);
}

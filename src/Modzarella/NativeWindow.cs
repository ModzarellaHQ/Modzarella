using System.Runtime.InteropServices;
using Photino.NET;

namespace Modzarella;

static class NativeWindow
{
    public static void Apply(PhotinoWindow window)
    {
        window.RegisterMaximizedHandler((_, _) => window.SetMaximized(false));
        if (OperatingSystem.IsWindows()) Windows(window.WindowHandle);
        else if (OperatingSystem.IsMacOS()) { Mac(); AboutItem(); }
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

    // "About Modzarella" at the top of the app menu, showing the standard panel built from Info.plist
    static void AboutItem()
    {
        var app = Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
        var appMenu = Send(Send(Send(app, Sel("mainMenu")), Sel("itemAtIndex:"), 0), Sel("submenu"));
        if (appMenu == 0) return;
        var item = SendInit(Send(objc_getClass("NSMenuItem"), Sel("alloc")), Sel("initWithTitle:action:keyEquivalent:"),
            NSString("About Modzarella"), Sel("orderFrontStandardAboutPanel:"), NSString(""));
        SendInsert(appMenu, Sel("insertItem:atIndex:"), Send(objc_getClass("NSMenuItem"), Sel("separatorItem")), 0);
        SendInsert(appMenu, Sel("insertItem:atIndex:"), item, 0);
    }

    static IntPtr NSString(string s) => SendStr(objc_getClass("NSString"), Sel("stringWithUTF8String:"), s);

    const string ObjC = "/usr/lib/libobjc.A.dylib";
    static IntPtr Sel(string name) => sel_registerName(name);
    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(IntPtr target, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(IntPtr target, IntPtr sel, nint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendBool(IntPtr target, IntPtr sel, bool arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendStr(IntPtr target, IntPtr sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendInit(IntPtr target, IntPtr sel, IntPtr title, IntPtr action, IntPtr key);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendInsert(IntPtr target, IntPtr sel, IntPtr item, nint index);
}

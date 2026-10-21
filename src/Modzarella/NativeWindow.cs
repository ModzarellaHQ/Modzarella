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

    const nint FullScreenNone = 1 << 9, ZoomButton = 2, FullSizeContentView = 1 << 15, TitleHidden = 1;
    const double HeaderHeight = 48;

    static void Mac()
    {
        var windows = Send(Send(objc_getClass("NSApplication"), Sel("sharedApplication")), Sel("windows"));
        for (nint i = 0, n = Send(windows, Sel("count")); i < n; i++)
        {
            var w = Send(windows, Sel("objectAtIndex:"), i);
            Send(w, Sel("setCollectionBehavior:"), FullScreenNone);
            Send(w, Sel("setStyleMask:"), Send(w, Sel("styleMask")) | FullSizeContentView);
            SendBool(w, Sel("setTitlebarAppearsTransparent:"), true);
            Send(w, Sel("setTitleVisibility:"), TitleHidden);
            SendBool(Send(w, Sel("standardWindowButton:"), ZoomButton), Sel("setEnabled:"), false);
            Lights(w);
        }
    }

    // AppKit puts the window buttons back on every title bar layout, so they are watched and moved again each time
    static void Lights(IntPtr w)
    {
        for (nint i = 0; i < 3; i++)
        {
            var b = Send(w, Sel("standardWindowButton:"), i);
            if (b == 0 || !watched.Add(b)) continue;
            SendBool(b, Sel("setPostsFrameChangedNotifications:"), true);
            Observe(b, "NSViewFrameDidChangeNotification");
            SendObserver(b, Sel("addObserver:forKeyPath:options:context:"), Observer, NSString("frame"), 0, 0);
        }
        Observe(w, "NSWindowDidUpdateNotification");
        Place(w);
    }

    static void Place(IntPtr w)
    {
        if (placing) return;
        placing = true;
        var close = Send(w, Sel("standardWindowButton:"), 0);
        var bar = close == 0 ? default : Frame(Send(close, Sel("superview")));
        for (nint i = 0; i < 3 && close != 0; i++)
        {
            var b = Send(w, Sel("standardWindowButton:"), i);
            var f = Frame(b);
            double inset = (HeaderHeight - f.H) / 2, x = inset + i * 23, y = bar.H - inset - f.H;
            if (Math.Abs(f.X - x) > 0.5 || Math.Abs(f.Y - y) > 0.5) SendPoint(b, Sel("setFrameOrigin:"), x, y);
        }
        placing = false;
    }

    static bool placing;
    static readonly HashSet<nint> watched = [];
    delegate void Callback(IntPtr self, IntPtr sel, IntPtr a, IntPtr b, IntPtr c, IntPtr d);
    static readonly Callback onChange = (_, _, _, _, _, _) =>
    {
        foreach (var w in Windows()) Place(w);
        SendDelay(Observer, Sel("performSelector:withObject:afterDelay:"), Sel("later:"), 0, 0);
    };
    static readonly Callback onLater = (_, _, _, _, _, _) => { foreach (var w in Windows()) Place(w); };

    static readonly IntPtr Observer = MakeObserver();

    static IntPtr MakeObserver()
    {
        var cls = objc_allocateClassPair(objc_getClass("NSObject"), "ModzarellaLights", 0);
        var fn = Marshal.GetFunctionPointerForDelegate(onChange);
        class_addMethod(cls, Sel("changed:"), fn, "v@:@");
        class_addMethod(cls, Sel("later:"), Marshal.GetFunctionPointerForDelegate(onLater), "v@:@");
        class_addMethod(cls, Sel("observeValueForKeyPath:ofObject:change:context:"), fn, "v@:@@@^v");
        objc_registerClassPair(cls);
        return Send(Send(cls, Sel("alloc")), Sel("init"));
    }

    static void Observe(IntPtr target, string name) =>
        SendObserver(Send(objc_getClass("NSNotificationCenter"), Sel("defaultCenter")), Sel("addObserver:selector:name:object:"), Observer, Sel("changed:"), NSString(name), target);

    static IEnumerable<IntPtr> Windows()
    {
        var windows = Send(Send(objc_getClass("NSApplication"), Sel("sharedApplication")), Sel("windows"));
        for (nint i = 0, n = Send(windows, Sel("count")); i < n; i++) yield return Send(windows, Sel("objectAtIndex:"), i);
    }

    public static void Drag()
    {
        var app = Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
        var w = Send(app, Sel("keyWindow"));
        if (w != 0) Send(w, Sel("performWindowDragWithEvent:"), Send(app, Sel("currentEvent")));
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

    record struct Rect(double X, double Y, double W, double H);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern Rect SendRect(IntPtr target, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")] static extern void SendRectStret(out Rect r, IntPtr target, IntPtr sel);
    static Rect Frame(IntPtr view)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return SendRect(view, Sel("frame"));
        SendRectStret(out var r, view, Sel("frame"));
        return r;
    }

    const string ObjC = "/usr/lib/libobjc.A.dylib";
    static IntPtr Sel(string name) => sel_registerName(name);
    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extra);
    [DllImport(ObjC)] static extern void objc_registerClassPair(IntPtr cls);
    [DllImport(ObjC)] static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendObserver(IntPtr target, IntPtr sel, IntPtr a, IntPtr b, IntPtr c, IntPtr d);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendDelay(IntPtr target, IntPtr sel, IntPtr action, IntPtr arg, double delay);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendPoint(IntPtr target, IntPtr sel, double x, double y);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(IntPtr target, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(IntPtr target, IntPtr sel, nint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendBool(IntPtr target, IntPtr sel, bool arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendStr(IntPtr target, IntPtr sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendInit(IntPtr target, IntPtr sel, IntPtr title, IntPtr action, IntPtr key);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendInsert(IntPtr target, IntPtr sel, IntPtr item, nint index);
}

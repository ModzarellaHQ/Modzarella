using System.Diagnostics;
using System.Runtime.InteropServices;
using Photino.NET;

namespace Modzarella;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var settings = Settings.Load();
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60), MaxResponseContentBufferSize = 64 << 20 };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Modzarella/" + Web.AppVersion);
        if (args.Length == 0 || args[0].StartsWith("-psn")) return Window(settings, http);
        if (OperatingSystem.IsWindows()) AttachConsole(-1);
        return Cli.Run(args, settings, http).GetAwaiter().GetResult();
    }

    static int Window(Settings settings, HttpClient http)
    {
        var url = Web.Start(settings, http);
        if (Environment.GetEnvironmentVariable("MODZARELLA_BROWSER") == null)
            try
            {
                new PhotinoWindow()
                    .SetLogVerbosity(0)
                    .SetTitle("Modzarella")
                    .SetSize(1040, 760)
                    .SetMinSize(560, 480)
                    .Center()
                    .SetDevToolsEnabled(false)
                    .SetContextMenuEnabled(false)
                    .RegisterWindowCreatedHandler((w, _) => NativeWindow.Apply((PhotinoWindow)w!))
                    .RegisterWebMessageReceivedHandler((_, m) => { if (m == "drag" && OperatingSystem.IsMacOS()) NativeWindow.Drag(); })
                    .Load(new Uri(OperatingSystem.IsMacOS() ? url + "?mac" : url))
                    .WaitForClose();
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine($"No app window ({e.Message}), using the browser."); }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}

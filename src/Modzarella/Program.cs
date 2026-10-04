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
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Modzarella/1.0");
        if (args.Length == 0 || args[0].StartsWith("-psn")) return Window(settings, http);
        if (OperatingSystem.IsWindows()) AttachConsole(-1);
        return Cli.Run(args, settings, http).GetAwaiter().GetResult();
    }

    static int Window(Settings settings, HttpClient http)
    {
        var url = Web.Start(settings, http, () => Environment.Exit(0));
        if (Environment.GetEnvironmentVariable("MODZARELLA_BROWSER") == null)
            try
            {
                new PhotinoWindow()
                    .SetTitle("Modzarella")
                    .SetSize(1040, 760)
                    .SetMinSize(560, 480)
                    .Center()
                    .SetDevToolsEnabled(false)
                    .SetContextMenuEnabled(false)
                    .RegisterWindowCreatedHandler((w, _) => NoFullscreen.Apply((PhotinoWindow)w!))
                    .Load(new Uri(url))
                    .WaitForClose();
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine($"No app window ({e.Message}), using the browser."); }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}

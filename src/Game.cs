using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CheeseMM;

public enum Platform { Mac, Windows, Proton }

public record Game(string Dir, Platform Platform)
{
    public const int SteamAppId = 3809440;
    public const string FolderName = "Cheese Rolling";

    public string Plugins => Path.Combine(Dir, "BepInEx", "plugins", "CheeseMods");
    public string Disabled => Path.Combine(Dir, "BepInEx", "plugins-disabled", "CheeseMods");
    public bool LoaderInstalled => File.Exists(Path.Combine(Dir, "BepInEx", "core", "BepInEx.dll"));

    public static Game? Find(string? overrideDir)
    {
        var dirs = string.IsNullOrEmpty(overrideDir) ? SteamLibraries().Select(l => Path.Combine(l, "steamapps", "common", FolderName)) : [overrideDir];
        foreach (var dir in dirs.Where(Directory.Exists))
        {
            if (Directory.Exists(Path.Combine(dir, "CheeseRolling.app"))) return new Game(dir, Platform.Mac);
            if (File.Exists(Path.Combine(dir, "CheeseRolling.exe")))
                return new Game(dir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Platform.Windows : Platform.Proton);
        }
        return null;
    }

    static IEnumerable<string> SteamLibraries()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] roots = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? [@"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam"]
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? [Path.Combine(home, "Library/Application Support/Steam")]
                : [Path.Combine(home, ".local/share/Steam"), Path.Combine(home, ".steam/steam"), Path.Combine(home, ".var/app/com.valvesoftware.Steam/data/Steam")];
        var seen = new HashSet<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            if (seen.Add(root)) yield return root;
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var lib = m.Groups[1].Value.Replace(@"\\", @"\");
                if (seen.Add(lib)) yield return lib;
            }
        }
    }

    public string Launch()
    {
        if (Platform == Platform.Mac)
        {
            if (Process.GetProcessesByName("steam_osx").Length == 0)
            {
                Process.Start("open", "-a Steam");
                return "Steam isn't running, so it's starting now. Press Play again once Steam is open.";
            }
            var psi = new ProcessStartInfo("/bin/sh", ["-c", "nohup ./run_bepinex.sh >/dev/null 2>&1 &"]) { WorkingDirectory = Dir, UseShellExecute = false };
            psi.Environment["SteamAppId"] = SteamAppId.ToString();
            psi.Environment["SteamGameId"] = SteamAppId.ToString();
            Process.Start(psi);
            return "Started with mods. Steam must be running.";
        }
        Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamAppId}") { UseShellExecute = true });
        return Platform == Platform.Proton
            ? "Started through Steam. Proton needs this once in Steam → Properties → Launch Options: WINEDLLOVERRIDES=\"winhttp=n,b\" %command%"
            : "Started through Steam.";
    }
}

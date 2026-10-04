using System.IO.Compression;

namespace Modzarella;

public static class Loader
{
    const string Version = "5.4.23.5";
    const string Releases = "https://github.com/BepInEx/BepInEx/releases/download/v" + Version + "/";

    public static async Task<string> Install(Game game, HttpClient http)
    {
        var asset = game.Platform == Platform.Mac ? $"BepInEx_macos_universal_{Version}.zip" : $"BepInEx_win_x64_{Version}.zip";
        var zip = await http.GetByteArrayAsync(Releases + asset);
        using (var archive = new ZipArchive(new MemoryStream(zip)))
            archive.ExtractToDirectory(game.Dir, overwriteFiles: true);

        Directory.CreateDirectory(Path.Combine(game.Dir, "BepInEx", "config"));
        if (game.Platform == Platform.Mac) PatchMac(game.Dir);
        return $"BepInEx {Version} installed.";
    }

    static void PatchMac(string dir)
    {
        var script = Path.Combine(dir, "run_bepinex.sh");
        var s = File.ReadAllText(script)
            .Replace("executable_name=\"\"", "executable_name=\"CheeseRolling.app\"")
            .Replace("export ARCHPREFERENCE=\"arm64,x86_64\"", "export ARCHPREFERENCE=\"x86_64\"")
            .Replace("exec arch -e DYLD_INSERT_LIBRARIES", "exec arch -x86_64 -e DYLD_INSERT_LIBRARIES");
        File.WriteAllText(script, s);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(script, File.GetUnixFileMode(script) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        var cfg = Path.Combine(dir, "BepInEx", "config", "BepInEx.cfg");
        var text = File.Exists(cfg) ? File.ReadAllText(cfg) : "";
        text = text.Contains("[Preloader.Entrypoint]")
            ? System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^Type = .*$", "Type = Camera")
            : text + "\n[Preloader.Entrypoint]\nAssembly = UnityEngine.CoreModule.dll\nType = Camera\nMethod = .cctor\n";
        File.WriteAllText(cfg, text);
    }

    public static string Remove(Game game)
    {
        foreach (var d in new[] { "BepInEx" }) if (Directory.Exists(Path.Combine(game.Dir, d))) Directory.Delete(Path.Combine(game.Dir, d), true);
        foreach (var f in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "libdoorstop.dylib", "run_bepinex.sh", "changelog.txt" })
            if (File.Exists(Path.Combine(game.Dir, f))) File.Delete(Path.Combine(game.Dir, f));
        return "Loader and all mods removed.";
    }
}

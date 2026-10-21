using System.Text.RegularExpressions;
namespace Modzarella;

public static class CoreRuntime
{
    public const string Id = "core";

    public static string? Ensure(Game game)
    {
        foreach (var old in new[] { Path.Combine(game.Plugins, Id), Path.Combine(game.Disabled, Id) })
            if (Directory.Exists(old)) Directory.Delete(old, true);
        // installs cut short by older versions left Modz/<id>.new, which the game loaded as a mod
        if (Directory.Exists(game.Plugins))
            foreach (var left in Directory.GetDirectories(game.Plugins, "*.new")) Directory.Delete(left, true);

        var dir = Path.Combine(game.Dir, "BepInEx", "plugins", "Modzarella");
        Directory.CreateDirectory(dir);
        var asm = typeof(CoreRuntime).Assembly;
        bool changed = false;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("runtime/")))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            var path = Path.Combine(dir, name["runtime/".Length..]);
            if (Same(path, s)) continue;
            using (var f = File.Create(path)) s.CopyTo(f);
            changed = true;
        }
        LateStart(game);
        return changed ? "Modzarella Core installed." : null;
    }

    static bool Same(string path, Stream resource)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != resource.Length) return false;
        try
        {
            using var f = file.OpenRead();
            Span<byte> a = stackalloc byte[4096], b = stackalloc byte[4096];
            for (int n; (n = resource.Read(a)) > 0;)
                if (f.ReadAtLeast(b[..n], n, false) != n || !a[..n].SequenceEqual(b[..n])) return false;
            return true;
        }
        finally { resource.Position = 0; }
    }

    // start plugins once the game's camera exists; the earlier default start leaves them silent on Windows
    static void LateStart(Game game)
    {
        var cfg = Path.Combine(game.Dir, "BepInEx", "config", "BepInEx.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
        var text = File.Exists(cfg) ? File.ReadAllText(cfg) : "";
        var want = "[Preloader.Entrypoint]\nAssembly = UnityEngine.CoreModule.dll\nType = Camera\nMethod = .cctor\n";
        if (Regex.IsMatch(text, @"(?m)^Type = Camera\s*$")) return;
        text = text.Contains("[Preloader.Entrypoint]")
            ? Regex.Replace(text, @"(?ms)^\[Preloader\.Entrypoint\].*?(?=^\[|\z)", want + "\n")
            : text + "\n" + want;
        File.WriteAllText(cfg, text);
    }
}

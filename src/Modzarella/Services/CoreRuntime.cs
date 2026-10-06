using System.Text.RegularExpressions;
namespace Modzarella;

public static class CoreRuntime
{
    public const string Id = "core";

    public static string? Ensure(Game game)
    {
        foreach (var old in new[] { Path.Combine(game.Plugins, Id), Path.Combine(game.Disabled, Id) })
            if (Directory.Exists(old)) Directory.Delete(old, true);

        var dir = Path.Combine(game.Dir, "BepInEx", "plugins", "Modzarella");
        Directory.CreateDirectory(dir);
        var asm = typeof(CoreRuntime).Assembly;
        bool changed = false;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("runtime/")))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var m = new MemoryStream();
            s.CopyTo(m);
            var path = Path.Combine(dir, name["runtime/".Length..]);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(m.ToArray())) continue;
            File.WriteAllBytes(path, m.ToArray());
            changed = true;
        }
        LateStart(game);
        return changed ? "Modzarella Core installed." : null;
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

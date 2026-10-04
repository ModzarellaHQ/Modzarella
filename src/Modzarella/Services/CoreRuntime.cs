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
        return changed ? "Modzarella Core installed." : null;
    }
}

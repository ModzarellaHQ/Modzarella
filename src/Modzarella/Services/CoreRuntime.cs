namespace Modzarella;

public static class CoreRuntime
{
    public const string Id = "core";

    static byte[] Bytes()
    {
        using var s = typeof(CoreRuntime).Assembly.GetManifestResourceStream("Modzarella.Core.dll")!;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    public static string? Ensure(Game game)
    {
        foreach (var old in new[] { Path.Combine(game.Plugins, Id), Path.Combine(game.Disabled, Id) })
            if (Directory.Exists(old)) Directory.Delete(old, true);

        var dir = Path.Combine(game.Dir, "BepInEx", "plugins", "Modzarella");
        var path = Path.Combine(dir, "Modzarella.Core.dll");
        var bytes = Bytes();
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return null;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, bytes);
        return "Modzarella Core installed.";
    }
}

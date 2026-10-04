using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CheeseMM;

public record ModFile(string Path, string Target, long Size, string Sha256);
public record Mod(string Id, string Name, string Version, string Author, string Description, string[] Dependencies, string[]? Credits, ModFile[] Files);
record Index(Mod[] Mods);

public enum State { NotInstalled, Enabled, Disabled, UpdateAvailable }

public class Catalog(string source, HttpClient http)
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    bool Local => !source.StartsWith("http://") && !source.StartsWith("https://");

    async Task<byte[]> Fetch(string path) =>
        Local ? await File.ReadAllBytesAsync(Path.Combine(source, path)) : await http.GetByteArrayAsync(source.TrimEnd('/') + "/" + path);

    public async Task<Mod[]> Mods() => JsonSerializer.Deserialize<Index>(await Fetch("index.json"), Json)!.Mods;

    public static Mod? Installed(Game game, string id)
    {
        foreach (var root in new[] { game.Plugins, game.Disabled })
        {
            var f = System.IO.Path.Combine(root, id, "mod.json");
            if (File.Exists(f)) return JsonSerializer.Deserialize<Mod>(File.ReadAllText(f), Json);
        }
        return null;
    }

    public static State StateOf(Game game, Mod mod)
    {
        var inst = Installed(game, mod.Id);
        if (inst == null) return State.NotInstalled;
        if (inst.Version != mod.Version) return State.UpdateAvailable;
        return Directory.Exists(System.IO.Path.Combine(game.Plugins, mod.Id)) ? State.Enabled : State.Disabled;
    }

    public async Task<List<string>> Install(Game game, string id, Mod[]? all = null, HashSet<string>? done = null)
    {
        all ??= await Mods();
        done ??= [];
        var log = new List<string>();
        if (!done.Add(id)) return log;
        var mod = all.FirstOrDefault(m => m.Id == id) ?? throw new Exception($"No mod '{id}' in the catalog.");
        foreach (var dep in mod.Dependencies) log.AddRange(await Install(game, dep, all, done));
        if (StateOf(game, mod) is State.Enabled or State.Disabled) return log;

        var dir = System.IO.Path.Combine(game.Plugins, mod.Id);
        var staging = dir + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        foreach (var f in mod.Files)
        {
            var bytes = await Fetch(f.Path);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != f.Sha256) throw new Exception($"Checksum mismatch: {f.Path}");
            var to = System.IO.Path.Combine(staging, f.Target);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
            await File.WriteAllBytesAsync(to, bytes);
        }
        await File.WriteAllTextAsync(System.IO.Path.Combine(staging, "mod.json"), JsonSerializer.Serialize(mod, Json));
        Remove(game, mod.Id);
        Directory.Move(staging, dir);
        log.Add($"Installed {mod.Name} {mod.Version}");
        return log;
    }

    public static string Remove(Game game, string id)
    {
        foreach (var root in new[] { game.Plugins, game.Disabled })
        {
            var d = System.IO.Path.Combine(root, id);
            if (Directory.Exists(d)) Directory.Delete(d, true);
        }
        return $"Removed {id}";
    }

    public static string SetEnabled(Game game, string id, bool on)
    {
        var from = System.IO.Path.Combine(on ? game.Disabled : game.Plugins, id);
        var to = System.IO.Path.Combine(on ? game.Plugins : game.Disabled, id);
        if (!Directory.Exists(from)) return $"{id} is already {(on ? "enabled" : "disabled")}";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
        Directory.Move(from, to);
        return $"{(on ? "Enabled" : "Disabled")} {id}";
    }

    public async Task<List<string>> UpdateAll(Game game)
    {
        var all = await Mods();
        var log = new List<string>();
        foreach (var m in all.Where(m => StateOf(game, m) == State.UpdateAvailable))
        {
            bool wasDisabled = Directory.Exists(System.IO.Path.Combine(game.Disabled, m.Id));
            Remove(game, m.Id);
            log.AddRange(await Install(game, m.Id, all));
            if (wasDisabled) SetEnabled(game, m.Id, false);
        }
        if (log.Count == 0) log.Add("Everything is up to date.");
        return log;
    }
}

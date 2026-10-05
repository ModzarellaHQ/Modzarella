using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Modzarella;

public record ModFile(string Path, string Target, long Size, string Sha256);
public record Mod(string Id, string Name, string Version, string Author, string Description, string[] Dependencies, string[]? Credits, ModFile[] Files, string[]? Replaces = null);
record Index(Mod[] Mods);

public enum State { NotInstalled, Enabled, Disabled, UpdateAvailable }

public class Catalog(string source, HttpClient http)
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    static (string Source, string? ETag, Mod[] Mods, DateTime At)? cached;
    bool Local => !source.StartsWith("http://") && !source.StartsWith("https://");

    public static bool ValidId(string? id) => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9]+(-[a-z0-9]+)*$");

    static string Inside(string dir, string relative)
    {
        var root = System.IO.Path.GetFullPath(dir).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, relative));
        return full.StartsWith(root, StringComparison.Ordinal) ? full : throw new Exception($"Unsafe path in the mod list: {relative}");
    }

    async Task<byte[]> Fetch(string path) =>
        Local ? await File.ReadAllBytesAsync(Inside(source, path)) : await http.GetByteArrayAsync(source.TrimEnd('/') + "/" + path);

    // the list is cached for a minute and revalidated with its ETag, so it is only downloaded again when it changed
    public async Task<Mod[]> Mods()
    {
        if (Local) return Parse(await Fetch("index.json"));
        if (cached is { } c && c.Source == source && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(1)) return c.Mods;
        using var req = new HttpRequestMessage(HttpMethod.Get, source.TrimEnd('/') + "/index.json");
        if (cached is { } old && old.Source == source && old.ETag != null) req.Headers.TryAddWithoutValidation("If-None-Match", old.ETag);
        using var res = await http.SendAsync(req);
        if (res.StatusCode == System.Net.HttpStatusCode.NotModified && cached is { } same)
        {
            cached = same with { At = DateTime.UtcNow };
            return same.Mods;
        }
        res.EnsureSuccessStatusCode();
        var mods = Parse(await res.Content.ReadAsByteArrayAsync());
        cached = (source, res.Headers.ETag?.ToString(), mods, DateTime.UtcNow);
        return mods;
    }

    static Mod[] Parse(byte[] json) => JsonSerializer.Deserialize<Index>(json, Json)!.Mods.Where(m => ValidId(m.Id)).ToArray();

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
        if (!ValidId(id)) throw new Exception($"Invalid mod id: {id}");
        all ??= await Mods();
        done ??= [];
        var log = new List<string>();
        if (!done.Add(id)) return log;
        var mod = all.FirstOrDefault(m => m.Id == id) ?? throw new Exception($"No mod '{id}' in the catalog.");
        foreach (var dep in mod.Dependencies.Where(d => d != CoreRuntime.Id)) log.AddRange(await Install(game, dep, all, done));
        if (StateOf(game, mod) is State.Enabled or State.Disabled) return log;

        var dir = System.IO.Path.Combine(game.Plugins, mod.Id);
        var old = Directory.Exists(dir) ? dir : System.IO.Path.Combine(game.Disabled, mod.Id);
        var staging = dir + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        foreach (var f in mod.Files)
        {
            var to = Inside(staging, f.Target);
            var have = Inside(old, f.Target);
            var local = File.Exists(have) ? await File.ReadAllBytesAsync(have) : null;
            var bytes = local != null && Convert.ToHexStringLower(SHA256.HashData(local)) == f.Sha256 ? local : await Fetch(f.Path);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != f.Sha256) throw new Exception($"Checksum mismatch: {f.Path}");
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
        if (!ValidId(id)) throw new Exception($"Invalid mod id: {id}");
        foreach (var root in new[] { game.Plugins, game.Disabled })
        {
            var d = System.IO.Path.Combine(root, id);
            if (Directory.Exists(d)) Directory.Delete(d, true);
        }
        return $"Removed {id}";
    }

    public static string SetEnabled(Game game, string id, bool on)
    {
        if (!ValidId(id)) throw new Exception($"Invalid mod id: {id}");
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
        // a mod that was renamed lists its old id in "replaces": swap the old install for the new one
        foreach (var m in all.Where(m => m.Replaces != null && StateOf(game, m) == State.NotInstalled))
            foreach (var old in m.Replaces!.Where(o => ValidId(o) && Installed(game, o) != null))
            {
                log.AddRange(await Install(game, m.Id, all));
                log.Add(Remove(game, old));
            }
        foreach (var m in all.Where(m => StateOf(game, m) == State.UpdateAvailable))
        {
            bool wasDisabled = Directory.Exists(System.IO.Path.Combine(game.Disabled, m.Id));
            log.AddRange(await Install(game, m.Id, all));
            if (wasDisabled) SetEnabled(game, m.Id, false);
        }
        if (log.Count == 0) log.Add("Everything is up to date.");
        return log;
    }
}

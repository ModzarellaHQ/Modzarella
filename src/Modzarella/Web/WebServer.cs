using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Modzarella;

public static class Web
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly SemaphoreSlim busy = new(1, 1);

    static readonly Dictionary<string, (string Resource, string Type)> files = new()
    {
        ["/"] = ("ui.html", "text/html"),
        ["/ui.css"] = ("ui.css", "text/css"),
        ["/ui.js"] = ("ui.js", "text/javascript"),
        ["/theme.css"] = ("theme.css", "text/css"),
    };

    public static string Start(Settings settings, HttpClient http)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var url = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();
        _ = Loop(listener, settings, http, $"127.0.0.1:{port}");
        return url;
    }

    static async Task Loop(HttpListener listener, Settings settings, HttpClient http, string host)
    {
        var cache = files.Values.ToDictionary(f => f.Resource, f => Read(f.Resource));
        while (true)
        {
            var ctx = await listener.GetContextAsync();
            _ = Handle(ctx, settings, http, host, cache);
        }
    }

    static byte[] Read(string resource)
    {
        using var s = typeof(Web).Assembly.GetManifestResourceStream(resource)!;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    static async Task Handle(HttpListenerContext ctx, Settings settings, HttpClient http, string host, Dictionary<string, byte[]> cache)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; form-action 'none'; base-uri 'none'";
        res.Headers["X-Content-Type-Options"] = "nosniff";
        res.Headers["Referrer-Policy"] = "no-referrer";
        res.Headers["Cache-Control"] = "no-store";
        try
        {
            if (req.Headers["Host"] != host) { await Send(res, 403, new { error = "forbidden" }); return; }
            var path = req.Url!.AbsolutePath;
            if (files.TryGetValue(path, out var file))
            {
                if (req.HttpMethod != "GET") { await Send(res, 405, new { error = "method not allowed" }); return; }
                await Send(res, 200, cache[file.Resource], file.Type);
                return;
            }
            if (path == "/api/state" && req.HttpMethod == "GET") { await Send(res, 200, await State(settings, http)); return; }
            if (path == "/api/do")
            {
                var origin = req.Headers["Origin"];
                if (req.HttpMethod != "POST" || req.ContentType?.StartsWith("application/json") != true || (origin != null && origin != $"http://{host}"))
                {
                    await Send(res, 403, new { error = "forbidden" });
                    return;
                }
                var r = (await JsonSerializer.DeserializeAsync<Request>(req.InputStream, Json))!;
                await busy.WaitAsync();
                try { await Send(res, 200, new { log = await Do(settings, http, r) }); }
                finally { busy.Release(); }
                return;
            }
            await Send(res, 404, new { error = "not found" });
        }
        catch (Exception e)
        {
            try { await Send(res, 500, new { error = e.Message }); } catch { }
        }
    }

    record Request(string Action, string? Id, string? Value);

    static string Pretty(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home) ? "~" + path[home.Length..] : path;
    }

    static Task Send(HttpListenerResponse res, int status, object reply) =>
        Send(res, status, JsonSerializer.SerializeToUtf8Bytes(reply, Json), "application/json");

    static async Task Send(HttpListenerResponse res, int status, byte[] body, string type)
    {
        res.StatusCode = status;
        res.ContentType = type + "; charset=utf-8";
        res.ContentLength64 = body.Length;
        await res.OutputStream.WriteAsync(body);
        res.Close();
    }

    static async Task<object> State(Settings settings, HttpClient http)
    {
        var game = Game.Find(settings.GameDir);
        Mod[] mods = [];
        string? error = null;
        try { mods = await new Catalog(settings.Source, http).Mods(); }
        catch (Exception e) { error = $"Can't read the mod source: {e.Message}"; }
        return new
        {
            source = settings.Source,
            gameDir = settings.GameDir,
            game = game == null ? null : new { Dir = Pretty(game.Dir), platform = game.Platform.ToString(), loader = game.LoaderInstalled },
            mods = mods.Select(m => new { mod = m, state = game == null ? "NotInstalled" : Catalog.StateOf(game, m).ToString() }),
            error,
        };
    }

    static async Task<List<string>> Do(Settings settings, HttpClient http, Request r)
    {
        if (r.Action == "source") { settings.Source = r.Value!.Trim(); settings.Save(); return ["Source set."]; }
        if (r.Action == "game") { settings.GameDir = string.IsNullOrWhiteSpace(r.Value) ? null : r.Value.Trim(); settings.Save(); return ["Game folder set."]; }
        var game = Game.Find(settings.GameDir) ?? throw new Exception("Cheese Rolling not found. Set the game folder.");
        var catalog = new Catalog(settings.Source, http);
        switch (r.Action)
        {
            case "loader": return [await Loader.Install(game, http)];
            case "unloader": return [Loader.Remove(game)];
            case "install":
                var log = new List<string>();
                if (!game.LoaderInstalled) log.Add(await Loader.Install(game, http));
                CoreRuntime.Ensure(game);
                log.AddRange(await catalog.Install(game, r.Id!));
                return log;
            case "remove": return [Catalog.Remove(game, r.Id!)];
            case "reset":
                var cfgs = Directory.Exists(game.Config) ? Directory.GetFiles(game.Config, "modz.*.cfg") : [];
                foreach (var f in cfgs) File.Delete(f);
                return [$"Reset {cfgs.Length} settings files. Restart the game to see the defaults."];
            case "open":
                var path = r.Value switch { "game" => game.Dir, "mods" => game.Plugins, "log" => game.Log, _ => throw new Exception("Unknown folder") };
                if (!File.Exists(path) && !Directory.Exists(path)) return [$"{Pretty(path)} doesn't exist yet."];
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return [$"Opened {Pretty(path)}"];
            case "enable": return [Catalog.SetEnabled(game, r.Id!, true)];
            case "disable": return [Catalog.SetEnabled(game, r.Id!, false)];
            case "update": return await catalog.UpdateAll(game);
            case "launch":
                var steps = new List<string>();
                if (!game.LoaderInstalled) steps.Add(await Loader.Install(game, http));
                if (CoreRuntime.Ensure(game) is { } core) steps.Add(core);
                steps.Add(game.Launch());
                return steps;
            default: throw new Exception("Unknown action " + r.Action);
        }
    }
}

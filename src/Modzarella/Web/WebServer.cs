using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Modzarella;

public static class Web
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Start(Settings settings, HttpClient http, Action quit)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var url = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();
        _ = Loop(listener, settings, http, quit);
        return url;
    }

    static async Task Loop(HttpListener listener, Settings settings, HttpClient http, Action quit)
    {
        var html = new StreamReader(typeof(Web).Assembly.GetManifestResourceStream("ui.html")!).ReadToEnd();
        while (true)
        {
            var ctx = await listener.GetContextAsync();
            var path = ctx.Request.Url!.AbsolutePath;
            object reply;
            try
            {
                if (path == "/") { await Send(ctx, html, "text/html"); continue; }
                if (path == "/api/quit") { await Send(ctx, "{}", "application/json"); quit(); return; }
                reply = path switch
                {
                    "/api/state" => await State(settings, http),
                    "/api/do" => new { log = await Do(settings, http, (await JsonSerializer.DeserializeAsync<Request>(ctx.Request.InputStream, Json))!) },
                    _ => new { error = "not found" },
                };
            }
            catch (Exception e) { reply = new { error = e.Message }; }
            await Send(ctx, JsonSerializer.Serialize(reply, Json), "application/json");
        }
    }

    record Request(string Action, string? Id, string? Value);

    static string Pretty(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home) ? "~" + path[home.Length..] : path;
    }

    static async Task Send(HttpListenerContext ctx, string body, string type)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentType = type + "; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
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

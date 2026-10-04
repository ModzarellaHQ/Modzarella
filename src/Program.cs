using CheeseMM;

var settings = Settings.Load();
var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("CheeseMM/1.0");
var cmd = args.FirstOrDefault() ?? "ui";
var rest = args.Skip(1).ToArray();

try
{
    if (cmd == "ui") { await Web.Serve(settings, http); return 0; }
    if (cmd == "source")
    {
        if (rest.Length > 0) { settings.Source = rest[0]; settings.Save(); }
        Console.WriteLine(settings.Source);
        return 0;
    }
    if (cmd == "game")
    {
        if (rest.Length > 0) { settings.GameDir = rest[0]; settings.Save(); }
        Console.WriteLine(Game.Find(settings.GameDir)?.Dir ?? "Cheese Rolling not found. Set it with: cheesemm game <folder>");
        return 0;
    }

    var game = Game.Find(settings.GameDir) ?? throw new Exception("Cheese Rolling not found. Set it with: cheesemm game <folder>");
    var catalog = new Catalog(settings.Source, http);
    switch (cmd)
    {
        case "status":
            Console.WriteLine($"Game:   {game.Dir} ({game.Platform})");
            Console.WriteLine($"Loader: {(game.LoaderInstalled ? "installed" : "missing (cheesemm loader)")}");
            Console.WriteLine($"Source: {settings.Source}");
            break;
        case "list":
            foreach (var m in await catalog.Mods())
                Console.WriteLine($"{m.Id,-12} {m.Version,-8} {Catalog.StateOf(game, m),-16} {m.Name} — {m.Description}");
            break;
        case "loader":
            Console.WriteLine(await Loader.Install(game, http));
            break;
        case "unloader":
            Console.WriteLine(Loader.Remove(game));
            break;
        case "install":
            if (!game.LoaderInstalled) Console.WriteLine(await Loader.Install(game, http));
            var all = await catalog.Mods();
            foreach (var id in rest.Length > 0 ? rest : all.Select(m => m.Id).ToArray())
                foreach (var line in await catalog.Install(game, id, all)) Console.WriteLine(line);
            break;
        case "remove":
            foreach (var id in rest) Console.WriteLine(Catalog.Remove(game, id));
            break;
        case "enable":
        case "disable":
            foreach (var id in rest) Console.WriteLine(Catalog.SetEnabled(game, id, cmd == "enable"));
            break;
        case "update":
            foreach (var line in await catalog.UpdateAll(game)) Console.WriteLine(line);
            break;
        case "launch":
            Console.WriteLine(game.Launch());
            break;
        default:
            Console.WriteLine("""
                cheesemm                  open the manager in your browser
                cheesemm status           game folder, loader, mod source
                cheesemm list             all mods and their state
                cheesemm install [ids]    install mods (all if none given) and the loader if missing
                cheesemm remove <ids>     uninstall mods
                cheesemm enable <ids>     turn installed mods on
                cheesemm disable <ids>    turn installed mods off
                cheesemm update           update installed mods
                cheesemm loader           install or repair BepInEx
                cheesemm unloader         remove BepInEx and every mod
                cheesemm launch           start the game with mods
                cheesemm source [url|dir] show or set the mod source
                cheesemm game [dir]       show or set the game folder
                """);
            return cmd is "help" or "-h" or "--help" ? 0 : 1;
    }
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

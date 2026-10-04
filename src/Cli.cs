namespace Modzarella;

static class Cli
{
    public static async Task<int> Run(string[] args, Settings settings, HttpClient http)
    {
        var cmd = args[0];
        var rest = args.Skip(1).ToArray();
        try
        {
            if (cmd == "source")
            {
                if (rest.Length > 0) { settings.Source = rest[0]; settings.Save(); }
                Console.WriteLine(settings.Source);
                return 0;
            }
            if (cmd == "game")
            {
                if (rest.Length > 0) { settings.GameDir = rest[0]; settings.Save(); }
                Console.WriteLine(Game.Find(settings.GameDir)?.Dir ?? "Cheese Rolling not found. Set it with: modzarella game <folder>");
                return 0;
            }

            var game = Game.Find(settings.GameDir) ?? throw new Exception("Cheese Rolling not found. Set it with: modzarella game <folder>");
            var catalog = new Catalog(settings.Source, http);
            switch (cmd)
            {
                case "status":
                    Console.WriteLine($"Game:   {game.Dir} ({game.Platform})");
                    Console.WriteLine($"Loader: {(game.LoaderInstalled ? "installed" : "missing (modzarella loader)")}");
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
                        Modzarella                  open the app
                        modzarella status           game folder, loader, mod source
                        modzarella list             all mods and their state
                        modzarella install [ids]    install mods (all if none given) and the loader if missing
                        modzarella remove <ids>     uninstall mods
                        modzarella enable <ids>     turn installed mods on
                        modzarella disable <ids>    turn installed mods off
                        modzarella update           update installed mods
                        modzarella loader           install or repair BepInEx
                        modzarella unloader         remove BepInEx and every mod
                        modzarella launch           start the game with mods
                        modzarella source [url|dir] show or set the mod source
                        modzarella game [dir]       show or set the game folder
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
    }
}

# CheeseMM

The mod manager for [Cheese Rolling](https://store.steampowered.com/app/3809440/). Runs on macOS, Windows and Linux (Steam/Proton).

- Finds the game in your Steam libraries.
- Installs the BepInEx mod loader with the right setup for your platform.
- Installs, updates, enables, disables and removes mods from a [CheeseMods](../CheeseMods) catalog, with dependencies and checksums.
- Launches the game with mods.

## Use

Download `cheesemm` for your system and run it. It opens in your browser.

```
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
```

In game: **Play Offline**, then **F1** for the mod menu.

## Platforms

| | Loader | Launch |
|---|---|---|
| macOS | BepInEx macOS universal, run under Rosetta (x86_64) | `run_bepinex.sh`, Steam must be running |
| Windows | BepInEx win x64 | Steam |
| Linux (Proton) | BepInEx win x64 | Steam, with launch option `WINEDLLOVERRIDES="winhttp=n,b" %command%` |

Mods go to `BepInEx/plugins/CheeseMods/<id>/`; disabled mods move to `BepInEx/plugins-disabled/CheeseMods/<id>/`.

## Mod source

Default: `https://raw.githubusercontent.com/CheeseMods/CheeseMods/main/`. Any URL or local folder that contains an `index.json` works:

```sh
cheesemm source ~/code/CheeseMods
```

Settings live in `settings.json` in your app-data folder (`~/Library/Application Support/CheeseMM`, `%APPDATA%\CheeseMM`, `~/.config/CheeseMM`).

## Build

.NET 10 SDK.

```sh
dotnet run --project src                    # run
for rid in osx-arm64 osx-x64 win-x64 linux-x64; do
  dotnet publish src -c Release -r $rid --self-contained -p:PublishSingleFile=true -o publish/$rid
done
```

## License

MIT

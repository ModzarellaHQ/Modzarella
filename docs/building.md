# Building

## Layout

```
src/
  Modzarella/          the desktop app (.NET 10)
    Services/          finding the game, BepInEx, the mod catalog, settings
    Web/               the UI (ui.html) and the local server behind it
    Runtime/           prebuilt Core and MoonSharp DLLs, bundled into the app
  Modzarella.Core/     the in-game part: F1 menu, camera, game tweaks and the Lua engine (MoonSharp, in lib/)
scripts/package.sh     builds the downloads for each system
assets/branding/       logo, app icons and banner
```

## The app

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run --project src/Modzarella      # run it
scripts/package.sh osx-arm64             # or osx-x64, win-x64, linux-x64; output goes to dist/
```

Pushing a `v*` tag builds every system on GitHub Actions and publishes a release.

## Modzarella.Core

Core runs inside the game as a BepInEx plugin. It builds against the game's own DLLs, so you need Cheese Rolling installed with BepInEx (press Play in Modzarella once).

```sh
dotnet build src/Modzarella.Core -c Release
# or, if the game isn't in the default Steam folder:
dotnet build src/Modzarella.Core -c Release -p:GameDir="/path/to/Cheese Rolling"
```

The build writes `src/Modzarella/Runtime/Modzarella.Core.dll` and `MoonSharp.Interpreter.dll`. Commit them, because CI can't build Core without the game. The app installs them to `BepInEx/plugins/Modzarella/` every time you press Play. Lua mods are loaded from `BepInEx/plugins/Modz/<id>/`; see [Lua mods](lua-api.md).

## Command line

The app also runs from a terminal. On macOS the binary is `Modzarella.app/Contents/MacOS/Modzarella`.

| Command | |
|---|---|
| `status` | Game folder, loader and mod source |
| `list` | Every mod and its state |
| `install [ids]` | Install mods (all if none are given) |
| `remove <ids>` / `enable <ids>` / `disable <ids>` | Manage installed mods |
| `update` | Update installed mods |
| `launch` | Start the game with mods |
| `loader` / `unloader` | Install or remove BepInEx and every mod |
| `source [url\|folder]` | Show or set where mods come from |
| `game [folder]` | Show or set the game folder |

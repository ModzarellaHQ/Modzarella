# Modzarella

The mod manager for [Cheese Rolling](https://store.steampowered.com/app/3809440/) on Steam. Runs on macOS, Windows and Linux.

## Install

Download the latest file for your system from [Releases](https://github.com/ModzarellaHQ/Modzarella/releases/latest):

| System | File | First launch |
|---|---|---|
| macOS (Apple Silicon) | `Modzarella-mac-apple-silicon.zip` | Unzip, move to Applications, open. If macOS blocks it: System Settings → Privacy & Security → Open Anyway. |
| macOS (Intel) | `Modzarella-mac-intel.zip` | Same as above. |
| Windows | `Modzarella-windows.exe` | Run it. If SmartScreen warns: More info → Run anyway. |
| Linux | `Modzarella-linux.tar.gz` | Extract and run `Modzarella`. Needs `webkit2gtk-4.1`. |

Then:
1. Turn mods on with their switches.
2. Press **Play**.

Modzarella finds the game and installs the mod loader (BepInEx) for you. In game, choose **Play Offline** and press **F1** for the mod menu.

On Linux with Proton, also set this once in Steam → Cheese Rolling → Properties → Launch Options:
`WINEDLLOVERRIDES="winhttp=n,b" %command%`

## What it does

- Finds Cheese Rolling in every Steam library.
- Installs and repairs BepInEx 5, including the macOS setup.
- Installs, updates, turns on/off and uninstalls mods, with dependencies and checksums.
- Starts the game with mods. On macOS it starts Steam first if needed.

Mods come from [Modz](https://github.com/ModzarellaHQ/Modz). You can point **Settings → Mod source** at any URL or folder that has an `index.json`.

## Command line

The app also takes commands:

```
Modzarella status | list | install [ids] | remove <ids> | enable <ids> | disable <ids>
           update | loader | unloader | launch | source [url|dir] | game [dir]
```

On macOS the binary is `Modzarella.app/Contents/MacOS/Modzarella`.

## Build

Needs the .NET 10 SDK.

```sh
dotnet run --project src          # run from source
./package.sh osx-arm64            # osx-x64, win-x64, linux-x64; output in dist/
```

Pushing a `v*` tag builds every platform and publishes a GitHub release.

Settings are stored in `settings.json` in your app-data folder (`~/Library/Application Support/Modzarella`, `%APPDATA%\Modzarella`, `~/.config/Modzarella`).

## License

MIT

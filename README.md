<p align="center"><img src="assets/icon.png" width="112" alt=""></p>

<h1 align="center">Modzarella</h1>

<p align="center">
  A mod manager for <a href="https://store.steampowered.com/app/3809440/">Cheese Rolling</a>. Works on macOS, Windows and Linux.<br>
  <a href="https://modzarella.dev">modzarella.dev</a>
</p>

---

## Download

Grab the latest build from [**Releases**](https://github.com/ModzarellaHQ/Modzarella/releases/latest).

| System | File |
|---|---|
| Mac (Apple Silicon) | `Modzarella-mac-apple-silicon.zip` |
| Mac (Intel) | `Modzarella-mac-intel.zip` |
| Windows 10 / 11 | `Modzarella-windows.exe` |
| Linux | `Modzarella-linux.tar.gz` |

Not sure which Mac you have? Open  → **About This Mac**. "Apple M…" means Apple Silicon.

You'll need Cheese Rolling installed through Steam and launched at least once.

## Install

**macOS:** unzip it and drag **Modzarella.app** into Applications.
If macOS refuses to open it, go to **System Settings → Privacy & Security** and click **Open Anyway**. You only do this once.

**Windows:** run the `.exe`.
If SmartScreen appears, click **More info → Run anyway**.

**Linux:** extract the archive and run `./Modzarella`. It needs WebKitGTK 4.1 (for example `libwebkit2gtk-4.1-0` on Ubuntu). The game runs through Proton, so add this once under **Properties → Launch Options** in Steam:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## Usage

1. Open Modzarella. It finds the game on its own.
2. Switch on the mods you want.
3. Hit **Play**. The first launch also installs the mod loader, [BepInEx](https://github.com/BepInEx/BepInEx).
4. In game, pick **Play Offline** and press **F1** for the mod menu.

| To… | Do this |
|---|---|
| Turn a mod off | Flip its switch |
| Remove a mod | Click **Uninstall** under it |
| Update | Click **Update all** (it only shows when there's an update) |
| Go back to the unmodded game | **Settings → Remove all mods** |

On Mac, Steam needs to be running. Modzarella opens it if it isn't; press **Play** again once it's loaded.

## Troubleshooting

<details>
<summary><b>"Cheese Rolling wasn't found"</b></summary>
<br>

Set the folder in **Settings → Game folder**. In Steam you can find it with **right-click the game → Manage → Browse local files**.
</details>

<details>
<summary><b>The game opens without mods</b></summary>
<br>

- Launch from Modzarella, not Steam. On Windows, launching from Steam works too.
- Pick **Play Offline**. Mods don't run online.
- Try **Settings → Repair mod loader**.
- On Linux, double-check the launch option above.
</details>

<details>
<summary><b>Something broke after an update</b></summary>
<br>

Turn mods off one by one to find the culprit. Still stuck? [Open an issue](https://github.com/ModzarellaHQ/Modzarella/issues) and attach `BepInEx/LogOutput.log` from the game folder.
</details>

## FAQ

**Can I get banned?** Mods only run in Play Offline and never touch online matches.

**Is it safe?** Mods come from [Modz](https://github.com/ModzarellaHQ/Modz), and every file is checked against its checksum before it's installed.

**How do I uninstall it?** Use **Settings → Remove all mods**, then delete the app. Its settings live in `~/Library/Application Support/Modzarella`, `%APPDATA%\Modzarella` or `~/.config/Modzarella`.

## Development

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run --project src        # run from source
./package.sh osx-arm64          # or osx-x64, win-x64, linux-x64; output goes to dist/
```

Pushing a `v*` tag builds every platform and publishes a release.

<details>
<summary><b>Command line</b></summary>
<br>

```
Modzarella status | list | update | launch
Modzarella install [ids] | remove <ids> | enable <ids> | disable <ids>
Modzarella loader | unloader
Modzarella source [url|folder] | game [folder]
```

On macOS the binary is `Modzarella.app/Contents/MacOS/Modzarella`. Mods install to `<game>/BepInEx/plugins/Modz/<id>/`.
</details>

## License

MIT

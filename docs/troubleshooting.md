# Troubleshooting

## Cheese Rolling isn't found

Open **Settings → Game folder** and paste the game's folder. In Steam: **right-click Cheese Rolling → Manage → Browse local files**.

| System | Usual location |
|---|---|
| macOS | `~/Library/Application Support/Steam/steamapps/common/Cheese Rolling` |
| Windows | `C:\Program Files (x86)\Steam\steamapps\common\Cheese Rolling` |
| Linux | `~/.local/share/Steam/steamapps/common/Cheese Rolling` |

## The game opens without mods

- Start it with **Play** in Modzarella. (On Windows, starting from Steam works too.)
- Choose **Play Offline**. Mods don't run online.
- Try **Settings → Repair mod loader**.

## The game won't start (macOS)

Steam has to be open and logged in. Modzarella opens Steam if it isn't running. Wait for it to load, then press **Play** again.

## Linux

1. Install WebKitGTK 4.1 if no window opens:

   | Distro | Command |
   |---|---|
   | Debian / Ubuntu | `sudo apt install libwebkit2gtk-4.1-0` |
   | Fedora | `sudo dnf install webkit2gtk4.1` |
   | Arch | `sudo pacman -S webkit2gtk-4.1` |

2. Cheese Rolling runs through Proton. In Steam, open **Properties → Launch Options** and add:

   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```

## Crashes or odd behaviour

Turn mods off one at a time to find the cause. If that doesn't help, [open an issue](https://github.com/ModzarellaHQ/Modzarella/issues/new/choose) and attach `BepInEx/LogOutput.log` from the game folder.

## Removing everything

**Settings → Remove all mods** restores the unmodded game. Then delete the app.

Modzarella keeps its settings in one of these folders:

| System | Folder |
|---|---|
| macOS | `~/Library/Application Support/Modzarella` |
| Windows | `%APPDATA%\Modzarella` |
| Linux | `~/.config/Modzarella` |

# Changelog

## 1.3.0

- F1 menu: only "Modzarella" in the title bar
- Switching between first and third person is instant, slow motion included
- Online mods (experimental, off by default): in online lobbies where everyone has Modzarella with the same mods, mods run and can sync through the new `net` Lua API

## 1.2.3

- The F1 menu is equally see-through on every platform

## 1.2.2

- Windows and Linux: mods start once the game is running, so the F1 menu and mods work there too
- Installed mods show their name and version in the F1 menu

## 1.2.1

- Renamed mods replace their old version automatically (BMW becomes Sports Cars)
- Particle effects can be stacked on one object

## 1.2.0

- Installed and Browse tabs: browse mods to install, manage the ones you have
- Play turns into Stop while the game runs
- The app shows its version, with an About section in Settings and About Modzarella in the macOS app menu
- Lighter: the mod list is only downloaded when it changed, updates reuse unchanged files, mod models are 80% smaller, and the app uses less memory
- Security: mod ids and file paths from a mod list can't escape the mods folder, the BepInEx download is checked against its known checksum, and Lua mods can only read files inside their own folder
- Fixes: an update that fails no longer removes the mod, a broken settings file no longer stops the app, and the in-game Core no longer keeps memory from previous maps
- Square scrollbars and no bottom bar in the app

## 1.1.4

- Freecam stops cleanly when the map changes, and its key always turns it off

## 1.1.3

- Mods update automatically when the app opens and before playing (can be turned off in Settings)
- The app tells you when a new version of Modzarella is out
- Restart map (F6) and next map (F7) shortcuts
- Settings: open the game and mods folders and the game log, reset mod settings
- Blood and other marks lie on the ground instead of floating, and always appear
- Seats use generic upright and reclined poses

## 1.1.2

- Mods are downloaded from modza.space/modz; existing installs switch over automatically

## 1.1.1

- Mods are downloaded from modza.space/Modz; existing installs switch over automatically

## 1.1.0

- New look for the app, the F1 menu and the website, with colours shared in `assets/theme.css`
- F1 menu: search, collapsible mods, per-setting reset, slightly see-through
- Mods are turned on and off in the app only
- Freecam (F3) that leaves your character alone and restores the view afterwards
- First person: your body faces where you look
- New logo and icons
- The app window can no longer go fullscreen or maximize
- The app's local server only answers its own window

## 1.0.0

First release.

- Desktop app for macOS, Windows and Linux
- Finds Cheese Rolling and installs BepInEx automatically
- Turn mods on and off, update and uninstall them from one window
- Modzarella Core built in: F1 mod menu, free mouse camera, first person, zoom, endless rounds, frozen bots, slow motion
- Lua engine: mods are Lua scripts with an API for bodies, vehicles, effects, sound and the menu
- Command-line mode for scripting

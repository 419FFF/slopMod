# slopMod

**A vibecoded mod for _Bluey: The Videogame_ that gives useful information for speedrunning, plus some fun
extras.**

It adds an in-game overlay with tools to help you practice, route, and understand the game - and a few
just-for-fun options on the side. Everything is off until you switch it on, and you don't need to be a
programmer to use it: if you can copy a file and press a few keys, you're set.

> **Disclaimer:** this project is **entirely vibe-coded** - including this README. Every line of the code
> and every word of this document was written by an AI coding agent (**Cline**, running the
> **DeepSeek 4.1 Flash** model) from plain-English requests, with no human hand-writing either. It works,
> but please treat it as a fun hobby / debugging tool rather than polished, professional software. Read
> before you run it.

## Does it work on my game?

The game's code barely changed between versions `0.23.2` and `1.0.6`, so the mod should work fine on any
version in that range.

* ✅ **Tested and supported: `0.23.2`** - the exact version this build was made for.
* ✅ **Should also work fine: `0.23.2` through `1.0.6`** - these versions share basically the same code,
  so the mod is expected to work across that whole range (untested, but nothing should be broken).
* ⚠️ **`1.0.10A` and newer: this is where things start to break.** The game's code changes from this point
  on, so expect some features to stop working or misbehave.

## What it can do

Speedrunning and practice tools:

* **Info panel** (`F1`) - an on-screen readout of your progress: which episode you're on, whether a
  cutscene is playing, the sticker book, and how many pieces of trash are in the Episode 4 bag.
* **Tool window** (`F2`) - a movable window (tabs: World / Player / Episode / About) where you switch
  features on and off.
* **Episode 4 trash helper** - shows how many pieces of trash are in the bag, plus a countdown bar for the
  timing "window" that lets you throw in extra trash (a well-known time-saver).
* **Skip cutscenes** (`F4`) - instantly end the story scene that's playing. Can also auto-skip them all.
* **Practice jumps** - a new **EXTRAS** option on the main menu (and buttons in the tool window) wipes the
  save and drops you straight into a practice spot: **Episode 4 - The Creek (trash area)** or **Episode 1**.
* **Reload / restart a level** (`F7` / `F8`) - rebuild the current level, or restart into a practice area.
* **Jump around the game** (`PgUp` / `PgDn`) - skip the current checkpoint, or step back one episode phase.
  You can also edit your episode/save state in the tool window.
* **Teleports** - jump to the level start, to the next cutscene, or to wherever your free camera is looking.
* **See hidden things** (`T` / `C`) - colour-coded views of the invisible stuff in a level: triggers, solid
  collision, and character hitboxes. Handy for understanding routes and geometry.
* **Free camera** (`F6`) - detach the camera and look around freely, even during cutscenes.

Fun extras:

* **Sticker book anywhere** (`F3`) - open the sticker book without walking back to the bedroom.
* **Free the player** (`F5`) - keep moving during cutscenes and dialogs.
* **Noclip** (`F10`) - walk through walls and floors.
* **Pause time** (`F9`) - freeze the world (you can still fly the free camera around).

If you *do* want the nerdy details, see the [For developers](#for-developers) section at the bottom.

## Controls

Press these on your keyboard while playing (most of them are also buttons in the tool window):

* **F1** - show / hide the info panel
* **F2** - show / hide the tool window
* **F3** - open / close the sticker book
* **F4** - skip the current cutscene
* **F5** - free the player (lets you move during cutscenes and dialogs)
* **F6** - free camera: hold the **right mouse button** to look, **W A S D** to move, **Q / E** to go
  down / up, **Shift** to go faster, and the **mouse wheel** to change speed
* **F7** - reload the current level
* **F8** (or the controller **View / Share** button) - restart the current practice level
* **F9** - pause / resume time
* **F10** - noclip (walk through walls)
* **PgUp** - skip the current checkpoint
* **PgDn** - go back one episode step
* **T** - show / hide triggers
* **C** - show / hide solid collision

## Install

1. **Get BepInEx 6 for the game.** BepInEx is the free framework that lets mods like this one load -
   install it once and you're done (it has its own instructions; search "BepInEx 6 install").
2. **Copy `SlopMod.dll`** into the game's `BepInEx\plugins\` folder. Download it from this repo's
   **Releases** page once it's published, or build it yourself (see below).
3. **Start the game** (versions `0.23.2` through `1.0.6` are expected to work). The mod loads by itself -
   no extra setup needed.

To uninstall, just delete `SlopMod.dll` from the plugins folder.

## Build it yourself (only if you want to)

You only need this if you'd rather build the mod from source instead of downloading it. You'll need:

* the **.NET SDK** (free),
* your own copy of the **game**, and
* **BepInEx 6** installed.

Then, in this folder, run:

```
dotnet build -c Release
```

By default it looks for the game and BepInEx next to this project. If yours live somewhere else, point
it at them:

```
dotnet build -c Release -p:ManagedDir="C:\path\to\game\Project biscuits_Data\Managed" -p:BepInExCoreDir="C:\path\to\BepInEx\core"
```

The finished file lands in `bin\Release\SlopMod.dll` - copy that into `BepInEx\plugins\` as in the
Install steps above. (No game files are included in this repo, which is why you have to supply your own.)

## For developers

The short technical version, for anyone who wants to poke at the code:

* **What it is:** a **BepInEx 6 (Unity Mono)** plugin that uses **Harmony** patches and reflection. It
  never edits the game's files.
* **How it hooks in:** it patches a couple of the game's own startup methods to create the overlay and
  menu, and otherwise reads/writes game internals through cached reflection.
* **Layout:**
  * `Plugin.cs` - the entry point (loads the mod, applies the patches).
  * `Behaviour/` - the runtime pieces: `DebugOverlay` (F1/F2 + hotkeys), `ExtrasRuntime` (free player,
    auto-skip cutscenes, reload), `FreeCam`, `WorldDebugView` (colliders/triggers), `ThrowWindowIndicator`,
    and the main-menu pieces `ModMenuController` / `SlopExtrasMenu` / `SlopMenuRow`.
  * `Patches/` - the Harmony hooks that attach the above at boot.
  * `Utils/` - helpers: `EpisodeTools` (save/episode editing, noclip, cutscene, sticker book, practice),
    `TeleportTools`, `TimeTools`, and `UiKit` (reads the game's menu art).
* **Built against:** BepInEx `6.0.0-be.788` (Unity Mono), `.NET Framework 4.7.2`.
* **Settings** live in `BepInEx\config\com.slopmod.plugin.cfg` (for example `EnableDebugOverlay`,
  `AutoSkipCutscenes`, `ShowThrowWindowIndicator`).

## Credits

Made by **419FFF**. Built with **Cline** running **DeepSeek 4.1 Flash**.

This is an unofficial fan tool and is **not affiliated with the game's developers or publishers**. Use at
your own risk.

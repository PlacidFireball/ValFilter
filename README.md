# ValFilter

GitHub: https://github.com/PlacidFireball/ValFilter

Adds a search box to every crafting station (workbench, forge, cauldron, artisan table, etc.) so you can filter the recipe list by name.

## Features

- Type to filter recipes on both the **Craft** and **Upgrade** tabs.
- Optional **fuzzy search** (off by default, set `Search.Mode = Fuzzy`), fzf-style: type letters in order and skip the rest. `bzarw` finds *Bronzehead arrow*, `flkn` finds *Flint knife*. Best matches are listed first, with craftable recipes still on top.
- Fuzzy mode is **typo tolerant**: `baer`, `brnoze` and `cultivater` still find what you meant. Typo matches are listed after exact ones.
- Case-insensitive, matches the localized item name.
- Press **F** to jump into the search box, same as the build menu.
- Typing in the box doesn't trigger hotkeys (inventory close, hotbar, movement).
- Client-side only; other players don't need it installed.

## Configuration

`BepInEx/config/com.placidfireball.valfilter.cfg`

| Setting | Default | Description |
| --- | --- | --- |
| `General.ClearOnClose` | `true` | Clear the search when the crafting panel closes. |
| `General.FocusKey` | `F` | Key that jumps into the search box while the inventory is open. `None` disables it. |
| `Search.Mode` | `Substring` | `Substring`: name must contain exactly what you typed, in the game's usual order. `Fuzzy`: fzf-style matching, best matches first. |
| `Search.TypoTolerance` | `true` | Fuzzy mode: also match words of 4+ letters with a typo or two. |
| `Layout.Width` | `220` | Width of the search box. |
| `Layout.Offset` | `40, 0` | Position offset from the right edge of the Upgrade tab. |

## Changelog

- **0.2.1**
  - Optional fuzzy search with typo tolerance, ranked by match quality. Off by default; enable with `Search.Mode = Fuzzy`.
- **0.1.1**
  - Fixed lag when editing the search with many recipes unlocked.
  - Plugin GUID changed to `com.placidfireball.valfilter`; settings from 0.1.0 reset to defaults (old `com.jaredweiss.valfilter.cfg` can be deleted).
- **0.1.0** - Initial release: recipe search box.

# ValFilter

Adds a search box to every crafting station (workbench, forge, cauldron, artisan table, etc.) so you can filter the recipe list by name.

## Features

- Type to filter recipes on both the **Craft** and **Upgrade** tabs.
- Case-insensitive, matches the localized item name.
- Press **F** to jump into the search box, same as the build menu.
- Typing in the box doesn't trigger hotkeys (inventory close, hotbar, movement).
- Client-side only; other players don't need it installed.

## Configuration

`BepInEx/config/com.jaredweiss.valfilter.cfg`

| Setting | Default | Description |
| --- | --- | --- |
| `General.ClearOnClose` | `true` | Clear the search when the crafting panel closes. |
| `General.FocusKey` | `F` | Key that jumps into the search box while the inventory is open. `None` disables it. |
| `Layout.Width` | `220` | Width of the search box. |
| `Layout.Offset` | `40, 0` | Position offset from the right edge of the Upgrade tab. |

## Changelog

- **0.1.0** - Initial release: recipe search box.

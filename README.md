# Powerup Minimap

A BepInEx mod for **Sineus Arena** that displays live powerups, pickup items, and collectibles on both the minimap and the large unfolded map overlays.

## Features

- **Pulsing Colored Blips**: Clear visual indicators for different pickup types.
- **Auto-Clipping**: Automatically masks and clips blips to stay inside circular or rectangular map frames.
- **Big Map Support**: Full scaling and positioning support when unfolding the large map (default `M` key).
- **Native Game Integration**: Fully compatible with the game's built-in chest icons (prevents duplicate chest icons).
- **Categorized Blips**:
  - `+` (Green): Healing and HP potions
  - `B` (Orange): Attack/Damage/Tower buffs
  - `💣` (Red): Bombs and explosives
  - `M` (Purple): Magnets
  - `E` (Blue): Experience boosts
  - `S` (Yellow): Speed buffs
  - `?` (Grey): Generic/other drop types

## Installation

1. Make sure you have **BepInEx 5** installed for Sineus Arena.
2. Download `PowerupMinimap.dll` from the releases page (or build from source).
3. Place `PowerupMinimap.dll` into your `<Steam>/steamapps/common/Sineus Arena/BepInEx/plugins/` directory.
4. Launch the game!

## Configuration

You can customize the mod settings by editing `<Steam>/steamapps/common/Sineus Arena/BepInEx/config/com.sineusarena.powerupminimap.cfg` or using a configuration manager:

- **General**: Toggle the entire mod.
- **Appearance**: Adjust blip sizes, pulse speed, and pulse scale amplitude.
- **Colors**: Assign custom HSL/RGBA colors to each powerup category.
- **Filters**: Selectively show or hide specific category blips.

## Requirements / Development

- .NET SDK (supporting `netstandard2.1`)
- Game assemblies (e.g., `Assembly-CSharp.dll`, `UnityEngine.dll`) are resolved from the game directory.

```bash
dotnet build -c Release
```

English | [中文](README_zh.md)

# TwilightInputOverlay

A [BepInEx 5](https://docs.bepinex.dev/) plugin for **Human: Fall Flat** that adds an input-overlay HUD to your screen. It shows the movement keys, jump, play dead, and the left/right hand actions as a neat key grid anchored to the bottom-left corner, so you (or your viewers) can always see which keys you are pressing. It supports keyboard and mouse input only.

## Features

- **Key grid HUD** showing `W` / `A` / `S` / `D`, a jump bar (`—`), play dead (`Y`), and the left/right hand keys (`L` / `R`):

  ```text
  [Y] [W] [L/R]
  [A] [S] [D]
  [  —  ]
  ```

- **Reacts to your real input** — the HUD lights up based on the key/mouse bindings of each displayed key.
- **Fully customizable key layout** — add, remove and reorder rows and keys, pick each key's label and bound key (including a dual keybind like the original `L`/`R` hand key), widen a key like the jump bar, or turn a key into an empty slot that just reserves its space.
- **Live settings panel** — press `Home` to open it. Every change applies to the HUD immediately.
- **Fully customizable colors** — edit the text, border, and fill colors for both the *idle* and *pressed* states, using either hex color codes or RGBA sliders.
- **Layout controls** — show/hide the HUD, toggle the key text, adjust the X/Y offset from the bottom-left corner, scale the whole HUD, and tweak key spacing and corner radius.
- **Smooth key transitions** — keys fade between idle and pressed styles, with a configurable fade speed.
- **Localization** — comes with English and Simplified Chinese, and you can add more languages by dropping in a language file.

## Requirements

- Human: Fall Flat with [BepInEx 5](https://docs.bepinex.dev/) installed.

## Installation

1. Download the latest release: `TwilightInputOverlay-v{version}.dll`.
2. Copy the DLL into your game's `BepInEx/plugins/` folder.
3. Launch the game. The overlay appears at the bottom-left of the screen.
4. Press `Home` to open the settings panel.

## Timer settings-panel integration

If HSRTimer or TwilightTimer (the HSRTimer fork) is installed, TwilightInputOverlay automatically integrates into that timer's settings panel as an **Input Overlay** tab; in that case the standalone `Home`-key panel is not created, so open the timer's settings panel to configure the overlay. HSRTimer is preferred when both are present. Without either timer, the plugin still works normally and provides its own standalone panel.

## Usage

### Settings panel

The panel opens on a root page: **General** settings (change the settings panel key, switch the language) plus two buttons that drill into sub-pages:

- **Key Overlay** — toggle the key-grid HUD and its key text, adjust the X/Y offset, overall scale, fade speed, key spacing and corner radius, and edit the *Idle*/*Pressed* style colors (text, border, fill) with hex codes or RGBA sliders.
- **Mouse Overlay** — toggle the cursor and its region, raw mouse input, wrap and clamp behavior, and adjust the region position/size/color, cursor size, sensitivity, color, and trail max length/response.

Each sub-page has a **← Back** button at the top; the panel always opens on the root page.

Changes are applied immediately and saved automatically when you close the panel or exit the game.

### Configuration files

On first run, the plugin creates its config files under `BepInEx/config/TwilightInputOverlay/`:

- `settings.ini` — your saved settings. You can edit it while the game is closed; malformed lines are ignored.
- `lang/` — language files (`*.txt`). Drop in a new file to add your own translation.

## License

This project is released under the [MIT License](LICENSE).

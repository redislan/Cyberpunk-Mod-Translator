# Themes

Each `.css` file in this folder is one theme for Cyberpunk Mod Translator's interface.
Pick one in **Settings → Theme**. "Default (built-in)" is the tool's original look.

A theme only changes how the tool looks. It can't change what the tool does, so a broken theme can't break a translation: at worst, pick "Default" again.

**If a theme makes the tool unusable** (everything hidden, text the same color as the background): press **Ctrl+Alt+T**. The default theme comes back and is saved. Deleting the theme's `.css` file from this folder does the same.

A theme can only use files from this folder (images, fonts). It can't load anything from the Internet: the tool blocks it.

The tool comes with five themes:

| File | Look |
| --- | --- |
| `neon.css` | Night City yellow and cyan, cut corners, light scanlines |
| `corpo.css` | Black and blood red, sharp and corporate |
| `netrunner.css` | Magenta and cyan on deep violet, with a soft glow |
| `badlands.css` | Desert dust, rust and sunset orange |
| `daylight.css` | Light theme for daytime work |

## Make a theme

1. Copy one of the themes and rename the copy (`my-theme.css`). The file name is what Settings remembers.
2. Open it in a text editor (Notepad++, VS Code…) and save it as UTF-8.
3. Change the header at the top: your theme's name, your name, a short description.
4. Change the colors in `:root` (the list is below).
5. Open the tool, go to Settings → Theme, and pick your theme. It shows right away.

**See your changes:** save the file, then press **F5** in the tool (or pick the theme again in Settings). No need to recompile or restart.

## The header

The first comment of the file gives the name shown in Settings. Every line is optional.

```css
/*
  @name: My theme
  @author: Your name
  @description: One line about the look.
  @description.fr: Une ligne sur le style, en français.
*/
```

Add `.fr`, `.de`, `.pt-br`… after `@name` or `@description` to give a text for one interface language. Without `@name`, Settings shows the file name.

## The colors

The theme is loaded **after** the tool's own styles, so whatever you put in `:root` wins. You only need the lines you change: the others keep the default value.

| Variable | Used for | Default |
| --- | --- | --- |
| `--bg` | Window background | `#0d0f14` |
| `--bg-2` | Top bar, side panels, windows | `#141821` |
| `--bg-3` | Buttons, fields, cards | `#1b2030` |
| `--line` | Borders | `#2a3142` |
| `--text` | Text | `#e6e9ef` |
| `--text-dim` | Secondary text, labels | `#9aa3b5` |
| `--accent` | Main color: logo, main button, progress, selection | `#f5e14b` |
| `--accent-2` | Second color: hover, focus, tags | `#4be0f5` |
| `--on-accent` | Text on the main button | `#111` |
| `--logo-text` | Text of the CMT logo | `var(--bg)` |
| `--on-accent-2` | Text on `--accent-2` tags | `var(--bg)` |
| `--danger` | Errors | `#ff5577` |
| `--ok` | Success | `#46d483` |
| `--warn` | Warnings | `#ffb347` |
| `--todo` / `--check` / `--done` / `--code` | Line status: to translate / to check / translated / code | cyan / orange / green / grey |
| `--row-line` | Lines between rows (table, file browser, Modlist) | `#1d2230` |
| `--row-hover` | Row under the mouse | `#12161f` |
| `--row-selected` | Selected row | `#161c28` |
| `--entry-selected` | Selected file in the built-in file browser | `#1f2738` |
| `--sep-hover` | Modlist separator under the mouse | `#222941` |
| `--banner-bg` / `--banner-line` | Warning banner under the top bar | dark orange |
| `--accent-soft` / `--done-soft` / `--warn-soft` | Light tints behind boxes and badges | |
| `--overlay` / `--busy-overlay` | Background behind a window / while waiting | black, see-through |
| `--shadow` | Shadow of the messages in the corner | |
| `--radius` / `--radius-lg` | Rounded corners: small / windows and cards | `6px` / `10px` |
| `--font` / `--mono` | Text font / code font | Segoe UI / Cascadia Mono |

For a light theme, also put `color-scheme: light;` in `:root`, so scrollbars and drop-down lists turn light too. For a dark theme, `color-scheme: dark;` darkens them.

## Going further

A theme is plain CSS: any rule works, not just colors. Useful classes:

- `.topbar`, `.logo`, `.title`, `.tabs`, `.tab.on`: the top bar
- `.btn`, `.btn.primary`, `.btn.small`: buttons
- `.sidebar`, `.files`, `.file.active`, `.progress-bar`: left panel
- `.table-head`, `.row`, `.row.selected`, `.row .fr textarea`, `.status`: translation table
- `.detail`: right panel
- `.welcome-card`, `.dialog`, `.toast`, `.chip`: welcome screen, windows, messages, filters
- `.ml-sep`, `.ml-row`, `.ml-tag`: Modlist tab

The included themes show some tricks: cut corners (`clip-path`), glows (`box-shadow`), gradient backgrounds, custom scrollbars (`::-webkit-scrollbar`).

**Images and fonts:** put them in this folder, ideally in a subfolder named after your theme, and use a path relative to your `.css`:

```css
body { background: url("my-theme/background.jpg") center / cover; }
@font-face { font-family: "My Font"; src: url("my-theme/myfont.woff2"); }
```

Allowed files: `.png`, `.jpg`, `.webp`, `.gif`, `.svg`, `.avif`, `.woff`, `.woff2`, `.ttf`, `.otf`. Keep everything in this folder so the theme works without an internet connection.

## Share it

Send your theme as a pull request on GitHub, or on the Nexus Mods page of the tool, so it can ship with the next version.

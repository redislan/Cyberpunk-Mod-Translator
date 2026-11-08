# Cyberpunk Mod Translator — v0.7.1

*[Version française](README.fr.md)*

Translate Cyberpunk 2077 mods into your language without WolvenKit. Open a mod, translate its lines in a table, and the tool builds a small translation mod that loads on top of the original.

- Works with texts inside `.archive` files (onscreens and subtitles, including the ones declared in ArchiveXL `.xl` files).
- Works with plain-text mods: `.lua` (CET), `.reds` (redscript), `.yaml` (TweakXL), `.json`, `.tweak`.
- Never edits the original mod: the translation is a separate mod you install below it in MO2.
- **Modlist** tab: your MO2 list with its separators, and for each mod whether it is translated, still to translate, or has nothing to translate.
- Interface in English and French, and anyone can add a language (see [Interface languages](#interface-languages)).

Made by Sterben. MIT license.

## Requirements

- Windows 10 or 11.
- **.NET 8 Runtime** and **ASP.NET Core 8 Runtime**, from [Microsoft's .NET 8 download page](https://dotnet.microsoft.com/download/dotnet/8.0). The ".NET Desktop Runtime" alone is not enough. If something is missing, Windows shows a message with a download link when you start the tool.
- Cyberpunk 2077 installed (Steam, GOG or Epic). The tool uses the game's own compression DLL (`bin\x64\oo2ext_7_win64.dll`) and finds it automatically. That DLL is **not** shipped with the tool.
- Microsoft Edge (comes with Windows), Chrome or Brave: the interface opens in an app window of that browser.

## Install

1. Download the latest release zip.
2. Extract it anywhere. Keep all the files together: the `.exe` needs the `.dll` files and the `lang` folder next to it.
3. Start `CyberpunkModTranslator.exe`.

## Translate a mod

1. **Open a mod**: say whether it's a folder (MO2) or a `.zip` (Nexus), then pick it in the Windows "Open" window. If the MO2 "mods" folder is set in Settings, the window opens straight there.
2. **Translate** in the translation column. Everything is saved automatically, and your work comes back by itself when you reopen the same mod. When you translate a line, the still-empty lines with exactly the same English text get the same translation (you can still change them).
3. **Build the translation**: this makes `<Mod> - FR.zip` (or `- DE`, `- ES`…, depending on the target language) in the output folder (by default `Downloads\Traductions Cyberpunk`). Install it in MO2 **below** the original mod.

The rest of the interface:

- **Browse**: everywhere, it's the real Windows "Open" window, with the MO2 "mods" folder, the game folder and the output folder added as shortcuts on the left. Windows remembers the last folder for each use. If that window can't open, a built-in browser takes over (also available from the "Built-in browser" link).
- **Files** (left): filter by file, with the progress of each one.
- **Filters**: by status (To translate, To check, Already translated, Code), "Untranslated only", search in the English and the translation.
- **Details** (right): where the line comes from (archive, file, key, code line) and the tags to keep. "Copy the English" and "Restore the original" are there too.
- **⇄ linked to code**: this text is also used as an identifier in the mod's code (see [Texts that are also identifiers](#texts-that-are-also-identifiers)). Its translation is copied automatically where the code compares it. The details show where, with a box to turn it off.
- **Shortcuts**: `Ctrl+Enter` or `Alt+↓` for the next line, `Alt+↑` for the previous one, `Ctrl+S` to save, `Ctrl+F` to search.
- **Reuse a translation**: picks up the texts of an existing translation (folder or zip), for example to update a translation after a new version of the mod.
- **TSV**: export and import to work in Excel.
- **Settings**: interface language, game folder (for the compression DLL), MO2 "mods" folder (also read by the Modlist tab), output folder, target language.
- **Data**: settings and saves are in `%AppData%\CyberpunkModTranslator` (`reglages.json`, `projets\*.tsv`, `modlist-cache.json`), with one log per session in `logs\` (the last 10 are kept). Interface languages are next to the exe, in `lang\`.

## The Modlist tab

The same list as MO2 (same order, same separators, same profile), with a colour for each mod:

- **Green: translated.** A mod named `<Mod> - FR` (or `- DE`…, depending on the target language) is in MO2. The name is enough: the tool doesn't look inside it.
- **Orange: to translate.** The mod has lines to translate (or to check). The line shows how many, and how far your saved work goes (“started: 12 / 40”).
- **No colour: nothing to translate.** No text was found, or the author already translated it.

The rest of the tab:

- **Translate** (or a double-click) opens the mod in the Translation tab.
- **Filters**: by colour (click the counters), “Hide disabled mods”, search, and **Separators ▾** to hide whole separators. The ✕ on a separator hides it too, and a click on it collapses it. These choices are kept.
- **MO2 profile**: the profile active in MO2 by default; the other ones are in the list.
- **“built, not in MO2 yet”**: the translation zip is in the output folder, but no `<Mod> - FR` mod is in MO2 yet.
- The `… - FR` mods themselves are in grey (“Translation mods” filter).
- Each mod is analysed in the background, exactly like when you open it. The result is kept (`modlist-cache.json`): a mod is only analysed again when its files change, or its translation save. **Refresh** reads the MO2 list again; **Analyse all again** forgets the kept results.
- MO2's files are only read, never written. The `profiles` folder is the one next to the `mods` folder set in Settings. The active profile comes from `ModOrganizer.ini`, found next to the `mods` folder, in a neighbouring folder (portable MO2 installed apart from its mods) or in `%LocalAppData%\ModOrganizer`. Without it, the profile whose list changed last is used.

## Reporting a bug

Please give:

1. **The mod**: its name and its Nexus link.
2. **What went wrong**: a text still in English in game, a line sorted as "Code" that is real text (or the other way round), an error message…
3. **The log of that session**: Settings → "Logs folder" opens the folder with the latest log selected. Every error is written there, with its details.

## Interface languages

The interface comes in English and French. In **Settings → Interface language**, "Automatic" uses the Windows language if it exists, otherwise English.

- The texts are in `lang\en.json` and `lang\fr.json` next to the exe (the sources are in `lang\` at the root of this repository). Both are also built into the program as a fallback.
- **Add a language**: copy `en.json`, rename it with the language code (`de.json`, `es.json`, `pt-br.json`…), put the language name in `language_name` and your name in `translator`, and translate the texts on the right of each `:`. No need to recompile: the folder is read again every time Settings opens, and the "Languages folder" button takes you there. Full instructions for translators: [lang/README.md](lang/README.md).
- A line missing from a file is shown in English. Settings shows how much of each language is translated, and flags an unreadable file (an extra comma or quote mark…) instead of crashing.
- A file in the folder replaces the built-in version with the same code: you can fix a text without recompiling.
- The target language follows too: the translation column shows the language name, and the built files use its code (`<Mod> - DE.zip`, `#<Archive>_DE.archive`).
- The command line `cmt.exe` and the TSV file follow the interface language too (the Windows language, or `--ui en`). A TSV from any version or language still loads: the columns are read by position.

Want to share your language? Send it as a pull request, or post it on the Nexus Mods page of the tool.

## Build from source

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Run `build.bat`. It makes `dist\CyberpunkModTranslator.exe`, and `dist\cmt.exe` for the command line.

The interface is HTML, served by a small local server that only your PC can reach (127.0.0.1), with a random token at each start. The look is easy to change (CSS variables in `src/App/wwwroot/app.css`).

# The engine

Translates Cyberpunk 2077 mod texts without WolvenKit:

- **Texts in archives** (onscreens and subtitles, read through the ArchiveXL `.xl`). The patch is a small `#<Archive>_FR.archive` holding only the translated text files, at the same path as in the original mod. The `#` makes it load before the original.
- **Plain files**: `.lua` (CET mods), `.reds` (redscript), `.yaml` (TweakXL), `.json`, `.tweak`. The patch holds a copy of each file, at the same path, where only the translated strings change. The translation mod goes below the original mod in MO2.

Only dependency: the game's compression DLL (`bin\x64\oo2ext_7_win64.dll`), found automatically (Steam, GOG, Epic).

## Command line (`cmt.exe`)

```
cmt analyze "D:\MO2\mods\Night City Interactions"
cmt export  "D:\MO2\mods\Night City Interactions" nci.tsv
cmt build   "D:\MO2\mods\Night City Interactions" nci.tsv "D:\Translations"
```

- **analyze**: shows the texts found, the file that will be written and why, and how the plain-file strings were sorted.
- **export**: writes a TSV file. Fill in the `translation` column; line breaks are written `\n`.
- **build**: makes `<Mod> - FR.zip` (or `- DE`…, see `--lang`), to install in MO2 below the original mod.
- `--from <existing translation>` picks up the texts of an existing translation. Works with `export` and `build`, to update a translation when the mod changes.
- `--all` (with `export`) also adds the strings sorted as "code".
- `--game "D:\...\Cyberpunk 2077"` gives the game folder, if automatic detection fails.
- `--lang de-de` sets the target language (French by default), `--ui en` the language of the messages.
- The French names of earlier versions still work: `analyser`, `exporter`, `construire`, `lister`, `--depuis`, `--tout`, `--jeu`, `--langue`, `--sans-compression`.
- `--debug` writes a more detailed log (also for the app: `CyberpunkModTranslator.exe --debug`). Logs go to `%AppData%\CyberpunkModTranslator\logs\`.

The `<mod>` can be the mod's MO2 folder or the `.zip` downloaded from Nexus. `.7z` and `.rar` are not supported: use the MO2 folder.

## The TSV file

Columns (in the interface language; here in English): `#source` (the archive, or `-` for a plain file), `file`, `key`, `field`, `status`, `context`, `english`, `translation` (whatever the target language), `apply_to_code`.

The last column is only filled for texts linked to the code: `yes` (default) or `no` (`oui` / `non` also work). TSV files from v0.3, without this column, load normally, and so do TSV files written in another language: the columns are read by position.

| Status | Meaning |
|---|---|
| `to translate` | Text to translate |
| `to check` | Maybe text, maybe an identifier (a single word, a redscript `s"..."` string, a TweakXL field…). Only translate it if you're sure. |
| `already translated` | The mod's target-language file already holds a translation (made by the author). |
| `code` | Identifier, path, key, debug message. Not exported, except with `--all`. |

The `context` column shows the code line the string comes from (plain files) or the text key (archives). It helps you decide.

You can edit the TSV in Excel. The program also reads files saved again as "Text (Tab delimited)" or "Unicode Text", and cells that Excel wraps in quotes.

Don't edit the `english` column: on import, a line whose English no longer matches the mod is skipped, with a warning. This avoids writing a translation in the wrong place if the mod changed since the export.

## How the program picks the file to write (archives)

1. If the `.xl` declares a file for the target language (`fr-fr`, `de-de`…), that's the one replaced. That's the case for Night City Interactions.
2. If the `.xl` doesn't declare one, the program replaces ArchiveXL's fallback language, that is the first one declared.
3. Without a `.xl`: if the archive holds a target-language version, it's replaced; otherwise the English file is replaced.
4. Subtitle maps (`*_subtitles.json`) are followed to the real subtitle files.
5. If the target-language file already differs from the English, its lines are marked "already translated". They only go into the patch if you change them.

## How plain-file strings are sorted

- **Never text**: comments; in redscript, `n"..."`, `t"..."`, `r"..."` and `l"..."` strings; in YAML, the `$type` and `$base` fields; in JSON, the keys.
- **Code**:
  - paths (`base\...`, `r6/logs/mods`, `/MyMod/option`), file names, `snake_case`, `camelCase` and `Items.Thing`;
  - arguments of `require`, `GetMod`, `ModuleExists`, `Observe`, `SetFactStr`, `GameOptions`…;
  - log and debug messages: `print`, `spdlog`, `Log`, `LogRetreat`, `Logger.error`, redscript `ModLog`, home-made log functions (`tlog`, `dbg`, `wind_log`, `DebugLog`…). `addLog` / `pushLog` stay text: they usually fill a log shown in the mod's window;
  - what is written to a file (`f:write`, `io.write`, `WriteText`): it's often read by the mod's DLL or another script;
  - in Lua settings tables like `{'Clouds', 'Density', 'Cloud density', 0, 1, .5, 'f'}`, the single words before the label (they are keys, often written to a `.ini`), and lists of identifiers like `{'Clear', 'FewClouds', 'LightRain', …}`. A word used as such a key stays code everywhere in the file;
  - the start of a built key (`'Weather.' .. name`) and game enum values (`Enum.new('type', 'Value')`);
  - Lua patterns (`gsub`, `find`, `match`), comparisons (`== "Menu"`), table keys (`["name"]`) and enums (`Menu = 'Menu'`);
  - generated code (`Game.AddToInventory(...)`, `camera=%s mode=%s`) and ImGui identifiers (`PushID`, `OpenPopup`…);
  - a single word repeated 10 times or more in the same file (a data list), or a single word in a TweakXL file;
  - known CET libraries (`GameUI.lua`, `GameSettings.lua`, `Cron.lua`…);
  - translations already shipped in another language (`German.reds`, `de-de.json`…).
- **Text, based on where it is**:
  - ImGui labels (`ImGui.Text`, `Button`, `Checkbox`…; the part after `##` is left untouched);
  - Native Settings UI options (`addSwitch("/path", "Label", "Description")`);
  - displayed Mod Settings properties (`ModSettings.mod`, `category`, `displayName`, `description`, `displayValues`), unless the value is a translation key;
  - Codeware translation files (`this.Text("Key", "Text")`) and `localization.json` / `en-us.json` files;
  - `SetText(...)` and notifications.
- **Based on the field name**: `label`, `title`, `desc`, `tooltip`… go to "to translate"; `event`, `id`, `type`, `path`… to "code".
- A sentence stays text even if it contains `/` or `=` ("Enable/disable", "0 = off").
- Redscript strings like `s"Hello \(name)"` are kept as they are: keep the `\(...)`. A `"` typed in their translation is written `\"`.
- In YAML, a comment after a key (`Vehicle.my_car: # name`) is never text: replacing it would break the file.
- When writing, quotes and backslashes are escaped for each language. The rest of the file doesn't change by a single byte.

## Texts that are also identifiers

Some mods use the same text for display and in their code. A real example, Main Menu Backgrounds:

```
public func Categories() -> array<String> { return ["Background", "Loading screen", "Main menu"]; }   // displayed
public func Settings(category: String) -> array<CName> {
  if Equals(category, "Background") { ... }                                                           // compared
```

If only the first one is translated, `Equals(category, "Background")` no longer matches anything and the menu gets mixed up. So the program finds the code comparisons (`==`, `~=`, `!=`, `Equals`, `case`, `StrCmp`… in Lua and redscript) **in the same file** whose text is exactly a text to translate, and copies the translation there. From one file to another, the same word is often unrelated (Night City Traffic Overhaul: the "Badlands" label of a setting, and the district name that comes from the game).

- The copy only happens if **every** occurrence of that text is translated **the same way**. Otherwise the code is left alone and a warning shows where the occurrences are.
- A comparison you translated yourself is not changed.
- Each copy is listed at the end of the build.
- The copy can be turned off line by line (box in the details, or `non` in the TSV), for example when the mod compares with a value coming from the game, which stays in English.

## What is tested

- **Night City Interactions**: the generated patch (archive text + `loc.lua`) is bit-for-bit identical to the French translation on Nexus, made with WolvenKit. It was also checked in game.
- **Main Menu Backgrounds**: with the v0.3 French translation, `Equals(category, ...)` stayed in English and the settings menu got mixed up. Since v0.3.1 the translations are copied into the comparisons; the rest of the file is identical to the original translation.
- **A sample of 33 mods** (62 files: lua, reds, yaml, json, archives): "to check" strings went from 1540 to 165. 122 real texts wrongly sorted as code are now "to translate" (Mod Settings descriptions with `/` or `=`, `ImGui.Text`, Native Settings…), and 216 "to check" became "to translate". The other way round, 349 strings that were "to translate" are now sorted as code: log messages, generated code, German translations already shipped.
- **Tricky files**: comments containing quotes, Lua long strings `[[...]]`, escapes, prefixed redscript strings, YAML values containing `:`, JSON. The output files stay valid.
- **v0.6, on a real modlist of 52 mods** (about 23,000 strings): every change in the sorting was compared before/after; only the expected strings moved (debug messages, settings keys, file protocol, YAML comments). The 38 translations built from them came out byte for byte identical before and after the rewrite of the plain-file readers.
- **Damaged archives**: sizes and positions read from an archive are checked before use (index, file count, segments, unpacked size up to 1 GB). A damaged or booby-trapped archive gives a clear error instead of crashing the tool.
- **v0.7, Modlist tab**: MO2's files were read on a real instance (portable MO2 installed in a folder next to its mods, 3 profiles, 94 entries, separators with and without a colour, `modlist.txt` with Windows line breaks). The whole tab was then tested on a copy of that list: statuses, kept results, profile change, missing game DLL, damaged archive.
- **On every build**, the program reads back everything it just wrote (archive, texts, plain files) and stops if anything doesn't match.
- **Tags**: `{int_0}`, `<Rich ...>`, `%d`, `\(...)` and line breaks are compared between the English and the translation, with a warning if they differ.

## Not handled yet

- Texts stored outside localization files (`.inkwidget` interfaces, etc.).
- A Cyberpunk-style skin for the interface.

## Code layout

| File | Role |
|---|---|
| `Core/Compression.cs` | The game's Oodle DLL (and libkraken for tests outside Windows) |
| `Core/GameLocator.cs` | Finding the game folder |
| `Core/Hashing.cs` | CRC-32, CRC-64, path hashes (FNV-1a 64) |
| `Core/RedArchive.cs` | Reading `.archive` files |
| `Core/RedArchiveWriter.cs` | Writing `.archive` files (same layout as WolvenKit) |
| `Core/Cr2w.cs` | Reading and rewriting CR2W files |
| `Core/Localization.cs` | Text files (onscreens, subtitles, maps) |
| `Core/ArchiveXl.cs` | Tolerant reading of the `localization` block of `.xl` files |
| `Core/PlainText.cs` | A plain file and its strings, rewriting |
| `Core/TextClassifier.cs` | Text / code sorting of the strings |
| `Core/Scanners/` | One reader per language (`LuaScanner`, `RedsScanner`, `YamlScanner`, `JsonScanner`) on a common base (`SourceScanner`); `StringSyntax` holds the quotes and escapes of each language, `CodeContext` finds the function call around a string, `FileRules` the whole-file rules |
| `Core/ModPackage.cs` | Opening a mod (folder or zip) |
| `Core/Mo2.cs` | Reading MO2: instance, profiles, `modlist.txt` in MO2's order, separator colours (`meta.ini`) |
| `Core/TranslationProject.cs` | Analysis, TSV, reusing a translation, building the patch |
| `Core/Tsv.cs` | TSV files (including ones saved again by Excel) |
| `Core/UiText.cs` | Interface languages: reading `lang\*.json`, English fallback |
| `Core/Log.cs` | Session log (one file per launch, also sent to the Visual Studio Output window) |
| `lang/` | Language files (`en.json`, `fr.json`) and instructions for translators |
| `Cli/Program.cs` | The `cmt` command line |
| `App/Program.cs` | The app's local server, opening the window |
| `App/AppState.cs` | Settings, open project, auto-save, built-in browser |
| `App/ModlistService.cs` | Modlist tab: status of each mod, background analysis, kept results |
| `App/NativeDialog.cs` | The Windows "Open" window (direct shell call, no dependency) |
| `App/wwwroot/` | Interface (HTML, CSS, JavaScript), built into the exe |
| `assets/icon/` | The app icon (`app.ico`, the yellow CMT badge) and `make_icon.py`, the script that draws it |

The file formats were studied from WolvenKit's documentation and source code, but this code is written from scratch, without copying anything from WolvenKit (GPL). That's why it can stay under the MIT license.

Code comments are in French.

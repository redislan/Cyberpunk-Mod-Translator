# Cyberpunk Mod Translator — v0.5.0

*[Version française](README.fr.md)*

Translate Cyberpunk 2077 mods into your language without WolvenKit. Open a mod, translate its lines in a table, and the tool builds a small translation mod that loads on top of the original.

- Works with texts inside `.archive` files (onscreens and subtitles, including the ones declared in ArchiveXL `.xl` files).
- Works with plain-text mods: `.lua` (CET), `.reds` (redscript), `.yaml` (TweakXL), `.json`, `.tweak`.
- Never edits the original mod: the translation is a separate mod you install below it in MO2.
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
- **Settings**: interface language, game folder (for the compression DLL), MO2 "mods" folder, output folder, target language.
- **Data**: settings and saves are in `%AppData%\CyberpunkModTranslator` (`reglages.json`, `projets\*.tsv`, `journal.txt` if something goes wrong). Interface languages are next to the exe, in `lang\`.

## Interface languages

The interface comes in English and French. In **Settings → Interface language**, "Automatic" uses the Windows language if it exists, otherwise English.

- The texts are in `lang\en.json` and `lang\fr.json` next to the exe (the sources are in `lang\` at the root of this repository). Both are also built into the program as a fallback.
- **Add a language**: copy `en.json`, rename it with the language code (`de.json`, `es.json`, `pt-br.json`…), put the language name in `language_name` and your name in `translator`, and translate the texts on the right of each `:`. No need to recompile: the folder is read again every time Settings opens, and the "Languages folder" button takes you there. Full instructions for translators: [lang/README.md](lang/README.md).
- A line missing from a file is shown in English. Settings shows how much of each language is translated, and flags an unreadable file (an extra comma or quote mark…) instead of crashing.
- A file in the folder replaces the built-in version with the same code: you can fix a text without recompiling.
- Messages already shown for the open mod (analysis notes) stay in the old language until the mod is reopened.
- The target language follows too: the translation column shows the language name, and the built files use its code (`<Mod> - DE.zip`, `#<Archive>_DE.archive`).
- Still in French only: the command line `cmt.exe` and the TSV column names (`anglais`, `francais`…), kept as they are so older TSV files still load.

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

The command line is in French (command names included):

```
cmt analyser   "D:\MO2\mods\Night City Interactions"
cmt exporter   "D:\MO2\mods\Night City Interactions" nci.tsv
cmt construire "D:\MO2\mods\Night City Interactions" nci.tsv "D:\Traductions"
```

- **analyser** (analyse): shows the texts found, the file that will be written and why, and how the plain-file strings were sorted.
- **exporter** (export): writes a TSV file. Fill in the `francais` column; line breaks are written `\n`.
- **construire** (build): makes `<Mod> - FR.zip`, to install in MO2 below the original mod.
- `--depuis <existing translation>` picks up the texts of an existing translation. Works with `exporter` and `construire`, to update a translation when the mod changes.
- `--tout` (with `exporter`) also adds the strings sorted as "code".
- `--jeu "D:\...\Cyberpunk 2077"` gives the game folder, if automatic detection fails.

The `<mod>` can be the mod's MO2 folder or the `.zip` downloaded from Nexus. `.7z` and `.rar` are not supported: use the MO2 folder.

## The TSV file

Columns: `source` (the archive, or `-` for a plain file), `fichier` (file), `cle` (key), `champ` (field), `statut` (status), `contexte` (context), `anglais` (English), `francais` (translation, whatever the target language), `reporter_dans_le_code` (copy into code).

The last column is only filled for texts linked to the code: `oui` (yes, default) or `non` (no). TSV files from v0.3, without this column, load normally.

| Status | Meaning |
|---|---|
| `a traduire` | Text to translate |
| `a verifier` | Maybe text, maybe an identifier (a single word, a redscript `s"..."` string, a TweakXL field…). Only translate it if you're sure. |
| `deja traduit` | The mod's target-language file already holds a translation (made by the author). |
| `code` | Identifier, path, key, debug message. Not exported, except with `--tout`. |

The `contexte` column shows the code line the string comes from (plain files) or the text key (archives). It helps you decide.

You can edit the TSV in Excel. The program also reads files saved again as "Text (Tab delimited)" or "Unicode Text", and cells that Excel wraps in quotes.

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
  - log messages: `print`, `spdlog`, `Log`, `LogRetreat`, `Logger.error`…;
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
- Redscript strings like `s"Hello \(name)"` are kept as they are: keep the `\(...)`.
- When writing, quotes and backslashes are escaped for each language. The rest of the file doesn't change by a single byte.

## Texts that are also identifiers

Some mods use the same text for display and in their code. A real example, Main Menu Backgrounds:

```
public func Categories() -> array<String> { return ["Background", "Loading screen", "Main menu"]; }   // displayed
public func Settings(category: String) -> array<CName> {
  if Equals(category, "Background") { ... }                                                           // compared
```

If only the first one is translated, `Equals(category, "Background")` no longer matches anything and the menu gets mixed up. So the program finds the code comparisons (`==`, `~=`, `!=`, `Equals`, `case`, `StrCmp`… in Lua and redscript) whose text is exactly a text to translate, and copies the translation there.

- The copy only happens if **every** occurrence of that text is translated **the same way**. Otherwise the code is left alone and a warning shows where the occurrences are.
- A comparison you translated yourself is not changed.
- Each copy is listed at the end of the build.
- The copy can be turned off line by line (box in the details, or `non` in the TSV), for example when the mod compares with a value coming from the game, which stays in English.

## What is tested

- **Night City Interactions**: the generated patch (archive text + `loc.lua`) is bit-for-bit identical to the French translation on Nexus, made with WolvenKit. It was also checked in game.
- **Main Menu Backgrounds**: with the v0.3 French translation, `Equals(category, ...)` stayed in English and the settings menu got mixed up. Since v0.3.1 the translations are copied into the comparisons; the rest of the file is identical to the original translation.
- **A sample of 33 mods** (62 files: lua, reds, yaml, json, archives): "to check" strings went from 1540 to 165. 122 real texts wrongly sorted as code are now "to translate" (Mod Settings descriptions with `/` or `=`, `ImGui.Text`, Native Settings…), and 216 "to check" became "to translate". The other way round, 349 strings that were "to translate" are now sorted as code: log messages, generated code, German translations already shipped.
- **Tricky files**: comments containing quotes, Lua long strings `[[...]]`, escapes, prefixed redscript strings, YAML values containing `:`, JSON. The output files stay valid.
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
| `Core/PlainText.cs` | Plain files: reading strings, text/code sorting, rewriting |
| `Core/ModPackage.cs` | Opening a mod (folder or zip) |
| `Core/TranslationProject.cs` | Analysis, TSV, reusing a translation, building the patch |
| `Core/Tsv.cs` | TSV files (including ones saved again by Excel) |
| `Core/UiText.cs` | Interface languages: reading `lang\*.json`, English fallback |
| `lang/` | Language files (`en.json`, `fr.json`) and instructions for translators |
| `Cli/Program.cs` | The `cmt` command line |
| `App/Program.cs` | The app's local server, opening the window |
| `App/AppState.cs` | Settings, open project, auto-save, built-in browser |
| `App/NativeDialog.cs` | The Windows "Open" window (direct shell call, no dependency) |
| `App/wwwroot/` | Interface (HTML, CSS, JavaScript), built into the exe |

The file formats were studied from WolvenKit's documentation and source code, but this code is written from scratch, without copying anything from WolvenKit (GPL). That's why it can stay under the MIT license.

Code comments are in French.

# Interface languages

Each file in this folder is one language for Cyberpunk Mod Translator's interface.
English (`en.json`) and French (`fr.json`) come with the tool.

## Add a language

1. Copy `en.json`.
2. Rename the copy with the language code: `de.json`, `es.json`, `pl.json`, `pt-br.json`…
3. Open it in a text editor (Notepad++, VS Code…) and save it as UTF-8.
4. Put your language's name in `language_name` (for example `"Deutsch"`) and your name in `translator`.
5. Translate only the texts on the **right** of each `:`. Never change the keys on the left.
6. Open the tool, go to Settings → Interface language, and pick your language.

The tool reads this folder each time Settings opens: no need to recompile or restart.

## Rules

- Keep `{0}`, `{1}`… : the tool puts numbers, names or paths there. You can move them in the sentence.
- Keep `\n` (line break) and `\"` (quote mark) as they are.
- Keep game tags such as `{int_0}`, `<Rich …>`, `%d` or `\\(…)`.
- A missing line is shown in English, so a half-done file still works. Settings shows how much of the file is translated.
- If the file has a mistake (a missing comma or quote mark…), Settings says so and the tool keeps using English.

## Share it

Send your file as a pull request on GitHub, or on the Nexus Mods page of the tool, so it can ship with the next version.

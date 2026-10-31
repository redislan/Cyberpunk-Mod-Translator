# Cyberpunk Mod Translator — v0.5.0

*[English version](README.md)*

## L'appli

1. Lance `build.bat`. Il faut le SDK .NET 8. Ça produit `dist\CyberpunkModTranslator.exe`, et `dist\cmt.exe` pour la ligne de commande.
2. Double-clique `CyberpunkModTranslator.exe` (il faut le runtime .NET 8 **et** le runtime ASP.NET Core 8 ; le « .NET Desktop Runtime » seul ne suffit pas) : l'appli s'ouvre dans sa propre fenêtre (une fenêtre d'application Edge, sans barre d'adresse). Fermer la fenêtre ferme le programme.
3. **Ouvrir un mod** : dis si c'est un dossier (MO2) ou un `.zip` (Nexus), puis choisis-le dans la fenêtre « Ouvrir » de Windows. Si le dossier « mods » de MO2 est indiqué dans les Paramètres, elle s'ouvre directement dedans.
4. Traduis dans la colonne Français. Tout est enregistré automatiquement, et le travail revient tout seul quand tu rouvres le même mod. Quand tu traduis une ligne, les lignes encore vides qui ont exactement le même anglais reçoivent la même traduction (tu peux toujours les modifier).
5. **Fabriquer la trad** : produit `<Mod> - FR.zip` dans le dossier de sortie (par défaut `Téléchargements\Traductions Cyberpunk`). Installe-le dans MO2 sous le mod d'origine.

Le reste de l'appli :

- **Parcourir** : partout, c'est la vraie fenêtre « Ouvrir » de Windows. Le dossier « mods » de MO2, le dossier du jeu et le dossier de sortie y sont ajoutés en raccourcis à gauche. Windows retient le dernier dossier utilisé pour chaque usage. Si la fenêtre ne peut pas s'ouvrir, l'explorateur intégré prend le relais (il reste aussi accessible par le lien « Explorateur intégré »).
- **Fichiers** (à gauche) : filtre par fichier, avec l'avancement de chacun.
- **Filtres** : par statut (À traduire, À vérifier, Déjà traduit, Code), « Non traduites seulement », recherche dans l'anglais et le français.
- **Détails** (à droite) : d'où vient la ligne (archive, fichier, clé, ligne de code), et les balises à garder. Les boutons « Copier l'anglais » et « Remettre l'original » sont là aussi.
- **⇄ lié au code** : ce texte sert aussi d'identifiant dans le code du mod (voir plus bas). Sa traduction est reportée automatiquement là où le code le compare. Le détail de la ligne montre où, avec une case pour désactiver le report.
- **Raccourcis** : `Ctrl+Entrée` ou `Alt+↓` pour la ligne suivante, `Alt+↑` pour la précédente, `Ctrl+S` pour enregistrer, `Ctrl+F` pour rechercher.
- **Reprendre une trad** : récupère les textes d'une trad déjà faite (dossier ou zip), par exemple pour mettre une trad à jour après une nouvelle version du mod.
- **TSV** : export et import pour travailler dans Excel.
- **Paramètres** : langue de l'interface, dossier du jeu (pour la DLL de compression), dossier « mods » de MO2, dossier de sortie, langue cible.
- **Données** : réglages et sauvegardes dans `%AppData%\CyberpunkModTranslator` (`reglages.json`, `projets\*.tsv`, `journal.txt` en cas d'erreur). Les langues de l'interface sont à côté de l'exe, dans `lang\`.

## Langues de l'interface

L'interface existe en anglais et en français. Dans **Paramètres → Langue de l'interface** : « Automatique » prend la langue de Windows si elle existe, sinon l'anglais.

- Les textes sont dans `dist\lang\en.json` et `dist\lang\fr.json` (les sources sont dans `lang\` à la racine du projet). Ces deux langues sont aussi intégrées dans le programme, en secours.
- **Ajouter une langue** : copier `en.json`, le renommer avec le code de la langue (`de.json`, `es.json`, `pt-br.json`…), mettre le nom de la langue dans `language_name`, son pseudo dans `translator`, et traduire les textes à droite des `:`. Pas besoin de recompiler : le dossier est relu à chaque ouverture des Paramètres, et le bouton « Dossier des langues » y mène directement. Le mode d'emploi en anglais pour les traducteurs est dans `lang\README.md`.
- Une ligne absente d'un fichier s'affiche en anglais. Les Paramètres montrent le pourcentage traduit de chaque langue, et signalent un fichier illisible (virgule ou guillemet en trop…) sans planter.
- Un fichier `fr.json` posé dans le dossier remplace la version intégrée : on peut corriger un texte sans recompiler.
- Les messages déjà affichés pour le mod ouvert (notes de l'analyse) restent dans l'ancienne langue jusqu'à la prochaine ouverture du mod.
- La langue cible suit aussi : la colonne de traduction porte le nom de la langue, et les noms produits utilisent son code (`<Mod> - DE.zip`, `#<Archive>_DE.archive`). Pour le français, rien ne change (`- FR`, `_FR`).
- Restent en français : la ligne de commande `cmt.exe` et les noms de colonnes du TSV (`anglais`, `francais`…), gardés tels quels pour que les anciens TSV se relisent.

## Fonctionnement

L'interface est en HTML, servie par un petit serveur local accessible seulement depuis ton PC (127.0.0.1), avec un jeton aléatoire à chaque lancement. Le style se change facilement (variables CSS dans `src/App/wwwroot/app.css`) : c'est là que viendra l'habillage Cyberpunk.

# Le moteur

Traduit les textes des mods Cyberpunk 2077 sans WolvenKit :

- **Textes dans les archives** (onscreens et sous-titres lus via le `.xl` d'ArchiveXL). Le patch est une petite archive `#<Archive>_FR.archive` qui ne contient que les fichiers de texte traduits, au même chemin que dans le mod d'origine. Le `#` la fait charger avant l'original.
- **Fichiers en clair** : `.lua` (mods CET), `.reds` (redscript), `.yaml` (TweakXL), `.json`, `.tweak`. Le patch contient une copie de chaque fichier, au même chemin, où seules les chaînes traduites changent. Le mod FR se place sous le mod d'origine dans MO2.

Seule dépendance : la DLL de compression du jeu (`bin\x64\oo2ext_7_win64.dll`), trouvée automatiquement (Steam, GOG, Epic).

## Ligne de commande (`cmt.exe`)

```
cmt analyser   "D:\MO2\mods\Night City Interactions"
cmt exporter   "D:\MO2\mods\Night City Interactions" nci.tsv
cmt construire "D:\MO2\mods\Night City Interactions" nci.tsv "D:\Traductions"
```

- **analyser** : montre les textes trouvés, le fichier qui sera écrit et pourquoi, et le tri des chaînes des fichiers en clair.
- **exporter** : écrit un fichier TSV. Tu remplis la colonne `francais`, et les retours à la ligne s'écrivent `\n`.
- **construire** : fabrique `<Mod> - FR.zip`, à installer dans MO2 sous le mod d'origine.
- `--depuis <trad existante>` reprend les textes d'une traduction déjà faite. Ça marche avec `exporter` et `construire`, et sert à mettre une trad à jour quand le mod change.
- `--tout` (avec `exporter`) ajoute aussi les chaînes classées « code ».
- `--jeu "D:\...\Cyberpunk 2077"` indique le dossier du jeu, si la détection automatique échoue.

Le `<mod>` peut être le dossier du mod dans MO2 ou le `.zip` téléchargé sur Nexus. Les `.7z` et `.rar` ne sont pas gérés : utilise le dossier MO2.

## Le fichier TSV

Colonnes : `source` (l'archive, ou `-` pour un fichier en clair), `fichier`, `cle`, `champ`, `statut`, `contexte`, `anglais`, `francais`, `reporter_dans_le_code`.

La dernière colonne n'est remplie que pour les textes liés au code : `oui` (par défaut) ou `non`. Les TSV de la v0.3, sans cette colonne, se relisent normalement.

| Statut | Signification |
|---|---|
| `a traduire` | Du texte à traduire |
| `a verifier` | Peut-être du texte, peut-être un identifiant (un seul mot, chaîne `s"..."` de redscript, champ TweakXL…). Ne traduis que si tu es sûr. |
| `deja traduit` | Le fichier `fr-fr` du mod contient déjà du français (traduit par l'auteur). |
| `code` | Identifiant, chemin, clé, message de debug. Pas exporté, sauf avec `--tout`. |

La colonne `contexte` montre la ligne de code d'où vient la chaîne (fichiers en clair) ou la clé texte (archives). Elle aide à décider.

Tu peux éditer le TSV dans Excel. Le programme relit aussi les fichiers réenregistrés en « Texte (séparateur : tabulation) » ou en « Texte Unicode », et les cellules qu'Excel entoure de guillemets.

## Comment le programme choisit le fichier à écrire (archives)

1. Si le `.xl` déclare un fichier `fr-fr`, c'est celui-là qui est remplacé. C'est le cas de NCI.
2. Si le `.xl` ne déclare pas de `fr-fr`, le programme remplace la langue de repli d'ArchiveXL, c'est-à-dire la première déclarée.
3. Sans `.xl` : si l'archive contient une version `fr-fr`, il la remplace ; sinon il remplace le fichier anglais.
4. Les cartes de sous-titres (`*_subtitles.json`) sont suivies jusqu'aux vrais fichiers de sous-titres.
5. Si le fichier `fr-fr` est déjà différent de l'anglais, les lignes sont marquées « deja traduit ». Elles ne vont dans le patch que si tu les modifies.

## Comment les chaînes des fichiers en clair sont triées

- **Jamais du texte** : les commentaires ; en redscript, les chaînes `n"..."`, `t"..."`, `r"..."` et `l"..."` ; en YAML, les champs `$type` et `$base` ; en JSON, les clés.
- **Code** :
  - les chemins (`base\...`, `r6/logs/mods`, `/MonMod/option`), les noms de fichiers, `snake_case`, `camelCase` et `Items.Truc` ;
  - les arguments de `require`, `GetMod`, `ModuleExists`, `Observe`, `SetFactStr`, `GameOptions`… ;
  - les messages de journal : `print`, `spdlog`, `Log`, `LogRetreat`, `Logger.error`… ;
  - les motifs Lua (`gsub`, `find`, `match`), les comparaisons (`== "Menu"`), les clés de table (`["nom"]`) et les énumérations (`Menu = 'Menu'`) ;
  - le code généré (`Game.AddToInventory(...)`, `camera=%s mode=%s`) et les identifiants ImGui (`PushID`, `OpenPopup`…) ;
  - un mot seul répété 10 fois ou plus dans un même fichier (liste de données), ou un mot seul dans un fichier TweakXL ;
  - les bibliothèques CET connues (`GameUI.lua`, `GameSettings.lua`, `Cron.lua`…) ;
  - les traductions déjà fournies dans une autre langue (`German.reds`, `de-de.json`…).
- **Texte, d'après l'endroit** :
  - les libellés ImGui (`ImGui.Text`, `Button`, `Checkbox`… ; la partie après `##` reste intacte) ;
  - les réglages Native Settings UI (`addSwitch("/chemin", "Libellé", "Description")`) ;
  - les propriétés Mod Settings affichées (`ModSettings.mod`, `category`, `displayName`, `description`, `displayValues`), sauf si la valeur est une clé de traduction ;
  - les fichiers de traduction Codeware (`this.Text("Clé", "Texte")`) et les `localization.json` / `en-us.json` ;
  - `SetText(...)` et les notifications.
- **Selon le nom du champ** : `label`, `title`, `desc`, `tooltip`… vont vers « a traduire » ; `event`, `id`, `type`, `path`… vers « code ».
- Une phrase reste du texte même si elle contient `/` ou `=` (« Enable/disable », « 0 = off »).
- Les chaînes redscript `s"Bonjour \(nom)"` restent telles quelles : garde les `\(...)`.
- À l'écriture, guillemets et antislashs sont échappés selon le langage. Le reste du fichier ne change pas d'un octet.

## Textes qui servent aussi d'identifiants

Certains mods utilisent le même texte pour l'affichage et dans leur code. Exemple réel, Main Menu Backgrounds :

```
public func Categories() -> array<String> { return ["Background", "Loading screen", "Main menu"]; }   // affiché
public func Settings(category: String) -> array<CName> {
  if Equals(category, "Background") { ... }                                                           // comparé
```

Si on traduit seulement le premier, `Equals(category, "Background")` ne trouve plus rien et le menu se mélange. Le programme repère donc les comparaisons du code (`==`, `~=`, `!=`, `Equals`, `case`, `StrCmp`… en Lua et redscript) dont le texte est exactement un texte à traduire, et y reporte la traduction.

- Le report ne se fait que si **toutes** les occurrences de ce texte sont traduites **pareil**. Sinon le code n'est pas touché et un avertissement dit où sont les occurrences.
- Une comparaison que tu as traduite toi-même n'est pas modifiée.
- Chaque report est listé à la fin de la fabrication.
- Le report peut se désactiver ligne par ligne (case dans les détails, ou `non` dans le TSV), par exemple si le mod compare avec une valeur venant du jeu, qui reste en anglais.

## Ce qui est vérifié

- **Night City Interactions** : le patch généré (texte de l'archive + `loc.lua`) est identique au bit près à la traduction FR de Nexus, faite avec WolvenKit. Il a aussi été validé en jeu.
- **Main Menu Backgrounds** : avec la trad FR de la v0.3, `Equals(category, ...)` restait en anglais et le menu des réglages se mélangeait. La v0.3.1 reporte « Arrière-plan » et « Écran de chargement » dans les comparaisons ; le reste du fichier est identique à la trad d'origine.
- **Échantillon de 33 mods** (62 fichiers : lua, reds, yaml, json, archives) : les chaînes « à vérifier » sont passées de 1540 à 165. 122 vrais textes rangés à tort dans le code sont maintenant à traduire (descriptions Mod Settings avec `/` ou `=`, `ImGui.Text`, Native Settings…), et 216 « à vérifier » sont devenus « à traduire ». Dans l'autre sens, 349 chaînes qui étaient « à traduire » sont maintenant rangées dans le code : messages de journal, code généré, traductions allemandes déjà fournies.
- **Fichiers piégés** : commentaires contenant des guillemets, chaînes longues `[[...]]` en Lua, échappements, chaînes préfixées redscript, valeurs YAML contenant `:`, JSON. Les fichiers produits restent valides.
- **À chaque construction**, le programme relit tout ce qu'il vient d'écrire (archive, textes, fichiers en clair) et s'arrête si quelque chose ne correspond pas.
- **Balises** : `{int_0}`, `<Rich ...>`, `%d`, `\(...)` et les retours à la ligne sont comparés entre l'anglais et le français, avec un avertissement si ça diffère.

## Pas encore géré

- Les textes écrits ailleurs que dans les fichiers de localisation (interfaces `.inkwidget`, etc.).
- L'habillage Cyberpunk de l'interface.

## Organisation du code

| Fichier | Rôle |
|---|---|
| `Core/Compression.cs` | DLL Oodle du jeu (et libkraken pour les tests hors Windows) |
| `Core/GameLocator.cs` | Recherche du dossier du jeu |
| `Core/Hashing.cs` | CRC-32, CRC-64, hash des chemins (FNV-1a 64) |
| `Core/RedArchive.cs` | Lecture des `.archive` |
| `Core/RedArchiveWriter.cs` | Écriture des `.archive` (même disposition que WolvenKit) |
| `Core/Cr2w.cs` | Lecture et réécriture des fichiers CR2W |
| `Core/Localization.cs` | Fichiers de textes (onscreens, sous-titres, cartes) |
| `Core/ArchiveXl.cs` | Lecture tolérante du bloc `localization` des `.xl` |
| `Core/PlainText.cs` | Fichiers en clair : lecture des chaînes, tri texte/code, réécriture |
| `Core/ModPackage.cs` | Ouverture d'un mod (dossier ou zip) |
| `Core/TranslationProject.cs` | Analyse, TSV, reprise d'une trad, construction du patch |
| `Core/Tsv.cs` | Fichiers TSV (y compris réenregistrés par Excel) |
| `Core/UiText.cs` | Langues de l'interface : lecture des `lang\*.json`, repli sur l'anglais |
| `lang/` | Fichiers de langue (`en.json`, `fr.json`) et mode d'emploi pour les traducteurs |
| `Cli/Program.cs` | Ligne de commande `cmt` |
| `App/Program.cs` | Serveur local de l'appli, ouverture de la fenêtre |
| `App/AppState.cs` | Réglages, projet ouvert, sauvegarde auto, explorateur intégré |
| `App/NativeDialog.cs` | Fenêtre « Ouvrir » de Windows (appel direct au shell, sans dépendance) |
| `App/wwwroot/` | Interface (HTML, CSS, JavaScript), intégrée dans l'exe |

Le format a été étudié dans la doc et le code source de WolvenKit, mais le code est écrit de zéro, sans rien copier de WolvenKit (sous licence GPL). Il peut donc rester sous licence MIT.

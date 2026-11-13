using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using CyberpunkModTranslator.Core;
using CyberpunkModTranslator.Tests;

// -------------------------------------------------------------------------------------------------
// Tests du moteur. Chaque test fabrique un faux mod dans un dossier temporaire, le fait passer par le
// moteur et verifie le resultat. A lancer avant chaque version : test.bat (ou "dotnet run" ici).
// Code de sortie : 0 si tout passe, 1 sinon.
// -------------------------------------------------------------------------------------------------

var root = Path.Combine(Path.GetTempPath(), "cmt-tests-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(root);
Log.Start("cmt-tests", Path.Combine(root, "logs"));
UiText.Use("en");
var comp = new NoCompressor();
int passed = 0, failed = 0;

void Test(string name, Action body)
{
    try
    {
        body();
        passed++;
        Console.WriteLine("  ok     " + name);
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine("  ECHEC  " + name);
        Console.WriteLine("         " + ex.GetType().Name + " : " + ex.Message.Replace("\n", "\n         "));
    }
}

void Check(bool condition, string what)
{
    if (!condition) throw new InvalidOperationException("attendu : " + what);
}

T Throws<T>(Action action, string what) where T : Exception
{
    try
    {
        action();
    }
    catch (T ex)
    {
        return ex;
    }
    throw new InvalidOperationException("une erreur etait attendue : " + what);
}

string Dir(params string[] parts)
{
    var d = Path.Combine(new[] { root }.Concat(parts).ToArray());
    Directory.CreateDirectory(d);
    return d;
}

void Archive(string path, params (string Path, byte[] Data)[] files) => RedArchiveWriter.Write(path, files, null);

string SimpleMod(string test, string name = "MyMod", string archive = "mymod.archive")
{
    var mod = Dir(test, name);
    Archive(Path.Combine(mod, "archive", "pc", "mod", archive),
        (@"mod\localization\en-us\onscreens\mymod.json", FakeLoc.OnScreens((0, "MyMod-Title", "Hello world"), (0, "MyMod-Desc", "A description"))));
    return mod;
}

void Text(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
}

Console.WriteLine("Fabrication");

Test("cycle complet : analyse, traduction, fabrication, relecture du patch", () =>
{
    var mod = SimpleMod("cycle");
    using var p = TranslationProject.Scan(mod, comp);
    Check(p.Units.Count == 2, "2 lignes");
    p.Units[0].Translation = "Bonjour le monde";
    var r = p.Build(Dir("cycle", "out"), null);
    Check(r.Archives.Count == 1 && File.Exists(r.Archives[0]), "une archive fabriquee");
    Check(Path.GetFileName(r.Archives[0]) == "#mymod_FR.archive", "nom #mymod_FR.archive");
    Check(r.Zip != null && File.Exists(r.Zip), "un zip");
    using var ar = RedArchive.Open(r.Archives[0], comp);
    var loc = LocalizationFile.TryLoad(ar.ReadMain(ar.Files[0]))!;
    Check(loc.ByKey["MyMod-Title"].Get("F") == "Bonjour le monde", "la traduction est dans le patch");
    Check(loc.ByKey["MyMod-Desc"].Get("F") == "A description", "la ligne non traduite reste en anglais");
});

Test("nom de sortie '..' ou '.' : refuse, rien n'est efface", () =>
{
    var mod = SimpleMod("dotdot");
    var parent = Dir("dotdot", "Downloads");
    var output = Dir("dotdot", "Downloads", "Translations");
    Text(Path.Combine(parent, "important.txt"), "x");
    Text(Path.Combine(output, "old.zip"), "x");
    using var p = TranslationProject.Scan(mod, comp);
    p.Units[0].Translation = "Bonjour";
    Throws<InvalidOperationException>(() => p.Build(output, null, false, ".."), "nom '..'");
    Throws<InvalidOperationException>(() => p.Build(output, null, false, "."), "nom '.'");
    Check(File.Exists(Path.Combine(parent, "important.txt")), "le dossier parent est intact");
    Check(File.Exists(Path.Combine(output, "old.zip")), "le dossier de sortie est intact");
});

Test("dossier du meme nom qui ne vient pas de l'outil : renomme, pas efface", () =>
{
    var mods = Dir("foreign", "mods");
    var mod = SimpleMod(Path.Combine("foreign", "mods"));
    var other = Dir("foreign", "mods", "MyMod - FR");
    Text(Path.Combine(other, "handmade.archive"), "x");
    using var p = TranslationProject.Scan(mod, comp);
    p.Units[0].Translation = "Bonjour";
    var r = p.Build(mods, null, false);
    var kept = Directory.GetDirectories(mods, "MyMod - FR.old-*");
    Check(kept.Length == 1 && File.Exists(Path.Combine(kept[0], "handmade.archive")), "l'ancien dossier est garde sous un autre nom");
    Check(r.Warnings.Count == 1, "un avertissement le signale");
    Check(File.Exists(Path.Combine(other, TranslationProject.MarkerFile)), "la nouvelle traduction porte le marqueur");
});

Test("nom de sortie = mod d'origine : refuse, le mod d'origine est intact", () =>
{
    var mods = Dir("oversource", "mods");
    var mod = SimpleMod(Path.Combine("oversource", "mods"));
    using var p = TranslationProject.Scan(mod, comp);
    p.Units[0].Translation = "Bonjour";
    Throws<InvalidOperationException>(() => p.Build(mods, null, false, "MyMod"), "sortie sur le mod d'origine");
    Throws<InvalidOperationException>(() => p.Build(mod, null, false), "sortie dans le mod d'origine");
    Check(File.Exists(Path.Combine(mod, "archive", "pc", "mod", "mymod.archive")), "archive d'origine intacte");
});

Test("refabriquer : l'ancienne traduction de l'outil est remplacee sans avertissement", () =>
{
    var mod = SimpleMod("rebuild");
    var output = Dir("rebuild", "out");
    using var p = TranslationProject.Scan(mod, comp);
    p.Units[0].Translation = "Bonjour";
    p.Build(output, null);
    p.Units[0].Translation = "Salut";
    var r = p.Build(output, null);
    Check(r.Warnings.Count == 0, "pas d'avertissement");
    Check(Directory.GetFileSystemEntries(output).Length == 2, "un dossier et un zip, rien d'autre");
});

Test("fabrication qui echoue : pas de dossier de travail laisse, ancienne traduction intacte", () =>
{
    var mod = Dir("failing", "MyMod");
    Text(Path.Combine(mod, "bin", "init.lua"), "ImGui.Text(\"A full sentence here\")\n");
    var output = Dir("failing", "out");
    using var p = TranslationProject.Scan(mod, comp);
    p.Units[0].Translation = "Bonjour";
    p.Build(output, null, false);
    // un fichier a la place du dossier d'un fichier en clair fait echouer l'ecriture
    var previous = Path.Combine(output, "MyMod - FR", "bin", "init.lua");
    var blocker = Path.Combine(output, "blocked");
    Text(blocker, "x");
    Throws<Exception>(() => p.Build(blocker, null, false), "dossier de sortie impossible");
    Check(File.Exists(previous), "ancienne traduction intacte");
    Check(Directory.GetDirectories(output, ".cmt-*").Length == 0, "aucun dossier de travail");
});

Test("fichier sans nom connu (archive sans liste de chemins) : le patch garde le meme hash", () =>
{
    var mod = Dir("nolxrs", "MyMod");
    var archive = Path.Combine(mod, "archive", "pc", "mod", "m.archive");
    Archive(archive, (@"base\localization\en-us\onscreens\onscreens.json", FakeLoc.OnScreens((0, "K", "Hello world"))));
    var bytes = File.ReadAllBytes(archive);
    BitConverter.GetBytes(0u).CopyTo(bytes, 0x28); // plus de liste de chemins, comme une archive de fichiers d'origine du jeu
    File.WriteAllBytes(archive, bytes);
    using var p = TranslationProject.Scan(mod, comp);
    Check(p.Units.Count == 1, "le fichier de textes est trouve par son contenu");
    p.Units[0].Translation = "Bonjour le monde";
    var r = p.Build(Dir("nolxrs", "out"), null, false);
    using var original = RedArchive.Open(archive, comp);
    using var patch = RedArchive.Open(r.Archives[0], comp);
    Check(patch.Files[0].Hash == original.Files[0].Hash, "meme hash que dans l'archive d'origine");
    Check(patch.CustomPaths.Count == 0, "pas de faux chemin dans la liste");
});

Test("deux archives du meme nom (options A / B) : sauvegarde relue, deux patchs fabriques", () =>
{
    var mod = Dir("options", "MyMod");
    Archive(Path.Combine(mod, "Option A", "archive", "pc", "mod", "mymod.archive"), (@"mod\localization\en-us\a.json", FakeLoc.OnScreens((0, "K", "Hello A"))));
    Archive(Path.Combine(mod, "Option B", "archive", "pc", "mod", "mymod.archive"), (@"mod\localization\en-us\a.json", FakeLoc.OnScreens((0, "K", "Hello B"))));
    var tsv = Path.Combine(Dir("options"), "save.tsv");
    using (var p = TranslationProject.Scan(mod, comp))
    {
        Check(p.Units.Count == 2, "2 lignes");
        p.Units.Single(u => u.Source == "Hello A").Translation = "Bonjour A";
        p.Units.Single(u => u.Source == "Hello B").Translation = "Bonjour B";
        p.ExportTsv(tsv, true);
    }
    using (var p = TranslationProject.Scan(mod, comp))
    {
        var (applied, warnings) = p.ImportTsv(tsv);
        Check(applied == 2 && warnings.Count == 0, "2 traductions relues");
        Check(p.Units.Single(u => u.Source == "Hello B").Translation == "Bonjour B", "chaque traduction dans la bonne archive");
        var r = p.Build(Dir("options", "out"), null, false);
        Check(r.Archives.Count == 2, "2 patchs");
        Check(r.Archives.Any(a => a.Replace('\\', '/').Contains("Option A/archive/pc/mod/")) &&
              r.Archives.Any(a => a.Replace('\\', '/').Contains("Option B/archive/pc/mod/")), "chaque patch dans son dossier d'option");
    }
});

Test("deux lignes avec la meme cle : les deux sont traduisibles, la sauvegarde se relit", () =>
{
    var mod = Dir("dupkeys", "MyMod");
    Archive(Path.Combine(mod, "archive", "pc", "mod", "m.archive"),
        (@"mod\localization\en-us\a.json", FakeLoc.OnScreens((0, "Same", "Hello"), (0, "Same", "World"))));
    var tsv = Path.Combine(Dir("dupkeys"), "save.tsv");
    using var p = TranslationProject.Scan(mod, comp);
    Check(p.Units.Count == 2 && p.Units[0].Key != p.Units[1].Key, "2 lignes, 2 cles distinctes");
    p.Units[0].Translation = "Bonjour";
    p.Units[1].Translation = "Monde";
    p.ExportTsv(tsv, true);
    var (applied, warnings) = p.ImportTsv(tsv);
    Check(applied == 2 && warnings.Count == 0, "sauvegarde relue");
    var r = p.Build(Dir("dupkeys", "out"), null, false);
    using var ar = RedArchive.Open(r.Archives[0], comp);
    var loc = LocalizationFile.TryLoad(ar.ReadMain(ar.Files[0]))!;
    Check(loc.Entries[0].Get("F") == "Bonjour" && loc.Entries[1].Get("F") == "Monde", "les deux lignes sont traduites");
});

Test("zip avec un dossier englobant : la traduction commence aux dossiers du jeu", () =>
{
    var src = Dir("wrapper", "src", "MyMod v1.2");
    Archive(Path.Combine(src, "archive", "pc", "mod", "mymod.archive"), (@"mod\localization\en-us\a.json", FakeLoc.OnScreens((0, "K", "Hello world"))));
    Text(Path.Combine(src, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "mymod", "init.lua"), "ImGui.Text(\"A full sentence here\")\n");
    Text(Path.Combine(src, "textures", "big.dds"), new string('x', 5000));
    var zip = Path.Combine(Dir("wrapper"), "MyMod.zip");
    ZipFile.CreateFromDirectory(Dir("wrapper", "src"), zip);
    using var p = TranslationProject.Scan(zip, comp);
    Check(!p.Mod.Files.Any(f => f.EndsWith(".dds")), "les fichiers inutiles ne sont pas sortis du zip");
    foreach (var u in p.Units.Where(u => u.Status == UnitStatus.ToTranslate)) u.Translation = "FR";
    var output = Dir("wrapper", "out");
    p.Build(output, null, false);
    var made = Path.Combine(output, "MyMod - FR");
    Check(File.Exists(Path.Combine(made, "archive", "pc", "mod", "#mymod_FR.archive")), "archive dans archive/pc/mod");
    Check(File.Exists(Path.Combine(made, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "mymod", "init.lua")), "fichier en clair dans bin/..., sans le dossier englobant");
});

Console.WriteLine("Analyse");

Test("archive abimee, vide ou coupee : signalee, le reste du mod est analyse", () =>
{
    foreach (var bad in new[] { Array.Empty<byte>(), Enumerable.Range(0, 5000).Select(i => (byte)(i * 31)).ToArray(), new byte[] { 0x52, 0x44, 0x41, 0x52, 12, 0, 0, 0 } })
    {
        var mod = SimpleMod(Path.Combine("badarchive", bad.Length.ToString()));
        File.WriteAllBytes(Path.Combine(mod, "archive", "pc", "mod", "broken.archive"), bad);
        using var p = TranslationProject.Scan(mod, comp);
        Check(p.Units.Count == 2, "les lignes de l'archive saine sont la");
        Check(p.FailedArchives.Count == 1 && p.Notes.Count == 1, "l'archive abimee est signalee");
    }
});

Test(".xl qui ne porte pas le nom de l'archive : il est lu", () =>
{
    var mod = Dir("xlname", "MyMod");
    var dir = Path.Combine(mod, "archive", "pc", "mod");
    Archive(Path.Combine(dir, "mymod.archive"),
        (@"mymod\texts\english.json", FakeLoc.OnScreens((0, "K", "Hello world"))),
        (@"mymod\texts\french.json", FakeLoc.OnScreens((0, "K", "Hello world"))));
    Text(Path.Combine(dir, "mymod_settings.xl"), "localization:\n  onscreens:\n    en-us: mymod\\texts\\english.json\n    fr-fr: mymod\\texts\\french.json\n");
    using var p = TranslationProject.Scan(mod, comp);
    Check(p.Targets.Count == 1 && p.Targets[0].TargetPath.EndsWith("french.json"), "la cible est le fichier fr-fr declare dans le .xl");
});

Test("gros JSON (40 000 valeurs) : analyse en moins de 5 secondes", () =>
{
    var sb = new StringBuilder("{\n");
    for (var i = 0; i < 40000; i++) sb.Append($"  \"k{i}\": \"Word{Convert.ToString(i, 16)}x\",\n".Replace("Word", "W" + (char)('a' + i % 26)));
    sb.Append("  \"end\": \"x\"\n}\n");
    var clock = Stopwatch.StartNew();
    var file = PlainTextFile.Parse("data/items.json", sb.ToString());
    Check(file.Literals.Count == 40001, "toutes les valeurs sont lues");
    Check(clock.Elapsed.TotalSeconds < 5, $"moins de 5 s (mesure : {clock.Elapsed.TotalSeconds:0.0} s)");
});

Test("nom d'archive qui commence par un espace : un nom de patch est trouve (pas de boucle sans fin)", () =>
{
    Check(TranslationProject.PatchArchiveName("mymod.archive") == "#mymod_FR.archive", "nom habituel inchange");
    Check(TranslationProject.PatchArchiveName("!!!mymod.archive") == "!!!#!!!mymod_FR.archive", "nom avec ! inchange");
    foreach (var name in new[] { " mymod.archive", "! mymod.archive", "#mymod.archive", "0mod.archive", "Zmod.archive" })
    {
        var task = Task.Run(() => TranslationProject.PatchArchiveName(name));
        Check(task.Wait(2000), $"reponse pour '{name}'");
        Check(string.CompareOrdinal(task.Result, name) < 0, $"'{task.Result}' se charge avant '{name}'");
    }
});

Test("traduction faite seulement d'espaces : ne compte pas", () =>
{
    var mod = SimpleMod("spaces");
    using var p = TranslationProject.Scan(mod, comp);
    p.Units[0].Translation = "   ";
    Check(!p.Units[0].HasChange, "pas comptee comme traduite");
});

Console.WriteLine("Sauvegarde et TSV");

Test("mod mis a jour (une chaine ajoutee en haut d'un .lua) : les traductions sont retrouvees", () =>
{
    const string lua = "ImGui.Text(\"First sentence here\")\nImGui.Text(\"Second sentence here\")\nImGui.Text(\"Third sentence here\")\n";
    var mod = Dir("update", "MyMod");
    var file = Path.Combine(mod, "bin", "init.lua");
    Text(file, lua);
    var tsv = Path.Combine(Dir("update"), "save.tsv");
    using (var p = TranslationProject.Scan(mod, comp))
    {
        foreach (var u in p.Units) u.Translation = "FR " + u.Source;
        p.ExportTsv(tsv, true);
    }
    Text(file, "ImGui.Text(\"Brand new sentence\")\n" + lua);
    using (var p = TranslationProject.Scan(mod, comp))
    {
        var (applied, warnings) = p.ImportTsv(tsv);
        Check(applied == 3 && warnings.Count == 0, $"3 traductions retrouvees (mesure : {applied}, {warnings.Count} refusee(s))");
        Check(p.Units.Single(u => u.Source == "Third sentence here").Translation == "FR Third sentence here", "chaque traduction sur le bon texte");
        Check(p.Units.Single(u => u.Source == "Brand new sentence").Translation == null, "la nouvelle chaine reste a traduire");
    }
});

Test("texte anglais modifie par l'auteur : la ligne est signalee, pas appliquee au mauvais texte", () =>
{
    var mod = Dir("changed", "MyMod");
    var file = Path.Combine(mod, "bin", "init.lua");
    Text(file, "ImGui.Text(\"First sentence here\")\n");
    var tsv = Path.Combine(Dir("changed"), "save.tsv");
    using (var p = TranslationProject.Scan(mod, comp))
    {
        p.Units[0].Translation = "Premiere phrase";
        p.ExportTsv(tsv, true);
    }
    Text(file, "ImGui.Text(\"A completely different sentence\")\n");
    using (var p = TranslationProject.Scan(mod, comp))
    {
        var (applied, warnings) = p.ImportTsv(tsv);
        Check(applied == 0 && warnings.Count == 1 && p.Units[0].Translation == null, "0 appliquee, 1 signalee");
    }
});

Test("cle numerique longue : protegee d'Excel, et retrouvee meme si Excel l'a abimee", () =>
{
    var mod = Dir("excel", "MyMod");
    Archive(Path.Combine(mod, "archive", "pc", "mod", "m.archive"),
        (@"mod\localization\en-us\a.json", FakeLoc.OnScreens((12345678901234567UL, "", "Hello world"))));
    var tsv = Path.Combine(Dir("excel"), "export.tsv");
    using var p = TranslationProject.Scan(mod, comp);
    p.Units[0].Translation = "Bonjour";
    p.ExportTsv(tsv);
    var text = File.ReadAllText(tsv);
    Check(text.Contains("\tk:12345678901234567\t"), "cle ecrite k:12345678901234567");
    p.Units[0].Translation = null;
    Check(p.ImportTsv(tsv).Applied == 1 && p.Units[0].Translation == "Bonjour", "relue avec le prefixe");
    File.WriteAllText(tsv, text.Replace("k:12345678901234567", "1,23457E+16"));
    p.Units[0].Translation = null;
    Check(p.ImportTsv(tsv).Applied == 1 && p.Units[0].Translation == "Bonjour", "retrouvee par le texte anglais");
});

Console.WriteLine("Fichiers en clair");

Test("fichier en Latin-1 et traduction avec « œ » : fabrique (fichier passe en UTF-8)", () =>
{
    var mod = Dir("latin1", "MyMod");
    Directory.CreateDirectory(Path.Combine(mod, "bin"));
    File.WriteAllBytes(Path.Combine(mod, "bin", "init.lua"), Encoding.Latin1.GetBytes("-- café\nImGui.Text(\"The heart of the city\")\n"));
    using var p = TranslationProject.Scan(mod, comp);
    p.Units.Single(u => u.Source.StartsWith("The heart")).Translation = "Le cœur de la ville";
    var r = p.Build(Dir("latin1", "out"), null, false);
    var written = File.ReadAllText(r.PlainFiles[0], new UTF8Encoding(false, true));
    Check(written.Contains("Le cœur de la ville") && written.Contains("café"), "texte et commentaire corrects en UTF-8");
    Check(r.Warnings.Count == 1, "le changement d'encodage est signale");
});

Test("YAML : une valeur qui continue sur la ligne suivante n'est pas touchee", () =>
{
    var yaml = "Items.Foo:\n  folded: this value continues\n    on the next line here\n  plain: A plain value here\n  list:\n    - name: First item here\n      desc: Second value here\n";
    var file = PlainTextFile.Parse("r6/tweaks/a.yaml", yaml);
    var values = file.Literals.Select(l => l.Value).ToList();
    Check(!values.Contains("this value continues"), "la valeur sur deux lignes est laissee");
    Check(values.Contains("A plain value here") && values.Contains("First item here") && values.Contains("Second value here"), "les autres valeurs sont lues");
});

Test("Lua : accents ecrits octet par octet (\\195\\169, \\xC3\\xA9) lus comme « é »", () =>
{
    var file = PlainTextFile.Parse("init.lua", "ImGui.Text(\"Caf\\195\\169 is open today\")\nImGui.Text(\"Caf\\xC3\\xA9 is closed today\")\n");
    Check(file.Literals[0].Value == "Café is open today", "\\195\\169 = é");
    Check(file.Literals[1].Value == "Café is closed today", "\\xC3\\xA9 = é");
});

Test("textes pieges (guillemets, antislash, retours a la ligne) : relus a l'identique apres fabrication", () =>
{
    var mod = Dir("tricky", "MyMod");
    Text(Path.Combine(mod, "bin", "init.lua"), "local t = { label = 'Enable the feature', tooltip = \"It's a \\\"good\\\" idea\" }\nImGui.Text([[A long bracket sentence]])\n");
    Text(Path.Combine(mod, "r6", "scripts", "a.reds"), "let a = \"A plain sentence here\";\nlet b = s\"You have \\(count) items left\";\n");
    Text(Path.Combine(mod, "r6", "tweaks", "a.yaml"), "Items.Foo:\n  desc: 'It''s single quoted here'\n  text: \"Double quoted here\"\n");
    Text(Path.Combine(mod, "bin", "config.json"), "{ \"label\": \"A json sentence here\" }\n");
    var tricky = new[] { "Ligne 1\nLigne 2 \"guillemets\" et \\ antislash", "C'est l'été ]] fin {0} %s", "Tab\tici 'apostrophes' \"doubles\"" };
    using var p = TranslationProject.Scan(mod, comp);
    var k = 0;
    foreach (var u in p.Units.Where(u => u.Status != UnitStatus.Code)) u.Translation = tricky[k++ % tricky.Length];
    var r = p.Build(Dir("tricky", "out"), null, false); // la fabrication relit chaque fichier et compare chaque chaine
    Check(r.PlainFiles.Count == 4, "4 fichiers fabriques et verifies");
});

Test("redscript : une chaine dans une expression \\(...) ne coupe pas la chaine interpolee", () =>
{
    var mod = Dir("interp", "MyMod");
    Text(Path.Combine(mod, "r6", "scripts", "a.reds"), "let a = s\"Price: \\(GetLocalizedText(\"Common-Price\")) for \\(count) items\";\nlet b = \"After the interpolated one\";\n");
    using var p = TranslationProject.Scan(mod, comp);
    Check(p.Units.Count == 2, $"2 chaines (mesure : {p.Units.Count})");
    Check(p.Units[0].Source == "Price: \\(GetLocalizedText(\"Common-Price\")) for \\(count) items", "la chaine interpolee est lue en entier");
    p.Units[0].Translation = "Prix : \\(GetLocalizedText(\"Common-Price\")) pour \\(count) \"objets\"";
    var r = p.Build(Dir("interp", "out"), null, false);
    var written = File.ReadAllText(r.PlainFiles[0]);
    Check(written.Contains("s\"Prix : \\(GetLocalizedText(\"Common-Price\")) pour \\(count) \\\"objets\\\"\";"), "expression intacte, guillemets du texte echappes : " + written);
});

Console.WriteLine("Tri texte / code");

void Kind(string file, string code, string value, StringKind expected)
{
    var lit = PlainTextFile.Parse(file, code).Literals.FirstOrDefault(l => l.Value == value);
    Check(lit != null, $"chaine trouvee : {value}");
    Check(lit!.Kind == expected, $"« {value} » classee {expected} (mesure : {lit.Kind})");
}

Test("requetes SQL, modeles sans mot, listes de colonnes, hex, chemins : du code", () =>
{
    Kind("init.lua", "local q = \"SELECT * FROM entities WHERE entity_name = '%s'\"\n", "SELECT * FROM entities WHERE entity_name = '%s'", StringKind.Code);
    Kind("init.lua", "local q = \" ORDER BY anim_name ASC\"\n", " ORDER BY anim_name ASC", StringKind.Code);
    Kind("init.lua", "local q = \"AND entity_id IN %s \"\n", "AND entity_id IN %s ", StringKind.Code);
    Kind("init.lua", "local q = \"DELETE FROM favorites\"\n", "DELETE FROM favorites", StringKind.Code);
    Kind("init.lua", "local c = '(entity_id, entity_name, cat_id, parameters)'\n", "(entity_id, entity_name, cat_id, parameters)", StringKind.Code);
    Kind("init.lua", "local s = f(\"{x = %f, y = %f, z = %f}\", a, b, c)\n", "{x = %f, y = %f, z = %f}", StringKind.Code);
    Kind("init.lua", "local id = '0x3B6EF8F9, 13'\n", "0x3B6EF8F9, 13", StringKind.Code);
    Kind("init.lua", "local d = \"./Collabs/Custom Poses\"\n", "./Collabs/Custom Poses", StringKind.Code);
    Kind("a.reds", "this.total.SetText(\"{label} {price}{dollar}\", p);\n", "{label} {price}{dollar}", StringKind.Code);
    Kind("a.reds", "@runtimeProperty(\"ModSettings.displayName\", \"Gameplay-Items-Item Type-Cyb_Launcher\")\n", "Gameplay-Items-Item Type-Cyb_Launcher", StringKind.Code);
});

Test("... mais le vrai texte qui leur ressemble reste a traduire", () =>
{
    Kind("init.lua", "ImGui.Text(\"SELECT A CHARACTER\")\n", "SELECT A CHARACTER", StringKind.Text);
    Kind("init.lua", "ImGui.Text(\"Delete from the list\")\n", "Delete from the list", StringKind.Text);
    Kind("init.lua", "ImGui.Text(\"{0} items in your stash\")\n", "{0} items in your stash", StringKind.Text);
    Kind("init.lua", "ImGui.Text(\"Red, green and blue\")\n", "Red, green and blue", StringKind.Text);
    Kind("init.lua", "ImGui.Text(\"Where do you want to go?\")\n", "Where do you want to go?", StringKind.Text);
    Kind("a.reds", "@runtimeProperty(\"ModSettings.displayName\", \"Enable the mod\")\n", "Enable the mod", StringKind.Text);
});

Test("fonction de journal maison (E, LHUDLogDebug) : ses messages sont du code", () =>
{
    var mod = Dir("logwrap", "MyMod");
    Text(Path.Combine(mod, "r6", "scripts", "Log.reds"),
        "public func E(str: String) -> Void {\n  if MyLogs.IsEnabled() {\n    ModLog(n\"MyMod\", str);\n  };\n}\n" +
        "public func Notify(str: String) -> Void {\n  ModLog(n\"MyMod\", str);\n  this.label.SetText(str);\n}\n");
    Text(Path.Combine(mod, "r6", "scripts", "Main.reds"),
        "E(\"Humanity value restored after sleep\");\nLHUDLogDebug(\"Queue event dispatched right now\");\nNotify(\"You feel much better now\");\n");
    using var p = TranslationProject.Scan(mod, comp);
    UnitStatus Of(string text) => p.Units.Single(u => u.Source == text).Status;
    Check(Of("Humanity value restored after sleep") == UnitStatus.Code, "message de E() = code");
    Check(Of("Queue event dispatched right now") == UnitStatus.Code, "message de LHUDLogDebug() = code");
    Check(Of("You feel much better now") == UnitStatus.ToTranslate, "Notify() affiche aussi le texte : il reste a traduire");
});

Test("traductions deja fournies dans une autre langue : pas proposees a la traduction", () =>
{
    const string lua = "return {\n  Button_Save = \"Save the appearance\",\n}\n";
    foreach (var other in new[] { "Localization/tr_TR.lua", "Localization/ru_RU.lua", "lang/de.lua", "data/mymod.es-es.json", "localization/ru-ru/texts.json", "r6/scripts/German.reds" })
    {
        var content = other.EndsWith(".json") ? "{ \"Button_Save\": \"Save the appearance\" }" : other.EndsWith(".reds") ? "let a = \"Save the appearance\";" : lua;
        Kind(other, content, "Save the appearance", StringKind.Code);
    }
    foreach (var english in new[] { "Localization/en_US.lua", "data/mymod.en-us.json", "bin/init.lua", "modules/tr.lua" })
    {
        var content = english.EndsWith(".json") ? "{ \"label\": \"Save the appearance\" }" : lua;
        Kind(english, content, "Save the appearance", StringKind.Text);
    }
    // plusieurs langues dans le meme fichier
    const string blocks = "local lang = {\n  [\"de-de\"] = {\n    [\"hello\"] = \"Hallo und herzlich willkommen\",\n  },\n  [\"en-us\"] = {\n    [\"hello\"] = \"Hello and welcome here\",\n  },\n}\n";
    Kind("mod/lang.lua", blocks, "Hallo und herzlich willkommen", StringKind.Code);
    Kind("mod/lang.lua", blocks, "Hello and welcome here", StringKind.Text);
    const string json = "{ \"en-us\": { \"label\": \"Hello and welcome here\" }, \"fr-fr\": { \"label\": \"Bonjour et bienvenue ici\" } }";
    Kind("mod/texts.json", json, "Bonjour et bienvenue ici", StringKind.Code);
    Kind("mod/texts.json", json, "Hello and welcome here", StringKind.Text);
});

Test("fichiers d'outils de developpement (.vscode, .github) : ignores", () =>
{
    Check(!PlainTextFile.IsSupported(".vscode/tasks.json") && !PlainTextFile.IsSupported("mod/.github/workflows/build.yaml"), "dossiers caches ignores");
    Check(PlainTextFile.IsSupported("bin/x64/plugins/cyber_engine_tweaks/mods/m/init.lua"), "fichier normal lu");
});

Log.End();
try
{
    Directory.Delete(root, true);
}
catch (Exception ex)
{
    Console.WriteLine("(dossier de test non supprime : " + ex.Message + ")");
}
Console.WriteLine();
Console.WriteLine(failed == 0 ? $"TOUT PASSE : {passed} tests" : $"{failed} ECHEC(S) sur {passed + failed} tests");
return failed == 0 ? 0 : 1;

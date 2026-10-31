using System.Text;
using CyberpunkModTranslator.Core;

namespace CyberpunkModTranslator.Cli;

/// <summary>
/// Version en ligne de commande du moteur, en attendant l'interface.
///   cmt analyser   &lt;mod&gt;
///   cmt exporter   &lt;mod&gt; &lt;fichier.tsv&gt; [--depuis &lt;trad existante&gt;]
///   cmt construire &lt;mod&gt; (&lt;fichier.tsv&gt; | --depuis &lt;trad existante&gt;) &lt;dossier de sortie&gt;
///   cmt lister     &lt;fichier.archive&gt;
/// Options : --jeu &lt;dossier de Cyberpunk 2077&gt;  --oodle &lt;dll&gt;  --langue fr-fr  --sans-compression
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var name = args[i][2..];
                if (name is "sans-compression" or "aide" or "tout") options[name] = "1";
                else options[name] = i + 1 < args.Length ? args[++i] : null;
            }
            else
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count == 0 || options.ContainsKey("aide"))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            using var compressor = Compressors.Load(options.GetValueOrDefault("oodle"), options.GetValueOrDefault("jeu"));
            var lang = options.GetValueOrDefault("langue") ?? "fr-fr";
            return positional[0].ToLowerInvariant() switch
            {
                "analyser" => Analyse(positional, compressor, lang),
                "exporter" => Export(positional, options, compressor, lang),
                "construire" => Build(positional, options, compressor, lang),
                "lister" => List(positional, compressor),
                _ => Fail("Commande inconnue : " + positional[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERREUR : " + ex.Message);
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Cyberpunk Mod Translator - moteur (ligne de commande)

              cmt analyser   <mod>
                  Montre les textes trouves dans le mod et ce qui sera traduit.
              cmt exporter   <mod> <fichier.tsv> [--depuis <trad existante>] [--tout]
                  Ecrit les lignes dans un fichier TSV (colonne "francais" a remplir).
                  Statuts : "a traduire", "a verifier" (peut-etre du texte), "deja traduit".
                  --tout ajoute aussi les chaines classees "code" (identifiants, chemins...).
              cmt construire <mod> <fichier.tsv> <dossier de sortie>
              cmt construire <mod> --depuis <trad existante> <dossier de sortie>
                  Fabrique le patch "#<archive>_FR.archive" et un zip a installer dans MO2.
              cmt lister     <fichier.archive>
                  Liste le contenu d'une archive.

            <mod> = dossier du mod (par exemple dans MO2\mods) ou .zip telecharge sur Nexus.
            Options : --jeu <dossier de Cyberpunk 2077>   --langue fr-fr   --sans-compression
            """);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static int Analyse(List<string> p, ICompressor compressor, string lang)
    {
        if (p.Count < 2) return Fail("Usage : cmt analyser <mod>");
        using var project = TranslationProject.Scan(p[1], compressor, lang);
        Console.WriteLine($"Mod : {project.Mod.Name}");
        Console.WriteLine($"Compresseur : {compressor.Name}");
        Console.WriteLine();
        foreach (var t in project.Targets)
        {
            var units = project.Units.Where(u => u.Loc == t).ToList();
            var todo = units.Count(u => u.Status == UnitStatus.ToTranslate);
            var done = units.Count - todo;
            Console.WriteLine($"[{t.ArchiveName}] {(t.Kind == LocKind.OnScreens ? "textes" : "sous-titres")}");
            Console.WriteLine($"   anglais  : {t.SourcePath}");
            Console.WriteLine($"   a ecrire : {t.TargetPath}{(t.TargetExists ? "" : " (nouveau fichier)")}");
            Console.WriteLine($"   pourquoi : {t.Reason}");
            Console.WriteLine($"   lignes   : {units.Count} ({todo} a traduire, {done} deja traduites)");
            foreach (var u in units.Take(3))
                Console.WriteLine($"      {u.Key} : {Short(u.Source)}");
        }
        if (project.ArchivesWithoutText.Count > 0)
            Console.WriteLine($"\nArchives sans texte : {string.Join(", ", project.ArchivesWithoutText)}");
        foreach (var pt in project.PlainTargets)
        {
            var units = project.Units.Where(u => u.Plain == pt).ToList();
            var text = units.Count(u => u.Status == UnitStatus.ToTranslate);
            var check = units.Count(u => u.Status == UnitStatus.ToCheck);
            var code = units.Count(u => u.Status == UnitStatus.Code);
            if (text + check == 0)
            {
                Console.WriteLine($"[{pt.RelativePath}] aucun texte ({code} chaines de code)");
                continue;
            }
            Console.WriteLine($"[{pt.RelativePath}]");
            Console.WriteLine($"   chaines   : {text} texte(s), {check} a verifier, {code} de code (ignorees)");
            foreach (var u in units.Where(u => u.Status != UnitStatus.Code).Take(4))
                Console.WriteLine($"      {(u.Status == UnitStatus.ToCheck ? "?" : " ")} {Short(u.Source)}");
        }
        var linked = project.Units.Where(u => u.LinkedCode.Count > 0).ToList();
        if (linked.Count > 0)
        {
            Console.WriteLine("\nTextes qui servent aussi d'identifiants (leur traduction sera reportee dans le code) :");
            foreach (var u in linked)
                Console.WriteLine($"   {Short(u.Source)}  ->  {string.Join(" ; ", u.LinkedCode.Select(TranslationProject.Where))}");
        }
        foreach (var n in project.Notes) Console.WriteLine("Note : " + n);
        return 0;
    }

    private static int Export(List<string> p, Dictionary<string, string?> o, ICompressor compressor, string lang)
    {
        if (p.Count < 3) return Fail("Usage : cmt exporter <mod> <fichier.tsv> [--depuis <trad existante>]");
        using var project = TranslationProject.Scan(p[1], compressor, lang);
        if (o.TryGetValue("depuis", out var from) && from != null)
            Console.WriteLine($"{project.ImportFromTranslationMod(from)} ligne(s) reprise(s) de {from}");
        var all = o.ContainsKey("tout");
        project.ExportTsv(p[2], all);
        var written = project.Units.Count(u => all || u.Status != UnitStatus.Code || u.Translation != null);
        Console.WriteLine($"{written} ligne(s) ecrite(s) dans {p[2]}{(all ? "" : " (chaines de code exclues, --tout pour les inclure)")}");
        return 0;
    }

    private static int Build(List<string> p, Dictionary<string, string?> o, ICompressor compressor, string lang)
    {
        var from = o.GetValueOrDefault("depuis");
        if (p.Count < (from != null ? 3 : 4))
            return Fail("Usage : cmt construire <mod> <fichier.tsv> <dossier>  ou  cmt construire <mod> --depuis <trad> <dossier>");
        using var project = TranslationProject.Scan(p[1], compressor, lang);
        if (from != null)
        {
            Console.WriteLine($"{project.ImportFromTranslationMod(from)} ligne(s) reprise(s) de {from}");
        }
        else
        {
            var (applied, warnings) = project.ImportTsv(p[2]);
            Console.WriteLine($"{applied} ligne(s) lue(s) dans {p[2]}");
            foreach (var w in warnings) Console.WriteLine("Attention : " + w);
        }
        var output = from != null ? p[2] : p[3];
        var result = project.Build(output, o.ContainsKey("sans-compression") ? null : compressor);
        foreach (var w in result.Warnings) Console.WriteLine("Attention : " + w);
        foreach (var i in result.Infos) Console.WriteLine("Info : " + i);
        Console.WriteLine($"{result.ChangedLines} ligne(s) traduite(s)");
        foreach (var a in result.Archives) Console.WriteLine("Archive : " + a);
        foreach (var f in result.PlainFiles) Console.WriteLine("Fichier : " + f);
        if (result.Zip != null) Console.WriteLine("A installer dans MO2 : " + result.Zip);
        return result.Archives.Count + result.PlainFiles.Count > 0 ? 0 : 1;
    }

    private static int List(List<string> p, ICompressor compressor)
    {
        if (p.Count < 2) return Fail("Usage : cmt lister <fichier.archive>");
        using var ar = RedArchive.Open(p[1], compressor);
        Console.WriteLine($"{Path.GetFileName(p[1])} : {ar.Files.Count} fichier(s), {ar.CustomPaths.Count} nom(s) connus");
        foreach (var e in ar.Files.OrderBy(f => f.Display, StringComparer.OrdinalIgnoreCase))
        {
            var info = "";
            try
            {
                var data = ar.ReadMain(e);
                info = Cr2wFile.LooksLikeCr2w(data) ? Cr2wFile.Parse(data).RootClass : "";
                var loc = LocalizationFile.TryLoad(data);
                if (loc != null) info += $" ({loc.Entries.Count} lignes de texte)";
            }
            catch (Exception ex)
            {
                info = "illisible : " + ex.Message;
            }
            Console.WriteLine($"   {e.Display}  {e.Size:N0} o  {info}");
        }
        return 0;
    }

    private static string Short(string s)
    {
        s = s.Replace("\n", " ");
        return s.Length > 70 ? s[..67] + "..." : s;
    }
}

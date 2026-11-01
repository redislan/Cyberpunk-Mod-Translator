using System.Text;
using CyberpunkModTranslator.Core;

namespace CyberpunkModTranslator.Cli;

/// <summary>
/// Version en ligne de commande du moteur.
///   cmt analyze  &lt;mod&gt;                                   (ou : analyser)
///   cmt export   &lt;mod&gt; &lt;fichier.tsv&gt; [--from &lt;trad&gt;] [--all] (ou : exporter, --depuis, --tout)
///   cmt build    &lt;mod&gt; (&lt;fichier.tsv&gt; | --from &lt;trad&gt;) &lt;dossier de sortie&gt; (ou : construire)
///   cmt list     &lt;fichier.archive&gt;                         (ou : lister)
/// Options : --game &lt;dossier de Cyberpunk 2077&gt; (--jeu)  --oodle &lt;dll&gt;  --lang fr-fr (--langue)
///           --no-compression (--sans-compression)  --ui en|fr  --debug
/// Les messages suivent la langue de Windows (ou --ui), comme l'interface.
/// Journal : %AppData%\CyberpunkModTranslator\logs\cmt_*.log
/// </summary>
public static class Program
{
    /// <summary>Noms francais des options (versions precedentes) vers les noms anglais.</summary>
    private static readonly Dictionary<string, string> OptionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jeu"] = "game",
        ["langue"] = "lang",
        ["depuis"] = "from",
        ["tout"] = "all",
        ["sans-compression"] = "no-compression",
        ["aide"] = "help",
    };

    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "no-compression", "help", "all", "debug" };

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
                if (OptionAliases.TryGetValue(name, out var english)) name = english;
                if (Flags.Contains(name)) options[name] = "1";
                else options[name] = i + 1 < args.Length ? args[++i] : null;
            }
            else
            {
                positional.Add(args[i]);
            }
        }

        UiText.Use(options.GetValueOrDefault("ui"));

        if (positional.Count == 0 || options.ContainsKey("help") || positional[0] is "help" or "aide")
        {
            Console.WriteLine(UiText.T("cli.help", Log.DefaultFolder));
            return 0;
        }

        var command = positional[0].ToLowerInvariant() switch
        {
            "analyze" or "analyse" or "analyser" => "analyze",
            "export" or "exporter" => "export",
            "build" or "construire" => "build",
            "list" or "lister" => "list",
            _ => null,
        };
        if (command == null) return Fail(UiText.T("cli.unknownCommand", positional[0]) + "\n\n" + UiText.T("cli.help", Log.DefaultFolder));

        if (options.ContainsKey("debug")) Log.DebugEnabled = true;
        Log.Start("cmt");
        Log.Info("Commande : cmt " + string.Join(" ", args));
        try
        {
            using var compressor = Compressors.Load(options.GetValueOrDefault("oodle"), options.GetValueOrDefault("game"));
            var lang = options.GetValueOrDefault("lang") ?? "fr-fr";
            return command switch
            {
                "analyze" => Analyse(positional, compressor, lang),
                "export" => Export(positional, options, compressor, lang),
                "build" => Build(positional, options, compressor, lang),
                _ => List(positional, compressor),
            };
        }
        catch (Exception ex)
        {
            Log.Error("Commande echouee", ex);
            Console.Error.WriteLine(UiText.T("cli.error", ex.Message));
            if (Log.FilePath != null) Console.Error.WriteLine(UiText.T("cli.logDetail", Log.FilePath));
            return 1;
        }
        finally
        {
            Log.End();
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static int Analyse(List<string> p, ICompressor compressor, string lang)
    {
        if (p.Count < 2) return Fail(UiText.T("cli.usage.analyze"));
        using var project = TranslationProject.Scan(p[1], compressor, lang);
        Console.WriteLine(UiText.T("cli.mod", project.Mod.Name));
        Console.WriteLine(UiText.T("cli.compressor", compressor.Name));
        Console.WriteLine();
        foreach (var t in project.Targets)
        {
            var units = project.Units.Where(u => u.Loc == t).ToList();
            var todo = units.Count(u => u.Status == UnitStatus.ToTranslate);
            var done = units.Count - todo;
            Console.WriteLine($"[{t.ArchiveName}] {UiText.T(t.Kind == LocKind.OnScreens ? "cli.kind.onscreens" : "cli.kind.subtitles")}");
            Console.WriteLine(UiText.T("cli.loc.source", t.SourcePath));
            Console.WriteLine(UiText.T("cli.loc.target", t.TargetPath, t.TargetExists ? "" : UiText.T("cli.loc.newFile")));
            Console.WriteLine(UiText.T("cli.loc.reason", t.Reason));
            Console.WriteLine(UiText.T("cli.loc.lines", units.Count, todo, done));
            foreach (var u in units.Take(3))
                Console.WriteLine($"      {u.Key} : {Short(u.Source)}");
        }
        if (project.ArchivesWithoutText.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(UiText.T("cli.noTextArchives", string.Join(", ", project.ArchivesWithoutText)));
        }
        foreach (var pt in project.PlainTargets)
        {
            var units = project.Units.Where(u => u.Plain == pt).ToList();
            var text = units.Count(u => u.Status == UnitStatus.ToTranslate);
            var check = units.Count(u => u.Status == UnitStatus.ToCheck);
            var code = units.Count(u => u.Status == UnitStatus.Code);
            if (text + check == 0)
            {
                Console.WriteLine(UiText.T("cli.plain.none", pt.RelativePath, code));
                continue;
            }
            Console.WriteLine($"[{pt.RelativePath}]");
            Console.WriteLine(UiText.T("cli.plain.counts", text, check, code));
            foreach (var u in units.Where(u => u.Status != UnitStatus.Code).Take(4))
                Console.WriteLine($"      {(u.Status == UnitStatus.ToCheck ? "?" : " ")} {Short(u.Source)}");
        }
        var linked = project.Units.Where(u => u.LinkedCode.Count > 0).ToList();
        if (linked.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(UiText.T("cli.linked"));
            foreach (var u in linked)
                Console.WriteLine($"   {Short(u.Source)}  ->  {string.Join(" ; ", u.LinkedCode.Select(TranslationProject.Where))}");
        }
        foreach (var n in project.Notes) Console.WriteLine(UiText.T("cli.note", n));
        return 0;
    }

    private static int Export(List<string> p, Dictionary<string, string?> o, ICompressor compressor, string lang)
    {
        if (p.Count < 3) return Fail(UiText.T("cli.usage.export"));
        using var project = TranslationProject.Scan(p[1], compressor, lang);
        if (o.TryGetValue("from", out var from) && from != null)
            Console.WriteLine(UiText.T("cli.imported", project.ImportFromTranslationMod(from), from));
        var all = o.ContainsKey("all");
        project.ExportTsv(p[2], all);
        var written = project.Units.Count(u => all || u.Status != UnitStatus.Code || u.Translation != null);
        Console.WriteLine(UiText.T("cli.written", written, p[2]) + (all ? "" : UiText.T("cli.codeExcluded")));
        return 0;
    }

    private static int Build(List<string> p, Dictionary<string, string?> o, ICompressor compressor, string lang)
    {
        var from = o.GetValueOrDefault("from");
        if (p.Count < (from != null ? 3 : 4)) return Fail(UiText.T("cli.usage.build"));
        using var project = TranslationProject.Scan(p[1], compressor, lang);
        if (from != null)
        {
            Console.WriteLine(UiText.T("cli.imported", project.ImportFromTranslationMod(from), from));
        }
        else
        {
            var (applied, warnings) = project.ImportTsv(p[2]);
            Console.WriteLine(UiText.T("cli.read", applied, p[2]));
            foreach (var w in warnings) Console.WriteLine(UiText.T("cli.warning", w));
        }
        var output = from != null ? p[2] : p[3];
        var result = project.Build(output, o.ContainsKey("no-compression") ? null : compressor);
        foreach (var w in result.Warnings) Console.WriteLine(UiText.T("cli.warning", w));
        foreach (var i in result.Infos) Console.WriteLine(UiText.T("cli.info", i));
        Console.WriteLine(UiText.T("cli.changed", result.ChangedLines));
        foreach (var a in result.Archives) Console.WriteLine(UiText.T("cli.archive", a));
        foreach (var f in result.PlainFiles) Console.WriteLine(UiText.T("cli.file", f));
        if (result.Zip != null) Console.WriteLine(UiText.T("cli.install", result.Zip));
        return result.Archives.Count + result.PlainFiles.Count > 0 ? 0 : 1;
    }

    private static int List(List<string> p, ICompressor compressor)
    {
        if (p.Count < 2) return Fail(UiText.T("cli.usage.list"));
        using var ar = RedArchive.Open(p[1], compressor);
        Console.WriteLine(UiText.T("cli.list.header", Path.GetFileName(p[1]), ar.Files.Count, ar.CustomPaths.Count));
        foreach (var e in ar.Files.OrderBy(f => f.Display, StringComparer.OrdinalIgnoreCase))
        {
            var info = "";
            try
            {
                var data = ar.ReadMain(e);
                info = Cr2wFile.LooksLikeCr2w(data) ? Cr2wFile.Parse(data).RootClass : "";
                var loc = LocalizationFile.TryLoad(data);
                if (loc != null) info += UiText.T("cli.list.lines", loc.Entries.Count);
            }
            catch (Exception ex)
            {
                Log.Warn($"lister : {e.Display} illisible", ex);
                info = UiText.T("cli.list.unreadable", ex.Message);
            }
            Console.WriteLine($"   {e.Display}  {UiText.T("cli.list.size", e.Size.ToString("N0"))}  {info}");
        }
        return 0;
    }

    private static string Short(string s)
    {
        s = s.Replace("\n", " ");
        return s.Length > 70 ? s[..67] + "..." : s;
    }
}

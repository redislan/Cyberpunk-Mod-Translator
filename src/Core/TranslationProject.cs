using System.IO.Compression;
using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

public enum UnitStatus
{
    /// <summary>Texte anglais a traduire.</summary>
    ToTranslate,

    /// <summary>Le fichier cible contient deja un texte different de l'anglais (traduit par l'auteur).</summary>
    AlreadyTranslated,

    /// <summary>Peut-etre du texte, peut-etre un identifiant : a l'utilisateur de decider.</summary>
    ToCheck,

    /// <summary>Identifiant, chemin, cle ou message de debug : on ne touche pas (sauf choix explicite).</summary>
    Code,
}

/// <summary>Un fichier de textes d'archive a produire : d'ou vient l'anglais, ou ecrire la traduction.</summary>
public sealed class LocTarget
{
    public string ArchiveName { get; init; } = "";
    public LocKind Kind { get; init; }
    public string SourcePath { get; init; } = "";
    public string TargetPath { get; init; } = "";
    public bool TargetExists { get; init; }
    public LocText Reason { get; init; } = new("reason.plainFile");
    public LocalizationFile Source { get; init; } = null!;
    public byte[] TemplateBytes { get; init; } = Array.Empty<byte>();
    public LocalizationFile Template { get; init; } = null!;
}

/// <summary>Un fichier en clair du mod (.lua, .reds...) : la version FR sera une copie modifiee au meme chemin.</summary>
public sealed class PlainTarget
{
    public string RelativePath { get; init; } = "";
    public PlainTextFile File { get; init; } = null!;
}

/// <summary>Une ligne a traduire, qu'elle vienne d'une archive ou d'un fichier en clair.</summary>
public sealed class TextUnit
{
    public LocTarget? Loc { get; init; }
    public PlainTarget? Plain { get; init; }
    public int LiteralIndex { get; init; }

    /// <summary>Nom de l'archive, ou "-" pour un fichier en clair.</summary>
    public string SourceName => Loc?.ArchiveName ?? "-";

    /// <summary>Chemin du fichier ecrit : chemin dans l'archive, ou chemin relatif dans le mod.</summary>
    public string FilePath => Loc?.TargetPath ?? Plain!.RelativePath.Replace('\\', '/');

    public string Key { get; init; } = "";
    public string Field { get; init; } = "F";
    public string Context { get; init; } = "";
    public string Source { get; init; } = "";
    public string Current { get; init; } = "";
    public UnitStatus Status { get; init; }
    private string? _translation;

    public string? Translation
    {
        get => _translation;
        set => _translation = MatchNewlineStyle(Source, value);
    }

    /// <summary>
    /// Si l'original ecrit ses retours a la ligne en texte (\n, deux caracteres) et n'en a aucun vrai,
    /// la traduction garde la meme forme : ses vrais retours a la ligne redeviennent \n.
    /// </summary>
    internal static string? MatchNewlineStyle(string source, string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('\n')) return text;
        if (!source.Contains("\\n") || source.Contains('\n')) return text;
        return text.Replace("\r\n", "\n").Replace("\n", "\\n");
    }

    public bool HasChange => !string.IsNullOrEmpty(Translation) && Translation != Current;

    /// <summary>
    /// Comparaisons du code qui utilisent exactement ce texte (Equals(x, "Background"), x == "Menu"...).
    /// Le texte sert alors aussi d'identifiant : sa traduction doit etre reportee dans ces comparaisons.
    /// </summary>
    public List<TextUnit> LinkedCode { get; } = new();

    /// <summary>Reporter la traduction dans les comparaisons liees (oui par defaut).</summary>
    public bool ApplyToLinkedCode { get; set; } = true;

    /// <summary>Pour une comparaison du code : le texte traduisible auquel elle est liee.</summary>
    public List<TextUnit> LinkedTexts { get; } = new();
}

public sealed class BuildResult
{
    public List<string> Archives { get; } = new();
    public List<string> PlainFiles { get; } = new();
    public string? Zip { get; set; }
    public int ChangedLines { get; set; }
    public List<string> Warnings { get; } = new();
    public List<string> Infos { get; } = new();
}

/// <summary>
/// Le coeur du programme : analyse un mod, liste les textes a traduire et fabrique le patch de traduction.
/// </summary>
public sealed class TranslationProject : IDisposable
{
    private static readonly string[] KnownLanguages =
    {
        "ar-ar", "cz-cz", "de-de", "en-us", "es-es", "es-mx", "fr-fr", "hu-hu", "it-it", "jp-jp", "kr-kr",
        "pl-pl", "pt-br", "ru-ru", "th-th", "tr-tr", "ua-ua", "zh-cn", "zh-tw",
    };

    private const long MaxPlainFileSize = 4 * 1024 * 1024;

    private readonly ICompressor _compressor;

    /// <summary>Analyse de fond (onglet Modlist) : remarques et etapes en niveau Debug, pour ne pas noyer le journal.</summary>
    private bool _quiet;

    public ModPackage Mod { get; }
    public string TargetLang { get; }
    public List<LocTarget> Targets { get; } = new();
    public List<PlainTarget> PlainTargets { get; } = new();
    public List<TextUnit> Units { get; } = new();
    public List<LocText> Notes { get; } = new();
    public List<string> ArchivesWithoutText { get; } = new();

    private TranslationProject(ModPackage mod, ICompressor compressor, string targetLang)
    {
        Mod = mod;
        _compressor = compressor;
        TargetLang = targetLang;
    }

    /// <summary>Remarque montree dans l'appli (et ecrite dans le journal).</summary>
    private void AddNote(LocText note)
    {
        Notes.Add(note);
        Log.Write(_quiet ? LogSeverity.Debug : LogSeverity.Warning, "Analyse : " + note, null);
    }

    /// <param name="quiet">Analyse de fond (onglet Modlist) : journal en niveau Debug seulement.</param>
    public static TranslationProject Scan(string modPath, ICompressor compressor, string targetLang = "fr-fr", bool quiet = false)
    {
        var level = quiet ? LogSeverity.Debug : LogSeverity.Info;
        Log.Write(level, $"Analyse de {modPath} (langue cible {targetLang})", null);
        var project = new TranslationProject(ModPackage.Open(modPath), compressor, targetLang) { _quiet = quiet };
        try
        {
            foreach (var archive in project.Mod.Archives)
                project.ScanArchive(archive);
            foreach (var f in project.Mod.Files)
                if (PlainTextFile.IsSupported(f))
                    project.ScanPlainFile(f);
            project.LinkTextsToComparisons();
            var u = project.Units;
            Log.Write(level, $"Analyse terminee : {u.Count} chaines ({u.Count(x => x.Status == UnitStatus.ToTranslate)} a traduire, " +
                     $"{u.Count(x => x.Status == UnitStatus.ToCheck)} a verifier, {u.Count(x => x.Status == UnitStatus.AlreadyTranslated)} deja traduites, " +
                     $"{u.Count(x => x.Status == UnitStatus.Code)} code), {project.Targets.Count} fichier(s) de textes, " +
                     $"{project.PlainTargets.Count} fichier(s) en clair, {project.Notes.Count} remarque(s)", null);
        }
        catch
        {
            project.Dispose();
            throw;
        }
        return project;
    }

    // -----------------------------------------------------------------------------------------
    // Analyse des archives
    // -----------------------------------------------------------------------------------------

    private void ScanArchive(string relative)
    {
        var fullPath = Mod.FullPath(relative);
        var archiveName = Path.GetFileName(relative);
        using var ar = RedArchive.Open(fullPath, _compressor);

        var cache = new Dictionary<ulong, (byte[] Bytes, LocalizationFile? Loc)>();

        (byte[] Bytes, LocalizationFile? Loc)? Load(ArchiveEntry e)
        {
            if (cache.TryGetValue(e.Hash, out var hit)) return hit;
            byte[] bytes;
            try
            {
                bytes = ar.ReadMain(e);
            }
            catch (Exception ex)
            {
                AddNote(new LocText("scan.readFailed", archiveName, e.Display, ex.Message));
                cache[e.Hash] = (Array.Empty<byte>(), null);
                return cache[e.Hash];
            }
            var loc = LocalizationFile.TryLoad(bytes);
            // Un fichier qui a tout d'un fichier de textes mais qu'on ne sait pas lire : ses lignes manqueraient
            if (loc == null && e.Path != null && Cr2wFile.LooksLikeCr2w(bytes) &&
                e.Path.Contains("localization", StringComparison.OrdinalIgnoreCase) &&
                e.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                LocalizationFile.TryReadSubtitleMap(bytes) == null)
                Log.Warn($"{archiveName} : {e.Path} ressemble a un fichier de textes mais n'a pas pu etre lu");
            cache[e.Hash] = (bytes, loc);
            return cache[e.Hash];
        }

        (byte[] Bytes, LocalizationFile? Loc)? Find(string path) =>
            ar.ByHash.TryGetValue(ResourcePath.Hash(path), out var e) ? Load(e) : null;

        // Fichiers de textes presents dans l'archive (par le chemin, ou par le contenu si le nom est inconnu)
        var locEntries = ar.Files
            .Where(e => e.Path != null
                ? e.Path.Contains("localization", StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                : e.Size < 8 * 1024 * 1024)
            .Where(e => Load(e)?.Loc != null)
            .ToList();

        var pairs = new List<(string Src, string Tgt, LocText Reason)>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Ce que declare le .xl d'ArchiveXL
        var xlPath = fullPath + ".xl";
        var xl = File.Exists(xlPath) ? XlLocalization.Load(xlPath) : null;
        if (xl != null)
        {
            foreach (var section in new[] { "onscreens", "subtitles" })
            {
                var langs = xl.Get(section).Where(l => l.Paths.Count > 0).ToList();
                if (langs.Count == 0) continue;
                var srcLang = langs.Any(l => l.Lang == "en-us") ? "en-us" : langs[0].Lang;
                var srcPaths = langs.First(l => l.Lang == srcLang).Paths;
                var tgt = langs.FirstOrDefault(l => l.Lang == TargetLang);
                List<string> tgtPaths;
                LocText reason;
                if (tgt.Paths != null)
                {
                    tgtPaths = tgt.Paths;
                    reason = new LocText("reason.xlDeclares", TargetLang);
                }
                else
                {
                    tgtPaths = langs[0].Paths;
                    reason = new LocText("reason.xlFallback", TargetLang, langs[0].Lang);
                }
                for (var i = 0; i < tgtPaths.Count; i++)
                {
                    var src = srcPaths[Math.Min(i, srcPaths.Count - 1)];
                    var srcFile = Find(src);
                    if (srcFile != null && srcFile.Value.Loc == null &&
                        LocalizationFile.TryReadSubtitleMap(srcFile.Value.Bytes) is { } srcSubs)
                    {
                        // Le .xl pointe vers une carte de sous-titres : on suit ses liens vers les vrais fichiers
                        var tgtFile = Find(tgtPaths[i]);
                        var tgtSubs = tgtFile != null ? LocalizationFile.TryReadSubtitleMap(tgtFile.Value.Bytes) : null;
                        for (var k = 0; k < srcSubs.Count; k++)
                        {
                            var t = tgtSubs != null && k < tgtSubs.Count ? tgtSubs[k] : srcSubs[k];
                            pairs.Add((srcSubs[k], t, reason.Then(new LocText("reason.viaSubtitleMap"))));
                            covered.Add(ResourcePath.Sanitize(srcSubs[k]));
                            covered.Add(ResourcePath.Sanitize(t));
                        }
                        continue;
                    }
                    pairs.Add((src, tgtPaths[i], reason));
                }
                foreach (var l in langs)
                    foreach (var p in l.Paths)
                        covered.Add(ResourcePath.Sanitize(p));
            }
        }

        // 2) Fichiers de textes non declares dans un .xl (remplacement direct d'un fichier)
        foreach (var e in locEntries)
        {
            var path = e.Path ?? e.Hash.ToString();
            if (covered.Contains(ResourcePath.Sanitize(path))) continue;
            var lang = e.Path == null ? null : KnownLanguages.FirstOrDefault(l =>
                Regex.IsMatch(e.Path, $@"(^|[\\/_.]){l}([\\/_.]|$)", RegexOptions.IgnoreCase));
            if (lang == null)
            {
                pairs.Add((path, path, new LocText("reason.noLangInPath")));
            }
            else if (lang == "en-us")
            {
                var fr = Regex.Replace(e.Path!, "en-us", TargetLang, RegexOptions.IgnoreCase);
                if (Find(fr)?.Loc != null)
                {
                    pairs.Add((path, fr, new LocText("reason.targetPresent", TargetLang)));
                    covered.Add(ResourcePath.Sanitize(fr));
                }
                else
                {
                    pairs.Add((path, path, new LocText("reason.replaceEnglish", TargetLang)));
                }
            }
            // autres langues : ignorees
        }

        // 3) Construction des cibles et des lignes
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (srcPath, tgtPath, reason) in pairs)
        {
            if (!done.Add(ResourcePath.Sanitize(tgtPath))) continue;
            var src = ulong.TryParse(srcPath, out var srcHash) && ar.ByHash.TryGetValue(srcHash, out var se) ? Load(se) : Find(srcPath);
            if (src?.Loc == null)
            {
                AddNote(new LocText("scan.sourceMissing", archiveName, srcPath));
                continue;
            }
            var tgt = ulong.TryParse(tgtPath, out var tgtHash) && ar.ByHash.TryGetValue(tgtHash, out var te) ? Load(te) : Find(tgtPath);
            var targetExists = tgt?.Loc != null;
            var templateBytes = targetExists ? tgt!.Value.Bytes : src.Value.Bytes;
            var template = targetExists ? tgt!.Value.Loc! : src.Value.Loc;
            if (template.Kind != src.Value.Loc.Kind)
            {
                AddNote(new LocText("scan.typeMismatch", archiveName, tgtPath, srcPath));
                continue;
            }

            var target = new LocTarget
            {
                ArchiveName = archiveName,
                Kind = src.Value.Loc.Kind,
                SourcePath = srcPath,
                TargetPath = tgtPath,
                TargetExists = targetExists,
                Reason = reason,
                Source = src.Value.Loc,
                TemplateBytes = templateBytes,
                Template = template,
            };
            Targets.Add(target);

            var samePath = string.Equals(ResourcePath.Sanitize(srcPath), ResourcePath.Sanitize(tgtPath), StringComparison.OrdinalIgnoreCase);
            var missing = 0;
            foreach (var entry in src.Value.Loc.Entries)
            {
                if (entry.Key.Length == 0) continue;
                template.ByKey.TryGetValue(entry.Key, out var tEntry);
                foreach (var field in new[] { "F", "M" })
                {
                    var text = entry.Get(field);
                    if (string.IsNullOrEmpty(text)) continue;
                    if (tEntry?.Get(field) == null)
                    {
                        missing++;
                        continue;
                    }
                    var current = tEntry.Get(field)!;
                    Units.Add(new TextUnit
                    {
                        Loc = target,
                        Key = entry.Key,
                        Field = field,
                        Context = entry.SecondaryKey ?? "",
                        Source = text,
                        Current = current,
                        Status = !samePath && current != text ? UnitStatus.AlreadyTranslated : UnitStatus.ToTranslate,
                    });
                }
            }
            if (missing > 0)
                AddNote(new LocText("scan.missingLines", archiveName, missing, tgtPath));
        }

        if (!Targets.Any(t => t.ArchiveName == archiveName))
            ArchivesWithoutText.Add(archiveName);
    }

    // -----------------------------------------------------------------------------------------
    // Analyse des fichiers en clair (.lua, .reds, .yaml, .json, .tweak)
    // -----------------------------------------------------------------------------------------

    private void ScanPlainFile(string relative)
    {
        var full = Mod.FullPath(relative);
        if (new FileInfo(full).Length > MaxPlainFileSize)
        {
            AddNote(new LocText("scan.tooBig", relative));
            return;
        }
        PlainTextFile file;
        try
        {
            file = PlainTextFile.Load(full, relative);
        }
        catch (Exception ex)
        {
            AddNote(new LocText("scan.plainReadFailed", relative, ex.Message));
            return;
        }
        if (file.Literals.Count == 0) return;

        var target = new PlainTarget { RelativePath = relative, File = file };
        PlainTargets.Add(target);
        foreach (var lit in file.Literals)
        {
            Units.Add(new TextUnit
            {
                Plain = target,
                LiteralIndex = lit.Index,
                Key = lit.Index.ToString(),
                Field = "-",
                Context = $"l.{lit.Line} : {lit.LineText}",
                Source = lit.Value,
                Current = lit.Value,
                Status = lit.Kind switch
                {
                    StringKind.Text => UnitStatus.ToTranslate,
                    StringKind.Maybe => UnitStatus.ToCheck,
                    _ => UnitStatus.Code,
                },
            });
        }
    }

    private static string Family(PlainTarget t) => Path.GetExtension(t.RelativePath).ToLowerInvariant() switch
    {
        ".reds" or ".tweak" => "reds",
        ".lua" => "lua",
        _ => "",
    };

    /// <summary>
    /// Repere les textes qui servent aussi d'identifiants : meme valeur exacte qu'une comparaison du code,
    /// dans le meme fichier. Exemple (Main Menu Backgrounds) :
    ///   Categories() renvoie ["Background", ...]  et  Settings() teste Equals(category, "Background").
    /// Traduire l'un sans l'autre casse le menu ; on reportera donc la traduction dans la comparaison.
    /// <para>
    /// Seulement dans le meme fichier : d'un fichier a l'autre, le meme mot est souvent sans rapport
    /// (Night City Traffic Overhaul : le libelle "Badlands" d'un reglage et le nom de district renvoye par le jeu).
    /// </para>
    /// </summary>
    private void LinkTextsToComparisons()
    {
        var texts = Units
            .Where(u => u.Plain != null && u.Status is UnitStatus.ToTranslate or UnitStatus.ToCheck && Family(u.Plain) != "")
            .GroupBy(u => (u.Plain!.RelativePath, u.Source))
            .ToDictionary(g => g.Key, g => g.ToList());
        if (texts.Count == 0) return;
        foreach (var code in Units.Where(u => u.Plain != null && u.Status == UnitStatus.Code &&
                                              u.Plain.File.Literals[u.LiteralIndex].IsComparison))
        {
            if (!texts.TryGetValue((code.Plain!.RelativePath, code.Source), out var candidates)) continue;
            // On ne sait pas lequel de ces textes arrive dans la comparaison : on les lie tous.
            // La traduction ne sera reportee que s'ils sont tous traduits pareil.
            foreach (var t in candidates)
            {
                t.LinkedCode.Add(code);
                code.LinkedTexts.Add(t);
            }
        }
    }

    /// <summary>Fichier et numero de ligne d'une chaine.</summary>
    public static string ShortWhere(TextUnit u) =>
        u.Plain == null ? u.FilePath : $"{Path.GetFileName(u.Plain.RelativePath)} l.{u.Plain.File.Literals[u.LiteralIndex].Line}";

    /// <summary>Description courte d'une comparaison liee (fichier et ligne).</summary>
    public static string Where(TextUnit u) =>
        u.Plain == null ? u.FilePath : $"{Path.GetFileName(u.Plain.RelativePath)} {u.Context}";

    // -----------------------------------------------------------------------------------------
    // Reprise d'une traduction existante et fichiers TSV
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Reprend les textes d'une traduction deja faite (un autre mod, par exemple une trad FR de Nexus).
    /// Sert a mettre a jour une traduction quand le mod d'origine change.
    /// </summary>
    public int ImportFromTranslationMod(string translationModPath)
    {
        using var mod = ModPackage.Open(translationModPath);
        var count = 0;

        // Textes des archives
        var texts = new Dictionary<(string Path, string Key), LocEntry>();
        foreach (var archive in mod.Archives)
        {
            using var ar = RedArchive.Open(mod.FullPath(archive), _compressor);
            foreach (var e in ar.Files)
            {
                LocalizationFile? loc;
                try
                {
                    loc = LocalizationFile.TryLoad(ar.ReadMain(e));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Reprise de trad : {archive} / {e.Display} illisible, ignore", ex);
                    continue;
                }
                if (loc == null) continue;
                var path = e.Path != null ? ResourcePath.Sanitize(e.Path) : e.Hash.ToString();
                foreach (var entry in loc.Entries)
                    texts.TryAdd((path, entry.Key), entry);
            }
        }
        foreach (var u in Units.Where(u => u.Loc != null))
        {
            if (!texts.TryGetValue((ResourcePath.Sanitize(u.Loc!.TargetPath), u.Key), out var entry) &&
                !texts.TryGetValue((ResourcePath.Sanitize(u.Loc.SourcePath), u.Key), out entry))
                continue;
            var text = entry.Get(u.Field);
            if (string.IsNullOrEmpty(text) || text == u.Current) continue;
            u.Translation = text;
            count++;
        }

        // Fichiers en clair : meme chemin dans la trad, chaines mises en correspondance dans l'ordre
        var files = mod.Files.ToDictionary(f => f.Replace('\\', '/'), f => f, StringComparer.OrdinalIgnoreCase);
        foreach (var target in PlainTargets)
        {
            if (!files.TryGetValue(target.RelativePath.Replace('\\', '/'), out var rel)) continue;
            PlainTextFile translated;
            try
            {
                translated = PlainTextFile.Load(mod.FullPath(rel), rel);
            }
            catch (Exception ex)
            {
                Log.Warn($"Reprise de trad : {rel} illisible, ignore", ex);
                continue;
            }
            if (translated.Literals.Count != target.File.Literals.Count)
            {
                AddNote(new LocText("import.structure", target.RelativePath));
                continue;
            }
            foreach (var u in Units.Where(u => u.Plain == target))
            {
                var text = translated.Literals[u.LiteralIndex].Value;
                if (text == u.Current) continue;
                u.Translation = text;
                count++;
            }
        }
        Log.Info($"Reprise de la trad {translationModPath} : {count} ligne(s) reprises");
        return count;
    }

    /// <summary>
    /// En-tete du TSV dans la langue de l'interface. La premiere colonne reste "#source" : c'est elle qui fait reconnaitre
    /// la ligne d'en-tete. La lecture se fait par position, donc un TSV d'une version ou d'une langue a l'autre se relit.
    /// </summary>
    public static string[] TsvHeader() => new[]
    {
        "#source", UiText.T("tsv.col.file"), UiText.T("tsv.col.key"), UiText.T("tsv.col.field"), UiText.T("tsv.col.status"),
        UiText.T("tsv.col.context"), UiText.T("tsv.col.english"), UiText.T("tsv.col.translation"), UiText.T("tsv.col.applyToCode"),
    };

    public static string StatusLabel(UnitStatus s) => s switch
    {
        UnitStatus.AlreadyTranslated => UiText.T("tsv.status.done"),
        UnitStatus.ToCheck => UiText.T("tsv.status.check"),
        UnitStatus.Code => UiText.T("tsv.status.code"),
        _ => UiText.T("tsv.status.todo"),
    };

    /// <summary>"non" / "no" (et la valeur de la langue choisie) dans la colonne du report dans le code.</summary>
    private static bool IsNo(string value)
    {
        var v = value.Trim();
        return v.Equals("non", StringComparison.OrdinalIgnoreCase) || v.Equals("no", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("n", StringComparison.OrdinalIgnoreCase) || v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) ||
               v.Equals(UiText.T("tsv.no"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Exporte les lignes. Par defaut sans les chaines classees "code".</summary>
    public void ExportTsv(string path, bool includeCode = false)
    {
        var rows = new List<string[]> { TsvHeader() };
        foreach (var u in Units)
        {
            if (u.Status == UnitStatus.Code && !includeCode && u.Translation == null) continue;
            rows.Add(new[]
            {
                u.SourceName, u.FilePath, u.Key, u.Field, StatusLabel(u.Status),
                u.LinkedCode.Count > 0
                    ? UiText.T("tsv.alsoCompared", u.Context, string.Join(" ; ", u.LinkedCode.Select(Where)))
                    : u.Context,
                u.Source,
                u.Translation ?? (u.Status == UnitStatus.AlreadyTranslated ? u.Current : ""),
                u.LinkedCode.Count == 0 ? "" : UiText.T(u.ApplyToLinkedCode ? "tsv.yes" : "tsv.no"),
            });
        }
        Tsv.Write(path, rows);
    }

    public (int Applied, List<string> Warnings) ImportTsv(string path)
    {
        var warnings = new List<string>();
        var index = Units.ToDictionary(u => (u.SourceName, Norm(u.FilePath), u.Key, u.Field));
        var applied = 0;
        foreach (var row in Tsv.Read(path))
        {
            if (row.Length < 8)
            {
                warnings.Add(UiText.T("tsv.missingColumns", string.Join(" | ", row)));
                continue;
            }
            if (!index.TryGetValue((row[0], Norm(row[1]), row[2], row[3]), out var unit))
            {
                warnings.Add(UiText.T("tsv.unknownLine", row[0], row[1], row[2]));
                continue;
            }
            // La cle doit toujours designer le meme texte anglais : une cle decalee ecrirait la traduction au mauvais endroit.
            if (row[6].Length > 0 && row[6] != Tsv.Escape(unit.Source))
            {
                warnings.Add(UiText.T("tsv.sourceChanged", row[0], row[1], row[2]));
                continue;
            }
            if (row.Length >= 9 && unit.LinkedCode.Count > 0)
                unit.ApplyToLinkedCode = !IsNo(row[8]);
            if (row[7].Length == 0) continue;
            unit.Translation = Tsv.Unescape(row[7]);
            applied++;
        }
        Log.Info($"Import du TSV {path} : {applied} ligne(s) importees, {warnings.Count} ignoree(s)");
        foreach (var w in warnings) Log.Warn("TSV : " + w);
        return (applied, warnings);
    }

    /// <summary>
    /// Lignes a traduire (ou a verifier) qui ont deja une traduction dans un TSV (la sauvegarde automatique de l'appli),
    /// sans rien modifier ni rien ecrire dans le journal : sert a l'onglet Modlist.
    /// </summary>
    public int CountTranslatedIn(string tsvPath)
    {
        var open = Units.Where(u => u.Status is UnitStatus.ToTranslate or UnitStatus.ToCheck)
            .ToDictionary(u => (u.SourceName, Norm(u.FilePath), u.Key, u.Field), u => u.Source);
        var count = 0;
        // Meme regle que ImportTsv : une ligne dont l'anglais ne correspond plus (cle decalee) ne compte pas comme traduite.
        foreach (var row in Tsv.Read(tsvPath))
            if (row.Length >= 8 && row[7].Length > 0 && open.Remove((row[0], Norm(row[1]), row[2], row[3]), out var source) &&
                (row[6].Length == 0 || row[6] == Tsv.Escape(source)))
                count++;
        return count;
    }

    private static string Norm(string path) => ResourcePath.Sanitize(path);

    // -----------------------------------------------------------------------------------------
    // Construction du patch
    // -----------------------------------------------------------------------------------------

    private static readonly Regex TagPattern = new(
        @"\{[^{}]*\}|<[^<>]*>|\n|\\n|%[-+#0]*\d*(?:\.\d+)?[sdifxXcq]|\\\([^)]*\)",
        RegexOptions.Compiled);

    /// <summary>Balises a conserver ({...}, &lt;...&gt;, %s, \(...), retours a la ligne). Renvoie une description si elles different.</summary>
    public static string? CheckTags(string source, string translation)
    {
        var a = TagPattern.Matches(source).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var b = TagPattern.Matches(translation).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (a.SequenceEqual(b)) return null;
        string Show(IEnumerable<string> l) => string.Join(" ", l.Select(x => x == "\n" ? "\\n" : x));
        return UiText.T("tags.diff", Show(a), Show(b));
    }

    /// <summary>Suffixe court de la langue cible pour les noms : fr-fr -> FR, de-de -> DE, es-mx -> ES-MX.</summary>
    public static string LangSuffix(string lang)
    {
        var parts = (lang ?? "").Trim().ToUpperInvariant().Split('-');
        if (parts.Length == 2 && parts[0] == parts[1]) return parts[0];
        return string.Join("-", parts.Where(p => p.Length > 0));
    }

    /// <summary>Nom du patch : se charge avant l'archive d'origine (ordre alphabetique du jeu).</summary>
    public static string PatchArchiveName(string originalArchiveName, string suffix = "_FR")
    {
        var baseName = Path.GetFileNameWithoutExtension(originalArchiveName);
        var candidate = "#" + baseName + suffix + ".archive";
        while (string.CompareOrdinal(candidate, originalArchiveName) >= 0)
            candidate = "!" + candidate;
        return candidate;
    }

    /// <param name="outputName">Nom du mod traduit (par defaut : nom du mod + " - FR", ou le suffixe de la langue cible).</param>
    public BuildResult Build(string outputDir, ICompressor? writeCompressor, bool makeZip = true, string? outputName = null)
    {
        var result = new BuildResult();
        var changed = Units.Where(u => u.HasChange).ToList();
        result.ChangedLines = changed.Count;
        if (changed.Count == 0)
        {
            result.Warnings.Add(UiText.T("build.nothing"));
            Log.Info("Fabrication : aucune ligne modifiee, rien a fabriquer");
            return result;
        }

        foreach (var u in changed)
        {
            var tags = CheckTags(u.Source, u.Translation!);
            if (tags != null) result.Warnings.Add(UiText.T("build.tagsDiffer", u.FilePath, u.Key, tags));
        }

        var modDir = Path.Combine(outputDir, SafeName(string.IsNullOrWhiteSpace(outputName) ? Mod.Name + " - " + LangSuffix(TargetLang) : outputName));
        Log.Info($"Fabrication : {changed.Count} ligne(s) modifiee(s) vers {modDir}");
        if (Directory.Exists(modDir)) Directory.Delete(modDir, true);

        // Archives
        foreach (var archiveGroup in changed.Where(u => u.Loc != null).GroupBy(u => u.Loc!.ArchiveName))
        {
            var files = new List<(string Path, byte[] Data)>();
            foreach (var targetGroup in archiveGroup.GroupBy(u => u.Loc!))
            {
                var target = targetGroup.Key;
                var loc = LocalizationFile.TryLoad(target.TemplateBytes)
                          ?? throw new InvalidDataException(UiText.T("build.templateUnreadable", target.TargetPath));
                foreach (var u in targetGroup)
                {
                    if (!loc.ByKey.TryGetValue(u.Key, out var entry) || !entry.Set(u.Field, u.Translation!))
                        result.Warnings.Add(UiText.T("build.keyMissing", target.TargetPath, u.Key));
                }
                var bytes = loc.Save();

                // Verification : on relit ce qu'on vient d'ecrire
                var check = LocalizationFile.TryLoad(bytes) ?? throw new InvalidDataException(UiText.T("build.rereadFailed"));
                foreach (var u in targetGroup)
                    if (check.ByKey.TryGetValue(u.Key, out var e) && e.Get(u.Field) != u.Translation)
                        throw new InvalidDataException(UiText.T("build.verifyKey", u.Key));

                files.Add((target.TargetPath, bytes));
            }

            var archivePath = Path.Combine(modDir, "archive", "pc", "mod", PatchArchiveName(archiveGroup.Key, "_" + LangSuffix(TargetLang)));
            RedArchiveWriter.Write(archivePath, files, writeCompressor);

            // Verification : on relit l'archive produite
            using (var ar = RedArchive.Open(archivePath, _compressor))
            {
                foreach (var (p, data) in files)
                {
                    if (!ar.ByHash.TryGetValue(ResourcePath.Hash(p), out var e) || !ar.ReadMain(e).AsSpan().SequenceEqual(data))
                        throw new InvalidDataException(UiText.T("build.verifyArchive", p));
                }
            }
            result.Archives.Add(archivePath);
        }

        // Fichiers en clair : copie modifiee au meme chemin (le mod FR se place sous l'original dans MO2)
        var plainChanges = new Dictionary<PlainTarget, Dictionary<int, string>>();
        void SetChange(TextUnit u, string text)
        {
            if (!plainChanges.TryGetValue(u.Plain!, out var d)) plainChanges[u.Plain!] = d = new Dictionary<int, string>();
            d[u.LiteralIndex] = text;
        }
        foreach (var u in changed.Where(u => u.Plain != null)) SetChange(u, u.Translation!);

        // Comparaisons du code liees a un texte traduit : on y reporte la meme traduction.
        // Tous les textes identiques a la comparaison doivent etre traduits pareil (on ne sait pas lequel
        // alimente le test). Dans le doute, on ne touche pas au code et on previent.
        foreach (var code in Units.Where(u => u.Plain != null && u.LinkedTexts.Count > 0))
        {
            if (code.HasChange) continue; // l'utilisateur l'a traduite lui-meme
            var texts = code.LinkedTexts;
            if (texts.Any(t => t.HasChange && !t.ApplyToLinkedCode)) continue; // report refuse par l'utilisateur
            var translated = texts.Where(t => t.HasChange && t.ApplyToLinkedCode).ToList();
            if (translated.Count == 0) continue;
            var values = translated.Select(t => t.Translation!).Distinct().ToList();
            if (translated.Count != texts.Count || values.Count > 1)
            {
                var list = string.Join(" ; ", texts.Select(t => UiText.T("build.linkItem",
                    t.HasChange ? UiText.T("build.quoted", t.Translation) : UiText.T("build.notTranslated"), ShortWhere(t))));
                result.Warnings.Add(UiText.T("build.linkMismatch", code.Source, ShortWhere(code), list));
                continue;
            }
            SetChange(code, values[0]);
            result.Infos.Add(UiText.T("build.linkApplied", code.Source, values[0], Where(code)));
        }

        foreach (var (target, replacements) in plainChanges)
        {
            var bytes = target.File.Apply(replacements);
            var outPath = Path.Combine(modDir, target.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllBytes(outPath, bytes);

            // Verification : on relit le fichier et on compare chaque chaine
            var check = PlainTextFile.Load(outPath, target.RelativePath);
            if (check.Literals.Count != target.File.Literals.Count)
                throw new InvalidDataException(UiText.T("build.verifyCount", target.RelativePath));
            for (var i = 0; i < check.Literals.Count; i++)
            {
                var expected = replacements.TryGetValue(i, out var t) ? target.File.Literals[i].Normalize(t) : target.File.Literals[i].Value;
                if (check.Literals[i].Value != expected)
                    throw new InvalidDataException(UiText.T("build.verifyString", target.RelativePath, i));
            }
            result.PlainFiles.Add(outPath);
        }

        // Textes traduits lies a une comparaison mais dont le report a ete refuse : on le rappelle
        foreach (var u in changed.Where(u => u.LinkedCode.Count > 0 && !u.ApplyToLinkedCode))
            result.Warnings.Add(UiText.T("build.linkRefused", u.Source, string.Join(" ; ", u.LinkedCode.Select(Where))));

        if (makeZip)
        {
            var zip = modDir + ".zip";
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(modDir, zip, CompressionLevel.Optimal, false);
            result.Zip = zip;
        }
        Log.Info($"Fabrication terminee : {result.Archives.Count} archive(s), {result.PlainFiles.Count} fichier(s) en clair" +
                 (result.Zip != null ? $", {result.Zip}" : ""));
        foreach (var info in result.Infos) Log.Info("Fabrication : " + info);
        foreach (var w in result.Warnings) Log.Warn("Fabrication : " + w);
        return result;
    }

    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }

    public void Dispose() => Mod.Dispose();
}

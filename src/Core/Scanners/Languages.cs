using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

/// <summary>
/// Reconnait les textes deja fournis dans une autre langue que l'anglais : fichiers (tr_TR.lua, German.reds,
/// monmod.es-es.json, dossier ru-ru/) et blocs d'un meme fichier (["de-de"] = { ... }). Ces textes sont des
/// traductions existantes, pas du texte a traduire : l'outil ne les propose pas et n'y touche pas.
/// </summary>
internal static class Languages
{
    private static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "de", "fr", "es", "it", "pl", "pt", "ru", "ja", "jp", "ko", "kr", "zh", "cn", "ar", "cs", "cz", "hu", "th", "tr", "uk", "ua",
    };

    private static readonly HashSet<string> Regions = new(StringComparer.OrdinalIgnoreCase)
    {
        "us", "gb", "uk", "au", "de", "at", "ch", "fr", "ca", "be", "es", "mx", "la", "it", "pl", "pt", "br", "ru", "jp", "kr", "cn", "tw",
        "hk", "ar", "sa", "cz", "hu", "th", "tr", "ua",
    };

    private static readonly string[] Names =
    {
        "german", "deutsch", "russian", "french", "francais", "spanish", "espanol", "mexican", "italian", "polish",
        "portuguese", "brazilian", "japanese", "korean", "chinese", "arabic", "czech", "hungarian", "thai", "turkish",
        "ukrainian",
    };

    private static readonly string[] LocalizationFolders = { "lang", "langs", "language", "languages", "locale", "locales", "localization", "localisation", "i18n", "translations" };

    /// <summary>Code "xx-YY" ou "xx_YY" isole dans un nom : "tr_TR", "monmod.es-es", "ru-ru".</summary>
    private static readonly Regex Pair = new(@"(?<![A-Za-z])(?<l>[A-Za-z]{2})[-_](?<r>[A-Za-z]{2})(?![A-Za-z])", RegexOptions.Compiled);

    private static readonly Regex Separators = new(@"[._\-\s]+", RegexOptions.Compiled);

    /// <summary>Le nom contient un code de langue "xx-YY" : renvoie la langue ("tr"), sinon null.</summary>
    private static string? PairLanguage(string name)
    {
        foreach (Match m in Pair.Matches(name))
            if (Codes.Contains(m.Groups["l"].Value) && Regions.Contains(m.Groups["r"].Value))
                return m.Groups["l"].Value.ToLowerInvariant();
        return null;
    }

    public static bool IsEnglishName(string name)
    {
        var n = name.ToLowerInvariant();
        return n is "english" or "en" or "enus" || PairLanguage(n) == "en";
    }

    /// <summary>Nom (de fichier sans extension, de classe redscript) qui designe une autre langue que l'anglais.</summary>
    /// <param name="loose">Accepte aussi un nom de langue au milieu du nom ("MyModGerman") et un code de deux lettres seul ("de").</param>
    public static bool IsOtherLanguageName(string name, bool loose)
    {
        var n = name.ToLowerInvariant();
        var pair = PairLanguage(n);
        if (pair != null) return pair != "en";
        var tokens = Separators.Split(n).Where(t => t.Length > 0).ToList();
        if (tokens.Any(t => Names.Contains(t))) return true;
        if (!loose) return false;
        if (Names.Any(n.Contains)) return true;
        return n.Length == 2 && n != "en" && Codes.Contains(n);
    }

    /// <summary>Fichier d'une autre langue, d'apres son nom et ses dossiers.</summary>
    public static bool IsOtherLanguageFile(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var stem = Path.GetFileNameWithoutExtension(parts[^1]);
        var folders = parts[..^1];
        // .json : regle large d'origine (de.json, MyModGerman.json) ; ailleurs seulement dans un dossier de langues
        var loose = parts[^1].EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    folders.Any(f => LocalizationFolders.Contains(f, StringComparer.OrdinalIgnoreCase));
        if (IsOtherLanguageName(stem, loose)) return true;
        // Dossier au nom d'une langue : localization/ru-ru/onscreens.json
        return folders.Any(f =>
        {
            var m = Pair.Match(f);
            return m.Success && m.Length == f.Length && PairLanguage(f) is { } l && l != "en";
        });
    }

    /// <summary>Cle d'un bloc de textes par langue : ["de-de"] = { ... }, "fr_FR": { ... }, ru = { ... }.</summary>
    public static bool IsOtherLanguageKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 5) return false;
        if (key.Length == 2) return !key.Equals("en", StringComparison.OrdinalIgnoreCase) && Codes.Contains(key);
        var m = Pair.Match(key);
        return m.Success && m.Length == key.Length && PairLanguage(key) is { } l && l != "en";
    }

    private static readonly Regex EnglishKey = new(@"[\[""'\s{,](en|en[-_](us|gb))[\]""'\s]*\s*[=:]\s*[\{\[]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Le fichier range ses textes par langue et contient un bloc anglais (["en-us"] = { ... }) : les blocs des autres
    /// langues sont alors des traductions. Sans bloc anglais on ne conclut rien (une cle "it" ou "tr" peut etre autre chose).
    /// </summary>
    public static bool HasEnglishBlock(string content) => EnglishKey.IsMatch(content);
}

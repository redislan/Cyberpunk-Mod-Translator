using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

/// <summary>Regles qui dependent du fichier entier (bibliotheques connues, autres langues, mots repetes).</summary>
internal static class FileRules
{
    /// <summary>Bibliotheques CET tres repandues (psiberx, rxi...) : aucun texte affiche dedans.</summary>
    private static readonly HashSet<string> LuaLibraries = new(StringComparer.OrdinalIgnoreCase)
    {
        "GameUI.lua", "GameSettings.lua", "GameSession.lua", "GameHUD.lua", "GameLocale.lua", "Cron.lua", "Ref.lua",
        "EventProxy.lua", "json.lua", "dkjson.lua", "inspect.lua", "middleclass.lua", "lume.lua", "TweakDB.lua",
    };

    private static readonly string[] OtherLanguages =
    {
        "german", "deutsch", "russian", "french", "francais", "spanish", "espanol", "mexican", "italian", "polish",
        "portuguese", "brazilian", "japanese", "korean", "chinese", "arabic", "czech", "hungarian", "thai", "turkish",
        "ukrainian",
    };

    private static readonly Regex LangCode = new(@"^(?<l>[a-z]{2})([-_][a-z]{2})?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LocPackage = new(@"class\s+(?<c>\w+)\s+extends\s+ModLocalizationPackage", RegexOptions.Compiled);

    public static void Apply(string relativePath, string content, List<StringLiteral> literals)
    {
        var file = Path.GetFileName(relativePath);
        var stem = Path.GetFileNameWithoutExtension(relativePath);
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();

        // 1. Bibliotheques connues
        if (ext == ".lua" && LuaLibraries.Contains(file))
        {
            SetAll(literals, StringKind.Code);
            return;
        }

        // 2. Traductions deja fournies dans une autre langue (German.reds, de-de.json...) : on n'y touche pas
        var package = ext == ".reds" ? LocPackage.Match(content) : Match.Empty;
        var langName = package.Success ? package.Groups["c"].Value : stem;
        if ((package.Success || ext == ".json") && IsOtherLanguage(langName))
        {
            SetAll(literals, StringKind.Code);
            return;
        }

        // 3. Fichier de traduction anglais (localization.json, en-us.json) : les valeurs sont du texte
        if (ext == ".json" && (IsEnglish(stem) || stem.Contains("locali", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var l in literals)
                if (l.Kind == StringKind.Maybe) l.Kind = StringKind.Text;
        }

        // 4. Un mot repete dix fois ou plus dans le meme fichier : une valeur de base de donnees ("Ped", "Ped"...)
        foreach (var group in literals.Where(l => l.Kind == StringKind.Maybe && !l.Value.Contains(' ')).GroupBy(l => l.Value))
        {
            if (literals.Count(l => l.Value == group.Key) < 10) continue;
            foreach (var l in group) l.Kind = StringKind.Code;
        }
    }

    private static void SetAll(List<StringLiteral> literals, StringKind kind)
    {
        foreach (var l in literals) l.Kind = kind;
    }

    private static bool IsEnglish(string name)
    {
        var n = name.ToLowerInvariant();
        return n is "english" or "en" or "en-us" or "en_us" or "en-gb" or "enus";
    }

    private static bool IsOtherLanguage(string name)
    {
        var n = name.ToLowerInvariant();
        if (OtherLanguages.Any(n.Contains)) return true;
        var m = LangCode.Match(n);
        return m.Success && m.Groups["l"].Value != "en" && n is not ("id" or "ui" or "db" or "ok");
    }
}

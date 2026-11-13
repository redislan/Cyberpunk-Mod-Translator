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

        // 2. Traductions deja fournies dans une autre langue (German.reds, de-de.json, Localization/tr_TR.lua,
        //    localization/ru-ru/onscreens.json...) : ce n'est pas du texte a traduire, on n'y touche pas
        var package = ext == ".reds" ? LocPackage.Match(content) : Match.Empty;
        if ((package.Success && Languages.IsOtherLanguageName(package.Groups["c"].Value, loose: true)) ||
            Languages.IsOtherLanguageFile(relativePath))
        {
            SetAll(literals, StringKind.Code);
            return;
        }

        // 3. Fichier de traduction anglais (localization.json, en-us.json) : les valeurs sont du texte
        if (ext == ".json" && (Languages.IsEnglishName(stem) || stem.Contains("locali", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var l in literals)
                if (l.Kind == StringKind.Maybe) l.Kind = StringKind.Text;
        }

        // 4. Un mot repete dix fois ou plus dans le meme fichier : une valeur de base de donnees ("Ped", "Ped"...)
        // (compte fait une seule fois : recompter pour chaque mot bloquait l'analyse des gros fichiers)
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var l in literals)
            occurrences[l.Value] = occurrences.GetValueOrDefault(l.Value) + 1;
        foreach (var l in literals)
            if (l.Kind == StringKind.Maybe && !l.Value.Contains(' ') && occurrences[l.Value] >= 10)
                l.Kind = StringKind.Code;
    }

    private static void SetAll(List<StringLiteral> literals, StringKind kind)
    {
        foreach (var l in literals) l.Kind = kind;
    }
}

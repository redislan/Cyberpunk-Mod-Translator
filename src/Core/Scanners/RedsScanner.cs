using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

/// <summary>Redscript (.reds) et ancien format TweakXL (.tweak).</summary>
internal sealed class RedsScanner : SourceScanner
{
    public RedsScanner(string source) : base(source)
    {
    }

    public override List<StringLiteral> Scan()
    {
        var s = S;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                var nl = s.IndexOf('\n', i);
                var from = i;
                i = nl < 0 ? s.Length : nl + 1;
                MarkRead(from, i);
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var from = i;
                i = end < 0 ? s.Length : end + 2;
                MarkRead(from, i);
                continue;
            }
            if (c == '"')
            {
                // prefixe eventuel : n"" (nom), r"" (ressource), t"" (TweakDB), l"" (LocKey), s"" (interpolation)
                var prefix = i > 0 && char.IsLetter(s[i - 1]) && (i < 2 || !(char.IsLetterOrDigit(s[i - 2]) || s[i - 2] == '_'))
                    ? s[i - 1]
                    : '\0';
                var (value, raw, end) = StringSyntax.ReadReds(s, i);
                if (prefix == 's') (raw, end) = StringSyntax.ReadRedsInterpolated(s, i);
                if (prefix is 'n' or 'r' or 't' or 'l')
                {
                    MarkRead(i, end);
                    i = end;
                    continue; // jamais du texte a traduire
                }
                var (call, arg) = CallAt(prefix == '\0' ? i : i - 1);
                MarkRead(i, end);
                var interpolated = prefix == 's';
                var kind = TextClassifier.ClassifyInContext(interpolated ? raw : value, call, arg, "reds");
                var lineStart = s.LastIndexOf('\n', Math.Max(0, i - 1)) + 1;
                var before = s[lineStart..(prefix == '\0' ? i : i - 1)].TrimEnd();
                var afterText = s[end..Math.Min(s.Length, end + 4)].TrimStart();
                var comparison = before.EndsWith("==") || before.EndsWith("!=") || afterText.StartsWith("==") || afterText.StartsWith("!=") ||
                                 Regex.IsMatch(before, @"\bcase$") ||
                                 call is "Equals" or "NotEquals" or "StrContains" or "StrBeginsWith" or "StrEndsWith" or "StrFindFirst" or "StrCmp";
                if (comparison)
                    kind = StringKind.Code;
                else if (ModSettingsProperty.Match(before) is { Success: true } msp)
                    kind = ModSettingsKind(msp.Groups["p"].Value, interpolated ? raw : value, kind);
                if (interpolated && kind == StringKind.Text) kind = StringKind.Maybe;
                Add(i, end, interpolated ? raw : value, kind,
                    interpolated
                        ? v => "\"" + StringSyntax.EscapeRedsRaw(v) + "\"" // le prefixe s reste devant, hors de la plage remplacee
                        : v => "\"" + StringSyntax.EscapeCStyle(v, '"') + "\"",
                    comparison,
                    interpolated ? StringSyntax.EscapeRedsRaw : null);
                i = end;
                continue;
            }
            i++;
        }
        return Literals;
    }

    /// <summary>@runtimeProperty("ModSettings.xxx", "valeur") : la propriete dit si la valeur est affichee.</summary>
    private static readonly Regex ModSettingsProperty = new(@"@runtimeProperty\(\s*""ModSettings\.(?<p>[A-Za-z0-9_.]+)""\s*,$", RegexOptions.Compiled);

    /// <summary>Cle de texte du jeu en plusieurs morceaux separes par des tirets, dont un peut contenir une espace.</summary>
    private static readonly Regex LocKeyWithSpace = new(@"^[A-Z0-9]\w*(-[A-Z0-9]\w*( [A-Z0-9]\w*)?){3,}$", RegexOptions.Compiled);

    private static StringKind ModSettingsKind(string property, string value, StringKind kind)
    {
        var p = property.ToLowerInvariant();
        if (p.EndsWith(".order")) return StringKind.Code;
        var displayed = p is "mod" or "category" or "displayname" or "description" or "title" or "tooltip" || p.StartsWith("displayvalues");
        if (!displayed) return StringKind.Code; // step, min, max, dependency, updatePolicy...
        // une cle de traduction ("IH.Core.mod.Name", "UI-Settings-KeyBindings", "Gameplay-Items-Item Type-Cyb_Launcher") reste une cle
        if (LocKeyWithSpace.IsMatch(value)) return StringKind.Code;
        return kind == StringKind.Code || !Regex.IsMatch(value, @"[A-Za-z\u00C0-\u024F]") ? StringKind.Code : StringKind.Text;
    }


}

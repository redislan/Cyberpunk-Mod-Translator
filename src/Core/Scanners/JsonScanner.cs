namespace CyberpunkModTranslator.Core;

/// <summary>Fichiers JSON en clair (reglages et traductions des mods CET, cartes RedscriptConfigFramework...).</summary>
internal sealed class JsonScanner : SourceScanner
{
    public JsonScanner(string source) : base(source)
    {
    }

    public override List<StringLiteral> Scan()
    {
        var s = S;
        string? lastKey = null;
        // Blocs de textes par langue ({ "en-us": { ... }, "de-de": { ... } }) : ceux des autres langues sont des traductions
        var languageBlocks = Languages.HasEnglishBlock(s);
        var blocks = new Stack<bool>();
        var otherLanguage = 0;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c is '{' or '[')
            {
                var other = languageBlocks && Languages.IsOtherLanguageKey(lastKey);
                blocks.Push(other);
                if (other) otherLanguage++;
                i++;
                continue;
            }
            if (c is '}' or ']')
            {
                if (blocks.Count > 0 && blocks.Pop()) otherLanguage--;
                i++;
                continue;
            }
            if (c != '"')
            {
                i++;
                continue;
            }
            var (value, end) = StringSyntax.ReadJson(s, i);
            var j = end;
            while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
            var isKey = j < s.Length && s[j] == ':';
            if (isKey) lastKey = value;
            else
            {
                var kind = otherLanguage > 0
                    ? StringKind.Code
                    : TextClassifier.ApplyKeyHint(TextClassifier.Classify(value), lastKey);
                Add(i, end, value, kind, StringSyntax.EncodeJson);
            }
            i = end;
        }
        return Literals;
    }
}

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
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] != '"')
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
                Add(i, end, value, TextClassifier.ApplyKeyHint(TextClassifier.Classify(value), lastKey), StringSyntax.EncodeJson);
            }
            i = end;
        }
        return Literals;
    }


}

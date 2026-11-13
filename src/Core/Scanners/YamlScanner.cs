using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

/// <summary>YAML de TweakXL (r6/tweaks) et des autres outils (Audioware...).</summary>
internal sealed class YamlScanner : SourceScanner
{
    public YamlScanner(string source) : base(source)
    {
    }

    /// <summary>Champs TweakXL qui contiennent une cle de traduction, pas le texte lui-meme.</summary>
    private static readonly HashSet<string> LocKeyFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "displayName", "localizedDescription", "localizedName", "localizedShortDescription", "description",
        "shortDescription", "title", "subtitle", "displayNameKey", "uiName", "fullName", "name",
    };

    // "- " est atomique (?>...) et la cle ne peut pas commencer par { ou [ : une ligne "- { tier: X, ... }" (mapping en ligne)
    // n'est pas une cle "{ tier", ni une cle "- { tier" par retour en arriere. Elle passe alors par ListItem, puis est ignoree ({).
    private static readonly Regex KeyValue = new(@"^(?<indent>\s*)(?>(?:-\s+)?)(?<key>[^\s:#'""{\[][^:#]*?|'[^']*'|""[^""]*"")\s*:(?:\s+(?<value>.*))?$", RegexOptions.Compiled);
    private static readonly Regex ListItem = new(@"^(?<indent>\s*)-\s+(?<value>.+)$", RegexOptions.Compiled);

    public override List<StringLiteral> Scan()
    {
        var s = S;
        var offset = 0;
        var lines = s.Split('\n');
        for (var lineNo = 0; lineNo < lines.Length; lineNo++)
        {
            var rawLine = lines[lineNo];
            var lineStart = offset;
            offset += rawLine.Length + 1;
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            string? key = null;
            Group? valueGroup = null;
            // Colonne a partir de laquelle une ligne suivante plus decalee est la suite de la valeur :
            // celle de la cle ("- nom: valeur" : les cles voisines sont alignees sur "nom"), sinon celle du tiret.
            var ownColumn = Indent(line);
            var kv = KeyValue.Match(line);
            if (kv.Success)
            {
                ownColumn = kv.Groups["key"].Index;
                key = kv.Groups["key"].Value.Trim().Trim('\'', '"');
                if (kv.Groups["value"].Success) valueGroup = kv.Groups["value"];
            }
            else
            {
                var li = ListItem.Match(line);
                if (li.Success) valueGroup = li.Groups["value"];
            }
            if (valueGroup == null || valueGroup.Length == 0) continue;

            var vStart = valueGroup.Index;
            var vText = valueGroup.Value;
            // "cle: # commentaire" : pas de valeur sur la ligne (le contenu suit en dessous). Le commentaire n'est pas du texte :
            // le remplacer par une chaine casserait le fichier (TweakXL : "end of map not found").
            if (vText.StartsWith('#')) continue;
            if (vText.StartsWith('[') || vText.StartsWith('{') || vText.StartsWith('|') || vText.StartsWith('>') ||
                vText.StartsWith('&') || vText.StartsWith('*') || vText.StartsWith('!')) continue;

            string value;
            int length;
            if (vText[0] is '"' or '\'')
            {
                var (quoted, quotedLength, closed) = StringSyntax.ReadYamlQuoted(vText);
                if (!closed) continue;
                value = quoted;
                length = quotedLength;
            }
            else
            {
                // valeur simple : jusqu'a un commentaire " #"
                var hash = vText.IndexOf(" #", StringComparison.Ordinal);
                var tabHash = vText.IndexOf("\t#", StringComparison.Ordinal);
                if (tabHash >= 0 && (hash < 0 || tabHash < hash)) hash = tabHash;
                var plain = (hash >= 0 ? vText[..hash] : vText).TrimEnd();
                value = plain;
                length = plain.Length;
                // Valeur sans guillemets qui continue sur la ligne suivante (ligne plus decalee) : la remplacer par
                // une chaine entre guillemets laisserait la suite orpheline et casserait le fichier. On n'y touche pas.
                if (ContinuesBelow(lines, lineNo, ownColumn)) continue;
                if (value.ToLowerInvariant() is "true" or "false" or "null" or "~" or "yes" or "no" or "on" or "off") continue;
            }

            StringKind kind;
            if (key != null && key.StartsWith('$')) kind = StringKind.Code;
            else
            {
                kind = TextClassifier.ApplyKeyHint(TextClassifier.Classify(value), key);
                if (key != null && LocKeyFields.Contains(key) && kind != StringKind.Code)
                    kind = value.Contains(' ') ? StringKind.Maybe : StringKind.Code; // champ LocKey : cle ou texte brut ?
                // TweakXL : un mot seul (Passenger, Driver, Vehicle...) est une valeur d'enumeration
                else if (kind == StringKind.Maybe && !value.Contains(' ')) kind = StringKind.Code;
            }

            Add(lineStart + vStart, lineStart + vStart + length, value, kind, StringSyntax.EncodeYaml);
        }
        return Literals;
    }

    private static int Indent(string line)
    {
        var n = 0;
        while (n < line.Length && (line[n] == ' ' || line[n] == '\t')) n++;
        return n;
    }

    /// <summary>La prochaine ligne non vide (hors commentaires) est plus decalee que la colonne donnee.</summary>
    private static bool ContinuesBelow(string[] lines, int index, int indent)
    {
        for (var k = index + 1; k < lines.Length; k++)
        {
            var next = lines[k].TrimEnd('\r');
            var trimmed = next.TrimStart();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith('#')) continue;
            return Indent(next) > indent;
        }
        return false;
    }
}

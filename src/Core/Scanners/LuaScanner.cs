using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

/// <summary>Fichiers Lua des mods CET (Cyber Engine Tweaks).</summary>
internal sealed class LuaScanner : SourceScanner
{
    public LuaScanner(string source) : base(source)
    {
    }

    /// <summary>Un element d'une table { a, b, c } : une seule chaine, une valeur simple, ou autre chose.</summary>
    private sealed class TableItem
    {
        public int Literal = -1;
        public bool Scalar; // nombre, true, false, nil
        public bool Other;  // expression, cle = valeur, table imbriquee...
        public bool IsEmpty => Literal < 0 && !Scalar && !Other;
    }

    /// <summary>Nombre de tables ouvertes qui sont un bloc de textes d'une autre langue (["de-de"] = { ... }).</summary>
    private int OtherLanguageDepth;

    private sealed class TableFrame
    {
        public bool OtherLanguage;
        public int Paren;
        public readonly List<TableItem> Items = new();
        public TableItem Cur = new();

        public void EndItem()
        {
            if (!Cur.IsEmpty) Items.Add(Cur);
            Cur = new TableItem();
        }
    }

    public override List<StringLiteral> Scan()
    {
        var s = S;
        var list = Literals;
        var frames = new Stack<TableFrame>();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var labels = new HashSet<int>();
        var languageBlocks = Languages.HasEnglishBlock(s);

        void Other()
        {
            if (frames.Count > 0) frames.Peek().Cur.Other = true;
        }

        void Literal(int index)
        {
            if (frames.Count == 0) return;
            var f = frames.Peek();
            if (f.Paren > 0 || !f.Cur.IsEmpty) f.Cur.Other = true;
            else f.Cur.Literal = index;
        }

        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                var level = LongBracketLevel(s, i + 2);
                var from = i;
                if (level >= 0) i = SkipLongBracket(s, i + 2, level);
                else
                {
                    var nl = s.IndexOf('\n', i);
                    i = nl < 0 ? s.Length : nl + 1;
                }
                MarkRead(from, i);
                continue;
            }
            if (c is '"' or '\'')
            {
                var (value, end, ok) = StringSyntax.ReadLua(s, i, c);
                if (ok)
                {
                    AddLua(i, end, value, c);
                    Literal(list.Count - 1);
                }
                else Other();
                i = end;
                continue;
            }
            if (c == '[')
            {
                var level = LongBracketLevel(s, i);
                if (level >= 0)
                {
                    var openLen = level + 2;
                    var close = "]" + new string('=', level) + "]";
                    var closeAt = s.IndexOf(close, i + openLen, StringComparison.Ordinal);
                    var end = closeAt < 0 ? s.Length : closeAt + close.Length;
                    var content = closeAt < 0 ? "" : s[(i + openLen)..closeAt];
                    if (content.StartsWith("\r\n")) content = content[2..];
                    else if (content.StartsWith('\n')) content = content[1..];
                    AddLua(i, end, content, '"');
                    Literal(list.Count - 1);
                    i = end;
                    continue;
                }
            }

            // Structure des tables, pour reconnaitre les lignes de reglages et les listes d'identifiants
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (c == '{')
            {
                Other(); // la table imbriquee compte comme "autre chose" pour la table parente
                var frame = new TableFrame();
                if (languageBlocks)
                {
                    var lineStart = s.LastIndexOf('\n', Math.Max(0, i - 1)) + 1;
                    var key = KeyBefore.Match(s[lineStart..i]);
                    frame.OtherLanguage = key.Success && Languages.IsOtherLanguageKey(key.Groups["k"].Value);
                    if (frame.OtherLanguage) OtherLanguageDepth++;
                }
                frames.Push(frame);
                i++;
                continue;
            }
            if (c == '}')
            {
                if (frames.Count > 0)
                {
                    var f = frames.Pop();
                    if (f.OtherLanguage) OtherLanguageDepth--;
                    f.EndItem();
                    ClassifyTable(f, list, identifiers, labels);
                }
                i++;
                continue;
            }
            if (frames.Count > 0)
            {
                var f = frames.Peek();
                if (c == '(') f.Paren++;
                else if (c == ')' && f.Paren > 0) f.Paren--;
                if ((c is ',' or ';') && f.Paren == 0)
                {
                    f.EndItem();
                    i++;
                    continue;
                }
                if (char.IsDigit(c) || (c is '-' or '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    var j = i + 1;
                    while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '.')) j++;
                    if (f.Cur.IsEmpty && f.Paren == 0) f.Cur.Scalar = true;
                    else f.Cur.Other = true;
                    i = j;
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    var j = i + 1;
                    while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                    var word = s[i..j];
                    if (word is "true" or "false" or "nil" && f.Cur.IsEmpty && f.Paren == 0) f.Cur.Scalar = true;
                    else f.Cur.Other = true;
                    i = j;
                    continue;
                }
                f.Cur.Other = true;
            }
            i++;
        }

        // Une valeur qui sert d'identifiant dans une table (cle de reglage, liste d'identifiants) reste un identifiant
        // partout dans le fichier : return 'Cloudy', {'Rain', {...}}...
        for (var k = 0; k < list.Count; k++)
        {
            var l = list[k];
            if (l.Kind == StringKind.Maybe && !l.IsComparison && !labels.Contains(k) && !l.Value.Any(char.IsWhiteSpace) &&
                identifiers.Contains(l.Value))
                l.Kind = StringKind.Code;
        }
        return list;
    }

    private static readonly Regex IdentifierStyle = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Deux formes de tables qui ne contiennent pas de texte affiche, sauf leurs libelles :
    /// <list type="bullet">
    /// <item>ligne de reglage <c>{'Clouds', 'Density', 'Densite des nuages', 0, 1, .5, 'f'}</c> : les mots seuls avant le premier
    /// libelle (avec espaces) sont des cles, lues par le mod ou ecrites dans un .ini ;</item>
    /// <item>liste d'identifiants <c>{'Clear', 'FewClouds', 'LightRain', ...}</c> : au moins trois noms en CamelCase/snake_case et
    /// aucune phrase, donc tous les mots de la liste sont des identifiants.</item>
    /// </list>
    /// </summary>
    private static void ClassifyTable(TableFrame f, List<StringLiteral> list, HashSet<string> identifiers, HashSet<int> labels)
    {
        var items = f.Items;
        if (items.Count < 3 || items.Any(it => it.Other)) return;
        bool HasSpace(TableItem it) => it.Literal >= 0 && list[it.Literal].Value.Any(char.IsWhiteSpace);

        var firstScalar = items.FindIndex(it => it.Scalar);
        if (firstScalar >= 0)
        {
            // Libelle : la premiere chaine avec des espaces, sinon la derniere chaine avant les nombres
            // ({'Clouds', 'Density', 'Density', .01, .3, .08, 'f'} : cle, cle, libelle)
            var label = items.FindIndex(HasSpace);
            if (label < 0)
            {
                var before = Enumerable.Range(0, firstScalar).Where(k => items[k].Literal >= 0).ToList();
                if (before.Count < 2 || before.Count != firstScalar) return;
                label = before[^1];
            }
            labels.Add(items[label].Literal);
            for (var k = 0; k < label; k++)
            {
                if (items[k].Literal < 0) continue;
                var lit = list[items[k].Literal];
                if (lit.Value.Length == 0 || lit.Value.Any(char.IsWhiteSpace)) continue;
                lit.Kind = StringKind.Code;
                identifiers.Add(lit.Value);
            }
            return;
        }

        if (items.All(it => it.Literal >= 0) && !items.Any(HasSpace))
        {
            var lits = items.Select(it => list[it.Literal]).ToList();
            var idStyle = lits.Count(l => IdentifierStyle.IsMatch(l.Value) && TextClassifier.Classify(l.Value) == StringKind.Code &&
                                          (l.Value.Contains('_') || l.Value.Skip(1).Any(char.IsUpper)));
            if (idStyle < 3) return;
            foreach (var l in lits)
            {
                if (l.Kind != StringKind.Code) l.Kind = StringKind.Code;
                identifiers.Add(l.Value);
            }
        }
    }

    private static readonly Regex KeyBefore = new(@"(?:(?<k>[A-Za-z_]\w*)|\[\s*['""](?<k>[^'""]+)['""]\s*\])\s*=\s*$", RegexOptions.Compiled);

    private static readonly Regex KeyPrefix = new(@"^[A-Za-z_][\w]*[._:]$", RegexOptions.Compiled);

    private void AddLua(int start, int end, string value, char quote)
    {
        var s = S;
        var (call, arg) = CallAt(start);
        MarkRead(start, end);
        var kind = TextClassifier.ClassifyInContext(value, call, arg, "lua");

        var lineStart = s.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        var before = s[lineStart..start];
        var lineEnd = s.IndexOf('\n', end);
        var after = s[end..(lineEnd < 0 ? s.Length : lineEnd)].TrimStart();

        // Comparaison (x == "Menu") ou cle de table (["Menu"] = ...) : identifiant
        var trimmedBefore = before.TrimEnd();
        var comparison = trimmedBefore.EndsWith("==") || trimmedBefore.EndsWith("~=") || after.StartsWith("==") || after.StartsWith("~=");
        var tableKey = trimmedBefore.EndsWith('[') && after.StartsWith(']'); // t["cle"] : nom de champ, on n'y touche pas
        // 'Weather.' .. name : debut d'une cle construite
        var keyPrefix = after.StartsWith("..") && KeyPrefix.IsMatch(value);
        if (comparison || tableKey || keyPrefix)
            kind = StringKind.Code;
        else
        {
            var m = KeyBefore.Match(before);
            if (m.Success && !trimmedBefore.EndsWith("=="))
            {
                var key = m.Groups["k"].Value;
                kind = key == value ? StringKind.Code : TextClassifier.ApplyKeyHint(kind, key); // Menu = 'Menu' : enumeration
            }
        }

        if (OtherLanguageDepth > 0) kind = StringKind.Code; // bloc ["de-de"] = { ... } : traduction existante
        Add(start, end, value, kind, v => quote + StringSyntax.EscapeCStyle(v, quote) + quote, comparison);
    }

    private static int LongBracketLevel(string s, int i)
    {
        if (i >= s.Length || s[i] != '[') return -1;
        var j = i + 1;
        var level = 0;
        while (j < s.Length && s[j] == '=')
        {
            level++;
            j++;
        }
        return j < s.Length && s[j] == '[' ? level : -1;
    }

    private static int SkipLongBracket(string s, int i, int level)
    {
        var close = "]" + new string('=', level) + "]";
        var at = s.IndexOf(close, i + level + 2, StringComparison.Ordinal);
        return at < 0 ? s.Length : at + close.Length;
    }

}

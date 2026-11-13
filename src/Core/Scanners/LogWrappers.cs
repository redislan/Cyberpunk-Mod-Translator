using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

/// <summary>
/// Fonctions maison d'un mod qui ne font qu'ecrire dans le journal, par exemple (Wannabe Edgerunner) :
/// <code>public func E(str: String) -> Void { if EdgerunnerLogs.IsEnabled() { ModLog(n"Edgerunner", str); }; }</code>
/// Leur nom ("E") ne dit pas que c'est un journal : sans les reconnaitre, tous les messages de debug du mod
/// sortiraient en « a traduire ». On lit donc le corps des petites fonctions : si chaque ligne est un appel de
/// journal (ou un if / return / fin de bloc), la fonction est un journal et ses textes sont du code.
/// <para>
/// Une fonction qui fait autre chose en plus (afficher le texte a l'ecran...) n'est pas retenue : dans le doute,
/// le texte reste propose a la traduction.
/// </para>
/// </summary>
internal static class LogWrappers
{
    [ThreadStatic] private static HashSet<string>? _current;

    /// <summary>Fonctions de journal du mod en cours d'analyse (propre a chaque fil d'execution).</summary>
    public static bool Contains(string name) => _current != null && _current.Contains(name);

    /// <summary>Declare les fonctions de journal trouvees pour la duree d'une analyse ; a refermer avec Dispose.</summary>
    public static IDisposable Use(HashSet<string> names)
    {
        var previous = _current;
        _current = names;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly HashSet<string>? _previous;
        public Scope(HashSet<string>? previous) => _previous = previous;
        public void Dispose() => _current = _previous;
    }

    private const int MaxBody = 600;

    private static readonly Regex RedsFunc = new(@"\bfunc\s+(?<n>\w+)\s*\((?<a>[^)]*)\)\s*(->\s*[\w<>:\[\] ]+?\s*)?\{", RegexOptions.Compiled);
    private static readonly Regex LuaFunc = new(@"\bfunction\s+(?<n>[\w.:]+)\s*\((?<a>[^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex LogCall = new(
        @"^(ModLog|FTLog\w*|LogChannel\w*|Log|LogError|LogWarning|LogInfo|LogDebug|RedLog[.:]\w+|print|spdlog[.:]\w+)\s*\(", RegexOptions.Compiled);
    private static readonly Regex Statement = new(@"^(?<n>[A-Za-z_][\w.:]*)\s*\(", RegexOptions.Compiled);
    // Lignes sans effet sur le verdict : debut et fin de bloc, return. Un if qui contient autre chose n'en fait pas partie.
    private static readonly Regex Neutral = new(
        @"^(if\b.*\bthen|if\b[^{]*\{|else|else\s*\{|elseif\b.*\bthen|\}\s*else\s*\{|\}\s*else\s+if\b[^{]*\{|end[;,]?|return|\{|\})$", RegexOptions.Compiled);
    private static readonly Regex InlineLuaIf = new(@"^if\b.*?\bthen\s+(?<x>.+?)(\s+end)?$", RegexOptions.Compiled);
    private static readonly Regex InlineRedsIf = new(@"^if\b[^{]*\{\s*(?<x>.+)$", RegexOptions.Compiled);

    /// <summary>Cherche les fonctions de journal dans les fichiers de code du mod (extension, contenu).</summary>
    public static HashSet<string> Find(IEnumerable<(string Extension, string Content)> files)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<(string Name, string Body, string Comment)>();
        foreach (var (ext, content) in files)
        {
            if (ext is ".reds")
            {
                foreach (Match m in RedsFunc.Matches(content))
                {
                    if (!m.Groups["a"].Value.Contains("String", StringComparison.Ordinal)) continue;
                    var body = BraceBody(content, m.Index + m.Length);
                    if (body != null) candidates.Add((m.Groups["n"].Value, body, "//"));
                }
            }
            else if (ext is ".lua")
            {
                foreach (Match m in LuaFunc.Matches(content))
                {
                    if (m.Groups["a"].Value.Trim().Length == 0) continue;
                    var body = LuaBody(content, m.Index + m.Length);
                    if (body != null) candidates.Add((m.Groups["n"].Value.Split('.', ':')[^1], body, "--"));
                }
            }
        }
        // Deux passes : une fonction de journal peut en appeler une autre trouvee a la premiere
        for (var pass = 0; pass < 2; pass++)
            foreach (var (name, body, comment) in candidates)
                if (!Common.Contains(name) && OnlyLogs(body, comment, found))
                    found.Add(name);
        return found;
    }

    /// <summary>Noms trop communs pour etre reserves au journal dans tout le mod.</summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal) { "new", "init", "update", "draw", "run", "show", "set", "get" };

    /// <summary>Texte entre l'accolade deja ouverte et celle qui la referme, s'il est court.</summary>
    private static string? BraceBody(string s, int start)
    {
        var depth = 1;
        for (var i = start; i < s.Length && i - start <= MaxBody; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return s[start..i];
        }
        return null;
    }

    /// <summary>Corps d'une fonction Lua : jusqu'au "end" qui la referme (en comptant les if / for / while / function).</summary>
    private static string? LuaBody(string s, int start)
    {
        var depth = 1;
        foreach (Match m in Regex.Matches(s.Substring(start, Math.Min(MaxBody, s.Length - start)), @"\b(function|if|for|while|repeat|end|until)\b"))
        {
            switch (m.Value)
            {
                case "end" or "until":
                    if (--depth == 0) return s.Substring(start, m.Index);
                    break;
                default:
                    depth++;
                    break;
            }
        }
        return null;
    }

    private static bool OnlyLogs(string body, string comment, HashSet<string> known)
    {
        var logs = 0;
        foreach (var raw in body.Split('\n', ';'))
        {
            var line = raw;
            var c = line.IndexOf(comment, StringComparison.Ordinal);
            if (c >= 0) line = line[..c];
            line = line.Trim();
            if (line.Length == 0 || Neutral.IsMatch(line)) continue;
            // if d'une seule ligne : "if debug then print(msg) end", "if enabled { ModLog(n"X", str)"
            var inline = comment == "--" ? InlineLuaIf.Match(line) : InlineRedsIf.Match(line);
            if (inline.Success) line = inline.Groups["x"].Value.Trim();
            var call = Statement.Match(line);
            if (!call.Success) return false;
            var name = call.Groups["n"].Value;
            if (!LogCall.IsMatch(line) && !TextClassifier.IsLogCallName(name) && !known.Contains(name.Split('.', ':')[^1])) return false;
            logs++;
        }
        return logs > 0;
    }
}

using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

/// <summary>Contexte d'une chaine dans le code : dans quel appel de fonction elle se trouve, et a quelle place.</summary>
internal static class CodeContext
{
    /// <summary>Remonte depuis une position pour trouver l'appel de fonction englobant et l'index de l'argument.</summary>
    /// <param name="skip">Positions deja lues comme chaines ou commentaires : une parenthese dedans ne compte pas.</param>
    public static (string? Name, int ArgIndex) CallAt(string s, int pos, bool[]? skip = null)
    {
        var (name, arg, nameStart) = CallContextAt(s, pos, skip);
        // string.format("...", x) / StrFormat(...) a l'interieur d'un print ou d'un Log : c'est l'appel exterieur qui compte
        if (name != null && nameStart > 0 && Regex.IsMatch(name, @"(^|[.:])(format|Format|StrFormat|tostring|ToString)$"))
        {
            var (outer, _, _) = CallContextAt(s, nameStart, skip);
            if (outer != null && TextClassifier.IsLogCallName(outer))
                return (outer, 0);
        }
        return (name, arg);
    }

    /// <summary>Mots-cles suivis d'une parenthese qui n'est pas un appel : if (...), return (...), x and (...).</summary>
    private static readonly HashSet<string> NotCalls = new(StringComparer.Ordinal)
    {
        "if", "elseif", "while", "until", "return", "and", "or", "not", "in", "then", "else", "do", "local",
        "for", "switch", "case", "let",
    };

    /// <summary>Parenthese de regroupement ("a" .. (x and "b" or "c")) : on regarde l'appel qui l'entoure.</summary>
    private static bool IsGrouping(string name) =>
        name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') || name.EndsWith('.') || name.EndsWith(':') || NotCalls.Contains(name);

    private static (string? Name, int ArgIndex, int NameStart) CallContextAt(string s, int pos, bool[]? skip)
    {
        var depth = 0;
        var commas = 0;
        var limit = Math.Max(0, pos - 600);
        for (var i = pos - 1; i >= limit; i--)
        {
            if (skip != null && skip[i]) continue;
            var c = s[i];
            if (c is ')' or ']' or '}') depth++;
            else if (c is '[' or '{')
            {
                if (depth == 0) return (null, 0, -1);
                depth--;
            }
            else if (c == '(')
            {
                if (depth > 0)
                {
                    depth--;
                    continue;
                }
                var j = i - 1;
                while (j >= 0 && char.IsWhiteSpace(s[j])) j--;
                var end = j + 1;
                while (j >= 0 && (char.IsLetterOrDigit(s[j]) || s[j] is '_' or '.' or ':' or '@')) j--;
                var name = s[(j + 1)..end].TrimStart('@');
                if (IsGrouping(name))
                {
                    // pas un appel : la chaine fait partie d'une expression, on continue vers l'appel exterieur
                    commas = 0;
                    continue;
                }
                return (name, commas, j + 1);
            }
            else if (c == ',' && depth == 0) commas++;
            else if (c is ';' && depth == 0) return (null, 0, -1);
        }
        return (null, 0, -1);
    }
}

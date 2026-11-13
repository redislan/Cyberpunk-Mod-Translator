using System.Text;

namespace CyberpunkModTranslator.Core;

/// <summary>
/// Lecture et ecriture des chaines litterales de chaque langage (guillemets, echappements), reunies ici :
/// les lecteurs de fichiers (Lua, redscript, YAML, JSON) ne refont pas chacun ce travail.
/// Chaque Read* renvoie la valeur decodee ; chaque Escape*/Encode* fait l'inverse.
/// </summary>
internal static class StringSyntax
{
    // ---- Style C (Lua, redscript) ----

    /// <summary>Echappe \\, \n, \r, \t et le guillemet utilise.</summary>
    public static string EscapeCStyle(string v, char quote)
    {
        var sb = new StringBuilder(v.Length + 8);
        foreach (var c in v)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c == quote) sb.Append('\\').Append(c);
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Chaine Lua '...' ou "..." (tous les echappements Lua 5.4). Ok = false si la chaine n'est pas fermee sur la ligne.</summary>
    public static (string Value, int End, bool Ok) ReadLua(string s, int start, char quote)
    {
        var sb = new StringBuilder();
        // Octets ecrits un par un (\195\169, \xC3\xA9) : en Lua ce sont les octets UTF-8 d'un caractere accentue.
        // On les rassemble pour afficher « é », pas « Ã© ».
        var bytes = new List<byte>();
        void Flush()
        {
            if (bytes.Count == 0) return;
            var raw = bytes.ToArray();
            bytes.Clear();
            try
            {
                sb.Append(new UTF8Encoding(false, true).GetString(raw));
            }
            catch (DecoderFallbackException)
            {
                foreach (var b in raw) sb.Append((char)b); // pas de l'UTF-8 : un caractere par octet, comme avant
            }
        }
        void AppendCode(int code)
        {
            if (code is >= 0x80 and <= 0xFF) bytes.Add((byte)code);
            else
            {
                Flush();
                sb.Append((char)code);
            }
        }

        var i = start + 1;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == quote)
            {
                Flush();
                return (sb.ToString(), i + 1, true);
            }
            if (c == '\n')
            {
                Flush();
                return (sb.ToString(), i, false);
            }
            if (c != '\\')
            {
                Flush();
                sb.Append(c);
                i++;
                continue;
            }
            if (i + 1 >= s.Length) break;
            var n = s[i + 1];
            i += 2;
            if (n != 'x' && !char.IsDigit(n)) Flush();
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'v': sb.Append('\v'); break;
                case '\n': sb.Append('\n'); break;
                case 'z':
                    while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                    break;
                case 'x':
                    if (i + 2 <= s.Length && int.TryParse(s.AsSpan(i, 2), System.Globalization.NumberStyles.HexNumber, null, out var hx))
                    {
                        AppendCode(hx);
                        i += 2;
                    }
                    else Flush();
                    break;
                case 'u':
                    if (i < s.Length && s[i] == '{')
                    {
                        var close = s.IndexOf('}', i);
                        if (close > i && int.TryParse(s.AsSpan(i + 1, close - i - 1), System.Globalization.NumberStyles.HexNumber, null, out var cp))
                        {
                            sb.Append(char.ConvertFromUtf32(cp));
                            i = close + 1;
                        }
                    }
                    break;
                default:
                    if (char.IsDigit(n))
                    {
                        var digits = n.ToString();
                        while (digits.Length < 3 && i < s.Length && char.IsDigit(s[i])) digits += s[i++];
                        AppendCode(int.Parse(digits));
                    }
                    else sb.Append(n); // \\ \" \'
                    break;
            }
        }
        Flush();
        return (sb.ToString(), s.Length, false);
    }

    // ---- redscript ----

    /// <summary>Chaine redscript "..." : valeur decodee, texte brut entre les guillemets, et position de fin.</summary>
    public static (string Value, string Raw, int End) ReadReds(string s, int start)
    {
        var sb = new StringBuilder();
        var i = start + 1;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"') return (sb.ToString(), s[(start + 1)..i], i + 1);
            if (c == '\\' && i + 1 < s.Length)
            {
                var n = s[i + 1];
                sb.Append(n switch { 'n' => "\n", 't' => "\t", 'r' => "\r", '"' => "\"", '\\' => "\\", '\'' => "'", _ => "\\" + n });
                i += 2;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return (sb.ToString(), s[(start + 1)..], s.Length);
    }

    /// <summary>
    /// Fin d'une expression \(...) d'une chaine interpolee : position juste apres la parenthese qui la referme.
    /// Les parentheses imbriquees et les chaines de l'expression (GetLocalizedText("Cle")) sont sautees.
    /// <paramref name="open"/> est la position de la parenthese ouvrante.
    /// </summary>
    private static int SkipInterpolation(string s, int open)
    {
        var depth = 0;
        var i = open;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"')
            {
                // chaine dans l'expression : jusqu'au guillemet fermant
                i++;
                while (i < s.Length && s[i] != '"')
                    i += s[i] == '\\' ? 2 : 1;
                i++;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i + 1;
            else if (c == '\n') return i; // expression jamais refermee sur la ligne : on s'arrete la
            i++;
        }
        return s.Length;
    }

    /// <summary>
    /// Chaine interpolee s"...\(expr)..." : texte brut entre les guillemets et position de fin. Un guillemet a
    /// l'interieur d'une expression (s"Prix : \(GetLocalizedText("Cle"))") ne ferme pas la chaine.
    /// </summary>
    public static (string Raw, int End) ReadRedsInterpolated(string s, int start)
    {
        var i = start + 1;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"') return (s[(start + 1)..i], i + 1);
            if (c == '\\' && i + 1 < s.Length)
            {
                i = s[i + 1] == '(' ? SkipInterpolation(s, i + 1) : i + 2;
                continue;
            }
            i++;
        }
        return (s[(start + 1)..], s.Length);
    }

    /// <summary>
    /// Chaine interpolee s"...\(expr)..." : on garde le texte brut tel que l'utilisateur l'a ecrit,
    /// en echappant seulement les guillemets nus et les retours a la ligne. Les expressions \(...) sont
    /// recopiees telles quelles (leurs guillemets font partie du code).
    /// </summary>
    public static string EscapeRedsRaw(string v)
    {
        var sb = new StringBuilder(v.Length + 4);
        for (var k = 0; k < v.Length; k++)
        {
            var c = v[k];
            if (c == '\\' && k + 1 < v.Length && v[k + 1] == '(' && (k == 0 || v[k - 1] != '\\'))
            {
                var end = SkipInterpolation(v, k + 1);
                sb.Append(v, k, end - k);
                k = end - 1;
                continue;
            }
            var escaped = k > 0 && v[k - 1] == '\\' && (k < 2 || v[k - 2] != '\\');
            if (c == '"' && !escaped) sb.Append("\\\"");
            else if (c == '\n') sb.Append("\\n");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---- JSON ----

    /// <summary>Chaine JSON "..." (\\uXXXX compris).</summary>
    public static (string Value, int End) ReadJson(string s, int start)
    {
        var sb = new StringBuilder();
        var i = start + 1;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"') return (sb.ToString(), i + 1);
            if (c == '\\' && i + 1 < s.Length)
            {
                var n = s[i + 1];
                i += 2;
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 <= s.Length && int.TryParse(s.AsSpan(i, 4), System.Globalization.NumberStyles.HexNumber, null, out var u))
                        {
                            sb.Append((char)u);
                            i += 4;
                        }
                        break;
                    default: sb.Append(n); break;
                }
                continue;
            }
            sb.Append(c);
            i++;
        }
        return (sb.ToString(), s.Length);
    }

    /// <summary>Chaine JSON reecrite, caracteres de controle en \\uXXXX.</summary>
    public static string EncodeJson(string v)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in v)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    // ---- YAML ----

    /// <summary>Valeur YAML entre guillemets en debut de <paramref name="vText"/> : '...' ('' = apostrophe) ou "..." (echappements \\).</summary>
    public static (string Value, int Length, bool Closed) ReadYamlQuoted(string vText)
    {
        var q = vText[0];
        var sb = new StringBuilder();
        var j = 1;
        var closed = false;
        while (j < vText.Length)
        {
            var c = vText[j];
            if (q == '\'' && c == '\'' && j + 1 < vText.Length && vText[j + 1] == '\'')
            {
                sb.Append('\'');
                j += 2;
                continue;
            }
            if (c == q)
            {
                closed = true;
                j++;
                break;
            }
            if (q == '"' && c == '\\' && j + 1 < vText.Length)
            {
                var n = vText[j + 1];
                sb.Append(n switch { 'n' => "\n", 't' => "\t", '"' => "\"", '\\' => "\\", _ => "\\" + n });
                j += 2;
                continue;
            }
            sb.Append(c);
            j++;
        }
        return (sb.ToString(), j, closed);
    }

    /// <summary>Valeur YAML reecrite entre guillemets doubles (toujours valide, meme si l'original n'en avait pas).</summary>
    public static string EncodeYaml(string v) => "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
}

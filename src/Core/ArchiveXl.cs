namespace CyberpunkModTranslator.Core;

/// <summary>
/// Lecture du bloc "localization" d'un fichier .xl (ArchiveXL).
/// Volontairement tolerant : tabulations, fins de ligne Windows, commentaires, listes en ligne ou en bloc.
/// Le reste du fichier (streaming, patchs...) n'est pas lu, il ne sert pas a la traduction.
/// </summary>
public sealed class XlLocalization
{
    /// <summary>"onscreens" / "subtitles" -> langue -> chemins, dans l'ordre du fichier.</summary>
    public Dictionary<string, List<(string Lang, List<string> Paths)>> Sections { get; } = new();

    public string? Extend { get; private set; }

    public List<(string Lang, List<string> Paths)> Get(string section) =>
        Sections.TryGetValue(section, out var v) ? v : new();

    public static XlLocalization? Load(string path)
    {
        var lines = File.ReadAllLines(path)
            .Select(Clean)
            .Where(l => l.Text.Length > 0)
            .ToList();

        var start = lines.FindIndex(l => l.Indent == 0 && l.Text.StartsWith("localization:"));
        if (start < 0) return null;
        var xl = new XlLocalization();

        string? section = null;
        var sectionIndent = -1;
        string? lang = null;
        var langIndent = -1;

        for (var i = start + 1; i < lines.Count && lines[i].Indent > 0; i++)
        {
            var (indent, text) = lines[i];
            if (section == null || indent <= sectionIndent)
            {
                // nouvelle cle sous "localization:"
                var (key, value) = SplitKey(text);
                if (key == "extend")
                {
                    xl.Extend = Unquote(value);
                    section = null;
                    continue;
                }
                section = key;
                sectionIndent = indent;
                lang = null;
                langIndent = -1;
                xl.Sections[section] = new();
                if (value.Length > 0)
                {
                    // forme courte : "onscreens: chemin" (= anglais)
                    xl.Sections[section].Add(("en-us", ParseValues(value)));
                    section = null;
                }
                continue;
            }

            if (text.StartsWith("- ") || text == "-")
            {
                var item = Unquote(text.Length > 1 ? text[2..].Trim() : "");
                if (lang != null && indent >= langIndent)
                    xl.Sections[section].Last().Paths.Add(item);
                else
                    AddPaths(xl, section, "en-us", new List<string> { item }); // liste directe = anglais
                continue;
            }

            var (k, v) = SplitKey(text);
            lang = k;
            langIndent = indent;
            AddPaths(xl, section, lang, v.Length > 0 ? ParseValues(v) : new List<string>());
        }
        return xl;
    }

    private static void AddPaths(XlLocalization xl, string section, string lang, List<string> paths)
    {
        var list = xl.Sections[section];
        var existing = list.FindIndex(e => e.Lang == lang);
        if (existing >= 0) list[existing].Paths.AddRange(paths);
        else list.Add((lang, paths));
    }

    private static (int Indent, string Text) Clean(string raw)
    {
        var line = raw.Replace("\r", "");
        var indent = 0;
        foreach (var c in line)
        {
            if (c == ' ') indent++;
            else if (c == '\t') indent += 2;
            else break;
        }
        var text = StripComment(line.Trim()).Trim();
        return (indent, text);
    }

    private static string StripComment(string s)
    {
        var quote = '\0';
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'') quote = c;
            else if (c == '#' && (i == 0 || char.IsWhiteSpace(s[i - 1]))) return s[..i];
        }
        return s;
    }

    private static (string Key, string Value) SplitKey(string text)
    {
        var idx = text.IndexOf(':');
        if (idx < 0) return (Unquote(text), "");
        return (Unquote(text[..idx].Trim()), text[(idx + 1)..].Trim());
    }

    private static List<string> ParseValues(string value)
    {
        value = value.Trim();
        if (value.StartsWith('[') && value.EndsWith(']'))
            return value[1..^1].Split(',').Select(v => Unquote(v.Trim())).Where(v => v.Length > 0).ToList();
        return new List<string> { Unquote(value) };
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1];
        return s.Replace(@"\\", @"\");
    }
}

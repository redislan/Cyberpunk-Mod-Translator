using System.Text;
using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

/// <summary>Nature probable d'une chaine trouvee dans un fichier en clair.</summary>
public enum StringKind
{
    /// <summary>Phrase ou texte affiche : a traduire.</summary>
    Text,

    /// <summary>Peut-etre du texte (un seul mot, chaine interpolee...) : a verifier par l'utilisateur.</summary>
    Maybe,

    /// <summary>Identifiant, chemin, cle, message de debug : a ne pas toucher.</summary>
    Code,
}

/// <summary>Une chaine litterale dans un fichier source, avec sa position exacte.</summary>
public sealed class StringLiteral
{
    public int Index { get; init; }
    public int Start { get; init; }
    public int Length { get; init; }
    public string Value { get; init; } = "";
    public int Line { get; init; }
    public string LineText { get; init; } = "";
    public StringKind Kind { get; set; }

    /// <summary>
    /// La chaine sert de valeur de comparaison dans le code (x == "...", Equals(x, "..."), case "...", cle de table).
    /// Si le meme texte est traduit ailleurs, il faut traduire ces comparaisons pareil, sinon le mod casse.
    /// </summary>
    public bool IsComparison { get; init; }

    /// <summary>Comment reecrire la chaine (guillemets, echappements) : propre a chaque langage.</summary>
    public Func<string, string> Encode { get; init; } = v => v;
}

/// <summary>Fichier texte d'un mod (.lua, .reds, .yaml, .json, .tweak) et ses chaines.</summary>
public sealed class PlainTextFile
{
    public static readonly string[] SupportedExtensions = { ".lua", ".reds", ".yaml", ".yml", ".json", ".tweak" };

    public string RelativePath { get; }
    public string Content { get; }
    public Encoding Encoding { get; }
    public bool HasBom { get; }
    public List<StringLiteral> Literals { get; }

    private PlainTextFile(string relativePath, string content, Encoding encoding, bool bom, List<StringLiteral> literals)
    {
        RelativePath = relativePath;
        Content = content;
        Encoding = encoding;
        HasBom = bom;
        Literals = literals;
    }

    public static bool IsSupported(string relativePath)
    {
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext)) return false;
        var norm = relativePath.Replace('\\', '/').ToLowerInvariant();
        if (norm.EndsWith(".xl")) return false;
        if (ext == ".json" && (norm.StartsWith("archive/") || norm.Contains("/archive/"))) return false;
        return true;
    }

    public static PlainTextFile Load(string fullPath, string relativePath)
    {
        var bytes = File.ReadAllBytes(fullPath);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var body = bom ? bytes[3..] : bytes;
        Encoding encoding;
        string content;
        try
        {
            encoding = new UTF8Encoding(false, true);
            content = encoding.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            encoding = Encoding.Latin1;
            content = encoding.GetString(body);
        }
        return Parse(relativePath, content, encoding, bom);
    }

    public static PlainTextFile Parse(string relativePath, string content, Encoding? encoding = null, bool bom = false)
    {
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        var literals = ext switch
        {
            ".lua" => LuaScanner.Scan(content),
            ".reds" or ".tweak" => RedsScanner.Scan(content),
            ".yaml" or ".yml" => YamlScanner.Scan(content),
            ".json" => JsonScanner.Scan(content),
            _ => new List<StringLiteral>(),
        };
        FileRules.Apply(relativePath, content, literals);
        return new PlainTextFile(relativePath, content, encoding ?? new UTF8Encoding(false), bom, literals);
    }

    /// <summary>Remplace les chaines indiquees (index -> nouveau texte). Le reste du fichier ne bouge pas d'un octet.</summary>
    public byte[] Apply(IReadOnlyDictionary<int, string> replacements)
    {
        var sb = new StringBuilder(Content.Length + 256);
        var pos = 0;
        foreach (var lit in Literals.OrderBy(l => l.Start))
        {
            if (!replacements.TryGetValue(lit.Index, out var value)) continue;
            sb.Append(Content, pos, lit.Start - pos);
            sb.Append(lit.Encode(value));
            pos = lit.Start + lit.Length;
        }
        sb.Append(Content, pos, Content.Length - pos);
        var body = Encoding.GetBytes(sb.ToString());
        return HasBom ? Encoding.UTF8.GetPreamble().Concat(body).ToArray() : body;
    }

    internal static (int Line, string Text) LineAt(string s, int pos)
    {
        var line = 1;
        for (var i = 0; i < pos && i < s.Length; i++)
            if (s[i] == '\n') line++;
        var start = s.LastIndexOf('\n', Math.Max(0, pos - 1)) + 1;
        if (pos == 0) start = 0;
        var end = s.IndexOf('\n', pos);
        if (end < 0) end = s.Length;
        var text = s[start..end].Trim();
        return (line, text.Length > 140 ? text[..137] + "..." : text);
    }
}

// ---------------------------------------------------------------------------------------------
// Tri texte / code
// ---------------------------------------------------------------------------------------------

public static class TextClassifier
{
    private static readonly Regex Word2 = new(@"[A-Za-z\u00C0-\u024F]{2,}", RegexOptions.Compiled);
    private static readonly Regex Dotted = new(@"^[\w$-]+(\.[\w$-]+)+$", RegexOptions.Compiled);
    private static readonly Regex Snake = new(@"^[a-z0-9_.\-:]+$", RegexOptions.Compiled);
    private static readonly Regex Camel = new(@"^[a-z]+([A-Z0-9][a-z0-9]*)+$", RegexOptions.Compiled);
    private static readonly Regex Pascal = new(@"^([A-Z][a-z0-9]+){2,}$", RegexOptions.Compiled);
    private static readonly Regex Upper = new(@"^[A-Z0-9_]{4,}$", RegexOptions.Compiled);
    private static readonly Regex Numberish = new(@"^[\s\d.,:;%+\-*/x#()\[\]]*$", RegexOptions.Compiled);
    private static readonly Regex Color = new(@"^#?[0-9a-fA-F]{6,8}$", RegexOptions.Compiled);
    private static readonly Regex FileName = new(@"^[^\s]+\.[A-Za-z0-9]{1,6}$", RegexOptions.Compiled);
    private static readonly Regex SingleWord = new(@"^[A-Za-z\u00C0-\u024F][A-Za-z\u00C0-\u024F'!?.]*$", RegexOptions.Compiled);

    /// <summary>Classe une chaine d'apres son seul contenu.</summary>
    public static StringKind Classify(string value)
    {
        var v = value.Trim();
        if (v.Length == 0 || !Word2.IsMatch(v)) return StringKind.Code;
        if (v.StartsWith("LocKey#", StringComparison.OrdinalIgnoreCase)) return StringKind.Code;
        if (Numberish.IsMatch(v) || Color.IsMatch(v)) return StringKind.Code;

        var hasSpace = v.Any(char.IsWhiteSpace);
        if (!hasSpace)
        {
            if (v.Contains('/') || v.Contains('\\')) return StringKind.Code;
            if (Dotted.IsMatch(v) || FileName.IsMatch(v)) return StringKind.Code;
            if (Snake.IsMatch(v) || Camel.IsMatch(v) || Pascal.IsMatch(v) || Upper.IsMatch(v)) return StringKind.Code;
            if (v.Contains('_') || v.Contains('-')) return StringKind.Code;
            // Un seul mot qui finit comme une phrase : "Whatever...", "Done!", "Ready?"
            if (SingleWord.IsMatch(v) && (v.EndsWith("...") || v.EndsWith('!') || v.EndsWith('?'))) return StringKind.Text;
            if (v.Skip(1).Any(char.IsUpper)) return StringKind.Code; // NCIedge, MainMenu, OK...
            if (SingleWord.IsMatch(v) && (char.IsUpper(v[0]) || v.Any(c => c > 127))) return StringKind.Maybe;
            return StringKind.Code;
        }

        // Plusieurs mots : texte, sauf chemins ou expressions de code evidentes.
        // "Enable/disable", "fill/max", "r6/logs/mods" au milieu d'une phrase : c'est du texte.
        var tokens = v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length <= 3 && tokens.All(t => PathChars.IsMatch(t)) && tokens.Any(IsPathToken)) return StringKind.Code;
        if (LooksLikeCode(v)) return StringKind.Code;
        return StringKind.Text;
    }

    private static readonly Regex PathChars = new(@"^[\w./\\~:-]+$", RegexOptions.Compiled);
    private static readonly Regex Extension = new(@"\.[A-Za-z0-9]{1,6}$", RegexOptions.Compiled);
    private static readonly Regex Assignment = new(@"^\s*[\[A-Za-z_][\w.'""\[\]]*\s*=\s*([{'""\[]|true\b|false\b|nil\b|-?\d)", RegexOptions.Compiled);
    private static readonly Regex LooseBrace = new(@"\{\s|\s\}", RegexOptions.Compiled);

    /// <summary>
    /// Un morceau de chemin : commence par un separateur, contient un antislash, a une extension,
    /// ou n'a que des segments en minuscules (r6/logs/mods). "Auto/Timed/Random" n'en est pas un.
    /// </summary>
    private static bool IsPathToken(string t)
    {
        var seps = t.Count(c => c is '/' or '\\');
        if (seps == 0) return false;
        if (t[0] is '/' or '\\' || t.Contains('\\') || Extension.IsMatch(t)) return true;
        return seps >= 2 && t.Split('/').Where(x => x.Length > 0).All(x => x == x.ToLowerInvariant());
    }

    private static readonly Regex MethodCall = new(@"[A-Za-z_]\w*[.:][A-Za-z_]\w*\(", RegexOptions.Compiled);
    private static readonly Regex KeyEquals = new(@"\b[A-Za-z_]\w*=[^\s=]", RegexOptions.Compiled);
    private static readonly Regex ProtocolTag = new(@"^[A-Z]{3,}\d+\b", RegexOptions.Compiled);

    /// <summary>
    /// Morceau de code genere, de condition ou de journal technique :
    /// "x == 1", "{ transform = true", "['k'] = '", "Game.AddToInventory(...)", "camera=%s owns=%s", "RAINCAR1 %d END".
    /// </summary>
    private static bool LooksLikeCode(string value)
    {
        var v = StripInterpolations(value);
        return v.Contains("==") || v.Contains("~=") || v.Contains("!=") || v.Contains("&&") || v.Contains("||") || v.Contains("()") ||
        v.Contains("))") || v.TrimEnd().EndsWith('=') || v.TrimEnd().EndsWith('{') || LooseBrace.IsMatch(v) || Assignment.IsMatch(v) ||
        MethodCall.IsMatch(v) || KeyEquals.Matches(v).Count >= 2 || ProtocolTag.IsMatch(v);
    }

    /// <summary>Remplace les \(expression) des chaines redscript s"..." par un mot neutre avant l'analyse.</summary>
    internal static string StripInterpolations(string v)
    {
        if (!v.Contains("\\(")) return v;
        var sb = new StringBuilder(v.Length);
        for (var i = 0; i < v.Length; i++)
        {
            if (v[i] == '\\' && i + 1 < v.Length && v[i + 1] == '(')
            {
                var depth = 0;
                var j = i + 1;
                for (; j < v.Length; j++)
                {
                    if (v[j] == '(') depth++;
                    else if (v[j] == ')' && --depth == 0) break;
                }
                sb.Append("X");
                i = j;
                continue;
            }
            sb.Append(v[i]);
        }
        return sb.ToString();
    }

    private static readonly HashSet<string> IdKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "key", "type", "kind", "event", "state", "menu", "submenu", "mode", "action", "class", "tag", "tags",
        "scenario", "context", "path", "file", "icon", "texture", "atlas", "sound", "anim", "animation", "input",
        "hotkey", "record", "tweak", "entity", "appearance", "slot", "channel", "group", "layer", "fact", "variant",
        "style", "font", "color", "theme", "lang", "language", "locale", "version", "author", "source", "target",
    };

    private static readonly HashSet<string> TextKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "label", "title", "desc", "description", "text", "tooltip", "caption", "message", "msg", "hint", "header",
        "subtitle", "placeholder", "button", "notification", "displayname", "display", "prompt", "question", "answer",
    };

    /// <summary>Le nom du champ aide a trancher : "event = 'Menu'" est un identifiant, "label = 'Menu'" un texte.</summary>
    public static StringKind ApplyKeyHint(StringKind kind, string? key)
    {
        if (key == null || kind == StringKind.Code) return kind;
        if (IdKeys.Contains(key) || key.EndsWith("Id") || key.EndsWith("Key") || key.EndsWith("Path") ||
            key.EndsWith("Event") || key.EndsWith("Type") || key.EndsWith("_id") || key.EndsWith("_key"))
            return kind == StringKind.Text ? StringKind.Maybe : StringKind.Code;
        if (TextKeys.Contains(key) || key.EndsWith("Label") || key.EndsWith("Title") || key.EndsWith("Desc") ||
            key.EndsWith("Text") || key.EndsWith("Tooltip") || key.EndsWith("Description"))
            return StringKind.Text;
        return kind;
    }

    /// <summary>Fonctions ImGui dont le premier argument est un libelle affiche ("Texte##id").</summary>
    private static readonly HashSet<string> ImGuiLabels = new(StringComparer.Ordinal)
    {
        "Text", "TextWrapped", "TextDisabled", "BulletText", "LabelText", "Button", "SmallButton", "Checkbox", "RadioButton",
        "Selectable", "MenuItem", "BeginMenu", "TreeNode", "TreeNodeEx", "CollapsingHeader", "BeginTabItem", "TabItemButton",
        "SliderFloat", "SliderFloat2", "SliderFloat3", "SliderInt", "SliderInt2", "DragFloat", "DragFloat2", "DragInt",
        "InputText", "InputTextMultiline", "InputTextWithHint", "InputInt", "InputFloat", "Combo", "ListBox", "ColorEdit3",
        "ColorEdit4", "ColorPicker3", "ColorPicker4", "SetTooltip", "TableSetupColumn", "Begin", "ArrowButton",
    };

    /// <summary>Fonctions ImGui qui ne prennent que des identifiants internes.</summary>
    private static readonly HashSet<string> ImGuiIds = new(StringComparer.Ordinal)
    {
        "PushID", "GetID", "BeginChild", "BeginTable", "BeginTabBar", "OpenPopup", "BeginPopup", "BeginPopupModal",
        "BeginPopupContextItem", "BeginPopupContextWindow", "IsPopupOpen", "CalcTextSize", "SetWindowFocus", "BeginGroup",
    };

    /// <summary>Native Settings UI : add*(chemin, libelle, description...)</summary>
    private static readonly HashSet<string> NativeSettingsAdders = new(StringComparer.Ordinal)
    {
        "addTab", "addSubcategory", "addSwitch", "addRangeInt", "addRangeFloat", "addSelectorString", "addButton",
        "addKeyBinding", "addCustom", "addInputText",
    };

    /// <summary>Fonctions qui affichent leurs arguments texte (inkText.SetText, notifications...).</summary>
    private static readonly HashSet<string> TextSinks = new(StringComparer.Ordinal)
    {
        "SetText", "SetLabel", "SetTitle", "SetDescription", "SetTooltip", "SetHint", "SetMessage", "ShowNotification",
        "SetDisplayName", "SetHeader", "SetSubtitle", "SetCaption", "SetButtonText",
    };

    private static readonly string[] FirstArgIsId =
    {
        "require", "GetMod", "registerForEvent", "registerInput", "registerHotkey", "Observe", "ObserveAfter",
        "ObserveBefore", "Override", "NewObject", "GetSingleton", "SetFactStr", "GetFactStr", "AddFactStr",
        "GetFlat", "SetFlat", "GetRecord", "CreateRecord", "CloneRecord", "Update", "open", "dofile", "loadfile",
        "PushID", "Get", "Set", "ModuleExists", "SetFontStyle", "SetFontFamily", "SetState", "SetAtlasResource",
        "SetTexturePart", "PlaySound", "PlayScenario", "GetVar", "SetVar", "StringToName", "IsModLoaded",
    };

    /// <summary>Codeware ModLocalizationPackage : this.Text("Cle", "Texte"), TextF, TextM, Subtitle...</summary>
    private static readonly HashSet<string> LocalizationCalls = new(StringComparer.Ordinal)
    {
        "Text", "TextF", "TextM", "TextN", "Subtitle", "SubtitleF", "SubtitleM",
    };

    private static readonly Regex Letters = new(@"[A-Za-z\u00C0-\u024F\u0400-\u04FF]", RegexOptions.Compiled);

    /// <summary>Appel de journal / debug : print, spdlog, Log, LogRetreat, Logger.error, RedLog.Append, self.log:info...</summary>
    private static bool IsLogCall(string name, string receiver, string language)
    {
        if (name is "print" or "error" or "assert" or "spdlog" or "FTLog" or "FTLogWarning" or "FTLogError" or "Print" or "Assert" or "LogChannel")
            return true;
        if (Regex.IsMatch(name, @"^(Log|log)([A-Z_]|$)")) return true;
        if (Regex.IsMatch(receiver, @"(^|[.:])(spdlog|log|logger|Logger|Log|Logging)$") || Regex.IsMatch(receiver, @"[a-z]Log$")) return true;
        if (language == "lua" && receiver.Length > 0 && name is "info" or "warning" or "warn" or "debug" or "trace" or "critical" or "error")
            return true;
        return false;
    }

    /// <summary>Contenu + contexte d'appel : certaines fonctions ne prennent que des identifiants ou des messages de debug.</summary>
    public static StringKind ClassifyInContext(string value, string? callName, int argIndex, string language)
    {
        var kind = Classify(value);
        if (callName == null) return kind;
        var parts = callName.Split('.', ':');
        var name = parts[^1];
        var receiver = parts.Length > 1 ? callName[..(callName.Length - name.Length - 1)] : "";

        if (IsLogCall(name, receiver, language)) return StringKind.Code;

        // Motifs Lua (string.gsub, :find, :match) : le texte doit rester tel quel pour correspondre
        if (language == "lua" && name is "gsub" or "find" or "match" or "gmatch") return StringKind.Code;

        if (receiver == "ImGui")
        {
            if (ImGuiIds.Contains(name)) return StringKind.Code;
            var isLabel = (ImGuiLabels.Contains(name) && argIndex == 0) || name == "TextColored" || (name == "ProgressBar" && argIndex == 3);
            if (!isLabel) return kind;
            var visible = value.Split("##")[0];
            if (!Letters.IsMatch(visible)) return StringKind.Code;
            var vk = Classify(visible);
            return vk == StringKind.Code && visible.Contains(' ') ? StringKind.Code : vk == StringKind.Code && !SingleWordish(visible) ? StringKind.Code : StringKind.Text;
        }

        if (receiver == "GameOptions") return StringKind.Code;

        if (NativeSettingsAdders.Contains(name))
            return argIndex == 0 ? StringKind.Code : kind == StringKind.Maybe ? StringKind.Text : kind;

        if (LocalizationCalls.Contains(name) && receiver is "" or "this" or "self")
        {
            if (argIndex == 0) return kind; // la cle : "IH.Core.mod.Name" est deja reconnue comme code
            return Letters.IsMatch(value) && kind != StringKind.Code || kind == StringKind.Maybe ? StringKind.Text : kind;
        }

        if (TextSinks.Contains(name) || (name == "Show" && receiver.EndsWith("Notification", StringComparison.Ordinal)))
            return kind == StringKind.Maybe ? StringKind.Text : kind;

        if (argIndex == 0 && FirstArgIsId.Contains(name)) return StringKind.Code;
        // Observe('Classe', 'Methode') : uniquement des noms de classes et de fonctions du jeu
        if (name is "Observe" or "ObserveAfter" or "ObserveBefore" or "Override" or "IsA" or "NewObject") return StringKind.Code;
        return kind;
    }

    /// <summary>Libelle court en majuscules ou en un mot ("FOV", "HUD", "OK") : on le laisse traduire dans un bouton ImGui.</summary>
    private static bool SingleWordish(string v) => Regex.IsMatch(v.Trim(), @"^[A-Za-z\u00C0-\u024F]{1,12}$");
}

// ---------------------------------------------------------------------------------------------
// Outils communs aux lecteurs
// ---------------------------------------------------------------------------------------------

internal static class ScanHelpers
{
    /// <summary>Remonte depuis une position pour trouver l'appel de fonction englobant et l'index de l'argument.</summary>
    public static (string? Name, int ArgIndex) CallContext(string s, int pos)
    {
        var (name, arg, nameStart) = CallContextAt(s, pos);
        // string.format("...", x) / StrFormat(...) a l'interieur d'un print ou d'un Log : c'est l'appel exterieur qui compte
        if (name != null && nameStart > 0 && Regex.IsMatch(name, @"(^|[.:])(format|Format|StrFormat|tostring|ToString)$"))
        {
            var (outer, _, _) = CallContextAt(s, nameStart);
            if (outer != null && Regex.IsMatch(outer, @"(^|[.:])(print|error|assert|spdlog\.\w+|[Ll]og\w*|\w*Log|\w*[Ll]ogger[.:]\w+)$"))
                return (outer, 0);
        }
        return (name, arg);
    }

    private static (string? Name, int ArgIndex, int NameStart) CallContextAt(string s, int pos)
    {
        var depth = 0;
        var commas = 0;
        var limit = Math.Max(0, pos - 600);
        for (var i = pos - 1; i >= limit; i--)
        {
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
                return (name.Length > 0 ? name : null, commas, j + 1);
            }
            else if (c == ',' && depth == 0) commas++;
            else if (c is ';' && depth == 0) return (null, 0, -1);
        }
        return (null, 0, -1);
    }

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
}

// ---------------------------------------------------------------------------------------------
// Lua (mods CET)
// ---------------------------------------------------------------------------------------------

internal static class LuaScanner
{
    public static List<StringLiteral> Scan(string s)
    {
        var list = new List<StringLiteral>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                var level = LongBracketLevel(s, i + 2);
                if (level >= 0) i = SkipLongBracket(s, i + 2, level);
                else
                {
                    var nl = s.IndexOf('\n', i);
                    i = nl < 0 ? s.Length : nl + 1;
                }
                continue;
            }
            if (c is '"' or '\'')
            {
                var (value, end, ok) = ReadShort(s, i, c);
                if (ok) Add(list, s, i, end, value, c);
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
                    Add(list, s, i, end, content, '"');
                    i = end;
                    continue;
                }
            }
            i++;
        }
        return list;
    }

    private static readonly Regex KeyBefore = new(@"(?:(?<k>[A-Za-z_]\w*)|\[\s*['""](?<k>[^'""]+)['""]\s*\])\s*=\s*$", RegexOptions.Compiled);

    private static void Add(List<StringLiteral> list, string s, int start, int end, string value, char quote)
    {
        var (line, text) = PlainTextFile.LineAt(s, start);
        var (call, arg) = ScanHelpers.CallContext(s, start);
        var kind = TextClassifier.ClassifyInContext(value, call, arg, "lua");

        var lineStart = s.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        var before = s[lineStart..start];
        var lineEnd = s.IndexOf('\n', end);
        var after = s[end..(lineEnd < 0 ? s.Length : lineEnd)].TrimStart();

        // Comparaison (x == "Menu") ou cle de table (["Menu"] = ...) : identifiant
        var trimmedBefore = before.TrimEnd();
        var comparison = trimmedBefore.EndsWith("==") || trimmedBefore.EndsWith("~=") || after.StartsWith("==") || after.StartsWith("~=");
        var tableKey = trimmedBefore.EndsWith('[') && after.StartsWith(']'); // t["cle"] : nom de champ, on n'y touche pas
        if (comparison || tableKey)
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

        list.Add(new StringLiteral
        {
            Index = list.Count,
            Start = start,
            Length = end - start,
            Value = value,
            Line = line,
            LineText = text,
            Kind = kind,
            IsComparison = comparison,
            Encode = v => quote + ScanHelpers.EscapeCStyle(v, quote) + quote,
        });
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

    private static (string Value, int End, bool Ok) ReadShort(string s, int start, char quote)
    {
        var sb = new StringBuilder();
        var i = start + 1;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == quote) return (sb.ToString(), i + 1, true);
            if (c == '\n') return (sb.ToString(), i, false);
            if (c != '\\')
            {
                sb.Append(c);
                i++;
                continue;
            }
            if (i + 1 >= s.Length) break;
            var n = s[i + 1];
            i += 2;
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
                        sb.Append((char)hx);
                        i += 2;
                    }
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
                        sb.Append((char)int.Parse(digits));
                    }
                    else sb.Append(n); // \\ \" \'
                    break;
            }
        }
        return (sb.ToString(), s.Length, false);
    }
}

// ---------------------------------------------------------------------------------------------
// Redscript (.reds) et TweakXL ancien format (.tweak)
// ---------------------------------------------------------------------------------------------

internal static class RedsScanner
{
    public static List<StringLiteral> Scan(string s)
    {
        var list = new List<StringLiteral>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                var nl = s.IndexOf('\n', i);
                i = nl < 0 ? s.Length : nl + 1;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? s.Length : end + 2;
                continue;
            }
            if (c == '"')
            {
                // prefixe eventuel : n"" (nom), r"" (ressource), t"" (TweakDB), l"" (LocKey), s"" (interpolation)
                var prefix = i > 0 && char.IsLetter(s[i - 1]) && (i < 2 || !(char.IsLetterOrDigit(s[i - 2]) || s[i - 2] == '_'))
                    ? s[i - 1]
                    : '\0';
                var (value, raw, end) = Read(s, i);
                if (prefix is 'n' or 'r' or 't' or 'l')
                {
                    i = end;
                    continue; // jamais du texte a traduire
                }
                var (line, text) = PlainTextFile.LineAt(s, i);
                var (call, arg) = ScanHelpers.CallContext(s, prefix == '\0' ? i : i - 1);
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
                list.Add(new StringLiteral
                {
                    Index = list.Count,
                    Start = i,
                    Length = end - i,
                    Value = interpolated ? raw : value,
                    Line = line,
                    LineText = text,
                    Kind = kind,
                    IsComparison = comparison,
                    Encode = interpolated
                        ? v => "\"" + EncodeRaw(v) + "\"" // le prefixe s reste devant, hors de la plage remplacee
                        : v => "\"" + ScanHelpers.EscapeCStyle(v, '"') + "\"",
                });
                i = end;
                continue;
            }
            i++;
        }
        return list;
    }

    /// <summary>@runtimeProperty("ModSettings.xxx", "valeur") : la propriete dit si la valeur est affichee.</summary>
    private static readonly Regex ModSettingsProperty = new(@"@runtimeProperty\(\s*""ModSettings\.(?<p>[A-Za-z0-9_.]+)""\s*,$", RegexOptions.Compiled);

    private static StringKind ModSettingsKind(string property, string value, StringKind kind)
    {
        var p = property.ToLowerInvariant();
        if (p.EndsWith(".order")) return StringKind.Code;
        var displayed = p is "mod" or "category" or "displayname" or "description" or "title" or "tooltip" || p.StartsWith("displayvalues");
        if (!displayed) return StringKind.Code; // step, min, max, dependency, updatePolicy...
        // une cle de traduction ("IH.Core.mod.Name", "UI-Settings-KeyBindings") reste une cle
        return kind == StringKind.Code || !Regex.IsMatch(value, @"[A-Za-z\u00C0-\u024F]") ? StringKind.Code : StringKind.Text;
    }

    /// <summary>
    /// Chaine interpolee s"...\(expr)..." : on garde le texte brut tel que l'utilisateur l'a ecrit,
    /// en echappant seulement les guillemets nus et les retours a la ligne.
    /// </summary>
    private static string EncodeRaw(string v)
    {
        var sb = new StringBuilder(v.Length + 4);
        for (var k = 0; k < v.Length; k++)
        {
            var c = v[k];
            var escaped = k > 0 && v[k - 1] == '\\' && (k < 2 || v[k - 2] != '\\');
            if (c == '"' && !escaped) sb.Append("\\\"");
            else if (c == '\n') sb.Append("\\n");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static (string Value, string Raw, int End) Read(string s, int start)
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
}

// ---------------------------------------------------------------------------------------------
// YAML (TweakXL)
// ---------------------------------------------------------------------------------------------

internal static class YamlScanner
{
    /// <summary>Champs TweakXL qui contiennent une cle de traduction, pas le texte lui-meme.</summary>
    private static readonly HashSet<string> LocKeyFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "displayName", "localizedDescription", "localizedName", "localizedShortDescription", "description",
        "shortDescription", "title", "subtitle", "displayNameKey", "uiName", "fullName", "name",
    };

    private static readonly Regex KeyValue = new(@"^(?<indent>\s*)(?:-\s+)?(?<key>[^\s:#'""][^:#]*?|'[^']*'|""[^""]*"")\s*:(?:\s+(?<value>.*))?$", RegexOptions.Compiled);
    private static readonly Regex ListItem = new(@"^(?<indent>\s*)-\s+(?<value>.+)$", RegexOptions.Compiled);

    public static List<StringLiteral> Scan(string s)
    {
        var list = new List<StringLiteral>();
        var offset = 0;
        var lineNo = 0;
        foreach (var rawLine in s.Split('\n'))
        {
            lineNo++;
            var lineStart = offset;
            offset += rawLine.Length + 1;
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            string? key = null;
            Group? valueGroup = null;
            var kv = KeyValue.Match(line);
            if (kv.Success)
            {
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
            if (vText.StartsWith('[') || vText.StartsWith('{') || vText.StartsWith('|') || vText.StartsWith('>') ||
                vText.StartsWith('&') || vText.StartsWith('*') || vText.StartsWith('!')) continue;

            string value;
            int length;
            if (vText[0] is '"' or '\'')
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
                if (!closed) continue;
                value = sb.ToString();
                length = j;
            }
            else
            {
                // valeur simple : jusqu'a un commentaire " #"
                var hash = vText.IndexOf(" #", StringComparison.Ordinal);
                var plain = (hash >= 0 ? vText[..hash] : vText).TrimEnd();
                value = plain;
                length = plain.Length;
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

            list.Add(new StringLiteral
            {
                Index = list.Count,
                Start = lineStart + vStart,
                Length = length,
                Value = value,
                Line = lineNo,
                LineText = line.Trim().Length > 140 ? line.Trim()[..137] + "..." : line.Trim(),
                Kind = kind,
                Encode = v => "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"",
            });
        }
        return list;
    }
}

// ---------------------------------------------------------------------------------------------
// JSON
// ---------------------------------------------------------------------------------------------

internal static class JsonScanner
{
    public static List<StringLiteral> Scan(string s)
    {
        var list = new List<StringLiteral>();
        string? lastKey = null;
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] != '"')
            {
                i++;
                continue;
            }
            var (value, end) = Read(s, i);
            var j = end;
            while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
            var isKey = j < s.Length && s[j] == ':';
            if (isKey) lastKey = value;
            else
            {
                var (line, text) = PlainTextFile.LineAt(s, i);
                list.Add(new StringLiteral
                {
                    Index = list.Count,
                    Start = i,
                    Length = end - i,
                    Value = value,
                    Line = line,
                    LineText = text,
                    Kind = TextClassifier.ApplyKeyHint(TextClassifier.Classify(value), lastKey),
                    Encode = Encode,
                });
            }
            i = end;
        }
        return list;
    }

    private static string Encode(string v)
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

    private static (string Value, int End) Read(string s, int start)
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
}

// ---------------------------------------------------------------------------------------------
// Regles qui dependent du fichier entier
// ---------------------------------------------------------------------------------------------

internal static class FileRules
{
    /// <summary>Bibliotheques CET tres repandues (psiberx, rxi...) : aucun texte affiche dedans.</summary>
    private static readonly HashSet<string> LuaLibraries = new(StringComparer.OrdinalIgnoreCase)
    {
        "GameUI.lua", "GameSettings.lua", "GameSession.lua", "GameHUD.lua", "GameLocale.lua", "Cron.lua", "Ref.lua",
        "EventProxy.lua", "json.lua", "dkjson.lua", "inspect.lua", "middleclass.lua", "lume.lua", "TweakDB.lua",
    };

    private static readonly string[] OtherLanguages =
    {
        "german", "deutsch", "russian", "french", "francais", "spanish", "espanol", "mexican", "italian", "polish",
        "portuguese", "brazilian", "japanese", "korean", "chinese", "arabic", "czech", "hungarian", "thai", "turkish",
        "ukrainian",
    };

    private static readonly Regex LangCode = new(@"^(?<l>[a-z]{2})([-_][a-z]{2})?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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

        // 2. Traductions deja fournies dans une autre langue (German.reds, de-de.json...) : on n'y touche pas
        var package = ext == ".reds" ? LocPackage.Match(content) : Match.Empty;
        var langName = package.Success ? package.Groups["c"].Value : stem;
        if ((package.Success || ext == ".json") && IsOtherLanguage(langName))
        {
            SetAll(literals, StringKind.Code);
            return;
        }

        // 3. Fichier de traduction anglais (localization.json, en-us.json) : les valeurs sont du texte
        if (ext == ".json" && (IsEnglish(stem) || stem.Contains("locali", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var l in literals)
                if (l.Kind == StringKind.Maybe) l.Kind = StringKind.Text;
        }

        // 4. Un mot repete dix fois ou plus dans le meme fichier : une valeur de base de donnees ("Ped", "Ped"...)
        foreach (var group in literals.Where(l => l.Kind == StringKind.Maybe && !l.Value.Contains(' ')).GroupBy(l => l.Value))
        {
            if (literals.Count(l => l.Value == group.Key) < 10) continue;
            foreach (var l in group) l.Kind = StringKind.Code;
        }
    }

    private static void SetAll(List<StringLiteral> literals, StringKind kind)
    {
        foreach (var l in literals) l.Kind = kind;
    }

    private static bool IsEnglish(string name)
    {
        var n = name.ToLowerInvariant();
        return n is "english" or "en" or "en-us" or "en_us" or "en-gb" or "enus";
    }

    private static bool IsOtherLanguage(string name)
    {
        var n = name.ToLowerInvariant();
        if (OtherLanguages.Any(n.Contains)) return true;
        var m = LangCode.Match(n);
        return m.Success && m.Groups["l"].Value != "en" && n is not ("id" or "ui" or "db" or "ok");
    }
}

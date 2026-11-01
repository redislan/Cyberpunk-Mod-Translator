using System.Text;
using System.Text.RegularExpressions;

namespace CyberpunkModTranslator.Core;

/// <summary>Tri texte / code des chaines trouvees dans les fichiers en clair.</summary>
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

    /// <summary>Noms de fonctions de debug sans "log" dedans (dbg, dprint, this.Fail...).</summary>
    private static readonly HashSet<string> DebugCalls = new(StringComparer.Ordinal)
    {
        "print", "error", "assert", "spdlog", "FTLog", "FTLogWarning", "FTLogError", "Print", "Assert", "LogChannel",
        "dbg", "dprint", "debugPrint", "DebugPrint", "printDebug", "PrintDebug", "trace", "Trace", "warn", "Fail",
    };

    /// <summary>Meme test que <see cref="IsLogCall"/> a partir du nom complet de l'appel ("self.log:info", "ModLog"...).</summary>
    internal static bool IsLogCallName(string callName)
    {
        var parts = callName.Split('.', ':');
        var name = parts[^1];
        var receiver = parts.Length > 1 ? callName[..(callName.Length - name.Length - 1)] : "";
        return IsLogCall(name, receiver, "lua");
    }

    /// <summary>
    /// Appel de journal / debug : print, spdlog, Log, LogRetreat, Logger.error, RedLog.Append, self.log:info,
    /// ModLog (redscript), et les fonctions maison tlog, wind_log, DebugLog...
    /// "Dialog" et "Catalog" ne sont pas des journaux : il faut un "Log" majuscule, "_log" ou un prefixe tres court.
    /// </summary>
    private static bool IsLogCall(string name, string receiver, string language)
    {
        if (DebugCalls.Contains(name)) return true;
        if (Regex.IsMatch(name, @"^(Log|log)([A-Z_]|$)")) return true;
        // addLog / pushLog / showLog remplissent souvent un journal affiche dans la fenetre du mod : du texte
        if (Regex.IsMatch(name, @"[A-Za-z0-9]Log$|_log$|^[a-z]{1,2}log$") &&
            !Regex.IsMatch(name, @"^(add|push|append|insert|show|display|set|ui|UI|Add|Push|Append|Insert|Show|Display|Set)[A-Z_]?")) return true;
        if (Regex.IsMatch(receiver, @"(^|[.:])(spdlog|log|logger|Logger|Log|Logging)$") || Regex.IsMatch(receiver, @"[a-z]Log$|[Ll]ogger$")) return true;
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

        // Ecriture dans un fichier (f:write, io.write, RedFileSystem WriteText) : souvent lu par une DLL ou un autre script
        if (language == "lua" && name == "write") return StringKind.Code;
        if (name is "WriteText" or "WriteLines" or "WriteJson") return StringKind.Code;

        // Enum.new('gamedataStatType', 'Health') : nom de type et valeur d'enumeration du jeu
        if (name == "new" && receiver == "Enum") return StringKind.Code;
        if (name is "EnumValueFromName" or "EnumValueFromString") return StringKind.Code;

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
